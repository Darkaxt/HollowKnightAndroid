package dev.silksong.launcher.build

import dev.silksong.launcher.profiles.GameProfile
import java.nio.charset.StandardCharsets
import java.security.MessageDigest

object GenerationProvenance {
    private val sha256 = Regex("[0-9a-f]{64}")

    /**
     * Uses the exact profile manifest when one exists. Silksong predates the
     * classic-tree manifest, so its registered source contract is hashed into
     * the same stable field until a Steam manifest is persisted locally.
     */
    fun sourceManifestSha256(profile: GameProfile, exactManifestSha256: String?): String {
        if (exactManifestSha256 != null) {
            require(sha256.matches(exactManifestSha256.lowercase())) {
                "Invalid exact source manifest SHA-256"
            }
            return exactManifestSha256.lowercase()
        }
        val canonical = buildString {
            append("profile-source-contract-v1\n")
            append(profile.id).append('\n')
            append(profile.currentGameVersion).append('\n')
            append(profile.steamAppId).append('\n')
            append(profile.steamDepotId).append('\n')
            append(profile.unityVersion).append('\n')
            append(profile.dataDirectoryName).append('\n')
            profile.executableNames.sorted().forEach { append(it).append('\n') }
            append(profile.contentLayout.name).append('\n')
        }
        return MessageDigest.getInstance("SHA-256")
            .digest(canonical.toByteArray(StandardCharsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
    }
}
