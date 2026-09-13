package dev.silksong.launcher.skins.ui

import org.junit.Assert.*
import org.junit.Test

class SkinLibraryPresentationTest {
    private val receipt = SkinReceiptSummary()
    private fun row(selected: Boolean = false, eligible: Boolean = false) = SkinPackRow(
        id = "pack-id",
        name = "Moonlight",
        author = "Ada",
        candidateKey = "a".repeat(64),
        treeSha256 = "b".repeat(64),
        importReceiptSha256 = "c".repeat(64),
        selected = selected,
        rotationEligible = eligible,
        receipt = receipt,
    )

    @Test fun `selected live pack gets direct Disable and a plain-language delete block`() {
        for (mode in listOf("ON", "ROTATE")) {
            val item = SkinPresentationMapper.pack(row(selected = true), mode, pendingPackId = null)
            assertEquals(SkinPackPrimaryAction.DISABLE, item.primaryAction)
            assertEquals(SkinDeleteBlock.IN_USE, item.deleteBlock)
            assertEquals(if (mode == "ON") SkinPackBadge.ENABLED else SkinPackBadge.ROTATING, item.badge)
        }
    }

    @Test fun `selected OFF and unselected packs get direct Enable with truthful badges`() {
        val off = SkinPresentationMapper.pack(row(selected = true), "OFF", pendingPackId = null)
        assertEquals(SkinPackPrimaryAction.ENABLE, off.primaryAction)
        assertNull(off.deleteBlock)
        assertEquals(SkinPackBadge.SELECTED_OFF, off.badge)

        val eligible = SkinPresentationMapper.pack(row(eligible = true), "ROTATE", pendingPackId = null)
        assertEquals(SkinPackPrimaryAction.ENABLE, eligible.primaryAction)
        assertNull(eligible.deleteBlock)
        assertEquals(SkinPackBadge.ROTATION_INCLUDED, eligible.badge)
    }

    @Test fun `pending pack is named as pending and cannot be deleted while mode is live`() {
        val item = SkinPresentationMapper.pack(row(), "ROTATE", pendingPackId = "pack-id")
        assertEquals(SkinPackBadge.PENDING, item.badge)
        assertEquals(SkinDeleteBlock.IN_USE, item.deleteBlock)
        assertEquals(SkinPackPrimaryAction.ENABLE, item.primaryAction)
    }

    @Test fun `requested configuration and runtime observation remain separate presentation fields`() {
        val state = SkinLibraryViewState(
            generationSha256 = "d".repeat(64),
            mode = "ROTATE",
            selectedPackId = "pack-id",
            activePackId = null,
            rotationOrder = emptyList(),
            interlock = "NOT_USED",
            originalFailure = null,
            rollbackFailure = null,
            leaseObservation = "NOT_USED",
            packs = listOf(row(selected = true)),
            simplifiedAuthority = true,
            runtimeObservation = "Last game report (older configuration): Applied · pack-id · complete · 10 ms UTC; refresh to retry status",
        )

        val summary = SkinPresentationMapper.library(state)

        assertEquals("ROTATE", summary.requestedMode)
        assertEquals("Moonlight", summary.selectedPackName)
        assertEquals("Applied", summary.runtime.status)
        assertTrue(summary.runtime.olderConfiguration)
        assertEquals(state.runtimeObservation, summary.runtime.fullReport)
        assertFalse(summary.requestedMode.contains(summary.runtime.status))
    }

    @Test fun `missing and unreadable runtime observations stay concise on the main surface`() {
        val base = SkinLibraryViewState("d".repeat(64), "OFF", null, null, emptyList(), "NOT_USED", null, null,
            "NOT_USED", emptyList(), simplifiedAuthority = true)
        assertEquals(SkinRuntimeKind.NONE, SkinPresentationMapper.library(base.copy(runtimeObservation =
            "No game-process report yet; launch Hollow Knight, then refresh")).runtime.kind)
        assertEquals(SkinRuntimeKind.UNAVAILABLE, SkinPresentationMapper.library(base.copy(runtimeObservation =
            "Last game report unreadable: broken JSON")).runtime.kind)
    }
}
