package dev.silksong.launcher.skins.importing

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.PngInfo
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import java.io.File
import java.io.InputStream

fun interface PngDecoder {
    fun decodeAndRelease(file: File, expected: PngInfo): SkinResult<DecodeResult>
}

class AndroidPngDecoder(
    private val limits: SkinLimits = SkinLimits.V1,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
) : PngDecoder {
    override fun decodeAndRelease(file: File, expected: PngInfo): SkinResult<DecodeResult> = try {
        val fileIdentity = fileSystem.identity(file)
        if (!fileIdentity.regularFile || fileIdentity.size != expected.byteCount || fileIdentity.size > limits.textureBytes) {
            SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "PNG file is absent, changed, or too large")
        } else {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            decodeStable(file, fileIdentity) { BitmapFactory.decodeStream(it, null, bounds) }
            if (bounds.outWidth != expected.width || bounds.outHeight != expected.height || bounds.outMimeType != "image/png") {
                SkinResult.Error(SkinImportCode.PNG_INVALID, "Android dimensions differ from structural PNG authority")
            } else {
                val pixels = bounds.outWidth.toLong() * bounds.outHeight.toLong()
                if (bounds.outWidth > limits.dimension || bounds.outHeight > limits.dimension || pixels > limits.decodedPixels) {
                    SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Decoded PNG exceeds dimensions or pixel bound")
                } else {
                    var bitmap: Bitmap? = null
                    try {
                        val options = BitmapFactory.Options().apply {
                            inJustDecodeBounds = false
                            inScaled = false
                            inPreferredConfig = Bitmap.Config.ARGB_8888
                        }
                        bitmap = decodeStable(file, fileIdentity) { BitmapFactory.decodeStream(it, null, options) }
                        if (bitmap == null || bitmap.width != expected.width || bitmap.height != expected.height) {
                            SkinResult.Error(SkinImportCode.PNG_INVALID, "Decoded PNG dimensions changed")
                        } else {
                            SkinResult.Ok(DecodeResult(bitmap.width, bitmap.height, pixels))
                        }
                    } finally {
                        bitmap?.recycle()
                    }
                }
            }
        }
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Secure Android PNG decode failed: ${error.message}")
    }

    private inline fun <T> decodeStable(
        file: File,
        expectedIdentity: dev.silksong.launcher.skins.contracts.SkinNodeIdentity,
        decode: (InputStream) -> T,
    ): T {
        if (fileSystem.identity(file) != expectedIdentity) throw IllegalStateException("PNG identity changed before decode")
        val result = fileSystem.openNoFollow(file).use(decode)
        if (fileSystem.identity(file) != expectedIdentity) throw IllegalStateException("PNG identity changed during decode")
        return result
    }
}
