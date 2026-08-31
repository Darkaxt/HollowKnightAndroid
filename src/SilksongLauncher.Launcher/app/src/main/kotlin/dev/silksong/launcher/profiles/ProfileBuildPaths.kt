package dev.silksong.launcher.profiles

import java.io.File
import java.nio.file.Path

/**
 * Build paths for one game profile. Published packages live in immutable
 * [ProfilePaths.generations]; [packageDir] is retained only for adopting and
 * launching installs made before atomic generations existed. Large reusable
 * build state remains external and profile-scoped.
 */
class ProfileBuildPaths(
    val filesDir: File,
    externalFilesDir: File,
    val profile: GameProfile,
) {
    val profilePaths = ProfilePaths(filesDir, profile)

    private val internalRoot: Path = profilePaths.root.toPath().toAbsolutePath().normalize()
    private val filesRoot: Path = filesDir.toPath().toAbsolutePath().normalize()
    private val appDataRoot: Path = requireNotNull(filesRoot.parent) {
        "App files directory has no private-data parent: $filesRoot"
    }
    private val externalFilesRoot: Path = externalFilesDir.toPath().toAbsolutePath().normalize()

    val packageDir: File = internal("pkg")
    // Silksong's catalog has a fixed 56-byte content-root field. Even
    // <files>/p/<key>/aa is too long for test package suffixes, so this one
    // runtime bridge lives directly under the app-private data root.
    val contentLink: File = contained(
        appDataRoot.resolve("p").resolve(profile.runtimeStorageKey).resolve("aa"),
        appDataRoot.resolve("p"),
    ).toFile()

    val externalRoot: File = contained(
        externalFilesRoot.resolve("profiles").resolve(profile.id),
        externalFilesRoot.resolve("profiles"),
    ).toFile()
    val buildRoot: File = external("build")
    val installStaging: File = external("staging")
    val depotStaging: File = external("depot-staging")
    val downloadDepot: File = external("depot")
    val contentPointer: File = external("content-path.txt")

    private fun internal(name: String): File =
        contained(internalRoot.resolve(name), internalRoot).toFile()

    private fun external(name: String): File =
        contained(externalRoot.toPath().resolve(name), externalRoot.toPath()).toFile()

    private fun contained(candidate: Path, owner: Path): Path {
        val normalizedOwner = owner.toAbsolutePath().normalize()
        val normalizedCandidate = candidate.toAbsolutePath().normalize()
        require(normalizedCandidate.startsWith(normalizedOwner)) {
            "Profile build path escapes its owner: $normalizedCandidate"
        }
        return normalizedCandidate
    }
}
