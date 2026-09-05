package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.registry.*
import dev.silksong.launcher.skins.session.LeaseMutationGate
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.util.UUID

class GatedSkinLibraryMutationsTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `selection and eligibility have independent fresh gate and exact target CAS`() {
        val root = File(temporary.root, "profiles/hollow-knight/skins").apply { mkdirs() }
        val locks = SkinLockManager(root)
        val store = SkinRegistryStore(root, PermissiveTestSkinQuota(root), FastSkinFileSystem(), locks)
        var head = (store.recover() as SkinResult.Ok).value
        val candidate = "a".repeat(64)
        val published = PublishedSkin("local-${candidate.take(58)}", candidate, "A", "b".repeat(64),
            "c".repeat(64), "d".repeat(64), "e".repeat(64), File(root, "unused"), emptyList())
        head = (store.commit(head, UUID.randomUUID(), "host", SkinRegistryMutations().install(published)) as SkinResult.Ok).value
        val target = SkinReplaceTarget(published.id, head.sha256, published.treeSha256, published.importReceiptSha256)
        var gate = LeaseMutationGate.ACTIVE; var gateReads = 0
        val service = GatedSkinLibraryMutations.forHostTests(locks, store) { gateReads++; gate }
        assertTrue(service.select(target) is SkinResult.Error)
        gate = LeaseMutationGate.UNKNOWN
        assertTrue(service.eligibility(target, true) is SkinResult.Error)
        gate = LeaseMutationGate.CLEAR
        assertTrue(service.select(target) is SkinResult.Ok)
        assertEquals(3, gateReads)
        assertEquals(published.id, (store.snapshotForLibrary() as SkinResult.Ok).value.document.activation.selectedPackId)
        assertEquals(SkinImportCode.REGISTRY_CONFLICT, (service.eligibility(target, true) as SkinResult.Error).code)
        val current = (store.snapshotForLibrary() as SkinResult.Ok).value
        assertTrue(service.eligibility(target.copy(generationSha256 = current.sha256), true) is SkinResult.Ok)
        assertTrue((store.snapshotForLibrary() as SkinResult.Ok).value.document.packs.single().rotationEligible)
    }
}
