package dev.silksong.launcher.build

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.GameProfiles
import java.io.File
import java.nio.file.Path
import java.security.MessageDigest

object UnityToolchainRegistry {
    private const val IL2CPP_DLL = "Editor/Data/il2cpp/build/deploy/il2cpp.dll"
    private const val IL2CPP_CONFIG = "Editor/Data/il2cpp/libil2cpp/il2cpp-config.h"
    private const val MSCORLIB = "Editor/Data/MonoBleedingEdge/lib/mono/unityaot-linux/mscorlib.dll"

    private val descriptors = listOf(
        UnityToolchainDescriptor(
            unityVersion = "6000.0.50f1",
            editorUrl = "https://download.unity3d.com/download_unity/f1ef1dca8bff/" +
                "LinuxEditorInstaller/Unity-6000.0.50f1.tar.xz",
            editorSha256 = "076a2f975c9b807f5b9d9c560ac3cbb8202653c71a721ae5ad6e61d9b46e0d9b",
            androidModuleUrl = "https://download.unity3d.com/download_unity/f1ef1dca8bff/" +
                "MacEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-6000.0.50f1.pkg",
            androidModuleSha256 = "839de4ae756852b9f2ae9e193082b6d5d790bece180d616a6f40df3acf841e7d",
            editorArchiveBytes = 4_501_932_484L,
            androidModuleBytes = 673_712_656L,
            editorRequiredSha256 = mapOf(
                IL2CPP_DLL to "02d9d225cc8968fe39284dfbf2a9912796b3b0666d274294cfa6b90cf5e946bb",
                IL2CPP_CONFIG to "38d4d2855d372bb2a12de7dce3cde110d1ec9780232a6a298153bee96c352259",
                MSCORLIB to "2efab59f0bdc59e1242b40203aff1f96e529e880f752585286c2816871e4496c",
            ),
        ),
        UnityToolchainDescriptor(
            unityVersion = "6000.0.61f1",
            editorUrl = "https://download.unity3d.com/download_unity/74a0adb02c31/" +
                "LinuxEditorInstaller/Unity-6000.0.61f1.tar.xz",
            editorSha256 = "cf6182370a5c8911bc750122ee033d01d43e8bcf9348a930ab97f40831eef171",
            androidModuleUrl = "https://download.unity3d.com/download_unity/74a0adb02c31/" +
                "MacEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-6000.0.61f1.pkg",
            androidModuleSha256 = "af590a00ab049870c90b164ca86c1f245c01f2c22511f2a51148c566fa22afd5",
            editorArchiveBytes = 4_456_301_920L,
            androidModuleBytes = 675_183_137L,
            editorRequiredSha256 = mapOf(
                IL2CPP_DLL to "1dce82179954a6edbeb9c71cc20f05f2de3e22690c31078ee7acaa85aad0a1fe",
                IL2CPP_CONFIG to "50c73a112814ed24a9c36c067f5f0bc4fc906657bdb4d185335a15be28e2cd6d",
                MSCORLIB to "ac34797a4113d642776394e192cededc7de5b781761de8fac3725a75aa783e9b",
            ),
        ),
    )

    private val byVersion = descriptors.associateBy(UnityToolchainDescriptor::unityVersion).also {
        require(it.size == descriptors.size) { "Unity toolchain versions must be unique" }
        require(descriptors.map(UnityToolchainDescriptor::contentHash).toSet().size == descriptors.size) {
            "Unity toolchain descriptor hashes must be unique"
        }
    }

    fun resolve(profile: GameProfile): UnityToolchainDescriptor {
        require(GameProfiles.find(profile.id) == profile) {
            "Unity toolchains require an exact registered profile: ${profile.id}"
        }
        return resolve(profile.unityVersion)
    }

    fun resolve(unityVersion: String): UnityToolchainDescriptor =
        byVersion[unityVersion]
            ?: throw IllegalArgumentException("Unknown Unity toolchain version: $unityVersion")

    fun rootFor(filesDir: File, descriptor: UnityToolchainDescriptor): File {
        require(resolve(descriptor.unityVersion) == descriptor) {
            "Unity toolchain descriptor is not registered: ${descriptor.unityVersion}"
        }
        return File(File(filesDir, "toolchains"), descriptor.contentHash)
    }

    /**
     * Verify one resumable download in its owning staging directory.
     * A mismatch evicts only that exact file; immutable neighboring toolchains are untouched.
     */
    fun verifyStagedComponent(stagingDir: File, relativePath: String, expectedSha256: String): Boolean {
        require(expectedSha256.matches(Regex("[0-9a-f]{64}"))) { "Invalid expected SHA-256" }
        val owner = stagingDir.toPath().toAbsolutePath().normalize()
        val candidate = contained(owner.resolve(relativePath), owner).toFile()
        if (!candidate.isFile) return false
        if (sha256(candidate) == expectedSha256) return true
        check(candidate.delete()) { "Could not delete invalid staged component: $candidate" }
        return false
    }

    fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(1 shl 16)
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun contained(candidate: Path, owner: Path): Path {
        val normalized = candidate.toAbsolutePath().normalize()
        require(normalized.startsWith(owner)) { "Staged component escapes its owner: $candidate" }
        return normalized
    }
}
