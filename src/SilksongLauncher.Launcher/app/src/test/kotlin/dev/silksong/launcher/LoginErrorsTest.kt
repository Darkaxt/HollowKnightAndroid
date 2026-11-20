package dev.silksong.launcher

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test
import java.util.concurrent.CancellationException
import java.util.concurrent.CompletionException
import java.util.concurrent.ExecutionException

/** #33: sign-in failures never reach the status line as a bare class name. */
class LoginErrorsTest {

    @Test
    fun `a dropped connection has no readable reason`() {
        // What pollingWaitForResult().get() threw on the Thor: a message-less cancellation.
        val ended = QrAuth.SessionEnded("detail", CancellationException())
        assertNull(LoginErrors.userReason(ended))
        assertNull(LoginErrors.userReason(ExecutionException(CancellationException())))
    }

    @Test
    fun `future wrappers are peeled to the real failure`() {
        val real = IllegalStateException("Failed to poll status")
        val wrapped = QrAuth.SessionEnded("d", ExecutionException(CompletionException(real)))
        assertSame(real, LoginErrors.root(wrapped))
        assertEquals("Failed to poll status", LoginErrors.userReason(wrapped))
    }

    @Test
    fun `blank messages fall back to the caller's text, not the class name`() {
        assertNull(LoginErrors.userReason(RuntimeException("  ")))
        assertNull(LoginErrors.userReason(RuntimeException()))
    }

    @Test
    fun `the log detail names the root type`() {
        val d = LoginErrors.describe(ExecutionException(CancellationException()))
        assertEquals("java.util.concurrent.CancellationException: (no message)", d)
        assertFalse(LoginErrors.describe(RuntimeException("x")).contains("ExecutionException"))
    }
}
