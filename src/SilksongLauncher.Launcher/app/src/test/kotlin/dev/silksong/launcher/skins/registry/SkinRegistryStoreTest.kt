package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import java.io.File
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinRegistryStoreTest {
    private lateinit var profileRoot: File
    private lateinit var skinsRoot: File
    private lateinit var fs: FastSkinFileSystem

    @Before
    fun setUp() {
        profileRoot = File("build/test-skin-registry-store").absoluteFile
        profileRoot.deleteRecursively()
        skinsRoot = File(profileRoot, "profiles/hollow-knight/skins")
        skinsRoot.mkdirs()
        fs = FastSkinFileSystem()
    }

    @After
    fun tearDown() {
        profileRoot.deleteRecursively()
    }

    @Test
    fun registryGenerationQuotaRejectsBeforeStagingOrPublication() {
        val quota = object : SkinQuotaAdmission {
            override val root = skinsRoot
            override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> =
                SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "full")
        }

        val result = SkinRegistryStore(skinsRoot, quota, fs, SkinLockManager(skinsRoot)).recover()

        assertError(SkinImportCode.PROFILE_QUOTA_EXCEEDED, result)
        assertTrue(skinsRoot.listFiles().orEmpty().isEmpty())
    }

    @Test
    fun publishesDeterministicGenesisOnce() {
        val first = assertOk(SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover())
        val generationRoot = File(skinsRoot, "registry/generations")
        val firstBytes = File(generationRoot.listFiles()!!.single(), "registry.json").readBytes()

        val second = assertOk(SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover())
        val secondBytes = File(generationRoot.listFiles()!!.single(), "registry.json").readBytes()

        assertEquals(0, first.sequence)
        assertEquals(first, second)
        assertArrayEquals(firstBytes, secondBytes)
        assertEquals(1, generationRoot.listFiles()!!.size)
        assertEquals(SkinMode.OFF, first.document.activation.mode)
        assertEquals(null, first.document.activation.selectedPackId)
        assertEquals(ActiveVisual.Vanilla, first.document.activation.active)
        assertEquals(0, first.document.activation.skinStamp)
        assertEquals(InterlockState.CLEAR, first.document.activation.rotationInterlock.state)
        assertTrue(first.document.packs.isEmpty())
    }

    @Test
    fun registryStoreRejectsNonHollowKnightProfileRoot() {
        val wrongRoot = File(profileRoot, "profiles/silksong/skins").apply { mkdirs() }

        assertThrows(IllegalArgumentException::class.java) {
            SkinRegistryStore(wrongRoot, PermissiveTestSkinQuota(wrongRoot))
        }
        assertFalse(File(wrongRoot, "registry").exists())
    }

    @Test
    fun registryStoreRejectsLockManagerForAnotherProfileRoot() {
        val otherRoot = File(profileRoot, "other/profiles/hollow-knight/skins").apply { mkdirs() }

        assertThrows(IllegalArgumentException::class.java) {
            SkinRegistryStore(
                skinsRoot,
                PermissiveTestSkinQuota(skinsRoot),
                lockManager = SkinLockManager(otherRoot),
            )
        }
    }

    @Test
    fun virginProfileCreatesContainedRootAndPublishesGenesisThroughNext() {
        skinsRoot.deleteRecursively()
        val fs = FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }

        val genesis = assertOk(SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover())

        assertEquals(0, genesis.sequence)
        assertTrue(skinsRoot.isDirectory)
        assertTrue(File(skinsRoot, "locks/session.lock").isFile)
        assertTrue(File(skinsRoot, "locks/registry.lock").isFile)
        assertTrue(fs.events.indexOf("write-new:next") >= 0)
        assertTrue(fs.events.indexOf("write-new:current") > fs.events.indexOf("write-new:next"))
        assertFalse(File(skinsRoot, "registry/next").exists())
    }

    @Test
    fun idempotentOwnerMutationDoesNotPublishAnotherGeneration() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val installed = assertOk(
            store.commit(
                genesis,
                UUID.fromString("02345678-1234-4234-8234-123456789abc"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )

        val unchanged = assertOk(
            store.commit(
                installed,
                UUID.fromString("02345678-1234-4234-8234-123456789abd"),
                "launcher",
                SkinRegistryMutations().install(published(receipt = hex('f'))),
            ),
        )

        assertEquals(installed, unchanged)
        assertEquals(2, File(skinsRoot, "registry/generations").listFiles()!!.size)
    }

    @Test
    fun commitCasPublishesImmutableChildAndIsByteIdenticallyIdempotent() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val operation = UUID.fromString("12345678-1234-4234-8234-123456789abc")
        val mutation = SkinRegistryMutations().install(published())

        val committed = assertOk(store.commit(genesis, operation, "launcher", mutation))
        val retried = assertOk(store.commit(genesis, operation, "launcher", mutation))

        assertEquals(committed, retried)
        assertEquals(1, committed.sequence)
        assertEquals(genesis.generationId, committed.document.parentGenerationId)
        assertEquals(operation.toString(), committed.document.operationId)
        assertEquals(2, File(skinsRoot, "registry/generations").listFiles()!!.size)
        assertTrue(File(skinsRoot, "registry/current").isFile)
        assertTrue(File(skinsRoot, "registry/previous").isFile)
        assertFalse(File(skinsRoot, "registry/next").exists())

        val stale = genesis.copy(sha256 = hex('0'))
        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            store.commit(stale, UUID.fromString("22345678-1234-4234-8234-123456789abc"), "launcher", mutation),
        )
        assertEquals(2, File(skinsRoot, "registry/generations").listFiles()!!.size)
    }

    @Test
    fun retriesEveryPointerFaultThroughCompleteDurableReconciliation() {
        val boundaries = listOf(
            "write-new:next" to 1,
            "sync-file:next" to 1,
            "sync-dir:registry" to 2,
            "write-new:previous" to 1,
            "sync-file:previous" to 1,
            "sync-dir:registry" to 3,
            "write-existing:current" to 1,
            "sync-file:current" to 1,
            "sync-dir:registry" to 4,
            "delete-contained:next" to 1,
            "sync-dir:registry" to 5,
        )

        boundaries.forEachIndexed { index, (event, relativeOccurrence) ->
            val caseSkins = File(profileRoot, "fault-$index/profiles/hollow-knight/skins").apply { mkdirs() }
            val fs = FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }
            val store = SkinRegistryStore(caseSkins, PermissiveTestSkinQuota(caseSkins), fs, SkinLockManager(caseSkins))
            val genesis = assertOk(store.recover())
            val operation = UUID.nameUUIDFromBytes("pointer-fault-$index".toByteArray(StandardCharsets.US_ASCII))
            val mutation = SkinRegistryMutations().install(published())
            fs.failOnEvent = event
            fs.failOnOccurrence = fs.events.count { it == event } + relativeOccurrence

            assertError(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                store.commit(genesis, operation, "launcher", mutation),
            )

            fs.failOnEvent = null
            val retryStart = fs.events.size
            val retried = assertOk(store.commit(genesis, operation, "launcher", mutation))

            assertEquals(operation.toString(), retried.generationId)
            assertCompletePointerReconciliation(fs.events.drop(retryStart))
            assertFalse(File(caseSkins, "registry/next").exists())
            assertEquals(retried, assertOk(store.recover()))
            assertEquals(2, File(caseSkins, "registry/generations").listFiles()!!.size)
        }
    }

    @Test
    fun idempotentRetryRequiresExactLoadedImmediateParent() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val firstOperation = UUID.fromString("13345678-1234-4234-8234-123456789abc")
        val parent = assertOk(
            store.commit(genesis, firstOperation, "launcher", SkinRegistryMutations().install(published())),
        )
        val retryOperation = UUID.fromString("13345678-1234-4234-8234-123456789abd")
        val mutation = SkinRegistryMutations().setEligibility(published().id, true)
        val child = assertOk(store.commit(parent, retryOperation, "launcher", mutation))
        val forgedParents = listOf(
            parent.document.copy(writer = "forged-parent"),
            parent.document.copy(sequence = parent.sequence + 1L),
            parent.document.copy(generationId = "13345678-1234-4234-8234-123456789abe"),
        ).map { document ->
            RegistryHead(
                generationId = document.generationId,
                sequence = document.sequence,
                sha256 = digest(assertOk(SkinRegistryDocumentCodec.canonical(document))),
                document = document,
            )
        }

        forgedParents.forEach { forgedParent ->
            assertError(
                SkinImportCode.REGISTRY_CONFLICT,
                store.commit(forgedParent, retryOperation, "launcher", mutation),
            )
        }
        assertEquals(child, assertOk(store.recover()))
        assertEquals(3, File(skinsRoot, "registry/generations").listFiles()!!.size)
    }

    @Test
    fun retainedOperationScanStopsAtExactEightGenerationFloor() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val operationIds = (1..9).map { index ->
            UUID.fromString("15345678-1234-4234-8234-${index.toString().padStart(12, '0')}")
        }
        val mutationSequence = listOf(
            SkinRegistryMutations().install(published()),
            SkinRegistryMutations().setEligibility(published().id, true),
            SkinRegistryMutations().select(published().id),
            SkinRegistryMutations().setEligibility(published().id, false),
            SkinRegistryMutations().select(null),
            SkinRegistryMutations().setEligibility(published().id, true),
            SkinRegistryMutations().select(published().id),
            SkinRegistryMutations().setEligibility(published().id, false),
        )
        var current = genesis
        mutationSequence.forEachIndexed { index, mutation ->
            current = assertOk(store.commit(current, operationIds[index], "launcher", mutation))
        }
        assertEquals(8, current.sequence)
        val genesisDirectory = File(
            skinsRoot,
            "registry/generations/${RegistryPointer(genesis.sequence, genesis.generationId, genesis.sha256).directoryName}",
        )
        assertTrue(genesisDirectory.deleteRecursively())

        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            store.commit(
                current,
                operationIds.first(),
                "launcher",
                SkinRegistryMutations().select(null),
            ),
        )
        assertEquals(8, File(skinsRoot, "registry/generations").listFiles()!!.size)

        val advanced = assertOk(
            store.commit(
                current,
                operationIds.last(),
                "launcher",
                SkinRegistryMutations().setEligibility(published().id, true),
            ),
        )
        assertEquals(9, advanced.sequence)
        assertEquals(advanced, assertOk(store.recover()))
    }

    @Test
    fun operationUuidCannotBeReusedAcrossBoundedHistoryAToBToA() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val operationA = UUID.fromString("14345678-1234-4234-8234-123456789abc")
        val generationA = assertOk(
            store.commit(genesis, operationA, "launcher", SkinRegistryMutations().install(published())),
        )
        val operationB = UUID.fromString("14345678-1234-4234-8234-123456789abd")
        val generationB = assertOk(
            store.commit(
                generationA,
                operationB,
                "launcher",
                SkinRegistryMutations().setEligibility(published().id, true),
            ),
        )

        val result = store.commit(
            generationB,
            operationA,
            "launcher",
            SkinRegistryMutations().select(published().id),
        )

        assertError(SkinImportCode.REGISTRY_CONFLICT, result)
        assertEquals(generationB, assertOk(store.recover()))
        assertEquals(3, File(skinsRoot, "registry/generations").listFiles()!!.size)
    }

    @Test
    fun reusedOperationIdForDifferentRequestConflictsWithoutWrite() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val operation = UUID.fromString("11345678-1234-4234-8234-123456789abc")
        val installed = assertOk(
            store.commit(genesis, operation, "launcher", SkinRegistryMutations().install(published())),
        )

        val result = store.commit(
            installed,
            operation,
            "launcher",
            SkinRegistryMutations().setEligibility(published().id, true),
        )

        assertError(SkinImportCode.REGISTRY_CONFLICT, result)
        assertEquals(installed, assertOk(store.recover()))
        assertEquals(2, File(skinsRoot, "registry/generations").listFiles()!!.size)
    }

    @Test
    fun registryCommitRejectsUnqualifiedMutationSkipPathWithoutWrite() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val forged = RegistryMutation { current ->
            SkinResult.Ok(
                current.copy(
                    activation = current.activation.copy(mode = SkinMode.ROTATE),
                ),
            )
        }

        val result = store.commit(
            genesis,
            UUID.fromString("32345678-1234-4234-8234-123456789abc"),
            "launcher",
            forged,
        )

        assertError(SkinImportCode.REGISTRY_CONFLICT, result)
        assertEquals(1, File(skinsRoot, "registry/generations").listFiles()!!.size)
        assertEquals(genesis, assertOk(store.recover()))
    }

    @Test
    fun coordinatorCommitUsesItsExistingAdmissionWithoutDoubleReservation() {
        val genesis = assertOk(
            SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover(),
        )
        val rejectingQuota = object : SkinQuotaAdmission {
            override val root = skinsRoot
            override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> =
                SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "unexpected second reservation")
        }
        val admittedStore = SkinRegistryStore(skinsRoot, rejectingQuota, fs, SkinLockManager(skinsRoot))

        val committed = admittedStore.commitAdmittedForCoordinator(
            genesis,
            UUID.fromString("72345678-1234-4234-8234-123456789abc"),
            "import-coordinator",
            SkinRegistryMutations().install(published()),
        )

        assertTrue("Expected admitted commit success, got $committed", committed is SkinResult.Ok)
    }

    @Test
    fun referenceSnapshotValidatesAllPointersAndRetainsCurrentAndPreviousDigests() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val original = published()
        val installed = assertOk(
            store.commit(
                genesis,
                UUID.fromString("73345678-1234-4234-8234-123456789abc"),
                "launcher",
                SkinRegistryMutations().install(original),
            ),
        )
        val replacement = original.copy(
            contentSha256 = hex('4'),
            treeSha256 = hex('5'),
            manifestSha256 = hex('6'),
            importReceiptSha256 = hex('7'),
        )
        assertOk(
            store.commit(
                installed,
                UUID.fromString("73345678-1234-4234-8234-123456789abd"),
                "launcher",
                SkinRegistryMutations().replace(
                    original.id,
                    original.treeSha256,
                    original.importReceiptSha256,
                    replacement,
                ),
            ),
        )

        val references = assertOk(store.referenceSnapshotForCoordinator())

        assertTrue(setOf(hex('b'), hex('d'), hex('5'), hex('7')).all { it in references })
        val next = File(skinsRoot, "registry/next").apply { writeText("malformed\n") }
        assertError(SkinImportCode.REGISTRY_CORRUPT, store.referenceSnapshotForCoordinator())
        assertTrue(next.isFile)
    }

    @Test
    fun canonicalRegistryRejectsDuplicateUnknownNullAndNonNormalizedScalars() {
        val genesis = assertOk(SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover())
        val canonical = assertOk(SkinRegistryDocumentCodec.canonical(genesis.document))
        val text = canonical.toString(StandardCharsets.UTF_8)

        assertTrue(text.contains("\"sequence\":\"0\""))
        assertTrue(text.contains("\"skinStamp\":\"0\""))
        assertArrayEquals(canonical, assertOk(SkinRegistryDocumentCodec.canonical(assertOk(SkinRegistryDocumentCodec.parse(canonical)))))

        val duplicate = text.replaceFirst("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")
        val unknown = text.dropLast(1) + ",\"unknown\":\"x\"}"
        val withNull = text.replace("\"writer\":\"genesis\"", "\"writer\":null")
        listOf(duplicate, unknown, withNull).forEach { invalid ->
            assertError(SkinImportCode.REGISTRY_CORRUPT, SkinRegistryDocumentCodec.parse(invalid.toByteArray()))
        }

        val malformedDigest = genesis.document.copy(catalogSha256 = genesis.document.catalogSha256.uppercase())
        assertError(SkinImportCode.REGISTRY_CORRUPT, SkinRegistryDocumentCodec.canonical(malformedDigest))
        val negativeSequence = genesis.document.copy(sequence = -1)
        assertError(SkinImportCode.REGISTRY_CORRUPT, SkinRegistryDocumentCodec.canonical(negativeSequence))
    }

    @Test
    fun armedInterlockMustCrossReferenceItsImmediateBaseGeneration() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val installed = assertOk(
            store.commit(
                genesis,
                UUID.fromString("42345678-1234-4234-8234-123456789abc"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        val prior = installed.document.activation.snapshot()
        val pack = installed.document.packs.single()
        val target = ActivationSnapshot(
            SkinMode.ON,
            pack.id,
            ActiveVisual.Pack(pack.id, pack.treeSha256, pack.contentSha256, pack.importReceiptSha256),
            1,
        )
        val operation = "42345678-1234-4234-8234-123456789abd"
        val armed = RotationInterlock(
            InterlockState.ARMED,
            "42345678-1234-4234-8234-123456789abe",
            SkinOperationKind.MODE_ON,
            "49999999-9999-4999-8999-999999999999",
            installed.sha256,
            prior,
            target,
            SkinBindingToken("binding-one"),
            true,
            null,
            null,
        )
        val child = installed.document.copy(
            generationId = operation,
            sequence = 2,
            parentGenerationId = installed.generationId,
            operationId = operation,
            activation = installed.document.activation.copy(rotationInterlock = armed),
        )

        assertError(SkinImportCode.REGISTRY_CORRUPT, SkinRegistryDocumentCodec.canonical(child))

        val invalidFailure = armed.copy(
            state = InterlockState.ROLLBACK_FAILED,
            baseGenerationId = installed.generationId,
            originalFailure = "not a stable code",
            rollbackFailure = "ROLLBACK_FAILED",
        )
        val failedChild = child.copy(
            generationId = "42345678-1234-4234-8234-123456789abf",
            sequence = 3,
            parentGenerationId = operation,
            operationId = "42345678-1234-4234-8234-123456789abf",
            activation = child.activation.copy(rotationInterlock = invalidFailure),
        )
        assertError(SkinImportCode.REGISTRY_CORRUPT, SkinRegistryDocumentCodec.canonical(failedChild))
    }

    @Test
    fun registryDocumentEnforcesExactProfileGameCatalogAndPackOrder() {
        val genesis = assertOk(SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot)).recover()).document
        val first = RegistryPack("alpha", "Alpha", "Unknown", hex('a'), hex('b'), hex('c'), hex('d'), false)
        val second = RegistryPack("beta", "Beta", "Unknown", hex('e'), hex('f'), hex('1'), hex('2'), true)
        val operation = "52345678-1234-4234-8234-123456789abc"
        val valid = genesis.copy(
            generationId = operation,
            sequence = 1,
            parentGenerationId = genesis.generationId,
            operationId = operation,
            writer = "test",
            packs = listOf(first, second),
        )

        assertOk(SkinRegistryDocumentCodec.canonical(valid))
        assertError(
            SkinImportCode.REGISTRY_CORRUPT,
            SkinRegistryDocumentCodec.canonical(valid.copy(profileId = "silksong")),
        )
        assertError(
            SkinImportCode.REGISTRY_CORRUPT,
            SkinRegistryDocumentCodec.canonical(valid.copy(gameVersion = "1.5.12612")),
        )
        assertError(
            SkinImportCode.REGISTRY_CORRUPT,
            SkinRegistryDocumentCodec.canonical(valid.copy(catalogId = "other")),
        )
        assertError(
            SkinImportCode.REGISTRY_CORRUPT,
            SkinRegistryDocumentCodec.canonical(valid.copy(packs = listOf(second, first))),
        )
        assertNotEquals(first.candidateKey, second.candidateKey)
    }

    private fun assertCompletePointerReconciliation(events: List<String>) {
        val barriers = events.mapNotNull { event ->
            when (event) {
                "write-new:next", "write-existing:next" -> "write:next"
                "sync-file:next" -> event
                "write-new:previous", "write-existing:previous" -> "write:previous"
                "sync-file:previous" -> event
                "write-new:current", "write-existing:current" -> "write:current"
                "sync-file:current" -> event
                "delete-contained:next" -> "remove:next"
                "sync-dir:registry" -> event
                else -> null
            }
        }
        assertEquals(
            listOf(
                "write:next",
                "sync-file:next",
                "sync-dir:registry",
                "write:previous",
                "sync-file:previous",
                "sync-dir:registry",
                "write:current",
                "sync-file:current",
                "sync-dir:registry",
                "remove:next",
                "sync-dir:registry",
            ),
            barriers,
        )
    }

    private fun digest(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun published(receipt: String = hex('d')) = PublishedSkin(
        id = "local-${hex('a').take(58)}",
        candidateKey = hex('a'),
        name = "Alpha",
        contentSha256 = hex('c'),
        treeSha256 = hex('b'),
        manifestSha256 = hex('e'),
        importReceiptSha256 = receipt,
        objectRoot = File(profileRoot, "object"),
        newlyCreatedRoots = emptyList(),
    )

    private fun hex(character: Char) = character.toString().repeat(64)

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }
}
