package dev.silksong.launcher.runtime

import android.content.Context
import android.content.Intent
import dev.silksong.launcher.TokenStore
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.ProfileBuildPaths
import java.io.File

enum class EvidenceKind {
    ARM64_DEVICE,
    EMULATOR_FAKE,
}

data class RuntimeState(
    val ready: Boolean,
    val generationId: String?,
    val detail: String,
    val condition: RuntimeCondition =
        if (ready) RuntimeCondition.READY else RuntimeCondition.NOT_CONFIGURED,
)

enum class RuntimeCondition(val label: String) {
    NOT_CONFIGURED("Not configured"),
    BUILDING("Building"),
    READY("Ready"),
    NEEDS_REPAIR("Needs repair"),
}

data class RuntimeProgress(
    val stage: String,
    val fraction: Float,
    val detail: String,
)

data class RuntimeRequest(
    val context: Context,
    val profile: GameProfile,
    val paths: ProfileBuildPaths,
)

sealed interface ProvisionSource {
    data class Local(val depot: File) : ProvisionSource

    data class Steam(
        val credentials: TokenStore.Credentials,
        val depot: File,
        val staging: File,
    ) : ProvisionSource

    data object Synthetic : ProvisionSource
}

data class ProvisionRequest(
    val runtime: RuntimeRequest,
    val source: ProvisionSource,
)

interface LauncherRuntime {
    val evidenceKind: EvidenceKind

    fun inspect(request: RuntimeRequest): RuntimeState

    suspend fun provision(
        request: ProvisionRequest,
        emit: suspend (RuntimeProgress) -> Unit,
    ): RuntimeState

    fun reset(request: RuntimeRequest): Long

    fun gameProcessName(request: RuntimeRequest): String = request.context.packageName

    fun gameIntent(request: RuntimeRequest): Intent
}
