// Il2cppConverter — turning the game's IL into C++, on the device.
//
// This is the step everything else was assumed to need a PC for. Unity ships
// il2cpp as a self-contained .NET application with a Linux-x64 apphost, but
// the apphost is only a launcher: il2cpp.dll beside it is portable IL. Strip
// the bundled runtime and the deps.json, and it becomes an ordinary
// framework-dependent app that any .NET can run -- including the one
// MonoRuntime ships.
//
// This once ran on a PC, as a shell script and as steps 1 and 2 of
// tools/depot-to-apk/build.sh; both are retired. A desktop run of the same
// inputs produces byte-identical output: all generated sources and
// global-metadata.dat match by SHA-256.
//
// The assembly set is the part that is easy to get wrong, and it fails
// obscurely when it is. The depot's Managed/ folder cannot be handed to
// il2cpp wholesale -- it holds the *Mono* flavour of the class library, and
// IL2CPP wants the unityaot profile. Feeding it the Mono set does not produce
// a message about profiles; it dies deep inside the converter while building
// shared enum types, saying "System.Byte, ". The composition Unity itself uses
// for a build is:
//
//   class library   Unity's unityaot-linux profile
//   UnityEngine     the *Android* player's Managed folder, not the depot's
//                   Linux one
//   game code       the depot, and only what the two above do not provide

package dev.silksong.launcher

import dev.silksong.launcher.profiles.GameProfile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File
import java.io.IOException
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.util.Properties

object Il2cppConverter {

    data class Progress(val step: String, val fraction: Float, val detail: String = "")

    /**
     * The generated C++ and the player data.
     *
     * On external storage: this is around 1.5 GB of source that nothing has to
     * execute, and internal storage is the scarce kind.
     */
    fun rootFor(context: android.content.Context): File =
        File(context.getExternalFilesDir(null), "build")

    fun cppDir(root: File): File = File(root, "cpp")
    fun dataDir(root: File): File = File(root, "data")
    fun asmDir(root: File): File = File(root, "asm")

    /** global-metadata.dat is the one output nothing else can substitute for. */
    fun metadata(root: File): File = File(dataDir(root), "Metadata/global-metadata.dat")

    /**
     * Written only after il2cpp returns success and its required outputs have
     * been verified. A process kill cannot run a catch/finally block, so the
     * output tree itself needs a durable commit marker: metadata plus one C++
     * file merely proves that an interrupted converter had started writing.
     */
    internal fun completionMarker(root: File): File = File(root, "convert.complete")

    private const val COMPLETE = "complete"
    private const val UI_MESSAGE_DISMISS_SCHEMA = "1"
    internal const val UI_MESSAGE_DISMISS_ALGORITHM =
        "task105-v1;game=1.0.29980;tail=1886e0884a720b0b53412e04f912fb6d7c31d7c9da9cd365e9ac6b85fc4bc179;order=post-save-pre-mod"
    private const val UI_MESSAGE_DISMISS_MARKER = "uimsg-bridge.properties"
    private const val UI_MESSAGE_DISMISS_PART_SUFFIX = ".uimsg-bridge.part"
    private const val SILKSONG_DEATH_SCHEMA = "2"
    internal const val SILKSONG_DEATH_ALGORITHM =
        "task101-v4;game=1.0.29980;assembly=1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d;rewritten=86e8ffd402bb5e58c57d89ef2e0c3fa8dd3e89a9c1c049d4bf663484434b6a8e;site=post-normalization;owners=ring32;verify=canonical+final-structural;order=pre-save-through-post-mod"
    private const val SILKSONG_DEATH_REWRITTEN_SHA256 =
        "86e8ffd402bb5e58c57d89ef2e0c3fa8dd3e89a9c1c049d4bf663484434b6a8e"
    private const val SILKSONG_DEATH_MARKER = "silksong-death-bridge.properties"
    private const val SILKSONG_DEATH_PART_SUFFIX = ".silksong-death-bridge.part"
    private val SHA256 = Regex("^[0-9a-f]{64}$")

    internal fun requiresUiMessageDismissal(profile: GameProfile): Boolean =
        profile.id == "silksong"

    /** How often the output directory is counted while il2cpp works. */
    private const val PROGRESS_POLL_MS = 2_000L

    /**
     * What the last conversion produced, and what the next one is measured
     * against.
     *
     * A count rather than a guess at the work: the set is 186 assemblies of
     * someone else's game, and nothing here can predict how much C++ that
     * becomes. It barely moves between runs, though -- the same depot and the
     * same patches produce the same files -- so the last answer is a good
     * denominator and a wrong one only costs a bar that fills unevenly.
     *
     * Kept beside the output rather than in it: [convert] empties the
     * directory before il2cpp starts, which is exactly when this is needed.
     */
    private fun sourceCount(root: File) = File(root, "cpp.count")

    private fun expectedSources(root: File): Int =
        sourceCount(root).takeIf { it.isFile }?.readText()?.trim()?.toIntOrNull()
            ?.takeIf { it > 0 } ?: DEFAULT_SOURCES

    private fun rememberSources(root: File) {
        val n = cppDir(root).list()?.size ?: return
        if (n > 0) runCatching { sourceCount(root).writeText(n.toString()) }
    }

    /**
     * The count before there has ever been one, from a Pixel 10 Pro: 950 .cpp
     * and 197 .c, plus a header. Only ever used for a first conversion, and
     * replaced by that conversion's own number.
     */
    private const val DEFAULT_SOURCES = 1148

    /**
     * Proof that a conversion ran all the way to the end.
     *
     * [isPresent] used to ask only whether global-metadata.dat and some .cpp
     * existed, and a conversion killed near the end satisfies both -- the
     * metadata lands before the last of eleven hundred sources do. So an
     * interrupted run was inherited by the next build as finished work: the
     * conversion was skipped, the compile took whatever partial tree was on
     * disk, and the link accepted it because it allows undefined symbols. The
     * result was a libil2cpp.so nine megabytes short of the real one, twelve
     * minutes later, which the engine reported at launch as
     *
     *   dlopen failed: library "libil2cpp.so" not found
     *
     * -- naming neither the file it did find nor anything that happened here.
     *
     * That is not a rare shape. The conversion is three and a half minutes of
     * a memory-hungry .NET process, and a device that reclaims the app during
     * it drops the user back on a launcher with a Play button, because the
     * build screen has already finished itself and only the launcher is
     * restored. The recovery that worked was "Reset build", which is a thing
     * somebody has to know to do.
     *
     * Written last and deleted first, so it never outlives the output it
     * describes. Same reasoning as SetupActivity's .built marker and
     * NativeBuild's .stamp, one layer down.
     */
    private fun signatureMarker(root: File) = File(root, "cpp.done")

    internal fun uiMessageDismissMarker(root: File): File =
        File(root, UI_MESSAGE_DISMISS_MARKER)

    internal fun silksongDeathBridgeMarker(root: File): File =
        File(root, SILKSONG_DEATH_MARKER)

    private fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (count > 0) digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun uiMessageDismissProperties(root: File): Properties? =
        runCatching {
            Properties().apply {
                uiMessageDismissMarker(root).reader().use(::load)
            }
        }.getOrNull()

    internal fun recordUiMessageDismissProvenance(
        root: File,
        surgery: File,
        assembly: File,
        inputSha256: String,
    ) {
        if (!SHA256.matches(inputSha256)) {
            throw IOException("invalid ui message bridge input SHA-256")
        }
        if (surgery.length() <= 0 || assembly.length() <= 0) {
            throw IOException("cannot record ui message bridge provenance for empty inputs")
        }
        val marker = uiMessageDismissMarker(root)
        val part = File(root, "${marker.name}.part")
        part.delete()
        part.writeText(
            buildString {
                append("schema=").append(UI_MESSAGE_DISMISS_SCHEMA).append('\n')
                append("algorithm=").append(UI_MESSAGE_DISMISS_ALGORITHM).append('\n')
                append("inputSha256=").append(inputSha256).append('\n')
                append("assemblySha256=").append(sha256(assembly)).append('\n')
                append("toolSha256=").append(sha256(surgery)).append('\n')
            },
        )
        try {
            Files.move(part.toPath(), marker.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (error: Exception) {
            part.delete()
            throw IOException("could not commit ui message bridge provenance", error)
        }
    }

    internal fun hasUiMessageDismissProvenance(root: File): Boolean {
        val values = uiMessageDismissProperties(root) ?: return false
        val input = values.getProperty("inputSha256") ?: return false
        val assemblyHash = values.getProperty("assemblySha256") ?: return false
        val toolHash = values.getProperty("toolSha256") ?: return false
        if (values.getProperty("schema") != UI_MESSAGE_DISMISS_SCHEMA ||
            values.getProperty("algorithm") != UI_MESSAGE_DISMISS_ALGORITHM ||
            !SHA256.matches(input) || !SHA256.matches(assemblyHash) || !SHA256.matches(toolHash)
        ) return false
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        val surgery = File(root, "bundle-surgery/BundleSurgery.dll")
        return assembly.isFile && assembly.length() > 0 && sha256(assembly) == assemblyHash &&
            surgery.isFile && surgery.length() > 0 && sha256(surgery) == toolHash
    }

    private fun recordedUiMessageDismissToolSha256(root: File): String? =
        uiMessageDismissProperties(root)?.getProperty("toolSha256")
            ?.takeIf(SHA256::matches)

    internal fun uiMessageDismissToolMatches(root: File, assetSha256: String): Boolean =
        SHA256.matches(assetSha256) &&
            recordedUiMessageDismissToolSha256(root) == assetSha256

    private fun silksongDeathBridgeProperties(root: File): Properties? = runCatching {
        Properties().apply { silksongDeathBridgeMarker(root).reader().use(::load) }
    }.getOrNull()

    internal fun recordSilksongDeathBridgeProvenance(
        root: File,
        surgery: File,
        assembly: File,
        inputSha256: String,
    ) {
        if (!SHA256.matches(inputSha256) || surgery.length() <= 0 || assembly.length() <= 0) {
            throw IOException("invalid Silksong death bridge provenance inputs")
        }
        val marker = silksongDeathBridgeMarker(root)
        val part = File(root, "${marker.name}.part")
        part.delete()
        part.writeText(buildString {
            append("schema=").append(SILKSONG_DEATH_SCHEMA).append('\n')
            append("algorithm=").append(SILKSONG_DEATH_ALGORITHM).append('\n')
            append("inputSha256=").append(inputSha256).append('\n')
            append("bridgeAssemblySha256=").append(sha256(assembly)).append('\n')
            append("toolSha256=").append(sha256(surgery)).append('\n')
        })
        try {
            Files.move(part.toPath(), marker.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (error: Exception) {
            part.delete()
            throw IOException("could not commit Silksong death bridge provenance", error)
        }
    }

    internal suspend fun verifyFinalStagedSilksongNormalDeath(
        root: File,
        surgery: File,
        runVerify: suspend (assembly: File) -> Unit,
    ) {
        val provisional = silksongDeathBridgeProperties(root)
            ?: throw IOException("Silksong death bridge has no provisional provenance")
        val input = provisional.getProperty("inputSha256") ?: ""
        val bridge = provisional.getProperty("bridgeAssemblySha256") ?: ""
        val tool = provisional.getProperty("toolSha256") ?: ""
        if (provisional.getProperty("schema") != SILKSONG_DEATH_SCHEMA ||
            provisional.getProperty("algorithm") != SILKSONG_DEATH_ALGORITHM ||
            !SHA256.matches(input) || !SHA256.matches(bridge) || !SHA256.matches(tool) ||
            !surgery.isFile || surgery.length() <= 0 || sha256(surgery) != tool
        ) throw IOException("Silksong death bridge provisional provenance is invalid")
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        if (!assembly.isFile || assembly.length() <= 0) {
            throw IOException("no final staged Assembly-CSharp.dll for Silksong death verification")
        }
        val finalHash = sha256(assembly)
        val marker = silksongDeathBridgeMarker(root)
        if (!marker.delete()) throw IOException("could not invalidate provisional Silksong death provenance")
        try {
            runVerify(assembly)
            if (!assembly.isFile || assembly.length() <= 0 || sha256(assembly) != finalHash) {
                throw IOException("final Silksong death verification input changed during verification")
            }
            val part = File(root, "${marker.name}.part")
            part.delete()
            part.writeText(buildString {
                append("schema=").append(SILKSONG_DEATH_SCHEMA).append('\n')
                append("algorithm=").append(SILKSONG_DEATH_ALGORITHM).append('\n')
                append("inputSha256=").append(input).append('\n')
                append("bridgeAssemblySha256=").append(bridge).append('\n')
                append("finalAssemblySha256=").append(finalHash).append('\n')
                append("finalVerification=structural-v1\n")
                append("toolSha256=").append(tool).append('\n')
            })
            Files.move(part.toPath(), marker.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (error: Exception) {
            File(root, "${marker.name}.part").delete()
            marker.delete()
            throw error
        }
    }

    internal fun hasSilksongDeathBridgeProvenance(
        root: File,
        expectedBridgeSha256: String = SILKSONG_DEATH_REWRITTEN_SHA256,
    ): Boolean {
        val values = silksongDeathBridgeProperties(root) ?: return false
        val input = values.getProperty("inputSha256") ?: return false
        val output = values.getProperty("bridgeAssemblySha256") ?: return false
        val finalOutput = values.getProperty("finalAssemblySha256") ?: return false
        val tool = values.getProperty("toolSha256") ?: return false
        val surgery = File(root, "bundle-surgery/BundleSurgery.dll")
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        return values.getProperty("schema") == SILKSONG_DEATH_SCHEMA &&
            values.getProperty("algorithm") == SILKSONG_DEATH_ALGORITHM &&
            values.getProperty("finalVerification") == "structural-v1" &&
            input == "1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d" &&
            output == expectedBridgeSha256 && SHA256.matches(expectedBridgeSha256) && SHA256.matches(finalOutput) &&
            SHA256.matches(tool) && surgery.isFile && surgery.length() > 0 && sha256(surgery) == tool &&
            assembly.isFile && assembly.length() > 0 && sha256(assembly) == finalOutput
    }

    /**
     * What a finished conversion looks like, as a string.
     *
     * The source count and the metadata's size rather than a bare "yes":
     * those also notice a tree that has been pruned, or a metadata file
     * replaced, since the run that wrote this.
     */
    private fun completionSignature(root: File): String {
        val bridge = uiMessageDismissMarker(root)
        val bridgeHash = if (bridge.isFile) sha256(bridge) else "missing"
        val deathBridge = silksongDeathBridgeMarker(root)
        val deathBridgeHash = if (deathBridge.isFile) sha256(deathBridge) else "missing"
        return "${cppDir(root).list()?.size ?: 0}:${metadata(root).length()}:$bridgeHash:$deathBridgeHash"
    }

    private fun hasCompletionSignature(root: File): Boolean =
        runCatching { signatureMarker(root).readText().trim() }.getOrNull() ==
            completionSignature(root)

    /**
     * Whether the conversion on disk is one that finished and is still intact.
     *
     * A build made before both markers existed does not carry them and converts
     * again, once. That costs about four minutes -- the compile that follows is
     * incremental and re-generated C++ is byte-identical, so almost nothing is
     * rebuilt -- and it is the direction to be wrong in.
     */
    fun isComplete(profile: GameProfile, root: File): Boolean =
        (!requiresUiMessageDismissal(profile) ||
            (uiMessageDismissMarker(root).isFile && hasUiMessageDismissProvenance(root))) &&
            runCatching { completionMarker(root).readText().trim() }.getOrNull() == COMPLETE &&
            hasCompletionSignature(root) &&
            metadata(root).length() > 0 &&
            cppDir(root).listFiles()?.any { it.name.endsWith(".cpp") } == true

    fun isPresent(profile: GameProfile, root: File): Boolean = isComplete(profile, root)

    /**
     * Whether the conversion is older than what it was made from.
     *
     * Only our own assemblies and the mods folder are checked, because only
     * they change without anything else changing: the depot is fixed and the
     * Input System is rebuilt from a pinned version, but the port's own code
     * is edited between builds and a plugin can be dropped in at any time.
     * Without this a changed patch compiles happily, is copied into the
     * assembly set, and is then skipped by a conversion that thinks it has
     * nothing to do -- so the player keeps running the previous version and
     * nothing says otherwise.
     *
     * BOTH of ours, and that is not a tidiness point. SilksongIo arrived in a
     * release whose patch sources had not changed at all, so a check that
     * looked only at SilksongPatches said "nothing to do" and skipped the
     * conversion that applies the File.Replace redirect -- which would have
     * shipped a release whose headline fix reached nobody who already had a
     * build. An assembly of ours that is missing from the staged set counts as
     * stale for the same reason: that is exactly what an upgrade looks like.
     */
    fun isStale(
        profile: GameProfile,
        root: File,
        mods: File? = null,
        assets: android.content.res.AssetManager? = null,
    ): Boolean {
        if (requiresUiMessageDismissal(profile) && !hasUiMessageDismissProvenance(root)) return true
        if (profile.id == "silksong" && !hasSilksongDeathBridgeProvenance(root)) return true
        if (requiresUiMessageDismissal(profile) && assets != null) {
            val currentTool = runCatching { PlayerImage.surgeryAssetSha256(assets) }.getOrNull()
                ?: return true
            if (!uiMessageDismissToolMatches(root, currentTool)) return true
        }
        if (mods != null && !Mods.candidateMetadataPresent(root)) return true
        if (mods != null && Mods.isStale(mods, root, assets)) return true
        val ours = buildList {
            add(PackageCompiler.patchAssembly(profile, root))
            if (PackageCompiler.requiresSaveIo(profile)) add(PackageCompiler.ioAssembly(root))
            addAll(PackageCompiler.shimAssemblies(root))
        }
        for (built in ours) {
            if (!built.isFile) continue
            val staged = File(asmDir(root), built.name)
            if (!staged.isFile) return true
            // Content, not length. Two builds of the patches differ in what
            // they do far more often than in how big they are, and a same-size
            // assembly read as "unchanged" means the conversion is skipped, the
            // old generated C++ is recompiled, and the device runs the previous
            // version of a patch while every log line says the build succeeded.
            // That is not hypothetical: it cost three rounds of chasing a bug
            // that had already been fixed.
            if (staged.length() != built.length()) return true
            if (!staged.readBytes().contentEquals(built.readBytes())) return true
        }
        return false
    }

    // ── inputs ─────────────────────────────────────────────────────────────

    private fun bclDir(unity: File) =
        File(unity, "editor/Editor/Data/MonoBleedingEdge/lib/mono/unityaot-linux")

    private fun engineManagedDir(unity: File) =
        File(unity, "android/Variations/il2cpp/Managed")

    private fun deployDir(unity: File) =
        File(unity, "editor/Editor/Data/il2cpp/build/deploy")

    /**
     * The depot's Managed folder.
     *
     * Off [PlayerImage.depotData] rather than looked for separately: the depot
     * may be nested under folders a person copied it inside, and two different
     * ideas of where it is means one of them finds a hand-placed copy and the
     * other does not.
     */
    private fun depotManaged(depot: File): File? =
        PlayerImage.depotData(depot)?.let { File(it, "Managed") }
            ?.takeIf { File(it, "Assembly-CSharp.dll").isFile }

    // ── the run ────────────────────────────────────────────────────────────

    fun convert(
        profile: GameProfile,
        unity: File,
        depot: File,
        context: android.content.Context,
        root: File,
        mods: File? = null,
        assets: android.content.res.AssetManager? = null,
    ): Flow<Progress> = channelFlow {
        val bcl = bclDir(unity)
        val engine = engineManagedDir(unity)
        val deploy = deployDir(unity)
        val managed = depotManaged(depot)
            ?: throw IOException("no Managed folder with Assembly-CSharp.dll under $depot")
        // Last line of defence, and the one that names the cause. Everything
        // above this can be reached by a depot that was accepted before the
        // platform was ever checked -- an app updated mid-build, a pointer
        // written by an older version -- and the failure without it is the
        // one this check exists because of: four minutes of work, then il2cpp
        // dying on two class libraries at once with nothing readable to say.
        PlayerImage.wrongBuildProblem(depot)?.let { throw IOException(it) }
        if (!bcl.isDirectory) throw IOException("the unityaot class library is missing: $bcl")
        if (!engine.isDirectory) throw IOException("the Android player's Managed folder is missing: $engine")
        if (!File(deploy, "il2cpp.dll").isFile) throw IOException("il2cpp.dll is missing: $deploy")

        // Invalidate the previous commit before touching staged assemblies.
        // If Android kills this process at any later instruction, the next run
        // must convert again instead of compiling a half-written C++ tree.
        invalidateCompletion(root)

        send(Progress("Preparing the converter", -1f, "assemblies"))
        var assemblies = stageAssemblies(bcl, engine, managed, PackageCompiler.outputDir(root), asmDir(root))
        LauncherLog.log("il2cpp input: ${assemblies.size} assemblies")

        if (profile.id == "silksong") bridgeSilksongNormalDeath(context, root)

        if (PackageCompiler.requiresSaveIo(profile)) redirectSaveCalls(context, root)

        if (requiresUiMessageDismissal(profile)) bridgeUiMessageDismissal(context, root)

        // The chainloader, run here rather than at game startup: this is the
        // last moment the game exists as IL, so it is the only moment a
        // Harmony patch can be applied. Plugins are woven into the staged set
        // and then converted along with everything else, which is why the
        // assembly list is taken again afterwards -- a plugin il2cpp is not
        // handed is a plugin that is not in the game.
        //
        // Every plugin present is woven, not only the enabled ones. Which are
        // on is decided at startup by the gate each weave is wrapped in, so a
        // toggle costs nothing and only adding or removing a file is a
        // rebuild. See Mods.gates.
        val modInput = if (mods != null && assets != null) {
            Mods.snapshotForBuild(mods, root)
        } else {
            null
        }
        if (modInput != null && assets != null) {
            val plugins = Mods.all(modInput)
            if (plugins.isNotEmpty()) {
                send(Progress("Weaving mods", -1f, "${plugins.size} plugin(s)"))
            }
            Mods.weave(context, root, modInput, asmDir(root), assets) { line ->
                trySend(Progress("Weaving mods", -1f, line.take(80)))
            }
            if (plugins.isNotEmpty()) {
                assemblies = asmDir(root).listFiles().orEmpty()
                    .filter { it.name.endsWith(".dll") }.sortedBy { it.name }
                LauncherLog.log("il2cpp input after weaving: ${assemblies.size} assemblies")
            }
        }

        if (profile.id == "silksong") verifyFinalSilksongNormalDeath(context, root)

        prepareTool(deploy)

        val argv = ArrayList<String>()
        argv += "--convert-to-cpp"
        // The command line is long -- around 185 of these -- but it is handed
        // to the runtime as an array, so the argument limit that would force a
        // shell to spill them to a file does not apply.
        for (a in assemblies) argv += "--assembly=${a.absolutePath}"
        argv += "--generatedcppdir=${cppDir(root).absolutePath}"
        argv += "--data-folder=${dataDir(root).absolutePath}"
        // Must match the class library staged above.
        argv += "--dotnetprofile=unityaot-linux"
        argv += "--emit-null-checks"
        argv += "--enable-array-bounds-check"
        argv += "--static-lib-il2-cpp"

        val expected = expectedSources(root)
        val log = File(root, "convert.log")
        val started = System.currentTimeMillis()

        // One attempt, and the machinery that makes it watchable.
        //
        // A function rather than a block because a conversion can be reclaimed
        // for memory and tried again smaller, and everything here has to be
        // done afresh when it is: the output directory is emptied, the log is
        // rewritten, and the progress counter starts from nothing.
        suspend fun attempt(budget: MonoRuntime.Budget): Toolchain.Result {
            // Any previous attempt is cleared: il2cpp is not asked to reconcile
            // a half-written tree, and a stale .cpp left behind by an
            // interrupted run would be compiled into the result.
            //
            // The marker goes first, and on its own line, so that a run killed
            // anywhere below here leaves output that says outright it is
            // unfinished rather than output the next build mistakes for work
            // it does not have to do.
            invalidateCompletion(root)
            cppDir(root).deleteRecursively()
            dataDir(root).deleteRecursively()
            cppDir(root).mkdirs()
            dataDir(root).mkdirs()

            LauncherLog.log("il2cpp: starting with $budget; ${MonoRuntime.memory(context)}")
            send(Progress("Converting to C++", 0f, "0 of $expected files"))
            val sink = log.bufferedWriter()
            // Real progress, counted off the output directory.
            //
            // il2cpp says nothing at all while it works: its stdout is block
            // buffered into a file and the whole of it arrives at once when the
            // process ends, so the line-driven detail below never fires and the
            // bar had nothing to move on. Six minutes of a full stop looks
            // exactly like a hang -- one person waited half an hour and gave up
            // on a conversion that may well have been working.
            //
            // The files themselves are the honest signal: they land steadily
            // throughout, and there is no interpretation involved in counting
            // them. The total is close to fixed for a given depot and patch set,
            // so the previous run's count is the denominator and the constant is
            // only ever used once.
            val ticker = launch(Dispatchers.IO) {
                while (isActive) {
                    delay(PROGRESS_POLL_MS)
                    val n = cppDir(root).list()?.size ?: 0
                    // Never quite full: the step is over when il2cpp says so, not
                    // when a guessed total is reached, and a bar that sits at 100%
                    // is a bar that has started lying.
                    trySend(
                        Progress(
                            "Converting to C++",
                            (n.toFloat() / expected).coerceIn(0f, 0.99f),
                            "$n of $expected files",
                        ),
                    )
                }
            }
            return try {
                MonoRuntime.exec(
                    context,
                    File(deploy, "il2cpp.dll"),
                    argv,
                    // il2cpp resolves parts of its own installation relative to
                    // the working directory.
                    cwd = deploy,
                    env = budget.toEnv(),
                ) { line ->
                    sink.write(line); sink.write("\n")
                }
            } finally {
                ticker.cancel()
                sink.flush(); sink.close()
            }
        }

        var budget = MonoRuntime.budget(context)
        var result = attempt(budget)
        // Reclaimed rather than failed: the settings were too generous for
        // this device as it stood, so the same work is offered a smaller share
        // of it. Only for that one cause -- a conversion that threw is a
        // conversion that will throw again, and retrying it costs the user
        // another six minutes to reach the same message.
        while (result.outOfMemory) {
            val next = budget.tighter() ?: break
            budget = next
            LauncherLog.log("il2cpp: reclaimed for memory; retrying with $budget")
            send(Progress("Converting to C++", 0f, "retrying with less memory"))
            result = attempt(budget)
        }
        val seconds = (System.currentTimeMillis() - started) / 1000

        if (!result.ok) {
            if (result.outOfMemory) {
                throw IOException(
                    "il2cpp ran out of memory after ${seconds}s, at the smallest settings " +
                        "there are ($budget). Close other apps, or restart the device, and " +
                        "try again -- ${MonoRuntime.memory(context)}.",
                )
            }
            val why = result.output.lineSequence()
                .firstOrNull { it.contains("rror", true) || it.contains("xception", true) }
                ?.trim()?.take(300)
                ?: lastWords(log)
            throw IOException("il2cpp failed after ${seconds}s, exit ${result.code}: $why")
        }
        if (metadata(root).length() <= 0) {
            throw IOException("il2cpp produced no global-metadata.dat")
        }

        val cpp = cppDir(root).listFiles()?.count { it.name.endsWith(".cpp") } ?: 0
        val c = cppDir(root).listFiles()?.count { it.name.endsWith(".c") } ?: 0
        rememberSources(root)
        LauncherLog.log(
            "il2cpp: ${seconds}s, $cpp cpp + $c c, metadata ${metadata(root).length()} bytes",
        )
        // This belongs only to the reusable candidate cache. Publication copies
        // it into a sealed generation after native/content verification succeeds.
        if (modInput != null && assets != null) Mods.recordCandidate(modInput, root, assets)
        markComplete(root)
        send(Progress("Converted", 1f, "$cpp C++ files in ${seconds}s"))
    }.flowOn(Dispatchers.IO)

    internal fun invalidateCompletion(root: File) {
        val marker = completionMarker(root)
        val part = File(root, "${marker.name}.part")
        for (file in listOf(marker, part, signatureMarker(root))) {
            if (file.exists() && !file.delete()) {
                throw IOException("could not invalidate the previous il2cpp conversion marker: $file")
            }
        }
    }

    internal fun markComplete(root: File) {
        // The signature notices later damage to the output. The atomic marker
        // is written last and is the commit point, so neither an interrupted
        // signature write nor a failed rename can bless a partial conversion.
        signatureMarker(root).writeText(completionSignature(root))
        val marker = completionMarker(root)
        val part = File(root, "${marker.name}.part")
        part.writeText(COMPLETE)
        if (!part.renameTo(marker)) {
            part.delete()
            throw IOException("could not commit the completed il2cpp conversion marker")
        }
    }

    /**
     * The last thing il2cpp said, for a failure that said nothing error-shaped.
     *
     * The line above this picks the first line of output that reads like an
     * error, which is the right answer when there is one and was the only
     * answer there was. When there is not, what it fell back to was "exit
     * 120" -- and that is what a bug report on 1.0.3-rc3 consisted of, for a
     * failure whose cause was sitting unread in convert.log a directory away.
     * il2cpp does not always announce itself: fed two class libraries at once
     * it dies part-way through a sentence about a type, which contains
     * neither "error" nor "exception".
     *
     * So: the end of the log, which is where a program that stopped says why.
     * Read from the tail rather than whole -- a conversion that got some way
     * in leaves megabytes -- and the first line of that read is dropped,
     * because a seek to a byte offset lands in the middle of one.
     */
    private fun lastWords(log: File): String {
        val lines = runCatching {
            java.io.RandomAccessFile(log, "r").use { raf ->
                val from = (raf.length() - TAIL_BYTES).coerceAtLeast(0L)
                raf.seek(from)
                val buf = ByteArray((raf.length() - from).toInt())
                raf.readFully(buf)
                String(buf, Charsets.UTF_8).lines().let { if (from > 0) it.drop(1) else it }
            }
        }.getOrDefault(emptyList())
            .map { it.trim() }
            .filter { it.isNotEmpty() }
        if (lines.isEmpty()) return "it printed nothing at all, so ${log.name} is empty too"
        return lines.takeLast(TAIL_LINES).joinToString(" | ").takeLast(300)
    }

    /** How much of the converter's log a failure quotes. */
    private const val TAIL_BYTES = 8L * 1024L
    private const val TAIL_LINES = 3

    /**
     * Composes the assembly set, in the order Unity composes it.
     *
     * Later sources do not overwrite earlier ones: the class library and the
     * engine win over the depot's copies of the same names, which is the whole
     * point -- the depot carries the Linux player's UnityEngine assemblies and
     * the Mono class library, and both are wrong here.
     *
     * [packages] is different: those are Android builds of packages the depot
     * ships as desktop builds, and they REPLACE the depot's copy rather than
     * merely being preferred to it. Only names the set already has are taken,
     * because adding an unrelated assembly would change the type graph for no
     * reason.
     */
    internal fun atomicReplaceStaged(part: File, target: File) {
        try {
            Files.move(part.toPath(), target.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (error: Exception) {
            throw IOException("could not atomically replace staged ${target.name}", error)
        }
    }

    internal suspend fun rewriteStagedSilksongNormalDeath(
        root: File,
        surgery: File,
        expectedRewrittenSha256: String = SILKSONG_DEATH_REWRITTEN_SHA256,
        replace: (File, File) -> Unit = ::atomicReplaceStaged,
        runRewrite: suspend (input: File, output: File) -> Unit,
        runVerify: suspend (output: File) -> Unit,
    ) {
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        if (!assembly.isFile || assembly.length() <= 0) throw IOException("no staged Assembly-CSharp.dll for Silksong death bridge")
        if (!surgery.isFile || surgery.length() <= 0) throw IOException("no BundleSurgery.dll for Silksong death bridge")
        if (!SHA256.matches(expectedRewrittenSha256)) throw IOException("invalid expected Silksong death bridge SHA-256")
        val marker = silksongDeathBridgeMarker(root)
        val markerPart = File(root, "${marker.name}.part")
        for (stale in listOf(marker, markerPart)) if (stale.exists() && !stale.delete())
            throw IOException("could not invalidate stale Silksong death bridge provenance: $stale")
        val output = File(assembly.parentFile, assembly.name + SILKSONG_DEATH_PART_SUFFIX)
        if (output.exists() && !output.delete()) throw IOException("could not remove stale Silksong death bridge output: $output")
        val inputSha256 = sha256(assembly)
        try {
            runRewrite(assembly, output)
            if (!output.isFile || output.length() <= 0) throw IOException("Silksong death bridge produced no rewritten assembly")
            val managedImage = output.inputStream().use { it.read() == 'M'.code && it.read() == 'Z'.code }
            if (!managedImage) throw IOException("Silksong death bridge produced an invalid managed assembly")
            val outputSha256 = sha256(output)
            if (outputSha256 != expectedRewrittenSha256) {
                throw IOException("Silksong death bridge output differs from canonical identity: $outputSha256")
            }
            runVerify(output)
            val verifiedSha256 = sha256(output)
            if (verifiedSha256 != expectedRewrittenSha256) {
                throw IOException("verified Silksong death bridge output identity changed: $verifiedSha256")
            }
            replace(output, assembly)
            recordSilksongDeathBridgeProvenance(root, surgery, assembly, inputSha256)
        } finally {
            output.delete()
        }
    }

    private suspend fun bridgeSilksongNormalDeath(context: android.content.Context, root: File) {
        val surgery = PlayerImage.stageSurgery(root, context.assets)
        rewriteStagedSilksongNormalDeath(
            root = root,
            surgery = surgery,
            runRewrite = { input, output ->
                PlayerImage.run(surgery, context,
                    listOf("bridge-silksong-normal-death", input.absolutePath, output.absolutePath)) {
                    line -> LauncherLog.log("Silksong death bridge: ${line.trim()}")
                }
            },
            runVerify = { output ->
                PlayerImage.run(surgery, context,
                    listOf("verify-silksong-normal-death", output.absolutePath)) {
                    line -> LauncherLog.log("Silksong death verification: ${line.trim()}")
                }
            },
        )
    }

    private suspend fun verifyFinalSilksongNormalDeath(
        context: android.content.Context,
        root: File,
    ) {
        val surgery = PlayerImage.stageSurgery(root, context.assets)
        verifyFinalStagedSilksongNormalDeath(root, surgery) { assembly ->
            PlayerImage.run(
                surgery,
                context,
                listOf("verify-silksong-normal-death-final", assembly.absolutePath),
            ) { line -> LauncherLog.log("Final Silksong death verification: ${line.trim()}") }
        }
    }

    internal suspend fun rewriteStagedUiMessageDismissal(
        root: File,
        surgery: File,
        replace: (File, File) -> Unit = ::atomicReplaceStaged,
        runRewrite: suspend (input: File, output: File) -> Unit,
    ) {
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        if (!assembly.isFile || assembly.length() <= 0) {
            throw IOException("no staged Assembly-CSharp.dll for ui message bridge")
        }
        if (!surgery.isFile || surgery.length() <= 0) {
            throw IOException("no BundleSurgery.dll for ui message bridge")
        }
        val marker = uiMessageDismissMarker(root)
        val markerPart = File(root, "${marker.name}.part")
        for (stale in listOf(marker, markerPart)) {
            if (stale.exists() && !stale.delete()) {
                throw IOException("could not invalidate stale ui message bridge provenance: $stale")
            }
        }
        val output = File(assembly.parentFile, assembly.name + UI_MESSAGE_DISMISS_PART_SUFFIX)
        if (output.exists() && !output.delete()) {
            throw IOException("could not remove stale ui message bridge output: $output")
        }
        val inputSha256 = sha256(assembly)
        try {
            runRewrite(assembly, output)
            if (!output.isFile || output.length() <= 0) {
                throw IOException("ui message bridge produced no rewritten Assembly-CSharp.dll")
            }
            val managedImage = output.inputStream().use { input ->
                input.read() == 'M'.code && input.read() == 'Z'.code
            }
            if (!managedImage) {
                throw IOException("ui message bridge produced an invalid managed assembly")
            }
            replace(output, assembly)
            if (!assembly.isFile || assembly.length() <= 0) {
                throw IOException("ui message bridge replacement left no staged Assembly-CSharp.dll")
            }
            recordUiMessageDismissProvenance(root, surgery, assembly, inputSha256)
        } finally {
            output.delete()
        }
    }

    private suspend fun bridgeUiMessageDismissal(
        context: android.content.Context,
        root: File,
    ) {
        val surgery = PlayerImage.stageSurgery(root, context.assets)
        rewriteStagedUiMessageDismissal(root, surgery) { input, output ->
            PlayerImage.run(
                surgery,
                context,
                listOf("bridge-ui-message-dismiss", input.absolutePath, output.absolutePath),
            ) { line -> LauncherLog.log("ui message bridge: ${line.trim()}") }
        }
    }

    /**
     * Points the game's File.Replace calls at SafeIo, before il2cpp sees them.
     *
     * This is the only moment it can be done. The launcher is not running
     * while the game is, and WriteSaveSlot is the depot's own compiled code --
     * so the call site is rewritten here, while it is still IL and after the
     * assemblies have been staged but before they are turned into C++.
     *
     * See tools/silksong-io/src/SafeIo.cs: File.Replace fails outright on some
     * devices, and the game commits every save and every shared-data write
     * through it. The helper tries the original call first, so on a device
     * where saving already worked this changes nothing at all.
     *
     * A failure here is logged rather than thrown, and that is a deliberate
     * trade. If a future Silksong update reshapes the save path the rewrite
     * will stop matching, and a game that builds and cannot save on SOME
     * devices is a great deal better than a game that will not build on any.
     */
    private suspend fun redirectSaveCalls(context: android.content.Context, root: File) {
        val assembly = File(asmDir(root), "Assembly-CSharp.dll")
        val helper = File(asmDir(root), PackageCompiler.IO_ASSEMBLY)
        if (!assembly.isFile) {
            LauncherLog.log("save fix: no Assembly-CSharp.dll staged; skipping the File.Replace redirect")
            return
        }
        if (!helper.isFile) {
            LauncherLog.log("save fix: ${PackageCompiler.IO_ASSEMBLY} was not staged; skipping the File.Replace redirect")
            return
        }
        try {
            val surgery = PlayerImage.stageSurgery(root, context.assets)
            PlayerImage.run(
                surgery,
                context,
                listOf("redirect-file-replace", assembly.absolutePath, helper.absolutePath),
            ) { line -> LauncherLog.log("save fix: ${line.trim()}") }
        } catch (t: Throwable) {
            LauncherLog.log("save fix: could not redirect File.Replace -- saving may fail on devices " +
                "whose storage cannot do it", t)
        }
    }

    private fun stageAssemblies(
        bcl: File,
        engine: File,
        managed: File,
        packages: File,
        out: File,
    ): List<File> {
        out.deleteRecursively()
        out.mkdirs()
        var fromBcl = 0
        var fromEngine = 0
        var fromDepot = 0
        for ((source, counter) in listOf(bcl to 0, engine to 1, managed to 2)) {
            for (dll in source.listFiles().orEmpty()) {
                if (!dll.isFile || !dll.name.endsWith(".dll")) continue
                val dst = File(out, dll.name)
                if (dst.exists()) continue
                dll.copyTo(dst, overwrite = true)
                when (counter) {
                    0 -> fromBcl++
                    1 -> fromEngine++
                    else -> fromDepot++
                }
            }
        }
        var overridden = 0
        var added = 0
        for (dll in packages.listFiles().orEmpty()) {
            if (!dll.isFile || !dll.name.endsWith(".dll")) continue
            val dst = File(out, dll.name)
            if (dst.exists()) {
                // A rebuilt copy of something the depot already has: replace
                // it, because the depot's is the desktop build.
                dll.copyTo(dst, overwrite = true)
                overridden++
            } else {
                // Something the depot does not have at all -- our own patches.
                // Added rather than substituted, and it must reach il2cpp or
                // none of the port's own code exists in the player.
                dll.copyTo(dst, overwrite = true)
                added++
            }
        }
        LauncherLog.log(
            "assemblies: $fromBcl class library, $fromEngine engine, $fromDepot from the depot, " +
                "$overridden rebuilt for Android, $added ours",
        )
        val all = out.listFiles().orEmpty().filter { it.name.endsWith(".dll") }.sortedBy { it.name }
        if (all.isEmpty()) throw IOException("no assemblies were staged into $out")
        return all
    }

    /**
     * Makes Unity's il2cpp runnable by a shared framework.
     *
     * It ships self-contained: a private CoreCLR, native hosts, and a
     * deps.json pinning both to linux-x64. None of that survives the move to
     * arm64, and none of it is needed -- il2cpp.dll is portable IL. The
     * private System.Private.CoreLib has to go too: it is version-locked to
     * the runtime it shipped with, and leaving it behind makes the shared
     * framework load a mismatched core library.
     *
     * Done in place, once, marked so a re-run is free.
     */
    private fun prepareTool(deploy: File) {
        val marker = File(deploy, ".silksong-prepared")
        if (marker.isFile) return

        val doomed = listOf(
            "System.Private.CoreLib.dll", "libcoreclr.so", "libclrjit.so", "libclrgc.so",
            "libhostfxr.so", "libhostpolicy.so", "libmscordaccore.so", "libmscordbi.so",
            "libcoreclrtraceptprovider.so", "createdump", "il2cpp", "il2cpp-compile",
        )
        for (name in doomed) File(deploy, name).delete()
        for (f in deploy.listFiles().orEmpty()) {
            val n = f.name
            if (n.endsWith(".deps.json") || n.endsWith(".pdb") ||
                (n.startsWith("libSystem.") && n.endsWith(".so"))
            ) f.delete()
        }

        // rollForward=latestMajor so this keeps working against whichever
        // runtime the device happens to carry. Invariant globalization keeps
        // ICU out of the picture -- il2cpp does not need culture data, and it
        // is 30 MB not to have to fetch.
        //
        // Inert, and kept anyway: nothing on the device reads it. hostfxr and
        // hostpolicy are the parts that would, and they are among the files
        // deleted above; the runtime is started through the hosting API with
        // the properties monojni passes it, which is where the settings that
        // actually take effect live. It is written so the deploy directory
        // describes what it is being run as, and Server GC says false here for
        // the same reason it says false there -- this collector has no server
        // mode, and a file claiming otherwise is a file that sends the next
        // person looking in the wrong place.
        File(deploy, "il2cpp.runtimeconfig.json").writeText(
            """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
                "rollForward": "latestMajor",
                "configProperties": {
                  "System.GC.Server": false,
                  "System.Globalization.Invariant": true,
                  "System.Globalization.PredefinedCulturesOnly": true,
                  "System.Runtime.TieredCompilation.QuickJit": false
                }
              }
            }
            """.trimIndent(),
        )
        marker.writeText("")
    }
}
