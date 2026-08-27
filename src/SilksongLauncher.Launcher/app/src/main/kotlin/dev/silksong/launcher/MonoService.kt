// MonoService — the process the .NET runtime runs in.
//
// It exists to be a process, not to be a service. The launcher runs four .NET
// programs -- il2cpp.dll, Roslyn's csc.dll and the bundle surgery tool -- and
// each of them wants three things that only a process of its own provides:
//
//   A JavaVM, because .NET implements cryptography on Android by calling
//   Java's through JNI. That rules out an exec'd binary, which is what this
//   was first built as: there is no JavaVM in a bare process, JNI_OnLoad never
//   runs, and the first hash Roslyn takes lands on a null pointer. See
//   monojni.c.
//
//   Somewhere Environment.Exit is survivable. il2cpp calls it. In the
//   launcher's own process that would close the launcher.
//
//   A working directory of its own, and one runtime initialisation per
//   program. chdir is process-wide and the runtime cannot be started twice, so
//   there is no arrangement where several tools share a process and each gets
//   what it needs.
//
// Bound, not started, and that distinction is what this file is about.
// startService is refused for an app in the background from API 26 --
// IllegalStateException -- and a build runs twenty programs over tens of
// minutes, so "the user pressed Home" is not an edge case. A ContentProvider
// was doing the job here until recently, because an app may always reach its
// own provider and acquiring one creates its process.
//
// It could not be recovered from. When the activity manager cannot start the
// process for a provider it answers the request with nothing at all, and the
// record it leaves behind lives in system_server rather than in the app, so
// restarting the app does not clear it. See issue #4: a Xiaomi device refused
// every run from the third one onwards and stayed that way, and the launcher's
// advice -- close it from the recents screen -- could not have helped, because
// only a force stop or a reboot clears that state.
//
// bindService has neither property. The background restriction lives in
// startServiceLocked, not bindServiceLocked, so BIND_AUTO_CREATE brings this
// process up from a backgrounded launcher exactly as a provider did; and when
// the process cannot be started the binding simply stays unconnected, which is
// something the launcher can time out of and try again, rather than a poisoned
// record it can only be force stopped out of.
//
// Two of them, alternating: MonoService in ":builder" and MonoServiceAlt in
// ":builder2". A run ends by killing its own process, and the activity manager
// will not start a second process with the same name and uid until the first
// is confirmed dead -- it keeps the corpse as a "predecessor", defers the new
// start until the death is reaped, and cancels the start outright if that
// takes longer than ten seconds. Consecutive runs therefore queue behind the
// previous one's funeral, which is free on a device that reaps promptly and
// fatal on one that does not. Alternating the two means the process being
// started never shares a name with the one still dying.

package dev.silksong.launcher

import android.app.Service
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.Messenger
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

open class MonoService : Service() {

    companion object {
        /** Run the assembly named in the message's data. */
        const val MSG_RUN = 1

        /** End this process, whatever it is doing. See [quit]. */
        const val MSG_QUIT = 2

        const val KEY_ASSEMBLY = "assembly"
        const val KEY_ARGS = "args"
        const val KEY_CWD = "cwd"
        const val KEY_BCL = "bcl"
        const val KEY_RUNTIME = "runtime"
        const val KEY_ENV = "env"
        const val KEY_OUT = "out"
        const val KEY_RESULT = "result"

        /** What is written to the result file when the run never started. */
        const val FAILED = 120
    }

    /** Set for the life of the process once a run has been accepted. */
    private val running = AtomicBoolean(false)

    /**
     * The whole interface, which is one message in one direction.
     *
     * A Messenger rather than an AIDL interface because nothing is ever
     * returned through it: the run reports itself by writing the output file
     * the launcher is tailing and the result file that ends it, so there is no
     * reply to marshal and no callback to keep alive. The message arrives on
     * this process's main looper, which is only ever used to hand the work to
     * a thread.
     */
    private val messenger = Messenger(
        Handler(Looper.getMainLooper()) { msg ->
            when (msg.what) {
                MSG_RUN -> { start(msg.data); true }
                MSG_QUIT -> { quit(); true }
                else -> false
            }
        },
    )

    override fun onBind(intent: Intent?): IBinder = messenger.binder

    /**
     * Ends this process at once.
     *
     * The launcher asks for this when a run is cancelled. It used to be
     * killBackgroundProcesses, which is package-wide and takes every process
     * of this app that is not in the foreground -- :launcher among them, which
     * is where the build is being run from, and which is in the background
     * precisely in the case worth surviving. This reaches the one process that
     * should go and nothing else.
     *
     * Immediate, with none of the care a provider needed: Messenger.send is
     * one way, so the sender is not blocked on a reply that dying would eat.
     */
    private fun quit() {
        android.os.Process.killProcess(android.os.Process.myPid())
    }

    private fun start(extras: Bundle) {
        val assembly = extras.getString(KEY_ASSEMBLY)
        val args = extras.getStringArray(KEY_ARGS) ?: emptyArray()
        val cwd = extras.getString(KEY_CWD)
        val bcl = extras.getString(KEY_BCL)
        val runtime = extras.getString(KEY_RUNTIME)
        val env = extras.getStringArray(KEY_ENV) ?: emptyArray()
        val outPath = extras.getString(KEY_OUT)
        val resultPath = extras.getString(KEY_RESULT)

        // The launcher builds this message, so an incomplete one is a bug
        // rather than a condition -- but it still has to end the run, because
        // the launcher waits for the result file and nothing else would write
        // it.
        if (assembly == null || bcl == null || runtime == null || outPath == null ||
            resultPath == null
        ) {
            runCatching {
                if (outPath != null) {
                    File(outPath).appendText("monojni: the run request was incomplete\n")
                }
                resultPath?.let { File(it).writeText(FAILED.toString()) }
            }
            return
        }

        // One run per process, and this process only ever hosts one.
        //
        // A second run arriving while the first is going would not queue, it
        // would collide: the native side redirects stdout, rewrites the
        // environment and chdirs, all of which belong to the process and would
        // be pulled out from under the run already using them. A single setup
        // flow cannot do it, but a cancelled run leaves this process alive for
        // a moment, and the retry arrives here.
        if (!running.compareAndSet(false, true)) {
            runCatching {
                File(outPath).appendText("monojni: this process is already running another program\n")
                File(resultPath).writeText(FAILED.toString())
            }
            return
        }

        // Off the main looper: il2cpp runs for minutes.
        thread(name = "mono") {
            try {
                MonoBridge.load()
                // Writes the result file itself, including when the program
                // leaves through Environment.Exit and never comes back here.
                MonoBridge.nativeRun(assembly, args, cwd, bcl, runtime, env, outPath, resultPath)
            } catch (t: Throwable) {
                // The output file is the only channel back, and the launcher
                // is about to stop reading it, so anything worth saying has to
                // be appended now.
                runCatching {
                    File(outPath).appendText("monojni: ${t.javaClass.simpleName}: ${t.message}\n")
                }
                runCatching { File(resultPath).writeText(FAILED.toString()) }
            } finally {
                // Not stopSelf: that ends the service and leaves the process,
                // and the process is the point. A runtime that has already
                // started cannot start again, so the next run needs a new one.
                android.os.Process.killProcess(android.os.Process.myPid())
            }
        }
    }
}

/**
 * The second builder process, which is this one only because it has a
 * different name.
 *
 * See the note at the top of this file: consecutive runs must not ask for a
 * process whose predecessor is still dying, and a process is identified by its
 * name. Nothing else distinguishes the two, and neither of them is preferred.
 */
class MonoServiceAlt : MonoService()
