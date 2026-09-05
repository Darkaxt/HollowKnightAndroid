package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.registry.CandidatePreparationSummary
import dev.silksong.launcher.skins.registry.SkinPreparationHandle
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.util.UUID

class SkinImportWorkflowTest {
    @Test fun `prepare owns only ordered summaries and commit never reopens input`() {
        val service = Service()
        val workflow = SkinImportWorkflow(service)
        var opens = 0
        val input = SkinImportInput.SelectedFile("input.bin") { opens++; ByteArrayInputStream(byteArrayOf(1)) }
        val progress = mutableListOf<Int>()
        val prepared = workflow.prepare(listOf(input)) { done, _ -> progress += done }
        assertTrue(prepared is SkinResult.Ok)
        assertEquals(listOf(0, 1), progress)
        assertEquals(1, opens)
        assertEquals(service.handle.candidates, workflow.handles().single().candidates)
        val results = (workflow.importAll() as SkinResult.Ok).value
        assertEquals(listOf(SkinImportCode.OK, SkinImportCode.NO_CANDIDATE), results.flatMap { it.results }.map { it.code })
        assertEquals(1, opens)
        assertTrue(workflow.handles().isEmpty())
    }

    @Test fun `multi-ready Replace rejects missing source and never guesses first`() {
        val service = Service()
        val workflow = prepared(service)
        val result = workflow.confirmation(service.handle.handleId, null, target())
        assertTrue(result is SkinResult.Error)
        assertEquals(0, service.replaces.size)
    }

    @Test fun `Replace captures exact source and target CAS until explicit confirmation`() {
        val service = Service()
        val workflow = prepared(service)
        val target = target()
        val confirmation = (workflow.confirmation(service.handle.handleId, "b".repeat(64), target) as SkinResult.Ok).value
        assertTrue(service.replaces.isEmpty())
        assertTrue(workflow.replace(confirmation) is SkinResult.Ok)
        val sent = service.replaces.single()
        assertEquals("b".repeat(64), sent.sourceCandidateKey)
        assertEquals(target, sent.target)
        assertEquals(service.handle.handleId, sent.handleId)
        assertTrue(workflow.replace(confirmation) is SkinResult.Error)
    }

    @Test fun `unlisted rejected or foreign source cannot be confirmed`() {
        val service = Service()
        val workflow = prepared(service)
        assertTrue(workflow.confirmation(service.handle.handleId, "z".repeat(64), target()) is SkinResult.Error)
        assertTrue(workflow.confirmation(UUID.randomUUID(), "a".repeat(64), target()) is SkinResult.Error)
    }

    @Test fun `cancel releases every owned handle and invalidates confirmation`() {
        val service = Service()
        val workflow = prepared(service)
        val confirmation = (workflow.confirmation(service.handle.handleId, "a".repeat(64), target()) as SkinResult.Ok).value
        assertTrue(workflow.cancel() is SkinResult.Ok)
        assertEquals(listOf(service.handle.handleId), service.cancelled)
        assertTrue(workflow.handles().isEmpty())
        assertTrue(workflow.replace(confirmation) is SkinResult.Error)
    }

    @Test fun `cancellation arriving inside prepare cleans returned handle before returning`() {
        val service = Service()
        val workflow = SkinImportWorkflow(service)
        service.afterPrepare = workflow::requestCancel
        assertTrue(workflow.prepare(listOf(input())) is SkinResult.Error)
        assertEquals(listOf(service.handle.handleId), service.cancelled)
        assertTrue(workflow.handles().isEmpty())
    }

    @Test fun `blocked cleanup retains ownership for reachable cancel retry`() {
        val service = Service()
        val workflow = prepared(service)
        service.cancelBlocked = true
        assertTrue(workflow.cancel() is SkinResult.Error)
        assertEquals(listOf(service.handle), workflow.handles())
        service.cancelBlocked = false
        assertTrue(workflow.cancel() is SkinResult.Ok)
        assertTrue(workflow.handles().isEmpty())
    }

    @Test fun `indeterminate commit retains handle instead of pretending all imported`() {
        val service = Service()
        val workflow = prepared(service)
        service.commitFailure = SkinResult.Error(SkinImportCode.INDETERMINATE, "refresh registry before retry")
        val result = (workflow.importAll() as SkinResult.Ok).value.single()
        assertEquals(SkinImportCode.INDETERMINATE, result.error!!.code)
        assertEquals(listOf(service.handle), workflow.handles())
    }

    @Test fun `unavailable binding refuses prepare without opening provider`() {
        val workflow = SkinImportWorkflow(UnavailableSkinImportService)
        var opened = false
        val result = workflow.prepare(listOf(SkinImportInput.SelectedFile("x") { opened = true; error("opened") }))
        assertTrue(result is SkinResult.Error)
        assertFalse(opened)
        assertTrue(workflow.handles().isEmpty())
    }

    @Test fun `cancel failure after an uncertain commit is included in returned outcomes`() {
        val service = Service().apply { cancelBlocked = true }
        lateinit var workflow: SkinImportWorkflow
        val cancelling = object : SkinImportService by service {
            override fun importAttempt(handleId: UUID): SkinImportAttempt<List<SkinImportSummary>> {
                workflow.requestCancel()
                return SkinImportAttempt(SkinResult.Error(SkinImportCode.INDETERMINATE, "commit outcome unknown"))
            }
        }
        workflow = SkinImportWorkflow(cancelling)
        workflow.prepare(listOf(input()))
        val outcomes = (workflow.importAll() as SkinResult.Ok).value
        assertEquals(listOf(SkinImportCode.INDETERMINATE, SkinImportCode.LIFECYCLE_BLOCKED), outcomes.map { it.error!!.code })
        assertEquals(1, workflow.handles().size)
    }

    private fun input() = SkinImportInput.SelectedFile("input") { ByteArrayInputStream(byteArrayOf(1)) }
    private fun prepared(service: Service) = SkinImportWorkflow(service).also { it.prepare(listOf(input())) }
    private fun target() = SkinReplaceTarget("local-target", "c".repeat(64), "d".repeat(64), "e".repeat(64))

    private class Service : SkinImportService {
        override val available = true
        val handle = SkinPreparationHandle(UUID.randomUUID(), listOf(
            CandidatePreparationSummary("61", "a".repeat(64), "First", SkinImportCode.OK, "Ready"),
            CandidatePreparationSummary("62", "b".repeat(64), "Second", SkinImportCode.OK, "Ready"),
            CandidatePreparationSummary("63", null, null, SkinImportCode.NO_CANDIDATE, "Rejected"),
        ))
        val cancelled = mutableListOf<UUID>()
        val replaces = mutableListOf<SkinReplaceRequest>()
        var cancelBlocked = false
        var afterPrepare: () -> Unit = {}
        var commitFailure: SkinResult.Error? = null
        override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> {
            input.openOnce().close()
            afterPrepare()
            return SkinResult.Ok(handle)
        }
        override fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>> = commitFailure ?: SkinResult.Ok(listOf(
            SkinImportSummary("61", SkinImportCode.OK, "local-a", "Committed", emptyList()),
            SkinImportSummary("63", SkinImportCode.NO_CANDIDATE, null, "Rejected", emptyList()),
        ))
        override fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary> {
            replaces += request
            return SkinResult.Ok(SkinImportSummary("62", SkinImportCode.OK, request.target.id, "Replaced", emptyList()))
        }
        override fun cancel(handleId: UUID): SkinResult<Unit> {
            cancelled += handleId
            return if (cancelBlocked) SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "ACTIVE") else SkinResult.Ok(Unit)
        }
    }
}
