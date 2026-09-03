package dev.silksong.launcher.skins.fixtures

import java.io.ByteArrayOutputStream
import java.util.zip.CRC32
import java.util.zip.Deflater

object RawZipFixture {
    enum class LocalTuple { ZERO, FULL, PARTIAL, MISMATCH, SENTINEL }

    data class Entry(
        val rawName: ByteArray,
        val data: ByteArray,
        val method: Int = 0,
        val descriptor: Boolean = false,
        val descriptorSignature: Boolean = false,
        val localTuple: LocalTuple = if (descriptor) LocalTuple.ZERO else LocalTuple.FULL,
        val utf8: Boolean = true,
        val centralExtra: ByteArray = ByteArray(0),
        val localExtra: ByteArray = centralExtra,
        val externalAttributes: Long = 0L,
    )

    data class Built(
        val bytes: ByteArray,
        val centralOffset: Int,
        val eocdOffset: Int,
        val localOffsets: List<Int>,
    )

    fun one(
        name: String = "Knight.png",
        data: ByteArray = TinyPngFixture.rgba(),
        method: Int = 0,
        descriptor: Boolean = false,
        descriptorSignature: Boolean = false,
        localTuple: LocalTuple = if (descriptor) LocalTuple.ZERO else LocalTuple.FULL,
    ): Built = build(
        listOf(
            Entry(
                rawName = name.toByteArray(Charsets.UTF_8),
                data = data,
                method = method,
                descriptor = descriptor,
                descriptorSignature = descriptorSignature,
                localTuple = localTuple,
            ),
        ),
    )

    fun build(entries: List<Entry>, comment: ByteArray = ByteArray(0)): Built {
        val output = ByteArrayOutputStream()
        val records = mutableListOf<Record>()
        for (entry in entries) {
            val compressed = when (entry.method) {
                0 -> entry.data
                8 -> rawDeflate(entry.data)
                else -> entry.data
            }
            val crc = CRC32().apply { update(entry.data) }.value
            val flags = (if (entry.descriptor) 0x0008 else 0) or (if (entry.utf8) 0x0800 else 0)
            val localOffset = output.size()
            output.writeLe32(0x04034b50)
            output.writeLe16(20)
            output.writeLe16(flags)
            output.writeLe16(entry.method)
            output.writeLe16(0)
            output.writeLe16(0)
            val tuple = localTuple(entry.localTuple, crc, compressed.size.toLong(), entry.data.size.toLong())
            output.writeLe32(tuple.first)
            output.writeLe32(tuple.second)
            output.writeLe32(tuple.third)
            output.writeLe16(entry.rawName.size)
            output.writeLe16(entry.localExtra.size)
            output.write(entry.rawName)
            output.write(entry.localExtra)
            output.write(compressed)
            if (entry.descriptor) {
                if (entry.descriptorSignature) output.writeLe32(0x08074b50)
                output.writeLe32(crc)
                output.writeLe32(compressed.size.toLong())
                output.writeLe32(entry.data.size.toLong())
            }
            records += Record(entry, flags, crc, compressed.size.toLong(), entry.data.size.toLong(), localOffset)
        }
        val centralOffset = output.size()
        for (record in records) {
            val entry = record.entry
            output.writeLe32(0x02014b50)
            output.writeLe16(0x0314)
            output.writeLe16(20)
            output.writeLe16(record.flags)
            output.writeLe16(entry.method)
            output.writeLe16(0)
            output.writeLe16(0)
            output.writeLe32(record.crc)
            output.writeLe32(record.compressedSize)
            output.writeLe32(record.uncompressedSize)
            output.writeLe16(entry.rawName.size)
            output.writeLe16(entry.centralExtra.size)
            output.writeLe16(0)
            output.writeLe16(0)
            output.writeLe16(0)
            output.writeLe32(entry.externalAttributes)
            output.writeLe32(record.localOffset.toLong())
            output.write(entry.rawName)
            output.write(entry.centralExtra)
        }
        val centralSize = output.size() - centralOffset
        val eocdOffset = output.size()
        output.writeLe32(0x06054b50)
        output.writeLe16(0)
        output.writeLe16(0)
        output.writeLe16(records.size)
        output.writeLe16(records.size)
        output.writeLe32(centralSize.toLong())
        output.writeLe32(centralOffset.toLong())
        output.writeLe16(comment.size)
        output.write(comment)
        return Built(output.toByteArray(), centralOffset, eocdOffset, records.map { it.localOffset })
    }

    fun patchLe16(bytes: ByteArray, offset: Int, value: Int): ByteArray =
        bytes.copyOf().also {
            it[offset] = value.toByte()
            it[offset + 1] = (value ushr 8).toByte()
        }

    fun patchLe32(bytes: ByteArray, offset: Int, value: Long): ByteArray =
        bytes.copyOf().also {
            for (index in 0 until 4) it[offset + index] = (value ushr (index * 8)).toByte()
        }

    private data class Record(
        val entry: Entry,
        val flags: Int,
        val crc: Long,
        val compressedSize: Long,
        val uncompressedSize: Long,
        val localOffset: Int,
    )

    private fun localTuple(
        tuple: LocalTuple,
        crc: Long,
        compressedSize: Long,
        uncompressedSize: Long,
    ): Triple<Long, Long, Long> = when (tuple) {
        LocalTuple.ZERO -> Triple(0, 0, 0)
        LocalTuple.FULL -> Triple(crc, compressedSize, uncompressedSize)
        LocalTuple.PARTIAL -> Triple(crc, 0, uncompressedSize)
        LocalTuple.MISMATCH -> Triple(crc xor 1, compressedSize, uncompressedSize)
        LocalTuple.SENTINEL -> Triple(0xffffffffL, 0xffffffffL, 0xffffffffL)
    }

    private fun rawDeflate(bytes: ByteArray): ByteArray {
        val deflater = Deflater(Deflater.DEFAULT_COMPRESSION, true)
        return try {
            deflater.setInput(bytes)
            deflater.finish()
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(256)
            while (!deflater.finished()) output.write(buffer, 0, deflater.deflate(buffer))
            output.toByteArray()
        } finally {
            deflater.end()
        }
    }

    private fun ByteArrayOutputStream.writeLe16(value: Int) {
        write(value)
        write(value ushr 8)
    }

    private fun ByteArrayOutputStream.writeLe32(value: Long) {
        write(value.toInt())
        write((value ushr 8).toInt())
        write((value ushr 16).toInt())
        write((value ushr 24).toInt())
    }
}
