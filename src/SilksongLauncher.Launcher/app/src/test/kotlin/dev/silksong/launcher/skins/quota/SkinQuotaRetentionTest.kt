package dev.silksong.launcher.skins.quota

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.importing.SkinQuarantine
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.ByteArrayInputStream
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.channels.SeekableByteChannel
import java.nio.file.Files
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.atomic.AtomicReference
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinQuotaRetentionTest {
    private lateinit var testRoot: File
    private lateinit var skinsRoot: File
    private lateinit var fs: FastSkinFileSystem

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-quota").absoluteFile
        testRoot.deleteRecursively()
        skinsRoot = File(testRoot, "profiles/hollow-knight/skins")
        skinsRoot.mkdirs()
        fs = FastSkinFileSystem()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun `exact profile limit succeeds and one rounded block fails`() {
        val usage = AtomicReference(SkinQuotaUsage(profileBytes = 4096, sessionBytes = 0))
        val quota = quotaWithUsage(usage, profileLimit = 8192, sessionLimit = 4096)

        val exact = quota.reserve(SkinQuotaRequest.profile(1))
        assertTrue(exact is SkinResult.Ok)
        (exact as SkinResult.Ok).value.release()

        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(4097)))
    }

    @Test
    fun `logical files round independently and one rounded block over limit fails`() {
        val quota = quotaWithUsage(
            AtomicReference(SkinQuotaUsage.ZERO),
            profileLimit = 4096,
            sessionLimit = 4096,
        )

        val exact = quota.reserve(SkinQuotaRequest.profile(4096))
        assertTrue(exact is SkinResult.Ok)
        (exact as SkinResult.Ok).value.release()

        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(4097)))
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1, 1)))
    }

    @Test
    fun `import budgets cover bounded extraction and exact candidate publication peaks`() {
        val preparationCharge = SkinLimits.V1.uncompressedBytes +
            SkinLimits.V1.entries.toLong() * SkinQuotaLimits.FALLBACK_BLOCK_BYTES
        assertEquals(preparationCharge, SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.IMPORT_PREPARATION))

        val candidate = SkinQuotaBudgets.importCandidate(listOf(1L, 4097L), receiptBytes = 65L)
        val expectedCandidateCharge =
            4096L + 8192L +
                64L * 1024 +
                8L * 1024 * 1024 +
                4096L +
                16L * 1024 +
                SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.REGISTRY_PUBLICATION) +
                8L * 4096
        assertEquals(expectedCandidateCharge, SkinQuotaBudgets.fallbackCharge(candidate))

        assertThrows(IllegalArgumentException::class.java) {
            SkinQuotaBudgets.importCandidate(
                List(SkinLimits.V1.mappings + 1) { 1L },
                receiptBytes = 1L,
            )
        }
        assertThrows(IllegalArgumentException::class.java) {
            SkinQuotaBudgets.importStaging(listOf(SkinLimits.V1.uncompressedBytes + 1L))
        }
    }

    @Test
    fun `fixed operation budgets cover documents and transient metadata peaks`() {
        val cases = listOf(
            SkinQuotaBudgets.REGISTRY_PUBLICATION to (8L * 1024 * 1024 + 8L * 4096),
            SkinQuotaBudgets.SESSION_ACQUISITION to (
                2L * 8 * 1024 * 1024 + 3L * 64 * 1024 + (392L + 8L + 16L + 10L) * 4096
            ),
            SkinQuotaBudgets.SESSION_CLAIM to (64L * 1024 + 10L * 4096),
            SkinQuotaBudgets.SESSION_CLOSE to (64L * 1024 + 10L * 4096),
            SkinQuotaBudgets.SESSION_RECOVERY to (64L * 1024 + 392L * 4096),
        )
        cases.forEachIndexed { index, (request, expectedCharge) ->
            val root = File(testRoot, "budgets-$index/profiles/hollow-knight/skins").apply { mkdirs() }
            val usage = AtomicReference(SkinQuotaUsage.ZERO)
            val limits = SkinQuotaLimits(expectedCharge, expectedCharge, 4096)
            val quota = SkinQuota.testing(root, fs, SkinQuotaAccountingAuthority { usage.get() }, limits)

            assertEquals(expectedCharge, SkinQuotaBudgets.fallbackCharge(request))
            assertOk(quota.reserve(request)).release()
            usage.set(
                if (request.scope == SkinQuotaScope.SESSIONS) SkinQuotaUsage(4096, 4096)
                else SkinQuotaUsage(4096, 0),
            )
            assertQuotaError(quota.reserve(request))
        }
    }

    @Test
    fun `fixed lifecycle margin covers claim close and two complete recoveries`() {
        val expected =
            SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_CLAIM) +
                SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_CLOSE) +
                2L * SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_RECOVERY)

        assertEquals(expected, SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES)
    }

    @Test
    fun `maximum ordinary acquisition leaves fixed lifecycle margin in both caps`() {
        val limits = SkinQuotaLimits.V1
        val margin = SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val acquisitionCharge = SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_ACQUISITION)
        val usage = SkinQuotaUsage(
            profileBytes = limits.profileBytes - margin - acquisitionCharge,
            sessionBytes = limits.sessionBytes - margin - acquisitionCharge,
        )
        val quota = SkinQuota.testing(
            skinsRoot,
            fs,
            SkinQuotaAccountingAuthority { usage },
            limits,
        )

        val acquisition = assertOk(quota.reserve(SkinQuotaBudgets.SESSION_ACQUISITION))
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1)))
        val claim = assertOk(quota.reserve(SkinQuotaBudgets.SESSION_CLAIM))
        val close = assertOk(quota.reserve(SkinQuotaBudgets.SESSION_CLOSE))
        val firstRecovery = assertOk(quota.reserve(SkinQuotaBudgets.SESSION_RECOVERY))
        val secondRecovery = assertOk(quota.reserve(SkinQuotaBudgets.SESSION_RECOVERY))
        assertQuotaError(quota.reserve(SkinQuotaBudgets.SESSION_CLAIM))
        secondRecovery.release()
        firstRecovery.release()
        close.release()
        claim.release()
        acquisition.release()
    }

    @Test
    fun `one fallback block beyond ordinary acquisition capacity rejects`() {
        val limits = SkinQuotaLimits.V1
        val margin = SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val acquisitionCharge = SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_ACQUISITION)
        val usage = SkinQuotaUsage(
            profileBytes = limits.profileBytes - margin - acquisitionCharge + limits.allocationBlockBytes,
            sessionBytes = limits.sessionBytes - margin - acquisitionCharge + limits.allocationBlockBytes,
        )
        val quota = SkinQuota.testing(
            skinsRoot,
            fs,
            SkinQuotaAccountingAuthority { usage },
            limits,
        )

        assertQuotaError(quota.reserve(SkinQuotaBudgets.SESSION_ACQUISITION))
    }

    @Test
    fun `ordinary registry and quarantine reservations cannot consume profile lifecycle margin`() {
        val limits = SkinQuotaLimits.V1
        val margin = SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val registryCharge = SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.REGISTRY_PUBLICATION)
        val usage = SkinQuotaUsage(
            profileBytes = limits.profileBytes - margin - registryCharge,
            sessionBytes = 0,
        )
        val quota = SkinQuota.testing(
            skinsRoot,
            fs,
            SkinQuotaAccountingAuthority { usage },
            limits,
        )

        val registry = assertOk(quota.reserve(SkinQuotaBudgets.REGISTRY_PUBLICATION))
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1)))
        registry.release()
    }

    @Test
    fun `production constructors cannot change normative quota limits`() {
        assertTrue(
            SkinQuota::class.java.constructors.none { constructor ->
                constructor.parameterTypes.contains(SkinQuotaLimits::class.java)
            },
        )
    }

    @Test
    fun `quota limits reject zero raised caps and non normative fallback accounting`() {
        val v1 = SkinQuotaLimits.V1
        listOf(
            { SkinQuotaLimits(profileBytes = 0, sessionBytes = 0) },
            { SkinQuotaLimits(profileBytes = 4096, sessionBytes = 0) },
            { SkinQuotaLimits(profileBytes = v1.profileBytes + 4096, sessionBytes = v1.sessionBytes) },
            { SkinQuotaLimits(profileBytes = v1.profileBytes, sessionBytes = v1.sessionBytes + 4096) },
            { SkinQuotaLimits(profileBytes = 4096, sessionBytes = 8192) },
            { SkinQuotaLimits(profileBytes = 8192, sessionBytes = 8192, allocationBlockBytes = 1) },
            { SkinQuotaLimits(profileBytes = 8192, sessionBytes = 8192, allocationBlockBytes = 8192) },
            { v1.copy(profileBytes = v1.profileBytes + 4096) },
            { v1.copy(sessionBytes = v1.sessionBytes + 4096) },
        ).forEach { construct ->
            assertThrows(IllegalArgumentException::class.java) { construct() }
        }
    }

    @Test
    fun `accounting test factory cannot raise the profile cap above V1`() {
        val v1 = SkinQuotaLimits.V1

        assertThrows(IllegalArgumentException::class.java) {
            SkinQuota.testing(
                skinsRoot,
                fs,
                SkinQuotaAccountingAuthority { SkinQuotaUsage.ZERO },
                SkinQuotaLimits(v1.profileBytes + 4096, v1.sessionBytes),
            )
        }
    }

    @Test
    fun `tree accounting test factory cannot raise the session cap above V1`() {
        val v1 = SkinQuotaLimits.V1

        assertThrows(IllegalArgumentException::class.java) {
            SkinQuota.testing(
                skinsRoot,
                fs,
                SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
                SkinQuotaLimits(v1.profileBytes, v1.sessionBytes + 4096),
            )
        }
    }

    @Test
    fun `different test configs and lexical aliases share one physical-root ledger`() {
        val usage = SkinQuotaAccountingAuthority { SkinQuotaUsage.ZERO }
        val strict = SkinQuota.testing(
            skinsRoot,
            fs,
            usage,
            SkinQuotaLimits(profileBytes = 4096, sessionBytes = 4096),
        )
        val alias = File(skinsRoot.parentFile, "ignored/../skins")
        val permissive = SkinQuota.testing(
            alias,
            fs,
            usage,
            SkinQuotaLimits(profileBytes = 409_600, sessionBytes = 409_600),
        )

        val held = assertOk(strict.reserve(SkinQuotaRequest.profile(1)))
        assertQuotaError(permissive.reserve(SkinQuotaRequest.profile(409_600)))
        held.release()
    }

    @Test
    fun `profile parent replacement invalidates stale quota authority and separates replacement ledger`() {
        val limits = SkinQuotaLimits(profileBytes = 8192, sessionBytes = 8192)
        val unavailable = SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable }
        val realFs = AndroidSkinFileSystem()
        val stale = SkinQuota.testing(skinsRoot, realFs, unavailable, limits)
        val staleReservation = assertOk(stale.reserve(SkinQuotaRequest.profile(1)))
        val staleCapacityIdentity = SkinQuotaCapacityReserver(stale).capacityReconciliationIdentity
        val profileRoot = requireNotNull(skinsRoot.parentFile)
        val displaced = File(profileRoot.parentFile, "displaced-hollow-knight")
        Files.move(profileRoot.toPath(), displaced.toPath())
        assertTrue(skinsRoot.mkdirs())

        val replacement = SkinQuota.testing(skinsRoot, realFs, unavailable, limits)
        val replacementCapacityIdentity = SkinQuotaCapacityReserver(replacement).capacityReconciliationIdentity
        assertFalse(staleCapacityIdentity == replacementCapacityIdentity)
        val replacementReservation = assertOk(replacement.reserve(SkinQuotaRequest.profile(8192)))

        assertQuotaError(stale.usage())
        assertQuotaError(stale.reserve(SkinQuotaRequest.profile(1)))
        assertThrows(IllegalArgumentException::class.java) {
            staleReservation.transfer(File(skinsRoot, "owned"), SkinQuotaRequest.profile(1))
        }
        assertQuotaError(replacement.reserve(SkinQuotaRequest.profile(1)))

        staleReservation.release()
        replacementReservation.release()
        assertEquals(SkinQuotaUsage.ZERO, assertOk(replacement.usage()))
        assertOk(replacement.reserve(SkinQuotaRequest.profile(8192))).release()
    }

    @Test
    fun `root symlink evidence is rejected before ledger construction`() {
        val aliasing = DelegatingFileSystem(fs) {
            overrideSymbolic = { file -> file.absoluteFile.normalize() == skinsRoot.absoluteFile.normalize() }
        }

        assertThrows(IllegalArgumentException::class.java) {
            SkinQuota(skinsRoot, aliasing, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })
        }
    }

    @Test
    fun `quota fails closed at traversal when bounded listing capability is unavailable`() {
        val narrow = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {}
        val quota = SkinQuota(
            skinsRoot,
            narrow,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
        )

        assertQuotaError(quota.usage())
    }

    @Test
    fun `immediate over-bound directory stops before accumulating an extra entry`() {
        repeat(4) { File(skinsRoot, "entry-$it").writeBytes(byteArrayOf(it.toByte())) }
        val bounded = CountingBoundedFileSystem(fs)
        val accounting = SkinTreeQuotaAccounting(
            skinsRoot,
            bounded,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            fallbackBlockBytes = 4096,
            maxObservedNodes = 4,
        )
        val quota = SkinQuota.testing(skinsRoot, bounded, accounting, SkinQuotaLimits.V1)

        assertQuotaError(quota.usage())
        assertEquals(3, bounded.accumulatedEntries)
        assertTrue(bounded.detectedOverflow)
    }

    @Test
    fun `session reservations satisfy both session and profile caps`() {
        val sessionBound = quotaWithUsage(
            AtomicReference(SkinQuotaUsage(profileBytes = 8L * 4096, sessionBytes = 4L * 4096)),
            profileLimit = 10L * 4096,
            sessionLimit = 5L * 4096,
        )
        val exact = sessionBound.reserve(SkinQuotaRequest.sessions(1))
        assertTrue(exact is SkinResult.Ok)
        (exact as SkinResult.Ok).value.release()
        assertQuotaError(sessionBound.reserve(SkinQuotaRequest.sessions(4097)))

        val profileBound = quotaWithUsage(
            AtomicReference(SkinQuotaUsage(profileBytes = 9L * 4096, sessionBytes = 4096)),
            profileLimit = 10L * 4096,
            sessionLimit = 5L * 4096,
        )
        assertQuotaError(profileBound.reserve(SkinQuotaRequest.sessions(4097)))
    }

    @Test
    fun `checked rounding and total arithmetic fail closed on overflow`() {
        val limits = SkinQuotaLimits.V1
        val quota = quotaWithUsage(
            AtomicReference(SkinQuotaUsage.ZERO),
            profileLimit = limits.profileBytes,
            sessionLimit = limits.sessionBytes,
        )
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(Long.MAX_VALUE)))

        val overflowingTotal = quotaWithUsage(
            AtomicReference(SkinQuotaUsage(Long.MAX_VALUE, 0)),
            profileLimit = limits.profileBytes,
            sessionLimit = limits.sessionBytes,
        )
        assertQuotaError(overflowingTotal.reserve(SkinQuotaRequest.profile(1)))
    }

    @Test
    fun `allocated bytes win and unavailable allocation falls back to rounded logical length`() {
        val allocated = File(skinsRoot, "allocated.bin").apply { writeBytes(ByteArray(3)) }
        val fallback = File(skinsRoot, "fallback.bin").apply { writeBytes(ByteArray(1)) }
        val authority = SkinAllocatedBytesAuthority { file ->
            when (file.absoluteFile.normalize()) {
                allocated.absoluteFile.normalize() -> SkinAllocatedBytes.Available(123)
                fallback.absoluteFile.normalize() -> SkinAllocatedBytes.Unavailable
                else -> SkinAllocatedBytes.Unavailable
            }
        }
        val quota = SkinQuota(skinsRoot, fs, authority)

        assertEquals(SkinQuotaUsage(profileBytes = 4219, sessionBytes = 0), assertOk(quota.usage()))
    }

    @Test
    fun `nested tree charges sessions to both nested and profile totals`() {
        File(skinsRoot, "objects/a").apply { parentFile.mkdirs(); writeBytes(ByteArray(2)) }
        File(skinsRoot, "sessions/one/descriptor.json").apply { parentFile.mkdirs(); writeBytes(ByteArray(5)) }
        File(skinsRoot, "sessions/one/lease/states/state.json").apply {
            parentFile.mkdirs()
            writeBytes(ByteArray(9))
        }
        val quota = SkinQuota(skinsRoot, fs, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })

        assertEquals(
            SkinQuotaUsage(profileBytes = 3L * 4096, sessionBytes = 2L * 4096),
            assertOk(quota.usage()),
        )
    }

    @Test
    fun `zero length regular files have zero fallback charge`() {
        File(skinsRoot, "empty").createNewFile()
        val quota = SkinQuota(skinsRoot, fs, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })

        assertEquals(SkinQuotaUsage.ZERO, assertOk(quota.usage()))
    }

    @Test
    fun `symbolic evidence fails closed without traversal`() {
        val alias = File(skinsRoot, "alias").apply { writeBytes(byteArrayOf(1)) }
        val rejecting = DelegatingFileSystem(fs) {
            overrideSymbolic = { file -> file.absoluteFile.normalize() == alias.absoluteFile.normalize() }
        }
        val quota = SkinQuota(skinsRoot, rejecting, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })

        assertQuotaError(quota.usage())
    }

    @Test
    fun `duplicate node identity fails closed as an alias`() {
        val first = File(skinsRoot, "first").apply { writeBytes(byteArrayOf(1)) }
        val second = File(skinsRoot, "second").apply { writeBytes(byteArrayOf(2)) }
        val aliasing = DelegatingFileSystem(fs) {
            overrideIdentity = { file, original ->
                if (file.absoluteFile.normalize() == first.absoluteFile.normalize() ||
                    file.absoluteFile.normalize() == second.absoluteFile.normalize()
                ) {
                    original.copy(fileKey = "same-file-key")
                } else {
                    original
                }
            }
        }
        val quota = SkinQuota(skinsRoot, aliasing, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })

        assertQuotaError(quota.usage())
    }

    @Test
    fun `out of root child evidence fails closed`() {
        val outside = File(testRoot, "outside").apply { writeBytes(byteArrayOf(1)) }
        val escaping = DelegatingFileSystem(fs) {
            overrideList = { directory, original ->
                if (directory.absoluteFile.normalize() == skinsRoot.absoluteFile.normalize()) original + outside else original
            }
        }
        val quota = SkinQuota(skinsRoot, escaping, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })

        assertQuotaError(quota.usage())
    }

    @Test
    fun `ambiguous or changing allocated metadata fails closed`() {
        File(skinsRoot, "file").writeBytes(byteArrayOf(1))
        val ambiguous = SkinQuota(
            skinsRoot,
            fs,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Ambiguous("unknown blocks") },
        )
        assertQuotaError(ambiguous.usage())

        var read = 0
        val changing = SkinQuota(
            skinsRoot,
            fs,
            SkinAllocatedBytesAuthority {
                read++
                SkinAllocatedBytes.Available(if (read % 2 == 1) 4096 else 8192)
            },
        )
        assertQuotaError(changing.usage())
    }

    @Test
    fun `concurrent outstanding reservations cannot oversubscribe`() {
        val quota = quotaWithUsage(
            AtomicReference(SkinQuotaUsage.ZERO),
            profileLimit = 10L * 4096,
            sessionLimit = 10L * 4096,
        )
        val ready = CountDownLatch(16)
        val start = CountDownLatch(1)
        val results = Collections.synchronizedList(mutableListOf<SkinResult<SkinQuotaReservation>>())
        val threads = (0 until 16).map {
            Thread {
                ready.countDown()
                start.await()
                results += quota.reserve(SkinQuotaRequest.profile(1))
            }.apply { start() }
        }
        ready.await()
        start.countDown()
        threads.forEach(Thread::join)

        assertEquals(10, results.count { it is SkinResult.Ok })
        assertEquals(6, results.count { it is SkinResult.Error })
        results.filterIsInstance<SkinResult.Ok<SkinQuotaReservation>>().forEach { it.value.release() }
    }

    @Test
    fun `separate authorities for one root share outstanding reservation admission`() {
        val usage = SkinQuotaAccountingAuthority { SkinQuotaUsage.ZERO }
        val limits = SkinQuotaLimits(profileBytes = 4096, sessionBytes = 4096)
        val firstAuthority = SkinQuota.testing(skinsRoot, fs, usage, limits)
        val secondAuthority = SkinQuota.testing(skinsRoot, fs, usage, limits)

        val first = assertOk(firstAuthority.reserve(SkinQuotaRequest.profile(1)))
        assertQuotaError(secondAuthority.reserve(SkinQuotaRequest.profile(1)))
        first.release()
        val retried = assertOk(secondAuthority.reserve(SkinQuotaRequest.profile(1)))
        retried.release()
    }

    @Test
    fun `release transfer and retry lifecycle is explicit and idempotent`() {
        val usage = AtomicReference(SkinQuotaUsage.ZERO)
        val quota = quotaWithUsage(usage, profileLimit = 4096, sessionLimit = 4096)
        val first = assertOk(quota.reserve(SkinQuotaRequest.profile(1)))
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1)))

        first.release()
        first.release()
        val retry = assertOk(quota.reserve(SkinQuotaRequest.profile(1)))
        val anchor = File(skinsRoot, "owned").apply { writeBytes(byteArrayOf(1)) }
        usage.set(SkinQuotaUsage(profileBytes = 4096, sessionBytes = 0))
        retry.transfer(anchor, SkinQuotaRequest.profile(1))
        retry.transfer(anchor, SkinQuotaRequest.profile(1))
        retry.release()
        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1)))

        val released = quotaWithUsage(
            AtomicReference(SkinQuotaUsage.ZERO),
            profileLimit = 4096,
            sessionLimit = 4096,
        ).let { assertOk(it.reserve(SkinQuotaRequest.profile(1))) }
        released.release()
        assertThrows(IllegalStateException::class.java) {
            released.transfer(anchor, SkinQuotaRequest.profile(1))
        }
    }

    @Test
    fun `quota rejection performs no mutation deletion retention or garbage collection`() {
        val existing = File(skinsRoot, "keep/object").apply { parentFile.mkdirs(); writeBytes(byteArrayOf(7)) }
        var mutations = 0
        val observing = object : SkinFileSystem by fs,
            SkinFileSystemSecurity by fs,
            SkinFileSystemBoundedListing by fs {
            override fun createDirectory(path: File) { mutations++; fs.createDirectory(path) }
            override fun writeNew(path: File, bytes: ByteArray) { mutations++; fs.writeNew(path, bytes) }
            override fun atomicMove(source: File, target: File) { mutations++; fs.atomicMove(source, target) }
            override fun deleteContained(path: File, owner: File) { mutations++; fs.deleteContained(path, owner) }
            override fun openOutput(file: File, createNew: Boolean): OutputStream {
                mutations++
                return fs.openOutput(file, createNew)
            }
        }
        val quota = SkinQuota.testing(
            skinsRoot,
            observing,
            SkinQuotaAccountingAuthority {
                SkinQuotaUsage(profileBytes = 4096, sessionBytes = 0)
            },
            SkinQuotaLimits(profileBytes = 4096, sessionBytes = 4096),
        )

        assertQuotaError(quota.reserve(SkinQuotaRequest.profile(1)))
        assertTrue(existing.exists())
        assertEquals(byteArrayOf(7).toList(), existing.readBytes().toList())
        assertEquals(0, mutations)
    }

    @Test
    fun `quarantine quota adapter rejects before provider access or staging mutation`() {
        var providerOpens = 0
        val rejecting = object : SkinQuotaAdmission {
            override val root = skinsRoot
            override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> =
                SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "full")
        }
        val result = SkinQuarantine(
            SkinPaths(skinsRoot.parentFile),
            fs,
            SkinQuotaCapacityReserver(rejecting),
        ).copy(
            SkinImportInput.SelectedFile("skin.zip") {
                providerOpens++
                ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 0x03, 0x04))
            },
        )

        assertQuotaError(result)
        assertEquals(0, providerOpens)
        assertFalse(File(skinsRoot, "staging").exists())
    }

    @Test
    fun `quota authorities require and preserve exact profile isolation`() {
        val otherRoot = File(testRoot, "profiles/other/skins").apply { mkdirs() }
        assertThrows(IllegalArgumentException::class.java) {
            SkinQuota(otherRoot, fs, SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable })
        }

        val secondRoot = File(testRoot, "separate/profiles/hollow-knight/skins").apply { mkdirs() }
        val first = SkinQuota.testing(
            skinsRoot,
            fs,
            SkinQuotaAccountingAuthority { SkinQuotaUsage(profileBytes = 9L * 4096, sessionBytes = 0) },
            SkinQuotaLimits(profileBytes = 10L * 4096, sessionBytes = 10L * 4096),
        )
        val second = SkinQuota.testing(
            secondRoot,
            fs,
            SkinQuotaAccountingAuthority { SkinQuotaUsage.ZERO },
            SkinQuotaLimits(profileBytes = 10L * 4096, sessionBytes = 10L * 4096),
        )
        assertQuotaError(first.reserve(SkinQuotaRequest.profile(4097)))
        val accepted = assertOk(second.reserve(SkinQuotaRequest.profile(4097)))
        accepted.release()

        val reservation = assertOk(second.reserve(SkinQuotaRequest.profile(1)))
        assertThrows(IllegalArgumentException::class.java) {
            reservation.transfer(File(skinsRoot, "wrong-profile"), SkinQuotaRequest.profile(1))
        }
        reservation.release()
    }

    private fun quotaWithUsage(
        usage: AtomicReference<SkinQuotaUsage>,
        profileLimit: Long,
        sessionLimit: Long,
    ): SkinQuota = SkinQuota.testing(
        skinsRoot,
        fs,
        SkinQuotaAccountingAuthority { usage.get() },
        SkinQuotaLimits(profileLimit, sessionLimit),
    )

    private fun assertQuotaError(result: SkinResult<*>): SkinResult.Error {
        assertTrue("Expected quota error, got $result", result is SkinResult.Error)
        return (result as SkinResult.Error).also {
            assertEquals(SkinImportCode.PROFILE_QUOTA_EXCEEDED, it.code)
        }
    }

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private class CountingBoundedFileSystem(
        private val delegate: FastSkinFileSystem,
    ) : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate, SkinFileSystemBoundedListing {
        var accumulatedEntries = 0
        var detectedOverflow = false

        override fun listBounded(path: File, maximumEntries: Int): List<File> =
            java.nio.file.Files.newDirectoryStream(path.toPath()).use { stream ->
                val result = ArrayList<File>(maximumEntries)
                val iterator = stream.iterator()
                while (iterator.hasNext()) {
                    if (result.size == maximumEntries) {
                        detectedOverflow = true
                        throw IllegalStateException("bounded listing overflow")
                    }
                    result += iterator.next().toFile()
                    accumulatedEntries++
                }
                result
            }
    }

    private class DelegatingFileSystem(
        private val delegate: FastSkinFileSystem,
        configure: DelegatingFileSystem.() -> Unit,
    ) : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate, SkinFileSystemBoundedListing {
        var overrideSymbolic: ((File) -> Boolean)? = null
        var overrideIdentity: ((File, SkinNodeIdentity) -> SkinNodeIdentity)? = null
        var overrideList: ((File, List<File>) -> List<File>)? = null

        init {
            configure()
        }

        override fun isSymbolicLink(file: File): Boolean =
            overrideSymbolic?.invoke(file) ?: delegate.isSymbolicLink(file)

        override fun identity(path: File): SkinNodeIdentity =
            overrideIdentity?.invoke(path, delegate.identity(path)) ?: delegate.identity(path)

        override fun list(path: File): List<File> =
            overrideList?.invoke(path, delegate.list(path)) ?: delegate.list(path)

        override fun listBounded(path: File, maximumEntries: Int): List<File> =
            overrideList?.invoke(path, delegate.listBounded(path, maximumEntries))
                ?: delegate.listBounded(path, maximumEntries)

        override fun openNoFollow(path: File): InputStream = delegate.openNoFollow(path)
        override fun openOutput(file: File, createNew: Boolean): OutputStream = delegate.openOutput(file, createNew)
        override fun openSeekableNoFollow(file: File): SeekableByteChannel = delegate.openSeekableNoFollow(file)
    }
}
