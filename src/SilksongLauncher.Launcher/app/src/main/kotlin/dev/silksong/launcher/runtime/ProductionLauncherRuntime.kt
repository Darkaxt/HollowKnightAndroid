package dev.silksong.launcher.runtime

import android.content.Intent
import dev.silksong.launcher.BuildReset
import java.io.File

class ProductionLauncherRuntime : LauncherRuntime {
    companion object {
        const val GAME_ACTIVITY_CLASS = "dev.silksong.shell.GameActivity"
        const val PROFILE_ID_EXTRA = "dev.silksong.launcher.PROFILE_ID"
    }

    override val evidenceKind = EvidenceKind.ARM64_DEVICE

    override fun inspect(request: RuntimeRequest): RuntimeState {
        val built = File(request.paths.packageDir, ".built").isFile &&
            File(request.paths.packageDir, "lib/arm64/libil2cpp.so").length() > 0L
        return RuntimeState(
            ready = built,
            generationId = null,
            detail = if (built) "Production build is present" else "Production build is not ready",
        )
    }

    override suspend fun provision(
        request: ProvisionRequest,
        emit: suspend (RuntimeProgress) -> Unit,
    ): RuntimeState = ProductionProvisioner().provision(request, emit)

    override fun reset(request: RuntimeRequest): Long =
        BuildReset.clear(request.context, request.paths)

    override fun gameIntent(request: RuntimeRequest): Intent =
        Intent().apply {
            setClassName(request.context.packageName, GAME_ACTIVITY_CLASS)
            putExtra(PROFILE_ID_EXTRA, request.profile.id)
        }
}
