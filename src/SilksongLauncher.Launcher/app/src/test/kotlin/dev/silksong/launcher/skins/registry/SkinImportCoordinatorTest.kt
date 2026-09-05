package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.skins.ui.CoordinatorSkinImportService
import dev.silksong.launcher.skins.ui.SkinImportWorkflow
import dev.silksong.launcher.skins.ui.SkinReplaceTarget
import dev.silksong.launcher.skins.contracts.BuiltSkin
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.StagedPayload
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.importing.SkinQuarantine
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaCapacityReserver
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.session.LeaseMutationGate
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.ByteArrayInputStream
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinImportCoordinatorTest {
    private lateinit var testRoot: File
    private lateinit var profileRoot: File
    private lateinit var paths: SkinPaths
    private lateinit var fs: SkinFileSystem
    private lateinit var quota: PermissiveTestSkinQuota
    private lateinit var operations: FakeCoordinatorOperations

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-import-coordinator").absoluteFile
        testRoot.deleteRecursively()
        profileRoot = File(testRoot, "profiles/hollow-knight").apply { mkdirs() }
        paths = SkinPaths(profileRoot)
        paths.root.mkdirs()
        fs = FastSkinFileSystem()
        quota = PermissiveTestSkinQuota(paths.root)
        operations = FakeCoordinatorOperations()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun workflowReleasesConsumedStaleCasHandleAndCanPrepareAgain() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "ready")) }
        val service = CoordinatorSkinImportService.forHostTests(HollowKnightProfile)
        val workflow = SkinImportWorkflow(service)
        assertOk(workflow.prepare(listOf(zipInput())))
        val handle = workflow.handles().single()
        val target = SkinReplaceTarget("target", operations.registryHead.sha256, hex('1'), hex('3'))
        val confirmation = assertOk(workflow.confirmation(handle.handleId, hex('a'), target))
        operations.registryHead = registryHead(RegistryPack("target", "Changed", "Unknown", hex('d'), hex('1'), hex('2'), hex('3'), false))
        assertError(SkinImportCode.REGISTRY_CONFLICT, workflow.replace(confirmation))
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
        assertTrue("Consumed and cleaned handle must not remain UI-owned", workflow.handles().isEmpty())
        assertOk(workflow.cancel())
        val next = SkinImportWorkflow(service)
        assertOk(next.prepare(listOf(zipInput())))
        assertOk(next.cancel())
    }

    @Test
    fun workflowRetainsPreclaimActiveAndUnknownHandlesUntilGatedCancel() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "ready")) }
        for (gate in listOf(LeaseMutationGate.ACTIVE, LeaseMutationGate.UNKNOWN)) {
            val workflow = SkinImportWorkflow(CoordinatorSkinImportService.forHostTests(HollowKnightProfile))
            assertOk(workflow.prepare(listOf(zipInput())))
            val handle = workflow.handles().single()
            val confirmation = assertOk(workflow.confirmation(handle.handleId, hex('a'), SkinReplaceTarget("target", hex('f'), hex('1'), hex('3'))))
            operations.gate = gate
            assertError(SkinImportCode.LIFECYCLE_BLOCKED, workflow.replace(confirmation))
            assertEquals(listOf(handle), workflow.handles())
            assertError(SkinImportCode.LIFECYCLE_BLOCKED, workflow.cancel())
            assertTrue(paths.importHandleOwner(handle.handleId).exists())
            operations.gate = LeaseMutationGate.CLEAR
            assertOk(workflow.cancel())
            assertTrue(workflow.handles().isEmpty())
        }
    }

    @Test
    fun workflowRetainsAmbiguousConsumedCleanupAndRetriesWithFreshGate() {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "ready")) }
        var blockCleanup = true
        fs = FaultingSkinFileSystem(fs).apply {
            skipPhysicalSyncs = true
            beforeDelete = { path, _ ->
                if (blockCleanup && path.parentFile?.absoluteFile?.normalize() == paths.importHandles.absoluteFile.normalize()) {
                    error("ambiguous owner cleanup")
                }
            }
        }
        withCoordinator {
            val workflow = SkinImportWorkflow(CoordinatorSkinImportService.forHostTests(HollowKnightProfile))
            assertOk(workflow.prepare(listOf(zipInput())))
            val handle = workflow.handles().single()
            val confirmation = assertOk(workflow.confirmation(handle.handleId, hex('a'), SkinReplaceTarget("target", hex('f'), hex('1'), hex('3'))))
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, workflow.replace(confirmation))
            assertEquals(listOf(handle), workflow.handles())
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, workflow.cancel())
            blockCleanup = false
            for (gate in listOf(LeaseMutationGate.ACTIVE, LeaseMutationGate.UNKNOWN)) {
                operations.gate = gate
                assertError(SkinImportCode.LIFECYCLE_BLOCKED, workflow.cancel())
                assertEquals(listOf(handle), workflow.handles())
                assertTrue(paths.importHandleOwner(handle.handleId).exists())
            }
            operations.gate = LeaseMutationGate.CLEAR
            assertOk(workflow.cancel())
            assertTrue(workflow.handles().isEmpty())
            assertFalse(paths.importHandleOwner(handle.handleId).exists())
        }
    }

    @Test
    fun pendingCleanupTicketRejectsChangedOwnerAndReboundCoordinator() {
        var blockCleanup = true
        val base = fs
        fs = FaultingSkinFileSystem(base).apply {
            skipPhysicalSyncs = true
            beforeDelete = { path, _ ->
                if (blockCleanup && path.parentFile?.absoluteFile?.normalize() == paths.importHandles.absoluteFile.normalize()) error("retain owner")
            }
        }
        val binding = SkinImportCoordinatorDependencies(paths, fs, SkinLockManager(paths.root), quota,
            SkinQuarantine(paths, fs, SkinQuotaCapacityReserver(quota)), operations)
        lateinit var ticket: SkinHandleCleanupRetry
        SkinImportCoordinator.withTestBinding(binding) {
            val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
            val cancelled = SkinImportCoordinator.cancelWithOwnership(handle.handleId)
            assertEquals(SkinHandleDisposition.CLEANUP_PENDING, cancelled.disposition)
            ticket = requireNotNull(cancelled.cleanupRetry)
            blockCleanup = false
            val owner = paths.importHandleOwner(handle.handleId)
            val original = File(paths.root, "retained-original")
            base.atomicMove(owner, original)
            base.createDirectory(owner)
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, ticket.retry())
            assertTrue(owner.isDirectory)
            base.deleteContained(owner, paths.importHandles)
            base.atomicMove(original, owner)
        }
        // Even rebinding the exact dependencies object is a new ownership epoch.
        SkinImportCoordinator.withTestBinding(binding) {
            assertError(SkinImportCode.LIFECYCLE_BLOCKED, ticket.retry())
            assertEquals(1, assertOk(SkinImportCoordinator.recoverOrphansOnProcessStart()))
        }
    }

    @Test
    fun workflowRetriesAmbiguousCancelWithoutTreatingUnknownHandleAsSuccess() {
        var blockCleanup = true
        fs = FaultingSkinFileSystem(fs).apply {
            skipPhysicalSyncs = true
            beforeDelete = { path, _ ->
                if (blockCleanup && path.parentFile?.absoluteFile?.normalize() == paths.importHandles.absoluteFile.normalize()) error("retain owner")
            }
        }
        withCoordinator {
            val service = CoordinatorSkinImportService.forHostTests(HollowKnightProfile)
            val workflow = SkinImportWorkflow(service)
            assertOk(workflow.prepare(listOf(zipInput())))
            val handle = workflow.handles().single()
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, workflow.cancel())
            assertEquals(listOf(handle), workflow.handles())
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, workflow.cancel())
            blockCleanup = false
            assertOk(workflow.cancel())
            assertTrue(workflow.handles().isEmpty())
            assertError(SkinImportCode.LIFECYCLE_BLOCKED, service.cancel(handle.handleId))
        }
    }

    @Test
    fun repeatedCommitAndCancelRetainNoTerminalRecords() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "ready")) }
        repeat(20) { index ->
            val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
            if (index % 2 == 0) assertOk(SkinImportCoordinator.commitImport(handle.handleId))
            else assertOk(SkinImportCoordinator.cancel(handle.handleId))
            val field = SkinImportCoordinator::class.java.getDeclaredField("records").apply { isAccessible = true }
            assertTrue("Terminal record retained on iteration $index",
                (field.get(SkinImportCoordinator) as Map<*, *>).isEmpty())
            assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.cancel(handle.handleId))
        }
    }

    @Test
    fun ownerCapRejects129thBeforeProviderAndLeftoversRecoverOnRestart() {
        val base = fs as FastSkinFileSystem
        var blockOwnerCleanup = false
        fs = object : SkinFileSystem by base, SkinFileSystemSecurity by base, SkinFileSystemBoundedListing by base {
            override fun deleteContained(path: File, owner: File) {
                if (blockOwnerCleanup && path.parentFile?.absoluteFile?.normalize() == paths.importHandles.absoluteFile.normalize()) {
                    throw IllegalStateException("retain cleanup leftovers")
                }
                base.deleteContained(path, owner)
            }
        }
        val opens = AtomicInteger()
        withCoordinator {
            val handles = mutableListOf<SkinPreparationHandle>()
            try {
                repeat(128) { handles += assertOk(SkinImportCoordinator.prepare(zipInput(opens))) }
                val before = paths.importHandles.listFiles().orEmpty().map { it.name }.toSet()
                val rejected = SkinImportCoordinator.prepare(zipInput(opens))
                if (rejected is SkinResult.Ok) handles += rejected.value
                assertError(SkinImportCode.DURABILITY_UNAVAILABLE, rejected)
                assertEquals(128, opens.get())
                assertEquals(before, paths.importHandles.listFiles().orEmpty().map { it.name }.toSet())
            } finally {
                blockOwnerCleanup = true
                handles.forEach { assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.cancel(it.handleId)) }
            }
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.prepare(zipInput(opens)))
            assertEquals(128, opens.get())
            val field = SkinImportCoordinator::class.java.getDeclaredField("records").apply { isAccessible = true }
            assertTrue("Ambiguous disk cleanup must not retain terminal payload records",
                (field.get(SkinImportCoordinator) as Map<*, *>).isEmpty())
        }
        assertEquals(128, paths.importHandles.listFiles().orEmpty().size)
        // A reconstructed process sees the same bounded disk owner set, including failed cleanups.
        blockOwnerCleanup = false
        withCoordinator {
            assertEquals(128, assertOk(SkinImportCoordinator.recoverOrphansOnProcessStart()))
            val handle = assertOk(SkinImportCoordinator.prepare(zipInput(opens)))
            assertOk(SkinImportCoordinator.cancel(handle.handleId))
        }
    }

    @Test
    fun prepareOpensProviderOnceAndCommitNeverReopens() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "ready")) }
        val opens = AtomicInteger()
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput(opens)))

        val results = assertOk(SkinImportCoordinator.commitImport(handle.handleId))

        assertEquals(1, opens.get())
        assertEquals(1, results.size)
        assertEquals(SkinImportCode.OK, handle.candidates.single().code)
        assertEquals(SkinImportCode.OK, results.single().code)
        assertEquals(1, operations.buildCalls)
        assertEquals(1, operations.publishCalls)
        assertEquals(1, operations.commitCalls)
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
    }

    @Test
    fun handleExposesOnlyIdAndSummaries() = withCoordinator {
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        assertEquals(setOf("handleId", "candidates"), dataFields(SkinPreparationHandle::class.java))
        assertEquals(
            setOf("rawPrefixHex", "candidateKey", "name", "code", "detail"),
            dataFields(CandidatePreparationSummary::class.java),
        )
        val summary = handle.candidates.single()
        assertEquals("7061636b", summary.rawPrefixHex)
        assertEquals(null, summary.candidateKey)
        assertEquals(null, summary.name)
        assertEquals(SkinImportCode.NO_CANDIDATE, summary.code)
        assertFalse(summary.detail.contains(paths.staging.path))

        assertOk(SkinImportCoordinator.cancel(handle.handleId))
    }

    @Test
    fun preparedHandleHasNoCompleteOrDurablePointer() = withCoordinator {
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
        val owner = paths.importHandleOwner(handle.handleId)

        assertTrue(owner.isDirectory)
        assertFalse(owner.walkTopDown().any { it.name == ".complete" })
        assertFalse(File(paths.root, "registry/current").exists())
        assertFalse(File(paths.root, "registry/previous").exists())
        assertFalse(File(paths.root, "registry/next").exists())

        assertOk(SkinImportCoordinator.cancel(handle.handleId))
    }

    @Test
    fun commitVsCancelRaceAllowsExactlyOneOpenTransition() = withCoordinator {
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
        val start = CountDownLatch(1)
        val results = arrayOfNulls<SkinResult<*>>(2)
        val commit = thread {
            start.await()
            results[0] = SkinImportCoordinator.commitImport(handle.handleId)
        }
        val cancel = thread {
            start.await()
            results[1] = SkinImportCoordinator.cancel(handle.handleId)
        }

        start.countDown()
        commit.join(10_000)
        cancel.join(10_000)

        assertFalse(commit.isAlive)
        assertFalse(cancel.isAlive)
        assertEquals(1, results.count { it is SkinResult.Ok })
        assertEquals(1, results.count { it is SkinResult.Error && it.code == SkinImportCode.LIFECYCLE_BLOCKED })
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
    }

    @Test
    fun doubleCommitRejectsClaimedAndClosedHandle() = withCoordinator {
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        assertOk(SkinImportCoordinator.commitImport(handle.handleId))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.commitImport(handle.handleId))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.cancel(handle.handleId))
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
    }

    @Test
    fun cancelClaimedHandleCannotTouchStaging() {
        val owner = File(paths.importHandles, UUID.randomUUID().toString()).apply { mkdirs() }
        val handle = SkinPreparationHandle(UUID.fromString(owner.name), emptyList())
        val record = SkinPreparationRecord(
            handle,
            owner,
            SkinPreparationHandleState.OPEN,
            emptyList(),
        )

        assertOk(record.claim())
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, record.cancel())
        assertTrue(owner.isDirectory)

        record.closeClaimed()
    }

    @Test
    fun everyHandleOperationHonorsActiveOrUnknownLeaseGate() = withCoordinator {
        operations.gate = LeaseMutationGate.ACTIVE
        val blockedPrepareOwnerCount = paths.importHandles.listFiles().orEmpty().size
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.prepare(zipInput()))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.recoverOrphansOnProcessStart())
        assertEquals(blockedPrepareOwnerCount, paths.importHandles.listFiles().orEmpty().size)

        operations.gate = LeaseMutationGate.CLEAR
        val first = assertOk(SkinImportCoordinator.prepare(zipInput()))
        val second = assertOk(SkinImportCoordinator.prepare(zipInput()))
        val firstOwner = paths.importHandleOwner(first.handleId)
        val secondOwner = paths.importHandleOwner(second.handleId)

        operations.gate = LeaseMutationGate.ACTIVE
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.commitImport(first.handleId))
        assertTrue(firstOwner.isDirectory)
        operations.gate = LeaseMutationGate.UNKNOWN
        assertError(
            SkinImportCode.LIFECYCLE_BLOCKED,
            SkinImportCoordinator.commitReplace(second.handleId, hex('a'), "target", hex('b'), hex('c'), hex('d')),
        )
        assertTrue(secondOwner.isDirectory)
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, SkinImportCoordinator.cancel(first.handleId))
        assertTrue(firstOwner.isDirectory)

        operations.gate = LeaseMutationGate.CLEAR
        assertOk(SkinImportCoordinator.cancel(first.handleId))
        assertOk(SkinImportCoordinator.cancel(second.handleId))
        assertTrue(operations.gateReads >= 8)
    }

    @Test
    fun processStartOrphanRecoveryDeletesOnlyBoundedContainedIncompleteStaging() = withCoordinator {
        paths.importHandles.mkdirs()
        val first = File(paths.importHandles, "12345678-1234-4234-8234-123456789abc").apply {
            mkdirs()
            File(this, "quarantine-1/archive").apply { parentFile.mkdirs(); writeBytes(byteArrayOf(1)) }
        }
        val second = File(paths.importHandles, "22345678-1234-4234-8234-123456789abc").apply { mkdirs() }
        val unknown = File(paths.importHandles, "not-a-handle").apply { mkdirs() }

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.recoverOrphansOnProcessStart())
        assertTrue(first.isDirectory)
        assertTrue(second.isDirectory)
        assertTrue(unknown.isDirectory)

        assertTrue(unknown.deleteRecursively())
        File(second, ".complete").writeBytes(byteArrayOf())
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.recoverOrphansOnProcessStart())
        assertTrue(first.isDirectory)
        assertTrue(second.isDirectory)

        assertTrue(File(second, ".complete").delete())
        assertEquals(2, assertOk(SkinImportCoordinator.recoverOrphansOnProcessStart()))
        assertFalse(first.exists())
        assertFalse(second.exists())
    }

    @Test
    fun processStartOrphanRecoveryRejectsOverBoundListingWithoutDeletion() = withCoordinator {
        paths.importHandles.mkdirs()
        repeat(129) { index ->
            File(
                paths.importHandles,
                UUID.nameUUIDFromBytes("orphan-$index".toByteArray()).toString(),
            ).mkdirs()
        }

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.recoverOrphansOnProcessStart())
        assertEquals(129, paths.importHandles.listFiles().orEmpty().size)
    }

    @Test
    fun processStartOrphanRecoveryRejectsUnstableImmediateEvidenceBeforeDeletion() {
        paths.importHandles.mkdirs()
        val valid = File(paths.importHandles, UUID.randomUUID().toString()).apply { mkdirs() }
        val base = fs as FastSkinFileSystem
        var rootListings = 0
        fs = object : SkinFileSystem by base, SkinFileSystemSecurity by base, SkinFileSystemBoundedListing {
            override fun listBounded(path: File, maximumEntries: Int): List<File> {
                val listed = base.listBounded(path, maximumEntries)
                if (path.absoluteFile.normalize() == paths.importHandles.absoluteFile.normalize() && rootListings++ == 0) {
                    File(paths.importHandles, "unstable-name").mkdirs()
                }
                return listed
            }
        }

        withCoordinator {
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.recoverOrphansOnProcessStart())
            assertTrue(valid.isDirectory)
            assertTrue(File(paths.importHandles, "unstable-name").isDirectory)
        }
    }

    @Test
    fun processStartOrphanRecoveryLayersTheFiveHundredTwelveNodeCandidateBound() = withCoordinator {
        val owner = File(paths.importHandles, UUID.randomUUID().toString())
        val normalized = File(owner, "quarantine-1/normalized-1").apply { mkdirs() }
        repeat(2) { candidateIndex ->
            val candidate = File(normalized, "candidate-${candidateIndex.toString().padStart(3, '0')}").apply { mkdirs() }
            repeat(300) { fileIndex -> File(candidate, "payload-$fileIndex").writeBytes(byteArrayOf(1)) }
        }

        assertEquals(1, assertOk(SkinImportCoordinator.recoverOrphansOnProcessStart()))
        assertFalse(owner.exists())
    }

    @Test
    fun processStartOrphanRecoveryRejectsOneOverBoundCandidateWithoutDeletion() = withCoordinator {
        val owner = File(paths.importHandles, UUID.randomUUID().toString())
        val normalized = File(owner, "quarantine-1/normalized-1").apply { mkdirs() }
        val valid = File(normalized, "candidate-000").apply { mkdirs() }
        repeat(300) { index -> File(valid, "payload-$index").writeBytes(byteArrayOf(1)) }
        val overBound = File(normalized, "candidate-001").apply { mkdirs() }
        repeat(513) { index -> File(overBound, "payload-$index").writeBytes(byteArrayOf(1)) }

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.recoverOrphansOnProcessStart())
        assertEquals(300, valid.listFiles().orEmpty().size)
        assertEquals(513, overBound.listFiles().orEmpty().size)
        assertTrue(owner.isDirectory)
    }

    @Test
    fun sameLengthPayloadCorruptionStopsBeforeBuildPublicationAndRegistry() = withCoordinator {
        val catalog = dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture.load()
        val decoder = dev.silksong.launcher.skins.importing.PngDecoder { _, info ->
            SkinResult.Ok(dev.silksong.launcher.skins.contracts.DecodeResult(info.width, info.height,
                info.width.toLong() * info.height))
        }
        val normalizer = dev.silksong.launcher.skins.importing.SkinNormalizer(catalog, decoder, fs)
        var retained: PreparedSkinCandidate? = null
        operations.normalizeAction = { archive ->
            normalizer.prepare(archive).also { result ->
                retained = (result as SkinResult.Ok).value.filterIsInstance<CandidatePreparationResult.Ready>().single().candidate
            }
        }
        operations.verifyAction = dev.silksong.launcher.skins.importing.SkinObjectBuilder(fs)::verifyPrepared
        val bytes = dev.silksong.launcher.skins.fixtures.RawZipFixture.one(
            name = "Pack/Knight.png", data = dev.silksong.launcher.skins.fixtures.TinyPngFixture.rgba(),
        ).bytes
        val handle = assertOk(SkinImportCoordinator.prepare(SkinImportInput.SelectedFile("skin.zip") {
            ByteArrayInputStream(bytes)
        }))
        val payload = requireNotNull(retained).payloads.single().file
        val corrupt = payload.readBytes()
        corrupt[corrupt.lastIndex] = (corrupt.last().toInt() xor 1).toByte()
        payload.writeBytes(corrupt)
        val result = assertOk(SkinImportCoordinator.commitImport(handle.handleId)).single()
        assertEquals(SkinImportCode.DOCUMENT_INVALID, result.code)
        assertEquals(0, operations.recoverCalls)
        assertEquals(0, operations.buildCalls)
        assertEquals(0, operations.publishCalls)
        assertEquals(0, operations.commitCalls)
        assertFalse(File(paths.root, "registry").exists())
    }

    @Test
    fun claimedHandleReverifiesContainmentAndCandidateKeyBeforeBuild() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "first")) }
        operations.verifyAction = {
            SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "candidate key changed in staging")
        }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        val results = assertOk(SkinImportCoordinator.commitImport(handle.handleId))

        assertEquals(SkinImportCode.DOCUMENT_INVALID, results.single().code)
        assertEquals(1, operations.verifyCalls)
        assertEquals(0, operations.buildCalls)
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
    }

    @Test
    fun sameKeyReplaceRejectsRenamedOwnerBeforeBuild() = withCoordinator {
        val key = hex('a')
        val target = RegistryPack("target", "Original", "Unknown", key, hex('1'), hex('2'), hex('3'), true)
        operations.registryHead = registryHead(target)
        val original = operations.registryHead
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, key, "Renamed", hex('4'))) }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
        assertError(
            SkinImportCode.CANDIDATE_ALREADY_INSTALLED,
            SkinImportCoordinator.commitReplace(handle.handleId, key, target.id, original.sha256,
                target.treeSha256, target.importReceiptSha256),
        )
        assertEquals(0, operations.buildCalls)
        assertEquals(0, operations.publishCalls)
        assertEquals(0, operations.commitCalls)
        assertEquals(original, operations.registryHead)
    }

    @Test
    fun threeCandidateReplaceSelectsExactSourceKey() = withCoordinator {
        val sourceKey = hex('b')
        operations.preparationFactory = { archive ->
            listOf(
                operations.prepared(archive, hex('a'), "first"),
                operations.prepared(archive, sourceKey, "second"),
                operations.prepared(archive, hex('c'), "third"),
            )
        }
        operations.registryHead = registryHead(
            RegistryPack("target", "Old", "Unknown", hex('d'), hex('1'), hex('2'), hex('3'), true),
        )
        operations.buildAction = { prepared, id -> SkinResult.Ok(operations.built(prepared, id)) }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))
        val expected = operations.registryHead

        val result = assertOk(
            SkinImportCoordinator.commitReplace(
                handle.handleId,
                sourceKey,
                "target",
                expected.sha256,
                hex('1'),
                hex('3'),
            ),
        )

        assertEquals(SkinImportCode.OK, result.code)
        assertEquals(listOf(sourceKey), operations.builtCandidateKeys)
        assertEquals(listOf("target"), operations.builtIds)
        val replacement = operations.registryHead.document.packs.single()
        assertEquals(sourceKey, replacement.candidateKey)
        assertTrue(replacement.rotationEligible)
    }

    @Test
    fun absentOrDuplicateReplaceKeyRejectsBeforeBuild() = withCoordinator {
        val duplicateKey = hex('a')
        operations.preparationFactory = { archive ->
            listOf(
                operations.prepared(archive, duplicateKey, "first"),
                operations.prepared(archive, duplicateKey, "second"),
            )
        }
        val target = RegistryPack("target", "Old", "Unknown", hex('d'), hex('1'), hex('2'), hex('3'), false)
        operations.registryHead = registryHead(target)
        val duplicate = assertOk(SkinImportCoordinator.prepare(zipInput()))

        assertError(
            SkinImportCode.INVALID_INPUT,
            SkinImportCoordinator.commitReplace(
                duplicate.handleId,
                duplicateKey,
                target.id,
                operations.registryHead.sha256,
                target.treeSha256,
                target.importReceiptSha256,
            ),
        )
        assertEquals(0, operations.buildCalls)

        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('b'), "only")) }
        val absent = assertOk(SkinImportCoordinator.prepare(zipInput()))
        assertError(
            SkinImportCode.NO_CANDIDATE,
            SkinImportCoordinator.commitReplace(
                absent.handleId,
                duplicateKey,
                target.id,
                operations.registryHead.sha256,
                target.treeSha256,
                target.importReceiptSha256,
            ),
        )
        assertEquals(0, operations.buildCalls)
    }

    @Test
    fun replacesOnlyConfirmedCasTarget() = withCoordinator {
        val sourceKey = hex('a')
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, sourceKey, "new")) }
        val target = RegistryPack("target", "Old", "Unknown", hex('d'), hex('1'), hex('2'), hex('3'), true)
        operations.registryHead = registryHead(target).let { current ->
            current.copy(
                document = current.document.copy(
                    activation = current.document.activation.copy(selectedPackId = target.id, skinStamp = 7),
                ),
            ).rehashed()
        }
        val wrongGeneration = assertOk(SkinImportCoordinator.prepare(zipInput()))

        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            SkinImportCoordinator.commitReplace(
                wrongGeneration.handleId,
                sourceKey,
                target.id,
                hex('f'),
                target.treeSha256,
                target.importReceiptSha256,
            ),
        )
        assertEquals(0, operations.buildCalls)

        val wrongTree = assertOk(SkinImportCoordinator.prepare(zipInput()))
        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            SkinImportCoordinator.commitReplace(
                wrongTree.handleId,
                sourceKey,
                target.id,
                operations.registryHead.sha256,
                hex('e'),
                target.importReceiptSha256,
            ),
        )
        assertEquals(0, operations.buildCalls)
    }

    @Test
    fun reimportsOwnerIdempotentlyWithoutReceiptRename() = withCoordinator {
        val key = hex('a')
        val owner = RegistryPack("owned-id", "Original name", "Unknown", key, hex('1'), hex('2'), hex('3'), true)
        operations.registryHead = registryHead(owner)
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, key, "Renamed provider", hex('4'))) }
        operations.buildAction = { prepared, id ->
            SkinResult.Ok(
                operations.built(prepared, id).copy(
                    treeSha256 = owner.treeSha256,
                    contentSha256 = owner.contentSha256,
                    importReceiptSha256 = hex('4'),
                ),
            )
        }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        val result = assertOk(SkinImportCoordinator.commitImport(handle.handleId)).single()

        assertEquals(SkinImportCode.OK, result.code)
        assertEquals(owner.id, result.published?.id)
        assertEquals(owner.name, result.published?.name)
        assertEquals(owner.importReceiptSha256, result.published?.importReceiptSha256)
        assertEquals(listOf(owner.id), operations.builtIds)
        assertEquals(0, operations.publishCalls)
        assertEquals(0, operations.commitCalls)
    }

    @Test
    fun candidateSemanticFailureDoesNotBlockValidSibling() = withCoordinator {
        val rejectedKey = hex('a')
        val acceptedKey = hex('b')
        operations.preparationFactory = { archive ->
            listOf(
                operations.prepared(archive, rejectedKey, "bad"),
                operations.prepared(archive, acceptedKey, "good"),
            )
        }
        operations.buildAction = { prepared, id ->
            if (prepared.candidateKey == rejectedKey) {
                SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "candidate is invalid")
            } else {
                SkinResult.Ok(operations.built(prepared, id))
            }
        }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        val results = assertOk(SkinImportCoordinator.commitImport(handle.handleId))

        assertEquals(listOf(SkinImportCode.DOCUMENT_INVALID, SkinImportCode.OK), results.map { it.code })
        assertEquals(listOf(rejectedKey, acceptedKey), operations.builtCandidateKeys)
        assertEquals(1, operations.publishCalls)
        assertEquals(1, operations.commitCalls)
        assertEquals(acceptedKey, operations.registryHead.document.packs.single().candidateKey)
    }

    @Test
    fun failedCommitRemovesOnlyUnreferencedNewPublications() = withCoordinator {
        val key = hex('a')
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, key, "candidate")) }
        operations.commitAction = { _, _, _ -> SkinResult.Error(SkinImportCode.REGISTRY_CONFLICT, "CAS changed") }
        operations.referenceDigests = setOf(hex('9'))
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        val result = assertOk(SkinImportCoordinator.commitImport(handle.handleId)).single()

        assertEquals(SkinImportCode.REGISTRY_CONFLICT, result.code)
        assertEquals(1, operations.cleanupCalls)
        assertEquals(setOf(hex('9')), operations.lastCleanupReferences)
        assertEquals(1, operations.publishCalls)
    }

    @Test
    fun indeterminateReferenceEvidenceRetainsPublicationOwnership() = withCoordinator {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "candidate")) }
        operations.commitAction = { _, _, _ -> SkinResult.Error(SkinImportCode.REGISTRY_CONFLICT, "CAS changed") }
        operations.referenceAction = {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "pointer evidence changed")
        }
        val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.commitImport(handle.handleId))
        assertEquals(0, operations.cleanupCalls)
        assertEquals(1, operations.publishCalls)
    }

    @Test
    fun quotaRejectionPerCandidateMutatesNoBuildPublicationOrRegistry() {
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, hex('a'), "candidate")) }
        val rejecting = RejectingNthQuota(paths.root, rejectAt = 3)
        withCoordinator(rejecting) {
            val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

            assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, SkinImportCoordinator.commitImport(handle.handleId))
            assertEquals(0, operations.recoverCalls)
            assertEquals(0, operations.buildCalls)
            assertEquals(0, operations.publishCalls)
            assertEquals(0, operations.commitCalls)
            assertFalse(paths.importHandleOwner(handle.handleId).exists())
        }
    }

    @Test
    fun prepareCleanupAmbiguityOverridesCandidatePreparationFailure() {
        operations.normalizeAction = {
            SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "normalization failed")
        }
        fs = FaultingSkinFileSystem(fs).apply {
            skipPhysicalSyncs = true
            beforeDelete = { path, _ ->
                if (path.parentFile?.absoluteFile?.normalize() == paths.importHandles.absoluteFile.normalize()) {
                    throw IllegalStateException("owner cleanup failed")
                }
            }
        }

        withCoordinator {
            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCoordinator.prepare(zipInput()))
            assertEquals(1, paths.importHandles.listFiles().orEmpty().size)
        }
    }

    @Test
    fun replaceQuotaRejectionOccursBeforeBuildPublicationOrRegistryMutation() {
        val key = hex('a')
        operations.preparationFactory = { archive -> listOf(operations.prepared(archive, key, "candidate")) }
        val target = RegistryPack("target", "Old", "Unknown", hex('d'), hex('1'), hex('2'), hex('3'), false)
        operations.registryHead = registryHead(target)
        val rejecting = RejectingNthQuota(paths.root, rejectAt = 3)
        withCoordinator(rejecting) {
            val handle = assertOk(SkinImportCoordinator.prepare(zipInput()))

            assertError(
                SkinImportCode.PROFILE_QUOTA_EXCEEDED,
                SkinImportCoordinator.commitReplace(
                    handle.handleId,
                    key,
                    target.id,
                    operations.registryHead.sha256,
                    target.treeSha256,
                    target.importReceiptSha256,
                ),
            )
            assertEquals(0, operations.recoverCalls)
            assertEquals(0, operations.verifyCalls)
            assertEquals(0, operations.buildCalls)
            assertEquals(0, operations.publishCalls)
            assertEquals(0, operations.commitCalls)
            assertFalse(paths.importHandleOwner(handle.handleId).exists())
        }
    }

    private fun <T> withCoordinator(action: () -> T): T = withCoordinator(quota, action)

    private fun <T> withCoordinator(admission: SkinQuotaAdmission, action: () -> T): T {
        val quarantine = SkinQuarantine(paths, fs, SkinQuotaCapacityReserver(admission))
        return SkinImportCoordinator.withTestBinding(
            SkinImportCoordinatorDependencies(
                paths = paths,
                fileSystem = fs,
                lockManager = SkinLockManager(paths.root),
                quota = admission,
                quarantine = quarantine,
                operations = operations,
            ),
            action,
        )
    }

    private fun zipInput(opens: AtomicInteger = AtomicInteger()) = SkinImportInput.SelectedFile("skin.zip") {
        opens.incrementAndGet()
        ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 0x03, 0x04))
    }

    private fun dataFields(type: Class<*>): Set<String> = type.declaredFields
        .filterNot { it.isSynthetic || it.name.startsWith("$") }
        .mapTo(linkedSetOf()) { it.name }

    private fun hex(character: Char) = character.toString().repeat(64)

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private fun registryHead(vararg packs: RegistryPack): RegistryHead {
        val genesis = SkinRegistryAuthority.genesis()
        val generation = "62345678-1234-4234-8234-123456789abc"
        val document = genesis.copy(
            generationId = generation,
            sequence = 1,
            parentGenerationId = genesis.generationId,
            operationId = generation,
            writer = "test",
            packs = packs.sortedBy(RegistryPack::id),
        )
        return RegistryHead(generation, 1, digest(assertOk(SkinRegistryDocumentCodec.canonical(document))), document)
    }

    private fun RegistryHead.rehashed(): RegistryHead = copy(
        sha256 = digest(assertOk(SkinRegistryDocumentCodec.canonical(document))),
    )

    private fun digest(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private class RejectingNthQuota(
        override val root: File,
        private val rejectAt: Int,
    ) : SkinQuotaAdmission {
        private var requests = 0

        override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> {
            requests++
            if (requests == rejectAt) {
                return SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "quota full")
            }
            return SkinResult.Ok(
                object : SkinQuotaReservation {
                    override fun transfer(anchor: File, actual: SkinQuotaRequest) = Unit
                    override fun release() = Unit
                },
            )
        }
    }

    private inner class FakeCoordinatorOperations : SkinImportCoordinatorOperations {
        var gate = LeaseMutationGate.CLEAR
        var gateReads = 0
        var verifyCalls = 0
        var recoverCalls = 0
        var buildCalls = 0
        var publishCalls = 0
        var commitCalls = 0
        var cleanupCalls = 0
        var gateAction: () -> LeaseMutationGate = { gate }
        var preparationFactory: (QuarantinedArchive) -> List<CandidatePreparationResult> = {
            listOf(CandidatePreparationResult.Rejected("pack".toByteArray(), SkinImportCode.NO_CANDIDATE, "no skin"))
        }
        var normalizeAction: (QuarantinedArchive) -> SkinResult<List<CandidatePreparationResult>> = { archive ->
            SkinResult.Ok(preparationFactory(archive))
        }
        var verifyAction: (PreparedSkinCandidate) -> SkinResult<Unit> = { SkinResult.Ok(Unit) }
        var buildAction: (PreparedSkinCandidate, String) -> SkinResult<BuiltSkin> = { prepared, id ->
            SkinResult.Ok(built(prepared, id))
        }
        var publishAction: (BuiltSkin) -> SkinResult<PublishedSkin> = { built ->
            SkinResult.Ok(
                PublishedSkin(
                    built.id,
                    built.candidateKey,
                    built.name,
                    built.contentSha256,
                    built.treeSha256,
                    built.manifestSha256,
                    built.importReceiptSha256,
                    paths.objectRoot(built.treeSha256),
                    listOf(paths.objectRoot(built.treeSha256), paths.importReceiptRoot(built.importReceiptSha256)),
                ),
            )
        }
        var commitAction: (RegistryHead, UUID, RegistryMutation) -> SkinResult<RegistryHead> = { expected, operation, mutation ->
            when (val mutated = mutation.apply(expected.document)) {
                is SkinResult.Error -> mutated
                is SkinResult.Ok -> {
                    val child = mutated.value.copy(
                        generationId = operation.toString(),
                        sequence = expected.sequence + 1,
                        parentGenerationId = expected.generationId,
                        operationId = operation.toString(),
                        writer = "import-coordinator",
                    )
                    val result = RegistryHead(
                        child.generationId,
                        child.sequence,
                        digest(assertOk(SkinRegistryDocumentCodec.canonical(child))),
                        child,
                    )
                    registryHead = result
                    SkinResult.Ok(result)
                }
            }
        }
        var referenceDigests: Set<String> = emptySet()
        var referenceAction: () -> SkinResult<Set<String>> = {
            SkinResult.Ok(
                referenceDigests + registryHead.document.packs.flatMap { listOf(it.treeSha256, it.importReceiptSha256) },
            )
        }
        var settleAction: (Set<String>) -> SkinResult<Unit> = { SkinResult.Ok(Unit) }
        var registryHead: RegistryHead = registryHead()
        val builtIds = mutableListOf<String>()
        val builtCandidateKeys = mutableListOf<String>()
        var lastCleanupReferences: Set<String>? = null
        private var candidateIndex = 0
        private var normalizationArchiveParent: File? = null

        override fun mutationGate(): LeaseMutationGate {
            gateReads++
            return gateAction()
        }

        override fun normalize(archive: QuarantinedArchive): SkinResult<List<CandidatePreparationResult>> =
            normalizeAction(archive)

        fun prepared(
            archive: QuarantinedArchive,
            candidateKey: String,
            name: String,
            receiptSha256: String = hex('3'),
        ): CandidatePreparationResult.Ready {
            val archiveParent = archive.file.parentFile.absoluteFile.normalize()
            if (normalizationArchiveParent != archiveParent) {
                normalizationArchiveParent = archiveParent
                candidateIndex = 0
            }
            candidateIndex++
            val root = File(
                archiveParent,
                "normalized-1/candidate-${candidateIndex.toString().padStart(3, '0')}",
            ).apply { mkdirs() }
            val payload = File(root, "payload").apply { writeBytes(byteArrayOf(1)) }
            return CandidatePreparationResult.Ready(
                PreparedSkinCandidate(
                    candidateKey = candidateKey,
                    rawPrefix = name.toByteArray(),
                    layoutCode = 3,
                    name = name,
                    contentSha256 = hex('2'),
                    importReceiptBytes = byteArrayOf(candidateIndex.toByte()),
                    importReceiptSha256 = receiptSha256,
                    payloads = listOf(StagedPayload("assets/fake", hex('5'), 1L, payload)),
                    mappings = mapOf("Knight.png" to "fake"),
                    stagingRoot = root,
                ),
            )
        }

        fun built(prepared: PreparedSkinCandidate, id: String): BuiltSkin = BuiltSkin(
            id = id,
            candidateKey = prepared.candidateKey,
            name = prepared.name,
            contentSha256 = prepared.contentSha256,
            treeSha256 = digest("tree:$id:${prepared.candidateKey}".toByteArray()),
            manifestSha256 = digest("manifest:$id".toByteArray()),
            importReceiptSha256 = prepared.importReceiptSha256,
            manifestBytes = byteArrayOf(1),
            objectBytes = byteArrayOf(2),
            importReceiptBytes = prepared.importReceiptBytes,
            ephemeralRoot = File(prepared.stagingRoot.parentFile, "object-$id"),
        )

        override fun verify(prepared: PreparedSkinCandidate): SkinResult<Unit> {
            verifyCalls++
            return verifyAction(prepared)
        }

        override fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin> {
            buildCalls++
            builtIds += id
            builtCandidateKeys += prepared.candidateKey
            return buildAction(prepared, id)
        }

        override fun discard(built: BuiltSkin): SkinResult<Unit> = SkinResult.Ok(Unit)

        override fun publish(built: BuiltSkin): SkinResult<PublishedSkin> {
            publishCalls++
            return publishAction(built)
        }

        override fun recoverRegistry(): SkinResult<RegistryHead> {
            recoverCalls++
            return SkinResult.Ok(registryHead)
        }

        override fun commitRegistry(
            expected: RegistryHead,
            operationId: UUID,
            mutation: RegistryMutation,
        ): SkinResult<RegistryHead> {
            commitCalls++
            return commitAction(expected, operationId, mutation)
        }

        override fun referenceSnapshot(): SkinResult<Set<String>> = referenceAction()

        override fun discardUnreferenced(
            published: PublishedSkin,
            referencedDigests: Set<String>,
        ): SkinResult<Unit> {
            cleanupCalls++
            lastCleanupReferences = referencedDigests
            return SkinResult.Ok(Unit)
        }

        override fun settlePublications(referencedDigests: Set<String>): SkinResult<Unit> = settleAction(referencedDigests)
    }
}
