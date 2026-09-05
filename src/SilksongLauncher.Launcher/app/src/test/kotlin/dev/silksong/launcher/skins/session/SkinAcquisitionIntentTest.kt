package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.registry.SkinLockManager
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinAcquisitionIntentTest {
    private lateinit var testRoot: File

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-acquisition-intent").absoluteFile
        testRoot.deleteRecursively()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun canonicalIntentRoundTripsWithExactHashOnlyBinding() {
        val intent = intent()
        val bytes = SkinAcquisitionIntentCodec.canonical(intent)

        assertArrayEquals(bytes, SkinAcquisitionIntentCodec.canonical(assertOk(SkinAcquisitionIntentCodec.parse(bytes))))
        assertFalse(bytes.toString(StandardCharsets.US_ASCII).contains(RAW_TOKEN))
        assertTrue(bytes.toString(StandardCharsets.US_ASCII).contains(TOKEN_SHA256))
    }

    @Test
    fun codecRejectsUnknownDuplicateMissingMalformedAndNoncanonicalEvidence() {
        val canonical = SkinAcquisitionIntentCodec.canonical(intent()).toString(StandardCharsets.US_ASCII)
        val malformed = listOf(
            canonical.replace("phase=", "unknown="),
            canonical.replace("descriptorSha256=", "descriptorId="),
            canonical.replace(Regex("descriptorPath=.*\\n"), ""),
            canonical.replace("sessionSequence=7", "sessionSequence=07"),
            canonical.replace("launcherUid=1000", "launcherUid=01000"),
            canonical.replace("descriptorPath=sessions/$DESCRIPTOR_ID/descriptor.json", "descriptorPath=/tmp/descriptor.json"),
            canonical.replace("leaseTokenSha256=$TOKEN_SHA256", "leaseTokenSha256=${"A".repeat(64)}"),
            canonical.dropLast(1),
            canonical + "unknown=x\n",
        )

        malformed.forEach { assertError(SkinImportCode.DOCUMENT_INVALID, SkinAcquisitionIntentCodec.parse(it.toByteArray())) }
    }

    @Test
    fun preparedIntentIsDurableBeforeAnyDescriptorAndContainsNoRawToken() {
        val root = root("durable")
        val fs = fastFs()
        val store = store(root, fs)

        assertOk(store.prepareAcquisitionForCoordinator(intent()))

        val durable = File(root, "sessions/acquisition.intent")
        assertTrue(durable.isFile)
        assertFalse(File(root, "sessions/acquisition.intent.tmp").exists())
        assertFalse(durable.readText(StandardCharsets.US_ASCII).contains(RAW_TOKEN))
        assertTrue(File(root, "sessions").listFiles().orEmpty().none { it.isDirectory })
        assertOrdered(fs.events, "write-new:acquisition.intent.tmp", "sync-file:acquisition.intent.tmp")
        assertOrdered(fs.events, "sync-file:acquisition.intent.tmp", "rename:acquisition.intent.tmp->acquisition.intent")
        assertOrdered(fs.events, "rename:acquisition.intent.tmp->acquisition.intent", "sync-dir:sessions")
    }

    @Test
    fun intentDurabilityFaultsStopWithoutDescriptorAndRecoveryClearsOnlyQualifiedEvidence() {
        val failures = listOf(
            Triple("write", "write-new:acquisition.intent.tmp", false),
            Triple("file-sync", "sync-file:acquisition.intent.tmp", true),
            Triple("rename", "rename:acquisition.intent.tmp->acquisition.intent", true),
            Triple("directory-barrier", "sync-dir:sessions", true),
        )
        failures.forEach { (label, event, mayBeVisible) ->
            val root = root("fault-$label")
            val fs = fastFs().apply {
                failOnEvent = event
                failOnOccurrence = if (event == "sync-dir:sessions") 2 else 1
            }
            val store = store(root, fs)

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store.prepareAcquisitionForCoordinator(intent()))
            assertEquals(event, fs.events.last())
            assertTrue(File(root, "sessions").listFiles().orEmpty().none { it.isDirectory })
            assertFalse(File(root, "sessions/active").exists())
            assertEquals(mayBeVisible, File(root, "sessions/acquisition.intent").exists() || File(root, "sessions/acquisition.intent.tmp").exists())

            fs.failOnEvent = null
            assertEquals(null, assertOk(store.recover(quietLiveness)))
            assertFalse(File(root, "sessions/acquisition.intent").exists())
            assertFalse(File(root, "sessions/acquisition.intent.tmp").exists())
        }
    }

    @Test
    fun malformedOrTornIntentRejectsReadOnly() {
        listOf(
            byteArrayOf(),
            "SKIN_ACQUISITION_INTENT_V1\nphase=PREPARED\n".toByteArray(),
            SkinAcquisitionIntentCodec.canonical(intent()).copyOfRange(0, 80),
        ).forEachIndexed { index, bytes ->
            val root = root("malformed-$index")
            val sessions = File(root, "sessions").apply { mkdirs() }
            val durable = File(sessions, "acquisition.intent").apply { writeBytes(bytes) }
            val before = durable.readBytes().toList()
            val fs = fastFs()

            assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, store(root, fs).recover(quietLiveness))

            assertEquals(before, durable.readBytes().toList())
            assertTrue(fs.events.none(::isMutationEvent))
        }
    }

    @Test
    fun mismatchedIntentTempRejectsBeforeMutation() {
        val root = root("mismatch-temp")
        val sessions = File(root, "sessions").apply { mkdirs() }
        val durableIntent = intent()
        File(sessions, "acquisition.intent").writeBytes(SkinAcquisitionIntentCodec.canonical(durableIntent))
        val mismatched = durableIntent.copy(
            phase = SkinAcquisitionPhase.DESCRIPTOR_DURABLE,
            descriptorId = OTHER_DESCRIPTOR_ID,
            descriptorPath = "sessions/$OTHER_DESCRIPTOR_ID/descriptor.json",
        )
        File(sessions, "acquisition.intent.tmp").writeBytes(SkinAcquisitionIntentCodec.canonical(mismatched))
        val before = sessions.listFiles()!!.associate { it.name to it.readBytes().toList() }
        val fs = fastFs()

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, store(root, fs).recover(quietLiveness))

        assertEquals(before, sessions.listFiles()!!.associate { it.name to it.readBytes().toList() })
        assertTrue(fs.events.none(::isMutationEvent))
    }

    private fun intent() = SkinAcquisitionIntent(
        phase = SkinAcquisitionPhase.PREPARED,
        descriptorId = DESCRIPTOR_ID,
        descriptorSha256 = DESCRIPTOR_SHA256,
        descriptorPath = "sessions/$DESCRIPTOR_ID/descriptor.json",
        leaseId = LEASE_ID,
        leaseTokenSha256 = TOKEN_SHA256,
        sessionSequence = 7,
        registryGenerationId = REGISTRY_ID,
        registrySha256 = REGISTRY_SHA256,
        launcherOwner = launcherOwner,
    )

    private fun root(label: String): File =
        File(testRoot, "$label/profiles/hollow-knight/skins").apply { mkdirs() }

    private fun store(root: File, fs: FaultingSkinFileSystem): SkinSessionStore =
        SkinSessionStore(root, fs, SkinLockManager(root), TARGET_PROCESS, PermissiveTestSkinQuota(root))

    private fun fastFs(): FaultingSkinFileSystem =
        FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }

    private fun assertOrdered(events: List<String>, first: String, second: String) {
        val left = events.indexOf(first)
        val right = if (left >= 0) events.subList(left + 1, events.size).indexOf(second) else -1
        assertTrue("Missing ordered intent events: $events", left >= 0 && right >= 0)
    }

    private fun isMutationEvent(event: String): Boolean =
        event.startsWith("write-") || event.startsWith("rename:") || event.startsWith("delete-") ||
            event.startsWith("sync-") || event.startsWith("mkdir:")

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private companion object {
        val DESCRIPTOR_ID: UUID = UUID.fromString("12345678-1234-4234-8234-123456789abc")
        val OTHER_DESCRIPTOR_ID: UUID = UUID.fromString("22345678-1234-4234-8234-123456789abc")
        val LEASE_ID: UUID = UUID.fromString("32345678-1234-4234-8234-123456789abc")
        const val RAW_TOKEN = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        val TOKEN_SHA256 = SkinLeaseStateCodec.rawTokenSha256(RAW_TOKEN)
        const val DESCRIPTOR_SHA256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        const val REGISTRY_ID = "42345678-1234-4234-8234-123456789abc"
        const val REGISTRY_SHA256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
        val launcherOwner = ProcessIdentity(1000, 1001, "99")
        val TARGET_PROCESS = SkinTargetProcess("com.example.hollowknight", "com.example.hollowknight")
        val quietLiveness = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = SelfIdentityResult.Unknown
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = ExpectedOwnerLiveness.Unknown
            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence = ExactProcessPresence.Unknown
        }
    }
}
