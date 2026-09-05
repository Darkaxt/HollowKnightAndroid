package dev.silksong.launcher.skins.registry

enum class SkinMode {
    OFF,
    ON,
    ROTATE,
}

@JvmInline
value class SkinBindingToken(val value: String)

sealed interface ActiveVisual {
    data object Vanilla : ActiveVisual

    data class Pack(
        val id: String,
        val treeSha256: String,
        val contentSha256: String,
        val importReceiptSha256: String,
    ) : ActiveVisual
}

sealed interface VerifiedLiveVisualProof {
    val binding: SkinBindingToken

    data class Vanilla(
        override val binding: SkinBindingToken,
    ) : VerifiedLiveVisualProof

    data class Pack(
        override val binding: SkinBindingToken,
        val visual: ActiveVisual.Pack,
    ) : VerifiedLiveVisualProof
}

enum class InterlockState {
    CLEAR,
    ARMED,
    ROLLBACK_FAILED,
}

enum class SkinOperationKind {
    STARTUP_APPLY,
    MODE_ON,
    MODE_OFF,
    DEATH_ROTATION,
    REBIND_APPLY,
}

data class ActivationSnapshot(
    val mode: SkinMode,
    val selectedPackId: String?,
    val active: ActiveVisual,
    val skinStamp: Long,
)

data class RotationInterlock(
    val state: InterlockState,
    val transactionId: String?,
    val operation: SkinOperationKind?,
    val baseGenerationId: String?,
    val baseGenerationSha256: String?,
    val prior: ActivationSnapshot?,
    val target: ActivationSnapshot?,
    val bindingToken: SkinBindingToken?,
    val priorEstablishedOnBinding: Boolean?,
    val originalFailure: String?,
    val rollbackFailure: String?,
) {
    companion object {
        internal fun clear() = RotationInterlock(
            state = InterlockState.CLEAR,
            transactionId = null,
            operation = null,
            baseGenerationId = null,
            baseGenerationSha256 = null,
            prior = null,
            target = null,
            bindingToken = null,
            priorEstablishedOnBinding = null,
            originalFailure = null,
            rollbackFailure = null,
        )
    }
}

data class SkinActivation(
    val mode: SkinMode,
    val selectedPackId: String?,
    val active: ActiveVisual,
    val skinStamp: Long,
    val rotationInterlock: RotationInterlock,
) {
    internal fun snapshot() = ActivationSnapshot(mode, selectedPackId, active, skinStamp)
}
