package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.DepotFetcher
import dev.silksong.launcher.DepotLocation
import dev.silksong.launcher.BuildReset
import dev.silksong.launcher.LauncherLog
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.UnityFetcher
import dev.silksong.launcher.build.UnityToolchainRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import java.io.File

@RunWith(RobolectricTestRunner::class)
class SilksongRegressionTest {
    private fun File.writeFixture(contents: String): File = apply {
        requireNotNull(parentFile).mkdirs()
        writeText(contents)
    }

    @Test
    fun `profile preserves the existing Silksong pipeline identity`() {
        val profile = GameProfiles.require("silksong")

        assertEquals(1030300, profile.steamAppId)
        assertEquals(1030303, profile.steamDepotId)
        assertEquals("6000.0.50f1", profile.unityVersion)
        assertEquals("Hollow Knight Silksong_Data", profile.dataDirectoryName)
        assertEquals(setOf("Hollow Knight Silksong.x86_64"), profile.executableNames)
        assertEquals("ss", profile.runtimeStorageKey)
        assertEquals(ContentLayout.ADDRESSABLES, profile.contentLayout)
        assertEquals("StreamingAssets/aa/StandaloneLinux64", profile.addressablesRoot)
    }

    @Test
    fun `mutable Silksong build state is profile scoped`() {
        val filesDir = File("build/test-silksong-regression/internal").absoluteFile
        val externalFilesDir = File("build/test-silksong-regression/external").absoluteFile
        val profile = GameProfiles.require("silksong")
        val paths = ProfileBuildPaths(filesDir, externalFilesDir, profile)

        assertEquals(File(filesDir, "profiles/silksong/pkg"), paths.packageDir)
        assertEquals(File(filesDir, "p/ss/aa"), paths.contentLink)
        assertEquals(File(externalFilesDir, "profiles/silksong/build"), paths.buildRoot)
        assertEquals(File(externalFilesDir, "profiles/silksong/staging"), paths.installStaging)
        assertEquals(File(externalFilesDir, "profiles/silksong/depot-staging"), paths.depotStaging)
        assertEquals(File(externalFilesDir, "profiles/silksong/depot"), paths.downloadDepot)
        assertEquals(File(externalFilesDir, "profiles/silksong/content-path.txt"), paths.contentPointer)
        val contentRoot = requireNotNull(PlayerImage.contentRootForProfile("dev.example", paths))
        assertEquals("/data/user/0/dev.example/files/p/ss/aa", contentRoot)
        assertTrue(contentRoot.length <= 56)

        val hollowKnight = ProfileBuildPaths(
            filesDir,
            externalFilesDir,
            GameProfiles.require("hollow-knight"),
        )
        assertFalse(paths.packageDir.toPath().startsWith(hollowKnight.profilePaths.root.toPath()))
        assertFalse(paths.buildRoot.toPath().startsWith(hollowKnight.externalRoot.toPath()))
    }

    @Test
    fun `classic player profile does not allocate an Addressables content root`() {
        val root = File("build/test-classic-content-root").absoluteFile
        val paths = ProfileBuildPaths(
            File(root, "internal"),
            File(root, "external"),
            GameProfiles.require("hollow-knight"),
        )

        assertEquals(
            null,
            PlayerImage.contentRootForProfile(
                "io.github.darkaxt.dualsouls.hkpoc",
                paths,
            ),
        )
    }

    @Test
    fun `Addressables profile retains the catalog content-root limit`() {
        val root = File("build/test-addressables-content-root").absoluteFile
        val paths = ProfileBuildPaths(
            File(root, "internal"),
            File(root, "external"),
            GameProfiles.require("silksong"),
        )

        assertThrows(IllegalArgumentException::class.java) {
            PlayerImage.contentRootForProfile(
                "io.github.darkaxt.dualsouls.hkpoc",
                paths,
            )
        }
    }

    @Test
    fun `legacy generated state is adopted without moving the source depot or shared tools`() {
        val root = File("build/test-silksong-regression/adoption").absoluteFile
        root.deleteRecursively()
        val filesDir = File(root, "internal").apply { mkdirs() }
        val externalFilesDir = File(root, "external").apply { mkdirs() }
        val paths = ProfileBuildPaths(filesDir, externalFilesDir, GameProfiles.require("silksong"))

        File(filesDir, "pkg/.built").writeFixture("ready")
        File(externalFilesDir, "build/image.stamp").writeFixture("image")
        File(externalFilesDir, "staging/libunity.so").writeFixture("engine")
        val depot = File(externalFilesDir, "depot").apply { mkdirs() }
        File(depot, "source-marker").writeText("owned by the user")
        File(externalFilesDir, "depot-path.txt").writeText(depot.absolutePath)
        File(externalFilesDir, "content-path.txt").writeText(File(depot, "content").absolutePath)
        File(filesDir, "toolchain/verified").writeFixture("shared")
        File(externalFilesDir, "unity/editor/marker").writeFixture("shared")

        val report = LegacySilksongAdopter.adopt(filesDir, externalFilesDir, paths)

        assertEquals(setOf("pkg", "build", "staging"), report.moved)
        assertTrue(report.conflicts.isEmpty())
        assertEquals("ready", File(paths.packageDir, ".built").readText())
        assertEquals("image", File(paths.buildRoot, "image.stamp").readText())
        assertEquals("engine", File(paths.installStaging, "libunity.so").readText())
        assertEquals(depot.absolutePath, paths.profilePaths.sourcePointer.readText())
        assertEquals(File(depot, "content").absolutePath, paths.contentPointer.readText())
        assertEquals("owned by the user", File(depot, "source-marker").readText())
        assertEquals("shared", File(filesDir, "toolchain/verified").readText())
        assertEquals("shared", File(externalFilesDir, "unity/editor/marker").readText())
        assertFalse(File(filesDir, "pkg").exists())
        assertFalse(File(externalFilesDir, "build").exists())
    }

    @Test
    fun `adoption never overwrites an existing profile tree`() {
        val root = File("build/test-silksong-regression/conflict").absoluteFile
        root.deleteRecursively()
        val filesDir = File(root, "internal").apply { mkdirs() }
        val externalFilesDir = File(root, "external").apply { mkdirs() }
        val paths = ProfileBuildPaths(filesDir, externalFilesDir, GameProfiles.require("silksong"))
        File(filesDir, "pkg/legacy").writeFixture("legacy")
        File(paths.packageDir, "current").writeFixture("current")

        val report = LegacySilksongAdopter.adopt(filesDir, externalFilesDir, paths)

        assertEquals(setOf("pkg"), report.conflicts)
        assertEquals("legacy", File(filesDir, "pkg/legacy").readText())
        assertEquals("current", File(paths.packageDir, "current").readText())

        BuildReset.clearGenerated(paths)
        val retry = LegacySilksongAdopter.adopt(filesDir, externalFilesDir, paths)

        assertTrue(retry.moved.isEmpty())
        assertTrue(retry.conflicts.isEmpty())
        assertEquals("legacy", File(filesDir, "pkg/legacy").readText())
        assertFalse(paths.packageDir.exists())
    }

    @Test
    fun `legacy app downloaded depot remains discoverable without a pointer`() {
        val root = File("build/test-silksong-regression/legacy-depot").absoluteFile
        root.deleteRecursively()
        val filesDir = File(root, "internal").apply { mkdirs() }
        val externalFilesDir = File(root, "external").apply { mkdirs() }
        val profile = GameProfiles.require("silksong")
        val paths = ProfileBuildPaths(filesDir, externalFilesDir, profile)
        val depot = File(externalFilesDir, "depot")
        File(depot, "${profile.dataDirectoryName}/globalgamemanagers").writeFixture("fixture")
        File(depot, profile.executableNames.single()).writeFixture("fixture")
        File(depot, ".download-complete").writeFixture(profile.steamDepotId.toString())

        LegacySilksongAdopter.adopt(filesDir, externalFilesDir, paths)

        assertEquals(depot.absoluteFile, DepotLocation.picked(paths))
        assertTrue(depot.isDirectory)
        assertFalse(paths.downloadDepot.exists())
    }

    @Test
    fun `current fetchers resolve exact profile descriptors`() {
        val root = File("build/test-silksong-regression/fetchers").absoluteFile
        root.deleteRecursively()
        val externalFilesDir = File(root, "external").apply { mkdirs() }
        val depot = File(root, "depot").apply { mkdirs() }
        val silksong = GameProfiles.require("silksong")
        val hollowKnight = GameProfiles.require("hollow-knight")
        File(depot, "${silksong.dataDirectoryName}/globalgamemanagers").writeFixture("fixture")
        File(depot, "${silksong.executableNames.single()}").writeText("fixture")
        File(depot, ".download-complete").writeText(silksong.steamDepotId.toString())

        assertTrue(DepotFetcher.isPresent(silksong, depot))
        assertFalse(DepotFetcher.isPresent(hollowKnight, depot))
        assertEquals(
            UnityToolchainRegistry.rootFor(
                externalFilesDir,
                UnityToolchainRegistry.resolve(silksong),
            ),
            UnityFetcher.rootFor(externalFilesDir, silksong),
        )
        assertTrue(UnityFetcher.rootFor(externalFilesDir, hollowKnight) != UnityFetcher.rootFor(externalFilesDir, silksong))
        assertEquals(
            File(depot, "${silksong.dataDirectoryName}/StreamingAssets/aa"),
            PlayerImage.addressablesContainer(
                silksong,
                File(depot, silksong.dataDirectoryName),
            ),
        )
        assertThrows(IllegalArgumentException::class.java) {
            PlayerImage.addressablesContainer(
                hollowKnight,
                File(depot, hollowKnight.dataDirectoryName),
            )
        }
    }

    @Test
    fun `selected source pointer belongs to the Silksong profile`() {
        val root = File("build/test-silksong-regression/source-pointer").absoluteFile
        root.deleteRecursively()
        val paths = ProfileBuildPaths(
            File(root, "internal").apply { mkdirs() },
            File(root, "external").apply { mkdirs() },
            GameProfiles.require("silksong"),
        )
        val selected = File(root, "user-selected-source")

        DepotLocation.remember(paths, selected)

        assertEquals(selected.absoluteFile, DepotLocation.picked(paths))
        assertEquals(paths.downloadDepot, DepotLocation.downloadTarget(paths))
        assertTrue(paths.profilePaths.sourcePointer.isFile)

        DepotLocation.forget(paths)
        assertFalse(paths.profilePaths.sourcePointer.exists())
        assertFalse(paths.contentPointer.exists())
    }

    @Test
    fun `Unity shell consumes the selected profile namespace`() {
        var root = File(requireNotNull(System.getProperty("user.dir"))).absoluteFile
        while (!File(root, "tools/depot-to-apk/shell/GameActivity.java").isFile) {
            root = root.parentFile ?: error("Could not locate repository root")
        }
        val source = File(root, "tools/depot-to-apk/shell/GameActivity.java").readText()

        assertTrue(source.contains("dev.silksong.launcher.PROFILE_ID"))
        assertTrue(source.contains("!id.equals(\"silksong\") && !id.equals(\"hollow-knight\")"))
        assertTrue(source.contains("if (profileId().equals(\"hollow-knight\")) return \"hk\""))
        assertTrue(source.contains("new java.io.File(getFilesDir(), \"profiles/\" + profileId())"))
        assertTrue(source.contains("new java.io.File(getFilesDir(), \"p/\" + profileRuntimeKey() + \"/aa\")"))
        assertTrue(source.contains("new java.io.File(ext, \"profiles/\" + profileId())"))
        assertTrue(source.contains("new java.io.File(profileFilesDir(), \"current\")"))
        assertTrue(source.contains("new java.io.File(profileFilesDir(), \"generations\")"))
        assertTrue(source.contains("@Override public java.io.File getObbDir()"))
        assertTrue(source.contains("@Override public java.io.File[] getObbDirs()"))
        assertTrue(source.contains("return profilePackageDir()"))
        assertTrue(source.contains("return legacyPackageDir()"))
        assertTrue(source.contains("throw new IllegalStateException(\"current generation is invalid"))
        assertTrue(source.contains("new java.io.File(logs, GAME_LOG + \".prev\")"))
        assertTrue(source.contains("new java.io.File(logs, ERROR_LOG + \".prev\")"))
        assertFalse(source.contains("new java.io.File(ext, ERROR_LOG + \".prev\")"))
        assertFalse(source.contains("new java.io.File(getFilesDir(), \"pkg/"))
        assertFalse(source.contains("new java.io.File(getFilesDir(), \"aa\")"))
    }

    @Test
    fun `launcher reads game logs from the selected profile namespace`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val paths = ProfileBuildPaths(
            context.filesDir,
            requireNotNull(context.getExternalFilesDir(null)),
            GameProfiles.require("silksong"),
        )
        val logs = File(paths.externalRoot, "logs")

        assertEquals(
            listOf(
                File(logs, "errors.log.prev"),
                File(logs, "errors.log"),
                File(logs, "game.log.prev"),
                File(logs, "game.log"),
                File(context.getExternalFilesDir(null), "errors.log.prev"),
                File(context.getExternalFilesDir(null), "errors.log"),
                File(context.getExternalFilesDir(null), "game.log.prev"),
                File(context.getExternalFilesDir(null), "game.log"),
            ),
            LauncherLog.gameLogs(context),
        )
    }

    @Test
    fun `profile reset removes generated state and preserves its source`() {
        val root = File("build/test-silksong-regression/reset").absoluteFile
        root.deleteRecursively()
        val paths = ProfileBuildPaths(
            File(root, "internal").apply { mkdirs() },
            File(root, "external").apply { mkdirs() },
            GameProfiles.require("silksong"),
        )
        val otherPaths = ProfileBuildPaths(
            File(root, "internal"),
            File(root, "external"),
            GameProfiles.require("hollow-knight"),
        )
        File(paths.packageDir, ".built").writeFixture("generated")
        File(paths.buildRoot, "image.stamp").writeFixture("generated")
        File(paths.installStaging, "libunity.so").writeFixture("generated")
        File(paths.depotStaging, "chunk").writeFixture("generated")
        File(paths.contentLink, "placeholder").writeFixture("generated")
        File(paths.downloadDepot, "source-marker").writeFixture("source")
        paths.profilePaths.sourcePointer.writeFixture(paths.downloadDepot.absolutePath)
        File(otherPaths.packageDir, ".built").writeFixture("other-profile")
        File(otherPaths.buildRoot, "image.stamp").writeFixture("other-profile")
        File(root, "internal/toolchain/verified").writeFixture("shared")
        File(root, "external/unity/editor/marker").writeFixture("shared")

        BuildReset.clearGenerated(paths)

        assertFalse(paths.packageDir.exists())
        assertFalse(paths.buildRoot.exists())
        assertFalse(paths.installStaging.exists())
        assertFalse(paths.depotStaging.exists())
        assertFalse(paths.contentLink.exists())
        assertEquals("source", File(paths.downloadDepot, "source-marker").readText())
        assertEquals(paths.downloadDepot.absolutePath, paths.profilePaths.sourcePointer.readText())
        assertEquals("other-profile", File(otherPaths.packageDir, ".built").readText())
        assertEquals("other-profile", File(otherPaths.buildRoot, "image.stamp").readText())
        assertEquals("shared", File(root, "internal/toolchain/verified").readText())
        assertEquals("shared", File(root, "external/unity/editor/marker").readText())
    }
}
