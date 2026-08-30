package io.github.darkaxt.dualsouls.lab

import android.content.Context
import android.content.Intent
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.runtime.EvidenceKind
import dev.silksong.launcher.runtime.LauncherRuntime
import dev.silksong.launcher.runtime.ProvisionRequest
import dev.silksong.launcher.runtime.ProvisionSource
import dev.silksong.launcher.runtime.RuntimeProgress
import dev.silksong.launcher.runtime.RuntimeRequest
import dev.silksong.launcher.runtime.RuntimeState
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption

class LabLauncherRuntime : LauncherRuntime {
    companion object {
        const val CONTROL_PREFS = "lab_runtime.controls"
        const val STATE_PREFS = "lab_runtime.state"
        const val PROFILE_ID_EXTRA = "io.github.darkaxt.dualsouls.lab.PROFILE_ID"
        const val GENERATION_ID_EXTRA = "io.github.darkaxt.dualsouls.lab.GENERATION_ID"

        fun failureKey(profileId: String): String = "fail-before-publish.$profileId"

        fun recordCleanExit(context: Context, profileId: String, generationId: String) {
            val marker = cleanExitFile(context, profileId)
            marker.parentFile?.mkdirs()
            val pending = File(marker.parentFile, "${marker.name}.next")
            pending.writeText(generationId)
            Files.move(
                pending.toPath(),
                marker.toPath(),
                StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING,
            )
        }

        fun lastCleanExit(context: Context, profileId: String): String? =
            cleanExitFile(context, profileId).takeIf(File::isFile)?.readText()

        fun clearCleanExit(context: Context, profileId: String): Boolean =
            cleanExitFile(context, profileId).delete()

        private fun cleanExitFile(context: Context, profileId: String): File {
            require(profileId.matches(Regex("[a-z0-9-]+"))) { "Invalid lab profile ID" }
            return File(context.filesDir, "lab-runtime/clean-exits/$profileId")
        }
    }

    override val evidenceKind = EvidenceKind.EMULATOR_FAKE

    override fun inspect(request: RuntimeRequest): RuntimeState {
        val current = runCatching { GenerationPublisher(request.paths.profilePaths).current() }.getOrNull()
        return RuntimeState(
            ready = current != null,
            generationId = current?.id,
            detail = if (current == null) {
                "${request.profile.displayName} needs a synthetic generation"
            } else {
                "${request.profile.displayName} lab generation ${current.id} is ready"
            },
        )
    }

    override suspend fun provision(
        request: ProvisionRequest,
        emit: suspend (RuntimeProgress) -> Unit,
    ): RuntimeState {
        require(request.source is ProvisionSource.Synthetic) {
            "The emulator fake runtime accepts synthetic content only"
        }
        val runtime = request.runtime
        val context = runtime.context
        val state = context.getSharedPreferences(STATE_PREFS, Context.MODE_PRIVATE)
        val sequenceKey = "sequence.${runtime.profile.id}"
        val sequence = state.getInt(sequenceKey, 0) + 1
        check(state.edit().putInt(sequenceKey, sequence).commit()) {
            "Could not persist lab generation sequence"
        }
        val generationId = "lab-${runtime.profile.runtimeStorageKey}-$sequence"
        val jobId = "job-${runtime.profile.runtimeStorageKey}-$sequence"
        val publisher = GenerationPublisher(runtime.paths.profilePaths)
        emit(RuntimeProgress("synthetic", 0.25f, "Creating $generationId"))
        val staging = publisher.begin(jobId, generationId)
        File(staging, "generation.json").writeText(
            """{"profileId":"${runtime.profile.id}","generationId":"$generationId"}""",
        )
        File(staging, "synthetic-content.txt").writeText(
            "Dual Souls emulator fixture for ${runtime.profile.id}\n",
        )

        val controls = context.getSharedPreferences(CONTROL_PREFS, Context.MODE_PRIVATE)
        check(!controls.getBoolean(failureKey(runtime.profile.id), false)) {
            "Injected lab failure before publication"
        }
        emit(RuntimeProgress("synthetic", 0.75f, "Publishing $generationId"))
        val installed = publisher.publish(jobId, generationId)
        emit(RuntimeProgress("synthetic", 1f, "Published ${installed.id}"))
        return RuntimeState(true, installed.id, "${runtime.profile.displayName} is ready in the test runtime")
    }

    override fun reset(request: RuntimeRequest): Long {
        val publisher = GenerationPublisher(request.paths.profilePaths)
        val exitBytes = cleanExitFile(request.context, request.profile.id)
            .takeIf(File::isFile)?.length() ?: 0L
        clearCleanExit(request.context, request.profile.id)
        return publisher.clearStaged() + publisher.clearPublished() + exitBytes
    }

    override fun gameIntent(request: RuntimeRequest): Intent {
        val current = GenerationPublisher(request.paths.profilePaths).current()
            ?: throw IllegalStateException("No synthetic generation for ${request.profile.id}")
        return Intent(request.context, LabGameActivity::class.java).apply {
            putExtra(PROFILE_ID_EXTRA, request.profile.id)
            putExtra(GENERATION_ID_EXTRA, current.id)
        }
    }
}
