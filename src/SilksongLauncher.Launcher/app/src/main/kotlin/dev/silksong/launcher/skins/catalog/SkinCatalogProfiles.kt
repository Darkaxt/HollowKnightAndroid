package dev.silksong.launcher.skins.catalog

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile

/** Exact decoded atlas authority for one canonical target. */
data class SkinTextureDimensions(val width: Int, val height: Int) {
    init {
        require(width > 0 && height > 0)
    }
}

/** Closed, immutable per-game skin schema authority. */
class SkinCatalogProfile internal constructor(
    val profileId: String,
    val gameVersion: String,
    val assetName: String,
    val catalogId: String,
    val sha256: String,
    paths: List<String>,
    textureDimensions: Map<String, SkinTextureDimensions>,
    private val expectedPathCount: Int,
    val legacyMigrationAllowed: Boolean,
) {
    val paths: List<String> = paths.toList()
    val textureDimensions: Map<String, SkinTextureDimensions> = textureDimensions.toMap()
    val pathCount: Int get() = expectedPathCount

    init {
        require(profileId == "hollow-knight" || profileId == "silksong")
        require(expectedPathCount > 0 && (paths.isEmpty() || paths.size == expectedPathCount))
        require(paths.toSet().size == paths.size)
        require(paths.all(::safeCanonicalPath))
        require(textureDimensions.keys.all { it in paths })
        require(textureDimensions.isEmpty() || textureDimensions.keys == paths.toSet())
        require(sha256.matches(Regex("[0-9a-f]{64}")))
        require(legacyMigrationAllowed == (profileId == "hollow-knight"))
    }

    private fun safeCanonicalPath(path: String): Boolean =
        path.endsWith(".png") && !path.startsWith('/') && !path.contains('\\') &&
            path.split('/').all { it.isNotEmpty() && it != "." && it != ".." }
}

object SkinCatalogProfiles {
    // Generated from the 1.0.29980 depot census (tools/skin-census, #63): every tk2d collection atlas.
    private val silksongPaths = SilksongCatalogData.ROWS.map { it.first }

    private val silksongDimensions = SilksongCatalogData.ROWS.associate { (path, width, height) ->
        path to SkinTextureDimensions(width, height)
    }

    val HollowKnight = SkinCatalogProfile(
        HollowKnightProfile.id,
        HollowKnightProfile.currentGameVersion,
        HollowKnightCatalogPaths.ASSET_NAME,
        HollowKnightCatalogPaths.CATALOG_ID,
        HollowKnightCatalogPaths.SHA256,
        emptyList(),
        emptyMap(),
        expectedPathCount = 205,
        legacyMigrationAllowed = true,
    )
    val Silksong = SkinCatalogProfile(
        SilksongProfile.id,
        SilksongProfile.currentGameVersion,
        SilksongCatalogPaths.ASSET_NAME,
        SilksongCatalogPaths.CATALOG_ID,
        SilksongCatalogPaths.SHA256,
        silksongPaths,
        silksongDimensions,
        expectedPathCount = SilksongCatalogData.PATH_COUNT,
        legacyMigrationAllowed = false,
    )

    fun require(profile: GameProfile): SkinCatalogProfile = when {
        profile === HollowKnightProfile -> HollowKnight
        profile === SilksongProfile -> Silksong
        else -> throw IllegalArgumentException("No skin catalog for unregistered profile ${profile.id}")
    }

    fun require(profileId: String): SkinCatalogProfile = when (profileId) {
        HollowKnightProfile.id -> HollowKnight
        SilksongProfile.id -> Silksong
        else -> throw IllegalArgumentException("No skin catalog for profile $profileId")
    }
}

object SilksongCatalogPaths {
    const val ASSET_NAME = "silksong-skin-catalog-v1.txt"
    const val CATALOG_ID = SilksongCatalogData.CATALOG_ID
    /** Earlier catalogs whose paths are a strict prefix of this one; their packs stay valid (#34, #63). */
    val COMPATIBLE_CATALOG_IDS = setOf(
        "silksong-1.0.29980-character-hud-11",
        "silksong-1.0.29980-character-hud-16",
    )
    const val SHA256 = SilksongCatalogData.SHA256
}
