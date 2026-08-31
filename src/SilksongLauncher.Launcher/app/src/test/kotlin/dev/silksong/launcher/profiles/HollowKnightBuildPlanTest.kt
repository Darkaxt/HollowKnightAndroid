package dev.silksong.launcher.profiles

import dev.silksong.launcher.PackageCompiler
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.DepotLocation
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.json.JSONObject
import org.robolectric.RobolectricTestRunner
import java.io.File
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
    fun `classic conversion reads its source without requiring in-place writes`() {
        assertFalse(DepotLocation.requiresWritableContent(hollowKnight))
        assertTrue(DepotLocation.requiresWritableContent(silksong))
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
        }
        File(root, "patches").mkdirs()
        File(root, "patches/entrypoints.json").writeText(
            """{"entryPoints":[{"nameSpace":"HollowKnightPatches","className":"InjectionProbe","methodName":"Start","loadTypes":2}]}""",
        )
        File(image, "ScriptingAssemblies.json").writeText("""{"names":[],"types":[]}""")
        File(image, "RuntimeInitializeOnLoads.json").writeText("""{"root":[]}""")

        PlayerImage.registerPatches(hollowKnight, image, root)

        val assemblies = JSONObject(File(image, "ScriptingAssemblies.json").readText())
        assertEquals("HollowKnightPatches.dll", assemblies.getJSONArray("names").getString(0))
        val loads = JSONObject(File(image, "RuntimeInitializeOnLoads.json").readText())
        assertEquals("HollowKnightPatches", loads.getJSONArray("root").getJSONObject(0).getString("assemblyName"))
    }

    @Test
    fun `classic package replaces an existing player image`() {
        val root = temp.newFolder("repack")
        val image = File(root, "image").apply { mkdirs() }
        File(image, "globalgamemanagers").writeText("fresh")
        val pkg = File(root, "pkg").apply { mkdirs() }
        File(pkg, "data.apk").writeText("stale")
        val paths = ProfileBuildPaths(File(root, "files"), File(root, "external"), hollowKnight)

        PlayerImage.install(root, pkg, paths, File(root, "unused-classic-depot"))

        ZipFile(File(pkg, "data.apk")).use { zip ->
            assertEquals("fresh", zip.getInputStream(zip.getEntry("assets/bin/Data/globalgamemanagers")).reader().readText())
        }
        assertFalse(File(pkg, "data.apk.part").exists())
    }
}
