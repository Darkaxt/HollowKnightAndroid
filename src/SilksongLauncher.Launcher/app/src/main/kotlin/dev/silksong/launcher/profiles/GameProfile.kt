package dev.silksong.launcher.profiles

enum class ContentLayout {
    ADDRESSABLES,
    CLASSIC_PLAYER,
}

data class GameProfile(
    val id: String,
    val runtimeStorageKey: String,
    val displayName: String,
    val steamAppId: Int,
    val steamDepotId: Int,
    val unityVersion: String,
    val currentGameVersion: String,
    val backwardCompatibilityVersions: Set<String> = emptySet(),
    val acceptedPlatforms: Set<String>,
    val dataDirectoryName: String,
    val executableNames: Set<String>,
    val contentLayout: ContentLayout,
    val patchSet: String,
    val addressablesRoot: String? = null,
)
