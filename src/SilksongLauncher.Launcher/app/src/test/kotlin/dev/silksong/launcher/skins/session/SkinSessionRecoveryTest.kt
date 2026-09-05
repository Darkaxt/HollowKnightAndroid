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
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinSessionRecoveryTest {
    private lateinit var testRoot: File
    private lateinit var catalog: CatalogPathSet

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-session-recovery").absoluteFile
        testRoot.deleteRecursively()
        catalog = PinnedCatalogFixture.load()
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun acceptsEveryQualifiedPointerTupleAndCanonicalizesIt() {
        val cases = listOf(
            AcceptedCase(ChainKind.PENDING, 0, null, null),
            AcceptedCase(ChainKind.OWNED, 1, 0, null),
            AcceptedCase(ChainKind.OWNED, null, 1, null),
            AcceptedCase(ChainKind.PENDING, null, null, 0),
            AcceptedCase(ChainKind.OWNED, 0, null, 1),
            AcceptedCase(ChainKind.OWNED, 0, 0, 1),
            AcceptedCase(ChainKind.CLOSED_OWNED, 1, 0, 2),
            AcceptedCase(ChainKind.CLOSED_OWNED, 1, 1, 2),
            AcceptedCase(ChainKind.PENDING, 0, null, 0),
            AcceptedCase(ChainKind.OWNED, 1, 0, 1),
        )

        cases.forEachIndexed { index, case ->
            val root = root("accepted-$index")
            val fixture = writeFixture(root, index, case.kind)
            setTuple(fixture, case.current, case.previous, case.next)
            val recovered = assertOk(store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)))

            if (case.kind.closed) {
                assertNull(recovered)
                assertFalse(File(root, "sessions/active").exists())
            } else {
                assertEquals(fixture.heads.last(), recovered)
                assertEquals(fixture.heads.last(), readPointer(File(root, "sessions/active")))
            }
            assertCanonicalPointers(fixture)
        }
    }

    @Test
    fun rejectsRepresentativeIllegalPointerTuplesWithoutMutation() {
        val cases = listOf(
            AcceptedCase(ChainKind.OWNED, 1, null, null),
            AcceptedCase(ChainKind.OWNED, null, null, 1),
            AcceptedCase(ChainKind.OWNED, 0, 1, 1),
            AcceptedCase(ChainKind.OWNED, 1, 1, 1),
            AcceptedCase(ChainKind.PENDING, 0, 0, null),
            AcceptedCase(ChainKind.PENDING, null, null, null),
        )

        cases.forEachIndexed { index, case ->
            val root = root("illegal-$index")
            val fixture = writeFixture(root, index + 100, case.kind)
            setTuple(fixture, case.current, case.previous, case.next)
            val before = pointerSnapshot(fixture)

            assertError(
                SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
                store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
            )
            assertEquals(before, pointerSnapshot(fixture))
        }
    }

    @Test
    fun retriesCanonicalRepairAfterEveryInjectedPointerBarrier() {
        val events = listOf(
            "write-new:previous.tmp" to 1,
            "sync-file:previous.tmp" to 1,
            "rename:previous.tmp->previous" to 1,
            "sync-dir:lease" to 1,
            "write-new:current.tmp" to 1,
            "sync-file:current.tmp" to 1,
            "rename:current.tmp->current" to 1,
            "sync-dir:lease" to 2,
            "delete-contained:next" to 1,
            "sync-dir:lease" to 3,
            "sync-dir:lease" to 4,
        )
        events.forEachIndexed { index, (event, occurrence) ->
            val root = root("repair-$index")
            val fixture = writeFixture(root, index + 200, ChainKind.CLOSED_OWNED)
            setTuple(fixture, current = 1, previous = 0, next = 2)
            val fs = fastFaultingFs()
            fs.failOnEvent = event
            fs.failOnOccurrence = occurrence

            assertError(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                store(root, fs).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
            )
            fs.failOnEvent = null
            assertNull(assertOk(store(root, fs).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT))))
            assertCanonicalPointers(fixture)
        }
    }

    @Test
    fun retriesExactRecoveryClosePublishedBeforeNextInstallation() {
        val cases = listOf(
            Triple(ChainKind.PENDING, "write-new:next.tmp", 1),
            Triple(ChainKind.PENDING, "sync-file:next.tmp", 1),
            Triple(ChainKind.PENDING, "rename:next.tmp->next", 1),
            Triple(ChainKind.OWNED, "write-new:next.tmp", 1),
            Triple(ChainKind.OWNED, "sync-file:next.tmp", 1),
            Triple(ChainKind.OWNED, "rename:next.tmp->next", 1),
        )
        cases.forEachIndexed { index, (kind, event, occurrence) ->
            val root = root("recovery-close-resume-$index")
            val fixture = writeFixture(root, 260 + index, kind, activeSequence = kind.finalSequence)
            val fs = fastFaultingFs().apply {
                failOnEvent = event
                failOnOccurrence = occurrence
            }
            val liveness = if (kind == ChainKind.PENDING) {
                FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)
            } else {
                FakeLiveness(OwnerMode.DEAD, PresenceMode.UNKNOWN)
            }

            assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store(root, fs).recover(liveness))
            assertEquals(kind.finalSequence + 2, fixture.leaseDirectory.resolve("states").listFiles()?.size)
            fs.failOnEvent = null
            assertNull(assertOk(store(root, fs).recover(liveness)))
            assertEquals(LeaseState.CLOSED, readPointer(File(fixture.leaseDirectory, "current"))?.state)
            assertFalse(File(fixture.leaseDirectory, "next").exists())
            assertFalse(File(fixture.leaseDirectory, "next.tmp").exists())
            assertFalse(File(root, "sessions/active").exists())
        }
    }

    @Test
    fun rejectsRecoveryCloseResumeFromFallbackIntentOrLocalTemporaryEvidenceReadOnly() {
        val cases: List<(Fixture, LeaseHead) -> Unit> = listOf(
            { fixture, _ -> setTuple(fixture, current = null, previous = 0, next = null) },
            { fixture, _ ->
                setTuple(fixture, current = null, previous = 0, next = null)
                File(fixture.leaseDirectory, "repair.intent").writeBytes(
                    repairIntentBytes(fixture.heads[0], null),
                )
            },
            { fixture, child -> writePointer(File(fixture.leaseDirectory, "current.tmp"), child) },
            { fixture, _ -> writePointer(File(fixture.leaseDirectory, "previous.tmp"), fixture.heads[0]) },
        )
        cases.forEachIndexed { index, corruptEvidence ->
            val root = root("recovery-close-parent-reject-$index")
            val (fixture, child) = createTrailingRecoveryClose(root, 274 + index, ChainKind.PENDING)
            corruptEvidence(fixture, child)
            val before = recoveryEvidenceSnapshot(fixture)
            val fs = fastFaultingFs()

            assertError(
                SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
                store(root, fs).recover(FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)),
            )
            assertEquals(before, recoveryEvidenceSnapshot(fixture))
            assertTrue("Ambiguous recovery mutated evidence: ${fs.events}", fs.events.none(::isMutationEvent))
        }
    }

    @Test
    fun resumableNextInstallationStopsAtEveryDurabilityFailureBeforeDependentRepair() {
        val failures = listOf(
            "write-new:next.tmp" to 1,
            "sync-file:next.tmp" to 1,
            "rename:next.tmp->next" to 1,
            "sync-dir:lease" to 1,
        )
        listOf(ChainKind.PENDING, ChainKind.OWNED).forEachIndexed { kindIndex, kind ->
            failures.forEachIndexed { failureIndex, (event, occurrence) ->
                val root = root("recovery-close-strict-$kindIndex-$failureIndex")
                val (fixture, _) = createTrailingRecoveryClose(root, 290 + kindIndex * 10 + failureIndex, kind)
                val fs = fastFaultingFs().apply {
                    failOnEvent = event
                    failOnOccurrence = occurrence
                }
                val liveness = if (kind == ChainKind.PENDING) {
                    FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)
                } else {
                    FakeLiveness(OwnerMode.DEAD, PresenceMode.UNKNOWN)
                }

                assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store(root, fs).recover(liveness))
                assertEquals("Recovery continued after $event: ${fs.events}", event, fs.events.last())
                assertTrue(
                    "Dependent recovery mutation followed $event: ${fs.events}",
                    fs.events.none { it in DEPENDENT_NEXT_REPAIR_EVENTS },
                )
                fs.failOnEvent = null
                fs.events.clear()
                assertNull(assertOk(store(root, fs).recover(liveness)))
                if (event == "sync-dir:lease") assertNextBarrierPrecedesDependentRepair(fs.events)
                assertFalse(File(fixture.leaseDirectory, "next").exists())
                assertFalse(File(fixture.leaseDirectory, "next.tmp").exists())
            }
        }
    }

    @Test
    fun visibleAdvancingNextIsRebarrieredBeforeDependentRepairAndRetryFailureStops() {
        val cases = listOf(
            AcceptedCase(ChainKind.OWNED, current = 0, previous = 0, next = 1),
            AcceptedCase(ChainKind.PENDING, current = null, previous = null, next = 0),
        )
        val failures = listOf(
            "sync-file:next" to 1,
            "sync-dir:lease" to 1,
        )
        cases.forEachIndexed { caseIndex, case ->
            failures.forEachIndexed { failureIndex, (event, occurrence) ->
                val root = root("visible-next-rebarrier-$caseIndex-$failureIndex")
                val fixture = writeFixture(root, 320 + caseIndex * 10 + failureIndex, case.kind)
                setTuple(fixture, case.current, case.previous, case.next)
                val fs = fastFaultingFs().apply {
                    failOnEvent = event
                    failOnOccurrence = occurrence
                }
                val liveness = FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)

                assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store(root, fs).recover(liveness))
                assertEquals("Recovery continued after next re-barrier failure: ${fs.events}", event, fs.events.last())
                assertTrue(
                    "Dependent pointer mutation preceded next re-barrier: ${fs.events}",
                    fs.events.none { it in DEPENDENT_LOCAL_POINTER_EVENTS },
                )

                fs.failOnEvent = null
                fs.events.clear()
                assertEquals(fixture.heads.last(), assertOk(store(root, fs).recover(liveness)))
                assertNextBarrierPrecedesDependentRepair(fs.events)
                assertCanonicalPointers(fixture)
            }
        }
    }

    @Test
    fun rejectsEveryOtherUnpointedTrailingCloseState() {
        listOf("LAUNCH_FAILED", "RECOVERY_LAUNCHER_DEAD").forEachIndexed { index, reason ->
            val root = root("unpointed-close-reject-$index")
            val fixture = writeFixture(root, 268 + index, ChainKind.PENDING, activeSequence = 0)
            writeStateDirectory(
                fixture,
                fixture.documents[0].copy(
                    transitionSequence = 1,
                    transitionId = uuid("unqualified-close-$index"),
                    parentTransitionId = fixture.documents[0].transitionId,
                    state = LeaseState.CLOSED,
                    closeReason = reason,
                ),
            )

            assertError(
                SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
                store(root).recover(FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)),
            )
            assertEquals(fixture.heads[0], readPointer(File(fixture.leaseDirectory, "current")))
            assertEquals(fixture.heads[0], readPointer(File(root, "sessions/active")))
        }
    }

    @Test
    fun recognizesOnlyDurableExactPreviousFallbackRepairIntentAfterCurrentPublication() {
        val root = root("fallback-repair-intent")
        val fixture = writeFixture(root, 270, ChainKind.OWNED)
        setTuple(fixture, current = 1, previous = 1, next = null)
        File(fixture.leaseDirectory, "repair.intent").writeBytes(
            repairIntentBytes(fixture.heads[1], fixture.heads[0]),
        )

        assertEquals(
            fixture.heads.last(),
            assertOk(store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT))),
        )
        assertCanonicalPointers(fixture)
        assertFalse(File(fixture.leaseDirectory, "repair.intent").exists())
    }

    @Test
    fun rejectsMismatchedOrNonDurableRepairIntentAndPointerTemps() {
        val wrongIntentRoot = root("wrong-repair-intent")
        val wrongIntent = writeFixture(wrongIntentRoot, 271, ChainKind.OWNED)
        setTuple(wrongIntent, current = 1, previous = 1, next = null)
        File(wrongIntent.leaseDirectory, "repair.intent").writeBytes(
            repairIntentBytes(wrongIntent.heads[1], null),
        )
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(wrongIntentRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )

        val temporaryRoot = root("temporary-repair-intent")
        val temporary = writeFixture(temporaryRoot, 272, ChainKind.OWNED)
        setTuple(temporary, current = 1, previous = 1, next = null)
        File(temporary.leaseDirectory, "repair.intent.tmp").writeBytes(
            repairIntentBytes(temporary.heads[1], temporary.heads[0]),
        )
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(temporaryRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )

        val pointerRoot = root("wrong-pointer-temp")
        val pointer = writeFixture(pointerRoot, 273, ChainKind.OWNED, activeSequence = 1)
        writePointer(File(pointer.leaseDirectory, "current.tmp"), pointer.heads[0])
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(pointerRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
    }

    @Test
    fun retriesEveryPreviousFallbackRepairIntentAndPointerBarrier() {
        val failures = listOf(
            "write-new:repair.intent.tmp" to 1,
            "sync-file:repair.intent.tmp" to 1,
            "rename:repair.intent.tmp->repair.intent" to 1,
            "sync-dir:lease" to 1,
            "write-new:current.tmp" to 1,
            "sync-file:current.tmp" to 1,
            "rename:current.tmp->current" to 1,
            "sync-dir:lease" to 2,
            "write-new:previous.tmp" to 1,
            "sync-file:previous.tmp" to 1,
            "rename:previous.tmp->previous" to 1,
            "sync-dir:lease" to 3,
            "delete-contained:repair.intent" to 1,
            "sync-dir:lease" to 5,
        )
        failures.forEachIndexed { index, (event, occurrence) ->
            val root = root("fallback-intent-barrier-$index")
            val fixture = writeFixture(root, 280 + index, ChainKind.OWNED)
            setTuple(fixture, current = null, previous = 1, next = null)
            val fs = fastFaultingFs().apply {
                failOnEvent = event
                failOnOccurrence = occurrence
            }

            assertError(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                store(root, fs).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
            )
            assertEquals("Fallback repair continued after $event: ${fs.events}", event, fs.events.last())
            if (event.contains("current") || event.contains("previous") || (event == "sync-dir:lease" && occurrence in 2..3)) {
                assertTrue(File(fixture.leaseDirectory, "repair.intent").exists())
            }
            fs.failOnEvent = null
            assertEquals(
                fixture.heads.last(),
                assertOk(store(root, fs).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT))),
            )
            assertCanonicalPointers(fixture)
            assertFalse(File(fixture.leaseDirectory, "repair.intent").exists())
            assertFalse(File(fixture.leaseDirectory, "repair.intent.tmp").exists())
        }
    }

    @Test
    fun consumesExactMatchingLocalAndActivePointerTemps() {
        val root = root("matching-pointer-temps")
        val fixture = writeFixture(root, 300, ChainKind.OWNED, activeSequence = 1)
        listOf("current" to 1, "previous" to 0).forEach { (name, sequence) ->
            writePointer(File(fixture.leaseDirectory, "$name.tmp"), fixture.heads[sequence])
        }
        writePointer(File(root, "sessions/active.tmp"), fixture.heads[1])

        assertEquals(
            fixture.heads.last(),
            assertOk(store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT))),
        )
        listOf("current.tmp", "previous.tmp").forEach { name ->
            assertFalse(File(fixture.leaseDirectory, name).exists())
        }
        assertFalse(File(root, "sessions/active.tmp").exists())
    }

    @Test
    fun pendingDeadClosureConsumesMatchingActiveTempAndRemainsClear() {
        val root = root("pending-dead-active-temp")
        val fixture = writeFixture(root, 301, ChainKind.PENDING, activeSequence = 0)
        writePointer(File(root, "sessions/active.tmp"), fixture.heads[0])
        val liveness = FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)

        assertNull(assertOk(store(root).recover(liveness)))
        assertFalse(File(root, "sessions/active").exists())
        assertFalse(File(root, "sessions/active.tmp").exists())
        assertNull(assertOk(store(root).recover(liveness)))
    }

    @Test
    fun virginRootReturnsClearWithoutSyncingMissingSessionsDirectory() {
        val root = root("virgin-no-sessions")
        val fs = FaultingSkinFileSystem(RejectMissingDirectorySyncFileSystem()).apply { skipPhysicalSyncs = false }

        assertEquals(
            LeaseMutationGate.CLEAR,
            store(root, fs).mutationGate(FakeLiveness(OwnerMode.UNKNOWN, PresenceMode.UNKNOWN)),
        )
        assertFalse(File(root, "sessions").exists())
    }

    @Test
    fun rejectsDescriptorAndStateCrossLinkOrTamperEvidence() {
        val tamperers: List<(Fixture) -> Unit> = listOf(
            { fixture -> File(fixture.descriptorDirectory, "descriptor.sha256").writeText("${hex('f')}\n") },
            { fixture ->
                val file = File(fixture.descriptorDirectory, "descriptor.json")
                file.writeBytes(file.readBytes() + byteArrayOf(' '.code.toByte()))
            },
            { fixture ->
                val file = stateFile(fixture, 0, "lease.json")
                file.writeBytes(file.readBytes() + byteArrayOf(' '.code.toByte()))
            },
            { fixture ->
                rewriteState(fixture, 0, fixture.documents[0].copy(leaseTokenSha256 = hex('e')))
            },
            { fixture ->
                rewriteState(fixture, 0, fixture.documents[0].copy(descriptorSha256 = hex('e')))
            },
            { fixture ->
                rewriteState(fixture, 0, fixture.documents[0].copy(registrySha256 = hex('d')))
            },
        )

        tamperers.forEachIndexed { index, tamper ->
            val root = root("tamper-$index")
            val fixture = writeFixture(root, index + 300, ChainKind.PENDING)
            tamper(fixture)
            val before = pointerSnapshot(fixture)

            assertError(
                SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
                store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
            )
            assertEquals(before, pointerSnapshot(fixture))
        }
    }

    @Test
    fun rejectsConflictingSequenceIdentityAndNonAdjacentParentEvidence() {
        val duplicateRoot = root("duplicate-sequence")
        val duplicate = writeFixture(duplicateRoot, 350, ChainKind.PENDING)
        writeStateDirectory(
            duplicate,
            duplicate.documents[0].copy(
                leaseId = uuid("duplicate-lease"),
                transitionId = uuid("duplicate-transition"),
            ),
        )
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(duplicateRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )

        val parentRoot = root("wrong-parent")
        val parent = writeFixture(parentRoot, 351, ChainKind.OWNED)
        rewriteState(parent, 1, parent.documents[1].copy(parentTransitionId = uuid("wrong-parent")))
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(parentRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
    }

    @Test
    fun validatesAllCandidatesBeforeRepairingAnyPointer() {
        val root = root("two-phase")
        val repairable = writeFixture(root, 401, ChainKind.OWNED)
        setTuple(repairable, current = 0, previous = null, next = 1)
        val malformed = writeFixture(root, 402, ChainKind.PENDING)
        File(malformed.descriptorDirectory, "descriptor.sha256").writeText("${hex('f')}\n")
        val before = pointerSnapshot(repairable)

        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
        assertEquals(before, pointerSnapshot(repairable))
    }

    @Test
    fun rejectsSixtyFiveCompleteDescriptorDirectories() {
        val root = root("descriptor-bound")
        val sessions = File(root, "sessions").apply { mkdirs() }
        repeat(65) { index ->
            File(sessions, uuid("bound-descriptor-$index").toString()).apply { mkdirs() }
                .resolve(".complete").writeBytes(ByteArray(0))
        }

        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
    }

    @Test
    fun rejectsFourCompleteStatesWithoutSampling() {
        val root = root("state-bound")
        val fixture = writeFixture(root, 500, ChainKind.CLOSED_OWNED)
        File(fixture.leaseDirectory, "states/ls-00000000000000000003-${fixture.descriptor.leaseId}").apply { mkdirs() }
            .resolve(".complete").writeBytes(ByteArray(0))
        val before = pointerSnapshot(fixture)

        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(root).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
        assertEquals(before, pointerSnapshot(fixture))
    }

    @Test
    fun rejectsMultipleNonClosedHeadsAndCrossLeaseActiveEvidenceReadOnly() {
        val multipleRoot = root("multiple-active")
        val first = writeFixture(multipleRoot, 601, ChainKind.PENDING)
        writeFixture(multipleRoot, 602, ChainKind.OWNED)
        val firstBefore = pointerSnapshot(first)
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(multipleRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
        assertEquals(firstBefore, pointerSnapshot(first))
        assertFalse(File(multipleRoot, "sessions/active").exists())

        val crossRoot = root("cross-active")
        val fixture = writeFixture(crossRoot, 603, ChainKind.PENDING)
        val foreign = LeaseHead(uuid("foreign-descriptor"), uuid("foreign-lease"), 0, LeaseState.LAUNCH_PENDING, hex('a'))
        writePointer(File(crossRoot, "sessions/active"), foreign)
        val activeBefore = File(crossRoot, "sessions/active").readBytes().toList()
        assertError(
            SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
            store(crossRoot).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
        assertEquals(activeBefore, File(crossRoot, "sessions/active").readBytes().toList())
        assertCanonicalPointers(fixture)
    }

    @Test
    fun reconcilesClaimCrashAfterLocalHeadBeforeActiveBarrier() {
        val root = root("claim-crash")
        val fixture = writeFixture(root, 700, ChainKind.PENDING, activeSequence = 0)
        val fs = fastFaultingFs()
        val sessionStore = store(root, fs)
        fs.failOnEvent = "sync-file:active.tmp"
        fs.failOnOccurrence = 1

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, sessionStore.claim(fixture.handle, gameOwner))
        fs.failOnEvent = null
        val recovered = assertOk(store(root, fs).recover(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)))

        assertEquals(LeaseState.GAME_OWNED, recovered?.state)
        assertEquals(recovered, readPointer(File(root, "sessions/active")))
        assertFalse(File(root, "sessions/active.tmp").exists())
    }

    @Test
    fun reconcilesCloseCrashAfterLocalHeadBeforeActiveRemovalBarrier() {
        val root = root("close-crash")
        val fixture = writeFixture(root, 701, ChainKind.OWNED, activeSequence = 1)
        val fs = fastFaultingFs()
        val sessionStore = store(root, fs)
        fs.failOnEvent = "delete-contained:active"
        fs.failOnOccurrence = 1

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, sessionStore.close(fixture.handle, "GAME_EXIT"))
        fs.failOnEvent = null
        assertNull(assertOk(store(root, fs).recover(FakeLiveness(OwnerMode.UNKNOWN, PresenceMode.UNKNOWN))))

        assertFalse(File(root, "sessions/active").exists())
        assertEquals(LeaseState.CLOSED, readPointer(File(fixture.leaseDirectory, "current"))?.state)
    }

    @Test
    fun pendingLivenessTruthTableMapsExactlyToClearActiveOrUnknown() {
        val outcomes = listOf(
            Triple(OwnerMode.DEAD, PresenceMode.ABSENT, LeaseMutationGate.CLEAR),
            Triple(OwnerMode.DEAD, PresenceMode.PRESENT, LeaseMutationGate.ACTIVE),
            Triple(OwnerMode.DEAD, PresenceMode.UNKNOWN, LeaseMutationGate.UNKNOWN),
            Triple(OwnerMode.ALIVE, PresenceMode.ABSENT, LeaseMutationGate.ACTIVE),
            Triple(OwnerMode.ALIVE, PresenceMode.PRESENT, LeaseMutationGate.ACTIVE),
            Triple(OwnerMode.ALIVE, PresenceMode.UNKNOWN, LeaseMutationGate.ACTIVE),
            Triple(OwnerMode.UNKNOWN, PresenceMode.ABSENT, LeaseMutationGate.UNKNOWN),
            Triple(OwnerMode.UNKNOWN, PresenceMode.PRESENT, LeaseMutationGate.ACTIVE),
            Triple(OwnerMode.UNKNOWN, PresenceMode.UNKNOWN, LeaseMutationGate.UNKNOWN),
        )

        outcomes.forEachIndexed { index, (owner, presence, expected) ->
            val root = root("pending-live-$index")
            val fixture = writeFixture(root, index + 800, ChainKind.PENDING, activeSequence = 0)
            val authority = FakeLiveness(owner, presence)

            assertEquals(expected, store(root).mutationGate(authority))
            assertEquals(listOf(TARGET_PACKAGE to TARGET_PROCESS), authority.exactQueries)
            val current = requireNotNull(readPointer(File(fixture.leaseDirectory, "current")))
            assertEquals(if (expected == LeaseMutationGate.CLEAR) LeaseState.CLOSED else LeaseState.LAUNCH_PENDING, current.state)
            if (expected == LeaseMutationGate.CLEAR) assertEquals("RECOVERY_LAUNCHER_DEAD", readState(fixture, 1).closeReason)
        }
    }

    @Test
    fun ownedLivenessOutcomesMapExactlyAndNeverQueryPackagePresence() {
        val outcomes = listOf(
            OwnerMode.ALIVE to LeaseMutationGate.ACTIVE,
            OwnerMode.DEAD to LeaseMutationGate.CLEAR,
            OwnerMode.UNKNOWN to LeaseMutationGate.UNKNOWN,
        )
        outcomes.forEachIndexed { index, (owner, expected) ->
            val root = root("owned-live-$index")
            val fixture = writeFixture(root, index + 900, ChainKind.OWNED, activeSequence = 1)
            val authority = FakeLiveness(owner, PresenceMode.UNKNOWN)

            assertEquals(expected, store(root).mutationGate(authority))
            assertTrue(authority.exactQueries.isEmpty())
            val current = requireNotNull(readPointer(File(fixture.leaseDirectory, "current")))
            assertEquals(if (expected == LeaseMutationGate.CLEAR) LeaseState.CLOSED else LeaseState.GAME_OWNED, current.state)
            if (expected == LeaseMutationGate.CLEAR) assertEquals("RECOVERY_GAME_OWNER_DEAD", readState(fixture, 2).closeReason)
        }
    }

    @Test
    fun mutationGateIsClearOnlyForNoOpenLeaseAndUnknownForAmbiguityOrDurabilityFailure() {
        val emptyRoot = root("empty")
        assertEquals(LeaseMutationGate.CLEAR, store(emptyRoot).mutationGate(FakeLiveness(OwnerMode.UNKNOWN, PresenceMode.UNKNOWN)))
        assertFalse(File(emptyRoot, "registry").exists())

        val malformedRoot = root("gate-malformed")
        File(malformedRoot, "sessions/${uuid("malformed")}").apply { mkdirs() }.resolve(".complete").writeBytes(ByteArray(0))
        assertEquals(LeaseMutationGate.UNKNOWN, store(malformedRoot).mutationGate(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)))
        assertFalse(File(malformedRoot, "registry").exists())

        val failedRoot = root("gate-durability")
        val fixture = writeFixture(failedRoot, 1000, ChainKind.OWNED)
        setTuple(fixture, current = 0, previous = null, next = 1)
        val fs = fastFaultingFs().apply {
            failOnEvent = "sync-file:previous.tmp"
            failOnOccurrence = 1
        }
        assertEquals(LeaseMutationGate.UNKNOWN, store(failedRoot, fs).mutationGate(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)))
        assertFalse(File(failedRoot, "registry").exists())
    }

    @Test
    fun fileModificationTimesNeverAuthorizeRecoveryOrClosure() {
        val root = root("mtime")
        val fixture = writeFixture(root, 1100, ChainKind.PENDING, activeSequence = 0)
        fixture.descriptorDirectory.walkTopDown().forEachIndexed { index, file ->
            file.setLastModified(if (index % 2 == 0) 1L else Long.MAX_VALUE / 2)
        }

        assertEquals(
            LeaseMutationGate.ACTIVE,
            store(root).mutationGate(FakeLiveness(OwnerMode.ALIVE, PresenceMode.ABSENT)),
        )
        assertEquals(1, fixture.leaseDirectory.resolve("states").listFiles()?.size)
    }

    @Test
    fun targetPackageAndProcessPairIsStrictAndNotHardcoded() {
        assertThrows(IllegalArgumentException::class.java) { SkinTargetProcess("bad", "bad") }
        assertThrows(IllegalArgumentException::class.java) {
            SkinTargetProcess("com.example.hollowknight", "com.other.game")
        }
        assertEquals(
            TARGET_PACKAGE,
            SkinTargetProcess(TARGET_PACKAGE, TARGET_PROCESS).packageName,
        )

        val root = root("unconfigured-target")
        writeFixture(root, 1200, ChainKind.PENDING, activeSequence = 0)
        val authority = FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)
        val unconfigured = SkinSessionStore(
            root,
            fastFaultingFs(),
            SkinLockManager(root),
            null,
            PermissiveTestSkinQuota(root),
        )
        assertEquals(LeaseMutationGate.UNKNOWN, unconfigured.mutationGate(authority))
        assertTrue(authority.exactQueries.isEmpty())
    }

    private fun assertNextBarrierPrecedesDependentRepair(events: List<String>) {
        val nextFile = events.indexOf("sync-file:next")
        val nextDirectory = events.withIndex().firstOrNull { (index, event) ->
            index > nextFile && event == "sync-dir:lease"
        }?.index ?: -1
        val dependent = events.withIndex().firstOrNull { (_, event) -> event in DEPENDENT_LOCAL_POINTER_EVENTS }?.index ?: -1
        assertTrue("Missing next re-barrier or dependent repair: $events", nextFile >= 0 && nextDirectory >= 0 && dependent >= 0)
        assertTrue("Dependent repair preceded next re-barrier: $events", nextFile < nextDirectory && nextDirectory < dependent)
    }

    private fun createTrailingRecoveryClose(
        root: File,
        index: Int,
        kind: ChainKind,
    ): Pair<Fixture, LeaseHead> {
        val fixture = writeFixture(root, index, kind, activeSequence = kind.finalSequence)
        val fs = fastFaultingFs().apply {
            failOnEvent = "write-new:next.tmp"
            failOnOccurrence = 1
        }
        val liveness = if (kind == ChainKind.PENDING) {
            FakeLiveness(OwnerMode.DEAD, PresenceMode.ABSENT)
        } else {
            FakeLiveness(OwnerMode.DEAD, PresenceMode.UNKNOWN)
        }
        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, store(root, fs).recover(liveness))
        val child = SkinLeaseStateCodec.head(readState(fixture, kind.finalSequence + 1))
        return fixture to child
    }

    private fun recoveryEvidenceSnapshot(fixture: Fixture): Map<String, List<Byte>?> =
        listOf(
            "current",
            "previous",
            "next",
            "current.tmp",
            "previous.tmp",
            "next.tmp",
            "repair.intent",
            "repair.intent.tmp",
        ).associateWith { name ->
            File(fixture.leaseDirectory, name).takeIf(File::exists)?.readBytes()?.toList()
        }

    private fun isMutationEvent(event: String): Boolean =
        event.startsWith("write-") || event.startsWith("rename:") || event.startsWith("delete-contained:") ||
            event.startsWith("sync-") || event.startsWith("mkdir:")

    private fun root(label: String): File = File(testRoot, "$label/profiles/hollow-knight/skins").apply { mkdirs() }

    private fun store(root: File, fs: FaultingSkinFileSystem = fastFaultingFs()): SkinSessionStore =
        SkinSessionStore(
            root,
            fs,
            SkinLockManager(root),
            SkinTargetProcess(TARGET_PACKAGE, TARGET_PROCESS),
            PermissiveTestSkinQuota(root),
        )

    private fun fastFaultingFs() = FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }

    private fun writeFixture(
        root: File,
        index: Int,
        kind: ChainKind,
        activeSequence: Int? = null,
    ): Fixture {
        val descriptorId = uuid("descriptor-$index")
        val leaseId = uuid("lease-$index")
        val registryId = uuid("registry-$index").toString()
        val tokenSha256 = SkinLeaseStateCodec.rawTokenSha256(RAW_TOKEN)
        val descriptor = SkinLaunchDescriptor(
            schemaVersion = 1,
            descriptorId = descriptorId,
            sessionSequence = index.toLong(),
            profileId = "hollow-knight",
            gameVersion = "1.5.12620",
            catalogId = HollowKnightCatalogPaths.CATALOG_ID,
            catalogSha256 = catalog.sha256,
            registryGenerationId = registryId,
            registryGenerationSha256 = hex('c'),
            activation = SkinActivation(SkinMode.OFF, null, ActiveVisual.Vanilla, 0, RotationInterlock.clear()),
            packs = emptyList(),
            leaseId = leaseId,
            leaseTokenSha256 = tokenSha256,
        )
        val descriptorBytes = SkinLaunchDescriptorCodec.canonical(descriptor)
        val descriptorSha256 = SkinIdentity.sha256(descriptorBytes)
        val pending = LeaseStateDocument(
            schemaVersion = 1,
            leaseId = leaseId,
            leaseTokenSha256 = tokenSha256,
            profileId = "hollow-knight",
            sessionSequence = index.toLong(),
            transitionSequence = 0,
            transitionId = uuid("pending-$index"),
            parentTransitionId = null,
            state = LeaseState.LAUNCH_PENDING,
            descriptorId = descriptorId,
            descriptorSha256 = descriptorSha256,
            registryGenerationId = registryId,
            registrySha256 = hex('c'),
            launcherOwner = launcherOwner,
            gameOwner = null,
            closeReason = null,
        )
        val owned = pending.copy(
            transitionSequence = 1,
            transitionId = uuid("owned-$index"),
            parentTransitionId = pending.transitionId,
            state = LeaseState.GAME_OWNED,
            gameOwner = gameOwner,
        )
        val documents = when (kind) {
            ChainKind.PENDING -> listOf(pending)
            ChainKind.OWNED -> listOf(pending, owned)
            ChainKind.CLOSED_PENDING -> listOf(
                pending,
                pending.copy(
                    transitionSequence = 1,
                    transitionId = uuid("closed-pending-$index"),
                    parentTransitionId = pending.transitionId,
                    state = LeaseState.CLOSED,
                    closeReason = "LAUNCH_FAILED",
                ),
            )
            ChainKind.CLOSED_OWNED -> listOf(
                pending,
                owned,
                owned.copy(
                    transitionSequence = 2,
                    transitionId = uuid("closed-owned-$index"),
                    parentTransitionId = owned.transitionId,
                    state = LeaseState.CLOSED,
                    closeReason = "GAME_EXIT",
                ),
            )
        }
        val descriptorDirectory = File(root, "sessions/$descriptorId").apply { mkdirs() }
        writeSequenceAtLeast(root, index.toLong())
        File(descriptorDirectory, "descriptor.json").writeBytes(descriptorBytes)
        File(descriptorDirectory, "descriptor.sha256").writeText("$descriptorSha256\n", StandardCharsets.US_ASCII)
        File(descriptorDirectory, ".complete").writeBytes(ByteArray(0))
        val leaseDirectory = File(descriptorDirectory, "lease")
        val states = File(leaseDirectory, "states").apply { mkdirs() }
        documents.forEach { document ->
            val bytes = SkinLeaseStateCodec.canonical(document)
            val sha256 = SkinIdentity.sha256(bytes)
            val directory = File(states, stateDirectoryName(document.transitionSequence, leaseId)).apply { mkdirs() }
            File(directory, "lease.json").writeBytes(bytes)
            File(directory, "lease.sha256").writeText("$sha256\n", StandardCharsets.US_ASCII)
            File(directory, ".complete").writeBytes(ByteArray(0))
        }
        val fixture = Fixture(
            root,
            descriptor,
            descriptorDirectory,
            leaseDirectory,
            documents,
            documents.map(SkinLeaseStateCodec::head),
            SkinLaunchHandle(
                descriptorId,
                descriptorSha256,
                "sessions/$descriptorId/descriptor.json",
                leaseId,
                RAW_TOKEN,
                index.toLong(),
            ),
        )
        setTuple(fixture, documents.lastIndex, documents.lastIndex.takeIf { it > 0 }?.minus(1), null)
        activeSequence?.let { writePointer(File(root, "sessions/active"), fixture.heads[it]) }
        return fixture
    }

    private fun writeSequenceAtLeast(root: File, value: Long) {
        val sequence = File(root, "sessions/sequence")
        val current = sequence.takeIf(File::exists)
            ?.readText(StandardCharsets.US_ASCII)
            ?.removeSuffix("\n")
            ?.toLong()
        if (current == null || current < value) {
            sequence.writeText("$value\n", StandardCharsets.US_ASCII)
        }
    }

    private fun setTuple(fixture: Fixture, current: Int?, previous: Int?, next: Int?) {
        mapOf("current" to current, "previous" to previous, "next" to next).forEach { (name, sequence) ->
            val file = File(fixture.leaseDirectory, name)
            file.delete()
            File(fixture.leaseDirectory, "$name.tmp").delete()
            sequence?.let { writePointer(file, fixture.heads[it]) }
        }
    }

    private fun assertCanonicalPointers(fixture: Fixture) {
        assertEquals(fixture.heads.last(), readPointer(File(fixture.leaseDirectory, "current")))
        assertEquals(fixture.heads.dropLast(1).lastOrNull(), readPointer(File(fixture.leaseDirectory, "previous")))
        assertFalse(File(fixture.leaseDirectory, "next").exists())
    }

    private fun pointerSnapshot(fixture: Fixture): Map<String, List<Byte>?> = listOf("current", "previous", "next").associateWith { name ->
        File(fixture.leaseDirectory, name).takeIf(File::exists)?.readBytes()?.toList()
    }

    private fun writeStateDirectory(fixture: Fixture, document: LeaseStateDocument) {
        val bytes = SkinLeaseStateCodec.canonical(document)
        val sha256 = SkinIdentity.sha256(bytes)
        val directory = File(
            fixture.leaseDirectory,
            "states/${stateDirectoryName(document.transitionSequence, document.leaseId)}",
        ).apply { mkdirs() }
        File(directory, "lease.json").writeBytes(bytes)
        File(directory, "lease.sha256").writeText("$sha256\n", StandardCharsets.US_ASCII)
        File(directory, ".complete").writeBytes(ByteArray(0))
    }

    private fun rewriteState(fixture: Fixture, sequence: Int, document: LeaseStateDocument) {
        val bytes = SkinLeaseStateCodec.canonical(document)
        stateFile(fixture, sequence, "lease.json").writeBytes(bytes)
        stateFile(fixture, sequence, "lease.sha256").writeText("${SkinIdentity.sha256(bytes)}\n", StandardCharsets.US_ASCII)
        writePointer(File(fixture.leaseDirectory, "current"), SkinLeaseStateCodec.head(document))
    }

    private fun readState(fixture: Fixture, sequence: Int): LeaseStateDocument {
        val parsed = SkinLeaseStateCodec.parse(stateFile(fixture, sequence, "lease.json").readBytes())
        return assertOk(parsed)
    }

    private fun stateFile(fixture: Fixture, sequence: Int, name: String): File = File(
        fixture.leaseDirectory,
        "states/${stateDirectoryName(sequence.toLong(), fixture.descriptor.leaseId)}/$name",
    )

    private fun stateDirectoryName(sequence: Long, leaseId: UUID): String =
        "ls-${sequence.toString().padStart(20, '0')}-$leaseId"

    private fun repairIntentBytes(head: LeaseHead, previous: LeaseHead?): ByteArray = buildString {
        append("PREVIOUS_ONLY_V1\n")
        append(LeasePointerCodec.canonical(head).toString(StandardCharsets.US_ASCII))
        if (previous == null) {
            append("ABSENT\n")
        } else {
            append("PRESENT\n")
            append(LeasePointerCodec.canonical(previous).toString(StandardCharsets.US_ASCII))
        }
    }.toByteArray(StandardCharsets.US_ASCII)

    private fun writePointer(file: File, head: LeaseHead) {
        requireNotNull(file.parentFile).mkdirs()
        file.writeBytes(LeasePointerCodec.canonical(head))
    }

    private fun readPointer(file: File): LeaseHead? = if (file.exists()) LeasePointerCodec.parse(file.readBytes()) else null

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

    private data class Fixture(
        val root: File,
        val descriptor: SkinLaunchDescriptor,
        val descriptorDirectory: File,
        val leaseDirectory: File,
        val documents: List<LeaseStateDocument>,
        val heads: List<LeaseHead>,
        val handle: SkinLaunchHandle,
    )

    private data class AcceptedCase(
        val kind: ChainKind,
        val current: Int?,
        val previous: Int?,
        val next: Int?,
    )

    private enum class ChainKind(val closed: Boolean, val finalSequence: Int) {
        PENDING(false, 0),
        OWNED(false, 1),
        CLOSED_PENDING(true, 1),
        CLOSED_OWNED(true, 2),
    }

    private enum class OwnerMode { ALIVE, DEAD, UNKNOWN }
    private enum class PresenceMode { PRESENT, ABSENT, UNKNOWN }

    private class FakeLiveness(
        private val ownerMode: OwnerMode,
        private val presenceMode: PresenceMode,
    ) : ProcessIdentityAuthority {
        val exactQueries = mutableListOf<Pair<String, String>>()

        override fun self(): SelfIdentityResult = SelfIdentityResult.Unknown

        override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = when (ownerMode) {
            OwnerMode.ALIVE -> ExpectedOwnerLiveness.Alive(expected)
            OwnerMode.DEAD -> ExpectedOwnerLiveness.DefinitivelyDead
            OwnerMode.UNKNOWN -> ExpectedOwnerLiveness.Unknown
        }

        override fun exactProcess(packageName: String, processName: String): ExactProcessPresence {
            exactQueries += packageName to processName
            return when (presenceMode) {
                PresenceMode.PRESENT -> ExactProcessPresence.Present(gameOwner)
                PresenceMode.ABSENT -> ExactProcessPresence.Absent
                PresenceMode.UNKNOWN -> ExactProcessPresence.Unknown
            }
        }
    }

    private class RejectMissingDirectorySyncFileSystem(
        private val delegate: FastSkinFileSystem = FastSkinFileSystem(),
    ) : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
        override fun syncDirectory(path: File) {
            require(delegate.exists(path)) { "Cannot sync a missing directory" }
            delegate.syncDirectory(path)
        }
    }

    private companion object {
        const val RAW_TOKEN = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        const val TARGET_PACKAGE = "com.example.hollowknight"
        const val TARGET_PROCESS = "com.example.hollowknight"
        val DEPENDENT_LOCAL_POINTER_EVENTS = setOf(
            "sync-file:previous",
            "write-new:previous.tmp",
            "rename:previous.tmp->previous",
            "sync-file:current",
            "write-new:current.tmp",
            "rename:current.tmp->current",
        )
        val DEPENDENT_NEXT_REPAIR_EVENTS = setOf(
            "write-new:previous.tmp",
            "rename:previous.tmp->previous",
            "write-new:current.tmp",
            "rename:current.tmp->current",
            "delete-contained:next",
            "delete-contained:active",
        )
        val launcherOwner = ProcessIdentity(1000, 1001, "99")
        val gameOwner = ProcessIdentity(1000, 1002, "100")
    }
}
