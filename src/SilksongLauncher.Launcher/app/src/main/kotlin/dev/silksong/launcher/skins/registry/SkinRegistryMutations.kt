package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.text.Normalizer

internal sealed interface RegistryActivationClosure {
    data class OnToRotate(
        val expected: ActivationSnapshot,
    ) : RegistryActivationClosure

    data class VerifiedVanillaToOff(
        val expected: ActivationSnapshot,
        val proof: VerifiedLiveVisualProof.Vanilla,
    ) : RegistryActivationClosure

    data class VerifiedTransaction(
        val expectedArmed: RotationInterlock,
        val closure: ActivationSnapshot,
    ) : RegistryActivationClosure
}

private class AuthorizedRegistryMutation(
    private val action: (SkinRegistryDocument) -> SkinResult<SkinRegistryDocument>,
) : RegistryMutation {
    override fun apply(current: SkinRegistryDocument): SkinResult<SkinRegistryDocument> = action(current)
}

internal fun RegistryMutation.hasRegistryAuthority(): Boolean = this is AuthorizedRegistryMutation

class SkinRegistryMutations {
    fun select(id: String?): RegistryMutation = mutation { current ->
        readiness(current)?.let { return@mutation it }
        if (id != null && current.packs.none { it.id == id }) {
            return@mutation error(SkinImportCode.INVALID_INPUT, "Selected skin is not installed")
        }
        if (current.activation.selectedPackId == id) return@mutation SkinResult.Ok(current)
        SkinResult.Ok(current.copy(activation = current.activation.copy(selectedPackId = id)))
    }

    fun setEligibility(id: String, eligible: Boolean): RegistryMutation = mutation { current ->
        readiness(current)?.let { return@mutation it }
        val index = current.packs.indexOfFirst { it.id == id }
        if (index < 0) return@mutation error(SkinImportCode.INVALID_INPUT, "Eligible skin is not installed")
        if (current.packs[index].rotationEligible == eligible) return@mutation SkinResult.Ok(current)
        val packs = current.packs.toMutableList()
        packs[index] = packs[index].copy(rotationEligible = eligible)
        SkinResult.Ok(current.copy(packs = packs))
    }

    internal fun closeActivation(closure: RegistryActivationClosure): RegistryMutation = mutation { current ->
        registryCorruption(current)?.let { return@mutation it }
        when (closure) {
            is RegistryActivationClosure.OnToRotate -> closeOnToRotate(current, closure)
            is RegistryActivationClosure.VerifiedVanillaToOff -> closeVerifiedVanilla(current, closure)
            is RegistryActivationClosure.VerifiedTransaction -> closeVerifiedTransaction(current, closure)
        }
    }

    fun install(published: PublishedSkin): RegistryMutation = mutation { current ->
        readiness(current)?.let { return@mutation it }
        validatePublished(published)?.let { return@mutation it }
        val owner = current.packs.singleOrNull { it.candidateKey == published.candidateKey }
        if (owner != null) {
            if (owner.id != published.id) {
                return@mutation error(
                    SkinImportCode.CANDIDATE_ALREADY_INSTALLED,
                    "Candidate is already owned by ${owner.id}",
                )
            }
            if (owner.treeSha256 != published.treeSha256 || owner.contentSha256 != published.contentSha256) {
                return@mutation error(SkinImportCode.REIMPORT_CHANGED, "Candidate owner content changed")
            }
            return@mutation SkinResult.Ok(current)
        }
        val derivedId = "local-${published.candidateKey.take(58)}"
        if (published.id != derivedId) {
            return@mutation error(SkinImportCode.ID_COLLISION, "New skin ID is not derived from its candidate owner")
        }
        if (current.packs.any { it.id == published.id }) {
            return@mutation error(SkinImportCode.ID_COLLISION, "Skin ID belongs to another candidate")
        }
        if (current.packs.size >= SkinRegistryAuthority.MAX_PACKS) {
            return@mutation error(SkinImportCode.LIMIT_EXCEEDED, "Installed skin bound exceeded")
        }
        val installed = RegistryPack(
            id = published.id,
            name = published.name,
            author = "Unknown",
            candidateKey = published.candidateKey,
            treeSha256 = published.treeSha256,
            contentSha256 = published.contentSha256,
            importReceiptSha256 = published.importReceiptSha256,
            rotationEligible = false,
        )
        SkinResult.Ok(current.copy(packs = (current.packs + installed).sortedBy(RegistryPack::id)))
    }

    fun replace(
        targetId: String,
        expectedTree: String,
        expectedReceipt: String,
        published: PublishedSkin,
    ): RegistryMutation = mutation { current ->
        readiness(current)?.let { return@mutation it }
        validatePublished(published)?.let { return@mutation it }
        val targetIndex = current.packs.indexOfFirst { it.id == targetId }
        if (targetIndex < 0) return@mutation error(SkinImportCode.REGISTRY_CONFLICT, "Replace target is absent")
        val target = current.packs[targetIndex]
        if (target.treeSha256 != expectedTree || target.importReceiptSha256 != expectedReceipt) {
            return@mutation error(SkinImportCode.REGISTRY_CONFLICT, "Replace target changed")
        }
        if (published.id != targetId) {
            return@mutation error(SkinImportCode.ID_COLLISION, "Replacement was not built for its target ID")
        }
        val otherOwner = current.packs.singleOrNull {
            it.candidateKey == published.candidateKey && it.id != targetId
        }
        if (otherOwner != null) {
            return@mutation error(
                SkinImportCode.CANDIDATE_ALREADY_INSTALLED,
                "Candidate is already owned by ${otherOwner.id}",
            )
        }
        val replacement = target.copy(
            name = published.name,
            author = "Unknown",
            candidateKey = published.candidateKey,
            treeSha256 = published.treeSha256,
            contentSha256 = published.contentSha256,
            importReceiptSha256 = published.importReceiptSha256,
        )
        if (replacement == target) return@mutation SkinResult.Ok(current)
        val packs = current.packs.toMutableList()
        packs[targetIndex] = replacement
        SkinResult.Ok(current.copy(packs = packs))
    }

    private fun closeOnToRotate(
        current: SkinRegistryDocument,
        closure: RegistryActivationClosure.OnToRotate,
    ): SkinResult<SkinRegistryDocument> {
        val activation = current.activation
        if (activation.mode == SkinMode.OFF && activation.selectedPackId == null &&
            activation.rotationInterlock.state == InterlockState.CLEAR && activation.snapshot() == closure.expected
        ) {
            return error(SkinImportCode.NO_SELECTED_SKIN, "No skin is selected")
        }
        if (activation.rotationInterlock.state != InterlockState.CLEAR ||
            activation.snapshot() != closure.expected || activation.mode != SkinMode.ON
        ) {
            return error(SkinImportCode.REGISTRY_CONFLICT, "ON to ROTATE closure is stale or inapplicable")
        }
        return SkinResult.Ok(current.copy(activation = activation.copy(mode = SkinMode.ROTATE)))
    }

    private fun closeVerifiedVanilla(
        current: SkinRegistryDocument,
        closure: RegistryActivationClosure.VerifiedVanillaToOff,
    ): SkinResult<SkinRegistryDocument> {
        val activation = current.activation
        if (!validToken(closure.proof.binding.value) || activation.rotationInterlock.state != InterlockState.CLEAR ||
            activation.snapshot() != closure.expected || activation.mode != SkinMode.ROTATE
        ) {
            return error(SkinImportCode.REGISTRY_CONFLICT, "Verified vanilla closure is stale or inapplicable")
        }
        return SkinResult.Ok(current.copy(activation = activation.copy(mode = SkinMode.OFF)))
    }

    private fun closeVerifiedTransaction(
        current: SkinRegistryDocument,
        closure: RegistryActivationClosure.VerifiedTransaction,
    ): SkinResult<SkinRegistryDocument> {
        val interlock = current.activation.rotationInterlock
        val authorizedClosure = closure.closure == closure.expectedArmed.prior ||
            closure.closure == closure.expectedArmed.target
        if (interlock.state != InterlockState.ARMED || interlock != closure.expectedArmed ||
            current.activation.snapshot() != interlock.prior || !authorizedClosure
        ) {
            return error(SkinImportCode.REGISTRY_CONFLICT, "Verified transaction closure is stale or unauthorized")
        }
        val completed = SkinActivation(
            mode = closure.closure.mode,
            selectedPackId = closure.closure.selectedPackId,
            active = closure.closure.active,
            skinStamp = closure.closure.skinStamp,
            rotationInterlock = RotationInterlock.clear(),
        )
        return SkinResult.Ok(current.copy(activation = completed))
    }

    private fun readiness(current: SkinRegistryDocument): SkinResult.Error? =
        registryCorruption(current) ?: if (current.activation.rotationInterlock.state != InterlockState.CLEAR) {
            error(SkinImportCode.REGISTRY_CONFLICT, "Registry metadata is blocked by the activation interlock")
        } else {
            null
        }

    private fun registryCorruption(current: SkinRegistryDocument): SkinResult.Error? =
        when (val validated = SkinRegistryDocumentCodec.canonical(current)) {
            is SkinResult.Ok -> null
            is SkinResult.Error -> error(SkinImportCode.REGISTRY_CORRUPT, validated.detail)
        }

    private fun validatePublished(published: PublishedSkin): SkinResult.Error? {
        if (!ID.matches(published.id) || !DIGEST.matches(published.candidateKey) ||
            !DIGEST.matches(published.treeSha256) || !DIGEST.matches(published.contentSha256) ||
            !DIGEST.matches(published.importReceiptSha256) || !DIGEST.matches(published.manifestSha256) ||
            !validDisplay(published.name)
        ) {
            return error(SkinImportCode.INVALID_INPUT, "Published skin identity is invalid")
        }
        return null
    }

    private fun validDisplay(value: String): Boolean =
        value.codePointCount(0, value.length) in 1..80 && value == value.trim() &&
            Normalizer.normalize(value, Normalizer.Form.NFKC) == value && !hasUnpairedSurrogate(value) &&
            value.none { it.isISOControl() || it in BIDI_CONTROLS || it == '/' || it == '\\' }

    private fun validToken(value: String): Boolean =
        value.codePointCount(0, value.length) in 1..256 && value == value.trim() &&
            Normalizer.normalize(value, Normalizer.Form.NFKC) == value && !hasUnpairedSurrogate(value) &&
            value.none { it.isISOControl() || it in BIDI_CONTROLS }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            when {
                value[index].isHighSurrogate() -> {
                    if (index + 1 >= value.length || !value[index + 1].isLowSurrogate()) return true
                    index += 2
                }
                value[index].isLowSurrogate() -> return true
                else -> index++
            }
        }
        return false
    }

    private fun mutation(
        action: (SkinRegistryDocument) -> SkinResult<SkinRegistryDocument>,
    ): RegistryMutation = AuthorizedRegistryMutation(action)

    private fun error(code: SkinImportCode, detail: String) = SkinResult.Error(code, detail)

    private companion object {
        val DIGEST = Regex("[0-9a-f]{64}")
        val ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
        val BIDI_CONTROLS = setOf(
            '؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮',
            '⁦', '⁧', '⁨', '⁩',
        )
    }
}
