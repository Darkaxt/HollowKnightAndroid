package dev.silksong.launcher.profiles

import java.io.File
import java.nio.file.Path

/**
 * Mutable build paths for one game profile before atomic generations are
 * introduced. Internal executable/package state and large external build
 * state are both namespaced by the registered profile identifier.
 */
class ProfileBuildPaths(
    filesDir: File,
    externalFilesDir: File,
    val profile: GameProfile,
) {
    val profilePaths = ProfilePaths(filesDir, profile)

    private val internalRoot: Path = profilePaths.root.toPath().toAbsolutePath().normalize()
    private val filesRoot: Path = filesDir.toPath().toAbsolutePath().normalize()
    private val externalFilesRoot: Path = externalFilesDir.toPath().toAbsolutePath().normalize()

    val packageDir: File = internal("pkg")
    // Silksong's catalog has a fixed 56-byte content-root field. The canonical
    // profile tree is too long once the package name is included, so only this
    // runtime bridge uses the compact, registry-owned profile key.
    val contentLink: File = contained(
        filesRoot.resolve("p").resolve(profile.runtimeStorageKey).resolve("aa"),
        filesRoot.resolve("p"),
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
