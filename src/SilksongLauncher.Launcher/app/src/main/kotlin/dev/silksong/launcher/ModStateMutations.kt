package dev.silksong.launcher

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.launch
import java.io.File
import java.io.IOException

/** Process-owned FIFO mutation workers, one for each canonical profile state. */
internal class DisabledStateMutationCoordinator(
    private val workerScope: CoroutineScope,
    private val mutate: (File, String, Boolean) -> Unit = Mods::setEnabled,
) {
    private data class Request(
        val state: File,
        val relative: String,
        val enabled: Boolean,
        val completion: CompletableDeferred<Result<Unit>>,
    )

    private val queues = HashMap<String, Channel<Request>>()

    fun submit(state: File, relative: String, enabled: Boolean): Deferred<Result<Unit>> {
        val completion = CompletableDeferred<Result<Unit>>()
        val profileState = try {
            state.canonicalFile
        } catch (failure: Throwable) {
            completion.complete(Result.failure(failure))
            return completion
        }
        val request = Request(profileState, relative, enabled, completion)
        if (!queueFor(profileState.path).trySend(request).isSuccess) {
            completion.complete(Result.failure(IOException("profile mod-state mutation queue is unavailable")))
        }
        return completion
    }

    @Synchronized
    private fun queueFor(profileKey: String): Channel<Request> = queues.getOrPut(profileKey) {
        Channel<Request>(Channel.UNLIMITED).also { queue ->
            workerScope.launch {
                for (request in queue) {
                    val result = runCatching {
                        mutate(request.state, request.relative, request.enabled)
                    }
                    request.completion.complete(result)
                }
            }
        }
    }
}

/** Activity-owned observer state around process-owned durable mutations. */
internal class ModStateMutationSession(
    private val coordinator: DisabledStateMutationCoordinator,
    private val callbackScope: CoroutineScope,
    private val onPendingChanged: (Boolean) -> Unit,
    private val onCompleted: (String, Boolean, Result<Unit>) -> Unit,
) {
    private val lock = Any()
    private var pending = 0

    fun submit(state: File, relative: String, enabled: Boolean): Deferred<Result<Unit>> {
        val accepted = coordinator.submit(state, relative, enabled)
        val busy = synchronized(lock) {
            pending++
            pending > 0
        }
        onPendingChanged(busy)
        callbackScope.launch {
            val result = accepted.await()
            val stillBusy = synchronized(lock) {
                pending--
                pending > 0
            }
            onPendingChanged(stillBusy)
            onCompleted(relative, enabled, result)
        }
        return accepted
    }

    fun canNavigate(): Boolean = synchronized(lock) { pending == 0 }
}

/** Never tied to an Activity job: accepted writes live for the process lifetime. */
internal object DurableDisabledStateMutations {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    val coordinator = DisabledStateMutationCoordinator(scope)
}
