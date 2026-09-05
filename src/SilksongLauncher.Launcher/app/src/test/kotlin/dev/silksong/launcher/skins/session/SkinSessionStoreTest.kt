package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.quota.SkinQuota
import dev.silksong.launcher.skins.quota.SkinQuotaAccountingAuthority
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaLimits
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.quota.SkinQuotaUsage
import dev.silksong.launcher.skins.registry.SkinLockManager
import java.io.File
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinSessionStoreTest {
    private lateinit var testRoot: File
    private lateinit var skinsRoot: File

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-session-store").absoluteFile
        testRoot.deleteRecursively()
        skinsRoot = File(testRoot, "profiles/hollow-knight/skins").apply { mkdirs() }
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun fastLeaseFilesystemRejectsContainedSymlink() {
        val root = File(testRoot, "fast-fs/root").apply { mkdirs() }
        val outside = File(testRoot, "fast-fs/outside").apply { mkdirs() }
        val alias = File(root, "alias")
        Files.createSymbolicLink(alias.toPath(), outside.toPath())

        assertThrows(IllegalArgumentException::class.java) {
            FastSkinFileSystem().requireContained(File(alias, "lease.json"), root, allowMissingLeaf = true)
        }
    }

    @Test
    fun leaseUnitSyncModeStillRecordsAndInjectsEverySyncBoundary() {
        val fs = leaseFs().apply { skipPhysicalSyncs = true }
        val missing = File(testRoot, "missing-sync-target")

        fs.syncFile(missing)
        fs.syncDirectory(missing)

        assertTrue(fs.events.contains("sync-file:${missing.name}"))
        assertTrue(fs.events.contains("sync-dir:${missing.name}"))
        fs.failOnEvent = "sync-file:${missing.name}"
        fs.failOnOccurrence = 2
        assertThrows(IllegalStateException::class.java) { fs.syncFile(missing) }
    }

    @Test
    fun sessionStoreSourceUsesVisibleNulEscapeAndRemainsOrdinaryText() {
        val sourceFile = File("src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionStore.kt")
        val bytes = sourceFile.readBytes()
        val source = bytes.toString(StandardCharsets.UTF_8)

        assertFalse(bytes.contains(0))
        assertTrue(source.contains("separator = \"\\u0000\""))
        assertTrue(Regex("private fun ownerKey").containsMatchIn(source))
        assertTrue(Regex("internal object LeasePointerCodec").containsMatchIn(source))
    }

    @Test
    fun canonicalLeaseStateRoundTripsWithoutRawToken() {
        val state = pendingState()
        val bytes = SkinLeaseStateCodec.canonical(state)

        assertArrayEquals(bytes, SkinLeaseStateCodec.canonical(assertOk(SkinLeaseStateCodec.parse(bytes))))
        assertFalse(bytes.toString(StandardCharsets.UTF_8).contains(rawLeaseToken))
    }

    @Test
    fun canonicalLeaseStateRejectsStrictNegativeForms() {
        val canonical = SkinLeaseStateCodec.canonical(pendingState()).toString(StandardCharsets.UTF_8)
        val invalid = listOf(
            canonical.replaceFirst("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            canonical.dropLast(1) + ",\"unknown\":\"x\"}",
            canonical.replace("\"profileId\":\"hollow-knight\"", "\"profileId\":null"),
            canonical.replace("\"sessionSequence\":\"7\"", "\"sessionSequence\":\"07\""),
            canonical.replace(descriptorId.toString(), descriptorId.toString().uppercase()),
            canonical.replace("\"descriptorSha256\":\"${"b".repeat(64)}\"", "\"descriptorSha256\":\"${"B".repeat(64)}\""),
            canonical.dropLast(1) + ",\"leaseToken\":\"$rawLeaseToken\"}",
        )

        invalid.forEach { bytes -> assertError(SkinImportCode.DOCUMENT_INVALID, SkinLeaseStateCodec.parse(bytes.toByteArray())) }
    }

    @Test
    fun canonicalClosedStateRequiresItsStandaloneOwnerAndSequenceShape() {
        val pending = pendingState()
        val directClose = closedFromPending(pending)
        val ownedClose = closedFromOwned(gameOwned(pending))

        listOf(directClose, ownedClose).forEach { state ->
            val canonical = SkinLeaseStateCodec.canonical(state)
            assertArrayEquals(canonical, SkinLeaseStateCodec.canonical(assertOk(SkinLeaseStateCodec.parse(canonical))))
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.canonical(directClose.copy(gameOwner = gameOwner))
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.canonical(ownedClose.copy(gameOwner = null))
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.canonical(directClose.copy(transitionSequence = 3))
        }

        val seqOneWithOwner = SkinLeaseStateCodec.canonical(ownedClose)
            .toString(StandardCharsets.UTF_8)
            .replace("\"transitionSequence\":\"2\"", "\"transitionSequence\":\"1\"")
        val seqTwoWithoutOwner = SkinLeaseStateCodec.canonical(directClose)
            .toString(StandardCharsets.UTF_8)
            .replace("\"transitionSequence\":\"1\"", "\"transitionSequence\":\"2\"")
        val invalidSequence = SkinLeaseStateCodec.canonical(directClose)
            .toString(StandardCharsets.UTF_8)
            .replace("\"transitionSequence\":\"1\"", "\"transitionSequence\":\"3\"")
        listOf(seqOneWithOwner, seqTwoWithoutOwner, invalidSequence).forEach { bytes ->
            assertError(SkinImportCode.DOCUMENT_INVALID, SkinLeaseStateCodec.parse(bytes.toByteArray()))
        }
    }

    @Test
    fun leaseTransitionRequiresImmediateParentAndUnchangedIdentity() {
        val pending = pendingState()
        val owned = gameOwned(pending)

        SkinLeaseStateCodec.requireImmediateChild(owned, pending)
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.requireImmediateChild(owned.copy(sessionSequence = pending.sessionSequence + 1), pending)
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.requireImmediateChild(owned.copy(parentTransitionId = UUID.randomUUID()), pending)
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinLeaseStateCodec.requireImmediateChild(
                owned.copy(state = LeaseState.LAUNCH_PENDING, transitionSequence = 0, parentTransitionId = null, gameOwner = null),
                pending,
            )
        }
    }

    @Test
    fun revalidatesEveryStagedLeaseLeafBeforeItsWriteAndRejectsContainmentSwap() {
        listOf("lease.json", "lease.sha256", ".complete").forEachIndexed { index, target ->
            val root = File(testRoot, "stage-race-$index/profiles/hollow-knight/skins").apply { mkdirs() }
            val fs = FaultingSkinFileSystem()
            val handle = handle(
                descriptor = UUID.nameUUIDFromBytes("stage-descriptor-$index".toByteArray(StandardCharsets.US_ASCII)),
                lease = UUID.nameUUIDFromBytes("stage-lease-$index".toByteArray(StandardCharsets.US_ASCII)),
            )
            val stagingDirectory = File(root, "staging/lease-${handle.descriptorId}-${handle.leaseId}-0")
            val outside = File(testRoot, "stage-race-outside-$index").apply { mkdirs() }
            var containmentSwapAttempted = false
            var injectionFailure: Throwable? = null
            fs.beforeContainment = { path, _, _ ->
                if (path.name == target && !containmentSwapAttempted) {
                    containmentSwapAttempted = true
                    injectionFailure = runCatching {
                        check(stagingDirectory.deleteRecursively()) { "Cannot remove staging directory" }
                        Files.createSymbolicLink(stagingDirectory.toPath(), outside.toPath())
                        check(Files.isSymbolicLink(stagingDirectory.toPath())) { "Staging path is not a symbolic link" }
                    }.exceptionOrNull()
                }
            }

            assertError(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                SkinSessionStore(root, fs, SkinLockManager(root), null, PermissiveTestSkinQuota(root)).establishPendingForCoordinator(
                    handle,
                    launcherOwner,
                    registryGenerationId,
                    registrySha256,
                ),
            )

            assertTrue("No containment race was injected for $target", containmentSwapAttempted)
            assertTrue("Containment race injection failed for $target: $injectionFailure", injectionFailure == null)
            assertTrue("Containment race did not replace staging with a symbolic link for $target", Files.isSymbolicLink(stagingDirectory.toPath()))
            assertFalse("Staging race wrote outside its authority for $target", File(outside, target).exists())
            assertFalse("$target write preceded staging containment revalidation: ${fs.events}", "write-new:$target" in fs.events)
        }
    }

    @Test
    fun retainsVerifiedPublicationAndReadsEachParentStateOncePerTraversal() {
        val fs = leaseFs()
        val store = store(fs)
        val handle = handle()
        val pendingJson = leaseStateFile(handle, 0, "lease.json")
        val pendingSha256 = leaseStateFile(handle, 0, "lease.sha256")
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        assertEquals(1, fs.contentReadCount(pendingJson))
        assertEquals(1, fs.contentReadCount(pendingSha256))

        assertOk(store.claim(handle, gameOwner))
        val ownedJson = leaseStateFile(handle, 1, "lease.json")
        val ownedSha256 = leaseStateFile(handle, 1, "lease.sha256")
        fs.clearContentReadCounts()
        assertOk(store.claim(handle, gameOwner))

        assertEquals(1, fs.contentReadCount(ownedJson))
        assertEquals(1, fs.contentReadCount(ownedSha256))
        assertEquals(2, fs.contentReadCount(pendingJson))
        assertEquals(2, fs.contentReadCount(pendingSha256))
    }

    @Test
    fun establishesOnlyOneActiveLeaseByExactAbsentCas() {
        val store = store()
        val first = handle()

        val established = assertOk(store.establishPendingForCoordinator(first, launcherOwner, registryGenerationId, registrySha256))
        assertError(
            SkinImportCode.LIFECYCLE_BLOCKED,
            store.establishPendingForCoordinator(handle(descriptor = UUID.randomUUID(), lease = UUID.randomUUID()), launcherOwner, registryGenerationId, registrySha256),
        )

        assertEquals(LeaseState.LAUNCH_PENDING, established.state)
        val leaseBytes = File(skinsRoot, "sessions/${first.descriptorId}/lease/states").listFiles()!!.single()
            .resolve("lease.json")
            .readText()
        assertTrue(leaseBytes.contains(SkinLeaseStateCodec.rawTokenSha256(rawLeaseToken)))
        assertFalse(leaseBytes.contains(rawLeaseToken))
    }

    @Test
    fun rejectsStaleHandlesAndWrongTokensBeforeClaim() {
        val store = store()
        val handle = handle()
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, store.claim(handle.copy(leaseToken = "f".repeat(64)), gameOwner))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, store.claim(handle, gameOwner.copy(pid = 0)))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, store.claim(handle.copy(descriptorSha256 = "d".repeat(64)), gameOwner))
        assertOk(store.claim(handle, gameOwner))
        assertError(
            SkinImportCode.LIFECYCLE_BLOCKED,
            store.claim(handle(descriptor = UUID.randomUUID(), lease = UUID.randomUUID()), gameOwner),
        )
    }

    @Test
    fun claimAndCloseQuotaRejectionPrecedesEveryMutation() {
        val fs = leaseFs()
        val admitted = SkinSessionStore(
            skinsRoot,
            fs,
            SkinLockManager(skinsRoot),
            null,
            PermissiveTestSkinQuota(skinsRoot),
        )
        val handle = handle()
        assertOk(admitted.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        val requests = mutableListOf<SkinQuotaRequest>()
        val rejectingQuota = object : SkinQuotaAdmission {
            override val root = skinsRoot
            override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> {
                requests += request
                return SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "full")
            }
        }
        val guarded = SkinSessionStore(skinsRoot, fs, SkinLockManager(skinsRoot), null, rejectingQuota)
        val unusedLiveness = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = error("quota rejection must precede liveness")
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness =
                error("quota rejection must precede liveness")
            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence =
                error("quota rejection must precede liveness")
        }
        val beforeRecovery = evidenceSnapshot(skinsRoot)
        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, guarded.recover(unusedLiveness))
        assertEquals(beforeRecovery, evidenceSnapshot(skinsRoot))
        assertEquals(listOf(SkinQuotaBudgets.SESSION_RECOVERY), requests)
        requests.clear()
        assertEquals(LeaseMutationGate.UNKNOWN, guarded.mutationGate(unusedLiveness))
        assertEquals(beforeRecovery, evidenceSnapshot(skinsRoot))
        assertEquals(listOf(SkinQuotaBudgets.SESSION_RECOVERY), requests)
        requests.clear()

        val beforeClaim = evidenceSnapshot(skinsRoot)

        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, guarded.claim(handle, gameOwner))
        assertEquals(beforeClaim, evidenceSnapshot(skinsRoot))
        assertEquals(listOf(SkinQuotaBudgets.SESSION_CLAIM), requests)

        assertOk(admitted.claim(handle, gameOwner))
        requests.clear()
        val beforeClose = evidenceSnapshot(skinsRoot)
        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, guarded.close(handle, "GAME_EXIT"))
        assertEquals(beforeClose, evidenceSnapshot(skinsRoot))
        assertEquals(listOf(SkinQuotaBudgets.SESSION_CLOSE), requests)
    }

    @Test
    fun lifecycleMarginKeepsMutationRecoveryClaimAndCloseAvailableAtOrdinaryCeiling() {
        val fs = leaseFs()
        val admitted = SkinSessionStore(
            skinsRoot,
            fs,
            SkinLockManager(skinsRoot),
            null,
            PermissiveTestSkinQuota(skinsRoot),
        )
        val handle = handle()
        val limits = SkinQuotaLimits.V1
        val margin = SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val quota = SkinQuota.testing(
            skinsRoot,
            fs,
            SkinQuotaAccountingAuthority {
                SkinQuotaUsage(
                    profileBytes = limits.profileBytes - margin,
                    sessionBytes = limits.sessionBytes - margin,
                )
            },
            limits,
        )
        val guarded = SkinSessionStore(skinsRoot, fs, SkinLockManager(skinsRoot), null, quota)
        val alive = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = SelfIdentityResult.Known(launcherOwner)
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness =
                ExpectedOwnerLiveness.Alive(expected)
            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence =
                ExactProcessPresence.Unknown
        }

        assertOk(quota.reserve(SkinQuotaBudgets.SESSION_RECOVERY)).release()
        assertEquals(null, assertOk(guarded.recover(alive)))
        assertEquals(LeaseMutationGate.CLEAR, guarded.mutationGate(alive))
        assertOk(admitted.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        assertEquals(LeaseState.GAME_OWNED, assertOk(guarded.claim(handle, gameOwner)).state)
        assertEquals(LeaseState.CLOSED, assertOk(guarded.close(handle, "GAME_EXIT")).state)
    }

    @Test
    fun claimsAndClosesOnlyAfterDurableActiveBarrier() {
        val fs = leaseFs()
        val store = store(fs)
        val handle = handle()
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))

        val claimStart = fs.events.size
        failAtActiveParentBarrier(fs, relativeOccurrence = 2)
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.claim(handle, gameOwner))
        fs.failOnEvent = null
        val owned = assertOk(store.claim(handle, gameOwner))
        assertTransitionPointerOrder(fs.events.drop(claimStart), "rename:active.tmp->active")

        val closeStart = fs.events.size
        failAtActiveParentBarrier(fs, relativeOccurrence = 2)
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.close(handle, "GAME_EXIT"))
        fs.failOnEvent = null
        val closed = assertOk(store.close(handle, "GAME_EXIT"))
        assertTransitionPointerOrder(fs.events.drop(closeStart), "delete-contained:active")

        assertEquals(LeaseState.GAME_OWNED, owned.state)
        assertEquals(LeaseState.CLOSED, closed.state)
    }

    @Test
    fun retriesEveryLeasePointerAndActiveParentBarrierIdempotently() {
        listOf(
            "write-new:next.tmp" to 1,
            "sync-file:next.tmp" to 1,
            "rename:next.tmp->next" to 1,
            "sync-dir:lease" to 2,
            "write-new:previous.tmp" to 1,
            "sync-file:previous.tmp" to 1,
            "rename:previous.tmp->previous" to 1,
            "sync-dir:lease" to 3,
            "write-new:current.tmp" to 1,
            "sync-file:current.tmp" to 1,
            "rename:current.tmp->current" to 1,
            "sync-dir:lease" to 4,
            "delete-contained:next" to 1,
            "sync-dir:lease" to 5,
            "write-new:active.tmp" to 1,
            "sync-file:active.tmp" to 1,
            "rename:active.tmp->active" to 1,
            "sync-dir:sessions" to 2,
        ).forEachIndexed { index, (event, relativeOccurrence) ->
            retriesClaimAfter(event, relativeOccurrence, index)
        }
    }

    @Test
    fun retriesWhenActiveRemovalCannotStart() {
        val fs = leaseFs()
        val store = store(fs)
        val handle = handle()
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        assertOk(store.claim(handle, gameOwner))
        fs.failOnEvent = "delete-contained:active"
        fs.failOnOccurrence = fs.events.count { it == "delete-contained:active" } + 1

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.close(handle, "GAME_EXIT"))

        fs.failOnEvent = null
        assertEquals(LeaseState.CLOSED, assertOk(store.close(handle, "GAME_EXIT")).state)
    }

    @Test
    fun pendingLeaseCanCloseWithoutGameOwnership() {
        val store = store()
        val handle = handle()
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))

        val closed = assertOk(store.close(handle, "LAUNCH_FAILED"))

        assertEquals(LeaseState.CLOSED, closed.state)
        assertEquals(2, stateDirectories(skinsRoot, handle.descriptorId).size)
    }

    @Test
    fun closedLeaseIsTerminalAndPublicCloseRemainsTokenAuthorized() {
        val store = store()
        val handle = handle()
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        assertOk(store.claim(handle, gameOwner))
        assertOk(store.close(handle, "GAME_EXIT"))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, store.claim(handle, gameOwner))
        assertEquals(LeaseState.CLOSED, assertOk(store.close(handle, "GAME_EXIT")).state)
        assertEquals(3, stateDirectories(skinsRoot, handle.descriptorId).size)
    }

    @Test
    fun hasNoProductionLeaseEstablishmentOrAcquisitionWiring() {
        val sourceRoot = File("src/main")
        val authorities = setOf(
            "SkinSessionStore.kt",
            "SkinLaunchCoordinator.kt",
            "SkinSessionBridgeProtocol.kt",
            "SkinAcquisitionIntent.kt",
        ).map { name ->
            File(sourceRoot, "kotlin/dev/silksong/launcher/skins/session/$name").canonicalFile
        }.toSet()
        val sources = sourceRoot.walkTopDown()
            .filter { file -> file.isFile && (file.extension == "kt" || file.name == "AndroidManifest.xml") }
            .filter { file -> file.canonicalFile !in authorities }
            .toList()
        assertTrue("Production source scan escaped its bound", sources.size in 1..256)
        assertTrue("Production source scan exceeded its file bound", sources.all { it.length() <= 1024 * 1024 })
        val seamSources = authorities.map(File::readText)
        val forbiddenSeamWiring = listOf(
            "android.content.Intent",
            "android.app.Activity",
            "GameProcessStartup",
            "startActivity(",
        )
        assertTrue(
            "Non-production session seams gained Android launch wiring",
            seamSources.none { source -> forbiddenSeamWiring.any(source::contains) },
        )
        assertTrue(
            "Production source scan found forbidden lease wiring: ${leaseWiringViolations(sources.map { it to it.readText() })}",
            leaseWiringViolations(sources.map { it to it.readText() }).isEmpty(),
        )
        assertEquals(
            listOf(
                "SkinSessionBridge.kt:SkinSessionBridgeProtocol(",
                "SkinsActivity.kt:SkinSessionStore(",
                "Any.kt:establishPendingForCoordinator(",
            ),
            leaseWiringViolations(
                listOf(
                    File("SkinSessionBridge.kt") to "SkinSessionBridgeProtocol(store)",
                    File("SkinsActivity.kt") to "val store = SkinSessionStore(root)",
                    File("Any.kt") to "establishPendingForCoordinator(handle)",
                ),
            ),
        )
    }

    private fun retriesClaimAfter(event: String, relativeOccurrence: Int, index: Int) {
        val root = File(testRoot, "fault-$index/profiles/hollow-knight/skins").apply { mkdirs() }
        val fs = leaseFs()
        val handle = handle(
            descriptor = UUID.nameUUIDFromBytes("descriptor-$index".toByteArray(StandardCharsets.US_ASCII)),
            lease = UUID.nameUUIDFromBytes("lease-$index".toByteArray(StandardCharsets.US_ASCII)),
        )
        val store = SkinSessionStore(root, fs, SkinLockManager(root), null, PermissiveTestSkinQuota(root))
        assertOk(store.establishPendingForCoordinator(handle, launcherOwner, registryGenerationId, registrySha256))
        fs.failOnEvent = event
        fs.failOnOccurrence = fs.events.count { it == event } + relativeOccurrence

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.claim(handle, gameOwner))

        fs.failOnEvent = null
        val retried = assertOk(store.claim(handle, gameOwner))
        assertEquals(LeaseState.GAME_OWNED, retried.state)
        assertEquals(2, stateDirectories(root, handle.descriptorId).size)
    }

    private fun leaseWiringViolations(sources: List<Pair<File, String>>): List<String> {
        val patterns = listOf(
            Regex("\\bSkinSessionStore\\s*\\("),
            Regex("\\bSkinLaunchCoordinator\\s*\\("),
            Regex("\\bSkinSessionBridgeProtocol\\s*\\("),
            Regex("\\bestablishPendingForCoordinator\\s*\\("),
            Regex("\\bprepareAcquisitionForCoordinator\\s*\\("),
            Regex("\\badvanceAcquisitionForCoordinator\\s*\\("),
            Regex("\\bcompleteAcquisitionForCoordinator\\s*\\("),
        )
        return sources.flatMap { (file, source) ->
            patterns.mapNotNull { pattern ->
                pattern.find(source)?.value?.let { "${file.name}:$it" }
            }
        }
    }

    private fun evidenceSnapshot(root: File): Pair<Set<String>, Map<String, List<Byte>>> {
        if (!root.exists()) return emptySet<String>() to emptyMap()
        val nodes = root.walkTopDown().drop(1).toList()
        val directories = nodes.filter(File::isDirectory).map { it.relativeTo(root).invariantSeparatorsPath }.toSet()
        val files = nodes.filter(File::isFile).associate { file ->
            file.relativeTo(root).invariantSeparatorsPath to file.readBytes().toList()
        }
        return directories to files
    }

    private fun assertTransitionPointerOrder(events: List<String>, activeBarrier: String) {
        val next = events.indexOf("rename:next.tmp->next")
        val previous = events.indexOf("rename:previous.tmp->previous")
        val current = events.indexOf("rename:current.tmp->current")
        val removeNext = events.indexOf("delete-contained:next")
        val active = events.indexOf(activeBarrier)
        assertTrue("Missing ordered transition events: $events", next >= 0 && previous >= 0 && current >= 0 && removeNext >= 0 && active >= 0)
        assertTrue("Lease pointer order changed: $events", next < previous && previous < current && current < removeNext && removeNext < active)
    }

    private fun failAtActiveParentBarrier(fs: FaultingSkinFileSystem, relativeOccurrence: Int) {
        fs.failOnEvent = "sync-dir:sessions"
        fs.failOnOccurrence = fs.events.count { it == "sync-dir:sessions" } + relativeOccurrence
    }

    private fun leaseFs(): FaultingSkinFileSystem = FaultingSkinFileSystem(FastSkinFileSystem()).apply {
        skipPhysicalSyncs = true
    }

    private fun store(fs: FaultingSkinFileSystem = leaseFs()): SkinSessionStore =
        SkinSessionStore(skinsRoot, fs, SkinLockManager(skinsRoot), null, PermissiveTestSkinQuota(skinsRoot))

    private fun handle(
        descriptor: UUID = descriptorId,
        lease: UUID = leaseId,
    ) = SkinLaunchHandle(
        descriptorId = descriptor,
        descriptorSha256 = descriptorSha256,
        descriptorPath = "sessions/$descriptor/descriptor.json",
        leaseId = lease,
        leaseToken = rawLeaseToken,
        sessionSequence = 7,
    )

    private fun pendingState() = LeaseStateDocument(
        schemaVersion = 1,
        leaseId = leaseId,
        leaseTokenSha256 = "a".repeat(64),
        profileId = "hollow-knight",
        sessionSequence = 7,
        transitionSequence = 0,
        transitionId = transitionId,
        parentTransitionId = null,
        state = LeaseState.LAUNCH_PENDING,
        descriptorId = descriptorId,
        descriptorSha256 = descriptorSha256,
        registryGenerationId = registryGenerationId,
        registrySha256 = registrySha256,
        launcherOwner = launcherOwner,
        gameOwner = null,
        closeReason = null,
    )

    private fun gameOwned(parent: LeaseStateDocument) = parent.copy(
        transitionSequence = 1,
        transitionId = UUID.fromString("52345678-1234-4234-8234-123456789abc"),
        parentTransitionId = parent.transitionId,
        state = LeaseState.GAME_OWNED,
        gameOwner = gameOwner,
    )

    private fun closedFromPending(parent: LeaseStateDocument) = parent.copy(
        transitionSequence = 1,
        transitionId = UUID.fromString("62345678-1234-4234-8234-123456789abc"),
        parentTransitionId = parent.transitionId,
        state = LeaseState.CLOSED,
        closeReason = "LAUNCH_FAILED",
    )

    private fun closedFromOwned(parent: LeaseStateDocument) = parent.copy(
        transitionSequence = 2,
        transitionId = UUID.fromString("72345678-1234-4234-8234-123456789abc"),
        parentTransitionId = parent.transitionId,
        state = LeaseState.CLOSED,
        closeReason = "GAME_EXIT",
    )

    private fun leaseStateFile(handle: SkinLaunchHandle, sequence: Long, name: String): File = File(
        skinsRoot,
        "sessions/${handle.descriptorId}/lease/states/ls-${sequence.toString().padStart(20, '0')}-${handle.leaseId}/$name",
    )

    private fun stateDirectories(root: File, descriptor: UUID): List<File> =
        File(root, "sessions/$descriptor/lease/states").listFiles()?.toList().orEmpty()

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private companion object {
        val descriptorId: UUID = UUID.fromString("32345678-1234-4234-8234-123456789abc")
        val leaseId: UUID = UUID.fromString("42345678-1234-4234-8234-123456789abc")
        val transitionId: UUID = UUID.fromString("12345678-1234-4234-8234-123456789abc")
        const val rawLeaseToken = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        const val descriptorSha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        const val registryGenerationId = "22345678-1234-4234-8234-123456789abc"
        const val registrySha256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
        val launcherOwner = ProcessIdentity(uid = 1000, pid = 1001, processStartToken = "99")
        val gameOwner = ProcessIdentity(uid = 1000, pid = 1002, processStartToken = "100")
    }
}
