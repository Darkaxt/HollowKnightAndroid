package dev.silksong.launcher

import android.content.Context
import dalvik.system.DexClassLoader
import java.io.File

/**
 * Turns Unity's Java classes into dex, on the device, and makes them loadable.
 *
 * The APK links against com.unity3d.player.* but contains none of it: the
 * shipped dex defines zero Unity classes and only references the dozen methods
 * [dev.silksong.shell.PlayerActivity] calls. Unity's classes.jar arrives with
 * the Android player module the app downloads, which is the user's own
 * download from Unity's CDN -- so the classes exist here, as Java bytecode,
 * and ART cannot load Java bytecode.
 *
 * So they are dexed here. The dexer is d8, shipped in the APK already dexed
 * (Google's tool, Apache-2.0). ART runs dex, so d8-as-dex runs on the phone
 * with no JVM involved; it converts classes.jar in about two seconds.
 *
 * The result is injected into the app's own class loader at process start, so
 * that when the framework instantiates GameActivity its superclass and the
 * player type resolve normally. Injecting into a *child* loader would not
 * work: the activity is loaded by the app loader, which cannot see a child.
 */
object UnityDex {

    private const val TAG = "UnityDex"

    /** d8, dexed, as shipped in the APK. */
    private const val D8_ASSET = "d8.zip"

    /** The entry point d8 exposes for programmatic use. */
    private const val D8_MAIN = "com.android.tools.r8.D8"

    /** Where the dexed player classes live once built. */
    fun outputDir(context: Context): File = File(context.filesDir, "unity-dex")

    private fun outputJar(context: Context): File = File(outputDir(context), "classes.jar")

    /**
     * The player classes as they arrive: Java bytecode, inside the module.
     *
     * Found by walking rather than by a fixed path, for the same reason
     * [UnityFetcher] walks for the engine libraries -- the .pkg's payload is
     * rooted wherever Unity's installer puts it.
     */
    fun sourceJar(unityRoot: File): File? {
        val android = File(unityRoot, "android")
        if (!android.isDirectory) return null
        android.walkTopDown().maxDepth(8).forEach { f ->
            if (f.isFile && f.name == "classes.jar" &&
                f.path.replace('\\', '/').contains("/Variations/il2cpp/Release/Classes/")
            ) return f
        }
        return null
    }

    /** True once the dex is built and is no older than the jar it came from. */
    fun isBuilt(context: Context, unityRoot: File): Boolean {
        val out = outputJar(context)
        if (!out.isFile || out.length() == 0L) return false
        val src = sourceJar(unityRoot) ?: return true
        return out.lastModified() >= src.lastModified()
    }

    /**
     * Dexes the player classes. Cheap enough to be unconditional, but skipped
     * when the output is already current.
     */
    fun build(context: Context, unityRoot: File) {
        if (isBuilt(context, unityRoot)) return
        val src = sourceJar(unityRoot)
            ?: throw java.io.IOException("no classes.jar in the Unity Android module")

        val out = outputDir(context)
        out.deleteRecursively()
        out.mkdirs()

        // d8 writes classes.dex (and classes2.dex...) into a directory, or a
        // zip if the output name ends in .zip. A zip is what the class loader
        // wants, and it keeps multidex output in one file.
        // d8 validates the output by extension: it must end in .zip or .jar,
        // or already exist as a directory. The usual ".part" while-writing
        // suffix is rejected outright, so the temporary name is a .jar too.
        val tmp = File(out, "part.jar")
        val started = System.currentTimeMillis()
        runD8(context, listOf(
            "--release",
            // Matches the APK's own minSdk, so d8 desugars to the same level
            // the rest of this app was built for.
            "--min-api", "26",
            "--output", tmp.absolutePath,
            src.absolutePath,
        ))
        if (!tmp.isFile || tmp.length() == 0L)
            throw java.io.IOException("d8 produced nothing for ${src.name}")
        if (!tmp.renameTo(outputJar(context)))
            throw java.io.IOException("could not move $tmp into place")

        LauncherLog.log(
            "$TAG: dexed ${src.name} in ${System.currentTimeMillis() - started} ms " +
                "(${outputJar(context).length() / 1024} KB)")
    }

    /**
     * Runs d8 in this process.
     *
     * d8 is Java bytecode in the SDK, and is shipped here already converted,
     * so it loads like any other dex. Its command-line entry point is used
     * rather than its API: the API's classes move between versions, the
     * arguments do not.
     */
    private fun runD8(context: Context, args: List<String>) {
        val d8 = stagedD8(context)
        // The dexer's own classes have nothing to do with the app's, so it
        // gets its own loader. Only the result is shared, and that goes back
        // through the app loader deliberately (see inject).
        val loader = DexClassLoader(
            d8.absolutePath,
            File(context.cacheDir, "d8-opt").apply { mkdirs() }.absolutePath,
            null,
            UnityDex::class.java.classLoader,
        )
        val main = loader.loadClass(D8_MAIN)
        // D8.main(String[]) exits the process on failure, so the run method
        // that throws is preferred when it is there.
        val run = runCatching {
            main.getMethod("run", Array<String>::class.java)
        }.getOrNull()
        val argv = args.toTypedArray()
        try {
            if (run != null) run.invoke(null, argv)
            else main.getMethod("main", Array<String>::class.java).invoke(null, argv)
        } catch (e: java.lang.reflect.InvocationTargetException) {
            throw java.io.IOException("d8 failed: ${e.cause?.message ?: e.message}", e.cause)
        }
    }

    /**
     * d8 as a file, copied out of the APK's assets.
     *
     * A class loader needs a path it can open, and an asset inside the APK is
     * not one. Copied once and reused; the APK's own timestamp is the version.
     */
    private fun stagedD8(context: Context): File {
        val dst = File(context.filesDir, "tools/$D8_ASSET")
        val apkStamp = File(context.applicationInfo.sourceDir).lastModified()
        if (dst.isFile && dst.length() > 0 && dst.lastModified() >= apkStamp) return dst
        dst.parentFile?.mkdirs()
        val part = File(dst.parentFile, "$D8_ASSET.part")
        context.assets.open(D8_ASSET).use { input ->
            part.outputStream().use { out -> input.copyTo(out, 1 shl 16) }
        }
        if (!part.renameTo(dst)) throw java.io.IOException("could not stage $D8_ASSET")
        return dst
    }

    /**
     * Adds the dexed player classes to the app's own class loader.
     *
     * Android's loader keeps its dex files in a private array inside
     * BaseDexClassLoader.pathList. Appending to that array is what MultiDex
     * did for years and what every dynamic-loading library still does: build
     * a loader over the new dex, take the elements it made, and concatenate.
     *
     * It has to be the app's loader, not a child, because the framework
     * instantiates activities through the app's loader and a parent cannot
     * see into a child. And it has to happen before any of those activities
     * is loaded, which is why this runs from Application.attachBaseContext.
     */
    fun inject(context: Context) {
        val jar = outputJar(context)
        if (!jar.isFile) return
        val appLoader = context.classLoader ?: return
        try {
            val dexPathList = field(appLoader.javaClass, "pathList") ?: return
            val pathList = dexPathList.get(appLoader) ?: return
            val elementsField = field(pathList.javaClass, "dexElements") ?: return

            val donor = DexClassLoader(
                jar.absolutePath,
                File(context.cacheDir, "unity-dex-opt").apply { mkdirs() }.absolutePath,
                null,
                appLoader,
            )
            val donorList = field(donor.javaClass, "pathList")?.get(donor) ?: return
            val donorElements = elementsField.get(donorList) as? Array<*> ?: return
            val current = elementsField.get(pathList) as? Array<*> ?: return

            val merged = java.lang.reflect.Array.newInstance(
                current.javaClass.componentType!!, current.size + donorElements.size)
            System.arraycopy(current, 0, merged, 0, current.size)
            System.arraycopy(donorElements, 0, merged, current.size, donorElements.size)
            elementsField.set(pathList, merged)

            LauncherLog.log("$TAG: player classes added to the app class loader")
        } catch (t: Throwable) {
            // Not recoverable here, but throwing would take the launcher UI
            // down with it -- and the launcher is where the user goes to
            // build the thing that is missing. The game fails later, loudly.
            LauncherLog.log("$TAG: could not add the player classes", t)
        }
    }

    /** Walks up the hierarchy, since the field is declared on a base class. */
    private fun field(start: Class<*>, name: String): java.lang.reflect.Field? {
        var c: Class<*>? = start
        while (c != null) {
            try {
                return c.getDeclaredField(name).apply { isAccessible = true }
            } catch (_: NoSuchFieldException) {
                c = c.superclass
            }
        }
        return null
    }
}
