package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.PackageCompiler
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.DepotLocation
import dev.silksong.launcher.Il2cppConverter
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.json.JSONObject
import org.robolectric.RobolectricTestRunner
import java.io.File
import java.security.MessageDigest
import java.util.Properties
import java.util.zip.ZipFile

@RunWith(RobolectricTestRunner::class)
class HollowKnightBuildPlanTest {

    @get:Rule
    val temp = TemporaryFolder()

    private val hollowKnight = GameProfiles.require("hollow-knight")
    private val silksong = GameProfiles.require("silksong")

    @Test
    fun `patch artifacts and sources are selected by profile`() {
        assertEquals("HollowKnightPatches.dll", PackageCompiler.patchAssemblyName(hollowKnight))
        assertEquals("ondevice/hollow-knight-patches", PackageCompiler.patchAssetPath(hollowKnight))

        assertEquals("SilksongPatches.dll", PackageCompiler.patchAssemblyName(silksong))
        assertEquals("ondevice/silksong-patches", PackageCompiler.patchAssetPath(silksong))
    }

    @Test
    fun `save rewrite remains a Silksong-only build step`() {
        assertFalse(PackageCompiler.requiresSaveIo(hollowKnight))
        assertTrue(PackageCompiler.requiresSaveIo(silksong))
    }

    @Test
    fun `production package manifest binds profile toolchain depot and assembly digests`() {
        val root = temp.newFolder("package-manifest-root")
        val unity = temp.newFolder("package-manifest-unity")
        val bcl = File(unity, "editor/Editor/Data/MonoBleedingEdge/lib/mono/unityaot-linux/mscorlib.dll")
            .apply { requireNotNull(parentFile).mkdirs(); writeText("bcl") }
        val core = File(unity, "android/Variations/il2cpp/Managed/UnityEngine.CoreModule.dll")
            .apply { requireNotNull(parentFile).mkdirs(); writeText("core") }
        val depot = temp.newFolder("package-manifest-depot")
        val data = File(depot, "hollow_knight_Data").apply { mkdirs() }
        File(data, "globalgamemanagers").writeText("player")
        val game = File(data, "Managed/Assembly-CSharp.dll")
            .apply { requireNotNull(parentFile).mkdirs(); writeText("game") }
        for (name in listOf(
            "Unity.InputSystem.dll",
            "HollowKnightPatches.dll",
            "0Harmony.dll",
            "BepInEx.dll",
        )) {
            File(PackageCompiler.outputDir(root), name).apply {
                requireNotNull(parentFile).mkdirs()
                writeText(name)
            }
        }

        val manifest = PackageCompiler.publishAssemblyManifest(hollowKnight, unity, depot, root)
        val values = Properties().apply { manifest.reader().use(::load) }

        assertEquals("1", values.getProperty("schema"))
        assertEquals(hollowKnight.id, values.getProperty("profile"))
        assertEquals(hollowKnight.unityVersion, values.getProperty("unityVersion"))
        assertEquals("4.12.0", values.getProperty("roslynVersion"))
        assertEquals(hollowKnight.steamDepotId.toString(), values.getProperty("steamDepotId"))
        assertEquals(hollowKnight.currentGameVersion, values.getProperty("gameVersion"))
        assertEquals(sha256(bcl), values.getProperty("unityMscorlibSha256"))
        assertEquals(sha256(core), values.getProperty("androidCoreModuleSha256"))
        assertEquals(sha256(game), values.getProperty("depotAssemblySha256"))
        assertEquals(
            sha256(File(PackageCompiler.outputDir(root), "HollowKnightPatches.dll")),
            values.getProperty("assembly.HollowKnightPatches.dll"),
        )
        assertFalse(values.containsKey("assembly.SilksongPatches.dll"))
        assertFalse(File(manifest.parentFile, "${manifest.name}.part").exists())
        assertTrue(PackageCompiler.isPresent(hollowKnight, unity, depot, root))

        File(PackageCompiler.outputDir(root), "Unity.InputSystem.dll").writeText("stale")
        assertFalse(PackageCompiler.isPresent(hollowKnight, unity, depot, root))
        assertFalse(PackageCompiler.isPresent(silksong, unity, depot, root))
    }

    @Test
    fun `classic conversion reads its source without requiring in-place writes`() {
        assertFalse(DepotLocation.requiresWritableContent(hollowKnight))
        assertTrue(DepotLocation.requiresWritableContent(silksong))
    }

    @Test
    fun `interrupted il2cpp output is never treated as a completed conversion`() {
        val root = temp.newFolder("interrupted-conversion")
        File(Il2cppConverter.cppDir(root), "partial.cpp").apply {
            parentFile.mkdirs()
            writeText("// converter was killed after writing only part of the tree")
        }
        Il2cppConverter.metadata(root).apply {
            parentFile.mkdirs()
            writeBytes(byteArrayOf(1))
        }

        Il2cppConverter.markComplete(root)
        assertTrue(Il2cppConverter.isPresent(root))
        assertTrue(Il2cppConverter.isComplete(root))

        val unexpected = File(Il2cppConverter.cppDir(root), "unexpected.cpp").apply {
            writeText("// tree changed after completion")
        }
        assertFalse(Il2cppConverter.isPresent(root))
        assertTrue(unexpected.delete())
        assertTrue(Il2cppConverter.isPresent(root))

        Il2cppConverter.metadata(root).writeBytes(byteArrayOf(1, 2))
        assertFalse(Il2cppConverter.isComplete(root))
        Il2cppConverter.metadata(root).writeBytes(byteArrayOf(1))
        assertTrue(Il2cppConverter.isComplete(root))

        Il2cppConverter.invalidateCompletion(root)
        File(Il2cppConverter.cppDir(root), "partial.cpp").writeText("// overwritten partial tree")
        assertFalse(Il2cppConverter.isPresent(root))

        File(root, "${Il2cppConverter.completionMarker(root).name}.part").writeText("complete")
        assertFalse(Il2cppConverter.isPresent(root))

        Il2cppConverter.markComplete(root)
        assertTrue(Il2cppConverter.isPresent(root))
        assertFalse(File(root, "${Il2cppConverter.completionMarker(root).name}.part").exists())
    }

    @Test
    fun `conversion aborts when an old completion marker cannot be invalidated`() {
        val root = temp.newFolder("failed-invalidation")
        val marker = Il2cppConverter.completionMarker(root).apply {
            mkdirs()
            File(this, "held").writeText("prevents deletion")
        }

        val failure = assertThrows(java.io.IOException::class.java) {
            Il2cppConverter.invalidateCompletion(root)
        }

        assertTrue(failure.message.orEmpty().contains("invalidate"))
        assertTrue(marker.isDirectory)
    }

    @Test
    fun `source markers distinguish copied classic data from live addressables`() {
        val classicSource = temp.newFolder("classic-source")
        val addressablesSource = temp.newFolder("addressables-source")

        DepotLocation.writeMarker(hollowKnight, classicSource)
        DepotLocation.writeMarker(silksong, addressablesSource)

        val classicText = requireNotNull(classicSource.listFiles()?.single()).readText()
        val addressablesText = requireNotNull(addressablesSource.listFiles()?.single()).readText()
        assertTrue(classicText.contains("build and repair"))
        assertFalse(classicText.contains("every time it runs"))
        assertTrue(addressablesText.contains("every time it runs"))
    }

    @Test
    fun `classic image keeps game data but rejects desktop managed runtimes`() {
        assertTrue(PlayerImage.shouldCopyClassicRelativePath("globalgamemanagers"))
        assertTrue(PlayerImage.shouldCopyClassicRelativePath("StreamingAssets/media/intro.mp4"))
        assertTrue(PlayerImage.shouldCopyClassicRelativePath("level0.resS"))

        assertFalse(PlayerImage.shouldCopyClassicRelativePath("Managed/Assembly-CSharp.dll"))
        assertFalse(PlayerImage.shouldCopyClassicRelativePath("MonoBleedingEdge/etc/mono/config"))
        assertFalse(PlayerImage.shouldCopyClassicRelativePath("Resources/unity default resources"))
        assertFalse(PlayerImage.shouldCopyClassicRelativePath("Resources/unity_builtin_extra"))
    }

    @Test
    fun `classic runtime registration names the Hollow Knight patch assembly`() {
        val root = temp.newFolder("build")
        val image = temp.newFolder("image")
        PackageCompiler.patchAssembly(hollowKnight, root).apply {
            requireNotNull(parentFile).mkdirs()
            writeBytes(byteArrayOf(1))
            File(Il2cppConverter.asmDir(root), name).also { staged ->
                requireNotNull(staged.parentFile).mkdirs()
                copyTo(staged)
            }
        }
        File(root, "patches").mkdirs()
        File(root, "patches/entrypoints.json").writeText(
            """{"entryPoints":[{"nameSpace":"HollowKnightPatches","className":"InjectionProbe","methodName":"Start","loadTypes":2}]}""",
        )
        for (name in listOf("0Harmony.dll", "BepInEx.dll")) {
            File(PackageCompiler.outputDir(root), name).writeBytes(byteArrayOf(1))
            File(Il2cppConverter.asmDir(root), name).writeBytes(byteArrayOf(1))
        }
        File(image, "ScriptingAssemblies.json").writeText("""{"names":[],"types":[]}""")
        File(image, "RuntimeInitializeOnLoads.json").writeText("""{"root":[]}""")

        PlayerImage.registerPatches(hollowKnight, image, root)

        val assemblies = JSONObject(File(image, "ScriptingAssemblies.json").readText())
        assertEquals("HollowKnightPatches.dll", assemblies.getJSONArray("names").getString(0))
        val loads = JSONObject(File(image, "RuntimeInitializeOnLoads.json").readText())
        assertEquals("HollowKnightPatches", loads.getJSONArray("root").getJSONObject(0).getString("assemblyName"))
        assertEquals(
            setOf("HollowKnightPatches.dll", "0Harmony.dll", "BepInEx.dll"),
            (0 until assemblies.getJSONArray("names").length())
                .map { assemblies.getJSONArray("names").getString(it) }
                .toSet(),
        )
        assertTrue(
            (0 until loads.getJSONArray("root").length())
                .map { loads.getJSONArray("root").getJSONObject(it).getString("assemblyName") }
                .contains("BepInEx"),
        )
    }

    @Test
    fun `classic package replaces an existing player image`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val root = temp.newFolder("repack")
        val image = File(root, "image").apply { mkdirs() }
        File(image, "globalgamemanagers").writeText("fresh")
        File(image, "unity_app_guid").writeText("11111111-2222-3333-4444-555555555555")
        File(image, "sharedassets1.assets.resS").writeText("first-scene-sidecar")
        File(image, "level1").writeText("later-scene")
        File(image, "StreamingAssets/media/intro.mp4").apply {
            parentFile.mkdirs()
            writeText("streamed-media")
        }
        val pkg = File(root, "pkg").apply { mkdirs() }
        File(pkg, "data.apk").writeText("stale")
        val paths = ProfileBuildPaths(File(root, "files"), File(root, "external"), hollowKnight)

        PlayerImage.install(context, root, pkg, paths, File(root, "unused-classic-depot"))

        ZipFile(File(pkg, "data.apk")).use { zip ->
            assertEquals("fresh", zip.getInputStream(zip.getEntry("assets/bin/Data/globalgamemanagers")).reader().readText())
            assertTrue(zip.getEntry("assets/bin/Data/sharedassets1.assets.resS") != null)
            assertTrue(zip.getEntry("assets/bin/Data/unity_app_guid") != null)
            assertTrue(zip.getEntry("assets/unity_obb_guid") != null)
            assertTrue(zip.getEntry("assets/bin/Data/level1") == null)
            assertTrue(zip.getEntry("assets/media/intro.mp4") == null)
        }
        val obb = File(pkg, PlayerImage.mainObbName(context))
        assertTrue(obb.isFile)
        ZipFile(obb).use { zip ->
            assertTrue(zip.getEntry("assets/bin/Data/globalgamemanagers") == null)
            assertEquals("later-scene", zip.getInputStream(zip.getEntry("assets/bin/Data/level1")).reader().readText())
            assertEquals("streamed-media", zip.getInputStream(zip.getEntry("assets/media/intro.mp4")).reader().readText())
            assertTrue(zip.getEntry("unity_obb_guid") != null)
            val apkGuid = ZipFile(File(pkg, "data.apk")).use { apk ->
                apk.getInputStream(apk.getEntry("assets/unity_obb_guid")).reader().readText()
            }
            assertEquals(apkGuid, zip.getInputStream(zip.getEntry("unity_obb_guid")).reader().readText())
        }
        assertFalse(File(pkg, "data.apk.part").exists())
        assertFalse(File(pkg, "${PlayerImage.mainObbName(context)}.part").exists())
    }

    @Test
    fun `Unity first scene rule keeps required classic resources in the APK`() {
        for (path in listOf(
            "globalgamemanagers",
            "globalgamemanagers.assets",
            "unity_app_guid",
            "sharedassets0.assets",
            "sharedassets447.assets.resS",
            "level0",
            "Managed/Metadata/global-metadata.dat",
            "Resources/unity default resources",
            "Resources/unity_builtin_extra",
            "RuntimeInitializeOnLoads.json",
            "ScriptingAssemblies.json",
        )) {
            assertTrue(path, PlayerImage.isFirstSceneResource(path))
        }
        for (path in listOf(
            "level1",
            "resources.assets",
            "sharedassets83.resource",
            "StreamingAssets/media/intro.mp4",
        )) {
            assertFalse(path, PlayerImage.isFirstSceneResource(path))
        }
    }

    private fun sha256(file: File): String =
        MessageDigest.getInstance("SHA-256").digest(file.readBytes())
            .joinToString("") { "%02x".format(it) }
}
