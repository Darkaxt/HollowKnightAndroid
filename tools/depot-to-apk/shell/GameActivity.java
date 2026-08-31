package dev.silksong.shell;

import android.os.Build;
import android.os.Bundle;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;

// The player's window otherwise stops short of the display: the system keeps
// the navigation bar's strip reserved, so the surface comes out 1920x969 on a
// 1920x1080 panel and the bottom of the game is cut off.
//
// The engine is already asking for fullscreen -- the depot's player settings
// carry androidStartInFullscreen, androidRenderOutsideSafeArea and
// androidFullscreenMode=FullScreenWindow -- but those are the settings of a
// desktop build, and nothing in a hand-assembled APK applies them to the
// window. A normal Unity build gets this from the Editor-generated manifest
// and theme, which is exactly what is not present here.
//
// So the window is configured directly. The superclass is PlayerActivity,
// ours, which hosts the Unity player without any Unity-authored code being
// compiled into this APK; it holds the mUnityPlayer field the engine's native
// side looks for.
public class GameActivity extends PlayerActivity
{
    private static final String TAG = "SilksongShell";
    private static final String PROFILE_ID_EXTRA = "dev.silksong.launcher.PROFILE_ID";

    private String profileId()
    {
        String id = getIntent() == null ? null : getIntent().getStringExtra(PROFILE_ID_EXTRA);
        if (id == null || id.isEmpty())
            throw new IllegalArgumentException("game profile is required");
        if (!id.equals("silksong") && !id.equals("hollow-knight"))
            throw new IllegalArgumentException("unsupported game profile: " + id);
        dev.silksong.launcher.runtime.GameProcessStartup.requireProfile(id);
        return id;
    }

    private dev.silksong.launcher.runtime.GameProcessStartupSnapshot startup()
    {
        return dev.silksong.launcher.runtime.GameProcessStartup.requireSnapshot();
    }

    // Application.attachBaseContext resolves this immutable package path before
    // Unity's classes are injected. The activity must never re-read `current`:
    // a generation published while the process is alive belongs to the next
    // cold process, not this one.
    private java.io.File profilePackageDir()
    {
        return new java.io.File(startup().getPackageDir());
    }

    private String profileRuntimeKey()
    {
        if (profileId().equals("silksong")) return "ss";
        if (profileId().equals("hollow-knight")) return "hk";
        throw new IllegalArgumentException("unsupported runtime profile: " + profileId());
    }

    private java.io.File profileContentLink()
    {
        return new java.io.File(getDataDir(), "p/" + profileRuntimeKey() + "/aa");
    }

    private java.io.File profileExternalDir()
    {
        java.io.File ext = getExternalFilesDir(null);
        return ext == null ? null : new java.io.File(ext, "profiles/" + profileId());
    }

    // The engine does not dlopen its libraries by path, and does not go
    // looking for them on disk. It asks Java:
    //
    //     getClassLoader().findLibrary("il2cpp")
    //
    // and dlopens whatever that returns; when it returns null the player stops
    // with "Failed to load Il2CPP." In libunity.so the JNI name strings
    // "getClassLoader" and "findLibrary" sit next to the message "Unable to
    // find library path for '%s'.", and the call site takes the error branch
    // on that lookup alone -- before extraction, which is on the far side of
    // the branch and never runs.
    //
    // That is why staging paths on disk never helped and why dlopen never
    // appeared in the linker log: the lookup is not a filesystem search.
    // findLibrary walks DexPathList's own list of directories, which is built
    // when the process starts and holds only the APK's -- a directory that,
    // by design here, no longer contains the engine.
    //
    // So the list is extended, in place, to include app storage.
    // <generation>/pkg/lib/arm64 -- mirrors <apk dir>/lib/<abi>.
    private java.io.File externalLibDir()
    {
        return new java.io.File(startup().getNativeLibraryDir());
    }

    // The game's data, as a zip, once it is built on the device rather than
    // shipped in the APK. Named .apk because that is what it stands in for.
    private java.io.File externalDataApk()
    {
        return new java.io.File(startup().getDataArchive());
    }

    private long packageVersionCode()
    {
        try
        {
            android.content.pm.PackageInfo info = getPackageManager()
                .getPackageInfo(getPackageName(), 0);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) return info.getLongVersionCode();
            return info.versionCode;
        }
        catch (Exception ignored) { return 1; }
    }

    private String obbName(String kind)
    {
        return kind + "." + packageVersionCode() + "." + getPackageName() + ".obb";
    }

    private java.io.File generationObbFile(String kind)
    {
        return new java.io.File(profilePackageDir(), obbName(kind));
    }

    private boolean hasGenerationObb()
    {
        return generationObbFile("main").isFile();
    }

    // A classic generation owns its APK and OBB as one immutable set. Return
    // that directory to Unity so it mounts the expansion in place, with no
    // second multi-gigabyte copy under shared external storage.
    @Override public java.io.File getObbDir()
    {
        if (hasGenerationObb()) return profilePackageDir();
        return super.getObbDir();
    }

    @Override public java.io.File[] getObbDirs()
    {
        if (hasGenerationObb()) return new java.io.File[] { profilePackageDir() };
        return super.getObbDirs();
    }

    // Unity's own name for the same thing. The engine looks for
    // <obb dir>/main.<versionCode>.<package>.obb without being told to --
    // libunity.so carries the format string and the errors that go with it --
    // so data placed here needs no redirection at all.
    private java.io.File obbFile()
    {
        java.io.File dir = getObbDir();
        if (dir == null) return new java.io.File("/nonexistent");
        return new java.io.File(dir, obbName("main"));
    }

    // findLibrary consults DexPathList.nativeLibraryPathElements, so the
    // directory has to be added there rather than to anything the engine
    // reads. The platform maintains that array itself through addNativePath,
    // which is what instrumentation and split-APK loading use; it appends the
    // directory and rebuilds the element array in one step.
    //
    // The application's loader is patched, not the activity's -- they are the
    // same PathClassLoader, and every caller in the process shares it, so one
    // patch covers whichever context the engine happens to ask.
    private void addNativeLibraryPath()
    {
        java.io.File dir = externalLibDir();
        if (!new java.io.File(dir, "libil2cpp.so").exists())
        {
            android.util.Log.i(TAG, "no external engine at " + dir + "; using the APK's");
            return;
        }

        ClassLoader loader = getApplicationContext().getClassLoader();
        Object pathList;
        try
        {
            java.lang.reflect.Field f = Class.forName("dalvik.system.BaseDexClassLoader")
                .getDeclaredField("pathList");
            f.setAccessible(true);
            pathList = f.get(loader);
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "no pathList on " + loader.getClass() + ": " + e);
            return;
        }

        try
        {
            java.lang.reflect.Method add = pathList.getClass()
                .getDeclaredMethod("addNativePath", java.util.Collection.class);
            add.setAccessible(true);
            add.invoke(pathList, java.util.Collections.singletonList(dir.getAbsolutePath()));
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "addNativePath failed: " + e);
            return;
        }

        // Ask the same question the engine asks, so the log says outright
        // whether the lookup it depends on now succeeds.
        android.util.Log.i(TAG, "findLibrary(\"il2cpp\") -> " + findLibrary(loader, "il2cpp"));
    }

    private static String findLibrary(ClassLoader loader, String name)
    {
        try
        {
            java.lang.reflect.Method m =
                ClassLoader.class.getDeclaredMethod("findLibrary", String.class);
            m.setAccessible(true);
            return (String) m.invoke(loader, name);
        }
        catch (Exception e)
        {
            return "<unavailable: " + e + ">";
        }
    }

    // Separate from the lookup above: having found and loaded the engine, it
    // still needs the game's data. It does not use AssetManager for this --
    // it builds "jar:file://<package path>!/assets" and reads
    // assets/bin/Data out of that zip itself. So the data does not have to be
    // the APK; it only has to be a zip laid out like one.
    //
    // That is what makes the depot-built data possible at all. It cannot ship
    // in a CI-built APK, and an installed APK cannot be modified -- but the
    // path the engine derives all this from is one it asks the framework for,
    // and that answer can be pointed somewhere else.
    //
    // With <files>/pkg/data.apk present, that zip is used and the real APK is
    // never consulted for game data. Without it, the APK is presented in the
    // layout Android installs -- <dir>/base.apk beside <dir>/lib/<abi>/ --
    // through a symlink, which is the arrangement that works while the data
    // is still inside it.
    private void stagePackageLayout()
    {
        java.io.File libDir = externalLibDir();
        if (!new java.io.File(libDir, "libil2cpp.so").exists()) return;
        try
        {
            android.content.pm.ApplicationInfo app = getApplicationContext().getApplicationInfo();
            java.io.File pkgDir = libDir.getParentFile().getParentFile();
            java.io.File data = externalDataApk();
            java.io.File apkLink = new java.io.File(pkgDir, "base.apk");

            if (data.isFile())
            {
                // The framework keeps using the real APK for dex and
                // resources: those come from LoadedApk and the AssetManager,
                // which were both set up before this runs. Only the engine's
                // own idea of where its data lives is moved.
                apkLink.delete();
                android.system.Os.symlink(data.getAbsolutePath(), apkLink.getAbsolutePath());
                android.util.Log.i(TAG, "game data from " + data + " (" + data.length() + " bytes)");
                if (obbFile().isFile())
                    android.util.Log.i(TAG, "game expansion from " + obbFile()
                        + " (" + obbFile().length() + " bytes)");
            }
            else if (obbFile().isFile())
            {
                // The OBB is the engine's own supported route for data
                // outside the APK, so nothing has to be redirected: it looks
                // there by itself. The APK is still presented normally.
                apkLink.delete();
                android.system.Os.symlink(app.sourceDir, apkLink.getAbsolutePath());
                android.util.Log.i(TAG, "game data from " + obbFile()
                    + " (" + obbFile().length() + " bytes)");
            }
            else
            {
                // Os.symlink rather than shelling out to ln: it reports failure.
                // exists() follows the link, so a stale one reads as absent and
                // then fails EEXIST -- and it is stale after every reinstall,
                // because the APK path changes. Replace it rather than test it.
                apkLink.delete();
                android.system.Os.symlink(app.sourceDir, apkLink.getAbsolutePath());
            }
            if (!apkLink.exists())
                throw new java.io.IOException("apk symlink not created at " + apkLink);

            String apk = apkLink.getAbsolutePath();
            String lib = libDir.getAbsolutePath();
            applyPaths(app, apk, lib);
            applyPaths(super.getApplicationInfo(), apk, lib);
            redirectPackageCodePath(apk);
            if (data.isFile()) addAssetPath(data.getAbsolutePath());
            else if (obbFile().isFile()) addAssetPath(obbFile().getAbsolutePath());

            android.util.Log.i("SilksongShell", "package staged: apk=" + apk + " lib=" + lib);
        }
        catch (Exception e)
        {
            android.util.Log.e("SilksongShell", "could not stage the package: " + e);
        }
    }

    // The other way the engine could be reaching its data: through the
    // AssetManager rather than by opening the package itself. An AssetManager
    // searches a list of archives, and that list can be extended -- which is
    // how overlays and instrumentation add resources at runtime.
    //
    // Cheap to do and harmless if it turns out not to be the route, so both
    // this and the path redirect above are applied and the log says which one
    // the engine actually followed.
    private void addAssetPath(String path)
    {
        try
        {
            java.lang.reflect.Method add = android.content.res.AssetManager.class
                .getDeclaredMethod("addAssetPath", String.class);
            add.setAccessible(true);
            Object a = add.invoke(getAssets(), path);
            Object b = add.invoke(getApplicationContext().getAssets(), path);
            android.util.Log.i(TAG, "addAssetPath(" + path + ") -> activity=" + a + " app=" + b);
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "addAssetPath failed: " + e);
        }
    }

    private static void applyPaths(android.content.pm.ApplicationInfo info, String apk, String lib)
    {
        info.nativeLibraryDir = lib;
        info.sourceDir = apk;
        info.publicSourceDir = apk;
    }

    // Context.getPackageCodePath() is the engine's actual question, and it
    // does not go through ApplicationInfo: ContextImpl forwards it to
    // LoadedApk, which copied the path once when the process started and has
    // held its own string ever since. Rewriting ApplicationInfo therefore
    // changes nothing, which is exactly what the first attempt at this
    // demonstrated -- everything reported success and the engine still said
    // "no boot config".
    //
    // Only the field the framework will actually read is touched. Resources
    // and dex are not affected: the AssetManager and the class loader were
    // both built from the real APK before this runs, and neither consults
    // this field again.
    private void redirectPackageCodePath(String apk)
    {
        try
        {
            // getApplicationContext() is the Application, which wraps the
            // ContextImpl that actually holds the LoadedApk. Unwrap until the
            // real one is in hand rather than assuming a particular depth.
            android.content.Context base = getApplicationContext();
            while (base instanceof android.content.ContextWrapper
                   && ((android.content.ContextWrapper) base).getBaseContext() != null)
            {
                base = ((android.content.ContextWrapper) base).getBaseContext();
            }

            java.lang.reflect.Field pkgField =
                Class.forName("android.app.ContextImpl").getDeclaredField("mPackageInfo");
            pkgField.setAccessible(true);
            Object loadedApk = pkgField.get(base);
            if (loadedApk == null)
                throw new IllegalStateException("no LoadedApk on " + base.getClass());

            java.lang.reflect.Field appDir =
                Class.forName("android.app.LoadedApk").getDeclaredField("mAppDir");
            appDir.setAccessible(true);
            appDir.set(loadedApk, apk);

            android.util.Log.i(TAG, "getPackageCodePath() -> " + getPackageCodePath());
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "could not redirect getPackageCodePath: " + e);
        }
    }

    @Override public android.content.pm.ApplicationInfo getApplicationInfo()
    {
        android.content.pm.ApplicationInfo info = super.getApplicationInfo();
        java.io.File dir = externalLibDir();
        if (new java.io.File(dir, "libil2cpp.so").exists())
            info.nativeLibraryDir = dir.getAbsolutePath();
        return info;
    }

    // The catalog's content root is one length-prefixed string patched in
    // place, so it can only be replaced by something no longer than the token
    // it overwrites -- 56 bytes. That is enough for a path in internal
    // storage and not enough for one in external storage, let alone one
    // inside the downloaded depot.
    //
    // So the catalog keeps pointing at a short fixed path and that path is a
    // symlink to wherever the content actually is. The content is the depot's
    // own bundle tree, retargeted in place, which is far too large to copy.
    //
    // Where that tree is, though, is no longer fixed: the user may point the
    // launcher at any folder on the device. This process cannot search for it
    // the way the launcher does, so the launcher writes down the answer it
    // resolved (DepotLocation.relink) and this reads it. The two paths below
    // it are what installs made before that existed have, and are still
    // correct for a depot the app downloaded itself.
    private void linkContent()
    {
        java.io.File link = profileContentLink();
        java.io.File ext = profileExternalDir();
        if (ext == null) return;

        // The launcher's answer first, then where a download leaves the
        // bundles, then the staging directory used before one exists. First
        // one wins.
        java.io.File[] candidates = {
            recordedContentDir(ext),
            new java.io.File(ext, "depot/Hollow Knight Silksong_Data/StreamingAssets/aa"),
        };
        java.io.File target = null;
        for (java.io.File c : candidates)
        {
            if (c == null) continue;
            if (c.isDirectory() && c.list() != null && c.list().length > 0) { target = c; break; }
        }
        if (target == null) return;

        try
        {
            // A directory with content already there is the real thing, not a
            // stale link, and is left alone.
            if (link.isDirectory() && !isSymlink(link)) return;
            java.io.File parent = link.getParentFile();
            if (parent == null || (!parent.isDirectory() && !parent.mkdirs()))
                throw new java.io.IOException("could not create content-link parent: " + parent);
            link.delete();
            android.system.Os.symlink(target.getAbsolutePath(), link.getAbsolutePath());
            android.util.Log.i(TAG, "content: " + link + " -> " + target);
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "could not link the content: " + e);
        }
    }

    private static boolean isSymlink(java.io.File f)
    {
        try { return !f.getCanonicalPath().equals(f.getAbsolutePath()); }
        catch (java.io.IOException e) { return false; }
    }

    /**
     * The content directory the launcher last resolved, or null.
     *
     * A plain text file rather than shared preferences: the launcher runs in
     * its own process, and cross-process preferences mean MODE_MULTI_PROCESS,
     * which is deprecated because it does not reliably work. Same arrangement
     * the game's settings already use, in the same directory.
     */
    private static java.io.File recordedContentDir(java.io.File ext)
    {
        java.io.File f = new java.io.File(ext, "content-path.txt");
        if (!f.isFile()) return null;
        try
        {
            byte[] b = new byte[(int) Math.min(f.length(), 4096)];
            java.io.FileInputStream in = new java.io.FileInputStream(f);
            try { in.read(b); } finally { in.close(); }
            String path = new String(b, "UTF-8").trim();
            return path.isEmpty() ? null : new java.io.File(path);
        }
        catch (Exception e)
        {
            android.util.Log.e(TAG, "could not read the content path: " + e);
            return null;
        }
    }

    // ── the game's own log, on disk ─────────────────────────────────────────
    //
    // Save failures are why this exists. DesktopPlatform.WriteSaveSlot puts
    // every reason a save can fail into Debug.LogException and nothing else:
    //
    //     try { File.WriteAllBytes(text, bytes); }
    //     catch (Exception e) { Debug.LogException(e); }   // and carries on
    //
    // That reaches logcat, which is gone the moment the user unplugs, and the
    // launcher's own log file never sees a line of it. So a report from a
    // device nobody working on the port owns says "cannot save" and carries no
    // cause -- which is exactly the position an Android 15 report left us in.
    //
    // An app may read its OWN logcat with no permission at all: the platform
    // has filtered the buffer by uid since Jelly Bean, so this returns this
    // package's output and nobody else's. The game runs in THIS process, so
    // --pid is the engine, and the Unity tag is the game's own C# logging.
    //
    // Rotated once per launch rather than appended forever. The interesting
    // session is the one that just failed, and keeping a single previous
    // generation means that relaunching the game to go and fetch the log does
    // not destroy the log being fetched.
    private static final String GAME_LOG = "game.log";

    // Enough for a session's worth of Unity output without becoming the
    // reason external storage filled up. Two generations, so twice this.
    private static final long GAME_LOG_MAX_BYTES = 512L * 1024L;

    // The distilled version, and the one a report is actually likely to
    // contain. game.log is ~180 KB per session and is rotated on every launch,
    // which is fine for "the run that just failed" and useless for the way
    // these bugs are actually met: a player whose save will not stick, or
    // whose game dies after two hours, relaunches and tries again several
    // times before it occurs to anybody to go looking for a log -- and by then
    // the session that explained it has been rotated past .prev and is gone.
    //
    // So the lines that matter are ALSO appended here, and this file is not
    // rotated per launch. It only ever collects faults, so a cap this size
    // holds many sessions rather than three.
    private static final String ERROR_LOG = "errors.log";
    private static final long ERROR_LOG_MAX_BYTES = 128L * 1024L;

    // Substrings, not patterns: this runs on every line the engine logs, and a
    // regex per line during a level load is a cost with nothing to show for
    // it. Deliberately wider than any one bug -- the exception that explains a
    // lost save is a FileStream constructor throwing, and its own text
    // mentions neither saving nor Silksong.
    //
    // "Fatal signal" is what libc writes from the dying process when the
    // engine segfaults. It is the ONLY part of a native crash we can see: the
    // backtrace is produced by crash_dump, which is a different uid, and an
    // app may only ever read its own uid's log. So that line plus the thread
    // name is the whole of what a native crash leaves us, and it must not be
    // the line that gets rotated away.
    private static final String[] FAULT_MARKERS = {
        "Exception", "Access to the path", "EACCES", "ENOSPC",
        "No space left", "Temp file", "save file", "SaveSlot",
        "Sharing violation", "IOException",
        "Fatal signal", "FATAL EXCEPTION", "OutOfMemory", "Out of memory",
        "abort message", "libc :", "was killed",
    };

    private static boolean isNoteworthy(String line)
    {
        for (String m : FAULT_MARKERS) if (line.contains(m)) return true;
        return false;
    }

    private void startLogCapture()
    {
        final java.io.File ext = profileExternalDir();
        if (ext == null) return;
        final java.io.File logs = new java.io.File(ext, "logs");
        if (!logs.isDirectory() && !logs.mkdirs()) return;
        final java.io.File out = new java.io.File(logs, GAME_LOG);
        final java.io.File prev = new java.io.File(logs, GAME_LOG + ".prev");
        rotate(out, prev);

        Thread t = new Thread(new Runnable()
        {
            @Override public void run() { captureLog(out, prev); }
        }, "game-log");
        // Daemon: this thread blocks on a pipe that only closes when logcat
        // does, and it must never be the reason the process refuses to exit.
        t.setDaemon(true);
        t.start();
    }

    private static void rotate(java.io.File out, java.io.File prev)
    {
        try
        {
            if (!out.isFile()) return;
            prev.delete();
            if (!out.renameTo(prev)) out.delete();
        }
        catch (Exception e)
        {
            android.util.Log.w(TAG, "could not rotate the game log: " + e);
        }
    }

    private void captureLog(java.io.File out, java.io.File prev)
    {
        java.lang.Process proc = null;
        java.io.Writer w = null;
        java.io.Writer saveW = null;
        try
        {
            final java.io.File ext = profileExternalDir();
            final java.io.File logs = (ext == null) ? null : new java.io.File(ext, "logs");
            if (logs != null) logs.mkdirs();
            final java.io.File saveOut = (logs == null) ? null : new java.io.File(logs, ERROR_LOG);
            // Trimmed at the door rather than mid-stream: this file grows by a
            // handful of lines per session, so it reaching the cap at all is
            // already unusual and one previous generation is ample.
            if (saveOut != null && saveOut.length() > ERROR_LOG_MAX_BYTES)
            {
                rotate(saveOut, new java.io.File(logs, ERROR_LOG + ".prev"));
            }

            String header = "=== " + new java.text.SimpleDateFormat(
                    "yyyy-MM-dd HH:mm:ss", java.util.Locale.US).format(new java.util.Date())
                + " game pid " + android.os.Process.myPid() + " ===\n";

            // -v time to match the launcher's own log, so the two can be read
            // side by side when a report carries both.
            proc = new ProcessBuilder(
                    "logcat", "-v", "time", "--pid=" + android.os.Process.myPid())
                .redirectErrorStream(true)
                .start();

            java.io.BufferedReader in = new java.io.BufferedReader(
                new java.io.InputStreamReader(proc.getInputStream()), 1 << 16);
            w = new java.io.BufferedWriter(
                new java.io.OutputStreamWriter(new java.io.FileOutputStream(out, true)));
            long written = out.length();
            try
            {
                w.write(header);
                for (String line; (line = in.readLine()) != null; )
                {
                    w.write(line);
                    w.write('\n');
                    // Flushed per line on purpose, the same bargain
                    // LauncherLog makes: the whole point is to survive the
                    // process dying, and a buffered writer's last few
                    // kilobytes are the ones that say why it died.
                    w.flush();

                    if (saveOut != null && isNoteworthy(line))
                    {
                        // Opened on the first line worth keeping, so a session
                        // that saves without trouble adds nothing at all --
                        // not even a header to page past.
                        if (saveW == null)
                        {
                            saveW = new java.io.BufferedWriter(new java.io.OutputStreamWriter(
                                new java.io.FileOutputStream(saveOut, true)));
                            saveW.write(header);
                        }
                        saveW.write(line);
                        saveW.write('\n');
                        saveW.flush();
                    }

                    written += line.length() + 1;
                    if (written > GAME_LOG_MAX_BYTES)
                    {
                        w.close();
                        rotate(out, prev);
                        w = new java.io.BufferedWriter(new java.io.OutputStreamWriter(
                            new java.io.FileOutputStream(out, true)));
                        written = 0L;
                    }
                }
            }
            finally
            {
                try { in.close(); } catch (Exception ignored) { }
            }
        }
        catch (Exception e)
        {
            // A log that cannot be written must never cost the game its
            // launch. This is diagnostics and nothing else depends on it.
            android.util.Log.w(TAG, "game log capture stopped: " + e);
        }
        finally
        {
            if (w != null) { try { w.close(); } catch (Exception ignored) { } }
            if (saveW != null) { try { saveW.close(); } catch (Exception ignored) { } }
            if (proc != null) proc.destroy();
        }
    }

    // ── did the last session end, or did it die? ────────────────────────────
    //
    // A native crash and an out-of-memory kill both leave us nothing to read:
    // the backtrace belongs to crash_dump and the kill belongs to lmkd, and
    // both are other uids, which an app may not read the log of. So the only
    // way to know is to notice AFTERWARDS.
    //
    // A marker is written when the game starts, refreshed with the running
    // time and the memory picture as it goes, and deleted when the activity
    // stops of its own accord. Finding one at the next launch means the
    // previous run never got to delete it, and its contents say how long that
    // run lasted and what memory looked like when it stopped -- which is the
    // difference between "it crashes after about two hours" and a report that
    // can be acted on.
    //
    // Deleted on onStop as well as onDestroy, and deliberately: the engine
    // calls System.exit(0) on quit and onDestroy does not reliably follow it.
    // Treating a stopped activity as a clean end costs us the ability to spot
    // a background kill, and buys us a signal that is not crying wolf on every
    // ordinary exit -- and a game that dies two hours into being PLAYED is
    // resumed, not stopped, so the case we are chasing is still caught.
    private static final String ALIVE = "session.running";

    private static final long HEARTBEAT_MS = 60_000L;

    private java.io.File extFile(String name)
    {
        java.io.File ext = profileExternalDir();
        if (ext == null) return null;
        java.io.File logs = new java.io.File(ext, "logs");
        if (!logs.isDirectory()) logs.mkdirs();
        return new java.io.File(logs, name);
    }

    /** Straight to the file, not via logcat: a dying process may not get a turn. */
    private void appendError(String text)
    {
        android.util.Log.e(TAG, text);
        java.io.File f = extFile(ERROR_LOG);
        if (f == null) return;
        try
        {
            java.io.Writer w = new java.io.OutputStreamWriter(
                new java.io.FileOutputStream(f, true));
            try { w.write(text); w.write('\n'); w.flush(); }
            finally { w.close(); }
        }
        catch (Exception ignored) { }
    }

    private void reportPreviousSession()
    {
        java.io.File f = extFile(ALIVE);
        if (f == null || !f.isFile()) return;
        String detail;
        try
        {
            byte[] b = new byte[(int) Math.min(f.length(), 4096)];
            java.io.FileInputStream in = new java.io.FileInputStream(f);
            try { in.read(b); } finally { in.close(); }
            detail = new String(b, "UTF-8").trim();
        }
        catch (Exception e) { detail = "marker unreadable: " + e; }
        appendError("previous session did NOT exit cleanly -- last seen: " + detail);
        f.delete();
    }

    /** Uptime and memory, the two things a two-hour death is usually about. */
    private String status(long startedMs)
    {
        long upSec = (android.os.SystemClock.elapsedRealtime() - startedMs) / 1000L;
        Runtime rt = Runtime.getRuntime();
        long javaMb = (rt.totalMemory() - rt.freeMemory()) / (1024L * 1024L);
        long nativeMb = android.os.Debug.getNativeHeapAllocatedSize() / (1024L * 1024L);
        long availMb = -1;
        boolean low = false;
        try
        {
            android.app.ActivityManager am =
                (android.app.ActivityManager) getSystemService(ACTIVITY_SERVICE);
            android.app.ActivityManager.MemoryInfo mi =
                new android.app.ActivityManager.MemoryInfo();
            am.getMemoryInfo(mi);
            availMb = mi.availMem / (1024L * 1024L);
            low = mi.lowMemory;
        }
        catch (Exception ignored) { }
        return "uptime " + (upSec / 3600) + "h" + ((upSec % 3600) / 60) + "m"
            + ", java " + javaMb + " MB, native " + nativeMb + " MB"
            + ", system free " + availMb + " MB" + (low ? " (LOW MEMORY)" : "");
    }

    private void startHeartbeat()
    {
        final long started = android.os.SystemClock.elapsedRealtime();
        writeMarker(status(started));
        Thread t = new Thread(new Runnable()
        {
            @Override public void run()
            {
                while (true)
                {
                    try { Thread.sleep(HEARTBEAT_MS); }
                    catch (InterruptedException e) { return; }
                    String s = status(started);
                    // Into game.log by way of our own capture, and into the
                    // marker so that it survives the process that wrote it.
                    android.util.Log.i(TAG, s);
                    writeMarker(s);
                }
            }
        }, "game-heartbeat");
        t.setDaemon(true);
        t.start();
    }

    private void writeMarker(String text)
    {
        java.io.File f = extFile(ALIVE);
        if (f == null) return;
        try
        {
            java.io.Writer w = new java.io.OutputStreamWriter(new java.io.FileOutputStream(f));
            try { w.write(text); w.flush(); }
            finally { w.close(); }
        }
        catch (Exception ignored) { }
    }

    /**
     * Java-side crashes, written before the process is taken away.
     *
     * The engine is IL2CPP and most of what can go wrong here is native, which
     * this cannot see. What it does catch is anything thrown on a Java thread
     * -- the shell's own threads included -- and it writes the stack straight
     * to the file rather than trusting the log-capture thread to be scheduled
     * once more before the process ends.
     */
    private void installCrashHandler()
    {
        final Thread.UncaughtExceptionHandler prev =
            Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler()
        {
            @Override public void uncaughtException(Thread t, Throwable e)
            {
                try
                {
                    java.io.StringWriter sw = new java.io.StringWriter();
                    e.printStackTrace(new java.io.PrintWriter(sw));
                    appendError("FATAL EXCEPTION on thread " + t.getName() + "\n" + sw);
                }
                catch (Throwable ignored) { }
                if (prev != null) prev.uncaughtException(t, e);
            }
        });
    }

    @Override protected void onStop()
    {
        java.io.File f = extFile(ALIVE);
        if (f != null) f.delete();
        super.onStop();
    }

    @Override protected void onCreate(Bundle savedInstanceState)
    {
        // First, so that everything below it is in the file too: the engine
        // install and the content link are the other two things that fail on
        // hardware we do not have.
        startLogCapture();
        installCrashHandler();
        // Before the marker is rewritten, or the evidence is the thing that
        // overwrites the evidence.
        reportPreviousSession();
        startHeartbeat();
        // Before super.onCreate: that is where UnityPlayerActivity constructs
        // the player, which is what triggers the engine's own library loading.
        addNativeLibraryPath();
        stagePackageLayout();
        linkContent();
        super.onCreate(savedInstanceState);
        // Draw into the cutout/waterfall region as well; the game already
        // expects to own the whole panel.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P)
        {
            WindowManager.LayoutParams lp = getWindow().getAttributes();
            lp.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            getWindow().setAttributes(lp);
        }
        goFullscreen();
    }

    // The system restores the bars on every focus change -- returning from the
    // Game Dashboard, a notification shade pull, an orientation change -- so
    // this has to be re-applied rather than set once.
    @Override public void onWindowFocusChanged(boolean hasFocus)
    {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) goFullscreen();
    }

    private void goFullscreen()
    {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R)
        {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null)
            {
                c.hide(WindowInsets.Type.systemBars());
                // Without this a swipe permanently restores the bars and the
                // surface is resized back, cutting the game off again.
                c.setSystemBarsBehavior(
                    WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        }
        else
        {
            getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
        }
    }
}
