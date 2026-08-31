package dev.silksong.launcher.runtime

import android.content.Intent
import dev.silksong.launcher.BuildReset
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.build.GenerationPublisher
import java.io.File

class ProductionLauncherRuntime : LauncherRuntime {
    companion object {
        const val GAME_ACTIVITY_CLASS = "dev.silksong.shell.GameActivity"
        const val PROFILE_ID_EXTRA = "dev.silksong.launcher.PROFILE_ID"
    }

    override val evidenceKind = EvidenceKind.ARM64_DEVICE

    override fun inspect(request: RuntimeRequest): RuntimeState {
        val publishedResult = runCatching {
            GenerationPublisher(request.paths.profilePaths).current()
        }
        if (publishedResult.isFailure && request.paths.profilePaths.currentPointer.isFile) {
            return RuntimeState(
                ready = false,
                generationId = null,
                detail = "Current production generation is invalid: " +
                    (publishedResult.exceptionOrNull()?.message ?: "unknown error"),
                condition = RuntimeCondition.NEEDS_REPAIR,
            )
        }
        val published = publishedResult.getOrNull()
        val legacyBuilt = File(request.paths.packageDir, ".built").isFile &&
            File(request.paths.packageDir, "lib/arm64/libil2cpp.so").length() > 0L &&
            PlayerImage.runtimeArchivesPresent(request.context, request.profile, request.paths.packageDir)
        val startup = if (published != null) {
            runCatching { GameProcessStartup.resolve(request.context, request.profile, request.paths) }
        } else {
            null
        }
        val built = startup?.isSuccess == true
        return RuntimeState(
            ready = built,
            generationId = published?.id,
            detail = if (built) {
                "Production generation ${published?.id} is present"
            } else if (startup?.isFailure == true) {
                "Production generation needs repair: " +
                    (startup.exceptionOrNull()?.message ?: "unknown error")
            } else {
                "Production build is not ready"
            },
            condition = when {
                built -> RuntimeCondition.READY
                startup?.isFailure == true -> RuntimeCondition.NEEDS_REPAIR
                published == null && legacyBuilt -> RuntimeCondition.NEEDS_REPAIR
                request.paths.profilePaths.staging.listFiles().orEmpty().isNotEmpty() ->
                    RuntimeCondition.BUILDING
                else -> RuntimeCondition.NOT_CONFIGURED
            },
        )
    }

    override suspend fun provision(
        request: ProvisionRequest,
        emit: suspend (RuntimeProgress) -> Unit,
    ): RuntimeState = throw UnsupportedOperationException(
        "Production setup remains owned by SetupActivity; runtime provisioning is lab-only",
    )

    override fun reset(request: RuntimeRequest): Long =
        BuildReset.clear(request.context, request.paths)

    override fun gameIntent(request: RuntimeRequest): Intent =
        Intent().apply {
            setClassName(request.context.packageName, GAME_ACTIVITY_CLASS)
            putExtra(PROFILE_ID_EXTRA, request.profile.id)
        }
}
