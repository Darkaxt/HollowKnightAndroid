package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.library.*
import dev.silksong.launcher.skins.storage.*
import java.io.File
import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.util.UUID
import java.util.concurrent.Executor

class SkinLibrarySessionTest {
    @get:org.junit.Rule val temporary = org.junit.rules.TemporaryFolder()
    @Test fun `single authority enables controls without weakening legacy UNKNOWN gate`() {
        dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture.load()
        val owner = java.io.File(temporary.root, "profiles/hollow-knight").apply { mkdirs() }
        val store = dev.silksong.launcher.skins.library.SkinLibraryStore(dev.silksong.launcher.skins.storage.SkinPaths(owner))
        val services = SkinLibraryUiServices.bound(store)
        val fixture = Fixture()
        val session = SkinLibrarySession(services, SkinSafInputs(fixture.provider), { HollowKnightProfile }, fixture.worker, { it() })
        session.refresh(); fixture.worker.runAll()
        assertTrue(session.state.canImport); assertTrue(session.state.canEdit); assertTrue(session.state.canAdvance)
        assertNotEquals("CLEAR", session.state.library?.leaseObservation)
        session.close(); fixture.worker.runAll()
    }
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

    @Test fun `failed preparation without handle remains visible after close and retries before reopen admission`() {
        val fixture = TerminalFixture(temporary.root)
        val firstWorker = Queue(); val secondWorker = Queue()
        val first = fixture.session(firstWorker); val second = fixture.session(secondWorker)
        try {
            first.prepare("malformed", false); firstWorker.runAll()
            assertEquals(1, fixture.failedOpens); assertTrue(first.state.handles.isEmpty())
            assertTrue(first.state.message, first.state.message.contains("Retained preparation cleanup failed"))
            val owner = fixture.captureOwner()
            first.close(); firstWorker.runAll()
            assertTrue("No-handle failed preparation must retain terminal cleanup ownership", first.state.cleanupPending)
            SkinLibrarySession.retryPendingCleanup(); firstWorker.runAll()
            assertTrue(first.state.cleanupPending); assertTrue(owner.exists())
            second.prepare("valid", false); secondWorker.runAll()
            assertEquals("Reopened importer cannot bypass retained terminal cleanup", 0, fixture.freshOpens)
            second.recoverOff(); secondWorker.runAll()
            assertTrue(second.state.message, second.state.message.contains("OFF configuration saved"))
            assertEquals(LibraryMode.OFF, fixture.store.read().required().mode)
            fixture.denied = false
            SkinLibrarySession.retryPendingCleanup(); firstWorker.runAll()
            assertFalse(first.state.cleanupPending); assertFalse(owner.exists())
            second.prepare("valid", false); secondWorker.runAll()
            assertEquals(1, fixture.freshOpens); assertEquals(1, second.state.handles.size)
            assertFalse("Exact old owner must be gone before new provider IO", fixture.oldPresentAtFreshOpen)
            second.close(); secondWorker.runAll()
            assertFalse(second.state.cleanupPending)
            assertEquals("unrelated bytes", fixture.sentinel.readText())
            assertEquals(listOf(fixture.sentinel.parentFile), fixture.paths.importHandles.listFiles()!!.toList())
            assertEquals(LibraryMode.OFF, fixture.store.read().required().mode)
        } finally {
            fixture.denied = false
            first.close(); second.close()
            firstWorker.runAll(); secondWorker.runAll()
            SkinLibrarySession.retryPendingCleanup(); firstWorker.runAll(); secondWorker.runAll()
        }
    }

    @Test fun `queued terminal cleanup blocks reopened provider until existing worker finishes`() {
        val fixture = TerminalFixture(temporary.root)
        val firstWorker = Queue(); val secondWorker = Queue()
        val first = fixture.session(firstWorker); val second = fixture.session(secondWorker)
        try {
            first.prepare("malformed", false); firstWorker.runAll()
            val owner = fixture.captureOwner()
            first.close() // Its cleanup job is queued, not yet run.
            second.prepare("valid", false); secondWorker.runAll()
            assertEquals("Close must retain ownership before its worker can run", 0, fixture.freshOpens)
            fixture.denied = false
            SkinLibrarySession.retryPendingCleanup()
            second.prepare("valid", false); secondWorker.runAll()
            assertEquals("Scheduling retry is not proof cleanup finished", 0, fixture.freshOpens)
            firstWorker.runAll()
            assertFalse(owner.exists()); assertFalse(first.state.cleanupPending)
            second.prepare("valid", false); secondWorker.runAll()
            assertEquals(1, fixture.freshOpens); assertEquals(1, second.state.handles.size)
            assertFalse("Exact old owner must be gone before new provider IO", fixture.oldPresentAtFreshOpen)
            second.close(); secondWorker.runAll()
            assertEquals("unrelated bytes", fixture.sentinel.readText())
        } finally {
            fixture.denied = false
            first.close(); second.close()
            firstWorker.runAll(); secondWorker.runAll()
            SkinLibrarySession.retryPendingCleanup(); firstWorker.runAll(); secondWorker.runAll()
        }
    }

    @Test fun `terminal cleanup for Hollow Knight does not gate another profiles import service`() {
        val first = Fixture(); val other = Fixture()
        val services = SkinLibraryUiServices(SilksongProfile, other.services.read, other.imports,
            UnavailableSkinLibraryMutations, UnavailableSkinModeAdvancePort)
        val second = SkinLibrarySession(services, SkinSafInputs(other.provider), { SilksongProfile }, other.worker, { it() })
        try {
            first.session.prepare("document", false); first.worker.runAll()
            first.blockCancel = true
            first.session.close(); first.worker.runAll()
            assertTrue(first.session.state.cleanupPending)
            second.prepare("document", false); other.worker.runAll()
            assertEquals(1, other.providerReads); assertEquals(1, second.state.handles.size)
        } finally {
            first.blockCancel = false
            second.close(); other.worker.runAll()
            SkinLibrarySession.retryPendingCleanup(); first.worker.runAll()
        }
    }

    private class TerminalFixture(root: File) {
        init { PinnedCatalogFixture.load() }
        val paths = SkinPaths(File(root, "profiles/hollow-knight").apply { mkdirs() })
        private val production = AndroidSkinFileSystem()
        var denied = true; var failedOpens = 0; var freshOpens = 0; var oldPresentAtFreshOpen = false
        private var oldOwner: File? = null
        lateinit var sentinel: File
        private val fs = object : SkinFileSystem by production, SkinFileSystemSecurity by production,
            SkinFileSystemBoundedListing by production {
            override fun deleteContained(path: File, owner: File) {
                if (denied && path.name == "archive") throw java.io.IOException("terminal archive deletion fault")
                production.deleteContained(path, owner)
            }
        }
        val store = SkinLibraryStore(paths, fs)
        private val decoder = PngDecoder { _, info ->
            SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
        }
        private val provider = object : SkinDocumentProvider {
            override fun file(document: String) = SkinDocument(document, "skin.zip", SkinDocumentKind.FILE)
            override fun children(tree: String): SkinDocumentCursor = error("unused")
            override fun open(document: String): java.io.InputStream {
                if (document == "malformed") {
                    failedOpens++
                    return ByteArray(65537) { 65 }.also { it[0] = 0x50; it[1] = 0x4b; it[2] = 3; it[3] = 4 }.inputStream()
                }
                freshOpens++
                oldPresentAtFreshOpen = requireNotNull(oldOwner).exists()
                return RawZipFixture.build(listOf(RawZipFixture.Entry("Skin/Knight.png".toByteArray(), TinyPngFixture.rgba()))).bytes.inputStream()
            }
        }
        fun captureOwner(): File {
            val owner = paths.importHandles.listFiles()!!.single()
            assertEquals(65537L, owner.walkTopDown().filter { it.isFile }.sumOf { it.length() })
            oldOwner = owner
            sentinel = File(File(paths.importHandles, "unrelated").apply { mkdirs() }, "keep").apply { writeText("unrelated bytes") }
            return owner
        }
        fun session(worker: Queue): SkinLibrarySession {
            // Each normal screen owns a distinct production importer; only PNG decode is a host fixture.
            val services = SkinLibraryUiServices(HollowKnightProfile, { SkinLibraryService.readLibrary(store) },
                SkinLibraryImporter(store, decoder), UnavailableSkinLibraryMutations, UnavailableSkinModeAdvancePort,
                simplifiedAuthority = true, recover = store::recoverOff)
            return SkinLibrarySession(services, SkinSafInputs(provider), { HollowKnightProfile }, worker, { it() })
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
