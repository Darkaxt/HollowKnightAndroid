// BuildReset — throw away the ported game, keep everything expensive.
//
// A port can end up wrong in ways nothing detects: a build interrupted at the
// wrong moment, a patch that changed under a stamp, an engine that loads and
// then misbehaves. The answer was to uninstall the app, which also threw away
// the depot -- around 8 GB fetched from Steam -- and the player's saves. That
// is a bad trade for "try building it again".
//
// So this deletes what the build PRODUCED and nothing that was DOWNLOADED:
//
//   gone    build/          the on-device build: generated C++, objects,
//                           libil2cpp.so, the player image, and the stamps
//                           that say those are current
//           pkg/            the packaged player the engine actually loads
//           unity-dex/      the player's Java classes, dexed; seconds to redo
//           aa              the symlink to the content tree, remade every run
//           staging/        transient, and only ever holds a partial copy
//           depot-staging/
//
//   kept    depot/          the game, ~8 GB, and the user's own property
//           unity/          Unity's Android player module, ~640 MB
//           toolchain/      clang and .NET, several hundred MB
//           default/        THE SAVES
//           game-settings.txt, launcher.log
//
// The kept list is the point. Everything in it is either expensive to fetch
// again or impossible to replace, and none of it is what a bad port is made
// of -- the depot is verified against Steam's manifest, and the toolchain
// against a marker written only after a real compile and link succeeded.
//
// Anything not named here is left alone. Directories from older layouts of
// this app are still on some devices, and deleting a directory because it is
// unfamiliar is how a feature meant to save a download costs one.

package dev.silksong.launcher

import android.content.Context
import java.io.File

object BuildReset {

    /** Under the app's external files dir. */
    private val EXTERNAL = listOf("build", "staging", "depot-staging")

    /** Under filesDir, on internal storage. */
    private val INTERNAL = listOf("pkg", "unity-dex", "aa")

    /**
     * Deletes the build. Returns roughly how many bytes it freed.
     *
     * Slow -- the build is gigabytes across thousands of small files -- so
     * this must not be called on the main thread.
     */
    fun clear(context: Context): Long {
        var freed = 0L

        val external = context.getExternalFilesDir(null)
        if (external != null) {
            for (name in EXTERNAL) freed += remove(File(external, name))
        } else {
            LauncherLog.log("clear: no external files dir; the build may be left behind")
        }

        for (name in INTERNAL) freed += remove(File(context.filesDir, name))

        LauncherLog.log("clear: freed ${freed / 1024 / 1024} MB; the game will be built again")
        return freed
    }

    /** True once there is something worth clearing. */
    fun hasBuild(context: Context): Boolean {
        if (File(File(context.filesDir, "pkg"), ".built").isFile) return true
        val external = context.getExternalFilesDir(null) ?: return false
        return File(external, "build").isDirectory
    }

    private fun remove(target: File): Long {
        val tally = Tally()
        deleteTree(target, tally)
        if (tally.bytes > 0 || tally.failures > 0) {
            LauncherLog.log(
                "clear: ${if (tally.failures == 0) "removed" else "could not fully remove"} " +
                    "${target.name} (${tally.bytes / 1024 / 1024} MB" +
                    if (tally.failures > 0) ", ${tally.failures} left behind)" else ")",
            )
        }
        return tally.bytes
    }

    /** Carried down the walk, so nothing is accumulated in shared state. */
    private class Tally {
        var bytes = 0L
        var failures = 0
    }

    /**
     * Deletes a tree WITHOUT ever following a symbolic link.
     *
     * File.deleteRecursively cannot be used here, and the reason is not
     * theoretical. filesDir/aa is a link to the depot's StreamingAssets/aa
     * (PlayerImage.linkContent), and a walker that lists a linked directory
     * gets the TARGET's children -- so deleting the link recursively deletes
     * several gigabytes of the game this is trying not to touch. Older builds
     * have the same shape inside build/, where usr and sysroot were links into
     * the toolchain on devices whose filesystem allowed it.
     *
     * So every entry is lstat'ed, and a link is unlinked rather than entered.
     */
    private fun deleteTree(target: File, tally: Tally) {
        val kind = kindOf(target) ?: return   // already gone

        when (kind) {
            // Unlink. Whatever it pointed at is left exactly as it was.
            Kind.Link -> if (!target.delete()) tally.failures++

            Kind.Directory -> {
                val children = target.listFiles()
                if (children != null) for (child in children) deleteTree(child, tally)
                if (!target.delete()) tally.failures++
            }

            Kind.File -> {
                val size = target.length()
                if (target.delete()) tally.bytes += size else tally.failures++
            }
        }
    }

    private enum class Kind { File, Directory, Link }

    /**
     * What this path is, without resolving it.
     *
     * lstat rather than File.isDirectory, because isDirectory answers for the
     * link's TARGET and so calls a link to a directory a directory. Null means
     * the path does not exist.
     */
    private fun kindOf(f: File): Kind? =
        try {
            val mode = android.system.Os.lstat(f.absolutePath).st_mode
            when {
                android.system.OsConstants.S_ISLNK(mode) -> Kind.Link
                android.system.OsConstants.S_ISDIR(mode) -> Kind.Directory
                else -> Kind.File
            }
        } catch (e: android.system.ErrnoException) {
            null
        }
}
