package dev.silksong.launcher.profiles

import java.io.File
import java.nio.file.Path

class ProfilePaths(
    filesDir: File,
    val profile: GameProfile,
) {
    private val filesRoot: Path = filesDir.toPath().toAbsolutePath().normalize()
    private val profilesRoot: Path = filesRoot.resolve("profiles").normalize()

    init {
        require(GameProfiles.find(profile.id) == profile) {
            "Profile paths require an exact registered profile: ${profile.id}"
        }
    }

    val root: File = contained(profilesRoot.resolve(profile.id), profilesRoot).toFile()
    val generations: File = child("generations")
    val staging: File = child("staging")
    val skinsRoot: File = child("skins")
    val logs: File = child("logs")
    val currentPointer: File = child("current")
    val sourcePointer: File = child("source.pointer")

    private fun child(name: String): File =
        contained(root.toPath().resolve(name), root.toPath()).toFile()

    private fun contained(candidate: Path, owner: Path): Path {
        val normalizedOwner = owner.toAbsolutePath().normalize()
        val normalizedCandidate = candidate.toAbsolutePath().normalize()
        require(normalizedCandidate.startsWith(normalizedOwner)) {
            "Profile path escapes its owner: $normalizedCandidate"
        }
        return normalizedCandidate
    }
}
