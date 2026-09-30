package dev.silksong.launcher.runtime

import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class SkinRuntimeIoTest {
    private fun <T> await(io: SkinRuntimeIo, lane: Int, key: String, pending: T, action: () -> T): T {
        val limit = System.nanoTime() + TimeUnit.SECONDS.toNanos(5)
        while (System.nanoTime() < limit) {
            val value = io.poll(lane, key, pending, action)
            if (value != pending) return value
            Thread.sleep(5)
        }
        error("Host skin event exceeded bounded fixture deadline")
    }
    @Test fun `JNI event never waits for IO and returns success only after completed durable action`() {
        val io = SkinRuntimeIo(); val entered = CountDownLatch(1); val release = CountDownLatch(1)
        var calls = 0; var worker = ""
        try {
            val caller = Thread.currentThread().name
            val action = { calls++; worker = Thread.currentThread().name; entered.countDown(); check(release.await(5, TimeUnit.SECONDS)); "durable" }
            assertEquals("pending", io.poll(0, "slot0", "pending", action))
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            for (poll in 0..100) assertEquals("pending", io.poll(0, "slot0", "pending", action))
            assertEquals(1, calls)
            release.countDown()
            assertEquals("durable", await(io, 0, "slot0", "pending", action))
            assertNotEquals(caller, worker)
        } finally { release.countDown(); io.close() }
    }
    @Test fun `changed save retires completed old wire and exact report callback remains once only`() {
        val io = SkinRuntimeIo(); var oldCalls = 0; var newCalls = 0; var reportCalls = 0
        try {
            io.poll(0, "old", "pending") { oldCalls++; "old-slot" }
            assertTrue(await(io, 3, "report-old", false) { reportCalls++; true })
            assertEquals("new-slot", await(io, 0, "new", "pending") { newCalls++; "new-slot" })
            assertEquals(1, oldCalls); assertEquals(1, newCalls); assertEquals(1, reportCalls)
        } finally { io.close() }
    }
    @Test fun `timeout interrupts transient action fails closed and permits later bounded recovery`() {
        var time = 0L
        val io = SkinRuntimeIo { time }; val entered = CountDownLatch(1); val stopped = CountDownLatch(1)
        try {
            assertFalse(io.poll(2, "blocked", false) {
                entered.countDown()
                try { CountDownLatch(1).await(5, TimeUnit.SECONDS); true }
                finally { stopped.countDown() }
            })
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            time = SkinRuntimeIo.TIMEOUT_NANOS
            assertFalse(io.poll(2, "blocked", false) { error("old task must not be rerun") })
            assertTrue(stopped.await(5, TimeUnit.SECONDS))
            assertTrue(await(io, 2, "new-valid", false) { true })
        } finally { io.close() }
    }
    @Test fun `settled event has no worker timer poll diagnostic or repeat action across healthy frames`() {
        var time = 0L; var clockReads = 0; var calls = 0
        val io = SkinRuntimeIo { clockReads++; time }
        try {
            assertTrue(await(io, 3, "confirmed", false) { calls++; true })
            val before = clockReads
            for (frame in 0 until 100000) time += 1_000_000_000L
            assertEquals(1, calls)
            assertEquals(before, clockReads)
        } finally { io.close() }
    }
    @Test fun `retired worker rejects new work and cannot execute a queued callback`() {
        val io = SkinRuntimeIo(); val entered = CountDownLatch(1); val release = CountDownLatch(1)
        var queuedWrites = 0
        try {
            io.poll(0, "running", false) { entered.countDown(); release.await(5, TimeUnit.SECONDS); true }
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            assertFalse(io.poll(3, "queued", false) { queuedWrites++; true })
            io.close()
            assertFalse(io.poll(3, "after-close", false) { queuedWrites++; true })
            assertEquals(0, queuedWrites)
        } finally { release.countDown(); io.close() }
    }
    @Test fun `publication deadline fences a one shot writer even when its menu closes without another poll`() {
        var time = 0L
        val io = SkinRuntimeIo { time }
        val entered = CountDownLatch(1); val release = CountDownLatch(1); val finished = CountDownLatch(1)
        var allowed = true
        try {
            assertFalse(io.poll(2, "one-shot", false) {
                entered.countDown(); check(release.await(5, TimeUnit.SECONDS))
                allowed = SkinRuntimeIo.mayPublish(); finished.countDown(); true
            })
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            time = SkinRuntimeIo.TIMEOUT_NANOS
            release.countDown() // no subsequent call into ANY lane before the publication fence
            assertTrue(finished.await(5, TimeUnit.SECONDS))
            assertFalse(allowed)
            assertTrue(await(io, 2, "later-valid", false) { SkinRuntimeIo.mayPublish() })
            assertTrue(SkinRuntimeIo.mayPublish()) // scope is confined to the executing worker
        } finally { release.countDown(); io.close() }
    }
    @Test fun `repeated queued retirement keeps the existing five lane submission bound while a writer ignores interruption`() {
        val clock = java.util.concurrent.atomic.AtomicLong(0)
        val io = SkinRuntimeIo { clock.get() }
        val entered = CountDownLatch(1); val release = CountDownLatch(1)
        val writes = java.util.concurrent.atomic.AtomicInteger(0)
        // Host-only inspection of executor bookkeeping, never a production diagnostic poll.
        val field = SkinRuntimeIo::class.java.getDeclaredField("executor").apply { isAccessible = true }
        val executor = field.get(io) as java.util.concurrent.ThreadPoolExecutor
        try {
            io.poll(0, "blocker", false) {
                entered.countDown()
                while (true) {
                    try { if (release.await(5, TimeUnit.SECONDS)) break else error("bounded queue fixture expired") }
                    catch (_: InterruptedException) { /* emulate an uninterruptible transient provider */ }
                }
                true
            }
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            for (operation in 0 until 256) {
                clock.addAndGet(SkinRuntimeIo.TIMEOUT_NANOS)
                io.poll(1, "snapshot-$operation", false) { writes.incrementAndGet(); true }
            }
            clock.addAndGet(SkinRuntimeIo.TIMEOUT_NANOS)
            io.poll(4, "terminal-window", false) { true } // retire the final queued snapshot too
            assertTrue("retired submissions retained=${executor.queue.size}", executor.queue.size <= 4)
            release.countDown()
            assertTrue(await(io, 1, "later-valid", false) { writes.incrementAndGet(); true })
            assertEquals(1, writes.get()) // queued retired callbacks cannot run; only the new valid one does
        } finally { release.countDown(); io.close() }
    }

    @Test fun `queued timeout cancels without running obsolete slot writer`() {
        var time = 0L
        val io = SkinRuntimeIo { time }; val entered = CountDownLatch(1); val release = CountDownLatch(1)
        var obsoleteWrites = 0
        try {
            io.poll(0, "busy", false) { entered.countDown(); check(release.await(5, TimeUnit.SECONDS)); true }
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            assertFalse(io.poll(3, "obsolete-slot", false) { obsoleteWrites++; true })
            time = SkinRuntimeIo.TIMEOUT_NANOS
            assertFalse(io.poll(3, "obsolete-slot", false) { obsoleteWrites++; true })
            release.countDown()
            assertTrue(await(io, 3, "new-slot", false) { true })
            assertEquals(0, obsoleteWrites)
        } finally { release.countDown(); io.close() }
    }
}
