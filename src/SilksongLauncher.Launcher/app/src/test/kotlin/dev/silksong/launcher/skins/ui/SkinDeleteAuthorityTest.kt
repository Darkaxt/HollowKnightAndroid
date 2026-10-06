package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.catalog.SkinCatalogPaths
import dev.silksong.launcher.skins.catalog.SkinCatalogProfiles
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.library.LibraryMode
import dev.silksong.launcher.skins.library.SkinLibraryStore
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.junit.runners.Parameterized
import java.io.File

@RunWith(Parameterized::class)
class SkinDeleteAuthorityTest(private val profileId: String) {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `stale configuration cannot authorize Delete even when pack content is unchanged`() {
        val rig = Rig()
        val target = rig.install("First")
        assertTrue(rig.store.setEligibility(target.id, true) is SkinResult.Ok)
        rig.assertRejectedWithoutMutation(target)
    }

    @Test fun `confirmed Delete cannot remove a later replacement with the same pack id`() {
        val rig = Rig()
        val target = rig.install("First")
        val handle = rig.prepare("Replacement", 2)
        val replaced = rig.services.imports.commitReplace(SkinReplaceRequest(handle.handleId,
            requireNotNull(handle.candidates.single().candidateKey), target))
        assertTrue(replaced.toString(), replaced is SkinResult.Ok)
        val current = rig.target()
        assertEquals(target.id, current.id)
        assertNotEquals(target.treeSha256, current.treeSha256)
        assertNotEquals(target.receiptSha256, current.receiptSha256)
        rig.assertRejectedWithoutMutation(target)
    }

    @Test fun `current generation alone cannot authorize Delete of another tree`() {
        val rig = Rig()
        val target = rig.install("First")
        rig.assertRejectedWithoutMutation(target.copy(treeSha256 = "0".repeat(64)))
    }

    @Test fun `current generation and tree cannot authorize Delete with another receipt`() {
        val rig = Rig()
        val target = rig.install("First")
        rig.assertRejectedWithoutMutation(target.copy(receiptSha256 = "0".repeat(64)))
    }

    @Test fun `fresh OFF Delete removes membership but retains immutable object and receipt bytes`() {
        val rig = Rig()
        val target = rig.install("First")
        val retained = rig.immutableBytes()
        assertTrue(rig.services.mutations.remove(target) is SkinResult.Ok)
        assertTrue(rig.view().packs.isEmpty())
        assertEquals(retained, rig.immutableBytes())
    }

    @Test fun `fresh selected ON Delete remains blocked`() {
        val rig = Rig()
        val target = rig.install("First")
        assertTrue(rig.store.confirmPack(target.generationSha256, target.id) is SkinResult.Ok)
        assertTrue(rig.store.setMode(rig.target().generationSha256, LibraryMode.ON) is SkinResult.Ok)
        rig.assertRejectedWithoutMutation(rig.target())
    }

    @Test fun `fresh pending ROTATE successor Delete remains blocked`() {
        val rig = Rig()
        val first = rig.install("First")
        val second = rig.install("Second", 2)
        assertTrue(rig.store.confirmPack(second.generationSha256, first.id) is SkinResult.Ok)
        assertTrue(rig.store.setEligibility(first.id, true) is SkinResult.Ok)
        assertTrue(rig.store.setEligibility(second.id, true) is SkinResult.Ok)
        assertTrue(rig.store.setMode(rig.target(first.id).generationSha256, LibraryMode.ROTATE) is SkinResult.Ok)
        val started = (rig.store.startRuntime() as SkinResult.Ok).value
        val pending = (rig.store.confirmDeath(requireNotNull(started.rotationRun), 1) as SkinResult.Ok).value
        assertEquals(second.id, pending.pendingPackId)
        rig.assertRejectedWithoutMutation(rig.target(second.id))
    }

    private inner class Rig {
        private val profile = if (profileId == "hollow-knight") SkinCatalogProfiles.HollowKnight else SkinCatalogProfiles.Silksong
        private val catalogFile = listOf(
            File("../../../docs/superpowers/specs/data/${profile.assetName}"),
            File("../../docs/superpowers/specs/data/${profile.assetName}"),
            File("docs/superpowers/specs/data/${profile.assetName}"),
        ).map(File::getAbsoluteFile).first(File::isFile)
        private val catalog = (catalogFile.inputStream().use { SkinCatalogPaths.load(profile, it) } as SkinResult.Ok).value
        val store = SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/$profileId").apply { mkdirs() }), catalog = catalog)
        val services = SkinLibraryUiServices.bound(store)

        fun prepare(name: String, width: Int = 1): dev.silksong.launcher.skins.registry.SkinPreparationHandle {
            val sheet = if (profileId == "hollow-knight") "Knight.png" else if (width == 1)
                "Assets/Collections/Hornet Cln Data/atlas0.png" else "Assets/Collections/HUD Extras Cln Data/atlas0.png"
            val dimensions = profile.textureDimensions[sheet]
            val png = TinyPngFixture.rgba(dimensions?.width ?: width, dimensions?.height ?: 1)
            val archive = RawZipFixture.build(listOf(RawZipFixture.Entry("$name/$sheet".toByteArray(), png))).bytes
            val result = services.imports.prepare(SkinImportInput.SelectedFile("$name.zip") { archive.inputStream() })
            assertTrue(result.toString(), result is SkinResult.Ok)
            return (result as SkinResult.Ok).value
        }
        fun install(name: String, width: Int = 1): SkinReplaceTarget {
            val handle = prepare(name, width)
            val result = services.imports.commitImport(handle.handleId)
            assertTrue(result.toString(), result is SkinResult.Ok)
            return target(view().packs.single { it.name == name }.id)
        }
        fun view() = (services.read() as SkinResult.Ok).value
        fun target(id: String = view().packs.single().id): SkinReplaceTarget {
            val state = view(); val pack = state.packs.single { it.id == id }
            return SkinReplaceTarget(pack.id, state.generationSha256, pack.treeSha256, pack.importReceiptSha256)
        }
        fun immutableBytes() = listOf(store.paths.objects, store.paths.importReceipts).flatMap { root ->
            root.walkTopDown().filter(File::isFile).map { it.relativeTo(store.paths.root).path to it.readBytes().toList() }.toList()
        }.toMap()
        fun assertRejectedWithoutMutation(target: SkinReplaceTarget) {
            val authority = File(store.paths.root, "library.json")
            val before = authority.readBytes(); val retained = immutableBytes()
            val result = services.mutations.remove(target)
            assertTrue("Delete must reject retired or in-use target: $result", result is SkinResult.Error)
            assertArrayEquals(before, authority.readBytes())
            assertEquals(retained, immutableBytes())
        }
    }

    companion object {
        @JvmStatic @Parameterized.Parameters(name = "{0}")
        fun profiles() = listOf(arrayOf("hollow-knight"), arrayOf("silksong"))
    }
}
