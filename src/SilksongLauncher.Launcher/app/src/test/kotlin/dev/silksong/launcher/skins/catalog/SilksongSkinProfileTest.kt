package dev.silksong.launcher.skins.catalog

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.library.SkinLibraryImporter
import dev.silksong.launcher.skins.library.SkinLibraryCodec
import dev.silksong.launcher.skins.library.SkinLibraryDocument
import dev.silksong.launcher.skins.library.SkinLibraryStore
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.Rule
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayInputStream
import java.io.File

class SilksongSkinProfileTest {
    @get:Rule val temp = TemporaryFolder()
    private val expected = listOf(
        "Assets/Collections/Hornet Cln Data/atlas0.png",
        "Assets/Collections/Hornet Cloakless Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Dagger Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Drill Lance Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Scythe Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Shaman Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Warrior Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Whip Cln Data/atlas0.png",
        "Assets/Collections/Hornet_Needolin_Windy Data/atlas0.png",
        "Assets/Collections/HUD Cln Data/atlas0.png",
        "Assets/Collections/HUD Extras Cln Data/atlas0.png",
    )

    @Test
    fun closedProfilesExposeIndependentExactCatalogAuthorities() {
        val hk = SkinCatalogProfiles.require(GameProfiles.require("hollow-knight"))
        val ss = SkinCatalogProfiles.require(GameProfiles.require("silksong"))

        assertEquals("hollow-knight", hk.profileId)
        assertEquals(205, hk.pathCount)
        assertEquals("silksong", ss.profileId)
        assertEquals("1.0.29980", ss.gameVersion)
        assertEquals(11, ss.pathCount)
        assertEquals(expected, ss.paths)
        assertNotEquals(hk.assetName, ss.assetName)
        assertNotEquals(hk.catalogId, ss.catalogId)
        assertNotEquals(hk.sha256, ss.sha256)
    }

    @Test
    fun silksongCatalogRequiresExactPinnedBytesAndCannotBecomeHollowKnightAuthority() {
        val bytes = (expected.joinToString("\n") + "\n").toByteArray(Charsets.UTF_8)
        val loaded = SkinCatalogPaths.load(
            SkinCatalogProfiles.require(GameProfiles.require("silksong")),
            ByteArrayInputStream(bytes),
        )
        assertTrue(loaded is SkinResult.Ok)
        val catalog = when (loaded) {
            is SkinResult.Ok -> loaded.value
            is SkinResult.Error -> error(loaded.detail)
        }
        assertEquals("silksong", catalog.profile.profileId)
        assertEquals(expected, catalog.paths)
        assertFalse(runCatching { CatalogPathSet.requirePinned("hollow-knight") }.getOrNull() === catalog)
    }

    @Test
    fun foreignAndInventedProfilesAreRejectedClosed() {
        val fake = GameProfiles.require("silksong").copy(id = "silksong-copy")
        val failure = runCatching { SkinCatalogProfiles.require(fake) }
        assertTrue(failure.isFailure)
    }

    @Test
    fun libraryCodecBindsDocumentsToOneNamedProfile() {
        val document = SkinLibraryDocument()
        val ss = SkinLibraryCodec.encode(document, "silksong")
        val hk = SkinLibraryCodec.encode(document, "hollow-knight")

        assertNotEquals(String(hk), String(ss))
        assertEquals(document, SkinLibraryCodec.decode(ss, "silksong"))
        assertTrue(runCatching { SkinLibraryCodec.decode(hk, "silksong") }.isFailure)
        assertTrue(runCatching { SkinLibraryCodec.decode(ss, "hollow-knight") }.isFailure)
    }

    @Test
    fun silksongLibraryNeverReadsOrMigratesHollowKnightLegacyRegistry() {
        val profileRoot = temp.newFolder("profiles", "silksong")
        val catalog = silksongCatalog()
        val legacy = File(profileRoot, "skins/registry/current.json")
        legacy.parentFile.mkdirs()
        legacy.writeText("hollow-knight legacy sentinel")

        val store = SkinLibraryStore(SkinPaths(profileRoot), catalog = catalog)
        val read = store.read()

        assertTrue(read is SkinResult.Ok)
        assertEquals("silksong", store.profileId)
        assertEquals("hollow-knight legacy sentinel", legacy.readText())
        assertTrue(File(profileRoot, "skins/library.json").isFile)
    }

    @Test
    fun silksongImportPublishesOnlyCanonicalSilksongTargetsAndReportsOmissions() {
        val profileRoot = temp.newFolder("imports", "profiles", "silksong")
        val store = SkinLibraryStore(SkinPaths(profileRoot), catalog = silksongCatalog())
        val decoder = PngDecoder { _, info -> SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height)) }
        val archive = RawZipFixture.build(listOf(
            RawZipFixture.Entry("Scarlet/${expected[0]}".toByteArray(), TinyPngFixture.rgba()),
            RawZipFixture.Entry("Scarlet/Knight.png".toByteArray(), TinyPngFixture.rgba()),
        )).bytes
        val importer = SkinLibraryImporter(store, decoder)

        val prepared = importer.prepare(SkinImportInput.SelectedFile("scarlet.zip") { archive.inputStream() })
        assertTrue(prepared is SkinResult.Ok)
        val handle = when (prepared) {
            is SkinResult.Ok -> prepared.value
            is SkinResult.Error -> error(prepared.detail)
        }
        assertTrue(handle.candidates.toString(), handle.candidates.any { it.candidateKey != null })
        assertTrue(importer.commitImport(handle.handleId) is SkinResult.Ok)
        val pack = store.read().let { result -> when (result) {
            is SkinResult.Ok -> result.value.packs.single()
            is SkinResult.Error -> error(result.detail)
        } }
        val manifest = store.requireVerified(pack)
        val textures = manifest.games.getValue("silksong").textures

        assertEquals(setOf(expected[0]), textures.keys)
        assertFalse(textures.keys.any { it.endsWith("Knight.png") && !it.contains("Hornet") })
        assertEquals(10, expected.count { it !in textures })
    }

    private fun silksongCatalog(): CatalogPathSet {
        val bytes = (expected.joinToString("\n") + "\n").toByteArray(Charsets.UTF_8)
        return when (val loaded = SkinCatalogPaths.load(SkinCatalogProfiles.Silksong, ByteArrayInputStream(bytes))) {
            is SkinResult.Ok -> loaded.value
            is SkinResult.Error -> error(loaded.detail)
        }
    }
}
