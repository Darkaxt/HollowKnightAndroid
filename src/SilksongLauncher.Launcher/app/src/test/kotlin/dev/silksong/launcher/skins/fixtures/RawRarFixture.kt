package dev.silksong.launcher.skins.fixtures

import java.io.ByteArrayOutputStream
import java.util.zip.CRC32

object RawRarFixture {
    data class Entry(
        val name: String,
        val bytes: ByteArray,
        val flags: Int = 0,
        val host: Int = 2,
        val attributes: Int = 0x20,
    )

    fun build(entries: List<Entry>): ByteArray = ByteArrayOutputStream().use { output ->
        output.write(byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00))
        output.write(header(0x73, 0, le16(0) + le32(0)))
        entries.forEach { entry ->
            val name = entry.name.toByteArray(Charsets.UTF_8)
            require(name.isNotEmpty() && name.size <= 0xffff)
            val body = le32(entry.bytes.size) +
                le32(entry.bytes.size) +
                byteArrayOf(entry.host.toByte()) +
                le32(crc32(entry.bytes)) +
                le32(0) +
                byteArrayOf(20, 0x30) +
                le16(name.size) +
                le32(entry.attributes) +
                name
            output.write(header(0x74, 0x8000 or entry.flags, body))
            output.write(entry.bytes)
        }
        // Canonical RAR4 end marker. Unlike ordinary headers, its CRC and
        // flags are fixed by the format rather than recomputed from the body.
        output.write(byteArrayOf(0xc4.toByte(), 0x3d, 0x7b, 0x00, 0x40, 0x07, 0x00))
        output.toByteArray()
    }

    private fun header(type: Int, flags: Int, body: ByteArray): ByteArray {
        val remainder = byteArrayOf(type.toByte()) + le16(flags) + le16(7 + body.size) + body
        return le16(crc32(remainder) and 0xffff) + remainder
    }

    private fun crc32(bytes: ByteArray): Int = CRC32().apply { update(bytes) }.value.toInt()
    private fun le16(value: Int) = byteArrayOf(value.toByte(), (value ushr 8).toByte())
    private fun le32(value: Int) = byteArrayOf(
        value.toByte(),
        (value ushr 8).toByte(),
        (value ushr 16).toByte(),
        (value ushr 24).toByte(),
    )
}
