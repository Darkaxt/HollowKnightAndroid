package dev.silksong.launcher.skins.importing

import com.github.junrar.Archive
import com.github.junrar.ArchiveOptions
import com.github.junrar.exception.MissingNextVolumeException
import com.github.junrar.exception.MissingPreviousVolumeException
import com.github.junrar.exception.UnsupportedDictionarySizeException
import com.github.junrar.exception.UnsupportedRarEncryptedException
import com.github.junrar.exception.WrongPasswordException
import com.github.junrar.rarfile.FileHeader
import com.github.junrar.rarfile.HostSystem
import com.github.junrar.rarfile.rar5.Rar5HostOS
import com.github.junrar.rarfile.rar5.Rar5RedirType
import com.github.junrar.volume.Volume
import com.github.junrar.volume.VolumeManager
import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinArchiveFormat
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.ZipArchive
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.openSeekableNoFollow
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.security.MessageDigest
import java.util.zip.CRC32
import org.apache.commons.compress.MemoryLimitException
import org.apache.commons.compress.PasswordRequiredException
import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry
import org.apache.commons.compress.archivers.sevenz.SevenZFile

internal data class PortableExtraction(val length: Long, val sha256: String)

internal class PortableArchiveReader(
    private val limits: SkinLimits = SkinLimits.V1,
    private val fs: SkinFileSystem = AndroidSkinFileSystem(),
) {
    fun read(file: File, format: SkinArchiveFormat): SkinResult<ZipArchive> = try {
        if (format == SkinArchiveFormat.ZIP) {
            fail(SkinImportCode.INVALID_INPUT, "Portable reader does not replace bounded ZIP parsing")
        }
        val before = fs.identity(file)
        if (!before.regularFile || before.size > limits.quarantineBytes) {
            fail(SkinImportCode.LIMIT_EXCEEDED, "Archive is not a bounded regular file")
        }
        requireSignature(file, format)
        val entries = when (format) {
            SkinArchiveFormat.SEVEN_Z -> readSevenZip(file, before.size)
            SkinArchiveFormat.RAR -> readRar(file, before.size)
            SkinArchiveFormat.ZIP -> error("checked above")
        }
        if (fs.identity(file) != before) corrupt("Archive identity changed while parsing")
        SkinResult.Ok(ZipArchive(file, entries, format = format))
    } catch (error: ArchiveFailure) {
        SkinResult.Error(error.code, error.message ?: error.code.name)
    } catch (error: PasswordRequiredException) {
        SkinResult.Error(SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
    } catch (error: MemoryLimitException) {
        SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Archive decoder memory exceeds bound")
    } catch (error: UnsupportedDictionarySizeException) {
        SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Archive decoder memory exceeds bound")
    } catch (error: UnsupportedRarEncryptedException) {
        SkinResult.Error(SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
    } catch (error: WrongPasswordException) {
        SkinResult.Error(SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
    } catch (error: MissingNextVolumeException) {
        SkinResult.Error(SkinImportCode.INVALID_INPUT, "Multi-volume archives are unsupported")
    } catch (error: MissingPreviousVolumeException) {
        SkinResult.Error(SkinImportCode.INVALID_INPUT, "Multi-volume archives are unsupported")
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "Archive parse failed: ${error.message}")
    }

    fun extract(
        archive: ZipArchive,
        destinations: Map<Int, File>,
        owner: File,
    ): SkinResult<Map<Int, PortableExtraction>> {
        val created = mutableListOf<File>()
        return try {
            if (archive.format == SkinArchiveFormat.ZIP) {
                fail(SkinImportCode.INVALID_INPUT, "Portable extraction requires 7z or RAR")
            }
            if (destinations.isEmpty()) return SkinResult.Ok(emptyMap())
            val before = fs.identity(archive.file)
            if (!before.regularFile || before.size > limits.quarantineBytes) {
                corrupt("Archive identity is invalid before extraction")
            }
            requireSignature(archive.file, archive.format)
            val selected = selectedEntries(archive, destinations)
            destinations.values.forEach { fs.requireContained(it, owner, allowMissingLeaf = true) }
            val output = when (archive.format) {
                SkinArchiveFormat.SEVEN_Z -> extractSevenZip(
                    archive.file,
                    archive.entries.size,
                    selected,
                    destinations,
                    created,
                )
                SkinArchiveFormat.RAR -> extractRar(
                    archive.file,
                    archive.entries.size,
                    selected,
                    destinations,
                    created,
                )
                SkinArchiveFormat.ZIP -> error("checked above")
            }
            if (fs.identity(archive.file) != before) corrupt("Archive identity changed during extraction")
            destinations.values.forEach { destination ->
                fs.requireContained(destination, owner)
                fs.syncFile(destination)
            }
            SkinResult.Ok(output)
        } catch (error: ArchiveFailure) {
            extractionError(created, owner, error.code, error.message ?: error.code.name)
        } catch (error: PasswordRequiredException) {
            extractionError(created, owner, SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
        } catch (error: MemoryLimitException) {
            extractionError(created, owner, SkinImportCode.LIMIT_EXCEEDED, "Archive decoder memory exceeds bound")
        } catch (error: UnsupportedDictionarySizeException) {
            extractionError(created, owner, SkinImportCode.LIMIT_EXCEEDED, "Archive decoder memory exceeds bound")
        } catch (error: UnsupportedRarEncryptedException) {
            extractionError(created, owner, SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
        } catch (error: WrongPasswordException) {
            extractionError(created, owner, SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
        } catch (error: MissingNextVolumeException) {
            extractionError(created, owner, SkinImportCode.INVALID_INPUT, "Multi-volume archives are unsupported")
        } catch (error: MissingPreviousVolumeException) {
            extractionError(created, owner, SkinImportCode.INVALID_INPUT, "Multi-volume archives are unsupported")
        } catch (error: Exception) {
            extractionError(created, owner, SkinImportCode.ZIP_CORRUPT, "Archive extraction failed: ${error.message}")
        }
    }

    private fun readSevenZip(file: File, archiveBytes: Long): List<RawZipEntry> =
        withSevenZip(file) { archive ->
            val nativeEntries = archive.entries.toList()
            val entries = nativeEntries.mapIndexed { index, entry -> sevenZipEntry(index, entry) }
            enforceDeclaredBounds(entries, archiveBytes)
            verifySevenZip(archive, nativeEntries, entries)
            entries
        }

    private fun sevenZipEntry(index: Int, entry: SevenZArchiveEntry): RawZipEntry {
        if (entry.isAntiItem) fail(SkinImportCode.INVALID_INPUT, "Anti-items are unsupported")
        if (sevenZipEntryIsLink(entry)) {
            fail(SkinImportCode.INVALID_INPUT, "Link and special-file entries are unsupported")
        }
        val path = normalizedPath(entry.name, entry.isDirectory)
        val unpacked = entry.size
        if (unpacked < 0L || entry.isDirectory && (unpacked != 0L || entry.hasStream())) {
            corrupt("Archive entry sizes are invalid")
        }
        return rawEntry(
            index = index,
            path = path,
            packed = 0L,
            unpacked = unpacked,
            crc = if (entry.hasCrc) entry.crcValue and UINT_MASK else null,
            directory = entry.isDirectory,
        )
    }

    private fun verifySevenZip(
        archive: SevenZFile,
        nativeEntries: List<SevenZArchiveEntry>,
        entries: List<RawZipEntry>,
    ) {
        val total = LongCounter()
        nativeEntries.zip(entries).forEach { (native, entry) ->
            if (entry.directory) return@forEach
            val sink = MeasuredOutput(entry, total)
            archive.getInputStream(native).use { input -> copy(input, sink) }
            sink.requireComplete()
        }
    }

    private fun readRar(file: File, archiveBytes: Long): List<RawZipEntry> =
        withRar(file) { archive ->
            if (archive.isEncrypted || archive.isPasswordProtected) {
                fail(SkinImportCode.INVALID_INPUT, "Encrypted archives are unsupported")
            }
            if (archive.mainHeader?.isMultiVolume == true) {
                fail(SkinImportCode.INVALID_INPUT, "Multi-volume archives are unsupported")
            }
            if (archive.hasBrokenHeaders()) {
                corrupt("RAR headers are corrupt: ${archive.headerFailures.joinToString()}")
            }
            val headers = archive.fileHeaders
            val entries = headers.mapIndexed { index, header -> rarEntry(index, header) }
            enforceDeclaredBounds(entries, archiveBytes)
            verifyRar(archive, headers, entries)
            entries
        }

    private fun rarEntry(index: Int, header: FileHeader): RawZipEntry {
        if (header.isEncrypted) fail(SkinImportCode.INVALID_INPUT, "Encrypted archive entries are unsupported")
        if (header.isSplitBefore || header.isSplitAfter) {
            fail(SkinImportCode.INVALID_INPUT, "Split archive entries are unsupported")
        }
        if (rarEntryIsLink(header)) {
            fail(SkinImportCode.INVALID_INPUT, "Link and special-file entries are unsupported")
        }
        if (header.isUnpSizeUnknown) corrupt("Archive entry size is unavailable")
        val path = normalizedPath(header.fileName, header.isDirectory)
        val unpacked = header.fullUnpackSize
        val packed = header.fullPackSize
        if (unpacked < 0L || packed < 0L || header.isDirectory && (unpacked != 0L || packed != 0L)) {
            corrupt("Archive entry sizes are invalid")
        }
        return rawEntry(
            index = index,
            path = path,
            packed = packed,
            unpacked = unpacked,
            crc = if (header.hasFileCrc()) header.fileCRC.toLong() and UINT_MASK else null,
            directory = header.isDirectory,
        )
    }

    private fun verifyRar(archive: Archive, headers: List<FileHeader>, entries: List<RawZipEntry>) {
        val total = LongCounter()
        headers.zip(entries).forEach { (header, entry) ->
            if (entry.directory) return@forEach
            val sink = MeasuredOutput(entry, total)
            archive.extractFile(header, sink)
            sink.requireComplete()
        }
    }

    private fun extractSevenZip(
        file: File,
        expectedEntryCount: Int,
        selected: List<RawZipEntry>,
        destinations: Map<Int, File>,
        created: MutableList<File>,
    ): Map<Int, PortableExtraction> = withSevenZip(file) { archive ->
        val nativeEntries = archive.entries.toList()
        if (nativeEntries.size != expectedEntryCount) {
            corrupt("Archive entry table changed before extraction")
        }
        val total = LongCounter()
        selected.associate { entry ->
            val native = nativeEntries.getOrNull(entry.centralIndex)
                ?: corrupt("Selected archive entry is missing")
            requireSameEntry(entry, sevenZipEntry(entry.centralIndex, native))
            val destination = destinations.getValue(entry.centralIndex)
            val state = createOutput(entry, destination, total, created)
            try {
                archive.getInputStream(native).use { input -> copy(input, state) }
                state.requireComplete()
            } finally {
                state.close()
            }
            entry.centralIndex to state.extraction()
        }
    }

    private fun extractRar(
        file: File,
        expectedEntryCount: Int,
        selected: List<RawZipEntry>,
        destinations: Map<Int, File>,
        created: MutableList<File>,
    ): Map<Int, PortableExtraction> = withRar(file) { archive ->
        if (archive.isEncrypted || archive.isPasswordProtected || archive.mainHeader?.isMultiVolume == true) {
            fail(SkinImportCode.INVALID_INPUT, "Encrypted and multi-volume archives are unsupported")
        }
        if (archive.hasBrokenHeaders()) corrupt("RAR headers changed before extraction")
        val headers = archive.fileHeaders
        if (headers.size != expectedEntryCount) {
            corrupt("Archive entry table changed before extraction")
        }
        val total = LongCounter()
        selected.associate { entry ->
            val header = headers.getOrNull(entry.centralIndex)
                ?: corrupt("Selected archive entry is missing")
            if (header.isEncrypted || header.isSplitBefore || header.isSplitAfter || rarEntryIsLink(header)) {
                fail(SkinImportCode.INVALID_INPUT, "Selected archive entry is no longer a regular file")
            }
            requireSameEntry(entry, rarEntry(entry.centralIndex, header))
            val destination = destinations.getValue(entry.centralIndex)
            val state = createOutput(entry, destination, total, created)
            try {
                archive.extractFile(header, state)
                state.requireComplete()
            } finally {
                state.close()
            }
            entry.centralIndex to state.extraction()
        }
    }

    private fun selectedEntries(
        archive: ZipArchive,
        destinations: Map<Int, File>,
    ): List<RawZipEntry> {
        val entries = archive.entries.associateBy { it.centralIndex }
        return destinations.keys.sorted().map { index ->
            val entry = entries[index] ?: fail(SkinImportCode.INVALID_INPUT, "Selected archive entry is missing")
            if (entry.directory) fail(SkinImportCode.INVALID_INPUT, "Selected archive entry is a directory")
            entry
        }
    }

    private fun createOutput(
        entry: RawZipEntry,
        destination: File,
        total: LongCounter,
        created: MutableList<File>,
    ): MeasuredOutput {
        val output = fs.openOutput(destination, createNew = true)
        created += destination
        return MeasuredOutput(entry, total, limits.textureBytes, output)
    }

    private fun requireSameEntry(expected: RawZipEntry, actual: RawZipEntry) {
        if (expected.centralIndex != actual.centralIndex ||
            !expected.rawName.contentEquals(actual.rawName) ||
            expected.method != actual.method ||
            expected.crc32 != actual.crc32 ||
            expected.compressedSize != actual.compressedSize ||
            expected.uncompressedSize != actual.uncompressedSize ||
            expected.directory != actual.directory
        ) {
            corrupt("Archive entry metadata changed before extraction")
        }
    }

    private fun enforceDeclaredBounds(entries: List<RawZipEntry>, archiveBytes: Long) {
        if (entries.size > limits.entries) limit("Archive has too many entries")
        var declaredTotal = 0L
        entries.forEach { entry ->
            declaredTotal = checkedAdd(declaredTotal, entry.uncompressedSize)
            if (declaredTotal > limits.uncompressedBytes) limit("Declared archive output exceeds bound")
        }
        // Solid archives share one compressed stream across many entries, so an
        // individual entry's marginal compressed byte count is not a valid ratio
        // denominator. The archive-wide declared total is bounded and compared to
        // the quarantined archive bytes instead.
        enforceRatio(declaredTotal, archiveBytes)
    }

    private fun rawEntry(
        index: Int,
        path: String,
        packed: Long,
        unpacked: Long,
        crc: Long?,
        directory: Boolean,
    ): RawZipEntry {
        val rawName = path.toByteArray(Charsets.UTF_8)
        if (rawName.isEmpty() || rawName.size > limits.sourcePathBytes) {
            fail(SkinImportCode.PATH_REJECTED, "Archive entry path length is invalid")
        }
        return RawZipEntry(
            centralIndex = index,
            rawName = rawName,
            flags = UTF8_FLAG,
            method = if (crc == null) PORTABLE_METHOD_NO_CRC else PORTABLE_METHOD,
            crc32 = crc ?: 0L,
            compressedSize = packed,
            uncompressedSize = unpacked,
            localOffset = 0L,
            dataOffset = 0L,
            dataEnd = 0L,
            directory = directory,
        )
    }

    private fun normalizedPath(source: String?, directory: Boolean): String {
        var path = source?.replace('\\', '/')
            ?: fail(SkinImportCode.PATH_REJECTED, "Archive entry path is unavailable")
        if (directory && !path.endsWith('/')) path += "/"
        return path
    }

    private fun sevenZipEntryIsLink(entry: SevenZArchiveEntry): Boolean {
        if (!entry.hasWindowsAttributes) return false
        val attributes = entry.windowsAttributes
        if (attributes and WINDOWS_REPARSE_POINT != 0) return true
        val unixType = (attributes ushr 16) and UNIX_TYPE_MASK
        if (unixType == 0) return false
        val expected = if (entry.isDirectory) UNIX_DIRECTORY else UNIX_REGULAR
        return unixType != expected
    }

    private fun rarEntryIsLink(header: FileHeader): Boolean {
        val redirection = header.redirection
        if (redirection != null && redirection.type != Rar5RedirType.NONE) return true
        val unixHost = header.hostOS == HostSystem.unix || header.rar5HostOS == Rar5HostOS.UNIX
        if (!unixHost && header.fileAttr and WINDOWS_REPARSE_POINT != 0) return true
        if (!unixHost) return false
        val unixType = header.fileAttr and UNIX_TYPE_MASK
        if (unixType == 0) return false
        val expected = if (header.isDirectory) UNIX_DIRECTORY else UNIX_REGULAR
        return unixType != expected
    }

    private fun requireSignature(file: File, expected: SkinArchiveFormat) {
        val prefix = ByteArray(RAR5_SIGNATURE.size)
        val count = fs.openSeekableNoFollow(file).use { channel ->
            var read = 0
            while (read < prefix.size) {
                val current = channel.read(ByteBuffer.wrap(prefix, read, prefix.size - read))
                if (current < 0) break
                if (current == 0) continue
                read += current
            }
            read
        }
        val actual = when {
            prefix.startsWith(SEVEN_Z_SIGNATURE, count) -> SkinArchiveFormat.SEVEN_Z
            prefix.startsWith(RAR5_SIGNATURE, count) || prefix.startsWith(RAR4_SIGNATURE, count) -> SkinArchiveFormat.RAR
            else -> null
        }
        if (actual != expected) fail(SkinImportCode.INVALID_INPUT, "Archive magic and decoded format differ")
    }

    private inline fun <T> withSevenZip(file: File, block: (SevenZFile) -> T): T {
        val channel = fs.openSeekableNoFollow(file)
        val archive = try {
            SevenZFile.builder()
                .setSeekableByteChannel(channel)
                .setDefaultName(file.name)
                .setMaxMemoryLimitKiB(decoderMemoryKiB())
                .setTryToRecoverBrokenArchives(false)
                .setUseDefaultNameForUnnamedEntries(false)
                .get()
        } catch (error: Exception) {
            runCatching { channel.close() }
            throw error
        }
        archive.use { return block(it) }
    }

    private inline fun <T> withRar(file: File, block: (Archive) -> T): T {
        val channel = fs.openSeekableNoFollow(file)
        val archive = try {
            Archive(
                SingleVolumeManager(channel, channel.size()),
                ArchiveOptions.builder().maxDictionarySize(limits.decoderMemoryBytes).build(),
            )
        } catch (error: Exception) {
            runCatching { channel.close() }
            throw error
        }
        archive.use { return block(it) }
    }

    private fun decoderMemoryKiB(): Int {
        val kib = limits.decoderMemoryBytes / 1024L
        if (kib <= 0L || kib > Int.MAX_VALUE) limit("Archive decoder memory bound is invalid")
        return kib.toInt()
    }

    private fun copy(input: InputStream, output: OutputStream) {
        val buffer = ByteArray(COPY_BUFFER_SIZE)
        while (true) {
            val count = input.read(buffer)
            if (count < 0) return
            if (count == 0) {
                val byte = input.read()
                if (byte < 0) return
                output.write(byte)
            } else {
                output.write(buffer, 0, count)
            }
        }
    }

    private fun extractionError(
        created: List<File>,
        owner: File,
        code: SkinImportCode,
        detail: String,
    ): SkinResult.Error {
        val cleanupFailure = cleanup(created, owner)
        return if (cleanupFailure == null) {
            SkinResult.Error(code, detail)
        } else {
            SkinResult.Error(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                "Archive extraction cleanup failed: ${cleanupFailure.message}",
            )
        }
    }

    private fun cleanup(created: List<File>, owner: File): Exception? {
        var failure: Exception? = null
        created.asReversed().forEach { destination ->
            try {
                fs.deleteContained(destination, owner)
            } catch (error: Exception) {
                if (failure == null) failure = error
            }
        }
        return failure
    }

    private inner class MeasuredOutput(
        private val entry: RawZipEntry,
        private val total: LongCounter,
        private val entryLimit: Long = entry.uncompressedSize,
        private val delegate: OutputStream? = null,
    ) : OutputStream() {
        private val crc = CRC32()
        private val digest = MessageDigest.getInstance("SHA-256")
        var length = 0L
            private set

        override fun write(value: Int) {
            val byte = byteArrayOf(value.toByte())
            write(byte, 0, 1)
        }

        override fun write(bytes: ByteArray, offset: Int, count: Int) {
            if (count == 0) return
            val nextLength = checkedAdd(length, count.toLong())
            val nextTotal = checkedAdd(total.value, count.toLong())
            if (nextLength > entry.uncompressedSize || nextLength > entryLimit || nextTotal > limits.uncompressedBytes) {
                limit("Archive extraction exceeds declared or configured bound")
            }
            delegate?.write(bytes, offset, count)
            crc.update(bytes, offset, count)
            digest.update(bytes, offset, count)
            length = nextLength
            total.value = nextTotal
        }

        override fun flush() {
            delegate?.flush()
        }

        override fun close() {
            delegate?.close()
        }

        fun requireComplete() {
            if (length != entry.uncompressedSize) corrupt("Extracted size differs from archive metadata")
            if (entry.method != PORTABLE_METHOD_NO_CRC && crc.value != entry.crc32) {
                corrupt("Extracted CRC differs from archive metadata")
            }
        }

        fun extraction(): PortableExtraction = PortableExtraction(length, digest.digest().toHex())
    }

    private data class LongCounter(var value: Long = 0L)

    private class SingleVolumeManager(
        channel: SeekableByteChannel,
        private val archiveLength: Long,
    ) : VolumeManager {
        private val sourceChannel = JunrarChannel(channel)
        private var supplied = false

        override fun nextVolume(archive: Archive, lastVolume: Volume?): Volume {
            if (lastVolume != null || supplied) throw java.io.IOException("Multi-volume RAR archives are unsupported")
            supplied = true
            return object : Volume {
                override fun getChannel(): com.github.junrar.io.SeekableReadOnlyByteChannel = sourceChannel
                override fun getLength(): Long = archiveLength
                override fun getArchive(): Archive = archive
            }
        }
    }

    private class JunrarChannel(
        private val channel: SeekableByteChannel,
    ) : com.github.junrar.io.SeekableReadOnlyByteChannel {
        override fun getPosition(): Long = channel.position()

        override fun setPosition(position: Long) {
            if (position < 0L || position > channel.size()) throw java.io.IOException("Seek is outside archive")
            channel.position(position)
        }

        override fun read(): Int {
            val one = ByteBuffer.allocate(1)
            return if (channel.read(one) < 0) -1 else one.array()[0].toInt() and 0xff
        }

        override fun read(bytes: ByteArray, offset: Int, count: Int): Int =
            channel.read(ByteBuffer.wrap(bytes, offset, count))

        override fun readFully(bytes: ByteArray, count: Int): Int {
            var read = 0
            while (read < count) {
                val current = read(bytes, read, count - read)
                if (current < 0) throw java.io.EOFException()
                if (current == 0) continue
                read += current
            }
            return read
        }

        override fun close() = channel.close()
    }

    private fun enforceRatio(unpacked: Long, packed: Long) {
        if (unpacked == 0L) return
        if (packed <= 0L || packed > Long.MAX_VALUE / limits.expansionRatio ||
            unpacked > packed * limits.expansionRatio
        ) {
            limit("Archive expansion ratio exceeds bound ($unpacked bytes from $packed bytes)")
        }
    }

    private fun checkedAdd(left: Long, right: Long): Long {
        if (left < 0L || right < 0L || left > Long.MAX_VALUE - right) limit("Archive size overflow")
        return left + right
    }

    private fun ByteArray.startsWith(signature: ByteArray, count: Int): Boolean =
        count >= signature.size && signature.indices.all { this[it] == signature[it] }

    private class ArchiveFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun fail(code: SkinImportCode, detail: String): Nothing = throw ArchiveFailure(code, detail)
    private fun corrupt(detail: String): Nothing = fail(SkinImportCode.ZIP_CORRUPT, detail)
    private fun limit(detail: String): Nothing = fail(SkinImportCode.LIMIT_EXCEEDED, detail)
    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private companion object {
        const val UTF8_FLAG = 0x0800
        const val PORTABLE_METHOD = -1
        const val PORTABLE_METHOD_NO_CRC = -2
        const val COPY_BUFFER_SIZE = 8192
        const val WINDOWS_REPARSE_POINT = 0x0400
        const val UNIX_TYPE_MASK = 0xf000
        const val UNIX_REGULAR = 0x8000
        const val UNIX_DIRECTORY = 0x4000
        const val UINT_MASK = 0xffffffffL
        val SEVEN_Z_SIGNATURE = byteArrayOf(0x37, 0x7a, 0xbc.toByte(), 0xaf.toByte(), 0x27, 0x1c)
        val RAR4_SIGNATURE = byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00)
        val RAR5_SIGNATURE = byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00)
    }
}
