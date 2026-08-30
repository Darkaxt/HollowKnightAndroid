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
//           aa              the symlink to the content tree, remade every run
//           staging/        transient, and only ever holds a partial copy
//           depot-staging/
//           the game's settings   volumes, language, brightness, resolution,
//                           frame cap -- and the marker that says the
//                           first-run resolution has been decided
//           the launcher's settings
//
//   kept    depot/          the game, ~8 GB, and the user's own property
//           the game's files wherever the user put them, untouched
//           source.pointer  the selected profile's path to those game files
//           unity/          Unity's Android player module, ~640 MB
//           unity-dex/      shared dex output for that player module
//           toolchain/      clang and .NET, several hundred MB
//           default/        THE SAVES
//           the Steam sign-in
//
// The kept list is the point. Everything in it is either expensive to fetch
// again or impossible to replace, and none of it is what a bad port is made
// of -- the depot is verified against Steam's manifest, and the toolchain
// against a marker written only after a real compile and link succeeded.
//
// The sign-in is kept for the same reason: it is a QR scan on a phone, it has
// nothing to do with how the game was built, and losing it is a worse
// interruption than the one being fixed. It also lives in its own file, is
// encrypted with a key from the Android Keystore, and is the only thing here
// that cannot be reconstructed from what is on disk.
//
// Anything not named here is left alone. Directories from older layouts of
// this app are still on some devices, and deleting a directory because it is
// unfamiliar is how a feature meant to save a download costs one.

package dev.silksong.launcher

import android.content.Context
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.ProfileSettingsStore
import dev.silksong.launcher.build.GenerationPublisher
import java.io.File

object BuildReset {

    /**
     * Deletes the build. Returns roughly how many bytes it freed.
     *
     * Slow -- the build is gigabytes across thousands of small files -- so
     * this must not be called on the main thread.
     */
    fun clear(context: Context, paths: ProfileBuildPaths): Long {
        val freed = clearGenerated(paths)
        clearPreferences(context, paths)

        LauncherLog.log(
            "clear: ${paths.profile.id} freed ${freed / 1024 / 1024} MB; " +
                "the game will be built again",
        )
        return freed
    }

    /**
     * Removes only generated state owned by one profile.
     *
     * The source pointer and downloaded depot are deliberately excluded. A
     * reset rebuilds the selected game from the same validated source; it must
     * not affect another profile or force the user to find the depot again.
     * Shared Unity/toolchain state is outside [ProfileBuildPaths] and is also
     * retained.
     */
    fun clearGenerated(paths: ProfileBuildPaths): Long {
        val publisher = GenerationPublisher(paths.profilePaths)
        val generated = listOf(
            paths.buildRoot,
            paths.installStaging,
            paths.depotStaging,
            paths.packageDir,
            paths.contentLink,
        )
        return publisher.clearStaged() + publisher.clearPublished() + generated.sumOf(::remove)
    }

    /**
     * Resets the game's settings and the launcher's, but not the sign-in.
     *
     * Through the SharedPreferences API rather than by deleting the files, and
     * that is not a stylistic choice. Android keeps a crash-recovery journal
     * beside each preferences file, "<name>.xml.bak", and restores it over the
     * real one on the next load -- so a file deleted behind the API's back can
     * simply come back, with the values that were being reset. (Learned the
     * hard way: a copy made as a "backup" was restored over the edit it was
     * meant to protect.) The API also updates the in-memory copy, so nothing
     * writes the old values out again at process exit.
     *
     * The game's file is Unity's PlayerPrefs, which it names after the package.
     * It holds volumes, language, brightness, the video settings and the marker
     * that says the first-run resolution has been chosen -- so clearing it is
     * what makes a re-port feel like a first one. It does NOT hold saves; those
     * are files under the external files dir and are not touched by any of this.
     */
    private fun clearPreferences(context: Context, paths: ProfileBuildPaths) {
        // Unity's own naming. Matches the file seen on device:
        // shared_prefs/<package>.v2.playerprefs.xml
        clear(context, "${context.packageName}.v2.playerprefs", "the game's settings")
        clear(
            context,
            ProfileSettingsStore.preferenceName(paths.profile),
            "${paths.profile.displayName} launcher settings",
        )
    }

    private fun clear(context: Context, name: String, what: String) {
        try {
            val prefs = context.applicationContext
                .getSharedPreferences(name, Context.MODE_PRIVATE)
            // commit, not apply: this is followed by starting the port, and an
            // asynchronous write racing a process restart is how a reset that
            // reported success comes back.
            val ok = prefs.edit().clear().commit()
            LauncherLog.log("clear: ${if (ok) "reset" else "could not reset"} $what")
        } catch (t: Throwable) {
            LauncherLog.log("clear: could not reset $what: ${t.message}")
        }
    }

    /** True once there is something worth clearing. */
    fun hasBuild(paths: ProfileBuildPaths): Boolean =
        paths.profilePaths.currentPointer.isFile || paths.profilePaths.generations.isDirectory ||
            File(paths.packageDir, ".built").isFile || paths.buildRoot.isDirectory

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
