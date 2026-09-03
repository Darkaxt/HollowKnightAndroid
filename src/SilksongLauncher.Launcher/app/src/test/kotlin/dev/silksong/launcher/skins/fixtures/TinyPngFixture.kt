package dev.silksong.launcher.skins.fixtures

import java.io.ByteArrayOutputStream
import java.util.zip.CRC32
import java.util.zip.Deflater

object TinyPngFixture {
    fun rgba(width: Int = 1, height: Int = 1): ByteArray {
        require(width > 0 && height > 0)
        val raw = ByteArray((width * 4 + 1) * height)
        for (row in 0 until height) {
            val base = row * (width * 4 + 1)
            raw[base] = 0
            for (column in 0 until width) {
                val pixel = base + 1 + column * 4
                raw[pixel] = 0x22
                raw[pixel + 1] = 0x66
                raw[pixel + 2] = 0xAA.toByte()
                raw[pixel + 3] = 0xFF.toByte()
            }
        }
        val ihdr = ByteArrayOutputStream().apply {
            writeU32(width.toLong())
            writeU32(height.toLong())
            write(byteArrayOf(8, 6, 0, 0, 0))
        }.toByteArray()
        return ByteArrayOutputStream().apply {
            write(byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a))
            writeChunk("IHDR", ihdr)
            writeChunk("IDAT", deflate(raw))
            writeChunk("IEND", ByteArray(0))
        }.toByteArray()
    }

    fun withChunk(type: String, data: ByteArray = ByteArray(0)): ByteArray {
        val original = rgba()
        val beforeIend = original.size - 12
        return ByteArrayOutputStream().apply {
            write(original, 0, beforeIend)
            writeChunk(type, data)
            write(original, beforeIend, 12)
        }.toByteArray()
    }

    fun corruptCrc(): ByteArray = rgba().also { it[it.lastIndex - 1] = (it[it.lastIndex - 1].toInt() xor 1).toByte() }

    private fun deflate(bytes: ByteArray): ByteArray {
        val deflater = Deflater(Deflater.DEFAULT_COMPRESSION, false)
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

    private fun ByteArrayOutputStream.writeChunk(type: String, data: ByteArray) {
        val typeBytes = type.toByteArray(Charsets.US_ASCII)
        require(typeBytes.size == 4)
        writeU32(data.size.toLong())
        write(typeBytes)
        write(data)
        val crc = CRC32().apply {
            update(typeBytes)
            update(data)
        }
        writeU32(crc.value)
    }

    private fun ByteArrayOutputStream.writeU32(value: Long) {
        write((value ushr 24).toInt())
        write((value ushr 16).toInt())
        write((value ushr 8).toInt())
        write(value.toInt())
    }
}
