package dev.silksong.launcher.skins.session

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.registry.SkinRegistryStore
import dev.silksong.launcher.skins.quota.SkinAllocatedBytes
import dev.silksong.launcher.skins.quota.SkinAllocatedBytesAuthority
import dev.silksong.launcher.skins.quota.SkinQuota
import dev.silksong.launcher.skins.quota.SkinQuotaAccountingAuthority
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaLimits
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.quota.SkinQuotaUsage
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.UUID
import java.util.concurrent.atomic.AtomicReference
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinLaunchCoordinatorTest {
    private lateinit var testRoot: File
    private lateinit var catalog: CatalogPathSet

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-launch-coordinator").absoluteFile
        testRoot.deleteRecursively()
        catalog = PinnedCatalogFixture.load()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun quotaRejectionPrecedesEveryRegistryAndSessionMutation() {
        val requests = mutableListOf<SkinQuotaRequest>()
        val fixture = fixture(
            "quota-reject",
            quotaFactory = { root, _ ->
                object : SkinQuotaAdmission {
                    override val root = root
                    override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> {
                        requests += request
                        return SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "sessions full")
                    }
                }
            },
        )

        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, fixture.coordinator.acquire())

        assertEquals(listOf(SkinQuotaBudgets.SESSION_ACQUISITION), requests)
        assertFalse(fixture.paths.skinsRoot.exists())
        listOf(
            "registry", "sessions/sequence", "sessions/acquisition.intent", "sessions/active", "staging",
        ).forEach { relative -> assertFalse(File(fixture.paths.skinsRoot, relative).exists()) }
        assertTrue(
            fixture.fs.events.none { event ->
                event.startsWith("mkdir:") || event.startsWith("write-") || event.startsWith("rename:") ||
                    event.startsWith("delete-") || event.startsWith("sync-")
            },
        )
    }

    @Test
    fun realQuotaRejectsOneBlockBeyondCombinedAcquisitionCapacityBeforeMutation() {
        val limits = SkinQuotaLimits.V1
        val margin = SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val charge = SkinQuotaBudgets.fallbackCharge(SkinQuotaBudgets.SESSION_ACQUISITION)
        val fixture = fixture(
            "real-quota-reject",
            quotaFactory = { root, fs ->
                SkinQuota.testing(
                    root,
                    fs,
                    SkinQuotaAccountingAuthority {
                        SkinQuotaUsage(
                            profileBytes = limits.profileBytes - margin - charge + limits.allocationBlockBytes,
                            sessionBytes = limits.sessionBytes - margin - charge + limits.allocationBlockBytes,
                        )
                    },
                    limits,
                )
            },
        )

        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, fixture.coordinator.acquire())
        assertFalse(fixture.paths.skinsRoot.exists())
        assertTrue(
            fixture.fs.events.none { event ->
                event.startsWith("mkdir:") || event.startsWith("write-") || event.startsWith("rename:") ||
                    event.startsWith("delete-") || event.startsWith("sync-")
            },
        )
    }

    @Test
    fun realTreeAccountingKeepsClaimRecoveryCloseAndLaterRecoveryAvailableAtOrdinaryCeilings() {
        val real = realQuotaFixture("real-lifecycle-sequence")
        val handle = assertOk(real.fixture.coordinator.acquire())
        populateOrdinaryCeilings(real, handle)
        val gameOwner = ProcessIdentity(1000, 1002, "100")

        assertError(
            SkinImportCode.PROFILE_QUOTA_EXCEEDED,
            real.quota.reserve(SkinQuotaRequest.sessions(SkinQuotaLimits.V1.allocationBlockBytes)),
        )
        assertEquals(LeaseState.GAME_OWNED, assertOk(real.fixture.sessions.claim(handle, gameOwner)).state)
        assertEquals(LeaseMutationGate.ACTIVE, real.fixture.sessions.mutationGate(real.fixture.identity))
        assertEquals(LeaseState.GAME_OWNED, assertOk(real.fixture.sessions.recover(real.fixture.identity))?.state)
        assertEquals(LeaseState.CLOSED, assertOk(real.fixture.sessions.close(handle, "GAME_EXIT")).state)
        assertEquals(null, assertOk(real.fixture.sessions.recover(real.fixture.identity)))
        assertWithinV1(real.quota)
    }

    @Test
    fun realTreeAccountingKeepsRecoveryAvailableAfterRecoveryPersistsClosureAtOrdinaryCeilings() {
        val real = realQuotaFixture("real-repeat-recovery")
        val handle = assertOk(real.fixture.coordinator.acquire())
        populateOrdinaryCeilings(real, handle)
        val dead = object : ProcessIdentityAuthority {
            override fun self(): SelfIdentityResult = SelfIdentityResult.Known(launcherOwner)
            override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness =
                ExpectedOwnerLiveness.DefinitivelyDead

            override fun exactProcess(packageName: String, processName: String): ExactProcessPresence =
                ExactProcessPresence.Absent
        }
        val states = File(real.fixture.paths.skinsRoot, "sessions/${handle.descriptorId}/lease/states")

        assertEquals(null, assertOk(real.fixture.sessions.recover(dead)))
        assertEquals(2, states.listFiles().orEmpty().size)
        assertEquals(null, assertOk(real.fixture.sessions.recover(dead)))
        assertWithinV1(real.quota)
    }

    @Test
    fun acquisitionPublishesExactLinkedDescriptorThenPendingLeaseAndActiveBarrier() {
        val fixture = fixture("acquire")

        val handle = assertOk(fixture.coordinator.acquire())

        assertEquals(0L, handle.sessionSequence)
        assertEquals(DESCRIPTOR_ID, handle.descriptorId)
        assertEquals(LEASE_ID, handle.leaseId)
        assertEquals("sessions/$DESCRIPTOR_ID/descriptor.json", handle.descriptorPath)
        assertEquals(rawTokenHex(), handle.leaseToken)
        assertEquals("0\n", File(fixture.paths.skinsRoot, "sessions/sequence").readText(StandardCharsets.US_ASCII))

        val descriptorBytes = File(fixture.paths.skinsRoot, handle.descriptorPath).readBytes()
        assertEquals(SkinIdentity.sha256(descriptorBytes), handle.descriptorSha256)
        val descriptor = assertOk(
            SkinLaunchDescriptorCodec.parse(
                descriptorBytes,
                handle.descriptorSha256,
                DescriptorExpectations(
                    handle.descriptorId,
                    "hollow-knight",
                    "1.5.12620",
                    dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths.CATALOG_ID,
                    catalog.sha256,
                    handle.leaseId,
                ),
            ),
        )
        val state = singleState(fixture.paths.skinsRoot, handle)
        assertEquals(handle.sessionSequence, descriptor.sessionSequence)
        assertEquals(handle.sessionSequence, state.sessionSequence)
        assertEquals(handle.descriptorId, state.descriptorId)
        assertEquals(handle.descriptorSha256, state.descriptorSha256)
        assertEquals(descriptor.registryGenerationId, state.registryGenerationId)
        assertEquals(descriptor.registryGenerationSha256, state.registrySha256)
        assertEquals(descriptor.leaseTokenSha256, state.leaseTokenSha256)
        assertEquals(launcherOwner, state.launcherOwner)
        assertEquals(LeaseState.LAUNCH_PENDING, state.state)

        assertFalse(File(fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
        assertFalse(File(fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp").exists())
        assertAcquisitionOrder(fixture.fs.events, handle)
        assertRawTokenAbsentFromDisk(fixture.paths.root, RAW_TOKEN, handle.leaseToken)
    }

    @Test
    fun nonClearGateFailsBeforeIdentityReservationOrMaterialGeneration() {
        val fixture = fixture("active")
        val first = assertOk(fixture.coordinator.acquire())
        val sequenceBefore = File(fixture.paths.skinsRoot, "sessions/sequence").readBytes().toList()
        val materialCalls = fixture.materials.calls
        val selfCalls = fixture.identity.selfCalls

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.acquire())

        assertEquals(sequenceBefore, File(fixture.paths.skinsRoot, "sessions/sequence").readBytes().toList())
        assertEquals(materialCalls, fixture.materials.calls)
        assertEquals(selfCalls, fixture.identity.selfCalls)
        assertEquals(LeaseState.LAUNCH_PENDING, singleState(fixture.paths.skinsRoot, first).state)
    }

    @Test
    fun unknownSelfFailsBeforeReservationDescriptorOrLeasePublication() {
        val fixture = fixture("unknown-self", identity = FakeIdentity(SelfIdentityResult.Unknown))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.acquire())

        val sessions = File(fixture.paths.skinsRoot, "sessions")
        assertFalse(File(sessions, "sequence").exists())
        assertTrue(sessions.listFiles().orEmpty().none { it.isDirectory })
        assertEquals(0, fixture.materials.calls)
    }

    @Test
    fun invalidGeneratedMaterialConsumesReservationButPublishesNoDescriptorOrLease() {
        val invalidToken = SkinLaunchMaterial(DESCRIPTOR_ID, LEASE_ID, ByteArray(31))
        val materials = QueueMaterialAuthority(mutableListOf(invalidToken, validMaterial()))
        val fixture = fixture("invalid-material", materials = materials)

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.acquire())

        assertEquals("0\n", File(fixture.paths.skinsRoot, "sessions/sequence").readText(StandardCharsets.US_ASCII))
        assertTrue(descriptorDirectories(fixture.paths.skinsRoot).isEmpty())

        val handle = assertOk(fixture.coordinator.acquire())
        assertEquals(1L, handle.sessionSequence)
        assertEquals("1\n", File(fixture.paths.skinsRoot, "sessions/sequence").readText(StandardCharsets.US_ASCII))
    }

    @Test
    fun invalidOrCollidingGeneratedIdsAreRejectedAfterMonotonicReservation() {
        val nil = UUID.fromString("00000000-0000-0000-0000-000000000000")
        val materials = QueueMaterialAuthority(
            mutableListOf(
                SkinLaunchMaterial(nil, LEASE_ID, RAW_TOKEN.copyOf()),
                SkinLaunchMaterial(DESCRIPTOR_ID, DESCRIPTOR_ID, RAW_TOKEN.copyOf()),
                validMaterial(),
            ),
        )
        val fixture = fixture("invalid-ids", materials = materials)

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.acquire())
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.acquire())
        val handle = assertOk(fixture.coordinator.acquire())

        assertEquals(2L, handle.sessionSequence)
        assertEquals(1, descriptorDirectories(fixture.paths.skinsRoot).size)
    }

    @Test
    fun canonicalNameBasedIdsRemainValidForDeterministicAuthorities() {
        val descriptor = UUID.nameUUIDFromBytes("deterministic-descriptor".toByteArray(StandardCharsets.US_ASCII))
        val lease = UUID.nameUUIDFromBytes("deterministic-lease".toByteArray(StandardCharsets.US_ASCII))
        val fixture = fixture(
            "name-ids",
            materials = QueueMaterialAuthority(
                mutableListOf(SkinLaunchMaterial(descriptor, lease, RAW_TOKEN.copyOf())),
            ),
        )

        val handle = assertOk(fixture.coordinator.acquire())

        assertEquals(descriptor, handle.descriptorId)
        assertEquals(lease, handle.leaseId)
    }

    @Test
    fun coordinatorCopiesAuthorityOwnedRawToken() {
        val callerOwned = RAW_TOKEN.copyOf()
        val fixture = fixture(
            "token-copy",
            materials = QueueMaterialAuthority(mutableListOf(SkinLaunchMaterial(DESCRIPTOR_ID, LEASE_ID, callerOwned))),
        )

        val handle = assertOk(fixture.coordinator.acquire())
        callerOwned.fill(0x7f)

        assertEquals(rawTokenHex(), handle.leaseToken)
        assertEquals(
            SkinIdentity.sha256(RAW_TOKEN),
            singleState(fixture.paths.skinsRoot, handle).leaseTokenSha256,
        )
    }

    @Test
    fun definitiveFailureClosesOnlyExactHandleAndIsIdempotent() {
        val fixture = fixture("close")
        val handle = assertOk(fixture.coordinator.acquire())
        val wrong = handle.copy(leaseToken = "f".repeat(64))
        val before = fixture.fs.events.size

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, fixture.coordinator.closeDefinitiveFailure(wrong, "LAUNCH_FAILED"))
        assertTrue(fixture.fs.events.drop(before).none { it.startsWith("write-") || it.startsWith("rename:") || it.startsWith("delete-") })

        val closed = assertOk(fixture.coordinator.closeDefinitiveFailure(handle, "LAUNCH_FAILED"))
        assertEquals(LeaseState.CLOSED, closed.state)
        assertEquals(closed, assertOk(fixture.coordinator.closeDefinitiveFailure(handle, "LAUNCH_FAILED")))
        assertFalse(File(fixture.paths.skinsRoot, "sessions/active").exists())
    }

    @Test
    fun sequenceFaultsStopAcquisitionBeforeMaterialDescriptorLeaseOrActiveAndRetryDoesNotReuseVisibleEvidence() {
        val failures = listOf(
            Triple("write", "write-new:sequence.tmp", 0L),
            Triple("file-sync", "sync-file:sequence.tmp", 1L),
            Triple("rename", "rename:sequence.tmp->sequence", 1L),
            Triple("directory-barrier", "sync-dir:sessions", 1L),
        )
        failures.forEach { (label, event, retrySequence) ->
            val fixture = fixture("sequence-fault-$label")
            fixture.fs.failOnEvent = event
            fixture.fs.failOnOccurrence = if (event == "sync-dir:sessions") 2 else 1

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())

            assertEquals("Sequence failure did not stop at $event: ${fixture.fs.events}", event, fixture.fs.events.last())
            assertEquals(0, fixture.materials.calls)
            assertTrue(descriptorDirectories(fixture.paths.skinsRoot).isEmpty())
            assertFalse(File(fixture.paths.skinsRoot, "sessions/active").exists())

            fixture.fs.failOnEvent = null
            val handle = assertOk(fixture.coordinator.acquire())
            assertEquals(retrySequence, handle.sessionSequence)
        }
    }

    @Test
    fun failedActivePublicationIsResumedOnlyForExactPendingThenClosedBeforeFailureReturns() {
        val fixture = fixture("failed-active-publication")
        fixture.fs.failOnEvent = "sync-file:active.tmp"
        fixture.fs.failOnOccurrence = 1

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())

        assertFalse(File(fixture.paths.skinsRoot, "sessions/active").exists())
        val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
        val states = File(descriptor, "lease/states").listFiles().orEmpty()
        assertEquals(2, states.size)
        val current = requireNotNull(LeasePointerCodec.parse(File(descriptor, "lease/current").readBytes()))
        assertEquals(LeaseState.CLOSED, current.state)
        assertEquals("0\n", File(fixture.paths.skinsRoot, "sessions/sequence").readText(StandardCharsets.US_ASCII))
    }

    @Test
    fun restartAbandonsOnlyIntentBoundDescriptorAcrossPhaseDurabilityCrashCuts() {
        val failures = listOf(
            Triple("write", "write-new:acquisition.intent.tmp", 2),
            Triple("file-sync", "sync-file:acquisition.intent.tmp", 2),
            Triple("rename", "rename:acquisition.intent.tmp->acquisition.intent", 2),
            Triple("directory-barrier", "sync-dir:sessions", 5),
        )
        failures.forEach { (label, event, occurrence) ->
            val fixture = fixture("descriptor-crash-cut-$label")
            fixture.fs.failOnEvent = event
            fixture.fs.failOnOccurrence = occurrence

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())

            val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
            assertEquals(setOf("descriptor.json", "descriptor.sha256", ".complete"), descriptor.listFiles()!!.map(File::getName).toSet())
            val intentFile = File(fixture.paths.skinsRoot, "sessions/acquisition.intent")
            assertTrue(intentFile.isFile)
            listOf(intentFile, File(fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp"))
                .filter(File::exists)
                .forEach { assertFalse(it.readText(StandardCharsets.US_ASCII).contains(rawTokenHex())) }

            fixture.fs.failOnEvent = null
            assertEquals(null, assertOk(fixture.sessions.recover(fixture.identity)))
            assertFalse(descriptor.exists())
            assertFalse(intentFile.exists())
            assertFalse(File(fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp").exists())
            assertEquals("0\n", File(fixture.paths.skinsRoot, "sessions/sequence").readText(StandardCharsets.US_ASCII))
        }
    }

    @Test
    fun orphanAbandonmentNeverRecursivelyDeletesAVisibleSessionsDescriptor() {
        val fixture = fixture("partial-recursive-delete")
        fixture.fs.failOnEvent = "write-new:acquisition.intent.tmp"
        fixture.fs.failOnOccurrence = 2
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())
        fixture.fs.failOnEvent = null
        val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
        var recursiveDeleteCalls = 0
        fixture.fs.beforeDelete = { path, _ ->
            if (path.absoluteFile.normalize() == descriptor.absoluteFile.normalize()) {
                recursiveDeleteCalls++
                assertTrue(File(path, "descriptor.json").delete())
                throw IllegalStateException("simulated process death during recursive deletion")
            }
        }

        val firstRecovery = fixture.sessions.recover(fixture.identity)
        fixture.fs.beforeDelete = null
        val eventualRecovery = if (firstRecovery is SkinResult.Error) {
            fixture.sessions.recover(fixture.identity)
        } else {
            firstRecovery
        }

        assertEquals(null, assertOk(eventualRecovery))
        assertEquals(0, recursiveDeleteCalls)
        assertFalse(descriptor.exists())
    }

    @Test
    fun orphanQuarantineCrashCutsResumeAcrossRenameBarriersAndIntentRemoval() {
        data class Cut(val label: String, val event: (OrphanCut) -> String, val relativeOccurrence: Int)
        val cuts = listOf(
            Cut("rename", { "rename:${it.descriptor.name}->${it.tombstone.name}" }, 1),
            Cut("source-barrier", { "sync-dir:sessions" }, 1),
            Cut("destination-barrier", { "sync-dir:staging" }, 1),
            Cut("intent-removal", { "delete-contained:acquisition.intent" }, 1),
            Cut("post-removal-barrier", { "sync-dir:sessions" }, 2),
        )
        cuts.forEach { cut ->
            val orphan = descriptorOnlyOrphan("quarantine-${cut.label}")
            val event = cut.event(orphan)
            val beforeRecovery = orphan.fixture.fs.events.size
            orphan.fixture.fs.failOnEvent = event
            orphan.fixture.fs.failOnOccurrence = orphan.fixture.fs.events.count { it == event } + cut.relativeOccurrence

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, orphan.fixture.sessions.recover(orphan.fixture.identity))

            orphan.fixture.fs.failOnEvent = null
            assertEquals(null, assertOk(orphan.fixture.sessions.recover(orphan.fixture.identity)))
            assertFalse(orphan.descriptor.exists())
            assertTrue(orphan.tombstone.isDirectory)
            assertFalse(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
            assertFalse(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp").exists())
            assertEquals(null, assertOk(orphan.fixture.sessions.recover(orphan.fixture.identity)))

            val recoveryEvents = orphan.fixture.fs.events.drop(beforeRecovery)
            val rename = recoveryEvents.lastIndexOf("rename:${orphan.descriptor.name}->${orphan.tombstone.name}")
            val sourceBarrier = recoveryEvents.withIndex().firstOrNull { (index, value) ->
                index > rename && value == "sync-dir:sessions"
            }?.index ?: -1
            val destinationBarrier = recoveryEvents.withIndex().firstOrNull { (index, value) ->
                index > sourceBarrier && value == "sync-dir:staging"
            }?.index ?: -1
            val intentRemoval = recoveryEvents.withIndex().firstOrNull { (index, value) ->
                index > destinationBarrier && value == "delete-contained:acquisition.intent"
            }?.index ?: -1
            val postRemovalBarrier = recoveryEvents.withIndex().firstOrNull { (index, value) ->
                index > intentRemoval && value == "sync-dir:sessions"
            }?.index ?: -1
            assertTrue(
                "Orphan quarantine order changed: $recoveryEvents",
                rename >= 0 && rename < sourceBarrier && sourceBarrier < destinationBarrier &&
                    destinationBarrier < intentRemoval && intentRemoval < postRemovalBarrier,
            )
            assertTrue(recoveryEvents.none { it == "delete-contained:${orphan.descriptor.name}" })
        }
    }

    @Test
    fun ambiguousRenameOutcomeResumesFromExactTombstone() {
        val orphan = descriptorOnlyOrphan("quarantine-ambiguous-rename")
        orphan.fixture.fs.afterMove = { source, target ->
            if (source.absoluteFile.normalize() == orphan.descriptor.absoluteFile.normalize() &&
                target.absoluteFile.normalize() == orphan.tombstone.absoluteFile.normalize()
            ) {
                throw IllegalStateException("simulated process loss after atomic rename")
            }
        }

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, orphan.fixture.sessions.recover(orphan.fixture.identity))
        assertFalse(orphan.descriptor.exists())
        assertTrue(orphan.tombstone.isDirectory)
        assertTrue(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").isFile)

        orphan.fixture.fs.afterMove = null
        assertEquals(null, assertOk(orphan.fixture.sessions.recover(orphan.fixture.identity)))
        assertTrue(orphan.tombstone.isDirectory)
        assertFalse(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
    }

    @Test
    fun twoPhaseIntentRemovalCrashCutsKeepTheSameTombstoneAuthority() {
        data class Cut(val label: String, val event: String, val relativeOccurrence: Int)
        listOf(
            Cut("temporary-delete", "delete-contained:acquisition.intent.tmp", 1),
            Cut("temporary-barrier", "sync-dir:sessions", 2),
            Cut("durable-delete", "delete-contained:acquisition.intent", 1),
            Cut("durable-barrier", "sync-dir:sessions", 3),
        ).forEach { cut ->
            val orphan = descriptorOnlyOrphan(
                "quarantine-two-phase-${cut.label}",
                phaseFailureEvent = "sync-file:acquisition.intent.tmp",
            )
            val temporary = File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp")
            assertTrue(temporary.isFile)
            orphan.fixture.fs.failOnEvent = cut.event
            orphan.fixture.fs.failOnOccurrence = orphan.fixture.fs.events.count { it == cut.event } + cut.relativeOccurrence

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, orphan.fixture.sessions.recover(orphan.fixture.identity))

            orphan.fixture.fs.failOnEvent = null
            assertEquals(null, assertOk(orphan.fixture.sessions.recover(orphan.fixture.identity)))
            assertFalse(orphan.descriptor.exists())
            assertTrue(orphan.tombstone.isDirectory)
            assertFalse(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
            assertFalse(temporary.exists())
        }
    }

    @Test
    fun visibleTombstoneMustStillExactlyBindItsIntentBeforeCompletion() {
        val orphan = descriptorOnlyOrphan("quarantine-tombstone-revalidation")
        orphan.fixture.fs.afterMove = { source, target ->
            if (source.absoluteFile.normalize() == orphan.descriptor.absoluteFile.normalize() &&
                target.absoluteFile.normalize() == orphan.tombstone.absoluteFile.normalize()
            ) {
                throw IllegalStateException("simulated process loss after atomic rename")
            }
        }
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, orphan.fixture.sessions.recover(orphan.fixture.identity))
        orphan.fixture.fs.afterMove = null
        File(orphan.tombstone, "descriptor.json").writeText("{}", StandardCharsets.US_ASCII)
        val beforeRecovery = orphan.fixture.fs.events.size

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, orphan.fixture.sessions.recover(orphan.fixture.identity))

        assertTrue(orphan.tombstone.isDirectory)
        assertTrue(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").isFile)
        assertTrue(
            orphan.fixture.fs.events.drop(beforeRecovery).none {
                it.startsWith("rename:") || it.startsWith("delete-") || it.startsWith("write-") || it.startsWith("sync-")
            },
        )
    }

    @Test
    fun conflictingDeterministicTombstoneRejectsBeforeSourceMutation() {
        val orphan = descriptorOnlyOrphan("quarantine-conflict")
        orphan.tombstone.mkdir()
        File(orphan.tombstone, "conflict").writeText("not acquisition evidence", StandardCharsets.US_ASCII)
        val beforeRecovery = orphan.fixture.fs.events.size

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, orphan.fixture.sessions.recover(orphan.fixture.identity))

        assertTrue(orphan.descriptor.isDirectory)
        assertTrue(orphan.tombstone.isDirectory)
        assertTrue(File(orphan.fixture.paths.skinsRoot, "sessions/acquisition.intent").isFile)
        assertTrue(
            orphan.fixture.fs.events.drop(beforeRecovery).none {
                it.startsWith("rename:") || it.startsWith("delete-") || it.startsWith("write-") || it.startsWith("sync-")
            },
        )
    }

    @Test
    fun restartAbandonsOnlyIntentBoundPendingStateAcrossPrePointerCrashCuts() {
        listOf("write-new:next.tmp", "sync-file:next.tmp").forEach { event ->
            val fixture = fixture("pending-crash-cut-${event.substringBefore(':')}")
            fixture.fs.failOnEvent = event
            fixture.fs.failOnOccurrence = 1
            fixture.fs.beforeContainment = { _, _, _ ->
                if (event in fixture.fs.events) {
                    throw IllegalStateException("simulate process loss before in-process containment")
                }
            }

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())

            fixture.fs.beforeContainment = null
            fixture.fs.failOnEvent = null
            val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
            val states = File(descriptor, "lease/states").listFiles().orEmpty()
            assertEquals(1, states.size)
            val expectedLeaseFiles = if (event.startsWith("sync-file")) setOf("states", "next.tmp") else setOf("states")
            assertEquals(expectedLeaseFiles, File(descriptor, "lease").listFiles().orEmpty().map(File::getName).toSet())
            assertTrue(File(fixture.paths.skinsRoot, "sessions/acquisition.intent").isFile)

            assertEquals(null, assertOk(fixture.sessions.recover(fixture.identity)))
            assertFalse(descriptor.exists())
            assertFalse(File(fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
        }
    }

    @Test
    fun restartClearsExactStaleIntentOnlyAfterPendingAndActiveBarriers() {
        val fixture = fixture("stale-established-intent")
        fixture.fs.failOnEvent = "delete-contained:acquisition.intent"
        fixture.fs.failOnOccurrence = 1
        fixture.fs.beforeContainment = { _, _, _ ->
            if ("delete-contained:acquisition.intent" in fixture.fs.events) {
                throw IllegalStateException("simulate process loss before containment")
            }
        }

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())

        fixture.fs.beforeContainment = null
        fixture.fs.failOnEvent = null
        val intentFile = File(fixture.paths.skinsRoot, "sessions/acquisition.intent")
        assertTrue(intentFile.isFile)
        val activeBefore = requireNotNull(LeasePointerCodec.parse(File(fixture.paths.skinsRoot, "sessions/active").readBytes()))
        assertEquals(LeaseState.LAUNCH_PENDING, activeBefore.state)

        val recovered = assertOk(fixture.sessions.recover(fixture.identity))
        assertEquals(activeBefore, recovered)
        assertFalse(intentFile.exists())
        assertEquals(activeBefore, requireNotNull(LeasePointerCodec.parse(File(fixture.paths.skinsRoot, "sessions/active").readBytes())))
    }

    @Test
    fun restartClearsExactStaleIntentAfterTerminalBarrier() {
        val fixture = fixture("stale-terminal-intent")
        val handle = assertOk(fixture.coordinator.acquire())
        val pending = singleState(fixture.paths.skinsRoot, handle)
        val intent = SkinAcquisitionIntent(
            SkinAcquisitionPhase.DESCRIPTOR_DURABLE,
            handle.descriptorId,
            handle.descriptorSha256,
            handle.descriptorPath,
            handle.leaseId,
            pending.leaseTokenSha256,
            handle.sessionSequence,
            pending.registryGenerationId,
            pending.registrySha256,
            pending.launcherOwner,
        )
        val closed = assertOk(fixture.coordinator.closeDefinitiveFailure(handle, "LAUNCH_FAILED"))
        assertEquals(LeaseState.CLOSED, closed.state)
        val intentFile = File(fixture.paths.skinsRoot, "sessions/acquisition.intent")
        intentFile.writeBytes(SkinAcquisitionIntentCodec.canonical(intent))

        assertEquals(null, assertOk(fixture.sessions.recover(fixture.identity)))
        assertFalse(intentFile.exists())
        assertFalse(File(fixture.paths.skinsRoot, "sessions/active").exists())
        assertEquals(closed, requireNotNull(LeasePointerCodec.parse(File(fixture.paths.skinsRoot, "sessions/${handle.descriptorId}/lease/current").readBytes())))
    }

    @Test
    fun mismatchedIntentNeverAuthorizesDescriptorAbandonment() {
        val fixture = fixture("mismatched-orphan")
        fixture.fs.failOnEvent = "write-new:acquisition.intent.tmp"
        fixture.fs.failOnOccurrence = 2
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())
        fixture.fs.failOnEvent = null
        val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
        val intentFile = File(fixture.paths.skinsRoot, "sessions/acquisition.intent")
        val exact = assertOk(SkinAcquisitionIntentCodec.parse(intentFile.readBytes()))
        intentFile.writeBytes(
            SkinAcquisitionIntentCodec.canonical(exact.copy(descriptorSha256 = "f".repeat(64))),
        )
        val beforeEvents = fixture.fs.events.size

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, fixture.sessions.recover(fixture.identity))

        assertTrue(descriptor.exists())
        assertTrue(intentFile.exists())
        assertTrue(fixture.fs.events.drop(beforeEvents).none { it.startsWith("delete-") || it.startsWith("rename:") || it.startsWith("write-") })
    }

    @Test
    fun allEvidenceIsValidatedBeforeAuthorizedOrphanMutation() {
        val fixture = fixture("orphan-validation-order")
        fixture.fs.failOnEvent = "write-new:acquisition.intent.tmp"
        fixture.fs.failOnOccurrence = 2
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())
        fixture.fs.failOnEvent = null
        val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
        File(fixture.paths.skinsRoot, "sessions/active.tmp").writeText("malformed", StandardCharsets.US_ASCII)
        val beforeEvents = fixture.fs.events.size

        assertError(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, fixture.sessions.recover(fixture.identity))

        assertTrue(descriptor.exists())
        assertTrue(File(fixture.paths.skinsRoot, "sessions/acquisition.intent").exists())
        assertTrue(fixture.fs.events.drop(beforeEvents).none { it.startsWith("delete-") || it.startsWith("rename:") || it.startsWith("write-") })
    }

    @Test
    fun coordinatorRejectsAnyProfileOrLockRootOtherThanItsExactHollowKnightPaths() {
        val hollow = profilePaths("profile-check-hollow", "hollow-knight")
        val silksong = profilePaths("profile-check-silksong", "silksong")
        val fs = fastFs()
        val lock = SkinLockManager(hollow.skinsRoot)

        val quota = permittedQuota(hollow.skinsRoot)

        assertThrows(IllegalArgumentException::class.java) {
            SkinLaunchCoordinator(
                silksong,
                fs,
                lock,
                SkinRegistryStore(hollow.skinsRoot, quota, fs, lock),
                SkinSessionStore(hollow.skinsRoot, fs, lock, TARGET_PROCESS, quota),
                SkinDescriptorBuilder(SkinPaths(hollow.root), fs, catalog),
                FakeIdentity(SelfIdentityResult.Known(launcherOwner)),
                QueueMaterialAuthority(mutableListOf(validMaterial())),
                quota,
            )
        }
    }

    private fun realQuotaFixture(label: String): RealQuotaFixture {
        val allocatedBytes = mutableMapOf<String, Long>()
        val quotaRef = AtomicReference<SkinQuota>()
        val fixture = fixture(
            label,
            quotaFactory = { root, fs ->
                SkinQuota(
                    root,
                    fs,
                    SkinAllocatedBytesAuthority { file ->
                        allocatedBytes[file.absoluteFile.normalize().path]?.let(SkinAllocatedBytes::Available)
                            ?: SkinAllocatedBytes.Unavailable
                    },
                ).also(quotaRef::set)
            },
        )
        return RealQuotaFixture(fixture, quotaRef.get(), allocatedBytes)
    }

    private fun populateOrdinaryCeilings(real: RealQuotaFixture, handle: SkinLaunchHandle) {
        val limits = SkinQuotaLimits.V1
        val sessionTarget = limits.sessionBytes - SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        val profileTarget = limits.profileBytes - SkinQuotaBudgets.LIFECYCLE_MARGIN_BYTES
        var usage = assertOk(real.quota.usage())
        val descriptor = File(real.fixture.paths.skinsRoot, handle.descriptorPath)
        val descriptorCharge = fallbackCharge(descriptor)
        real.allocatedBytes[descriptor.absoluteFile.normalize().path] =
            descriptorCharge + (sessionTarget - usage.sessionBytes)
        usage = assertOk(real.quota.usage())
        assertEquals(sessionTarget, usage.sessionBytes)

        val registryCurrent = File(real.fixture.paths.skinsRoot, "registry/current")
        val registryCharge = fallbackCharge(registryCurrent)
        real.allocatedBytes[registryCurrent.absoluteFile.normalize().path] =
            registryCharge + (profileTarget - usage.profileBytes)
        assertEquals(SkinQuotaUsage(profileTarget, sessionTarget), assertOk(real.quota.usage()))
    }

    private fun fallbackCharge(file: File): Long {
        val length = file.length()
        val block = SkinQuotaLimits.V1.allocationBlockBytes
        if (length == 0L) return 0L
        val remainder = length % block
        return if (remainder == 0L) length else length + block - remainder
    }

    private fun assertWithinV1(quota: SkinQuota) {
        val usage = assertOk(quota.usage())
        assertTrue(usage.profileBytes <= SkinQuotaLimits.V1.profileBytes)
        assertTrue(usage.sessionBytes <= SkinQuotaLimits.V1.sessionBytes)
    }

    private fun fixture(
        label: String,
        identity: FakeIdentity = FakeIdentity(SelfIdentityResult.Known(launcherOwner)),
        materials: QueueMaterialAuthority = QueueMaterialAuthority(mutableListOf(validMaterial())),
        quotaFactory: ((File, SkinFileSystem) -> SkinQuotaAdmission)? = null,
    ): Fixture {
        val paths = profilePaths(label, "hollow-knight")
        val fs = fastFs()
        val lock = SkinLockManager(paths.skinsRoot)
        val quota = quotaFactory?.invoke(paths.skinsRoot, fs) ?: permittedQuota(paths.skinsRoot)
        val registry = SkinRegistryStore(paths.skinsRoot, quota, fs, lock)
        val sessions = SkinSessionStore(paths.skinsRoot, fs, lock, TARGET_PROCESS, quota)
        val coordinator = SkinLaunchCoordinator(
            paths,
            fs,
            lock,
            registry,
            sessions,
            SkinDescriptorBuilder(SkinPaths(paths.root), fs, catalog),
            identity,
            materials,
            quota,
        )
        return Fixture(paths, fs, sessions, coordinator, identity, materials)
    }

    private fun profilePaths(label: String, profileId: String): ProfilePaths {
        val files = File(testRoot, label).apply { mkdirs() }
        return ProfilePaths(files, GameProfiles.require(profileId)).also { it.root.mkdirs() }
    }

    private fun fastFs(): FaultingSkinFileSystem =
        FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }

    private fun permittedQuota(root: File): SkinQuotaAdmission = object : SkinQuotaAdmission {
        override val root = root.absoluteFile.normalize()
        override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> =
            SkinResult.Ok(permittedReservation())
    }

    private fun permittedReservation(): SkinQuotaReservation = object : SkinQuotaReservation {
        override fun transfer(anchor: File, actual: SkinQuotaRequest) = Unit
        override fun release() = Unit
    }

    private fun singleState(root: File, handle: SkinLaunchHandle): LeaseStateDocument {
        val states = File(root, "sessions/${handle.descriptorId}/lease/states").listFiles().orEmpty()
        assertEquals(1, states.size)
        return assertOk(SkinLeaseStateCodec.parse(File(states.single(), "lease.json").readBytes()))
    }

    private fun descriptorDirectories(root: File): List<File> =
        File(root, "sessions").listFiles().orEmpty().filter { it.isDirectory && UUID_PATTERN.matches(it.name) }

    private fun descriptorOnlyOrphan(
        label: String,
        phaseFailureEvent: String = "write-new:acquisition.intent.tmp",
    ): OrphanCut {
        val fixture = fixture(label)
        fixture.fs.failOnEvent = phaseFailureEvent
        fixture.fs.failOnOccurrence = 2
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, fixture.coordinator.acquire())
        fixture.fs.failOnEvent = null
        val descriptor = descriptorDirectories(fixture.paths.skinsRoot).single()
        val intentFile = File(fixture.paths.skinsRoot, "sessions/acquisition.intent")
        val intentTemporary = File(fixture.paths.skinsRoot, "sessions/acquisition.intent.tmp")
        val intent = assertOk(
            SkinAcquisitionIntentCodec.parse((intentTemporary.takeIf(File::exists) ?: intentFile).readBytes()),
        )
        val fingerprint = SkinIdentity.sha256(
            SkinAcquisitionIntentCodec.canonical(intent.copy(phase = SkinAcquisitionPhase.PREPARED)),
        )
        val tombstone = File(fixture.paths.skinsRoot, "staging/recovery-acquisition-$fingerprint")
        return OrphanCut(fixture, descriptor, tombstone)
    }

    private fun assertAcquisitionOrder(events: List<String>, handle: SkinLaunchHandle) {
        val reserve = events.indexOf("rename:sequence.tmp->sequence")
        val intentPrepared = events.indexOf("rename:acquisition.intent.tmp->acquisition.intent")
        val descriptor = events.indexOf("rename:descriptor-${handle.descriptorId}->${handle.descriptorId}")
        val descriptorBarrier = events.withIndex().firstOrNull { (index, event) ->
            index > descriptor && event == "sync-dir:sessions"
        }?.index ?: -1
        val intentAdvanced = events.withIndex().firstOrNull { (index, event) ->
            index > descriptorBarrier && event == "rename:acquisition.intent.tmp->acquisition.intent"
        }?.index ?: -1
        val pendingState = events.indexOfFirst { it.startsWith("rename:lease-${handle.descriptorId}-${handle.leaseId}-0->ls-") }
        val active = events.indexOf("rename:active.tmp->active")
        val activeBarrier = events.withIndex().firstOrNull { (index, event) ->
            index > active && event == "sync-dir:sessions"
        }?.index ?: -1
        val intentCleared = events.withIndex().firstOrNull { (index, event) ->
            index > activeBarrier && event == "delete-contained:acquisition.intent"
        }?.index ?: -1
        val required = listOf(reserve, intentPrepared, descriptor, descriptorBarrier, intentAdvanced, pendingState, active, activeBarrier, intentCleared)
        assertTrue("Missing acquisition durability events: $events", required.all { it >= 0 })
        assertTrue(
            "Acquisition order changed: $events",
            reserve < intentPrepared && intentPrepared < descriptor && descriptor < descriptorBarrier &&
                descriptorBarrier < intentAdvanced && intentAdvanced < pendingState && pendingState < active &&
                active < activeBarrier && activeBarrier < intentCleared,
        )
    }

    private fun assertRawTokenAbsentFromDisk(root: File, raw: ByteArray, tokenText: String) {
        val persisted = root.walkTopDown().filter(File::isFile).toList()
        persisted.forEach { file ->
            val bytes = file.readBytes()
            assertFalse("Raw token bytes persisted in ${file.path}", bytes.containsSubsequence(raw))
            assertFalse("Raw token text persisted in ${file.path}", bytes.toString(StandardCharsets.ISO_8859_1).contains(tokenText))
        }
    }

    private fun ByteArray.containsSubsequence(needle: ByteArray): Boolean {
        if (needle.isEmpty() || needle.size > size) return false
        return (0..size - needle.size).any { offset ->
            needle.indices.all { index -> this[offset + index] == needle[index] }
        }
    }

    private fun validMaterial() = SkinLaunchMaterial(DESCRIPTOR_ID, LEASE_ID, RAW_TOKEN.copyOf())

    private fun rawTokenHex(): String = RAW_TOKEN.joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals("Unexpected result: $result", code, (result as SkinResult.Error).code)
    }

    private data class OrphanCut(
        val fixture: Fixture,
        val descriptor: File,
        val tombstone: File,
    )

    private data class RealQuotaFixture(
        val fixture: Fixture,
        val quota: SkinQuota,
        val allocatedBytes: MutableMap<String, Long>,
    )

    private data class Fixture(
        val paths: ProfilePaths,
        val fs: FaultingSkinFileSystem,
        val sessions: SkinSessionStore,
        val coordinator: SkinLaunchCoordinator,
        val identity: FakeIdentity,
        val materials: QueueMaterialAuthority,
    )

    private class QueueMaterialAuthority(
        private val values: MutableList<SkinLaunchMaterial>,
    ) : SkinLaunchMaterialAuthority {
        var calls: Int = 0

        override fun create(): SkinLaunchMaterial {
            calls++
            return values.removeAt(0)
        }
    }

    private class FakeIdentity(
        private val selfResult: SelfIdentityResult,
    ) : ProcessIdentityAuthority {
        var selfCalls: Int = 0

        override fun self(): SelfIdentityResult {
            selfCalls++
            return selfResult
        }

        override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = ExpectedOwnerLiveness.Alive(expected)

        override fun exactProcess(packageName: String, processName: String): ExactProcessPresence = ExactProcessPresence.Absent
    }

    private companion object {
        val DESCRIPTOR_ID: UUID = UUID.fromString("12345678-1234-4234-8234-123456789abc")
        val LEASE_ID: UUID = UUID.fromString("22345678-1234-4234-8234-123456789abc")
        val RAW_TOKEN: ByteArray = ByteArray(32) { it.toByte() }
        val launcherOwner = ProcessIdentity(1000, 1001, "99")
        val TARGET_PROCESS = SkinTargetProcess("com.example.hollowknight", "com.example.hollowknight")
        val UUID_PATTERN = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    }
}
