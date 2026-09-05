package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class SkinRegistryMutationsTest {
    private val mutations = SkinRegistryMutations()

    @Test
    fun rejectsDuplicateCandidateOwnershipReadOnly() {
        val duplicate = pack(id = "beta", candidate = hex('b'), tree = hex('d'), content = hex('e'), receipt = hex('f'))
        val current = document(packs = listOf(pack(), duplicate.copy(candidateKey = hex('a'))))

        val result = mutations.install(published(id = "gamma", candidate = hex('c'))).apply(current)

        assertError(SkinImportCode.REGISTRY_CORRUPT, result)
        assertEquals(current, current.copy())
    }

    @Test
    fun reimportsOwnerIdempotentlyWithoutReceiptRename() {
        val owner = pack()
        val current = document(packs = listOf(owner))
        val renamedReceipt = published(
            id = owner.id,
            candidate = owner.candidateKey,
            tree = owner.treeSha256,
            content = owner.contentSha256,
            receipt = hex('f'),
        )

        val result = mutations.install(renamedReceipt).apply(current)

        assertSame(current, assertOk(result))
        assertEquals(owner.importReceiptSha256, assertOk(result).packs.single().importReceiptSha256)
    }

    @Test
    fun replacesOnlyConfirmedCasTarget() {
        val owner = pack(eligible = true)
        val active = ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256)
        val activation = SkinActivation(SkinMode.ON, owner.id, active, 7, clearInterlock())
        val current = document(packs = listOf(owner), activation = activation)
        val replacement = published(
            id = owner.id,
            candidate = hex('b'),
            tree = hex('d'),
            content = hex('e'),
            receipt = hex('f'),
            name = "Replacement",
        )

        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            mutations.replace(owner.id, hex('0'), owner.importReceiptSha256, replacement).apply(current),
        )
        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            mutations.replace(owner.id, owner.treeSha256, hex('0'), replacement).apply(current),
        )

        val changed = assertOk(
            mutations.replace(owner.id, owner.treeSha256, owner.importReceiptSha256, replacement).apply(current),
        )

        assertEquals(owner.id, changed.packs.single().id)
        assertEquals("Replacement", changed.packs.single().name)
        assertEquals(replacement.candidateKey, changed.packs.single().candidateKey)
        assertTrue(changed.packs.single().rotationEligible)
        assertEquals(activation, changed.activation)
    }

    @Test
    fun registryHasNoPublicModeSetterOrSkipPath() {
        val declaredNames = SkinRegistryMutations::class.java.methods
            .filter { it.declaringClass == SkinRegistryMutations::class.java && !it.isSynthetic }
            .map { it.name }
            .toSet()
        val publicNames = declaredNames.map { it.substringBefore('$') }.toSet()

        assertEquals(setOf("closeActivation", "install", "replace", "select", "setEligibility"), publicNames)
        assertTrue(declaredNames.any { it.startsWith("closeActivation\$") })
        assertFalse(publicNames.any { it == "setMode" || it == "advanceMode" })
    }

    @Test
    fun offAdvanceWithoutSelectionReturnsNoSelectedWithoutWrite() {
        val current = document()
        val expected = current.activation.snapshot()

        val result = mutations.closeActivation(RegistryActivationClosure.OnToRotate(expected)).apply(current)

        assertError(SkinImportCode.NO_SELECTED_SKIN, result)
        assertEquals(SkinMode.OFF, current.activation.mode)
        assertEquals(0, current.activation.skinStamp)
    }

    @Test
    fun onToRotateClosureKeepsVisualSelectionEligibilityStamp() {
        val owner = pack(eligible = true)
        val current = document(
            packs = listOf(owner),
            activation = SkinActivation(
                SkinMode.ON,
                owner.id,
                ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256),
                19,
                clearInterlock(),
            ),
        )

        val changed = assertOk(
            mutations.closeActivation(RegistryActivationClosure.OnToRotate(current.activation.snapshot())).apply(current),
        )

        assertEquals(SkinMode.ROTATE, changed.activation.mode)
        assertEquals(current.activation.selectedPackId, changed.activation.selectedPackId)
        assertEquals(current.activation.active, changed.activation.active)
        assertEquals(current.activation.skinStamp, changed.activation.skinStamp)
        assertEquals(current.packs, changed.packs)
        assertEquals(InterlockState.CLEAR, changed.activation.rotationInterlock.state)
    }

    @Test
    fun verifiedVanillaRotateOffModeOnlyPreservesReceiptStampAndWritesNothing() {
        val owner = pack(eligible = true)
        val active = ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256)
        val current = document(
            packs = listOf(owner),
            activation = SkinActivation(SkinMode.ROTATE, owner.id, active, 23, clearInterlock()),
        )
        val proof = VerifiedLiveVisualProof.Vanilla(SkinBindingToken("binding-23"))

        val changed = assertOk(
            mutations.closeActivation(
                RegistryActivationClosure.VerifiedVanillaToOff(current.activation.snapshot(), proof),
            ).apply(current),
        )

        assertEquals(SkinMode.OFF, changed.activation.mode)
        assertEquals(owner.id, changed.activation.selectedPackId)
        assertEquals(active, changed.activation.active)
        assertEquals(owner.importReceiptSha256, (changed.activation.active as ActiveVisual.Pack).importReceiptSha256)
        assertEquals(23, changed.activation.skinStamp)
        assertEquals(current.packs, changed.packs)
    }

    @Test
    fun modeOffTransactionClosureCannotPersistOffBeforeVerifiedCompletion() {
        val owner = pack()
        val prior = ActivationSnapshot(
            SkinMode.ROTATE,
            owner.id,
            ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256),
            31,
        )
        val target = ActivationSnapshot(SkinMode.OFF, owner.id, ActiveVisual.Vanilla, 32)
        val armed = armedInterlock(prior, target)
        val current = document(
            packs = listOf(owner),
            activation = SkinActivation(prior.mode, prior.selectedPackId, prior.active, prior.skinStamp, armed),
        )
        val stale = armed.copy(transactionId = "99999999-9999-4999-8999-999999999999")

        val rejected = mutations.closeActivation(
            RegistryActivationClosure.VerifiedTransaction(stale, target),
        ).apply(current)

        assertError(SkinImportCode.REGISTRY_CONFLICT, rejected)
        assertEquals(SkinMode.ROTATE, current.activation.mode)
        assertEquals(InterlockState.ARMED, current.activation.rotationInterlock.state)

        val completed = assertOk(
            mutations.closeActivation(
                RegistryActivationClosure.VerifiedTransaction(armed, target),
            ).apply(current),
        )
        assertEquals(SkinMode.OFF, completed.activation.mode)
        assertEquals(InterlockState.CLEAR, completed.activation.rotationInterlock.state)
    }

    @Test
    fun verifiedTransactionRejectsOuterActivationThatIsNotRecordedPrior() {
        val owner = pack()
        val prior = ActivationSnapshot(
            SkinMode.ROTATE,
            owner.id,
            ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256),
            41,
        )
        val target = ActivationSnapshot(SkinMode.OFF, owner.id, ActiveVisual.Vanilla, 42)
        val armed = armedInterlock(prior, target)
        val malformed = document(
            packs = listOf(owner),
            activation = SkinActivation(SkinMode.ON, owner.id, prior.active, prior.skinStamp, armed),
        )

        val result = mutations.closeActivation(
            RegistryActivationClosure.VerifiedTransaction(armed, target),
        ).apply(malformed)

        assertError(SkinImportCode.REGISTRY_CORRUPT, result)
        assertEquals(InterlockState.ARMED, malformed.activation.rotationInterlock.state)
    }

    @Test
    fun armedAndRollbackFailedCanonicalRequireCheckedSingleStampAdvance() {
        val owner = pack()
        val prior = ActivationSnapshot(
            SkinMode.ROTATE,
            owner.id,
            ActiveVisual.Pack(owner.id, owner.treeSha256, owner.contentSha256, owner.importReceiptSha256),
            51,
        )
        val target = ActivationSnapshot(SkinMode.OFF, owner.id, ActiveVisual.Vanilla, 52)

        listOf(InterlockState.ARMED, InterlockState.ROLLBACK_FAILED).forEach { state ->
            val validInterlock = armedInterlock(prior, target).copy(
                state = state,
                originalFailure = if (state == InterlockState.ROLLBACK_FAILED) "APPLY_FAILED" else null,
                rollbackFailure = if (state == InterlockState.ROLLBACK_FAILED) "ROLLBACK_FAILED" else null,
            )
            val valid = document(
                packs = listOf(owner),
                activation = SkinActivation(prior.mode, prior.selectedPackId, prior.active, prior.skinStamp, validInterlock),
            )
            assertOk(SkinRegistryDocumentCodec.canonical(valid))

            val skippedStamp = validInterlock.copy(target = target.copy(skinStamp = 53))
            assertError(
                SkinImportCode.REGISTRY_CORRUPT,
                SkinRegistryDocumentCodec.canonical(
                    valid.copy(activation = valid.activation.copy(rotationInterlock = skippedStamp)),
                ),
            )

            val overflowPrior = prior.copy(skinStamp = Long.MAX_VALUE)
            val overflowTarget = target.copy(skinStamp = Long.MAX_VALUE)
            val overflowInterlock = validInterlock.copy(prior = overflowPrior, target = overflowTarget)
            assertError(
                SkinImportCode.REGISTRY_CORRUPT,
                SkinRegistryDocumentCodec.canonical(
                    valid.copy(
                        activation = SkinActivation(
                            overflowPrior.mode,
                            overflowPrior.selectedPackId,
                            overflowPrior.active,
                            overflowPrior.skinStamp,
                            overflowInterlock,
                        ),
                    ),
                ),
            )
        }
    }

    @Test
    fun registryActiveReceiptNeverProvesLiveVanilla() {
        val owner = pack()
        val persistedVisual = ActiveVisual.Pack(
            owner.id,
            owner.treeSha256,
            owner.contentSha256,
            owner.importReceiptSha256,
        )
        val current = document(
            packs = listOf(owner),
            activation = SkinActivation(SkinMode.ROTATE, owner.id, persistedVisual, 43, clearInterlock()),
        )
        val proofParameter = RegistryActivationClosure.VerifiedVanillaToOff::class.java
            .declaredConstructors.single()
            .parameterTypes[1]

        assertFalse((persistedVisual as Any) is VerifiedLiveVisualProof)
        assertNotEquals(owner.importReceiptSha256, "binding-43")
        assertEquals(VerifiedLiveVisualProof.Vanilla::class.java, proofParameter)
        assertError(
            SkinImportCode.REGISTRY_CONFLICT,
            mutations.closeActivation(
                RegistryActivationClosure.VerifiedVanillaToOff(
                    current.activation.snapshot(),
                    VerifiedLiveVisualProof.Vanilla(SkinBindingToken(" ")),
                ),
            ).apply(current),
        )

        val changed = assertOk(
            mutations.closeActivation(
                RegistryActivationClosure.VerifiedVanillaToOff(
                    current.activation.snapshot(),
                    VerifiedLiveVisualProof.Vanilla(SkinBindingToken("binding-43")),
                ),
            ).apply(current),
        )

        assertEquals(SkinMode.OFF, changed.activation.mode)
        assertEquals(persistedVisual, changed.activation.active)
        assertEquals(owner.importReceiptSha256, (changed.activation.active as ActiveVisual.Pack).importReceiptSha256)
    }

    @Test
    fun installRejectsUnpairedSurrogateAndBidiTextReadOnly() {
        val current = document()

        listOf("\uD800", "Unsafe‮Name").forEach { invalidName ->
            val result = mutations.install(published(name = invalidName)).apply(current)
            assertError(SkinImportCode.INVALID_INPUT, result)
            assertTrue(current.packs.isEmpty())
        }
    }

    @Test
    fun ordinaryInstallCannotClaimArbitraryTargetIdForNewCandidate() {
        val current = document()

        val result = mutations.install(published(id = "target-id")).apply(current)

        assertError(SkinImportCode.ID_COLLISION, result)
        assertTrue(current.packs.isEmpty())
    }

    @Test
    fun newInstallIsIneligibleAndDoesNotAlterActivation() {
        val current = document()

        val changed = assertOk(mutations.install(published(id = derivedId(hex('a')))).apply(current))

        assertFalse(changed.packs.single().rotationEligible)
        assertEquals(current.activation, changed.activation)
    }

    private fun document(
        packs: List<RegistryPack> = emptyList(),
        activation: SkinActivation = SkinActivation(SkinMode.OFF, null, ActiveVisual.Vanilla, 0, clearInterlock()),
    ) = SkinRegistryDocument(
        schemaVersion = 1,
        generationId = "11111111-1111-4111-8111-111111111111",
        sequence = 1,
        parentGenerationId = "00000000-0000-0000-0000-000000000000",
        operationId = "22222222-2222-4222-8222-222222222222",
        writer = "test",
        profileId = "hollow-knight",
        gameVersion = "1.5.12620",
        catalogId = "hk-custom-knight-v3.5.0-205",
        catalogSha256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a",
        packs = packs,
        activation = activation,
    )

    private fun pack(
        id: String = "alpha",
        candidate: String = hex('a'),
        tree: String = hex('b'),
        content: String = hex('c'),
        receipt: String = hex('d'),
        eligible: Boolean = false,
    ) = RegistryPack(id, "Alpha", "Unknown", candidate, tree, content, receipt, eligible)

    private fun published(
        id: String = "alpha",
        candidate: String = hex('a'),
        tree: String = hex('b'),
        content: String = hex('c'),
        receipt: String = hex('d'),
        name: String = "Alpha",
    ) = PublishedSkin(
        id = id,
        candidateKey = candidate,
        name = name,
        contentSha256 = content,
        treeSha256 = tree,
        manifestSha256 = hex('e'),
        importReceiptSha256 = receipt,
        objectRoot = File("build/test-registry-object/$tree"),
        newlyCreatedRoots = emptyList(),
    )

    private fun clearInterlock() = RotationInterlock(
        InterlockState.CLEAR,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
    )

    private fun armedInterlock(prior: ActivationSnapshot, target: ActivationSnapshot) = RotationInterlock(
        InterlockState.ARMED,
        "33333333-3333-4333-8333-333333333333",
        SkinOperationKind.MODE_OFF,
        "00000000-0000-0000-0000-000000000000",
        hex('8'),
        prior,
        target,
        SkinBindingToken("binding-31"),
        true,
        null,
        null,
    )

    private fun derivedId(candidate: String) = "local-${candidate.take(58)}"
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
