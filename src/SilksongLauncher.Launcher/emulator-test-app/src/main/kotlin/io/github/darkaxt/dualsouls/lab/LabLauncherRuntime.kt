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

class LabLauncherRuntime : LauncherRuntime {
    companion object {
        const val CONTROL_PREFS = "lab_runtime.controls"
        const val STATE_PREFS = "lab_runtime.state"
        const val PROFILE_ID_EXTRA = "io.github.darkaxt.dualsouls.lab.PROFILE_ID"
        const val GENERATION_ID_EXTRA = "io.github.darkaxt.dualsouls.lab.GENERATION_ID"

        fun failureKey(profileId: String): String = "fail-before-publish.$profileId"

        fun recordCleanExit(context: Context, profileId: String, generationId: String) {
            check(
                context.getSharedPreferences(STATE_PREFS, Context.MODE_PRIVATE)
                    .edit()
                    .putString("clean-exit.$profileId", generationId)
                    .commit(),
            ) { "Could not persist the lab game clean-exit marker" }
        }

        fun lastCleanExit(context: Context, profileId: String): String? =
            context.getSharedPreferences(STATE_PREFS, Context.MODE_PRIVATE)
                .getString("clean-exit.$profileId", null)
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
        return publisher.clearStaged() + publisher.clearPublished()
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
