package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.util.UUID
import java.util.concurrent.Executor

class SkinLibrarySessionTest {
    @Test fun `provider IO is queued and never performed by prepare caller`() {
        val fixture = Fixture()
        fixture.session.refresh(); fixture.worker.runAll()
        fixture.session.prepare("document", false)
        assertEquals(0, fixture.providerReads)
        assertTrue(fixture.session.state.busy)
        fixture.worker.runAll()
        assertEquals(1, fixture.providerReads)
        assertEquals(1, fixture.session.state.handles.size)
        fixture.session.close(); fixture.worker.runAll()
        assertEquals(1, fixture.cancels)
    }

    @Test fun `detaching view invalidates queued callbacks without losing retained handles`() {
        val fixture = Fixture()
        var oldCallbacks = 0; var newCallbacks = 0
        fixture.session.attach { oldCallbacks++ }
        fixture.session.detach()
        fixture.ui.runAll()
        assertEquals(0, oldCallbacks)
        fixture.session.attach { newCallbacks++ }
        fixture.ui.runAll()
        assertEquals(1, newCallbacks)
        fixture.session.close(); fixture.worker.runAll()
    }

    @Test fun `changed profile prevents queued provider reads and mutations`() {
        val fixture = Fixture()
        fixture.session.refresh(); fixture.worker.runAll()
        fixture.session.prepare("document", false)
        fixture.profile = SilksongProfile
        fixture.worker.runAll()
        assertEquals(0, fixture.providerReads)
        assertTrue(fixture.session.state.message.contains("profile", true))
        fixture.session.close(); fixture.worker.runAll()
    }

    @Test fun `ACTIVE and UNKNOWN keep library readable and reject preparation before provider IO`() {
        for (gate in listOf("ACTIVE", "UNKNOWN")) {
            val fixture = Fixture().apply { observation = gate }
            fixture.session.refresh(); fixture.worker.runAll()
            assertNotNull(fixture.session.state.library)
            assertFalse(fixture.session.state.canImport)
            fixture.session.prepare("document", false); fixture.worker.runAll()
            assertEquals(0, fixture.providerReads)
            fixture.session.close(); fixture.worker.runAll()
        }
    }

    @Test fun `terminal exit retains blocked cleanup and later retry releases ownership`() {
        val fixture = Fixture()
        fixture.session.refresh(); fixture.worker.runAll()
        fixture.session.prepare("document", false); fixture.worker.runAll()
        fixture.blockCancel = true
        fixture.session.close(); fixture.worker.runAll()
        assertTrue(fixture.session.state.cleanupPending)
        fixture.blockCancel = false
        SkinLibrarySession.retryPendingCleanup(); fixture.worker.runAll()
        assertFalse(fixture.session.state.cleanupPending)
        assertTrue(fixture.session.state.handles.isEmpty())
    }

    @Test fun `throwing cleanup transport retains owner for retry rather than shutting it down`() {
        val fixture = Fixture()
        fixture.session.refresh(); fixture.worker.runAll()
        fixture.session.prepare("document", false); fixture.worker.runAll()
        fixture.throwCancel = true
        fixture.session.close(); fixture.worker.runAll()
        assertTrue(fixture.session.state.cleanupPending)
        fixture.throwCancel = false
        SkinLibrarySession.retryPendingCleanup(); fixture.worker.runAll()
        assertTrue(fixture.session.state.handles.isEmpty())
    }

    @Test fun `failed refresh disables previously enabled mutation controls`() {
        val fixture = Fixture()
        fixture.session.refresh(); fixture.worker.runAll()
        assertTrue(fixture.session.state.canImport)
        fixture.throwRead = true
        fixture.session.refresh(); fixture.worker.runAll()
        assertFalse(fixture.session.state.canImport)
        fixture.session.close(); fixture.worker.runAll()
    }

    @Test fun `successful commit outcome survives failed status refresh`() {
        checkCommitRefresh(indeterminate = false, throwingRefresh = false)
        checkCommitRefresh(indeterminate = false, throwingRefresh = true)
    }

    @Test fun `indeterminate commit outcome survives failed status refresh`() {
        checkCommitRefresh(indeterminate = true, throwingRefresh = false)
        checkCommitRefresh(indeterminate = true, throwingRefresh = true)
    }

    @Test fun `select eligibility and mode results survive failed refresh`() {
        for (operation in listOf("select", "eligibility", "mode")) {
            for (uncertain in listOf(false, true)) {
                for (throwing in listOf(false, true)) {
                    val fixture = Fixture().apply { uncertainCommit = uncertain; throwAfterCommit = throwing }
                    try {
                        fixture.session.refresh(); fixture.worker.runAll()
                        assertTrue(fixture.session.state.canEdit)
                        assertTrue(fixture.session.state.canAdvance)
                        val target = SkinReplaceTarget("target", "a".repeat(64), "b".repeat(64), "c".repeat(64))
                        when (operation) {
                            "select" -> fixture.session.select(target)
                            "eligibility" -> fixture.session.eligibility(target, true)
                            else -> fixture.session.advanceMode()
                        }
                        fixture.worker.runAll()
                        val state = fixture.session.state
                        assertTrue(state.message, state.message.contains(if (uncertain) "Commit outcome uncertain" else "Library operation completed"))
                        assertNotNull(state.refreshError)
                        assertFalse(state.canImport); assertFalse(state.canEdit); assertFalse(state.canAdvance)
                    } finally { fixture.session.close(); fixture.worker.runAll() }
                }
            }
        }
    }

    private fun checkCommitRefresh(indeterminate: Boolean, throwingRefresh: Boolean) {
        val fixture = Fixture().apply {
            uncertainCommit = indeterminate
            throwAfterCommit = throwingRefresh
        }
        try {
            fixture.session.prepare("document", false); fixture.worker.runAll()
            fixture.session.importAll(); fixture.worker.runAll()
            val state = fixture.session.state
            assertTrue(state.message, state.message.contains(if (indeterminate) "Commit outcome uncertain" else "Committed pack"))
            assertFalse(state.canImport)
            assertFalse(state.canEdit)
            assertFalse(state.canAdvance)
            assertEquals(if (indeterminate) 1 else 0, state.handles.size)
            // The distinct refresh error must remain available alongside the operation outcome.
            val error = requireNotNull(state.refreshError)
            assertTrue(error.detail.contains("snapshot"))
            fixture.session.refresh(); fixture.worker.runAll()
            assertTrue(fixture.session.state.message.contains(if (indeterminate) "Commit outcome uncertain" else "Committed pack"))
        } finally { fixture.session.close(); fixture.worker.runAll() }
    }

    private class Queue : Executor {
        val tasks = ArrayDeque<Runnable>()
        override fun execute(command: Runnable) { tasks += command }
        fun runAll() { while (tasks.isNotEmpty()) tasks.removeFirst().run() }
    }
    private class Fixture {
        val worker = Queue(); val ui = Queue()
        var profile: GameProfile = HollowKnightProfile
        var observation = "CLEAR"; var providerReads = 0; var cancels = 0; var blockCancel = false; var throwCancel = false; var throwRead = false
        var uncertainCommit = false; var throwAfterCommit = false; var committed = false
        val imports = object : SkinImportService {
            override val available = true
            override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> {
                input.openOnce().close()
                return SkinResult.Ok(SkinPreparationHandle(UUID.randomUUID(), listOf(
                    CandidatePreparationSummary("61", "a".repeat(64), "A", SkinImportCode.OK, "Ready"))))
            }
            override fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>> {
                committed = true
                return if (uncertainCommit) SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Commit outcome uncertain")
                else SkinResult.Ok(listOf(SkinImportSummary("61", SkinImportCode.OK, "installed", "Committed pack", emptyList())))
            }
            override fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary> = error("unused")
            override fun cancel(handleId: UUID): SkinResult<Unit> {
                cancels++
                if (throwCancel) error("cleanup transport disconnected")
                return if (blockCancel) SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "ACTIVE") else SkinResult.Ok(Unit)
            }
        }
        val provider = object : SkinDocumentProvider {
            override fun file(document: String): SkinDocument { providerReads++; return SkinDocument(document, "x", SkinDocumentKind.FILE) }
            override fun children(tree: String): SkinDocumentCursor = error("unused")
            override fun open(document: String) = ByteArrayInputStream(byteArrayOf(1))
        }
        val services = SkinLibraryUiServices(HollowKnightProfile, {
            if (throwRead || (committed && throwAfterCommit)) error("snapshot transport failed")
            if (committed) SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "snapshot read failed")
            else SkinResult.Ok(SkinLibraryViewState("a".repeat(64), "OFF", null, null, emptyList(), "CLEAR", null, null, observation, emptyList()))
        }, imports, object : SkinLibraryMutations {
            override val available = true
            override fun select(target: SkinReplaceTarget) = mutationResult()
            override fun eligibility(target: SkinReplaceTarget, eligible: Boolean) = mutationResult()
        }, SkinModeAdvancePort { mutationResult() }, modeAvailable = true)
        private fun mutationResult(): SkinResult<Unit> {
            committed = true
            return if (uncertainCommit) SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Commit outcome uncertain")
            else SkinResult.Ok(Unit)
        }
        val session = SkinLibrarySession(services, SkinSafInputs(provider), { profile }, worker, { ui.execute(it) })
    }
}
