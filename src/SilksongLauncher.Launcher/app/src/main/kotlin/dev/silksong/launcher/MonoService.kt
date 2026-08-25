// MonoService — the process the .NET runtime runs in.
//
// It exists to be a process, not to be a service. The launcher needs three
// things from wherever it runs il2cpp, csc and the surgery tool:
//
//   A JavaVM, because .NET does cryptography on Android through Java. That
//   rules out an exec'd binary, which is what this was first built as: there
//   is no JavaVM in a bare process, JNI_OnLoad never runs, and the first hash
//   Roslyn takes lands on a null pointer.
//
//   Somewhere Environment.Exit is survivable. il2cpp calls it. In the
//   launcher's process that would close the launcher.
//
//   A working directory of its own, and one runtime initialisation per
//   program. chdir is process-wide and the runtime cannot start twice, so
//   there is no arrangement where several tools share a process and each gets
//   what it needs.
//
// A separate process answers all three, and Android's way of asking for one
// is android:process on a component. Hence a Service -- started, not bound,
// because there is nothing to call back into: results come back down a pipe
// the launcher opened before starting it.
//
// The process is killed when the run finishes rather than left for the next
// one. It costs a process start and a runtime start per tool, and it buys
// exactly the guarantee the runtime cannot give: that the next program starts
// in a runtime nothing has run in yet.

package dev.silksong.launcher

import android.app.Service
import android.content.Intent
import android.os.IBinder
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

class MonoService : Service() {

    /** Set for the life of the process once a run has been accepted. */
    private val running = AtomicBoolean(false)

    companion object {
        const val EXTRA_ASSEMBLY = "assembly"
        const val EXTRA_ARGS = "args"
        const val EXTRA_CWD = "cwd"
        const val EXTRA_BCL = "bcl"
        const val EXTRA_RUNTIME = "runtime"
        const val EXTRA_ENV = "env"
        const val EXTRA_OUT = "out"
        const val EXTRA_RESULT = "result"

        /** What is written to the result file when the run never got started. */
        const val FAILED = 120
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent == null) {
            stopSelf(startId)
            return START_NOT_STICKY
        }

        val assembly = intent.getStringExtra(EXTRA_ASSEMBLY)
        val args = intent.getStringArrayExtra(EXTRA_ARGS) ?: emptyArray()
        val cwd = intent.getStringExtra(EXTRA_CWD)
        val bcl = intent.getStringExtra(EXTRA_BCL)
        val runtime = intent.getStringExtra(EXTRA_RUNTIME)
        val env = intent.getStringArrayExtra(EXTRA_ENV) ?: emptyArray()
        val resultPath = intent.getStringExtra(EXTRA_RESULT)
        val outPath = intent.getStringExtra(EXTRA_OUT)

        if (assembly == null || bcl == null || runtime == null || resultPath == null || outPath == null) {
            stopSelf(startId)
            return START_NOT_STICKY
        }

        // One run per process, and this process only ever hosts one.
        //
        // A second start arriving while the first is still going would be a
        // disaster rather than a queue: the native side redirects stdout,
        // rewrites the environment and chdirs, all of which belong to the
        // process and would be pulled out from under the run already using
        // them. It cannot happen from a single setup flow, but a cancelled
        // run leaves this process alive, and the retry lands here.
        if (!running.compareAndSet(false, true)) {
            runCatching {
                File(outPath).appendText(
                    "monojni: this process is already running another program\n",
                )
                File(resultPath).writeText(FAILED.toString())
            }
            return START_NOT_STICKY
        }

        // Off the main thread: il2cpp runs for minutes, and a Service's
        // onStartCommand is called on the main looper.
        thread(name = "mono") {
            try {
                MonoBridge.load()
                // Writes the result file itself, including when the program
                // leaves through Environment.Exit and never comes back here.
                MonoBridge.nativeRun(assembly, args, cwd, bcl, runtime, env, outPath, resultPath)
            } catch (t: Throwable) {
                // The output file is the only channel back, and the launcher
                // is about to stop reading it, so anything worth saying has
                // to be appended now.
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

        return START_NOT_STICKY
    }
}
