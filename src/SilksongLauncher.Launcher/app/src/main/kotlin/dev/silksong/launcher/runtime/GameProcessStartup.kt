package dev.silksong.launcher.runtime

import android.app.ActivityManager
import android.app.Application
import android.content.Context
import android.os.Build
import android.os.Process
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.UnityDex
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.build.UnityToolchainRegistry
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.SelectedGameStore
import java.io.File

data class GameProcessStartupSnapshot(
    val profileId: String,
    val generationId: String,
    val toolchainId: String,
    val packageDir: String,
    val nativeLibraryDir: String,
    val dataArchive: String,
    val unityRoot: String,
    val playerDexJar: String,
)

object ProcessRole {
    fun isGameProcess(packageName: String, processName: String): Boolean =
        processName == packageName

    fun currentName(context: Context): String? {
        if (Build.VERSION.SDK_INT >= 28) {
            Application.getProcessName()?.takeIf { it.isNotBlank() }?.let { return it }
        }
        val manager = context.getSystemService(ActivityManager::class.java) ?: return null
        return manager.runningAppProcesses
            ?.firstOrNull { it.pid == Process.myPid() }
            ?.processName
    }
}

/** Immutable profile and generation identity for the lifetime of one Unity process. */
object GameProcessStartup {
    @Volatile
    private var snapshot: GameProcessStartupSnapshot? = null

    fun prepare(context: Context): GameProcessStartupSnapshot {
        val processName = ProcessRole.currentName(context)
            ?: throw IllegalStateException("Could not identify the application process")
        check(ProcessRole.isGameProcess(context.packageName, processName)) {
            "Unity startup was requested from non-game process $processName"
        }
        val profile = SelectedGameStore(context).get()
        val paths = ProfileBuildPaths(
            context.filesDir,
            requireNotNull(context.getExternalFilesDir(null)) { "No external files directory" },
            profile,
        )
        val resolved = resolve(context, profile, paths)
        synchronized(this) {
            val existing = snapshot
            check(existing == null || existing == resolved) {
                "Unity process startup is already bound to ${existing?.profileId}/${existing?.generationId}"
            }
            if (existing == null) snapshot = resolved
            return requireNotNull(snapshot)
        }
    }

    internal fun resolve(
        context: Context,
        profile: GameProfile,
        paths: ProfileBuildPaths,
    ): GameProcessStartupSnapshot {
        require(GameProfiles.find(profile.id) == profile) {
            "Game startup requires an exact registered profile: ${profile.id}"
        }
        val installed = GenerationPublisher(paths.profilePaths).current()
            ?: throw IllegalStateException("No verified current generation for ${profile.id}")
        val descriptor = UnityToolchainRegistry.resolve(profile)
        check(installed.toolchainId == descriptor.contentHash) {
            "Generation toolchain mismatch for ${profile.id}: expected ${descriptor.contentHash}, " +
                "got ${installed.toolchainId}"
        }
        val expectedPatchManifest = ProductionBuildSignature.computeSha256(context)
        check(installed.patchManifestSha256 == expectedPatchManifest) {
            "Generation patch manifest mismatch for ${profile.id}: expected $expectedPatchManifest, " +
                "got ${installed.patchManifestSha256}"
        }
        val packageDir = File(installed.root, "pkg").canonicalFile
        check(File(packageDir, ".built").isFile) {
            "Current generation is missing its build marker for ${profile.id}"
        }
        check(File(packageDir, "lib/arm64/libil2cpp.so").length() > 0L) {
            "Current generation is missing its ARM64 IL2CPP runtime for ${profile.id}"
        }
        check(PlayerImage.runtimeArchivesPresent(context, profile, packageDir)) {
            "Current generation is missing its player archives for ${profile.id}"
        }
        val unityRoot = UnityToolchainRegistry.rootFor(paths.filesDir, descriptor).canonicalFile
        val playerDex = UnityDex.builtPlayerDex(paths.filesDir, descriptor, unityRoot)
            ?: throw IllegalStateException(
                "Current toolchain is missing its exact Unity player dex for ${profile.id}",
            )
        return GameProcessStartupSnapshot(
            profileId = profile.id,
            generationId = installed.id,
            toolchainId = installed.toolchainId,
            packageDir = packageDir.path,
            nativeLibraryDir = File(packageDir, "lib/arm64").canonicalPath,
            dataArchive = File(packageDir, "data.apk").canonicalPath,
            unityRoot = unityRoot.path,
            playerDexJar = playerDex.canonicalPath,
        )
    }

    @JvmStatic
    fun requireProfile(profileId: String) {
        check(GameProfiles.find(profileId) != null) { "Unsupported game profile: $profileId" }
        val startup = requireSnapshot()
        check(startup.profileId == profileId) {
            "Game intent profile $profileId does not match process startup profile ${startup.profileId}"
        }
    }

    @JvmStatic
    fun requireSnapshot(): GameProcessStartupSnapshot =
        snapshot ?: throw IllegalStateException("Unity process startup was not initialized")

    internal fun installForTests(value: GameProcessStartupSnapshot) {
        synchronized(this) {
            check(snapshot == null) { "Test startup snapshot is already installed" }
            snapshot = value
        }
    }

    internal fun resetForTests() {
        synchronized(this) { snapshot = null }
    }
}
