package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.registry.*
import dev.silksong.launcher.skins.session.LeaseMutationGate
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SkinLibraryServiceTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `mode port result is returned unchanged once without a mode payload`() {
        val failure = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "No executor")
        var calls = 0
        val controller = SkinLibraryController(SkinModeAdvancePort { calls++; failure })
        assertSame(failure, controller.advanceMode())
        assertEquals(1, calls)
    }

    @Test fun `unavailable mode port never reports live success`() {
        val result = SkinLibraryController(UnavailableSkinModeAdvancePort).advanceMode()
        assertTrue(result is SkinResult.Error)
        assertEquals(SkinImportCode.LIFECYCLE_BLOCKED, (result as SkinResult.Error).code)
    }

    @Test fun `only the exact Hollow Knight profile exposes skins`() {
        assertTrue(SkinLibraryService.isVisible(HollowKnightProfile))
        assertFalse(SkinLibraryService.isVisible(SilksongProfile))
    }

    @Test fun `unsupported profile never invokes snapshot authority`() {
        var reads = 0
        val service = SkinLibraryService(SilksongProfile) { reads++; error("wrong profile read") }
        assertTrue(service.refresh() is SkinResult.Error)
        assertEquals(0, reads)
    }

    @Test fun `foreign snapshot profile is rejected`() {
        val foreign = snapshot(LeaseMutationGate.CLEAR).let {
            it.copy(registryHead = it.registryHead.copy(document = it.registryHead.document.copy(profileId = "silksong")))
        }
        assertTrue(SkinLibraryService(HollowKnightProfile) { SkinResult.Ok(foreign) }.refresh() is SkinResult.Error)
    }

    @Test fun `ACTIVE and UNKNOWN retain readable packs and status but disable mutations`() {
        for (gate in listOf(LeaseMutationGate.ACTIVE, LeaseMutationGate.UNKNOWN)) {
            val state = state(SkinLibraryService(HollowKnightProfile) { SkinResult.Ok(snapshot(gate)) })
            assertFalse(state.mutationsEnabled)
            assertEquals(gate.name, state.leaseObservation)
            assertEquals("ROTATE", state.mode)
            assertEquals("local-a", state.selectedPackId)
            assertEquals("local-a", state.activePackId)
            assertEquals(listOf("local-a"), state.rotationOrder)
            assertEquals("Artist", state.packs.single().author)
            assertEquals("b".repeat(64), state.packs.single().candidateKey)
            assertEquals("c".repeat(64), state.packs.single().treeSha256)
            assertEquals("e".repeat(64), state.packs.single().importReceiptSha256)
            assertEquals("CLEAR", state.interlock)
        }
    }

    @Test fun `even observed CLEAR cannot enable production mutations while ledger blocks binding`() {
        val state = state(SkinLibraryService(HollowKnightProfile) { SkinResult.Ok(snapshot(LeaseMutationGate.CLEAR)) })
        assertFalse(state.mutationsEnabled)
    }

    @Test fun `refresh re-reads rather than treating cached CLEAR as authority`() {
        var reads = 0
        var gate = LeaseMutationGate.CLEAR
        val service = SkinLibraryService(HollowKnightProfile) { reads++; SkinResult.Ok(snapshot(gate)) }
        assertEquals("CLEAR", state(service).leaseObservation)
        gate = LeaseMutationGate.ACTIVE
        assertEquals("ACTIVE", state(service).leaseObservation)
        assertEquals(2, reads)
    }

    @Test fun `snapshot failure remains failure not an empty successful library`() {
        val error = SkinResult.Error(SkinImportCode.REGISTRY_CORRUPT, "bad current")
        assertSame(error, SkinLibraryService(HollowKnightProfile) { error }.refresh())
    }

    @Test fun `production refresh with missing locks creates no profile or genesis`() {
        val files = temporary.newFolder("files")
        val service = SkinLibraryService.production(files, HollowKnightProfile)
        val result = service.refresh()
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertFalse(File(files, "profiles").exists())
    }

    @Test fun `production Silksong service never reads Hollow Knight storage`() {
        val files = temporary.newFolder("files")
        assertTrue(SkinLibraryService.production(files, SilksongProfile).refresh() is SkinResult.Error)
        assertTrue(files.listFiles()!!.isEmpty())
    }

    @Test fun `pack warning failure remains visible without hiding pack status`() {
        val receiptError = SkinResult.Error(SkinImportCode.IMPORT_RECEIPT_CORRUPT, "bad receipt")
        val service = SkinLibraryService(HollowKnightProfile, SkinReceiptSummaryReader { receiptError }) {
            SkinResult.Ok(snapshot(LeaseMutationGate.ACTIVE))
        }
        val row = state(service).packs.single()
        assertSame(receiptError, row.receipt.error)
        assertEquals("Knight", row.name)
    }

    private fun state(service: SkinLibraryService) = (service.refresh() as SkinResult.Ok).value

    private fun snapshot(gate: LeaseMutationGate): SkinLibrarySnapshot {
        val pack = RegistryPack("local-a", "Knight", "Artist", "b".repeat(64), "c".repeat(64),
            "d".repeat(64), "e".repeat(64), true)
        val document = SkinRegistryAuthority.genesis().copy(
            packs = listOf(pack),
            activation = SkinActivation(SkinMode.ROTATE, pack.id,
                ActiveVisual.Pack(pack.id, pack.treeSha256, pack.contentSha256, pack.importReceiptSha256),
                1, RotationInterlock.clear()),
        )
        return SkinLibrarySnapshot(RegistryHead(document.generationId, 1, "a".repeat(64), document), gate)
    }
}
