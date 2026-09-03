package dev.silksong.launcher.skins.catalog

import android.content.res.AssetManager
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicReference

data class CatalogPathSet(
    val exactBytes: ByteArray,
    val sha256: String,
    val paths: List<String>,
) {
    init {
        require(sha256 == HollowKnightCatalogPaths.SHA256) { "Catalog digest is not the pinned Hollow Knight authority" }
        require(MessageDigest.getInstance("SHA-256").digest(exactBytes).toHex() == sha256) {
            "Catalog bytes do not match their digest"
        }
        require(decodeRows(exactBytes) == paths && paths.size == 205 && paths.toSet().size == paths.size) {
            "Catalog paths do not match the exact pinned bytes"
        }
        PINNED.compareAndSet(null, this)
    }

    fun revalidate(): CatalogPathSet {
        require(sha256 == HollowKnightCatalogPaths.SHA256)
        require(MessageDigest.getInstance("SHA-256").digest(exactBytes).toHex() == sha256)
        require(decodeRows(exactBytes) == paths && paths.size == 205 && paths.toSet().size == paths.size)
        return this
    }

    val pathSet: Set<String> = paths.toSet()
    val asciiFolded: Map<String, List<String>> = paths.groupBy(::asciiFold)
    val catalogId: String get() = HollowKnightCatalogPaths.CATALOG_ID

    companion object {
        private val PINNED = AtomicReference<CatalogPathSet?>()

        fun requirePinned(): CatalogPathSet = PINNED.get()?.revalidate()
            ?: throw IllegalStateException("Pinned Hollow Knight catalog has not been loaded")

        internal fun asciiFold(value: String): String = buildString(value.length) {
            value.forEach { character ->
                append(if (character in 'A'..'Z') character + ('a' - 'A') else character)
            }
        }

        internal fun decodeRows(bytes: ByteArray): List<String> {
            val text = StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes))
                .toString()
            return text.dropLast(1).split('\n')
        }

        private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }
}

class HollowKnightCatalogPaths(private val assets: AssetManager) {
    fun load(): SkinResult<CatalogPathSet> = try {
        assets.open(ASSET_NAME, AssetManager.ACCESS_BUFFER).use { load(it) }
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Catalog asset is unavailable: ${error.message}")
    }

    companion object {
        const val ASSET_NAME = "hollow-knight-skin-catalog-v1.txt"
        const val CATALOG_ID = "hk-custom-knight-v3.5.0-205"
        const val SHA256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a"

        fun load(assetManager: AssetManager): SkinResult<CatalogPathSet> = HollowKnightCatalogPaths(assetManager).load()

        fun load(input: InputStream, enforcePinnedIdentity: Boolean = true): SkinResult<CatalogPathSet> = try {
            validate(input.readBytes(), enforcePinnedIdentity)
        } catch (error: Exception) {
            SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Catalog could not be read: ${error.message}")
        }

        private fun validate(bytes: ByteArray, enforcePinnedIdentity: Boolean): SkinResult<CatalogPathSet> {
            fun invalid(detail: String) = SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, detail)
            if (bytes.isEmpty() || bytes.last() != '\n'.code.toByte()) return invalid("Catalog must be non-empty and LF terminated")
            if (bytes.size >= 3 && bytes[0] == 0xef.toByte() && bytes[1] == 0xbb.toByte() && bytes[2] == 0xbf.toByte()) {
                return invalid("Catalog must not contain a BOM")
            }
            if (bytes.any { it == '\r'.code.toByte() }) return invalid("Catalog must use LF, not CR")
            val rows = try {
                CatalogPathSet.decodeRows(bytes)
            } catch (_: Exception) {
                return invalid("Catalog must be strict UTF-8")
            }
            if (rows.any { row ->
                    row.isEmpty() || !row.endsWith(".png") || row.startsWith('/') || row.contains('\\') ||
                        row.split('/').any { it.isEmpty() || it == "." || it == ".." }
                }
            ) return invalid("Catalog contains an invalid path")
            if (rows.toSet().size != rows.size) return invalid("Catalog paths must be unique")
            val digest = MessageDigest.getInstance("SHA-256").digest(bytes).toHex()
            if (!enforcePinnedIdentity) return invalid("Only the pinned catalog may create CatalogPathSet authority")
            if (rows.size != 205 || digest != SHA256) return invalid("Catalog identity mismatch")
            return try {
                SkinResult.Ok(CatalogPathSet(bytes.copyOf(), digest, rows))
            } catch (error: IllegalArgumentException) {
                invalid(error.message ?: "Catalog identity mismatch")
            }
        }

        private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }
}
