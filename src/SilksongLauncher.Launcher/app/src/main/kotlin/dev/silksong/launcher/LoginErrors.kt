// LoginErrors — turns sign-in failures into log detail and status-line text (#33).
//
// JavaSteam reports failures through futures, so the useful exception is often
// wrapped (ExecutionException, CompletionException) and, when the connection
// drops, is a CancellationException with no message at all. The status line
// must never fall back to an exception's class name.

package dev.silksong.launcher

import java.util.concurrent.CancellationException
import java.util.concurrent.CompletionException
import java.util.concurrent.ExecutionException

internal object LoginErrors {

    /** The innermost meaningful exception: future wrappers and [QrAuth.SessionEnded] peeled off. */
    fun root(t: Throwable): Throwable {
        var e = t
        while (true) {
            val next = e.cause ?: return e
            if (e is ExecutionException || e is CompletionException || e is QrAuth.SessionEnded) e = next else return e
        }
    }

    /** Log detail: the root exception's full type and message. */
    fun describe(t: Throwable): String {
        val r = root(t)
        return r.javaClass.name + ": " + (r.message ?: "(no message)")
    }

    /**
     * Status-line reason, or null when there is nothing readable to show (the
     * caller then shows its own generic text). Cancellation means the
     * connection went away, which has no message worth showing.
     */
    fun userReason(t: Throwable): String? {
        val r = root(t)
        if (r is CancellationException) return null
        val m = r.message?.trim()
        return if (m.isNullOrEmpty()) null else m
    }
}
