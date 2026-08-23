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
    private static final String ABI = "arm64";

    // <files>/pkg/lib/arm64 -- mirrors <apk dir>/lib/<abi>.
    private java.io.File externalLibDir()
    {
        return new java.io.File(getFilesDir(), "pkg/lib/" + ABI);
    }

    // The game's data, as a zip, once it is built on the device rather than
    // shipped in the APK. Named .apk because that is what it stands in for.
    private java.io.File externalDataApk()
    {
        return new java.io.File(getFilesDir(), "pkg/data.apk");
    }

    // Unity's own name for the same thing. The engine looks for
    // <obb dir>/main.<versionCode>.<package>.obb without being told to --
    // libunity.so carries the format string and the errors that go with it --
    // so data placed here needs no redirection at all.
    private java.io.File obbFile()
    {
        java.io.File dir = getObbDir();
        if (dir == null) return new java.io.File("/nonexistent");
        int version = 1;
        try
        {
            version = (int) getPackageManager()
                .getPackageInfo(getPackageName(), 0).getLongVersionCode();
        }
        catch (Exception ignored) { }
        return new java.io.File(dir, "main." + version + "." + getPackageName() + ".obb");
    }

    // Android will not map code out of external storage, so wherever the
    // engine is fetched to -- fetch-unity.sh, or the launcher's download -- it
    // has to be moved inside before anything can load it. It arrives in the
    // app's external files directory, which is writable without any permission
    // and is the one place a download can land.
    //
    // The game's data comes the same way, for a different reason: it is not
    // code and could be read where it lies, but keeping both halves under one
    // directory means one place to look and one thing to delete.
    //
    // The move is one-way: the copy outside is dropped once it is safely in,
    // because two copies of the engine is 334 MB.
    private void installEngine()
    {
        java.io.File ext = getExternalFilesDir(null);
        if (ext == null) return;
        java.io.File src = new java.io.File(ext, "staging");
        if (!src.isDirectory()) src = new java.io.File(ext, "engine");
        java.io.File[] libs = src.listFiles();
        if (libs == null) return;

        java.io.File dst = externalLibDir();
        if (!dst.isDirectory() && !dst.mkdirs())
        {
            android.util.Log.e(TAG, "could not create " + dst);
            return;
        }
        for (java.io.File so : libs)
        {
            boolean isLib = so.getName().endsWith(".so");
            boolean isData = so.getName().equals("data.apk");
            if (!isLib && !isData) continue;
            java.io.File out = isData ? externalDataApk() : new java.io.File(dst, so.getName());
            if (out.length() == so.length()) { so.delete(); continue; }
            try
            {
                // Via a temporary name, so an interrupted copy is never left
                // looking like a complete library.
                java.io.File tmp = new java.io.File(out.getParentFile(), out.getName() + ".part");
                java.io.InputStream in = new java.io.FileInputStream(so);
                try
                {
                    java.io.OutputStream o = new java.io.FileOutputStream(tmp);
                    try
                    {
                        byte[] buf = new byte[1 << 20];
                        for (int n; (n = in.read(buf)) > 0; ) o.write(buf, 0, n);
                    }
                    finally { o.close(); }
                }
                finally { in.close(); }

                if (!tmp.renameTo(out)) throw new java.io.IOException("rename to " + out);
                out.setReadable(true, true);
                if (isLib) out.setExecutable(true, true);
                so.delete();
                android.util.Log.i(TAG, "installed " + out.getName() + " (" + out.length() + " bytes)");
            }
            catch (java.io.IOException e)
            {
                android.util.Log.e(TAG, "could not install " + so.getName() + ": " + e);
            }
        }
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
        java.io.File link = new java.io.File(getFilesDir(), "aa");
        java.io.File ext = getExternalFilesDir(null);
        if (ext == null) return;

        // The launcher's answer first, then where a download leaves the
        // bundles, then the staging directory used before one exists. First
        // one wins.
        java.io.File[] candidates = {
            recordedContentDir(ext),
            new java.io.File(ext, "depot/Hollow Knight Silksong_Data/StreamingAssets/aa"),
            new java.io.File(ext, "aa"),
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

    @Override protected void onCreate(Bundle savedInstanceState)
    {
        // Before super.onCreate: that is where UnityPlayerActivity constructs
        // the player, which is what triggers the engine's own library loading.
        installEngine();
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
