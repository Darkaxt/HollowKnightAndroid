package dev.silksong.launcher.build

import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext

sealed interface BuildStage {
    data object ValidateSource : BuildStage
    data object ResolveToolchain : BuildStage
    data object CompilePatches : BuildStage
    data object ConvertIl2Cpp : BuildStage
    data object CompileNative : BuildStage
    data object ConvertContent : BuildStage
    data object Verify : BuildStage
    data object Publish : BuildStage

    companion object {
        val executionOrder: List<BuildStage> = listOf(
            ValidateSource,
            ResolveToolchain,
            CompilePatches,
            ConvertIl2Cpp,
            CompileNative,
            ConvertContent,
            Verify,
            Publish,
        )
    }
}

data class BuildWorkspace(
    val root: File,
    val packageDir: File = File(root, "pkg"),
) {
    val engineDir: File = File(packageDir, "lib/arm64")
}

data class ProfileBuildRequest(
    val jobId: String,
    val generationId: String,
    val metadata: GenerationMetadata,
)

data class BuildProgress(
    val stage: BuildStage,
    val index: Int,
    val count: Int,
)

class ProfileBuildCoordinator(
    private val publisher: GenerationPublisher,
) {
    suspend fun run(
        request: ProfileBuildRequest,
        emit: suspend (BuildProgress) -> Unit = {},
        execute: suspend (BuildStage, BuildWorkspace) -> Unit,
    ): InstalledGeneration {
        val staged = publisher.begin(request.jobId, request.generationId)
        val workspace = BuildWorkspace(staged)
        try {
            for ((index, stage) in BuildStage.executionOrder.withIndex()) {
                currentCoroutineContext().ensureActive()
                emit(BuildProgress(stage, index + 1, BuildStage.executionOrder.size))
                execute(stage, workspace)
                when (stage) {
                    BuildStage.Verify -> {
                        val coroutineContext = currentCoroutineContext()
                        withContext(Dispatchers.IO) {
                            publisher.finalizeGeneration(
                                request.jobId,
                                request.generationId,
                                request.metadata,
                            ) { coroutineContext.ensureActive() }
                        }
                    }
                    BuildStage.Publish -> return withContext(NonCancellable + Dispatchers.IO) {
                        publisher.publish(request.jobId, request.generationId)
                    }
                    else -> Unit
                }
            }
            error("Build stage sequence did not publish")
        } catch (t: Throwable) {
            publisher.discard(request.jobId)
            throw t
        }
    }
}
