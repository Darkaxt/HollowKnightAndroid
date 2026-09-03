package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.PngInfo
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream
import java.util.zip.CRC32

class PngStructureValidator(
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun inspect(file: File): SkinResult<PngInfo> = if (!file.isFile) {
        SkinResult.Error(SkinImportCode.PNG_INVALID, "PNG is not a regular file")
    } else if (file.length() > limits.textureBytes) {
        SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "PNG exceeds texture byte bound")
    } else {
        file.inputStream().use { inspect(it, file.length()) }
    }

    fun inspect(bytes: ByteArray): SkinResult<PngInfo> =
        ByteArrayInputStream(bytes).use { inspect(it, bytes.size.toLong()) }

    fun inspect(input: InputStream, byteCount: Long): SkinResult<PngInfo> = try {
        if (byteCount < 0) invalid("PNG byte count is invalid")
        if (byteCount > limits.textureBytes) limit("PNG exceeds texture byte bound")
        val output = ByteArrayOutputStream(minOf(byteCount, limits.textureBytes).toInt())
        val buffer = ByteArray(64 * 1024)
        var count = 0L
        while (true) {
            val read = input.read(buffer)
            if (read < 0) break
            if (read == 0) continue
            count += read
            if (count > limits.textureBytes) limit("PNG exceeds texture byte bound while streaming")
            output.write(buffer, 0, read)
        }
        if (count != byteCount) invalid("PNG size changed while reading")
        parse(output.toByteArray())
    } catch (error: PngFailure) {
        SkinResult.Error(error.code, error.message ?: error.code.name)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.PNG_INVALID, "PNG parse failed: ${error.message}")
    }

    private fun parse(bytes: ByteArray): SkinResult<PngInfo> {
        if (bytes.size < SIGNATURE.size || !bytes.copyOfRange(0, SIGNATURE.size).contentEquals(SIGNATURE)) {
            invalid("PNG signature is invalid")
        }
        var offset = SIGNATURE.size
        var width = 0
        var height = 0
        var chunks = 0
        var sawIdat = false
        var leftIdat = false
        var sawIend = false
        while (offset < bytes.size) {
            if (sawIend || bytes.size - offset < 12) invalid("PNG has trailing or truncated chunk data")
            val lengthLong = be32(bytes, offset)
            if (lengthLong > Int.MAX_VALUE || lengthLong > bytes.size - offset - 12L) invalid("PNG chunk length is invalid")
            val length = lengthLong.toInt()
            val typeBytes = bytes.copyOfRange(offset + 4, offset + 8)
            if (typeBytes.any { value -> (value.toInt() and 0xff) !in 0x41..0x5a && (value.toInt() and 0xff) !in 0x61..0x7a }) {
                invalid("PNG chunk type is invalid")
            }
            val type = typeBytes.toString(Charsets.US_ASCII)
            val dataStart = offset + 8
            val crcOffset = dataStart + length
            val expectedCrc = be32(bytes, crcOffset)
            val actualCrc = CRC32().apply { update(bytes, offset + 4, length + 4) }.value
            if (expectedCrc != actualCrc) invalid("PNG chunk CRC mismatch")
            chunks++
            when (type) {
                "IHDR" -> {
                    if (chunks != 1 || length != 13 || width != 0) invalid("PNG IHDR must be first and unique")
                    width = be32(bytes, dataStart).checkedDimension("width")
                    height = be32(bytes, dataStart + 4).checkedDimension("height")
                    val bitDepth = bytes[dataStart + 8].toInt() and 0xff
                    val colorType = bytes[dataStart + 9].toInt() and 0xff
                    if (!validBitDepth(bitDepth, colorType) || bytes[dataStart + 10] != 0.toByte() ||
                        bytes[dataStart + 11] != 0.toByte() || (bytes[dataStart + 12].toInt() and 0xff) !in 0..1
                    ) invalid("PNG IHDR fields are unsupported")
                    val pixels = width.toLong() * height.toLong()
                    if (pixels > limits.decodedPixels) limit("PNG decoded pixel bound exceeded")
                }
                "IDAT" -> {
                    if (width == 0 || leftIdat) invalid("PNG IDAT order is invalid")
                    sawIdat = true
                }
                "IEND" -> {
                    if (length != 0 || !sawIdat) invalid("PNG IEND is invalid")
                    sawIend = true
                }
                "acTL", "fcTL", "fdAT" -> invalid("APNG is unsupported")
                "PLTE" -> if (sawIdat || length == 0 || length % 3 != 0 || length > 768) invalid("PNG PLTE is invalid")
                else -> {
                    if (sawIdat) leftIdat = true
                    if ((typeBytes[0].toInt() and 0x20) == 0) invalid("Unknown critical PNG chunk")
                }
            }
            if (type != "IDAT" && sawIdat && type != "IEND") leftIdat = true
            offset = crcOffset + 4
        }
        if (!sawIend || width == 0 || offset != bytes.size) invalid("PNG is incomplete")
        return SkinResult.Ok(PngInfo(width, height, bytes.size.toLong()))
    }

    private fun Long.checkedDimension(name: String): Int {
        if (this == 0L) invalid("PNG $name is zero")
        if (this > limits.dimension) limit("PNG $name exceeds dimension bound")
        return toInt()
    }

    private fun validBitDepth(depth: Int, color: Int): Boolean = when (color) {
        0 -> depth in setOf(1, 2, 4, 8, 16)
        2, 4, 6 -> depth in setOf(8, 16)
        3 -> depth in setOf(1, 2, 4, 8)
        else -> false
    }

    private class PngFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun invalid(detail: String): Nothing = throw PngFailure(SkinImportCode.PNG_INVALID, detail)
    private fun limit(detail: String): Nothing = throw PngFailure(SkinImportCode.LIMIT_EXCEEDED, detail)

    companion object {
        fun inspect(input: InputStream, length: Long): SkinResult<PngInfo> =
            PngStructureValidator().inspect(input, length)

        private val SIGNATURE = byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
        fun be32(bytes: ByteArray, offset: Int): Long =
            ((bytes[offset].toLong() and 0xff) shl 24) or
                ((bytes[offset + 1].toLong() and 0xff) shl 16) or
                ((bytes[offset + 2].toLong() and 0xff) shl 8) or
                (bytes[offset + 3].toLong() and 0xff)
    }
}
