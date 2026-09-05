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

    /**
     * Where ART keeps the optimized form of the dex we graft on.
     *
     * Under cacheDir, so the system may delete it whenever it likes -- which
     * is fine, it is rebuilt in milliseconds -- but also means it can be
     * found stale or half-written, and a bad odex stops a perfectly good jar
     * from loading. See [repair].
     */
    private const val OPT_DIR = "unity-dex-opt"

    /**
     * The class whose loading is the thing that actually has to work.
     *
     * Not a Unity type, deliberately. The failure being guarded against is
     * the framework being unable to instantiate the game's activity, and
     * that is a chain: GameActivity extends PlayerActivity, which implements
     * com.unity3d.player.IUnityPlayerLifecycleEvents and holds a
     * UnityPlayerForActivityOrService -- and a superclass and its interfaces
     * must resolve before a class can be defined at all. So loading this one
     * name exercises exactly what ActivityThread is about to do, rather than
     * something chosen to stand in for it.
     *
     * (An earlier version probed com.unity3d.player.UnityPlayerActivity,
     * which this APK does not use and the dexed jar does not contain --
     * PlayerActivity replaces it. The check therefore failed on a perfectly
     * good build, every time.)
     */
    private const val GAME_ACTIVITY_CLASS = "dev.silksong.shell.GameActivity"

    /**
     * A type that can only have come from the dexed jar.
     *
     * Used to ask whether the JAR is good, as opposed to whether this
     * process can load it -- see the tail of [repair]. It must be a Unity
     * type, because the app loader does not have one and so cannot answer
     * the question by accident.
     */
    private const val UNITY_PLAYER_CLASS = "com.unity3d.player.UnityPlayerForActivityOrService"

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
        if (!jar.isFile) {
            // Silent until issue #24. This is the state in which the game
            // cannot start AT ALL -- GameActivity's superclass lives in this
            // jar, so without it the framework cannot even instantiate the
            // activity -- and it used to be the one state that produced no
            // log line anywhere. A launch that failed here looked exactly
            // like a launch that worked.
            LauncherLog.log("$TAG: the player classes are not built ($jar is missing)")
            return
        }
        val appLoader = context.classLoader
        if (appLoader == null) {
            LauncherLog.log("$TAG: no class loader to add the player classes to")
            return
        }
        try {
            val dexPathList = field(appLoader.javaClass, "pathList")
                ?: return failed("BaseDexClassLoader has no pathList field")
            val pathList = dexPathList.get(appLoader)
                ?: return failed("the class loader's pathList is null")
            val elementsField = field(pathList.javaClass, "dexElements")
                ?: return failed("DexPathList has no dexElements field")

            val donor = DexClassLoader(
                jar.absolutePath,
                File(context.cacheDir, OPT_DIR).apply { mkdirs() }.absolutePath,
                null,
                appLoader,
            )
            val donorList = field(donor.javaClass, "pathList")?.get(donor)
                ?: return failed("the donor loader has no pathList")
            val donorElements = elementsField.get(donorList) as? Array<*>
                ?: return failed("the donor loader produced no dex elements")
            val current = elementsField.get(pathList) as? Array<*>
                ?: return failed("the app loader's dexElements is not an array")

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

    /**
     * One reflection step did not find what it expected.
     *
     * Each of these was a bare `?: return` and so indistinguishable from
     * success. They are all "this Android version moved a private field",
     * which is worth knowing precisely: the step named here is the one to fix.
     */
    private fun failed(why: String) {
        LauncherLog.log("$TAG: could not add the player classes: $why")
    }

    /**
     * Whether the player classes actually resolve in THIS process.
     *
     * The question [inject] exists to make true, asked directly instead of
     * inferred from whether a file is on disk. A jar that is present and
     * current but does not load answers no here and yes to every other check
     * in this object, and that gap is the whole of the bug below.
     *
     * initialize = false: this only has to link. Running the static
     * initialiser of the engine's activity class inside the launcher process
     * is neither wanted nor safe.
     */
    /**
     * Whether the GAME's process will be able to load its own activity.
     *
     * Two questions, cheapest first, and the second one is the point.
     *
     * A process that grafted an unusable jar at startup can never resolve the
     * class afterwards -- appending a good jar does not undo a resolution
     * already attempted -- so asking only [resolvesHere] means the launcher
     * answers "no" forever after one bad start, while the game, which gets a
     * fresh process and a fresh graft, would have started perfectly well.
     * That is not theoretical: it shipped for about ten minutes and turned
     * every launch after a repair into a failure dialog.
     *
     * So a no from this process is not a no. What decides it is whether the
     * jar on disk supplies the classes.
     */
    fun playerClassesUsable(context: Context): Boolean =
        resolvesHere(context) || jarProvidesPlayerClasses(context)

    /**
     * Whether THIS process can load the game's activity.
     *
     * initialize = false: this only has to link. Running the static
     * initialiser of the engine's activity class inside the launcher process
     * is neither wanted nor safe.
     */
    private fun resolvesHere(context: Context): Boolean =
        runCatching { Class.forName(GAME_ACTIVITY_CLASS, false, context.classLoader) }.isSuccess

    /**
     * Makes sure the game's process will be able to load its own activity.
     *
     * Called before the game is started, because the alternative is issue
     * #24: the framework cannot instantiate GameActivity, so the process dies
     * between Application.onCreate and Activity.onCreate -- and every
     * recorder this app has is started INSIDE the onCreate that never ran.
     * The failure is therefore both fatal and invisible, and the only remedy
     * anyone found was a twenty-minute rebuild. The part of that rebuild
     * which actually mattered is the second and a half spent in [build].
     *
     * Deliberately not conditional on [isBuilt]: "the jar is present and
     * current" is exactly the state being repaired, so a check that believes
     * it would look straight at the broken thing and decide there was nothing
     * to do.
     *
     * Returns null when the game can be started, or a sentence saying why it
     * cannot. Blocking, and slow enough to matter: never call it on the main
     * thread.
     */
    fun repair(context: Context): String? {
        if (playerClassesUsable(context)) return null
        LauncherLog.log("$TAG: the player classes do not load; rebuilding them")

        // The odex first, and unconditionally. It is derived from the jar
        // rather than authored, it is in a directory the system may empty at
        // any moment, and it is remade in milliseconds -- so it is both the
        // cheapest thing to rule out and a real way for a good jar to stop
        // loading.
        runCatching { File(context.cacheDir, OPT_DIR).deleteRecursively() }

        // Removing the output is what forces build() past its "already
        // current" check.
        runCatching { outputDir(context).deleteRecursively() }

        runCatching { build(context, UnityFetcher.rootFor(context)) }.onFailure {
            LauncherLog.log("$TAG: could not rebuild the player classes", it)
            return "The player classes could not be rebuilt: ${it.message}"
        }

        // This process's loader still has nothing grafted onto it: inject ran
        // at process start, when there was nothing to graft. Running it now
        // is safe precisely because it did nothing then.
        inject(context)

        if (resolvesHere(context)) {
            LauncherLog.log("$TAG: the player classes load again")
            return null
        }

        // This process could not be mended, and that is not the same as the
        // game being unable to start. A loader that grafted an unusable jar
        // at process start keeps the failure -- appending a good one after
        // the fact does not undo a resolution already attempted -- whereas
        // the game gets a new process and a new graft. So the question that
        // decides whether to launch is whether the JAR is good NOW, asked
        // through a loader made for the purpose instead of this one.
        if (jarProvidesPlayerClasses(context)) {
            LauncherLog.log("$TAG: rebuilt; the game's process will load them fresh")
            return null
        }
        return "The player classes were rebuilt but still do not load."
    }

    /**
     * Whether the jar on disk supplies the Unity types.
     *
     * Independent of what this process's own loader has already made of it:
     * a throwaway loader over that one file, asked for a type only that file
     * can provide.
     */
    private fun jarProvidesPlayerClasses(context: Context): Boolean =
        runCatching {
            val probe = DexClassLoader(
                outputJar(context).absolutePath,
                File(context.cacheDir, "$OPT_DIR-probe").apply { mkdirs() }.absolutePath,
                null,
                UnityDex::class.java.classLoader,
            )
            Class.forName(UNITY_PLAYER_CLASS, false, probe)
        }.isSuccess

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
