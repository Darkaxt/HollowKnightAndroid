// DotnetFetcher — running Microsoft's .NET on a phone that has never heard of it.
//
// Unity's IL2CPP converter is a .NET application. Android has no .NET, and
// Microsoft publishes no runtime for it: the supported Linux builds are all
// glibc or musl, and Android is neither -- it uses bionic.
//
// None of that turns out to matter. Android is Linux; the kernel is the only
// thing the runtime actually needs from the operating system, and a C library
// is just a shared library. Carrying Debian's glibc along beside Microsoft's
// ordinary linux-arm64 runtime is enough to run .NET on a stock, unrooted
// phone at full native speed -- no proot, no chroot, no emulation.
//
// Two things make it work, and neither is discoverable from an error message:
//
//   Invoke the loader, not the program. A glibc binary names its interpreter
//   by absolute path (/lib/ld-linux-aarch64.so.1), which does not exist on
//   Android, so exec'ing it fails before a single instruction runs. Running
//   the loader explicitly and handing it the program sidesteps the missing
//   path entirely.
//
//   Give the loader the muxer's name. Launched that way, dotnet refuses to
//   start: "cannot execute dotnet when renamed to ld-linux-aarch64.so.1". It
//   identifies itself from /proc/self/exe, which is now the loader, and
//   --argv0 does not help because it is not argv[0] being checked. So a copy
//   of the loader is placed inside the dotnet root under the name "dotnet"
//   and the real muxer moves aside to "dotnet.real": the name check passes,
//   and host/fxr still resolves relative to it.
//
// A retired host-side script assembled the same bundle from a Docker image,
// back when a PC did this work. There is no Docker here, so the pieces come
// from where Docker would have got them: Microsoft's runtime tarball and
// Debian's own package pool.

package dev.silksong.launcher

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import java.io.BufferedInputStream
import java.io.BufferedReader
import java.io.File
import java.io.IOException
import java.io.InputStreamReader
import java.util.zip.GZIPInputStream
import kotlin.coroutines.coroutineContext

object DotnetFetcher {

    // 8.0 because that is the framework il2cpp's runtimeconfig asks for. It
    // rolls forward to a later major if one is present, but nothing here
    // installs one, so this is what actually runs.
    private const val VERSION = "8.0.14"
    private const val RUNTIME_URL =
        "https://builds.dotnet.microsoft.com/dotnet/Runtime/$VERSION/" +
            "dotnet-runtime-$VERSION-linux-arm64.tar.gz"

    private const val DEBIAN = "https://deb.debian.org/debian"
    private const val DEBIAN_INDEX = "$DEBIAN/dists/bookworm/main/binary-arm64/Packages.gz"

    /**
     * The C library and the others the runtime links against.
     *
     * libssl3 is not optional even though nothing here does cryptography: the
     * runtime's crypto shim is resolved at startup, and without it .NET aborts
     * before running a line of IL with "No usable version of libssl was
     * found" -- and aborts with SIGABRT, so what reaches the caller is exit
     * 134 and no explanation.
     *
     * Deliberately not libicu, which is the largest of them by far: il2cpp's
     * runtimeconfig sets System.Globalization.Invariant, so the runtime never
     * loads it. See prepareTool in Il2cppConverter, which writes that config.
     */
    private val DEBIAN_PACKAGES = listOf("libc6", "libstdc++6", "libgcc-s1", "zlib1g", "libssl3")

    /** The loader, which is also what everything is launched through. */
    private const val LOADER = "ld-linux-aarch64.so.1"

    data class Progress(val step: String, val fraction: Float, val detail: String = "")

    /** Everything lands under the toolchain root, beside clang. */
    fun rootFor(context: android.content.Context): File = File(Toolchain.rootFor(context), "dotnet")

    private fun libDir(root: File) = File(root, "lib")
    private fun dotnetDir(root: File) = File(root, "dotnet")

    /** The muxer stand-in: a copy of the loader that answers to "dotnet". */
    fun muxer(root: File): File = File(dotnetDir(root), "dotnet")

    fun isPresent(root: File): Boolean =
        File(root, "verified").isFile &&
            muxer(root).canExecute() &&
            File(dotnetDir(root), "dotnet.real").isFile &&
            File(libDir(root), LOADER).isFile

    /**
     * The command that runs a .NET program.
     *
     * Everything goes through the loader with an explicit library path; see
     * the header for why neither part is optional.
     */
    fun command(root: File, assembly: File, vararg args: String): List<String> =
        listOf(
            muxer(root).absolutePath,
            "--library-path", libDir(root).absolutePath,
            File(dotnetDir(root), "dotnet.real").absolutePath,
            assembly.absolutePath,
        ) + args

    /**
     * The environment .NET needs on a phone.
     *
     * DOTNET_ROOT so the muxer finds its shared framework, and the two
     * globalization switches because no ICU is fetched -- without them the
     * runtime aborts at startup with "Couldn't find a valid ICU package".
     */
    fun environment(root: File): Map<String, String> = mapOf(
        "DOTNET_ROOT" to dotnetDir(root).absolutePath,
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" to "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT" to "1",
        "DOTNET_NOLOGO" to "1",
        "HOME" to root.absolutePath,
        "TMPDIR" to File(root, "tmp").apply { mkdirs() }.absolutePath,
    )

    fun fetch(root: File): Flow<Progress> = channelFlow {
        root.mkdirs()

        // The staged set is recorded, not inferred. Adding a package to the
        // list above has to re-run the collection, and guessing at that from
        // one library's presence would mean the day a dependency is added is
        // the day every existing install silently keeps the old set.
        val wanted = DEBIAN_PACKAGES.sorted().joinToString(",")
        val record = File(root, "libs.txt")
        if (!File(libDir(root), LOADER).isFile || record.takeIf { it.isFile }?.readText()?.trim() != wanted) {
            send(Progress("C library", -1f, "asking Debian what is current"))
            val index = debianIndex()
            for ((n, name) in DEBIAN_PACKAGES.withIndex()) {
                coroutineContext.ensureActive()
                val pkg = index[name] ?: throw IOException("Debian has no package named $name for arm64")
                val step = "C library (${n + 1}/${DEBIAN_PACKAGES.size})"
                send(Progress(step, 0f, name))
                // Debian's archives are rooted at the filesystem root, so
                // nothing is stripped; the tree lands under <root>/deb and the
                // libraries are collected out of it below.
                val staging = File(root, "deb")
                fetchDeb(staging, pkg) { done, total ->
                    trySend(Progress(step, done.toFloat() / total, ToolchainFetcher.mb(done, total)))
                }
            }
            collectLibraries(File(root, "deb"), libDir(root))
            File(root, "deb").deleteRecursively()
            record.writeText(wanted)
            // The bundle changed, so whatever was proven about the old one no
            // longer applies.
            File(root, "verified").delete()
        }

        if (!File(dotnetDir(root), "dotnet.real").isFile) {
            send(Progress(".NET runtime", 0f, "downloading"))
            fetchRuntime(root) { done, total ->
                trySend(Progress(".NET runtime", done.toFloat() / total, ToolchainFetcher.mb(done, total)))
            }
            renameMuxer(root)
        }

        val verified = File(root, "verified")
        if (!verified.isFile) {
            send(Progress("Checking .NET", -1f, "listing runtimes"))
            val problem = verify(root)
            if (problem != null) {
                // Discard the runtime, so retrying is worth something.
                //
                // Everything above is skipped when its output is already on
                // disk, and the runtime is the one piece with no integrity
                // check -- the Debian packages are checked against the sha256
                // in the index, but Microsoft's tarball is streamed straight
                // into files. A truncated download therefore unpacks happily
                // and fails right here, and without this the next attempt saw
                // dotnet.real present, skipped the fetch, and failed in exactly
                // the same way for ever.
                //
                // The libraries are kept: they are verified, so they are not
                // what is in doubt.
                dotnetDir(root).deleteRecursively()
                LauncherLog.log("discarded the .NET runtime; it will be fetched again next time")
                throw IOException("the fetched .NET does not run: $problem")
            }
            verified.writeText("$VERSION\n")
        }
        send(Progress(".NET ready", 1f, ""))
    }.flowOn(Dispatchers.IO)

    // ── Debian ─────────────────────────────────────────────────────────────

    private class DebPackage(val name: String, val path: String, val sha256: String, val size: Long)

    /**
     * Reads the package index a stanza at a time.
     *
     * Debian's arm64 index is 12 MB compressed and around 90 MB of text.
     * Termux's fits in a string comfortably; this one does not, and reading it
     * whole is a way to run a phone out of heap for the sake of four packages.
     */
    private fun debianIndex(): Map<String, DebPackage> {
        val out = HashMap<String, DebPackage>()
        ToolchainFetcher.open(DEBIAN_INDEX).use { raw ->
            BufferedReader(InputStreamReader(GZIPInputStream(BufferedInputStream(raw, 1 shl 16)))).use { r ->
                var name: String? = null
                var path: String? = null
                var sha: String? = null
                var size = -1L
                while (true) {
                    val line = r.readLine() ?: break
                    when {
                        line.isEmpty() -> {
                            if (name != null && name in DEBIAN_PACKAGES && path != null && sha != null) {
                                out[name] = DebPackage(name, path, sha, size)
                            }
                            name = null; path = null; sha = null; size = -1L
                        }
                        line.startsWith("Package: ") -> name = line.substring(9).trim()
                        line.startsWith("Filename: ") -> path = line.substring(10).trim()
                        line.startsWith("SHA256: ") -> sha = line.substring(8).trim()
                        line.startsWith("Size: ") -> size = line.substring(6).trim().toLongOrNull() ?: -1L
                    }
                }
                if (name != null && name in DEBIAN_PACKAGES && path != null && sha != null) {
                    out[name] = DebPackage(name, path, sha, size)
                }
            }
        }
        return out
    }

    private suspend fun fetchDeb(dest: File, pkg: DebPackage, onProgress: (Long, Long) -> Unit) {
        dest.mkdirs()
        val tmp = File(dest, "${pkg.name}.deb")
        if (tmp.length() != pkg.size) {
            ToolchainFetcher.open("$DEBIAN/${pkg.path}").use { input ->
                tmp.outputStream().use { out -> ToolchainFetcher.copy(input, out, pkg.size, onProgress) }
            }
        }
        val got = ToolchainFetcher.sha256(tmp)
        if (got != pkg.sha256) {
            tmp.delete()
            throw IOException("${pkg.name}: expected sha256 ${pkg.sha256} but got $got")
        }
        tmp.inputStream().buffered().use { ToolchainFetcher.extractDeb(it, dest, strip = "") }
        tmp.delete()
    }

    /**
     * Flattens the shared libraries out of an unpacked Debian tree.
     *
     * They arrive under lib/aarch64-linux-gnu and usr/lib/aarch64-linux-gnu,
     * with symlinks between sonames and real files. The loader is given one
     * directory, so everything is copied into it by its final name and the
     * links are resolved rather than reproduced -- a dangling link here would
     * surface as a missing symbol much later.
     *
     * Only files sitting directly in those directories are taken. glibc also
     * ships a gconv/ subdirectory of some 250 charset modules which are
     * loaded by path, not by soname; sweeping them up recursively puts 250
     * unrelated objects in the runtime's library path for no reason, and
     * gives them the chance to shadow something.
     */
    private fun collectLibraries(from: File, into: File) {
        into.mkdirs()
        var found = 0
        for (dir in listOf(File(from, "lib/aarch64-linux-gnu"), File(from, "usr/lib/aarch64-linux-gnu"))) {
            for (f in dir.listFiles().orEmpty()) {
                if (!f.isFile || !f.name.contains(".so")) continue
                val dst = File(into, f.name)
                if (dst.exists() && dst.length() == f.length()) continue
                f.copyTo(dst, overwrite = true)
                dst.setExecutable(true, true)
                found++
            }
        }
        if (found == 0) throw IOException("no shared libraries in the Debian packages")
        val loader = File(into, LOADER)
        if (!loader.isFile) throw IOException("$LOADER is not in libc6")
        loader.setExecutable(true, true)

        // Checked by name because these are resolved *lazily*, when the
        // runtime first needs them, so nothing earlier notices they are
        // absent. libssl is the one that bites: --list-runtimes is answered by
        // hostfxr and succeeds without it, and the failure only appears when
        // real IL runs -- as "No usable version of libssl was found" followed
        // by SIGABRT, which reaches the caller as a bare exit 134.
        for (soname in listOf("libc.so.6", "libstdc++.so.6", "libssl.so.3", "libcrypto.so.3")) {
            if (!File(into, soname).isFile) throw IOException("$soname is missing from the C library bundle")
        }
    }

    // ── Microsoft ──────────────────────────────────────────────────────────

    private suspend fun fetchRuntime(root: File, onProgress: (Long, Long) -> Unit) {
        val dest = dotnetDir(root).apply { mkdirs() }
        val total = ToolchainFetcher.contentLength(RUNTIME_URL)
        var done = 0L
        ToolchainFetcher.open(RUNTIME_URL).use { raw ->
            val counting = object : java.io.FilterInputStream(raw) {
                override fun read(b: ByteArray, off: Int, len: Int): Int {
                    val n = super.read(b, off, len)
                    if (n > 0) {
                        done += n
                        if (done % (1L shl 22) < n) onProgress(done, total)
                    }
                    return n
                }
            }
            TarArchiveInputStream(GZIPInputStream(BufferedInputStream(counting, 1 shl 16))).use { tar ->
                while (true) {
                    coroutineContext.ensureActive()
                    val entry = tar.nextEntry ?: break
                    val rel = entry.name.removePrefix("./")
                    if (rel.isEmpty()) continue
                    val out = File(dest, rel)
                    when {
                        entry.isDirectory -> out.mkdirs()
                        entry.isSymbolicLink -> ToolchainFetcher.symlink(entry.linkName, out)
                        entry.isFile -> {
                            out.parentFile?.mkdirs()
                            out.outputStream().use { o -> tar.copyTo(o, 1 shl 16) }
                            ToolchainFetcher.applyMode(out, entry.mode)
                        }
                    }
                }
            }
        }
        onProgress(total, total)
    }

    /**
     * Puts the loader where the muxer was.
     *
     * See the header: the muxer checks its own name through /proc/self/exe,
     * which is the loader once the loader is what was exec'd, so the loader
     * has to be the thing called "dotnet". The real muxer is kept because
     * host/fxr resolution starts from the directory it lives in.
     */
    private fun renameMuxer(root: File) {
        val dir = dotnetDir(root)
        val muxer = File(dir, "dotnet")
        val real = File(dir, "dotnet.real")
        val loader = File(libDir(root), LOADER)
        if (!loader.isFile) throw IOException("the C library was not staged before the runtime")
        if (!real.isFile) {
            if (!muxer.isFile) throw IOException("the runtime archive had no dotnet muxer")
            if (!muxer.renameTo(real)) throw IOException("could not move the muxer aside")
        }
        muxer.delete()
        loader.copyTo(muxer, overwrite = true)
        muxer.setExecutable(true, true)
        real.setExecutable(true, true)
    }

    /**
     * Asks the runtime to list itself.
     *
     * --list-runtimes exercises the whole path -- loader, glibc, muxer name
     * check, hostfxr -- without needing an assembly to run, and its output
     * names the framework version, so a bundle that starts but carries the
     * wrong runtime is caught here too.
     *
     * Note what a failure here does NOT mean. If Android refuses the exec at
     * all, Toolchain.exec throws before this returns, with its own message. So
     * reaching a non-null return means the process really did start and then
     * failed -- the loader, a shared library, or hostfxr.
     */
    private suspend fun verify(root: File): String? {
        val result = Toolchain.exec(
            listOf(
                muxer(root).absolutePath,
                "--library-path", libDir(root).absolutePath,
                File(dotnetDir(root), "dotnet.real").absolutePath,
                "--list-runtimes",
            ),
            cwd = root,
            env = environment(root),
        )
        if (!result.ok || !result.output.contains("Microsoft.NETCore.App")) {
            // Everything, to the log, before anything is summarised for the
            // dialog. A failure here is almost always reported from a device
            // nobody working on this owns, so the log is the only evidence
            // there will ever be -- and it used to be thrown away.
            describeHost()
            LauncherLog.log("dotnet check failed: exit ${result.code}")
            val output = result.output.trim()
            if (output.isEmpty()) LauncherLog.log("dotnet check printed nothing")
            else for (line in output.lines()) LauncherLog.log("  dotnet: $line")

            // The exit status is always included, because it is the part that
            // survives when there is no output at all: 127 is a library that
            // could not be loaded, and 128+N is death by signal N -- 132, 134
            // and 139 being SIGILL, SIGABRT and SIGSEGV.
            //
            // And an empty message here is not hypothetical. This used to
            // report `output.lines().lastOrNull() ?: "exited ${result.code}"`,
            // but "".lines() is listOf(""), not an empty list -- so the
            // fallback never ran, and a process killed by a signal produced
            // "the fetched .NET does not run:" followed by nothing at all.
            // Which is exactly how it was reported from the field.
            val detail = when {
                output.isNotEmpty() -> output.lines().takeLast(3).joinToString(" / ")
                result.code > 128 -> "no output; killed by ${signalName(result.code - 128)}"
                else -> "no output"
            }
            return "exit ${result.code}: $detail"
        }
        LauncherLog.log("dotnet: ${result.output.trim().lines().firstOrNull()}")
        return null
    }

    /**
     * Names the signal a process died from, and says what it means here.
     *
     * SIGSYS is the one worth spelling out. Android confines every app with a
     * seccomp filter, and Android 15 tightened it: syscalls that modern glibc
     * makes at startup and for thread creation -- clone3 in particular -- are
     * refused in a way that kills the process rather than returning ENOSYS.
     * glibc only falls back to the old clone() when it gets ENOSYS, so a trap
     * leaves it no way to recover and the process dies before printing a word.
     *
     * That is the difference between a device this works on and one it does
     * not: the bundle carries Debian bookworm's glibc 2.36, which uses clone3.
     * glibc 2.33 and earlier never call it.
     */
    private fun signalName(signal: Int): String = when (signal) {
        4 -> "SIGILL (illegal instruction -- built for a CPU this is not)"
        6 -> "SIGABRT (the runtime gave up; usually a missing or wrong library)"
        7 -> "SIGBUS (bad memory access -- often a page-size or alignment mismatch)"
        11 -> "SIGSEGV"
        31 -> "SIGSYS (Android refused a system call the C library needs; this is " +
            "the seccomp restriction that stops glibc binaries on Android 15)"
        else -> "signal $signal"
    }

    /**
     * The facts about this device that decide whether the bundle can work.
     *
     * Written to the log when the check fails, because the answer is usually
     * one of these and none of them are visible from the error:
     *
     *   Page size. The bundle is ordinary linux-arm64 ELF, and a kernel with
     *   16 KB pages cannot map segments aligned for 4 KB. Android 15 allows
     *   16 KB page devices, which is new, and would break these devices while
     *   leaving every 4 KB device working.
     *
     *   Android version. Android 15 tightened the seccomp filter every app
     *   runs under, and the syscalls it now refuses include ones glibc makes.
     *   See signalName.
     *
     *   ABI. A 32-bit-only process cannot run an arm64 runtime; if the app
     *   ever ends up in one, everything else here is a red herring.
     */
    private fun describeHost() {
        LauncherLog.log(LauncherLog.deviceSummary())
        try {
            if (android.system.Os.sysconf(android.system.OsConstants._SC_PAGESIZE) != 4096L) {
                LauncherLog.log(
                    "NOTE: this device does not use 4 KB pages. The .NET bundle is " +
                        "stock linux-arm64 and expects 4 KB; that alone can be the reason.",
                )
            }
        } catch (_: Throwable) {
        }
    }
}
