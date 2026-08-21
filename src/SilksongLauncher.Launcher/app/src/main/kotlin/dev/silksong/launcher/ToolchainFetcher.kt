// ToolchainFetcher — getting a working C++ compiler onto the phone.
//
// The game's native code is compiled on the device, which needs three things
// Android does not have: an aarch64-hosted clang, a linker, and an NDK
// sysroot to compile against. None of it can ship in the APK -- it is 80 MB of
// other people's GPL and Apache binaries, and the APK is meant to stay small
// -- so each device fetches it, the same way it fetches Unity.
//
// Two sources, because no single one has both halves:
//
//   Termux      clang, lld, binutils and the libraries they need. The NDK's
//               own clang is a linux-x86_64 binary and cannot run here;
//               Termux's is built to run *on* Android, which is the whole
//               reason this project can compile anything at all. Packages are
//               ordinary .deb files: an ar archive around a data.tar.xz.
//
//   Google NDK  the sysroot -- Android's headers and its per-API-level stub
//               libraries. Termux's own ndk-sysroot package is not this: it
//               installs headers for building Termux programs, with no
//               aarch64-linux-android/<api>/ tree, so nothing can be targeted
//               at API 33 with it.
//
// The NDK is a 664 MB zip and we want 12.6 MB of it. Unlike Unity's tar.xz a
// zip is random-access, so the central directory is read first and only the
// sysroot entries are transferred -- one ranged request across the span they
// occupy, closed as soon as the last one has been read.
//
// On trust: Termux's repository is rolling, so package versions cannot be
// pinned in source without breaking the day Termux publishes an update. Each
// .deb is instead checked against the SHA-256 in the repository index, which
// is what apt itself verifies, and the index is fetched over HTTPS. That
// catches a truncated or corrupted download; it does not, unlike the pinned
// digests in UnityFetcher, defend against the repository itself changing. The
// versions actually used are recorded in <root>/manifest.txt so that a build
// which starts failing can be traced to what moved.

package dev.silksong.launcher

import android.system.ErrnoException
import android.system.Os
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import org.apache.commons.compress.compressors.xz.XZCompressorInputStream
import org.apache.commons.compress.compressors.zstandard.ZstdCompressorInputStream
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.zip.GZIPInputStream
import java.util.zip.Inflater
import java.util.zip.InflaterInputStream
import kotlin.coroutines.coroutineContext

object ToolchainFetcher {

    // ── Termux ─────────────────────────────────────────────────────────────

    private const val TERMUX_REPO = "https://packages.termux.dev/apt/termux-main"
    private const val TERMUX_INDEX = "$TERMUX_REPO/dists/stable/main/binary-aarch64/Packages.gz"

    /**
     * Termux builds everything for its own install path, so that is what the
     * archives contain. Stripped on extraction: our tree is rooted under the
     * app's own files directory, and the binaries find their libraries
     * through LD_LIBRARY_PATH rather than the baked-in RUNPATH.
     */
    private const val TERMUX_PREFIX = "data/data/com.termux/files/"

    /**
     * clang and lld are the point; the rest is what they refuse to start
     * without. libllvm is the bulk of it (clang is a thin driver over it), and
     * libicu/libxml2/ncurses are pulled in by LLVM's diagnostics and its
     * driver, not by anything we ask for.
     */
    private val TERMUX_PACKAGES = listOf(
        "clang", "libllvm", "lld", "libcompiler-rt", "binutils",
        "libc++", "libffi", "libxml2", "libicu", "ncurses", "zlib", "zstd",
        "liblzma", "libiconv",
    )

    // ── NDK ────────────────────────────────────────────────────────────────

    // r27 is what Unity 6000.0 ships, so its prebuilt baselib.a was compiled
    // against this sysroot's libc++ ABI. Mixing that with a different NDK
    // generation is exactly the sort of thing that links cleanly and then
    // fails at dlopen.
    private const val NDK = "r27c"
    private const val NDK_URL = "https://dl.google.com/android/repository/android-ndk-$NDK-linux.zip"
    private const val NDK_ROOT = "android-ndk-$NDK/toolchains/llvm/prebuilt/linux-x86_64/sysroot/"

    /**
     * All the headers, and the aarch64 stub libraries for every API level.
     *
     * The libraries could be narrowed to the one API level being targeted --
     * it is 9 MB against 0.3 MB -- but they are the part that makes the
     * sysroot usable for a different target later, and the saving is not worth
     * a fetch that has to be redone when ANDROID_API changes.
     */
    private val NDK_WANTED = listOf(
        "usr/include/",
        "usr/lib/aarch64-linux-android/",
    )

    data class Progress(
        val step: String,
        /** 0f..1f within the current step, or -1f when the size is unknown. */
        val fraction: Float,
        val detail: String = "",
    )

    fun rootFor(context: android.content.Context): File = Toolchain.rootFor(context)

    /** The compiler driver, once staged. */
    fun clang(root: File): File = File(root, "usr/bin/clang")

    /** The NDK sysroot, once staged. */
    fun sysroot(root: File): File = File(root, "sysroot")

    /**
     * True once both halves are on disk and the compiler has been proven to
     * work.
     *
     * The marker is written only after a real compile and link has succeeded,
     * so this cannot report a toolchain that downloaded but does not run. The
     * files are checked as well, because a marker on its own would survive
     * part of the tree being cleared.
     */
    fun isPresent(root: File): Boolean =
        File(root, "verified").isFile &&
            clang(root).canExecute() &&
            File(sysroot(root), "usr/include/stdio.h").isFile &&
            File(sysroot(root), "usr/lib/aarch64-linux-android/33/libc.so").isFile

    /**
     * Fetches whatever is missing. Safe to re-run: each package is skipped
     * when its files are already extracted, so an interrupted first attempt is
     * resumed by running this again.
     */
    fun fetch(root: File): Flow<Progress> = channelFlow {
        root.mkdirs()

        val index: Map<String, Package>
        if (TERMUX_PACKAGES.any { !staged(root, it) }) {
            send(Progress("Compiler index", -1f, "asking Termux what is current"))
            index = termuxIndex()
        } else {
            index = emptyMap()
        }

        val notes = StringBuilder()
        for ((n, name) in TERMUX_PACKAGES.withIndex()) {
            coroutineContext.ensureActive()
            if (staged(root, name)) continue
            val pkg = index[name] ?: throw IOException("Termux has no package named $name for aarch64")
            notes.append(pkg.name).append(' ').append(pkg.version).append('\n')
            val step = "Compiler (${n + 1}/${TERMUX_PACKAGES.size})"
            send(Progress(step, 0f, pkg.name))
            fetchDeb(root, pkg) { done, total ->
                trySend(Progress(step, done.toFloat() / total, "${pkg.name} ${mb(done, total)}"))
            }
            markStaged(root, name)
        }

        if (!File(sysroot(root), "usr/include/stdio.h").isFile) {
            send(Progress("Android sysroot", 0f, "reading the archive index"))
            fetchNdkSysroot(root) { done, total ->
                trySend(Progress("Android sysroot", done.toFloat() / total, mb(done, total)))
            }
            notes.append("android-ndk ").append(NDK).append('\n')
        }

        if (notes.isNotEmpty()) File(root, "manifest.txt").appendText(notes.toString())

        // Nothing is called ready until it has compiled something. This is the
        // last thing a fetch does, and the marker it writes is what isPresent
        // reports on.
        val verified = File(root, "verified")
        if (!verified.isFile) {
            send(Progress("Checking the compiler", -1f, "building a test library"))
            val problem = Toolchain.verify(root)
            if (problem != null) throw IOException("the fetched compiler does not work: $problem")
            verified.writeText(notes.toString())
        }
        send(Progress("Compiler ready", 1f, ""))
    }.flowOn(Dispatchers.IO)

    // ── Termux packages ────────────────────────────────────────────────────

    private data class Package(
        val name: String,
        val version: String,
        val path: String,
        val sha256: String,
        val size: Long,
    )

    /**
     * Extraction is recorded per package rather than inferred from any one
     * file, because most of these are libraries whose names we would have to
     * hardcode to check for -- and hardcoding them is how a package silently
     * stops being fetched after an upstream rename.
     */
    private fun staged(root: File, name: String): Boolean =
        File(root, "staged/$name").isFile

    private fun markStaged(root: File, name: String) {
        File(root, "staged").mkdirs()
        File(root, "staged/$name").writeText("")
    }

    private fun termuxIndex(): Map<String, Package> {
        val text = open(TERMUX_INDEX).use { GZIPInputStream(it).readBytes().toString(Charsets.UTF_8) }
        val out = HashMap<String, Package>()
        for (stanza in text.split("\n\n")) {
            if (stanza.isBlank()) continue
            val f = HashMap<String, String>()
            var key: String? = null
            for (line in stanza.lineSequence()) {
                if (line.startsWith(" ") || line.startsWith("\t")) continue
                val i = line.indexOf(':')
                if (i <= 0) continue
                key = line.substring(0, i)
                f[key] = line.substring(i + 1).trim()
            }
            val name = f["Package"] ?: continue
            if (name !in TERMUX_PACKAGES) continue
            val path = f["Filename"] ?: continue
            val sha = f["SHA256"] ?: continue
            // A repository can carry several versions of a package. Later
            // stanzas win, which is what apt does, and for a rolling repo that
            // is the newest.
            out[name] = Package(name, f["Version"] ?: "?", path, sha, f["Size"]?.toLongOrNull() ?: -1)
        }
        return out
    }

    private suspend fun fetchDeb(root: File, pkg: Package, onProgress: (Long, Long) -> Unit) {
        // A .deb has to be on disk before it can be trusted: the digest covers
        // the whole file, so nothing inside may be written out until all of it
        // has arrived and matched. They are small enough that this costs
        // nothing -- the largest is 30 MB.
        val tmp = File(root, "dl").apply { mkdirs() }
        val deb = File(tmp, "${pkg.name}.deb")
        if (deb.length() != pkg.size) {
            open("$TERMUX_REPO/${pkg.path}").use { input ->
                deb.outputStream().use { out ->
                    copy(input, out, pkg.size, onProgress)
                }
            }
        }
        val got = sha256(deb)
        if (got != pkg.sha256) {
            deb.delete()
            throw IOException("${pkg.name}: expected sha256 ${pkg.sha256} but got $got")
        }
        deb.inputStream().buffered().use { extractDeb(it, root) }
        deb.delete()
    }

    /**
     * Unpacks the data member of a .deb.
     *
     * ar is parsed here rather than with commons-compress: the format is a
     * magic number and a sequence of 60-byte plain-text headers, and doing it
     * directly keeps the stream single-pass, which matters because these are
     * read straight from the file rather than seeked around in.
     *
     * [strip] is the archive-side prefix to remove. Termux builds for its own
     * install path so its members are rooted at data/data/com.termux/files/;
     * Debian's are rooted at the filesystem root and strip nothing.
     */
    internal suspend fun extractDeb(input: InputStream, root: File, strip: String = TERMUX_PREFIX) {
        val magic = ByteArray(8)
        DataInputStream(input).readFully(magic)
        if (String(magic, Charsets.US_ASCII) != "!<arch>\n") throw IOException("not an ar archive")
        while (true) {
            val header = ByteArray(60)
            var read = 0
            while (read < 60) {
                val n = input.read(header, read, 60 - read)
                if (n < 0) break
                read += n
            }
            if (read < 60) return
            val name = String(header, 0, 16, Charsets.US_ASCII).trim().trimEnd('/')
            val size = String(header, 48, 10, Charsets.US_ASCII).trim().toLongOrNull()
                ?: throw IOException("bad ar member size for $name")
            if (name.startsWith("data.tar")) {
                val bounded = BoundedInputStream(input, size)
                val buffered = BufferedInputStream(bounded)
                val decompressed = when {
                    name.endsWith(".xz") -> XZCompressorInputStream(buffered)
                    name.endsWith(".gz") -> java.util.zip.GZIPInputStream(buffered)
                    name.endsWith(".zst") -> ZstdCompressorInputStream(buffered)
                    // Anything else is a change we have not tested against, so
                    // say which one rather than failing later with something
                    // about a corrupt archive.
                    else -> throw IOException("unsupported deb payload: $name")
                }
                TarArchiveInputStream(decompressed).use { extractDebTar(it, root, strip) }
                bounded.drain()
                return
            }
            skipFully(input, size + (size and 1L))
        }
    }

    private suspend fun extractDebTar(tar: TarArchiveInputStream, root: File, strip: String) {
        while (true) {
            coroutineContext.ensureActive()
            val entry = tar.nextEntry ?: break
            val raw = entry.name.removePrefix("./")
            if (!raw.startsWith(strip)) continue
            val rel = raw.removePrefix(strip)
            if (rel.isEmpty()) continue
            val out = File(root, rel)
            when {
                entry.isDirectory -> out.mkdirs()
                entry.isSymbolicLink -> symlink(entry.linkName, out)
                entry.isLink -> {
                    // Hard link inside the archive: the target has already
                    // gone past, so copying it is both correct and simpler
                    // than trying to link across app storage.
                    val target = File(root, entry.linkName.removePrefix("./").removePrefix(strip))
                    if (target.isFile) {
                        out.parentFile?.mkdirs()
                        target.copyTo(out, overwrite = true)
                        applyMode(out, entry.mode)
                    }
                }
                entry.isFile -> {
                    out.parentFile?.mkdirs()
                    out.outputStream().use { o -> tar.copyTo(o, 1 shl 16) }
                    applyMode(out, entry.mode)
                }
            }
        }
    }

    // ── NDK sysroot ────────────────────────────────────────────────────────

    private class ZipEntryInfo(
        val name: String,
        val offset: Long,
        val compressed: Long,
        val method: Int,
        val mode: Int,
    )

    /**
     * Pulls the sysroot out of the NDK zip without downloading the NDK.
     *
     * Three ranged reads: the tail to find the central directory, the central
     * directory itself, and then one span covering the wanted entries. The
     * span is read sequentially and abandoned as soon as the last wanted entry
     * has been extracted, so what actually crosses the network is close to the
     * 12.6 MB the entries occupy rather than the 664 MB the archive does.
     */
    private suspend fun fetchNdkSysroot(root: File, onProgress: (Long, Long) -> Unit) {
        val total = contentLength(NDK_URL)
        val tailLen = minOf(TAIL_BYTES, total)
        val tail = openRange(NDK_URL, total - tailLen, total - 1).use { it.readBytes() }

        var i = tail.lastIndexOf(EOCD_SIG)
        if (i < 0) throw IOException("no zip end-of-central-directory in the NDK archive")
        var cdSize = readInt(tail, i + 12)
        var cdOffset = readInt(tail, i + 16)
        if (cdOffset == 0xFFFFFFFFL || cdSize == 0xFFFFFFFFL) {
            val j = tail.lastIndexOf(EOCD64_SIG)
            if (j < 0) throw IOException("zip64 archive without a zip64 end record")
            cdSize = readLong(tail, j + 40)
            cdOffset = readLong(tail, j + 48)
        }

        val cd = openRange(NDK_URL, cdOffset, cdOffset + cdSize - 1).use { it.readBytes() }
        val wanted = centralDirectory(cd)
            .filter { e -> NDK_WANTED.any { e.name.startsWith(NDK_ROOT + it) } }
            .sortedBy { it.offset }
        if (wanted.isEmpty()) throw IOException("the NDK archive has no sysroot at $NDK_ROOT")

        val dest = sysroot(root)
        val first = wanted.first().offset
        val bytes = wanted.sumOf { it.compressed }
        var done = 0L
        var cursor = first
        // Ends at the central directory: the last wanted entry's data stops
        // somewhere before it, and the stream is closed the moment that entry
        // has been read, so the tail is never transferred.
        openRange(NDK_URL, first, cdOffset - 1).use { raw ->
            val input = BufferedInputStream(raw, 1 shl 16)
            for (entry in wanted) {
                coroutineContext.ensureActive()
                skipFully(input, entry.offset - cursor)
                cursor = entry.offset
                // The local header repeats the name and may carry a different
                // extra field from the central one, so its lengths have to be
                // read from the local header itself.
                val local = ByteArray(30)
                DataInputStream(input).readFully(local)
                if (readInt(local, 0) != LFH_SIG_INT) throw IOException("bad local header for ${entry.name}")
                val nameLen = readShort(local, 26)
                val extraLen = readShort(local, 28)
                skipFully(input, (nameLen + extraLen).toLong())
                cursor += 30 + nameLen + extraLen

                val rel = entry.name.removePrefix(NDK_ROOT)
                val out = File(dest, rel)
                val body = BoundedInputStream(input, entry.compressed)
                when {
                    rel.isEmpty() || rel.endsWith("/") -> out.mkdirs()
                    isSymlink(entry.mode) -> symlink(decode(body, entry.method).toString(Charsets.UTF_8), out)
                    else -> {
                        out.parentFile?.mkdirs()
                        out.outputStream().use { o -> stream(body, entry.method).copyTo(o, 1 shl 16) }
                        if (entry.mode != 0) applyMode(out, entry.mode)
                    }
                }
                body.drain()
                cursor += entry.compressed
                done += entry.compressed
                onProgress(done, bytes)
            }
        }
    }

    private fun centralDirectory(cd: ByteArray): List<ZipEntryInfo> {
        val out = ArrayList<ZipEntryInfo>()
        var o = 0
        while (o + 46 <= cd.size && readInt(cd, o) == CDH_SIG_INT) {
            val method = readShort(cd, o + 10)
            var compressed = readInt(cd, o + 20)
            val nameLen = readShort(cd, o + 28)
            val extraLen = readShort(cd, o + 30)
            val commentLen = readShort(cd, o + 32)
            val external = readInt(cd, o + 38)
            var offset = readInt(cd, o + 42)
            val name = String(cd, o + 46, nameLen, Charsets.UTF_8)
            if (compressed == 0xFFFFFFFFL || offset == 0xFFFFFFFFL) {
                var k = o + 46 + nameLen
                val end = k + extraLen
                while (k + 4 <= end) {
                    val id = readShort(cd, k)
                    val len = readShort(cd, k + 2)
                    if (id == 1) {
                        var p = k + 4
                        // Uncompressed size comes first and is skipped when
                        // present; only the fields that were saturated are
                        // actually stored here.
                        if (readInt(cd, o + 24) == 0xFFFFFFFFL) p += 8
                        if (compressed == 0xFFFFFFFFL) { compressed = readLong(cd, p); p += 8 }
                        if (offset == 0xFFFFFFFFL) { offset = readLong(cd, p) }
                        break
                    }
                    k += 4 + len
                }
            }
            out.add(ZipEntryInfo(name, offset, compressed, method, (external ushr 16).toInt()))
            o += 46 + nameLen + extraLen + commentLen
        }
        return out
    }

    private fun stream(input: InputStream, method: Int): InputStream = when (method) {
        0 -> input
        8 -> InflaterInputStream(input, Inflater(true), 1 shl 16)
        else -> throw IOException("unsupported zip compression method $method")
    }

    private fun decode(input: InputStream, method: Int): ByteArray {
        val out = ByteArrayOutputStream()
        stream(input, method).copyTo(out)
        return out.toByteArray()
    }

    private fun isSymlink(mode: Int) = mode != 0 && (mode and 0xF000) == 0xA000

    // ── shared plumbing ────────────────────────────────────────────────────

    internal fun symlink(target: String, out: File) {
        out.parentFile?.mkdirs()
        // Os.symlink fails outright if anything is already there, which a
        // re-run after an interrupted fetch guarantees.
        if (out.exists() || isDanglingLink(out)) out.delete()
        try {
            Os.symlink(target, out.absolutePath)
        } catch (e: ErrnoException) {
            throw IOException("could not link $out -> $target: ${e.message}", e)
        }
    }

    /** A link whose target is missing: exists() is false but delete() works. */
    private fun isDanglingLink(f: File): Boolean = try {
        Os.lstat(f.absolutePath); true
    } catch (_: ErrnoException) {
        false
    }

    /**
     * Applies the archive's permission bits.
     *
     * Only the execute bit really matters, and it matters a great deal: a
     * clang that arrives without it fails with "Permission denied", which
     * reads exactly like the SELinux refusal this project spent a while
     * getting out of the way.
     */
    internal fun applyMode(f: File, mode: Int) {
        val m = mode and 0x1FF
        if (m == 0) return
        try {
            Os.chmod(f.absolutePath, m or 0x180)
        } catch (_: ErrnoException) {
            f.setReadable(true, true)
            if (m and 0x40 != 0) f.setExecutable(true, true)
        }
    }

    internal fun sha256(f: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        f.inputStream().use { input ->
            val buf = ByteArray(1 shl 16)
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                digest.update(buf, 0, n)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    internal fun copy(input: InputStream, out: java.io.OutputStream, total: Long, onProgress: (Long, Long) -> Unit) {
        val buf = ByteArray(1 shl 16)
        var done = 0L
        var reported = 0L
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            out.write(buf, 0, n)
            done += n
            if (done - reported >= 1L shl 20) { reported = done; onProgress(done, total) }
        }
        onProgress(done, total)
    }

    internal fun skipFully(input: InputStream, count: Long) {
        var left = count
        val scratch = ByteArray(1 shl 16)
        while (left > 0) {
            val skipped = input.skip(left)
            if (skipped > 0) { left -= skipped; continue }
            val n = input.read(scratch, 0, minOf(left, scratch.size.toLong()).toInt())
            if (n < 0) throw IOException("archive ended $left bytes early")
            left -= n
        }
    }

    internal fun mb(done: Long, total: Long) =
        if (total > 0) "${done / 1024 / 1024} of ${total / 1024 / 1024} MB" else "${done / 1024 / 1024} MB"

    internal fun contentLength(url: String): Long {
        val c = URL(url).openConnection() as HttpURLConnection
        c.requestMethod = "HEAD"
        c.connectTimeout = 30_000
        c.readTimeout = 60_000
        // Same reason as openRange: this server varies on Accept-Encoding and
        // reports a different length for each, and the number is about to be
        // used to compute byte ranges. Asking for one encoding here and
        // getting another there is how the offsets end up pointing into a
        // file that was never downloaded.
        c.setRequestProperty("Accept-Encoding", "identity")
        try {
            if (c.responseCode !in 200..299) throw IOException("HTTP ${c.responseCode} for $url")
            val len = c.getHeaderField("Content-Length")?.toLongOrNull()
            return len ?: throw IOException("no Content-Length for $url")
        } finally {
            c.disconnect()
        }
    }

    internal fun open(url: String): InputStream = openRange(url, null, null)

    /**
     * A GET, optionally for a byte range.
     *
     * Accept-Encoding is set explicitly, and that is not cosmetic. Android's
     * HttpURLConnection is OkHttp, which adds "Accept-Encoding: gzip" of its
     * own accord and transparently decodes the reply. Ranges are applied by
     * the server to the *encoded* entity, so the two features do not compose:
     * dl.google.com serves this archive as 663,987,688 bytes identity and
     * 654,475,088 bytes gzipped, and a range computed against the first was
     * answered against the second -- 416 for the tail, and for anything else a
     * window onto the wrong bytes, which OkHttp then tried to gunzip and threw
     * "ID1ID2: actual 0x000014fd != expected 0x00001f8b".
     *
     * Naming the encoding also switches OkHttp's transparent decoding off,
     * which is what we want everywhere here: Packages.gz is gzip *content*
     * that this code decompresses itself, not a gzip transfer-coding.
     */
    internal fun openRange(url: String, from: Long?, to: Long?): InputStream {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 30_000
        c.readTimeout = 60_000
        c.instanceFollowRedirects = true
        c.setRequestProperty("Accept-Encoding", "identity")
        if (from != null) c.setRequestProperty("Range", "bytes=$from-${to ?: ""}")
        val code = c.responseCode
        if (code !in 200..299) {
            c.disconnect()
            throw IOException("HTTP $code for $url")
        }
        if (from != null && code != 206) {
            c.disconnect()
            throw IOException("$url ignored a range request (HTTP $code)")
        }
        return c.inputStream
    }

    private const val TAIL_BYTES = 70_000L
    private val EOCD_SIG = byteArrayOf(0x50, 0x4B, 0x05, 0x06)
    private val EOCD64_SIG = byteArrayOf(0x50, 0x4B, 0x06, 0x06)
    private const val LFH_SIG_INT = 0x04034B50L
    private const val CDH_SIG_INT = 0x02014B50L

    private fun readShort(b: ByteArray, o: Int) = (b[o].toInt() and 0xFF) or ((b[o + 1].toInt() and 0xFF) shl 8)

    private fun readInt(b: ByteArray, o: Int): Long =
        (b[o].toLong() and 0xFF) or ((b[o + 1].toLong() and 0xFF) shl 8) or
            ((b[o + 2].toLong() and 0xFF) shl 16) or ((b[o + 3].toLong() and 0xFF) shl 24)

    private fun readLong(b: ByteArray, o: Int): Long {
        var v = 0L
        for (i in 7 downTo 0) v = (v shl 8) or (b[o + i].toLong() and 0xFF)
        return v
    }

    private fun ByteArray.lastIndexOf(needle: ByteArray): Int {
        outer@ for (i in size - needle.size downTo 0) {
            for (j in needle.indices) if (this[i + j] != needle[j]) continue@outer
            return i
        }
        return -1
    }

    /** A fixed-length window onto a shared stream that must not be closed. */
    internal class BoundedInputStream(
        private val input: InputStream,
        private var remaining: Long,
    ) : InputStream() {
        override fun read(): Int {
            if (remaining <= 0) return -1
            val b = input.read()
            if (b >= 0) remaining--
            return b
        }

        override fun read(b: ByteArray, off: Int, len: Int): Int {
            if (remaining <= 0) return -1
            val n = input.read(b, off, minOf(len.toLong(), remaining).toInt())
            if (n > 0) remaining -= n
            return n
        }

        override fun available(): Int = minOf(remaining, input.available().toLong()).toInt()

        /** Consumes whatever the reader above did not, keeping the shared
         *  stream aligned on the next member. */
        fun drain() {
            while (remaining > 0) {
                val before = remaining
                val scratch = ByteArray(minOf(remaining, 1L shl 16).toInt())
                val n = input.read(scratch)
                if (n < 0) { remaining = 0; return }
                remaining -= n
                if (remaining == before) return
            }
        }

        override fun close() {
            // Deliberately not closing the shared stream.
        }
    }
}
