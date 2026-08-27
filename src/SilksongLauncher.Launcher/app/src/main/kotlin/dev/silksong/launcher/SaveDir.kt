// SaveDir — the game's save directory, made safe before the game is given it.
//
// This exists because of a defect in the game's own save path. Silksong's
// DesktopPlatform.WriteSaveSlot writes each save to a temp beside the real
// file and then renames it into place:
//
//     string text = saveSlotPath + ".new";
//     try { File.WriteAllBytes(text, bytes); }
//     catch (Exception e) { Debug.LogException(e); }        // swallowed
//     try {
//         if (File.Exists(saveSlotPath))
//             File.Replace(text, saveSlotPath, backup + GetBackupNumber(...));
//         else File.Move(text, saveSlotPath);
//         successful = true;                                 // reports success
//     }
//
// The write and the rename are in SEPARATE try blocks and a failed write does
// not stop the rename. So whatever happens to be sitting at `userN.dat.new` is
// promoted over the real save, and the save is reported as having SUCCEEDED --
// no SAVE_FAILED, nothing on screen, and nothing in any log a user can send.
//
// Verified on an AYN Thor (Android 13): a stale userN.dat.new owned by another
// uid made WriteAllBytes fail with UnauthorizedAccessException, the rename
// promoted the stale file regardless, and the slot came back as
//
//     SerializationException: The input stream is not a valid binary format.
//
// with the real save shunted into userN.dat.bakN. The game had said nothing.
//
// The game also claims to clear these temps, and does not:
//
//     if (File.Exists(text))
//         Debug.LogWarning("Temp file ... The file has been deleted.");
//
// There is no delete on that path. So a stranded temp survives and poisons
// every later save of that slot, not just the one that stranded it. Removing
// them before the game runs is the main thing this file is for.
//
// What this CANNOT fix is a temp created and only partly written inside a
// single save -- a full disk does exactly that, and the partial file is then
// promoted by the same rename. Fixing that means rewriting the game's method
// rather than preparing its directory. What is fixed here is every temp that
// outlived the save that made it, which is the case this port can reach from
// outside the engine.

package dev.silksong.launcher

import android.content.Context
import android.os.StatFs
import java.io.File

object SaveDir {

    /**
     * Silksong reads <persistentDataPath>/<userId>/userN.dat, and falls back
     * to "default" when no online subsystem supplies a userId -- which the
     * Android build never does. Shared with CloudSync so that the launcher has
     * exactly one idea of where saves live.
     */
    const val SUBDIR = "default"

    /** The temp each save is written through, and stranded when one fails. */
    private const val TEMP_SUFFIX = ".dat.new"

    /** Below this, a save is at real risk of being half-written. */
    private const val LOW_SPACE_MB = 256L

    private const val PROBE = ".silksong-save-probe"

    /**
     * Where the saves are.
     *
     * getExternalFilesDir is what Unity reports as Application.persistentDataPath
     * on this platform, so this resolves to the same directory the game works
     * out for itself, without either side having to know the other's
     * convention. The fallback matches the game's own behaviour when external
     * storage is not mounted.
     */
    fun of(context: Context): File {
        val external = context.getExternalFilesDir(null) ?: context.filesDir
        return File(external, SUBDIR)
    }

    /**
     * Everything worth doing to the save directory before the game opens it.
     *
     * Called on the launch path rather than at startup: this is the last
     * moment before the engine takes ownership of the directory, and it is
     * also the moment whose log lines are the ones attached to a report about
     * the session that follows.
     *
     * Never throws. A launcher that refuses to start the game because it could
     * not tidy a directory is worse than the bug it is tidying up after.
     */
    fun prepare(context: Context) {
        try {
            val dir = of(context)
            if (!dir.isDirectory) {
                // The game does NOT create this itself -- WriteSaveSlot has no
                // CreateDirectory, and today it only works because
                // JsonSharedData.Save happens to run first and makes it as a
                // side effect. That is an ordering accident, not a guarantee.
                if (dir.mkdirs()) {
                    LauncherLog.log("save dir: created ${dir.absolutePath}")
                } else {
                    LauncherLog.log(
                        "save dir: COULD NOT create ${dir.absolutePath} -- the game will not be able to save"
                    )
                    return
                }
            }
            sweepTemps(dir)
            probe(dir)
            reportSpace(dir)
            reportForeign(dir)
        } catch (t: Throwable) {
            LauncherLog.log("save dir: could not be prepared", t)
        }
    }

    /**
     * Deletes save temps left by a save that did not finish.
     *
     * Logged loudly rather than done quietly, because a stranded temp means
     * the LAST save failed and the user was never told. That is the single
     * most useful thing a report about saving can carry, so its name, size and
     * age go in the log even though the remedy is only to delete it.
     *
     * Deleting a file owned by another uid needs write permission on the
     * DIRECTORY and not on the file, so this works on the hand-copied temps
     * that cause the problem in the first place.
     */
    private fun sweepTemps(dir: File) {
        val temps = dir.listFiles { f -> f.isFile && f.name.endsWith(TEMP_SUFFIX) }
        if (temps == null || temps.isEmpty()) return
        for (f in temps) {
            // Both read BEFORE the delete: File.length() on a file that is no
            // longer there is 0, and the size is most of why this line exists.
            val bytes = f.length()
            val ageMin = (System.currentTimeMillis() - f.lastModified()) / 60000L
            val gone = runCatching { f.delete() }.getOrDefault(false)
            LauncherLog.log(
                if (gone) {
                    "save dir: removed a stranded save temp ${f.name} ($bytes bytes, " +
                        "$ageMin min old) -- the previous save of that slot FAILED, and left " +
                        "behind a file the game would have promoted over the real save"
                } else {
                    "save dir: could NOT remove the stranded save temp ${f.name} " +
                        "($bytes bytes) -- the next save of that slot will overwrite " +
                        "the real save with it"
                }
            )
        }
    }

    /**
     * Whether the game will be able to write here at all, asked by writing.
     *
     * canWrite() answers from the mode bits, which are not the whole story on
     * a FUSE-backed volume, so the only reliable question is the real one.
     * Creating a NEW file is the right probe: that is what a save does, and it
     * succeeds even where the directory belongs to another uid, because it
     * needs the directory's write bit rather than the directory's owner.
     */
    private fun probe(dir: File) {
        val p = File(dir, PROBE)
        try {
            p.delete()
            if (!p.createNewFile()) throw java.io.IOException("createNewFile returned false")
        } catch (t: Throwable) {
            LauncherLog.log(
                "save dir: NOT WRITABLE (${dir.absolutePath}) -- the game will not be able to save",
                t
            )
            return
        } finally {
            runCatching { p.delete() }
        }
    }

    /**
     * Free space, and a warning when there is little.
     *
     * Worth a line in every log rather than only when it is low: a save that
     * runs out of space part way through is promoted over the real save by the
     * same rename as any other, so "how full was the device" is a question
     * every save-related report has to answer and almost none of them do.
     */
    private fun reportSpace(dir: File) {
        try {
            val freeMb = StatFs(dir.absolutePath).availableBytes / (1024L * 1024L)
            LauncherLog.log(
                if (freeMb < LOW_SPACE_MB) {
                    "save dir: only $freeMb MB free -- LOW. A save that runs out of space part " +
                        "way through is silently promoted over the real save."
                } else {
                    "save dir: $freeMb MB free"
                }
            )
        } catch (t: Throwable) {
            LauncherLog.log("save dir: could not read free space", t)
        }
    }

    /**
     * Notes files that something other than the game put here.
     *
     * Not a fault on its own and deliberately not repaired: replacing a save
     * happens by rename, which cares about the directory and not about who
     * owns the file, so a hand-copied save loads and saves perfectly well.
     * It is logged because it says how the directory came to be the way it is,
     * which is worth knowing when the next report arrives.
     */
    private fun reportForeign(dir: File) {
        try {
            val uid = android.system.Os.getuid()
            val foreign = dir.listFiles().orEmpty().filter { f ->
                runCatching { android.system.Os.stat(f.absolutePath).st_uid != uid }
                    .getOrDefault(false)
            }
            if (foreign.isEmpty()) return
            LauncherLog.log(
                "save dir: ${foreign.size} entr(ies) not owned by this app " +
                    "(${foreign.joinToString { it.name }}) -- copied in from outside the game"
            )
        } catch (t: Throwable) {
            LauncherLog.log("save dir: could not check ownership", t)
        }
    }
}
