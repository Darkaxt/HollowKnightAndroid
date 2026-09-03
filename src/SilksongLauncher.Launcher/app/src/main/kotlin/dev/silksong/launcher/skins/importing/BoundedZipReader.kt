package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.ZipArchive
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.openSeekableNoFollow
import java.io.EOFException
import java.io.File
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.util.zip.CRC32
import java.util.zip.DataFormatException
import java.util.zip.Inflater

class BoundedZipReader(
    private val limits: SkinLimits = SkinLimits.V1,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
) {
    fun read(file: File): SkinResult<ZipArchive> = try {
        val before = fileSystem.identity(file)
        if (!before.regularFile || before.size > limits.quarantineBytes) {
            fail(SkinImportCode.LIMIT_EXCEEDED, "ZIP is not a bounded regular file")
        }
        fileSystem.openSeekableNoFollow(file).use { archive ->
            if (archive.size() != before.size || fileSystem.identity(file) != before) {
                corrupt("ZIP identity changed before parsing")
            }
            val parsed = parse(archive)
            if (archive.size() != before.size || fileSystem.identity(file) != before) {
                corrupt("ZIP identity changed while parsing")
            }
            SkinResult.Ok(ZipArchive(file, parsed.entries, parsed.ignoredExtraMetadata))
        }
    } catch (error: ZipFailure) {
        SkinResult.Error(error.code, error.message ?: error.code.name)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "ZIP parse failed: ${error.message}")
    }

    private fun parse(archive: SeekableByteChannel): ParsedZip {
        val length = archive.size()
        if (length < EOCD_FIXED) corrupt("ZIP is too short")
        val eocd = findEocd(archive, length)
        archive.position(eocd)
        requireSignature(archive, EOCD_SIGNATURE)
        val disk = archive.u16()
        val centralDisk = archive.u16()
        val diskEntries = archive.u16()
        val totalEntries = archive.u16()
        val centralSize = archive.u32()
        val centralOffset = archive.u32()
        val commentLength = archive.u16()
        if (disk != 0 || centralDisk != 0 || diskEntries != totalEntries) unsupported("Multi-disk ZIP is unsupported")
        if (totalEntries == 0xffff || centralSize == UINT32_MAX || centralOffset == UINT32_MAX) unsupported("ZIP64 is unsupported")
        if (totalEntries > limits.entries) limit("ZIP has too many entries")
        if (eocd + EOCD_FIXED + commentLength != length) corrupt("EOCD does not terminate the archive")
        if (centralOffset > eocd || centralSize > eocd - centralOffset || centralOffset + centralSize != eocd) {
            corrupt("Central directory range is invalid")
        }
        if (eocd >= 20) {
            archive.position(eocd - 20)
            if (archive.u32() == ZIP64_LOCATOR_SIGNATURE) unsupported("ZIP64 locator is unsupported")
        }

        val records = ArrayList<CentralRecord>(totalEntries)
        archive.position(centralOffset)
        repeat(totalEntries) { index -> records += readCentral(archive, index) }
        if (archive.position() != centralOffset + centralSize) corrupt("Central directory count or size mismatch")

        var declaredTotal = 0L
        var compressedTotal = 0L
        for (record in records) {
            declaredTotal = checkedAdd(declaredTotal, record.uncompressedSize)
            compressedTotal = checkedAdd(compressedTotal, record.compressedSize)
            if (declaredTotal > limits.uncompressedBytes) limit("Declared ZIP output exceeds bound")
            enforceRatio(record.uncompressedSize, record.compressedSize, "entry")
        }
        enforceRatio(declaredTotal, compressedTotal, "archive")

        val parsed = records.map { readLocal(archive, it, centralOffset) }
        val sorted = parsed.sortedBy { it.localOffset }
        var expected = 0L
        for (entry in sorted) {
            if (entry.localOffset != expected) corrupt("Local entries have gaps, prefixes, or overlap")
            expected = checkedAdd(entry.dataEnd, entry.descriptorLength.toLong())
            if (expected > centralOffset) corrupt("Local entry overlaps central directory")
        }
        if (expected != centralOffset) corrupt("Unaccounted data precedes central directory")

        var streamedTotal = 0L
        var streamedCompressedTotal = 0L
        for (entry in parsed) {
            val actual = verifyPayload(archive, entry)
            streamedTotal = checkedAdd(streamedTotal, actual)
            streamedCompressedTotal = checkedAdd(streamedCompressedTotal, entry.compressedSize)
            if (streamedTotal > limits.uncompressedBytes) limit("Streamed ZIP output exceeds bound")
            enforceRatio(actual, entry.compressedSize, "streamed entry")
        }
        enforceRatio(streamedTotal, streamedCompressedTotal, "streamed archive")
        return ParsedZip(parsed, commentLength > 0)
    }

    private fun findEocd(archive: SeekableByteChannel, length: Long): Long {
        val searchLength = minOf(length, EOCD_FIXED + 0xffffL).toInt()
        val start = length - searchLength
        val bytes = ByteArray(searchLength)
        archive.position(start)
        archive.readFully(bytes)
        var found = -1
        for (index in 0..bytes.size - EOCD_FIXED) {
            if (le32(bytes, index) == EOCD_SIGNATURE &&
                index + EOCD_FIXED + le16(bytes, index + 20) == bytes.size
            ) {
                if (found >= 0) corrupt("Ambiguous EOCD")
                found = index
            }
        }
        if (found < 0) corrupt("EOCD is missing")
        return start + found
    }

    private fun readCentral(archive: SeekableByteChannel, index: Int): CentralRecord {
        requireSignature(archive, CENTRAL_SIGNATURE)
        val madeBy = archive.u16()
        val needed = archive.u16()
        val flags = archive.u16()
        val method = archive.u16()
        archive.u16()
        archive.u16()
        val crc = archive.u32()
        val compressed = archive.u32()
        val uncompressed = archive.u32()
        val nameLength = archive.u16()
        val extraLength = archive.u16()
        val commentLength = archive.u16()
        val diskStart = archive.u16()
        archive.u16()
        val externalAttributes = archive.u32()
        val localOffset = archive.u32()
        if (needed >= 45 || compressed == UINT32_MAX || uncompressed == UINT32_MAX || localOffset == UINT32_MAX) {
            unsupported("ZIP64 is unsupported")
        }
        if (diskStart != 0) unsupported("Multi-disk ZIP entry is unsupported")
        validateFlags(flags, method)
        if (nameLength !in 1..limits.sourcePathBytes) path("ZIP entry name length is invalid")
        val rawName = archive.bytes(nameLength)
        val extras = archive.bytes(extraLength)
        val ignoredExtra = parseExtras(extras)
        if (commentLength > limits.sourcePathBytes) limit("ZIP entry comment is too long")
        if (commentLength > 0) archive.skipExact(commentLength.toLong())
        val directory = rawName.last() == '/'.code.toByte()
        validateExternalType(madeBy, externalAttributes, directory)
        if (directory && (crc != 0L || compressed != 0L || uncompressed != 0L)) corrupt("Directory entry has payload")
        return CentralRecord(
            index, rawName, flags, method, crc, compressed, uncompressed, localOffset,
            directory, ignoredExtra || commentLength > 0,
        )
    }

    private fun readLocal(archive: SeekableByteChannel, central: CentralRecord, centralOffset: Long): RawZipEntry {
        if (central.localOffset >= centralOffset) corrupt("Local offset points into central directory")
        archive.position(central.localOffset)
        requireSignature(archive, LOCAL_SIGNATURE)
        val needed = archive.u16()
        val flags = archive.u16()
        val method = archive.u16()
        archive.u16()
        archive.u16()
        val localCrc = archive.u32()
        val localCompressed = archive.u32()
        val localUncompressed = archive.u32()
        val nameLength = archive.u16()
        val extraLength = archive.u16()
        if (needed >= 45) unsupported("ZIP64 is unsupported")
        if (nameLength !in 1..limits.sourcePathBytes) path("Local ZIP entry name length is invalid")
        if (flags != central.flags || method != central.method) corrupt("Local and central flags or methods differ")
        val localName = archive.bytes(nameLength)
        if (!localName.contentEquals(central.rawName)) corrupt("Local and central filename bytes differ")
        val ignoredLocalExtra = parseExtras(archive.bytes(extraLength))
        val descriptor = flags and DATA_DESCRIPTOR != 0
        if (!descriptor) {
            if (localCrc != central.crc32 || localCompressed != central.compressedSize || localUncompressed != central.uncompressedSize) {
                corrupt("Local and central size tuple differs")
            }
        } else {
            val allZero = localCrc == 0L && localCompressed == 0L && localUncompressed == 0L
            val allFull = localCrc == central.crc32 && localCompressed == central.compressedSize &&
                localUncompressed == central.uncompressedSize
            if (!allZero && !allFull) corrupt("Descriptor local tuple must be all-zero or fully equal")
            if (localCrc == UINT32_MAX || localCompressed == UINT32_MAX || localUncompressed == UINT32_MAX) {
                unsupported("ZIP64 local tuple is unsupported")
            }
        }
        val dataOffset = archive.position()
        if (central.compressedSize > centralOffset - dataOffset) corrupt("Compressed payload range is invalid")
        val dataEnd = dataOffset + central.compressedSize
        var descriptorLength = 0
        if (descriptor) {
            val available = minOf(16L, centralOffset - dataEnd).toInt()
            archive.position(dataEnd)
            descriptorLength = when (
                val classified = classifyDescriptor(
                    archive.bytes(available),
                    central.crc32,
                    central.compressedSize,
                    central.uncompressedSize,
                )
            ) {
                is SkinResult.Ok -> classified.value
                is SkinResult.Error -> corrupt(classified.detail)
            }
        }
        return RawZipEntry(
            central.centralIndex,
            central.rawName,
            central.flags,
            central.method,
            central.crc32,
            central.compressedSize,
            central.uncompressedSize,
            central.localOffset,
            dataOffset,
            dataEnd,
            central.directory,
            descriptorLength,
            central.ignoredExtraMetadata || ignoredLocalExtra,
        )
    }

    private fun verifyPayload(archive: SeekableByteChannel, entry: RawZipEntry): Long {
        if (entry.directory) return 0
        val crc = CRC32()
        var outputCount = 0L
        when (entry.method) {
            0 -> {
                if (entry.compressedSize != entry.uncompressedSize) corrupt("Stored entry sizes differ")
                val input = SliceInputStream(archive, entry.dataOffset, entry.compressedSize)
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    crc.update(buffer, 0, count)
                    outputCount = charged(outputCount, count)
                }
            }
            8 -> {
                val inflater = Inflater(true)
                try {
                    archive.position(entry.dataOffset)
                    var compressedRemaining = entry.compressedSize
                    val compressed = ByteArray(64 * 1024)
                    val output = ByteArray(64 * 1024)
                    while (!inflater.finished()) {
                        if (inflater.needsInput()) {
                            if (compressedRemaining == 0L) corrupt("Deflate stream is truncated")
                            val count = minOf(compressed.size.toLong(), compressedRemaining).toInt()
                            archive.readFully(compressed, 0, count)
                            inflater.setInput(compressed, 0, count)
                            compressedRemaining -= count
                        }
                        val count = try {
                            inflater.inflate(output)
                        } catch (_: DataFormatException) {
                            corrupt("Deflate stream is invalid")
                        }
                        if (count > 0) {
                            crc.update(output, 0, count)
                            outputCount = charged(outputCount, count)
                        } else if (inflater.needsDictionary() || (!inflater.needsInput() && !inflater.finished())) {
                            corrupt("Deflate stream cannot make progress")
                        }
                    }
                    if (inflater.bytesRead != entry.compressedSize) corrupt("Deflate stream has trailing compressed bytes")
                } finally {
                    inflater.end()
                }
            }
            else -> unsupported("ZIP compression method ${entry.method} is unsupported")
        }
        if (outputCount != entry.uncompressedSize || crc.value != entry.crc32) {
            corrupt("Streamed CRC or size differs from central declaration")
        }
        return outputCount
    }

    private fun charged(current: Long, count: Int): Long {
        val next = checkedAdd(current, count.toLong())
        if (next > limits.uncompressedBytes) limit("Streamed entry exceeds output bound")
        return next
    }

    private fun validateFlags(flags: Int, method: Int) {
        if (flags and 0x0001 != 0 || flags and 0x0040 != 0) unsupported("Encrypted ZIP entries are unsupported")
        if (method !in setOf(0, 8)) unsupported("ZIP compression method $method is unsupported")
        val allowed = DATA_DESCRIPTOR or UTF8_FLAG or if (method == 8) 0x0006 else 0
        if (flags and allowed.inv() != 0) unsupported("Unsupported ZIP flags")
    }

    private fun validateExternalType(madeBy: Int, attributes: Long, directory: Boolean) {
        if ((madeBy ushr 8) != 3) return
        val mode = ((attributes ushr 16) and 0xffff).toInt()
        val type = mode and 0xf000
        if (type != 0 && type != 0x8000 && type != 0x4000) path("Linked or special ZIP entry is forbidden")
        if (directory && type == 0x8000 || !directory && type == 0x4000) path("ZIP entry type disagrees with path")
    }

    private fun parseExtras(bytes: ByteArray): Boolean {
        var offset = 0
        var ignored = false
        while (offset < bytes.size) {
            if (bytes.size - offset < 4) corrupt("Truncated ZIP extra field")
            val id = le16(bytes, offset)
            val length = le16(bytes, offset + 2)
            offset += 4
            if (length > bytes.size - offset) corrupt("ZIP extra field exceeds record")
            when (id) {
                0x0001 -> unsupported("ZIP64 extra field is unsupported")
                0x7075 -> path("Unicode Path extra field is forbidden")
                else -> ignored = true
            }
            offset += length
        }
        return ignored
    }

    private fun enforceRatio(uncompressed: Long, compressed: Long, scope: String) {
        if (uncompressed == 0L) return
        if (compressed == 0L) limit("$scope expansion ratio is unbounded")
        val quotient = uncompressed / compressed
        val remainder = uncompressed % compressed
        val maximum = limits.expansionRatio.toLong()
        if (quotient > maximum || quotient == maximum && remainder > 0) {
            limit("$scope expansion ratio exceeds ${limits.expansionRatio}:1")
        }
    }

    private fun checkedAdd(left: Long, right: Long): Long {
        if (left < 0 || right < 0 || left > Long.MAX_VALUE - right) limit("ZIP size arithmetic overflow")
        return left + right
    }

    private fun requireSignature(archive: SeekableByteChannel, expected: Long) {
        if (archive.u32() != expected) corrupt("ZIP signature mismatch")
    }

    private fun SeekableByteChannel.read(): Int {
        val byte = ByteBuffer.allocate(1)
        val count = read(byte)
        return if (count < 0) -1 else byte.array()[0].toInt() and 0xff
    }

    private fun SeekableByteChannel.readFully(bytes: ByteArray, offset: Int = 0, count: Int = bytes.size) {
        val target = ByteBuffer.wrap(bytes, offset, count)
        while (target.hasRemaining()) {
            if (read(target) < 0) throw EOFException()
        }
    }

    private fun SeekableByteChannel.u16(): Int {
        val low = read()
        val high = read()
        if (low < 0 || high < 0) throw EOFException()
        return low or (high shl 8)
    }

    private fun SeekableByteChannel.u32(): Long = u16().toLong() or (u16().toLong() shl 16)

    private fun SeekableByteChannel.bytes(count: Int): ByteArray = ByteArray(count).also { readFully(it) }

    private fun SeekableByteChannel.skipExact(count: Long) {
        if (count < 0 || position() > size() - count) throw EOFException()
        position(position() + count)
    }

    private inner class SliceInputStream(
        private val archive: SeekableByteChannel,
        offset: Long,
        private var remaining: Long,
    ) : InputStream() {
        init { archive.position(offset) }
        override fun read(): Int {
            if (remaining == 0L) return -1
            remaining--
            return archive.read().also { if (it < 0) throw EOFException() }
        }
        override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
            if (remaining == 0L) return -1
            val count = minOf(length.toLong(), remaining).toInt()
            archive.readFully(buffer, offset, count)
            remaining -= count
            return count
        }
    }

    private data class ParsedZip(
        val entries: List<RawZipEntry>,
        val ignoredExtraMetadata: Boolean,
    )

    private data class CentralRecord(
        val centralIndex: Int,
        val rawName: ByteArray,
        val flags: Int,
        val method: Int,
        val crc32: Long,
        val compressedSize: Long,
        val uncompressedSize: Long,
        val localOffset: Long,
        val directory: Boolean,
        val ignoredExtraMetadata: Boolean,
    )

    private class ZipFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun fail(code: SkinImportCode, detail: String): Nothing = throw ZipFailure(code, detail)
    private fun corrupt(detail: String): Nothing = fail(SkinImportCode.ZIP_CORRUPT, detail)
    private fun unsupported(detail: String): Nothing = fail(SkinImportCode.UNSUPPORTED_ZIP, detail)
    private fun path(detail: String): Nothing = fail(SkinImportCode.PATH_REJECTED, detail)
    private fun limit(detail: String): Nothing = fail(SkinImportCode.LIMIT_EXCEEDED, detail)

    companion object {
        fun read(file: File, limits: SkinLimits = SkinLimits.V1): SkinResult<ZipArchive> =
            BoundedZipReader(limits).read(file)

        private const val LOCAL_SIGNATURE = 0x04034b50L
        const val CENTRAL_SIGNATURE = 0x02014b50L
        const val EOCD_SIGNATURE = 0x06054b50L
        const val ZIP64_LOCATOR_SIGNATURE = 0x07064b50L
        const val DESCRIPTOR_SIGNATURE = 0x08074b50L
        const val EOCD_FIXED = 22
        const val DATA_DESCRIPTOR = 0x0008
        const val UTF8_FLAG = 0x0800
        const val UINT32_MAX = 0xffffffffL

        internal fun classifyDescriptor(
            bytes: ByteArray,
            crc32: Long,
            compressedSize: Long,
            uncompressedSize: Long,
        ): SkinResult<Int> {
            fun tupleMatches(offset: Int): Boolean = bytes.size >= offset + 12 &&
                le32(bytes, offset) == crc32 &&
                le32(bytes, offset + 4) == compressedSize &&
                le32(bytes, offset + 8) == uncompressedSize

            val unsigned = tupleMatches(0)
            val signed = bytes.size >= 16 && le32(bytes, 0) == DESCRIPTOR_SIGNATURE && tupleMatches(4)
            return if (unsigned == signed) {
                SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "Descriptor is absent or ambiguous")
            } else {
                SkinResult.Ok(if (signed) 16 else 12)
            }
        }

        fun le16(bytes: ByteArray, offset: Int): Int =
            (bytes[offset].toInt() and 0xff) or ((bytes[offset + 1].toInt() and 0xff) shl 8)
        fun le32(bytes: ByteArray, offset: Int): Long =
            le16(bytes, offset).toLong() or (le16(bytes, offset + 2).toLong() shl 16)
    }
}
