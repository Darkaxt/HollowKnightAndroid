package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.RotationInterlock
import dev.silksong.launcher.skins.registry.SkinActivation
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.registry.SkinMode
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinSessionSequenceTest {
    private lateinit var testRoot: File
    private lateinit var catalog: CatalogPathSet

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-session-sequence").absoluteFile
        testRoot.deleteRecursively()
        catalog = PinnedCatalogFixture.load()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun virginReservationIsZeroAndSequentialReservationsAreMonotonic() {
        val root = root("sequential")
        val store = store(root)

        assertEquals(0L, assertOk(store.reserveSequenceForCoordinator()))
        assertEquals("0\n", File(root, "sessions/sequence").readText(StandardCharsets.US_ASCII))
        assertEquals(1L, assertOk(store.reserveSequenceForCoordinator()))
        assertEquals("1\n", File(root, "sessions/sequence").readText(StandardCharsets.US_ASCII))
        assertFalse(File(root, "sessions/sequence.tmp").exists())
    }

    @Test
    fun reservationRejectsOverflowWithoutMutation() {
        val root = root("overflow")
        val sequence = File(root, "sessions/sequence").apply {
            parentFile!!.mkdirs()
            writeText("${Long.MAX_VALUE}\n", StandardCharsets.US_ASCII)
        }
        val fs = fastFs()

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, store(root, fs).reserveSequenceForCoordinator())

        assertEquals("${Long.MAX_VALUE}\n", sequence.readText(StandardCharsets.US_ASCII))
        assertFalse(File(root, "sessions/sequence.tmp").exists())
        assertTrue(fs.events.none { it.startsWith("write-") || it.startsWith("rename:") || it.startsWith("delete-") })
    }

    @Test
    fun recoveryRejectsMalformedOrNoncanonicalCounterAndTempReadOnly() {
        val cases = listOf(
            "counter-empty" to Pair("", null),
            "counter-leading-zero" to Pair("00\n", null),
            "counter-plus" to Pair("+1\n", null),
            "counter-missing-lf" to Pair("1", null),
            "counter-overflow" to Pair("9223372036854775808\n", null),
            "temp-gap" to Pair("4\n", "6\n"),
            "temp-same" to Pair("4\n", "4\n"),
            "temp-noncanonical" to Pair("4\n", "05\n"),
        )

        cases.forEach { (label, evidence) ->
            val root = root(label)
            val sessions = File(root, "sessions").apply { mkdirs() }
            File(sessions, "sequence").writeText(evidence.first, StandardCharsets.US_ASCII)
            evidence.second?.let { File(sessions, "sequence.tmp").writeText(it, StandardCharsets.US_ASCII) }
            val before = sessions.listFiles()!!.associate { it.name to it.readBytes().toList() }
            val fs = fastFs()

            assertError(
                SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
                store(root, fs).recover(quietLiveness),
            )

            assertEquals(before, sessions.listFiles()!!.associate { it.name to it.readBytes().toList() })
            assertTrue("Malformed sequence evidence mutated for $label: ${fs.events}", fs.events.none(::isMutationEvent))
        }
    }

    @Test
    fun exactQualifiedTempEvidenceIsFinalizedAndRetryAdvancesPastIt() {
        listOf(
            Triple("virgin", null, 0L),
            Triple("successor", 4L, 5L),
        ).forEach { (label, current, temporary) ->
            val root = root("qualified-$label")
            val sessions = File(root, "sessions").apply { mkdirs() }
            current?.let { File(sessions, "sequence").writeText("$it\n", StandardCharsets.US_ASCII) }
            File(sessions, "sequence.tmp").writeText("$temporary\n", StandardCharsets.US_ASCII)
            val store = store(root)

            assertEquals(null, assertOk(store.recover(quietLiveness)))
            assertEquals("$temporary\n", File(sessions, "sequence").readText(StandardCharsets.US_ASCII))
            assertFalse(File(sessions, "sequence.tmp").exists())
            assertEquals(temporary + 1L, assertOk(store.reserveSequenceForCoordinator()))
        }
    }

    @Test
    fun counterMayRemainAheadAfterAbandonedReservation() {
        val root = root("ahead")
        File(root, "sessions").mkdirs()
        File(root, "sessions/sequence").writeText("9\n", StandardCharsets.US_ASCII)
        val store = store(root)

        assertEquals(null, assertOk(store.recover(quietLiveness)))
        assertEquals(10L, assertOk(store.reserveSequenceForCoordinator()))
    }

    @Test
    fun recoveryRejectsCounterBehindValidatedDescriptorBeforePointerRepair() {
        val root = root("behind")
        val active = writePendingDescriptor(root, sessionSequence = 1L)
        File(root, "sessions/sequence").writeText("0\n", StandardCharsets.US_ASCII)
        val before = File(root, "sessions/active").readBytes().toList()
        val fs = fastFs()

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, store(root, fs).recover(aliveLiveness))

        assertEquals(before, File(root, "sessions/active").readBytes().toList())
        assertEquals(active, LeasePointerCodec.parse(File(root, "sessions/active").readBytes()))
        assertTrue("Counter-behind rejection mutated evidence: ${fs.events}", fs.events.none(::isMutationEvent))
    }

    @Test
    fun qualifiedTempIsNotPromotedUntilAllDescriptorEvidenceValidates() {
        val root = root("two-phase-temp")
        writePendingDescriptor(root, sessionSequence = 0L)
        File(root, "sessions/sequence").writeText("0\n", StandardCharsets.US_ASCII)
        File(root, "sessions/sequence.tmp").writeText("1\n", StandardCharsets.US_ASCII)
        val descriptor = File(root, "sessions").listFiles().orEmpty().single { it.isDirectory }
        File(descriptor, "descriptor.sha256").writeText("${hex('f')}\n", StandardCharsets.US_ASCII)
        val fs = fastFs()

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, store(root, fs).recover(aliveLiveness))

        assertEquals("0\n", File(root, "sessions/sequence").readText(StandardCharsets.US_ASCII))
        assertEquals("1\n", File(root, "sessions/sequence.tmp").readText(StandardCharsets.US_ASCII))
        assertTrue("Sequence recovery mutated before all evidence validated: ${fs.events}", fs.events.none(::isMutationEvent))
    }

    @Test
    fun modificationTimesNeverChooseBetweenCounterAndQualifiedTemp() {
        val root = root("mtime")
        val sessions = File(root, "sessions").apply { mkdirs() }
        val sequence = File(sessions, "sequence").apply { writeText("4\n", StandardCharsets.US_ASCII) }
        val temporary = File(sessions, "sequence.tmp").apply { writeText("5\n", StandardCharsets.US_ASCII) }
        sequence.setLastModified(Long.MAX_VALUE / 2)
        temporary.setLastModified(1L)

        assertEquals(6L, assertOk(store(root).reserveSequenceForCoordinator()))
        assertEquals("6\n", sequence.readText(StandardCharsets.US_ASCII))
    }

    @Test
    fun sequenceDurabilityFaultsStopBeforeDependentPublicationAndRetryNeverReusesVisibleEvidence() {
        val failures = listOf(
            Triple("write", "write-new:sequence.tmp", 0L),
            Triple("file-sync", "sync-file:sequence.tmp", 1L),
            Triple("rename", "rename:sequence.tmp->sequence", 1L),
            Triple("directory-barrier", "sync-dir:sessions", 1L),
        )
        failures.forEach { (label, event, retrySequence) ->
            val root = root("fault-$label")
            val fs = fastFs().apply {
                failOnEvent = event
                failOnOccurrence = if (event == "sync-dir:sessions") 2 else 1
            }
            val store = store(root, fs)

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.reserveSequenceForCoordinator())
            assertEquals("Reservation continued after $event: ${fs.events}", event, fs.events.last())
            assertFalse(File(root, "sessions/active").exists())
            assertTrue(File(root, "sessions").listFiles().orEmpty().none { it.isDirectory })

            fs.failOnEvent = null
            assertEquals(retrySequence, assertOk(store.reserveSequenceForCoordinator()))
        }
    }

    @Test
    fun sequenceFilesAreFixedEvidenceWhileUnknownSessionChildrenReject() {
        val root = root("fixed")
        val sessions = File(root, "sessions").apply { mkdirs() }
        File(sessions, "sequence.tmp").writeText("0\n", StandardCharsets.US_ASCII)

        assertEquals(null, assertOk(store(root).recover(quietLiveness)))

        File(sessions, "unknown").writeText("x")
        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, store(root).recover(quietLiveness))
    }

    private fun writePendingDescriptor(root: File, sessionSequence: Long): LeaseHead {
        val descriptorId = uuid("descriptor-$sessionSequence")
        val leaseId = uuid("lease-$sessionSequence")
        val tokenDigest = SkinLeaseStateCodec.rawTokenSha256(RAW_TOKEN)
        val registryId = uuid("registry-$sessionSequence").toString()
        val descriptor = SkinLaunchDescriptor(
            1,
            descriptorId,
            sessionSequence,
            "hollow-knight",
            "1.5.12620",
            HollowKnightCatalogPaths.CATALOG_ID,
            catalog.sha256,
            registryId,
            hex('c'),
            SkinActivation(SkinMode.OFF, null, ActiveVisual.Vanilla, 0, RotationInterlock.clear()),
            emptyList(),
            leaseId,
            tokenDigest,
        )
        val descriptorBytes = SkinLaunchDescriptorCodec.canonical(descriptor)
        val descriptorDigest = SkinIdentity.sha256(descriptorBytes)
        val pending = LeaseStateDocument(
            1,
            leaseId,
            tokenDigest,
            "hollow-knight",
            sessionSequence,
            0,
            uuid("transition-$sessionSequence"),
            null,
            LeaseState.LAUNCH_PENDING,
            descriptorId,
            descriptorDigest,
            registryId,
            hex('c'),
            launcherOwner,
            null,
            null,
        )
        val descriptorRoot = File(root, "sessions/$descriptorId").apply { mkdirs() }
        File(descriptorRoot, "descriptor.json").writeBytes(descriptorBytes)
        File(descriptorRoot, "descriptor.sha256").writeText("$descriptorDigest\n", StandardCharsets.US_ASCII)
        File(descriptorRoot, ".complete").writeBytes(ByteArray(0))
        val stateBytes = SkinLeaseStateCodec.canonical(pending)
        val head = SkinLeaseStateCodec.head(pending)
        val lease = File(descriptorRoot, "lease").apply { mkdirs() }
        val state = File(
            lease,
            "states/ls-${0L.toString().padStart(20, '0')}-$leaseId",
        ).apply { mkdirs() }
        File(state, "lease.json").writeBytes(stateBytes)
        File(state, "lease.sha256").writeText("${head.sha256}\n", StandardCharsets.US_ASCII)
        File(state, ".complete").writeBytes(ByteArray(0))
        File(lease, "current").writeBytes(LeasePointerCodec.canonical(head))
        File(root, "sessions/active").writeBytes(LeasePointerCodec.canonical(head))
        return head
    }

    private fun root(label: String): File =
        File(testRoot, "$label/profiles/hollow-knight/skins").apply { mkdirs() }

    private fun store(root: File, fs: FaultingSkinFileSystem = fastFs()): SkinSessionStore =
        SkinSessionStore(root, fs, SkinLockManager(root), TARGET_PROCESS, PermissiveTestSkinQuota(root))

    private fun fastFs(): FaultingSkinFileSystem =
        FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }

    private fun isMutationEvent(event: String): Boolean =
        event.startsWith("write-") || event.startsWith("rename:") || event.startsWith("delete-") ||
            event.startsWith("sync-") || event.startsWith("mkdir:")

    private fun uuid(value: String): UUID = UUID.nameUUIDFromBytes(value.toByteArray(StandardCharsets.US_ASCII))

    private fun hex(value: Char): String = value.toString().repeat(64)

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private companion object {
        const val RAW_TOKEN = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        val launcherOwner = ProcessIdentity(1000, 1001, "99")
        val gameOwner = ProcessIdentity(1000, 1002, "100")
        val TARGET_PROCESS = SkinTargetProcess("com.example.hollowknight", "com.example.hollowknight")
        val quietLiveness = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = SelfIdentityResult.Unknown
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = ExpectedOwnerLiveness.Unknown
            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence = ExactProcessPresence.Unknown
        }
        val aliveLiveness = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = SelfIdentityResult.Unknown
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = ExpectedOwnerLiveness.Alive(expected)
            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence = ExactProcessPresence.Absent
        }
    }
}
