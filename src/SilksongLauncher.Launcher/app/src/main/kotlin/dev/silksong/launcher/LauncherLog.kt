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

import android.content.Context
import android.util.Log
import java.io.File
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

    // ── the file ────────────────────────────────────────────────────────────
    //
    // The buffer above is memory, and memory is exactly what a bug report does
    // not have: the interesting failures happen during setup, the user closes
    // the app, and by the time anybody asks for the log it is gone. So every
    // line is also appended to a file, and the file is what the log screen
    // shows -- including the session that failed yesterday.
    //
    // Appended and closed per line rather than held open behind a buffer. The
    // whole point is to survive the process dying, and a buffered writer's
    // last few kilobytes are the ones that say why it died.

    private const val MAX_FILE_BYTES = 256L * 1024L
    private val fileLock = Any()
    @Volatile private var sink: File? = null

    /** Where the log lives, once [attach] has been called. */
    fun file(context: Context): File = File(context.filesDir, "launcher.log")

    /**
     * Starts writing to disk. Safe to call more than once and from any process.
     */
    fun attach(context: Context) {
        synchronized(fileLock) {
            if (sink != null) return
            val f = file(context)
            try {
                // Trimmed on the way in rather than on every write: this runs
                // once per process, and the alternative is checking the length
                // of a file on a line that is trying to explain a crash.
                if (f.length() > MAX_FILE_BYTES) {
                    val keep = f.readLines().takeLast(MAX_LINES)
                    f.writeText(keep.joinToString("\n") + "\n")
                }
                f.appendText("\n=== ${SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(Date())} ===\n")
                f.appendText("${deviceSummary()}\n")
                sink = f
            } catch (t: Throwable) {
                Log.w(TAG, "could not open the log file", t)
            }
        }
    }

    /** Everything on disk, which is more than this session. */
    fun history(context: Context): String =
        try {
            file(context).takeIf { it.isFile }?.readText().orEmpty()
        } catch (t: Throwable) {
            ""
        }

    /**
     * The device, in one line.
     *
     * First thing in every log and first thing in every report, because it is
     * the question asked of every bug that only happens to somebody else.
     */
    fun deviceSummary(): String =
        try {
            val pageSize = android.system.Os.sysconf(android.system.OsConstants._SC_PAGESIZE)
            "device: ${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}, " +
                "Android ${android.os.Build.VERSION.RELEASE} (API ${android.os.Build.VERSION.SDK_INT}), " +
                "abis=${android.os.Build.SUPPORTED_ABIS.joinToString("/")}, " +
                "pageSize=$pageSize"
        } catch (t: Throwable) {
            "device: unknown (${t.message})"
        }

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

        sink?.let { f ->
            // A log line must never be the reason something fails, so a full
            // disk or a revoked directory is swallowed rather than thrown.
            try {
                synchronized(fileLock) { f.appendText("$stamped\n") }
            } catch (_: Throwable) {
            }
        }

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
