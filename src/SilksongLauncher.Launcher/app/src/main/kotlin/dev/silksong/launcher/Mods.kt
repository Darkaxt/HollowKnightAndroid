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
// rebuild, not a restart. What is NOT owed is a rebuild for turning one off.
// Every plugin in the folder is woven, enabled or not, and every call the
// weaver writes is wrapped in a test of a gate field in the plugin's own
// assembly. The chainloader opens the gates of the plugins that are on. So
// the stamp below covers what is IN the folder rather than what is switched
// on: adding or replacing a file is a rebuild, flipping a switch is a file
// the game reads at startup.

package dev.silksong.launcher

import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.build.InstalledGeneration
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap

object Mods {

    /**
     * Shared plugin source library. Both registered profiles discover and import
     * DLLs here; no profile-owned mutable state belongs in this directory.
     */
    fun dir(context: android.content.Context): File =
        File(context.getExternalFilesDir(null), "mods")

    fun configDir(state: File): File = File(state, "config")

    private fun disabledFile(state: File): File = File(state, "disabled.txt")

    private val disabledStateLocks = ConcurrentHashMap<String, Any>()

    private fun disabledStateLock(state: File): Any =
        disabledStateLocks.computeIfAbsent(state.canonicalPath) { Any() }

    /** Creates shared storage and migrates legacy mutable state before first use. */
    fun ensure(mods: File, state: File) {
        if (!mods.isDirectory && !mods.mkdirs()) error("could not create the shared mod library")
        if (!state.isDirectory && !state.mkdirs()) error("could not create profile mod state")
        ModImport.reconcileTransactions(mods)
        ModStateMigration.migrate(mods, state)
        if (!configDir(state).isDirectory && !configDir(state).mkdirs()) {
            error("could not create profile mod config")
        }
        val readme = File(mods, "README.txt")
        if (!readme.isFile) {
            readme.writeText(
                """
                Put BepInEx 5 plugin DLLs in this folder.

                They are compiled into the selected game when you build, not
                loaded when you launch. Both game profiles discover DLLs here,
                while each profile keeps its own switches and config files.

                Adding or removing a plugin means rebuilding that profile.
                Turning one off or changing config applies on its next launch.

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
     * with the plugin and its own libraries inside. Legacy config/ and the
     * import transaction's hidden incoming/backup trees are skipped.
     */
    fun all(mods: File): List<File> {
        if (!mods.isDirectory) {
            if (!ModImport.transactionRoot(mods).exists()) return emptyList()
            if (!mods.mkdirs() && !mods.isDirectory) {
                throw IOException("could not recreate the mod library for transaction recovery")
            }
        }
        ModImport.reconcileTransactions(mods)
        return ModImport.boundedFiles(mods, excludedTopLevel = setOf("config"))
            .asSequence()
            .filter { it.extension.equals("dll", ignoreCase = true) }
            .filterNot {
                val owner = relativePath(mods, it).substringBefore(File.separatorChar)
                owner.equals("config", ignoreCase = true) ||
                    (owner.startsWith(".") &&
                        (owner.endsWith(".incoming") || owner.endsWith(".backup")))
            }
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
    fun disabled(state: File): Set<String> {
        val f = disabledFile(state)
        if (!f.isFile) return emptySet()
        return f.readLines().map { it.trim() }.filter { it.isNotEmpty() }.toSet()
    }

    /** Turns one plugin on or off in this exact profile's mutable state. */
    fun setEnabled(state: File, relative: String, enabled: Boolean) {
        val profileState = state.canonicalFile
        synchronized(disabledStateLock(profileState)) {
            if (!profileState.isDirectory && !profileState.mkdirs()) {
                throw IOException("could not create profile mod state")
            }
            if (!profileState.isDirectory || Files.isSymbolicLink(profileState.toPath())) {
                throw IOException("profile mod state is not a regular directory")
            }
            val current = disabled(profileState).toMutableSet()
            if (enabled) current.remove(relative) else current.add(relative)
            val expected = if (current.isEmpty()) {
                ByteArray(0)
            } else {
                (current.sorted().joinToString("\n") + "\n").toByteArray(Charsets.UTF_8)
            }
            val target = disabledFile(profileState)
            val staged = File(profileState, ".disabled.txt.part")
            if (staged.exists() && !staged.delete()) {
                throw IOException("could not clear interrupted profile mod-state publication")
            }
            try {
                FileOutputStream(staged).use { output ->
                    output.write(expected)
                    output.fd.sync()
                }
                Files.move(staged.toPath(), target.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
                if (!target.isFile || Files.isSymbolicLink(target.toPath()) ||
                    !target.readBytes().contentEquals(expected) || staged.exists()
                ) {
                    throw IOException("profile disabled state could not be verified after publication")
                }
            } catch (failure: Throwable) {
                throw IOException("could not atomically publish profile disabled state", failure)
            } finally {
                if (staged.exists() && !staged.delete()) {
                    LauncherLog.log("mods: could not clean interrupted disabled-state staging at $staged")
                }
            }
        }
    }

    fun enabled(mods: File, state: File): List<File> {
        val off = disabled(state)
        return all(mods).filterNot { relativePath(mods, it) in off }
    }

    // ── the gates ──────────────────────────────────────────────────────────

    /**
     * What the game reads at startup to decide which woven mods to run.
     *
     * The disabled paths and derived assembly gates are both owned by one
     * profile. [publisher] resolves and verifies that profile's current
     * generation immediately before this file is written; candidate reports
     * are never accepted here.
     */
    fun gatesFile(state: File): File = File(state, "disabled-assemblies.txt")

    fun writeCurrentGates(state: File, publisher: GenerationPublisher) {
        val generation = publisher.current()
            ?: error("cannot write mod gates without a published current generation")
        val publishedMetadata = publishedMetadataRoot(generation)
        val byFile = lastReport(publishedMetadata).associateBy { it.file }
        val names = disabled(state)
            .map { File(it).name }
            .mapNotNull { byFile[it]?.assembly?.takeIf(String::isNotEmpty) }
            .distinct()
            .sorted()
        val f = gatesFile(state)
        try {
            f.parentFile?.mkdirs()
            if (names.isEmpty()) {
                check(!f.exists() || f.delete()) { "could not clear stale profile mod gates" }
            } else {
                f.writeText(names.joinToString("\n") + "\n")
            }
            LauncherLog.log("mods: ${names.size} assembly/assemblies switched off")
        } catch (t: Throwable) {
            LauncherLog.log("mods: could not write the gate list: $t")
            throw IOException("could not write the profile mod gate list", t)
        }
    }

    private const val CANDIDATE_INPUT_DIR = "mod-input"

    /** Freezes plugin inputs so conversion metadata describes exactly what was woven. */
    fun snapshotForBuild(mods: File, candidateRoot: File): File {
        val snapshot = File(candidateRoot, CANDIDATE_INPUT_DIR)
        snapshot.deleteRecursively()
        check(snapshot.mkdirs()) { "could not create candidate mod input snapshot" }
        for (dll in all(mods)) {
            val target = File(snapshot, relativePath(mods, dll))
            target.parentFile?.mkdirs()
            dll.copyTo(target, overwrite = false)
        }
        return snapshot
    }

    // ── staleness ──────────────────────────────────────────────────────────

    private fun stampFile(root: File): File = File(root, "mods.stamp")

    /**
     * What the folder and the weaver that consumes it contain, by content.
     *
     * Every plugin present, not only the enabled ones: all of them are woven
     * into the build, and which are switched on is decided at startup rather
     * than at build time. A stamp over the enabled set would make every toggle
     * a twenty-minute rebuild for a change the build does not actually need.
     *
     * Content and not timestamps: a plugin replaced by a different build of
     * itself is the case that matters most, and it is also the case most
     * likely to arrive with whatever mtime the zip carried.
     */
    fun stamp(mods: File, assets: android.content.res.AssetManager? = null): String {
        val sha = MessageDigest.getInstance("SHA-256")
        val plugins = all(mods)
        for (dll in plugins) {
            sha.update(relativePath(mods, dll).toByteArray())
            dll.inputStream().use { sha.updateFrom(it) }
        }
        if (plugins.isNotEmpty() && assets != null) {
            sha.updateAssets(assets, WEAVER_ASSET_DIR)
        }
        return sha.digest().joinToString("") { "%02x".format(it) }
    }

    internal fun recordedStamp(root: File): String {
        val file = stampFile(root)
        if (!file.isFile) throw IOException("published mod stamp is missing")
        return file.readText().trim()
    }

    internal fun appendStampAssets(
        digest: MessageDigest,
        assets: android.content.res.AssetManager,
    ) {
        digest.updateAssets(assets, WEAVER_ASSET_DIR)
    }

    private fun MessageDigest.updateAssets(assets: android.content.res.AssetManager, dir: String) {
        for (name in assets.list(dir).orEmpty().sorted()) {
            val path = "$dir/$name"
            val children = assets.list(path).orEmpty()
            if (children.isNotEmpty()) {
                updateAssets(assets, path)
            } else {
                try {
                    update(path.toByteArray())
                    assets.open(path).use { updateFrom(it) }
                } catch (_: IOException) {
                }
            }
        }
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

    fun candidateMetadataPresent(root: File): Boolean =
        stampFile(root).isFile && builtFile(root).isFile && reportFile(root).isFile

    /** Records cache metadata for converted candidate output; it is not current. */
    fun recordCandidate(
        mods: File,
        candidateRoot: File,
        assets: android.content.res.AssetManager? = null,
    ) {
        check(reportFile(candidateRoot).isFile) { "candidate mod report is missing" }
        writeBuilt(mods, candidateRoot)
        stampFile(candidateRoot).writeText(stamp(mods, assets))
    }

    private const val GENERATION_METADATA_DIR = "mods"
    private val METADATA_FILES = listOf("mods.stamp", "mods.built", "mods.report.json")

    fun generationMetadataRoot(generationRoot: File): File =
        File(generationRoot, GENERATION_METADATA_DIR)

    /** Accepts mod status only when all metadata is part of a published manifest. */
    fun publishedMetadataRoot(generation: InstalledGeneration): File {
        val root = generationMetadataRoot(generation.root)
        for (name in METADATA_FILES) {
            val relative = "$GENERATION_METADATA_DIR/$name"
            val expected = generation.files[relative]
                ?: error("published generation ${generation.id} has no $relative")
            val file = File(root, name)
            check(file.isFile && digest(file) == expected) {
                "published generation ${generation.id} has invalid $relative"
            }
        }
        return root
    }

    /** Copies exact candidate mod inputs/report into the generation before sealing. */
    fun stageForGeneration(candidateRoot: File, generationRoot: File) {
        val target = generationMetadataRoot(generationRoot)
        check(!target.exists() || target.listFiles().orEmpty().isEmpty()) {
            "generation mod metadata already exists"
        }
        target.mkdirs()
        for (source in listOf(
            stampFile(candidateRoot),
            builtFile(candidateRoot),
            reportFile(candidateRoot),
        )) {
            check(source.isFile) { "candidate mod metadata is missing: ${source.name}" }
            source.copyTo(File(target, source.name), overwrite = false)
        }
    }

    fun clearStamp(root: File) {
        stampFile(root).delete()
        builtFile(root).delete()
        reportFile(root).delete()
    }

    // ── what is in the build, per mod ──────────────────────────────────────

    /**
     * Every plugin the current build was made from, by content.
     *
     * The stamp above answers "is anything different" for the whole folder,
     * which is the question a rebuild prompt needs. This answers "is THIS file
     * in the game you are about to play", which is the question somebody
     * looking at a list of six mods has -- and the two are not the same
     * question: a folder is stale the moment one mod is replaced, and the
     * other five are still built.
     *
     * By content, not by name, so that a mod updated in place is correctly no
     * longer the one that was compiled in.
     */
    private fun builtFile(root: File): File = File(root, "mods.built")

    private fun writeBuilt(mods: File, root: File) {
        val lines = all(mods).map { "${digest(it)}  ${relativePath(mods, it)}" }
        builtFile(root).writeText(
            if (lines.isEmpty()) "" else lines.joinToString("\n") + "\n",
        )
    }

    /** Digests of the plugins in the build, by the path the user sees. */
    fun built(root: File): Map<String, String> =
        runCatching { builtStrict(root) }.getOrDefault(emptyMap())

    internal fun builtStrict(root: File): Map<String, String> {
        val file = builtFile(root)
        if (!file.isFile) return emptyMap()
        return file.readLines().mapNotNull { line ->
            val parts = line.trim().split("  ", limit = 2)
            if (parts.size == 2 && parts[0].isNotEmpty()) parts[1] to parts[0] else null
        }.toMap()
    }

    /**
     * Whether this exact file is in the build.
     *
     * Null means "cannot tell": a build made before this was recorded has no
     * list, and answering "no" for every mod in it would be a screen full of
     * red about a game that is working. The caller falls back to the stamp,
     * which is what that build was judged by.
     */
    fun isBuilt(mods: File, root: File, dll: File): Boolean? {
        val known = built(root)
        if (known.isEmpty()) return null
        return known[relativePath(mods, dll)] == digest(dll)
    }

    private fun digest(file: File): String {
        val sha = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { sha.updateFrom(it) }
        return sha.digest().joinToString("") { "%02x".format(it) }
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
    fun lastReport(root: File): List<Plugin> = try {
        lastReportStrict(root)
    } catch (failure: Throwable) {
        LauncherLog.log("mods: could not read the last report: $failure")
        emptyList()
    }

    internal fun lastReportStrict(root: File): List<Plugin> {
        val file = reportFile(root)
        if (!file.isFile) return emptyList()
        return parse(file.readText())
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
     * Every plugin in the folder, switched on or not: the gate the weaver
     * wraps each patch in is what decides that, and it is read at startup.
     *
     * Failures are reported, not thrown. One broken plugin out of six should
     * cost that plugin and nothing else -- and the whole point of doing this
     * before the native build is that a mod which cannot work says so now,
     * rather than after seventeen minutes of clang.
     */
    suspend fun weave(
        context: android.content.Context,
        root: File,
        mods: File,
        assemblies: File,
        assets: android.content.res.AssetManager,
        onLine: (String) -> Unit = {},
    ): List<Plugin> {
        val plugins = all(mods)
        if (plugins.isEmpty()) {
            reportFile(root).writeText("{\"plugins\":[]}")
            return emptyList()
        }

        val weaver = stageWeaver(root, assets)
        val argv = ArrayList<String>()
        argv += "weave"
        argv += "--assemblies"
        argv += assemblies.absolutePath
        argv += "--report"
        argv += reportFile(root).absolutePath
        for (dll in plugins) {
            argv += "--mod"
            argv += dll.absolutePath
        }

        val result = MonoRuntime.exec(context, weaver, argv, cwd = weaver.parentFile, onLine = onLine)
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
