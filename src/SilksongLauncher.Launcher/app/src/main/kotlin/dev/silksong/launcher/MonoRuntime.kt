// MonoRuntime — the .NET the launcher runs its tools on.
//
// Three programs here are .NET applications: il2cpp.dll, Roslyn's csc.dll,
// and the bundle surgery tool. This is what runs them.
//
// It replaces DotnetFetcher, which built a runtime on the device at first
// launch out of Microsoft's linux-arm64 tarball and five packages from
// Debian's pool -- glibc, libstdc++, libssl and friends -- and invoked the
// whole thing through glibc's loader because Android has no
// /lib/ld-linux-aarch64.so.1 to name as an interpreter. That worked, then
// stopped: Android builds each app's seccomp filter from the syscalls bionic
// itself makes and answers everything else with SIGSYS rather than ENOSYS,
// and glibc 2.35 registers restartable sequences during startup. There is no
// fallback from a trap, so the process died before printing a word, and the
// only evidence was exit 159. A newer glibc will not fix it and an older one
// only postpones it: the filter is derived from bionic, so a foreign C
// library is always one syscall away from the same end.
//
// The runtime that has no such problem is the one Microsoft builds for
// Android: .NET's Mono flavour for android-arm64, linked against bionic,
// the same runtime MAUI ships. It is fetched at build time and shipped
// inside the APK, so there is nothing to download, nothing to verify at run
// time, and setup no longer needs the network at all.
//
// Two pieces, in the two places Android allows them:
//
//   lib/arm64-v8a/ holds libmonosgen-2.0.so, its components, and monohost --
//   an ordinary arm64 executable that the installer extracts to a directory
//   it will execute from. Nothing here depends on the app targeting API 28;
//   that requirement belongs to the fetched clang, which is written into the
//   data directory and governed by the SELinux domain targetSdk selects.
//
//   assets/mono-bcl holds the class library, which is data. Assets are not
//   executable and do not need to be -- the assemblies are read and JIT'd,
//   never exec'd -- but they also cannot be opened as files while they are
//   inside the APK, so they are unpacked once into filesDir.

package dev.silksong.launcher

import android.app.ActivityManager
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import kotlin.coroutines.coroutineContext

object MonoRuntime {

    /** Where the class library is unpacked to. */
    private const val BCL = "mono-bcl"

    /** Bumped when the staged copy has to be replaced rather than reused. */
    private const val STAGE_VERSION = "1"

    data class Progress(val step: String, val fraction: Float, val detail: String = "")

    /**
     * The host, as the installer left it.
     *
     * Not an executable any more, and not exec'd: see MonoService for why the
     * runtime needs an app process rather than a child process.
     */
    fun runtimeDir(context: Context): File = File(context.applicationInfo.nativeLibraryDir)

    fun bclDir(context: Context): File = File(context.filesDir, BCL)

    private fun stamp(context: Context) = File(bclDir(context), ".staged")

    /**
     * True when the runtime is ready to be used.
     *
     * The native side is part of the APK, so its absence is not a state that
     * can be repaired here -- it means the APK was assembled without it, and
     * saying so plainly beats failing later inside a converter.
     */
    fun isPresent(context: Context): Boolean =
        File(runtimeDir(context), "libmonojni.so").isFile && stamp(context).isFile

    fun environment(context: Context): Map<String, String> = mapOf(
        // il2cpp's runtimeconfig asks for invariant globalization and the
        // runtime carries no ICU for Android; monojni sets the same thing as
        // a runtime property, and this covers anything that reads the
        // environment instead.
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" to "1",
        "HOME" to context.filesDir.absolutePath,
        "TMPDIR" to File(context.filesDir, "tmp").apply { mkdirs() }.absolutePath,
    )

    /**
     * Runs a .NET program in :builder and collects what it printed.
     *
     * Deliberately the same shape as Toolchain.exec, and returning the same
     * Result, because until the runtime moved into a process of its own that
     * is exactly what this was -- an argv and a subprocess. Callers pass an
     * assembly and its arguments instead of a command line, and are otherwise
     * unchanged.
     *
     * The output file is the whole protocol. :builder redirects stdout and
     * stderr onto it and writes the result file when the run ends, so this
     * tails the one until the other appears. A pipe would say "the run is
     * over" by reaching end of file, which is tidier, but the descriptor
     * cannot be handed to the service: the activity manager refuses an Intent
     * carrying one. Both processes are the same application, so a path in the
     * cache directory is a channel they already share.
     */
    suspend fun exec(
        context: Context,
        assembly: File,
        args: List<String> = emptyList(),
        cwd: File? = null,
        env: Map<String, String> = emptyMap(),
        onLine: (String) -> Unit = {},
    ): Toolchain.Result = withContext(Dispatchers.IO) {
        if (!assembly.isFile) throw IOException("no such assembly: $assembly")

        val stamp = System.nanoTime()
        val out = File(context.cacheDir, "mono-$stamp.out")
        val result = File(context.cacheDir, "mono-$stamp.exit")
        out.delete(); result.delete()
        out.createNewFile()

        val merged = environment(context) + env
        val flatEnv = ArrayList<String>(merged.size * 2)
        for ((k, v) in merged) { flatEnv += k; flatEnv += v }

        val intent = Intent(context, MonoService::class.java).apply {
            putExtra(MonoService.EXTRA_ASSEMBLY, assembly.absolutePath)
            putExtra(MonoService.EXTRA_ARGS, args.toTypedArray())
            putExtra(MonoService.EXTRA_CWD, cwd?.absolutePath)
            putExtra(MonoService.EXTRA_BCL, bclDir(context).absolutePath)
            putExtra(MonoService.EXTRA_RUNTIME, runtimeDir(context).absolutePath)
            putExtra(MonoService.EXTRA_ENV, flatEnv.toTypedArray())
            putExtra(MonoService.EXTRA_RESULT, result.absolutePath)
            putExtra(MonoService.EXTRA_OUT, out.absolutePath)
        }

        val collected = StringBuilder()
        var died = false
        var exitCode: Int? = null
        // Stops :builder when the caller goes away. Toolchain.exec did the
        // same with destroyForcibly, and for the same reason: without it a
        // cancelled build leaves il2cpp running on every core with several
        // gigabytes held, in a process that hosts a started service and so is
        // not a candidate for reclaim. The retry then lands in that process
        // and finds a runtime that cannot be started twice.
        val onCancel = coroutineContext[Job]?.invokeOnCompletion { cause ->
            if (cause != null) runCatching { context.stopService(Intent(context, MonoService::class.java)) }
        }
        try {
            context.startService(intent)

            // Tail. Reading stops one pass *after* the result file appears,
            // not as soon as it does: the last of the output may still have
            // been in flight when the run ended, and a build log that loses
            // its final line is a build log that loses the error.
            var offset = 0L
            val carry = StringBuilder()
            var pending = ByteArray(0)
            var done = false
            var idle = 0
            val started = System.currentTimeMillis()
            while (true) {
                val finished = result.isFile
                val len = out.length()
                if (len > offset) {
                    java.io.RandomAccessFile(out, "r").use { raf ->
                        raf.seek(offset)
                        val buf = ByteArray((len - offset).coerceAtMost(1 shl 20).toInt())
                        val got = raf.read(buf)
                        if (got > 0) {
                            offset += got
                            // A read can stop anywhere, including halfway
                            // through a UTF-8 sequence. Whatever does not
                            // decode is carried to the next read rather than
                            // turned into replacement characters.
                            val merged = pending + buf.copyOf(got)
                            val cut = wholeChars(merged)
                            carry.append(String(merged, 0, cut, Charsets.UTF_8))
                            pending = merged.copyOfRange(cut, merged.size)
                        }
                    }
                    var nl = carry.indexOf("\n")
                    while (nl >= 0) {
                        val line = carry.substring(0, nl)
                        carry.delete(0, nl + 1)
                        if (collected.length < MAX_CAPTURED) collected.append(line).append('\n')
                        onLine(line)
                        nl = carry.indexOf("\n")
                    }
                    idle = 0
                    continue
                }
                if (done) break
                if (finished) { done = true; continue }

                // Is :builder still there?
                //
                // The result file is written by the native side, from an
                // atexit handler that survives Environment.Exit -- but not
                // everything that can end a process gives it the chance. A
                // SIGSEGV, or the low memory killer deciding that a process
                // holding several gigabytes is the one to take, ends it with
                // nothing written. Then no more output ever arrives and the
                // result file never appears, and without this the loop waits
                // for one or the other until the app is killed too.
                //
                // Deliberately not "was it ever seen alive": the process can
                // die before the first poll -- a missing crypto class aborts
                // it in well under a second -- and a rule that only fires
                // after a sighting would wait for ever for one that never
                // came. A grace period is enough, because the only thing
                // being distinguished is "not started yet" from "gone".
                if (++idle >= 8) {
                    idle = 0
                    if (System.currentTimeMillis() - started > STARTUP_GRACE_MS &&
                        !builderAlive(context) && !result.isFile
                    ) { died = true; break }
                }
                delay(120)
            }
            if (carry.isNotEmpty()) {
                if (collected.length < MAX_CAPTURED) collected.append(carry)
                onLine(carry.toString())
            }
        } finally {
            onCancel?.dispose()
            out.delete()
            // Read before this, so deleting here rather than after the read
            // is what keeps a cancelled run from leaving one behind.
            val code = result.takeIf { it.isFile }?.readText()?.trim()?.toIntOrNull()
            result.delete()
            exitCode = code
        }

        if (died) {
            val note = "the build process was killed before it finished; " +
                "on a large conversion this is usually the system reclaiming its memory"
            LauncherLog.log("mono: $note")
            onLine("monojni: $note")
            if (collected.length < MAX_CAPTURED) collected.append(note).append('\n')
        }

        Toolchain.Result(exitCode ?: MonoService.FAILED, collected.toString())
    }

    /** As much output as is kept; the rest is streamed to [onLine] and dropped. */
    private const val MAX_CAPTURED = 1 shl 20

    /**
     * True while the :builder process exists.
     *
     * getRunningAppProcesses has been useless for looking at other apps since
     * Android 5, and that is not what this is for: an app can still see its
     * own processes, which is exactly the question here.
     */
    private fun builderAlive(context: Context): Boolean {
        val am = context.getSystemService(Context.ACTIVITY_SERVICE) as? ActivityManager ?: return true
        return runCatching {
            am.runningAppProcesses?.any { it.processName.endsWith(BUILDER_PROCESS) } == true
        }.getOrDefault(true)
    }

    /** Must match android:process on MonoService in the manifest. */
    private const val BUILDER_PROCESS = ":builder"

    /**
     * How long :builder is allowed to not exist before it counts as dead.
     *
     * Only has to cover the gap between startService returning and the
     * process appearing, which is a process fork; a second is generous.
     */
    private const val STARTUP_GRACE_MS = 4_000L

    /**
     * How much of [b] decodes as whole UTF-8 characters.
     *
     * Returns the length to decode now, leaving any truncated sequence at the
     * end for the next read to complete. Only the last three bytes can be
     * part of one, so this looks no further back than that.
     */
    private fun wholeChars(b: ByteArray): Int {
        var i = b.size - 1
        val floor = maxOf(0, b.size - 3)
        while (i >= floor) {
            val c = b[i].toInt() and 0xFF
            // A continuation byte is 10xxxxxx; anything else starts a
            // character, and its length says whether it is all here.
            if (c and 0xC0 != 0x80) {
                val need = when {
                    c and 0x80 == 0x00 -> 1
                    c and 0xE0 == 0xC0 -> 2
                    c and 0xF0 == 0xE0 -> 3
                    c and 0xF8 == 0xF0 -> 4
                    else -> 1   // not a lead byte at all; let the decoder have it
                }
                return if (i + need <= b.size) b.size else i
            }
            i--
        }
        return b.size
    }

    /**
     * Unpacks the class library out of the APK.
     *
     * Assets cannot be opened as files while they are in the APK -- they are
     * entries in a zip, and the runtime wants paths -- so they are copied
     * once. The stamp is written last and names the version it was written
     * for, so an interrupted copy is not mistaken for a finished one and an
     * upgrade replaces what the previous version left.
     */
    fun stage(context: Context): Flow<Progress> = channelFlow {
        val dir = bclDir(context)
        val mark = stamp(context)
        if (mark.isFile && mark.readText().trim() == STAGE_VERSION) {
            send(Progress(".NET ready", 1f))
            return@channelFlow
        }

        if (!File(runtimeDir(context), "libmonojni.so").isFile) {
            throw IOException(
                "the .NET host is missing from this build: " +
                    "${File(runtimeDir(context), "libmonojni.so")}. " +
                    "The APK was assembled without it.",
            )
        }

        dir.deleteRecursively()
        dir.mkdirs()

        val names = context.assets.list(BCL)?.toList().orEmpty()
        if (names.isEmpty()) throw IOException("the APK carries no $BCL assets")

        send(Progress("Unpacking .NET", 0f, "${names.size} files"))
        for ((n, name) in names.withIndex()) {
            context.assets.open("$BCL/$name").use { input ->
                File(dir, name).outputStream().use { input.copyTo(it) }
            }
            send(Progress("Unpacking .NET", (n + 1f) / names.size, name))
        }

        if (!File(dir, "System.Private.CoreLib.dll").isFile) {
            throw IOException("the class library is incomplete: System.Private.CoreLib.dll is missing")
        }

        mark.writeText(STAGE_VERSION)
        LauncherLog.log("staged the .NET class library: ${names.size} files")
        send(Progress(".NET ready", 1f))
    }.flowOn(Dispatchers.IO)
}
