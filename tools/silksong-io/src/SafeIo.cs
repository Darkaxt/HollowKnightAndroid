// SafeIo -- File.Replace, for devices where File.Replace does not work.
//
// Silksong commits both a save and its shared data by writing a temp beside
// the real file and swapping it in with File.Replace:
//
//     DesktopPlatform.WriteSaveSlot:  File.Replace(tmp, userN.dat, bak + n)
//     JsonSharedData.WriteAllBytesSafe: File.Replace(tmp, shared.dat, bak, true)
//
// On some devices that call fails, every single time, with:
//
//     IOException: Invalid argument
//       at System.IO.FileSystem.ReplaceFile (...)
//
// Reported on an AYANEO Pocket FIT Elite (Android 16) with 134 GB free, so it
// is neither space nor permissions. ReplaceFile needs a hard link to stand the
// backup up, and Android's FUSE-backed emulated storage has never had hard
// links. Rename and unlink, by contrast, work fine there: FUSE resolves those
// through a package-ownership check rather than the caller's POSIX
// credentials.
//
// The effect on a player is that the game cannot save at all after the first
// time. Note the asymmetry in WriteSaveSlot: the FIRST save of a slot takes
// File.Move, a plain rename, and works everywhere. Only once userN.dat exists
// does every later save go through File.Replace. "New game saves once, then
// never again" is the shape of this bug.
//
// This type exists so that the two call sites above can be pointed at it
// instead, by a Cecil pass over Assembly-CSharp before IL2CPP converts it.
// It lives in its own assembly because SilksongPatches is compiled AGAINST
// Assembly-CSharp, and having Assembly-CSharp reference it back would be a
// circular assembly reference. Nothing here references the game; UnityEngine
// is fine, because UnityEngine does not reference Assembly-CSharp either.
//
// The normal path is tried FIRST and is bit-for-bit what the game did before.
// A device where File.Replace works therefore executes the same call it always
// did and never reaches a line of the fallback, which is what keeps this from
// being a regression risk for the many devices that were never broken.

using System;
using System.IO;
using UnityEngine;

public static class SafeIo
{
    // Set once File.Replace has been shown not to work here. It will not start
    // working later, and without this every save re-runs the doomed call and
    // fills the log with the same stack.
    static volatile bool _replaceUnusable;

    /// <summary>
    /// Forces the rename path, for testing it on a device that does not need it.
    ///
    /// Every device we can get our hands on takes the File.Replace branch and
    /// succeeds, which means the branch that matters to the people actually
    /// affected is the one that never runs here. Dropping this file beside the
    /// saves makes it run, so the fallback can be exercised on working
    /// hardware instead of being shipped on the strength of a code review:
    ///
    ///     adb shell touch /sdcard/Android/data/&lt;pkg&gt;/files/force-rename-saves
    ///
    /// Read once. It is also a support lever -- a player whose saves break in
    /// some new way can be asked to create it -- which is why it stays in
    /// rather than being a thing we deleted after the test.
    /// </summary>
    static bool Forced
    {
        get
        {
            if (_forcedKnown) return _forced;
            try
            {
                _forced = File.Exists(Path.Combine(
                    Application.persistentDataPath, "force-rename-saves"));
                if (_forced)
                {
                    Debug.LogWarning("[SafeIo] force-rename-saves is present: committing every "
                        + "write by rename without trying File.Replace first.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SafeIo] could not check for force-rename-saves: " + e.Message);
                _forced = false;
            }
            _forcedKnown = true;
            return _forced;
        }
    }

    static volatile bool _forced;
    static volatile bool _forcedKnown;

    /// <summary>Stands in for File.Replace(string, string, string).</summary>
    public static void Replace(string source, string destination, string backup)
    {
        Replace(source, destination, backup, false);
    }

    /// <summary>Stands in for File.Replace(string, string, string, bool).</summary>
    public static void Replace(string source, string destination, string backup, bool ignoreMetadataErrors)
    {
        Exception primary = null;

        if (!_replaceUnusable && !Forced)
        {
            try
            {
                File.Replace(source, destination, backup, ignoreMetadataErrors);
                return;
            }
            catch (Exception e)
            {
                primary = e;
                _replaceUnusable = true;
                Debug.LogWarning("[SafeIo] File.Replace is not usable on this device ("
                    + e.GetType().Name + ": " + e.Message
                    + "). Committing by rename instead, for this and every later write.");
            }
        }

        try
        {
            ReplaceByRename(source, destination, backup);
        }
        catch (Exception fallbackFailure)
        {
            if (primary == null) throw;

            // Both routes failed, which means the problem is not the one this
            // class is for -- a missing source, a full disk. Report the
            // fallback for completeness but raise the ORIGINAL, because that
            // is the truthful diagnosis of what went wrong first.
            Debug.LogError("[SafeIo] the rename fallback also failed: " + fallbackFailure);
            throw primary;
        }
    }

    /// <summary>
    /// File.Replace's contract, using only operations FUSE supports.
    ///
    /// That contract is: the existing destination is moved aside to [backup]
    /// (replacing whatever was there), the source takes its place, and the
    /// source no longer exists afterwards.
    /// </summary>
    static void ReplaceByRename(string source, string destination, string backup)
    {
        // File.Replace requires the destination to exist and throws if it does
        // not. Both callers check first, so this is only reached when a
        // previous step has already removed it -- and then a plain move is
        // exactly right.
        if (!File.Exists(destination))
        {
            File.Move(source, destination);
            return;
        }

        bool movedAside = false;
        if (backup != null)
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(destination, backup);
            movedAside = true;
        }
        else
        {
            File.Delete(destination);
        }

        try
        {
            File.Move(source, destination);
        }
        catch
        {
            // A half-done commit is worse than a failed one: it would leave
            // the slot with no save at all. Put the original back.
            if (movedAside)
            {
                try
                {
                    if (!File.Exists(destination)) File.Move(backup, destination);
                }
                catch (Exception restoreFailure)
                {
                    Debug.LogError("[SafeIo] could not restore " + destination
                        + " after a failed commit: " + restoreFailure);
                }
            }
            throw;
        }
    }
}
