package dev.silksong.launcher

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import java.io.File
import java.io.IOException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

@RunWith(RobolectricTestRunner::class)
class ModStateMutationTest {

    @get:Rule
    val temp = TemporaryFolder()

    @Test
    fun `concurrent profile toggles are serialized and both values survive atomic publication`() {
        val state = temp.newFolder("serialized-profile")
        val workerScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        try {
            val coordinator = DisabledStateMutationCoordinator(workerScope)
            runBlocking {
                coordinator.submit(state, "First.dll", enabled = false).await().getOrThrow()
                val first = coordinator.submit(state, "Second.dll", enabled = false)
                val second = coordinator.submit(state, "nested/Third.dll", enabled = false)
                first.await().getOrThrow()
                second.await().getOrThrow()
            }

            assertEquals(
                setOf("First.dll", "Second.dll", "nested/Third.dll"),
                Mods.disabled(state),
            )
            assertEquals(
                "First.dll\nSecond.dll\nnested/Third.dll\n",
                File(state, "disabled.txt").readText(),
            )
            assertFalse(File(state, ".disabled.txt.part").exists())
        } finally {
            workerScope.cancel()
        }
    }

    @Test
    fun `pending mutation blocks navigation until its live callback completes`() {
        val state = temp.newFolder("navigation-profile")
        val started = CountDownLatch(1)
        val release = CountDownLatch(1)
        val callback = CountDownLatch(1)
        val workerScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val callbackScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val coordinator = DisabledStateMutationCoordinator(workerScope) { profile, relative, enabled ->
                started.countDown()
                check(release.await(5, TimeUnit.SECONDS))
                Mods.setEnabled(profile, relative, enabled)
            }
            val session = ModStateMutationSession(
                coordinator,
                callbackScope,
                onPendingChanged = {},
                onCompleted = { _, _, result ->
                    result.getOrThrow()
                    callback.countDown()
                },
            )

            val accepted = session.submit(state, "First.dll", enabled = false)
            assertTrue(started.await(5, TimeUnit.SECONDS))
            assertFalse(session.canNavigate())
            var navigated = false
            if (session.canNavigate()) navigated = true
            assertFalse(navigated)

            release.countDown()
            runBlocking { accepted.await().getOrThrow() }
            assertTrue(callback.await(5, TimeUnit.SECONDS))
            assertTrue(session.canNavigate())
            assertEquals(setOf("First.dll"), Mods.disabled(state))
        } finally {
            release.countDown()
            callbackScope.cancel()
            workerScope.cancel()
        }
    }

    @Test
    fun `lifecycle callback cancellation does not cancel accepted persistence`() {
        val state = temp.newFolder("lifecycle-profile")
        val started = CountDownLatch(1)
        val release = CountDownLatch(1)
        val callbackCalled = AtomicBoolean(false)
        val workerScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val activityScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val coordinator = DisabledStateMutationCoordinator(workerScope) { profile, relative, enabled ->
                started.countDown()
                check(release.await(5, TimeUnit.SECONDS))
                Mods.setEnabled(profile, relative, enabled)
            }
            val session = ModStateMutationSession(
                coordinator,
                activityScope,
                onPendingChanged = {},
                onCompleted = { _, _, _ -> callbackCalled.set(true) },
            )

            val accepted = session.submit(state, "Survives.dll", enabled = false)
            assertTrue(started.await(5, TimeUnit.SECONDS))
            assertFalse(session.canNavigate())
            activityScope.cancel()
            release.countDown()

            runBlocking { accepted.await().getOrThrow() }
            assertEquals(setOf("Survives.dll"), Mods.disabled(state))
            assertFalse(callbackCalled.get())
        } finally {
            release.countDown()
            activityScope.cancel()
            workerScope.cancel()
        }
    }

    @Test
    fun `write failure reaches live callback and releases navigation gate`() {
        val state = temp.newFolder("failure-profile")
        val callback = CountDownLatch(1)
        val failure = AtomicReference<Throwable?>()
        val workerScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val callbackScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val coordinator = DisabledStateMutationCoordinator(workerScope) { _, _, _ ->
                throw IOException("write failed")
            }
            val session = ModStateMutationSession(
                coordinator,
                callbackScope,
                onPendingChanged = {},
                onCompleted = { _, _, result ->
                    failure.set(result.exceptionOrNull())
                    callback.countDown()
                },
            )

            session.submit(state, "Fails.dll", enabled = false)
            assertTrue(callback.await(5, TimeUnit.SECONDS))
            assertTrue(failure.get() is IOException)
            assertTrue(session.canNavigate())
            assertTrue(Mods.disabled(state).isEmpty())
        } finally {
            callbackScope.cancel()
            workerScope.cancel()
        }
    }
}
