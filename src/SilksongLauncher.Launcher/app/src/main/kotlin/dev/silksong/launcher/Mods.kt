// Mods — the folder, and what happens to what is in it.
//
// A BepInEx plugin is an ordinary managed assembly compiled against the game's
// Mono assemblies. That is exactly what this pipeline is holding right before
// il2cpp runs, and never again afterwards -- so the chainloader runs HERE,
// between the depot being staged and the conversion starting, rather than at
// game startup the way it does on a PC.
//
// mod-weaver does the work: it reads each plugin, resolves every [HarmonyPatch]
// against the staged assemblies, and writes the prefix and postfix calls into
// the game's IL as instructions. il2cpp then compiles a game that was already
// patched. Nothing is hooked at runtime, because by runtime there is no IL
// left to hook.
//
// The cost of that is honest and unavoidable: installing a mod means a
// rebuild, not a restart. The stamp below is what keeps that cost to the times
// it is actually owed -- change the folder and the conversion and the native
// compile both run again; leave it alone and neither does.

package dev.silksong.launcher

import java.io.File
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest

object Mods {

    /**
     * Where a user puts plugin DLLs.
     *
     * The app's own external files directory, so it is reachable over USB and
     * from any file manager without a permission, and -- because the game runs
     * inside this same package -- it is the same path the game's own
     * BepInEx.Paths finds at runtime for configs.
     */
    fun dir(context: android.content.Context): File =
        File(context.getExternalFilesDir(null), "mods")

    fun configDir(mods: File): File = File(mods, "config")

    private fun disabledFile(mods: File): File = File(mods, "disabled.txt")

    /** Written on first use, so the folder explains itself when it is empty. */
    fun ensure(mods: File) {
        if (!mods.isDirectory) mods.mkdirs()
        configDir(mods).mkdirs()
        val readme = File(mods, "README.txt")
        if (!readme.isFile) {
            readme.writeText(
                """
                Put BepInEx 5 plugin DLLs in this folder.

                They are compiled into the game when you build, not loaded when
                you launch -- so adding, removing or disabling one means
                rebuilding from the launcher. Config files are in config/ and
                are read at startup, so those you can change freely.

                Transpilers, runtime-computed patch targets and Reflection.Emit
                cannot work here. The launcher tells you which plugins used
                them.
                """.trimIndent() + "\n",
            )
        }
    }

    // ── what is in the folder ──────────────────────────────────────────────

    /**
     * Every plugin DLL, disabled ones included.
     *
     * Searched recursively, because a mod is often distributed as a folder
     * with the plugin and its own libraries inside. config/ is skipped: a .cfg
     * is not an assembly, but nothing stops somebody dropping one in there.
     */
    fun all(mods: File): List<File> {
        if (!mods.isDirectory) return emptyList()
        val config = configDir(mods).absolutePath + File.separator
        return mods.walkTopDown()
            .filter { it.isFile && it.extension.equals("dll", ignoreCase = true) }
            .filterNot { it.absolutePath.startsWith(config) }
            .sortedBy { it.absolutePath }
            .toList()
    }

    fun relativePath(mods: File, dll: File): String =
        dll.absolutePath.removePrefix(mods.absolutePath + File.separator)

    /**
     * Plugins the user turned off.
     *
     * A list rather than a rename or a move: disabling something should not
     * touch a file somebody downloaded, and a mod that is off today is usually
     * on again next week.
     */
    fun disabled(mods: File): Set<String> {
        val f = disabledFile(mods)
        if (!f.isFile) return emptySet()
        return f.readLines().map { it.trim() }.filter { it.isNotEmpty() }.toSet()
    }

    fun setEnabled(mods: File, relative: String, enabled: Boolean) {
        val current = disabled(mods).toMutableSet()
        if (enabled) current.remove(relative) else current.add(relative)
        val f = disabledFile(mods)
        if (current.isEmpty()) f.delete() else f.writeText(current.sorted().joinToString("\n") + "\n")
    }

    fun enabled(mods: File): List<File> {
        val off = disabled(mods)
        return all(mods).filterNot { relativePath(mods, it) in off }
    }

    // ── staleness ──────────────────────────────────────────────────────────

    private fun stampFile(root: File): File = File(root, "mods.stamp")

    /**
     * What the enabled set and the weaver that consumes it contain, by content.
     *
     * Content and not timestamps: a plugin replaced by a different build of
     * itself is the case that matters most, and it is also the case most
     * likely to arrive with whatever mtime the zip carried.
     */
    fun stamp(mods: File, assets: android.content.res.AssetManager? = null): String {
        val sha = MessageDigest.getInstance("SHA-256")
        val plugins = enabled(mods)
        for (dll in plugins) {
            sha.update(relativePath(mods, dll).toByteArray())
            dll.inputStream().use { sha.updateFrom(it) }
        }
        if (plugins.isNotEmpty() && assets != null) {
            for (name in assets.list(WEAVER_ASSET_DIR).orEmpty().sorted()) {
                sha.update("weaver/$name".toByteArray())
                assets.open("$WEAVER_ASSET_DIR/$name").use { sha.updateFrom(it) }
            }
        }
        return sha.digest().joinToString("") { "%02x".format(it) }
    }

    private fun MessageDigest.updateFrom(input: InputStream) {
        val buf = ByteArray(1 shl 16)
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            update(buf, 0, n)
        }
    }

    /** Whether the mods folder differs from what the current build was made from. */
    fun isStale(mods: File, root: File, assets: android.content.res.AssetManager? = null): Boolean {
        val f = stampFile(root)
        val previous = if (f.isFile) f.readText().trim() else ""
        return previous != stamp(mods, assets)
    }

    fun markCurrent(mods: File, root: File, assets: android.content.res.AssetManager? = null) {
        stampFile(root).writeText(stamp(mods, assets))
    }

    fun clearStamp(root: File) {
        stampFile(root).delete()
    }

    // ── the weaver ─────────────────────────────────────────────────────────

    private const val WEAVER_ASSET_DIR = "ondevice/mod-weaver"
    private const val WEAVER_DLL = "ModWeaver.dll"

    fun reportFile(root: File): File = File(root, "mods.report.json")

    /** One plugin, as the last weave found it. */
    data class Plugin(
        val file: String,
        val assembly: String,
        val guid: String,
        val name: String,
        val version: String,
        val status: String,
        val patched: Int,
        val issues: List<String>,
    ) {
        val ok: Boolean get() = status == "Ok"
        val failed: Boolean get() = status == "Failed"
        val title: String get() = if (name.isNotEmpty()) name else assembly.ifEmpty { file }
    }

    /** The last report, for the launcher to show without rebuilding. */
    fun lastReport(root: File): List<Plugin> {
        val f = reportFile(root)
        if (!f.isFile) return emptyList()
        return try {
            parse(f.readText())
        } catch (t: Throwable) {
            LauncherLog.log("mods: could not read the last report: $t")
            emptyList()
        }
    }

    private fun parse(text: String): List<Plugin> {
        val plugins = org.json.JSONObject(text).optJSONArray("plugins") ?: return emptyList()
        return (0 until plugins.length()).map { i ->
            val p = plugins.getJSONObject(i)
            val issues = p.optJSONArray("Issues")
            Plugin(
                file = p.optString("File"),
                assembly = p.optString("Assembly"),
                guid = p.optString("Guid"),
                name = p.optString("Name"),
                version = p.optString("Version"),
                status = p.optString("Status"),
                patched = p.optInt("Patched"),
                issues = (0 until (issues?.length() ?: 0)).map { issues!!.getString(it) },
            )
        }
    }

    /** Unpacks mod-weaver out of the APK, beside the build it operates on. */
    private fun stageWeaver(root: File, assets: android.content.res.AssetManager): File {
        val dir = File(root, "mod-weaver")
        dir.mkdirs()
        for (name in assets.list(WEAVER_ASSET_DIR).orEmpty()) {
            val out = File(dir, name)
            assets.open("$WEAVER_ASSET_DIR/$name").use { input ->
                out.outputStream().use { o -> input.copyTo(o) }
            }
        }
        val dll = File(dir, WEAVER_DLL)
        if (!dll.isFile) throw IOException("mod-weaver is not in the APK")
        return dll
    }

    /**
     * Runs the chainloader over the staged assemblies.
     *
     * Failures are reported, not thrown. One broken plugin out of six should
     * cost that plugin and nothing else -- and the whole point of doing this
     * before the native build is that a mod which cannot work says so now,
     * rather than after seventeen minutes of clang.
     */
    suspend fun weave(
        dotnet: File,
        root: File,
        mods: File,
        assemblies: File,
        assets: android.content.res.AssetManager,
        onLine: (String) -> Unit = {},
    ): List<Plugin> {
        val plugins = enabled(mods)
        if (plugins.isEmpty()) {
            reportFile(root).delete()
            return emptyList()
        }

        val weaver = stageWeaver(root, assets)
        val argv = ArrayList<String>()
        argv += DotnetFetcher.command(dotnet, weaver)
        argv += "weave"
        argv += "--assemblies"
        argv += assemblies.absolutePath
        argv += "--report"
        argv += reportFile(root).absolutePath
        for (dll in plugins) {
            argv += "--mod"
            argv += dll.absolutePath
        }

        val result = Toolchain.exec(
            argv,
            cwd = weaver.parentFile,
            env = DotnetFetcher.environment(dotnet),
            onLine = onLine,
        )
        if (!result.ok) {
            throw IOException(
                "the mod weaver failed: " +
                    (result.output.trim().lines().lastOrNull() ?: "exit ${result.code}").take(300),
            )
        }

        val report = lastReport(root)
        for (p in report) {
            LauncherLog.log(
                "mod ${p.title}: ${p.status}, ${p.patched} patch(es)" +
                    if (p.issues.isEmpty()) "" else " -- ${p.issues.joinToString("; ")}",
            )
        }
        return report
    }

    /**
     * Assemblies that came from the mods folder and are now in the build.
     *
     * Named by assembly rather than by file, because that is what the player
     * resolves by, and it is what the weaver staged them under.
     */
    fun stagedAssemblies(root: File): List<String> =
        lastReport(root).filterNot { it.failed }.map { it.assembly }.filter { it.isNotEmpty() }
}
