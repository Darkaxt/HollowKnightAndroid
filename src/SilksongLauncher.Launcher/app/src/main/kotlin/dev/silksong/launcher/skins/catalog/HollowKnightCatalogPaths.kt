package dev.silksong.launcher.skins.catalog

import android.content.res.AssetManager
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap

class CatalogPathSet internal constructor(
    val profile: SkinCatalogProfile,
    val exactBytes: ByteArray,
    val sha256: String,
    val paths: List<String>,
) {
    /** Compatibility surface remains Hollow Knight-only; new consumers must name their profile. */
    constructor(exactBytes: ByteArray, sha256: String, paths: List<String>) :
        this(SkinCatalogProfiles.HollowKnight, exactBytes, sha256, paths)

    init {
        require(sha256 == profile.sha256) { "Catalog digest is not the pinned ${profile.profileId} authority" }
        require(MessageDigest.getInstance("SHA-256").digest(exactBytes).toHex() == sha256) {
            "Catalog bytes do not match their digest"
        }
        require(decodeRows(exactBytes) == paths && paths.size == profile.pathCount && paths.toSet().size == paths.size) {
            "Catalog paths do not match the exact pinned bytes"
        }
        require(profile.paths.isEmpty() || profile.paths == paths) { "Catalog rows differ from closed profile authority" }
        PINNED.putIfAbsent(profile.profileId, this)
    }

    fun revalidate(): CatalogPathSet {
        require(sha256 == profile.sha256)
        require(MessageDigest.getInstance("SHA-256").digest(exactBytes).toHex() == sha256)
        require(decodeRows(exactBytes) == paths && paths.size == profile.pathCount && paths.toSet().size == paths.size)
        require(profile.paths.isEmpty() || profile.paths == paths)
        return this
    }

    val pathSet: Set<String> = paths.toSet()
    val asciiFolded: Map<String, List<String>> = paths.groupBy(::asciiFold)
    val catalogId: String get() = profile.catalogId

    companion object {
        private val PINNED = ConcurrentHashMap<String, CatalogPathSet>()

        fun requirePinned(profileId: String = "hollow-knight"): CatalogPathSet = PINNED[profileId]?.revalidate()
            ?: throw IllegalStateException("Pinned $profileId catalog has not been loaded")

        internal fun asciiFold(value: String): String = buildString(value.length) {
            value.forEach { character -> append(if (character in 'A'..'Z') character + ('a' - 'A') else character) }
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

object SkinCatalogPaths {
    fun load(assets: AssetManager, profile: SkinCatalogProfile): SkinResult<CatalogPathSet> = try {
        assets.open(profile.assetName, AssetManager.ACCESS_BUFFER).use { load(profile, it) }
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Catalog asset is unavailable: ${error.message}")
    }

    fun load(profile: SkinCatalogProfile, input: InputStream): SkinResult<CatalogPathSet> = try {
        validate(profile, input.readBytes())
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Catalog could not be read: ${error.message}")
    }

    private fun validate(profile: SkinCatalogProfile, bytes: ByteArray): SkinResult<CatalogPathSet> {
        fun invalid(detail: String) = SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, detail)
        if (bytes.isEmpty() || bytes.last() != '\n'.code.toByte()) return invalid("Catalog must be non-empty and LF terminated")
        if (bytes.size >= 3 && bytes[0] == 0xef.toByte() && bytes[1] == 0xbb.toByte() && bytes[2] == 0xbf.toByte()) {
            return invalid("Catalog must not contain a BOM")
        }
        if (bytes.any { it == '\r'.code.toByte() }) return invalid("Catalog must use LF, not CR")
        val rows = try { CatalogPathSet.decodeRows(bytes) } catch (_: Exception) { return invalid("Catalog must be strict UTF-8") }
        if (rows.any { row ->
                row.isEmpty() || !row.endsWith(".png") || row.startsWith('/') || row.contains('\\') ||
                    row.split('/').any { it.isEmpty() || it == "." || it == ".." }
            }) return invalid("Catalog contains an invalid path")
        if (rows.toSet().size != rows.size) return invalid("Catalog paths must be unique")
        val digest = MessageDigest.getInstance("SHA-256").digest(bytes).toHex()
        if (rows.size != profile.pathCount || digest != profile.sha256 ||
            (profile.paths.isNotEmpty() && rows != profile.paths)) return invalid("Catalog identity mismatch")
        return try { SkinResult.Ok(CatalogPathSet(profile, bytes.copyOf(), digest, rows)) }
        catch (error: IllegalArgumentException) { invalid(error.message ?: "Catalog identity mismatch") }
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
}

class HollowKnightCatalogPaths(private val assets: AssetManager) {
    fun load(): SkinResult<CatalogPathSet> = SkinCatalogPaths.load(assets, SkinCatalogProfiles.HollowKnight)

    companion object {
        const val ASSET_NAME = "hollow-knight-skin-catalog-v1.txt"
        const val CATALOG_ID = "hk-custom-knight-v3.5.0-205"
        const val SHA256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a"

        fun load(assetManager: AssetManager): SkinResult<CatalogPathSet> = HollowKnightCatalogPaths(assetManager).load()
        fun load(input: InputStream, enforcePinnedIdentity: Boolean = true): SkinResult<CatalogPathSet> =
            if (!enforcePinnedIdentity) SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Only the pinned catalog may create CatalogPathSet authority")
            else SkinCatalogPaths.load(SkinCatalogProfiles.HollowKnight, input)
    }
}
