// LauncherLog — single global event log for the launcher UI. Any
// component (SteamSession, LauncherActivity itself)
// can append lines via [log]; LauncherActivity subscribes via
// [setListener] and mirrors them into the on-screen log panel.
//
// Why a manual pubsub instead of LiveData/Flow: we want this object
// reachable from anywhere (including non-Android code paths like
// background coroutines + JavaSteam callbacks) without forcing every
// caller to depend on lifecycle libraries. The total event volume is
// small (~10s of lines per session) so a CopyOnWriteArrayList is fine.

package dev.silksong.launcher

import android.util.Log
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

object LauncherLog {
    private const val TAG = "SilksongLauncher"
    private const val MAX_LINES = 500

    // Newest at the end. Read-only snapshot returned to subscribers.
    private val buffer = CopyOnWriteArrayList<String>()
    private val listeners = CopyOnWriteArrayList<Listener>()
    private val timeFormat = SimpleDateFormat("HH:mm:ss", Locale.US)

    fun interface Listener {
        fun onLog(line: String, full: List<String>)
    }

    fun log(message: String) {
        val stamped = "[${timeFormat.format(Date())}] $message"
        Log.i(TAG, message)
        buffer.add(stamped)
        // Trim if needed. CopyOnWriteArrayList is O(n) on remove but
        // n stays small (capped at 500).
        while (buffer.size > MAX_LINES) buffer.removeAt(0)
        val snapshot = buffer.toList()
        for (l in listeners) {
            try {
                l.onLog(stamped, snapshot)
            } catch (_: Throwable) {
                // Listener crashes shouldn't bring down whatever thread is logging.
            }
        }
    }

    /**
     * Logs a failure with its stack trace.
     *
     * The on-screen panel still gets one line -- it has no room for more --
     * but logcat gets the whole thing. A message alone is not enough to place
     * a failure inside a fetch that has a dozen steps and several archive
     * formats: "not in gzip format" is the same sentence wherever it came
     * from, and the frames are the only thing that says which one it was.
     */
    fun log(message: String, error: Throwable) {
        Log.w(TAG, message, error)
        log("$message: $error")
    }

    fun snapshot(): List<String> = buffer.toList()

    fun addListener(l: Listener) {
        listeners.add(l)
    }

    fun removeListener(l: Listener) {
        listeners.remove(l)
    }
}
