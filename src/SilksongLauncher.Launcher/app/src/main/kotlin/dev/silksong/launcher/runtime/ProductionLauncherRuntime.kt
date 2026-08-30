package dev.silksong.launcher.runtime

import android.content.Intent
import dev.silksong.launcher.BuildReset
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
            )
        }
        val published = publishedResult.getOrNull()
        val packageDir = published?.let { File(it.root, "pkg") } ?: request.paths.packageDir
        val built = File(packageDir, ".built").isFile &&
            File(packageDir, "lib/arm64/libil2cpp.so").length() > 0L &&
            File(packageDir, "data.apk").length() > 0L
        return RuntimeState(
            ready = built,
            generationId = published?.id,
            detail = if (built) {
                "Production generation ${published?.id ?: "legacy"} is present"
            } else {
                "Production build is not ready"
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
