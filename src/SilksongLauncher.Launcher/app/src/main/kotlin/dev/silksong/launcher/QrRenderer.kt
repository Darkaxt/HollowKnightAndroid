// QrRenderer — turn a URL into a Bitmap via ZXing's QRCodeWriter.
// Pure helper, no Android dependencies beyond Bitmap itself.

package dev.silksong.launcher

import android.graphics.Bitmap
import android.graphics.Color
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel

object QrRenderer {

    /**
     * Renders [data] (typically a URL) as a square Bitmap of size
     * [sizePx]. Uses error-correction level M which is the sweet spot
     * between robustness and density; H makes the code more visually
     * dense and harder for a phone to scan from a 6-inch handheld
     * screen.
     */
    fun render(data: String, sizePx: Int = 800): Bitmap {
        val hints = mapOf(
            EncodeHintType.CHARACTER_SET to "UTF-8",
            EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M,
            EncodeHintType.MARGIN to 1,
        )
        val matrix = QRCodeWriter().encode(data, BarcodeFormat.QR_CODE, sizePx, sizePx, hints)
        val bmp = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.RGB_565)
        for (y in 0 until sizePx) {
            for (x in 0 until sizePx) {
                bmp.setPixel(x, y, if (matrix[x, y]) Color.BLACK else Color.WHITE)
            }
        }
        return bmp
    }
}
