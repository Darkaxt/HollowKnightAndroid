package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import java.util.Collections
import java.text.Normalizer

/** Descriptor-supplied membership/current object, not a catalog or live-visual proof. */
data class RotationDescriptor(val name: String, val currentObject: ActiveVisual.Pack, val eligible: Boolean)
class RotationRing private constructor(entries: List<RotationDescriptor>) {
    val entries: List<RotationDescriptor> = Collections.unmodifiableList(entries.toList())
    companion object {
        fun tryCreate(entries: List<RotationDescriptor>): RotationRing? {
            if (entries.size > 64) return null
            val snapshot = entries.toList()
            if (snapshot.any { !RotationValues.descriptor(it) } ||
                snapshot.map { it.currentObject.id }.distinct().size != snapshot.size) return null
            return RotationRing(snapshot.sortedWith { a, b ->
                val name = RotationValues.utf8Compare(a.name, b.name)
                if (name != 0) name else a.currentObject.id.compareTo(b.currentObject.id)
            })
        }
    }
}
enum class RotationPendingPhase { AWAITING_STABILITY, INTENT_ISSUED }
/** Readiness value only: grants no Prepare/Arm/Apply or publication authority. */
data class RotationReadyIntent(val epoch: DeathEpoch, val hero: HeroBindingToken, val skin: SkinBindingToken,
    val candidate: RotationDescriptor, val prior: ActivationSnapshot)
data class PendingRotation(val candidate: RotationDescriptor, val epoch: DeathEpoch,
    val hero: HeroBindingToken, val skin: SkinBindingToken,
    val phase: RotationPendingPhase = RotationPendingPhase.AWAITING_STABILITY,
    val issuedIntent: RotationReadyIntent? = null)
/** Activation is supplied history. This slice never changes modes or establishes live proof. */
data class RotationState(val ring: RotationRing, val activation: ActivationSnapshot,
    val currentHero: HeroBindingToken? = null, val currentSkin: SkinBindingToken? = null,
    val epochHighWater: DeathEpoch = DeathEpoch(0uL), val pending: PendingRotation? = null)
sealed interface RotationEvent {
    data class Rebind(val hero: HeroBindingToken, val skin: SkinBindingToken) : RotationEvent
    data class ConfirmDeath(val epoch: DeathEpoch, val hero: HeroBindingToken, val skin: SkinBindingToken) : RotationEvent
    data class StableRespawn(val token: StableRespawnToken) : RotationEvent
}
internal object RotationValues {
    private val id = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    private val hash = Regex("[0-9a-f]{64}")
    fun packId(value: String) = value.length <= 64 && id.matches(value)
    fun pack(value: ActiveVisual.Pack) = packId(value.id) &&
        hash.matches(value.treeSha256) && hash.matches(value.contentSha256) && hash.matches(value.importReceiptSha256)
    // Same scalar/name restrictions as CanonicalJson.requireDisplayName and descriptor validation.
    fun text(value: String, maximum: Int): Boolean {
        if (value.isEmpty() || value.length > maximum * 2 || value != value.trim()) return false
        var i = 0; var count = 0
        while (i < value.length) {
            val c = value[i]
            if (c.isISOControl() || c == '؜' || c == '‎' || c == '‏' || c in '‪'..'‮' || c in '⁦'..'⁩') return false
            if (c.isHighSurrogate()) { i++; if (i >= value.length || !value[i].isLowSurrogate()) return false }
            else if (c.isLowSurrogate()) return false
            i++; count++
        }
        return count <= maximum && Normalizer.isNormalized(value, Normalizer.Form.NFKC)
    }
    fun descriptor(value: RotationDescriptor) = text(value.name, 80) && '/' !in value.name && '\\' !in value.name && pack(value.currentObject)
    fun activation(value: ActivationSnapshot) = value.skinStamp >= 0 &&
        (value.selectedPackId == null || packId(value.selectedPackId)) &&
        (value.active is ActiveVisual.Vanilla || value.active is ActiveVisual.Pack && pack(value.active))
    fun utf8Compare(a: String, b: String): Int {
        val left = a.toByteArray(Charsets.UTF_8); val right = b.toByteArray(Charsets.UTF_8)
        for (i in 0 until minOf(left.size, right.size)) {
            val difference = (left[i].toInt() and 255) - (right[i].toInt() and 255)
            if (difference != 0) return difference
        }
        return left.size.compareTo(right.size)
    }
}

/** Fresh trusted caller verification, qualified by both authorities; never derived from history. */
data class HeroVerifiedVisual(val hero: HeroBindingToken, val proof: VerifiedLiveVisualProof)
data class ModeCorrelation(val operationId: String, val hero: HeroBindingToken, val skin: SkinBindingToken)
enum class ModeCommitPhase { ISSUED, INDETERMINATE, DEFINITIVE_FAILURE }
data class ModeOperation(val correlation: ModeCorrelation, val baseHead: VerifiedRegistryHead,
    val target: ActivationSnapshot, val vanillaProof: HeroVerifiedVisual? = null,
    val phase: ModeCommitPhase = ModeCommitPhase.ISSUED, val reboundBlocked: Boolean = false,
    val selectedProof: HeroVerifiedVisual? = null)
/** H1 owns only mode-only CAS. Visual transactions and rebound resolution belong to task66. */
data class RotationModeState(val rotation: RotationState, val head: VerifiedRegistryHead,
    val liveProof: HeroVerifiedVisual? = null, val operationHighWater: Long = 0,
    val operation: ModeOperation? = null, val offRequested: Boolean = false,
    val canceledPending: PendingRotation? = null)
sealed interface RotationModeEvent {
    data object AdvanceMode : RotationModeEvent
    data class VerifiedVisual(val value: HeroVerifiedVisual) : RotationModeEvent
    data class Selector(val event: RotationEvent) : RotationModeEvent
    data class ModeCommitted(val correlation: ModeCorrelation, val receipt: RegistryCommitReceipt,
        val head: VerifiedRegistryHead) : RotationModeEvent
    data class ModeIndeterminate(val correlation: ModeCorrelation) : RotationModeEvent
    data class ModeFailed(val correlation: ModeCorrelation) : RotationModeEvent
    /** Re-emits the identical CAS value, never changes its parent or clears uncertainty. */
    data class RetryModeCommit(val correlation: ModeCorrelation) : RotationModeEvent
}
sealed interface RotationModeCommand {
    val operation: ModeOperation
    data class CommitVerifiedSelectedOn(override val operation: ModeOperation) : RotationModeCommand
    data class CommitOnToRotate(override val operation: ModeOperation) : RotationModeCommand
    data class CommitVerifiedVanillaOff(override val operation: ModeOperation) : RotationModeCommand
}
class RotationModeDecision(val state: RotationModeState, val diagnosis: String,
    vararg commands: RotationModeCommand) {
    val commands: List<RotationModeCommand> = Collections.unmodifiableList(commands.toList())
}

class RotationDecision(val state: RotationState, val diagnosis: String, vararg intents: RotationReadyIntent) {
    val intents: List<RotationReadyIntent> = Collections.unmodifiableList(intents.toList())
}
