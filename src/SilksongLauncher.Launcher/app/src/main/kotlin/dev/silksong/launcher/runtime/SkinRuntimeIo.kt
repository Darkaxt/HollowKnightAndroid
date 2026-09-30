package dev.silksong.launcher.runtime

import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.Future

/** Five bounded JNI operation lanes, not a frame poller. File/JSON/hash work stays off Unity.
 * A completed boolean is returned only after the durable writer returned; busy/timeout is false.
 * A newer key retires an unconsumed completion, never an in-flight writer. */
internal class SkinRuntimeIo(private val now: () -> Long = System::nanoTime) {
    private class Job(val key: String, val started: Long, val pending: Any?) {
        @Volatile var done = false
        @Volatile var running = false
        @Volatile var cancelled = false
        var value: Any? = null
        var future: Future<*>? = null
    }
    private val jobs = arrayOfNulls<Job>(5)
    private var closed = false
    private val executor = ThreadPoolExecutor(1, 1, 0L, TimeUnit.MILLISECONDS, LinkedBlockingQueue<Runnable>(),
        java.util.concurrent.ThreadFactory { work -> Thread(work, "skin-library-io").apply { isDaemon = true } })
    @Synchronized fun <T> poll(lane: Int, key: String, pending: T, action: () -> T): T {
        require(lane in jobs.indices && key.length <= 4096)
        if (closed) return pending
        val tick = now()
        // Native mutations are one-shot lane2 calls; only lane1 may be polled afterwards.
        // Retire all fixed lanes on that existing operation retry, never via a healthy timer.
        for (job in jobs) if (job != null && !job.done && tick - job.started >= TIMEOUT_NANOS) {
            job.cancelled = true
            job.future?.cancel(true)
            val queued = job.future as? Runnable
            if (queued != null) executor.remove(queued) // cancellation alone leaves FutureTasks retained in the queue
            if (!job.running) { job.value = job.pending; job.done = true }
        }
        val existing = jobs[lane]
        if (existing != null) {
            if (!existing.done) return pending
            jobs[lane] = null
            if (existing.key == key) {
                @Suppress("UNCHECKED_CAST")
                return existing.value as T
            }
        }
        val job = Job(key, tick, pending)
        jobs[lane] = job
        job.future = executor.submit {
            synchronized(this) {
                if (job.cancelled) { job.value = pending; job.done = true; return@submit }
                job.running = true
            }
            publicationScope.set { !job.cancelled && now() - job.started < TIMEOUT_NANOS }
            try { job.value = action() }
            catch (_: Exception) { job.value = pending }
            finally {
                publicationScope.remove()
                if (job.cancelled || now() - job.started >= TIMEOUT_NANOS) job.value = pending
                job.done = true
            }
        }
        return pending
    }
    @Synchronized fun close() {
        if (closed) return
        closed = true
        jobs.forEach { it?.cancelled = true }
        executor.shutdownNow()
        jobs.fill(null)
    }
    companion object {
        const val TIMEOUT_NANOS = 30_000_000_000L
        private val publicationScope = ThreadLocal<() -> Boolean>()
        // Launcher writers have no runtime job. Runtime writers retain cancellation even if an
        // I/O provider clears interruption; checked only at existing durable publication barriers.
        internal fun mayPublish(): Boolean = publicationScope.get()?.invoke() ?: true
    }
}
