// PlayerImage — turning the depot's serialized data into an Android player image.
//
// The depot is a Linux build. Its serialized files carry a platform stamp the
// Android engine refuses ("File's Build target is: 24"), its shaders hold GLSL
// slices Android cannot load, and its version string is an internal branch
// build. This is the step that fixes all of that, and then assembles the
// result together with what the IL2CPP conversion produced into the Data
// folder the player expects.
//
// The Kotlin counterpart of step 4 of tools/depot-to-apk/build.sh. The asset
// surgery is done by the same bundle-surgery build a PC uses, carried in the
// APK and run under the .NET that MonoRuntime ships -- so its output here
// is byte-identical to a desktop run. The two edits that are plain byte pokes
// are done directly rather than shelling out for them.
//
// Two things in here are easy to get wrong and silent when they are:
//
//   "unity default resources" is an ENGINE built-in, shipped per platform, so
//   it must come from the Android player and not from the depot. The depot's
//   is the Linux one and the engine rejects it with "File's Build target is:
//   5". unity_builtin_extra beside it is the opposite -- that one is the
//   GAME's and must come from the depot.
//
//   unity_app_guid keys il2cpp's cache of what it extracts out of the package.
//   It is re-extracted only when the GUID stops matching what was cached, so a
//   GUID that is merely stable means every later build runs new generated code
//   against the first build's metadata. Nothing warns; the type indices stop
//   lining up and the first type whose parent resolves to itself sends
//   FromTypeDefinition into unbounded recursion. Hashing the metadata makes it
//   change exactly when the thing being cached changes.

package dev.silksong.launcher

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedOutputStream
import java.io.File
import java.io.IOException
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.util.zip.CRC32
import kotlin.coroutines.coroutineContext
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.ContentLayout
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.GameProfiles
import org.apache.commons.compress.archivers.zip.Zip64Mode
import org.apache.commons.compress.archivers.zip.ZipArchiveEntry
import org.apache.commons.compress.archivers.zip.ZipArchiveOutputStream

object PlayerImage {

    data class Progress(val step: String, val fraction: Float, val detail: String = "")

    private const val SURGERY_ASSET_DIR = "ondevice/bundle-surgery"
    private const val SURGERY_DLL = "BundleSurgery.dll"

    /** 21 = Vulkan. Android has no OpenGLCore, which is what a Linux build leaves. */
    private const val GRAPHICS_APIS = "21"

    /**
     * Where the assembled image goes.
     *
     * Deliberately not "Data", which is what it is called once installed.
     * External storage on Android is FUSE-backed and case-INSENSITIVE --
     * <build>/Data and <build>/data are one directory there -- and <build>/data
     * is where the IL2CPP conversion puts global-metadata.dat. Clearing the
     * image before rebuilding it therefore deleted the conversion output, and
     * the failure surfaced one step later as a missing metadata file that had
     * demonstrably been written minutes earlier. tools/depot-to-apk/build.sh
     * carries the same warning for Windows; the trap is not Windows-specific.
     */
    fun imageDir(root: File): File = File(root, "image")

    /** The patched catalog, which travels with the image rather than the content. */
    fun catalogDir(root: File): File = File(root, "aa")

    /**
     * Where the Addressables content is when the game runs.
     *
     * The catalog stores its content root once, as a length-prefixed string
     * of 56 bytes shared by every location, so this has to be short: the
     * depot's real path is far past the budget before the game's own
     * directory name is even counted. The short path is a symlink to the real
     * tree, made by install below.
     */
    fun contentRootFor(context: android.content.Context): String =
        "/data/user/0/${context.packageName}/files/aa"

    fun contentRootFor(packageName: String, paths: ProfileBuildPaths): String {
        val root = "/data/user/0/$packageName/files/p/${paths.profile.runtimeStorageKey}/aa"
        require(root.toByteArray(Charsets.UTF_8).size <= 56) {
            "Addressables content root exceeds the catalog field: $root"
        }
        return root
    }

    fun addressablesContainer(profile: GameProfile, dataDir: File): File {
        require(GameProfiles.find(profile.id) == profile) {
            "Addressables require an exact registered profile: ${profile.id}"
        }
        require(profile.contentLayout == ContentLayout.ADDRESSABLES) {
            "${profile.id} does not use Addressables"
        }
        val groupRoot = requireNotNull(profile.addressablesRoot) {
            "${profile.id} has no Addressables root"
        }.replace('\\', '/')
        val container = groupRoot.substringBeforeLast('/', missingDelimiterValue = "")
        require(container.isNotEmpty() && !container.startsWith('/') &&
            container.split('/').none { it == ".." }) {
            "Unsafe Addressables root for ${profile.id}: $groupRoot"
        }
        return File(dataDir, container)
    }

    fun isPresent(root: File): Boolean =
        File(imageDir(root), "globalgamemanagers").isFile &&
            File(imageDir(root), "Managed/Metadata/global-metadata.dat").length() > 0

    // ── staleness ──────────────────────────────────────────────────────────
    //
    // Everything below this comment used to run on every build, including all
    // the builds where only a patch changed. Neither step is cheap: the image
    // is a 55 MB zip written on a phone, and the content retarget opens and
    // parses every one of ~2000 bundles. Neither depends on a patch edit
    // except through the conversion's output, so each records what it was made
    // from and is skipped when that has not moved.
    //
    // Both stamps are written only after the step has run to completion. An
    // interrupted build therefore leaves no stamp and is redone rather than
    // assumed -- which matters most for the retarget, whose whole point is
    // that it edits the content tree in place.

    private fun imageStampFile(root: File) = File(root, "image.stamp")

    /**
     * What the player image is made of.
     *
     * The conversion's metadata is the input that moves: it is regenerated
     * whenever the patch assembly really changed, and left untouched when the
     * conversion is skipped. So its size and timestamp are what separate "the
     * patches changed" from "a patch file was edited and compiled to the same
     * assembly", which is the distinction this gate exists to make.
     *
     * The entry points are in here too, because they are read out of the APK's
     * assets rather than compiled: entrypoints.json can be edited without the
     * assembly changing at all, and gating on the metadata alone would go on
     * registering the old set while every log line said the build succeeded.
     *
     * The depot is stamped as well. It is fixed in practice, but a re-fetched
     * or repaired one is copied straight into the image, and a stale image
     * fails much later and much less clearly than a redundant rebuild does.
     */
    private fun imageStamp(profile: GameProfile, root: File, depot: File): String {
        val metadata = File(Il2cppConverter.dataDir(root), "Metadata/global-metadata.dat")
        val ggm = depotData(depot)?.let { File(it, "globalgamemanagers") }
        return buildString {
            append(profile.unityVersion).append('\n')
            append(GRAPHICS_APIS).append('\n')
            append(metadata.length()).append('/').append(metadata.lastModified()).append('\n')
            append(ggm?.length() ?: 0).append('/').append(ggm?.lastModified() ?: 0).append('\n')
            append(PackageCompiler.patchAssemblyName(profile)).append('\n')
            append(PackageCompiler.entryPoints(root).orEmpty())
        }
    }

    /**
     * Whether the image on disk is already the one this build would produce.
     *
     * The packed zip is checked as well as the staged image. They are written
     * by the same step, and a current image says nothing about whether the
     * engine can actually read it -- data.apk is what it opens.
     */
    fun isCurrent(profile: GameProfile, root: File, pkgDir: File, depot: File): Boolean =
        isPresent(root) &&
            File(pkgDir, "data.apk").length() > 0 &&
            imageStampFile(root).takeIf { it.isFile }?.readText() == imageStamp(profile, root, depot)

    fun markCurrent(profile: GameProfile, root: File, depot: File) {
        imageStampFile(root).writeText(imageStamp(profile, root, depot))
    }

    private fun contentStampFile(root: File) = File(root, "content.stamp")

    /**
     * Forgets that the content tree was retargeted.
     *
     * For when the depot moves to a folder this app has not seen before. The
     * stamp identifies a tree by what is in it, so a different copy of the
     * game is a tree it has never met and almost certainly a tree nothing has
     * retargeted. Being wrong in this direction costs a re-run of a step that
     * is idempotent; being wrong in the other direction ships bundles the
     * engine cannot read.
     */
    fun invalidateContent(root: File) {
        contentStampFile(root).delete()
    }

    /**
     * Identity of the Addressables content tree.
     *
     * Stat, not content: this is around 2000 files and several gigabytes, and
     * the only question is whether it is still the tree that was retargeted. A
     * re-fetched or repaired depot moves sizes and timestamps; a retarget that
     * is skipped moves nothing at all, so a tree that was current stays
     * current for as long as nothing else writes to it.
     */
    private fun contentStamp(aa: File): String {
        var count = 0L
        var bytes = 0L
        var newest = 0L
        for (f in aa.walkTopDown()) {
            if (!f.isFile || !f.name.endsWith(".bundle")) continue
            count++
            bytes += f.length()
            val m = f.lastModified()
            if (m > newest) newest = m
        }
        return "$count/$bytes/$newest"
    }

    /**
     * Builds the player image.
     *
     * [contentRoot] is where the Addressables content will be when the game
     * runs, which the catalog is repointed at. It has to be short: the catalog
     * stores its content root as a single length-prefixed string of 56 bytes,
     * so the real tree is reached through a symlink at that path rather than
     * named directly.
     */
    fun build(
        profile: GameProfile,
        unity: File,
        depot: File,
        context: android.content.Context,
        root: File,
        assets: android.content.res.AssetManager,
        contentRoot: String,
    ): Flow<Progress> = channelFlow {
        val data = depotData(depot)
            ?: throw IOException("no *_Data directory with globalgamemanagers under $depot")
        val converted = Il2cppConverter.dataDir(root)
        if (!File(converted, "Metadata/global-metadata.dat").isFile) {
            throw IOException("the conversion has not run: no global-metadata.dat")
        }
        val engineResources = File(unity, "android/Data/Resources/unity default resources")
        if (!engineResources.isFile) {
            throw IOException("the Android engine's built-in resources are missing: $engineResources")
        }

        send(Progress("Preparing the player image", -1f, "staging bundle-surgery"))
        val surgery = stageSurgery(root, assets)
        val img = imageDir(root)

        if (profile.contentLayout == ContentLayout.CLASSIC_PLAYER) {
            send(Progress("Converting classic player data", -1f, "validating exact source"))
            val classic = File(root, "classic-player-tree")
            val report = File(root, "classic-tree-report.json")
            val manifest = File(root, "classic-profile-manifest.json")
            val manifestAsset = "profiles/${profile.id}-${profile.currentGameVersion}.json"
            val manifestPart = File(root, "classic-profile-manifest.json.part")
            assets.open(manifestAsset).use { input ->
                manifestPart.outputStream().use { output -> input.copyTo(output) }
            }
            if (!manifestPart.renameTo(manifest)) {
                manifestPart.delete()
                throw IOException("could not stage $manifestAsset")
            }
            run(
                surgery,
                context,
                listOf(
                    "retarget-classic-tree",
                    data.absolutePath,
                    classic.absolutePath,
                    manifest.absolutePath,
                    report.absolutePath,
                ),
            )

            send(Progress("Assembling the player image", -1f, "copying classic data"))
            img.deleteRecursively()
            for (source in classic.walkTopDown().sortedBy { it.path }) {
                if (!source.isFile) continue
                val relative = source.relativeTo(classic).path.replace('\\', '/')
                if (!shouldCopyClassicRelativePath(relative)) continue
                val target = File(img, relative)
                target.parentFile?.mkdirs()
                source.copyTo(target, overwrite = true)
            }
            File(img, "Resources").mkdirs()
            File(img, "Managed/Metadata").mkdirs()
            File(img, "Managed/Resources").mkdirs()
            engineResources.copyTo(
                File(img, "Resources/unity default resources"),
                overwrite = true,
            )
            val builtin = File(data, "Resources/unity_builtin_extra")
            if (!builtin.isFile) throw IOException("classic source has no Resources/unity_builtin_extra")
            run(
                surgery,
                context,
                listOf(
                    "extract-vulkan-android",
                    builtin.absolutePath,
                    File(img, "Resources/unity_builtin_extra").absolutePath,
                ),
            )
            File(converted, "Metadata/global-metadata.dat").copyTo(
                File(img, "Managed/Metadata/global-metadata.dat"),
                overwrite = true,
            )
            for (resource in File(converted, "Resources").listFiles().orEmpty()) {
                if (resource.isFile && resource.name.endsWith(".dat")) {
                    resource.copyTo(
                        File(img, "Managed/Resources/${resource.name}"),
                        overwrite = true,
                    )
                }
            }
            setScriptingBackend(File(img, "boot.config"))
            registerPatches(profile, img, root)
            File(img, "unity_app_guid").writeText(
                guidFromMetadata(File(img, "Managed/Metadata/global-metadata.dat")),
            )
            catalogDir(root).deleteRecursively()
            if (!File(img, "globalgamemanagers").isFile) {
                throw IOException("classic converter produced no globalgamemanagers")
            }
            LauncherLog.log(
                "classic player image: " +
                    "${img.walkTopDown().filter { it.isFile }.sumOf { it.length() } / 1024 / 1024} MB",
            )
            send(Progress("Player image ready", 1f, "classic player"))
            return@channelFlow
        }

        require(profile.contentLayout == ContentLayout.ADDRESSABLES) {
            "Unsupported content layout for ${profile.id}: ${profile.contentLayout}"
        }
        img.deleteRecursively()
        File(img, "Resources").mkdirs()
        File(img, "Managed/Metadata").mkdirs()
        File(img, "Managed/Resources").mkdirs()

        // The three top-level .assets files carry the game's shaders, and each
        // shader holds a slice per graphics API. Reducing them to the Vulkan
        // slice and retargeting happens in one pass, straight from the depot,
        // so the depot is the only input this step has.
        val shaderFiles = listOf("globalgamemanagers.assets", "resources.assets", "sharedassets0.assets")
        for ((n, name) in shaderFiles.withIndex()) {
            coroutineContext.ensureActive()
            val src = File(data, name)
            if (!src.isFile) continue
            send(Progress("Retargeting shaders", (n.toFloat() / shaderFiles.size), name))
            run(surgery, context, listOf("extract-vulkan-android", src.absolutePath, File(img, name).absolutePath))
        }

        send(Progress("Assembling the player image", -1f, "copying"))
        for (name in listOf(
            "globalgamemanagers", "level0", "boot.config", "RuntimeInitializeOnLoads.json",
            "ScriptingAssemblies.json", "app.info",
            "globalgamemanagers.assets.resS", "resources.assets.resS",
        )) {
            val f = File(data, name)
            if (f.isFile) f.copyTo(File(img, name), overwrite = true)
        }
        File(data, "Resources/unity_builtin_extra").takeIf { it.isFile }
            ?.copyTo(File(img, "Resources/unity_builtin_extra"), overwrite = true)
        engineResources.copyTo(File(img, "Resources/unity default resources"), overwrite = true)

        File(converted, "Metadata/global-metadata.dat")
            .copyTo(File(img, "Managed/Metadata/global-metadata.dat"), overwrite = true)
        // The converter emits per-assembly resource blobs, which Unity puts
        // under Managed/Resources rather than Resources.
        for (f in File(converted, "Resources").listFiles().orEmpty()) {
            if (f.isFile && f.name.endsWith(".dat")) {
                f.copyTo(File(img, "Managed/Resources/${f.name}"), overwrite = true)
            }
        }

        send(Progress("Normalising the player image", -1f, "version and platform"))
        for (name in listOf(
            "globalgamemanagers", "level0", "globalgamemanagers.assets",
            "resources.assets", "sharedassets0.assets",
        )) {
            val f = File(img, name)
            if (!f.isFile) continue
            coroutineContext.ensureActive()
            run(surgery, context, listOf("set-unity-version", f.absolutePath, profile.unityVersion))
            retargetToAndroid(f)
        }
        File(img, "Resources/unity_builtin_extra").takeIf { it.isFile }?.let {
            run(surgery, context, listOf("set-unity-version", it.absolutePath, profile.unityVersion))
            retargetToAndroid(it)
        }

        // A built player keeps its resolved API list in BuildSettings, not in
        // the editor-only PlayerSettings table.
        val ggm = File(img, "globalgamemanagers")
        run(surgery, context, listOf("set-graphics-apis", ggm.absolutePath, GRAPHICS_APIS))
        run(surgery, context, listOf("set-build-version", ggm.absolutePath, profile.unityVersion))

        setScriptingBackend(File(img, "boot.config"))
        registerPatches(profile, img, root)

        File(img, "unity_app_guid").writeText(
            guidFromMetadata(File(img, "Managed/Metadata/global-metadata.dat")),
        )

        // The catalog travels with the image rather than with the content:
        // settings.json resolves it through Addressables.RuntimePath directly
        // rather than through the token, so it has to be somewhere the player
        // reads without any redirection. Only its content root is rewritten.
        val aaSrc = addressablesContainer(profile, data)
        val aaOut = catalogDir(root)
        aaOut.deleteRecursively()
        val catalog = File(aaSrc, "catalog.bin")
        if (catalog.isFile) {
            aaOut.mkdirs()
            catalog.copyTo(File(aaOut, "catalog.bin"), overwrite = true)
            File(aaSrc, "settings.json").takeIf { it.isFile }
                ?.copyTo(File(aaOut, "settings.json"), overwrite = true)
            send(Progress("Repointing the catalog", -1f, contentRoot))
            // Idempotent: the token is gone after the first pass, which is
            // what makes a re-run safe rather than an error.
            val patched = File(aaOut, "catalog.bin")
            run(
                surgery,
                context,
                listOf("patch-catalog-path", patched.absolutePath, patched.absolutePath, contentRoot),
            )
        } else {
            LauncherLog.log("no catalog.bin under $aaSrc -- Addressables content will not resolve")
        }

        LauncherLog.log("player image: ${img.walkTopDown().filter { it.isFile }.sumOf { it.length() } / 1024 / 1024} MB")
        send(Progress("Player image ready", 1f, ""))
    }.flowOn(Dispatchers.IO)

    /**
     * Puts the image where the engine looks for it.
     *
     * The engine does not use AssetManager to find its data: it builds
     * "jar:file://<package path>!/assets" and reads assets/bin/Data out of
     * that zip itself. GameActivity points the package path at
     * <files>/pkg/data.apk, so what has to exist is a ZIP -- loose files at
     * the same paths are not read at all, and the failure is Unity's
     * thoroughly misleading "Not enough storage space to install required
     * resources".
     *
     * data.apk is emphatically not a second package. It is never installed and
     * the package manager never sees it; it is a zip that happens to be laid
     * out like an APK, because that is the shape the engine opens. There is
     * one installed package and it stays that way.
     *
     * The content itself is not packed: it is several gigabytes and stays in
     * the depot, reached through a link at the short path the catalog was
     * repointed at.
     */
    fun install(root: File, pkgDir: File, paths: ProfileBuildPaths, depot: File) {
        val out = File(pkgDir, "data.apk")
        pkgDir.mkdirs()

        // Via a temporary name: this is 55 MB written on a phone, and a
        // truncated zip under the final name is something the engine would
        // try to open.
        val tmp = File(pkgDir, "data.apk.part")
        tmp.delete()
        var entries = 0
        ZipArchiveOutputStream(BufferedOutputStream(tmp.outputStream(), 1 shl 16)).use { zip ->
            zip.setUseZip64(Zip64Mode.AsNeeded)
            // Stored, not deflated. The engine memory-maps what it reads out
            // of here, and the payload is already-compressed asset data that
            // deflate would only make slower to load. Matches what build.sh
            // does with jar --no-compress.
            zip.setMethod(ZipArchiveOutputStream.STORED)
            entries += addTree(zip, imageDir(root), "assets/bin/Data")
            if (catalogDir(root).isDirectory) {
                entries += addTree(zip, catalogDir(root), "assets/aa")
            }
        }
        try {
            Files.move(tmp.toPath(), out.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (e: AtomicMoveNotSupportedException) {
            tmp.delete()
            throw IOException("could not atomically write $out", e)
        } catch (e: IOException) {
            tmp.delete()
            throw IOException("could not write $out", e)
        }

        // Anything an older layout left behind. Loose assets here are not
        // merely useless now, they are confusing: they look like the data is
        // in place while the engine reads none of it.
        File(pkgDir, "assets").deleteRecursively()

        // The catalog's content root is a fixed 56-byte field, which fits an
        // internal path and not the depot's real one -- the game's directory
        // name alone is most of the budget. So the short path is a link.
        if (paths.profile.contentLayout == ContentLayout.ADDRESSABLES) {
            linkContent(paths, depot)
        }
        LauncherLog.log("packed $entries file(s) into $out (${out.length()} bytes)")
    }

    /**
     * Links the short catalog path at the depot's content.
     *
     * Separate from the packing above because it is nearly free and lives in
     * internal storage, which can be cleared without anything about the image
     * changing. Skipping the pack must not also skip this, or the game comes
     * up with no content and a catalog pointing at nothing.
     */
    fun linkContent(filesDir: File, depot: File) {
        depotData(depot)?.let { data ->
            val content = File(data, "StreamingAssets/aa")
            if (content.isDirectory) {
                ToolchainFetcher.symlink(content.absolutePath, File(filesDir, "aa"))
            }
        }
    }

    fun linkContent(paths: ProfileBuildPaths, depot: File) {
        depotData(depot)?.let { data ->
            val content = addressablesContainer(paths.profile, data)
            if (content.isDirectory) {
                paths.contentLink.parentFile?.mkdirs()
                ToolchainFetcher.symlink(content.absolutePath, paths.contentLink)
            }
        }
    }

    /**
     * Adds a directory tree to a zip, stored.
     *
     * A stored entry carries its own size and CRC in the local header, which
     * the stream cannot work out for itself, so both are computed here before
     * the bytes are written.
     */
    private fun addTree(zip: ZipArchiveOutputStream, dir: File, prefix: String): Int {
        var n = 0
        for (f in dir.walkTopDown().sortedBy { it.path }) {
            if (!f.isFile) continue
            val rel = f.relativeTo(dir).path.replace('\\', '/')
            val entry = ZipArchiveEntry("$prefix/$rel")
            entry.method = ZipArchiveOutputStream.STORED
            entry.size = f.length()
            entry.compressedSize = f.length()
            entry.crc = crcOf(f)
            zip.putArchiveEntry(entry)
            f.inputStream().use { it.copyTo(zip, 1 shl 16) }
            zip.closeArchiveEntry()
            n++
        }
        return n
    }

    private fun crcOf(f: File): Long {
        val crc = CRC32()
        f.inputStream().use { input ->
            val buf = ByteArray(1 shl 16)
            while (true) {
                val read = input.read(buf)
                if (read < 0) break
                crc.update(buf, 0, read)
            }
        }
        return crc.value
    }

    fun shouldCopyClassicRelativePath(relativePath: String): Boolean {
        val normalized = relativePath.replace('\\', '/').trimStart('/')
        val lower = normalized.lowercase(java.util.Locale.ROOT)
        if (lower.endsWith(".part")) return false
        if (lower == "managed" || lower.startsWith("managed/")) return false
        if (lower == "monobleedingedge" || lower.startsWith("monobleedingedge/")) return false
        if (lower == "resources/unity default resources") return false
        if (lower == "resources/unity_builtin_extra") return false
        return normalized.isNotEmpty() && normalized.split('/').none { it == ".." }
    }

    /**
     * Retargets the whole Addressables content tree, in place.
     *
     * In place because there is no room on a phone for a second copy of a
     * multi-gigabyte content set, and because the operation is idempotent --
     * an interrupted run is resumed by running it again. bundle-surgery's
     * retarget-tree does the parallelism and the resume itself.
     */
    fun retargetContent(
        profile: GameProfile,
        depot: File,
        context: android.content.Context,
        root: File,
        assets: android.content.res.AssetManager,
    ): Flow<Progress> = channelFlow {
        val data = depotData(depot) ?: throw IOException("no player data under $depot")
        val aa = addressablesContainer(profile, data)
        if (!aa.isDirectory) throw IOException("no Addressables content at $aa")

        // Said before the first walk rather than after it.
        //
        // Everything between here and the first bundle -- the stamp, staging
        // the tool, counting what there is to do -- is three passes over a
        // couple of thousand files on the slowest storage in the device, and
        // none of it used to say anything at all. Until the first bundle was
        // reported the screen still held whatever the previous step had put
        // there, which on a Retroid Pocket Flip 2 meant sitting on "packing
        // the player image" for minutes after the packing had finished. A
        // step that is working and a step that has died looked the same.
        send(Progress("Retargeting content", -1f, "checking what is already done"))

        // Nothing a patch edit does can reach the content tree, and this step
        // is minutes of opening bundles to conclude exactly that.
        if (contentStampFile(root).takeIf { it.isFile }?.readText() == contentStamp(aa)) {
            LauncherLog.log("content is already retargeted; skipping")
            send(Progress("Content ready", 1f, "already retargeted"))
            return@channelFlow
        }

        send(Progress("Retargeting content", -1f, "preparing the tool"))
        val surgery = stageSurgery(root, assets)

        send(Progress("Retargeting content", -1f, "counting bundles"))
        // A shipped Addressables tree groups content into subdirectories, and
        // in this game most of the scenes live below the top level.
        val groups = aa.listFiles().orEmpty()
            .filter { it.isDirectory && it.name != "AddressablesLink" }
            .sortedBy { it.name }
        val counts = groups.associateWith { g ->
            g.walkTopDown().count { it.isFile && it.name.endsWith(".bundle") }
        }
        val total = counts.values.sum()
        if (total == 0) throw IOException("no bundles to retarget under $aa")
        var finished = 0
        // The real total, as soon as it is known, so the bar starts from a
        // number rather than from the first bundle a hundred bundles later.
        send(Progress("Retargeting content", 0f, "0 of $total bundles"))

        // What has actually been rewritten, counted off the tree.
        //
        // The reader below was meant to be this, and cannot be: retarget-tree
        // prints nothing this side of the runtime -- the per-group line in the
        // log came back empty on a Retroid Pocket Flip 2, after a run that
        // rewrote all 2068 bundles perfectly well. Whatever it says goes
        // nowhere we can see, and with one group holding the whole tree the
        // bar could then only move once, at the end. It read 0% for seven
        // minutes of a working retarget.
        //
        // The files answer without being asked. A bundle rewritten after this
        // started has an mtime to prove it, and that is true whatever the tool
        // does or does not print. Both paths report through [publish], so the
        // reader still wins if it ever has something to say.
        val startedAt = System.currentTimeMillis()
        val seen = java.util.concurrent.atomic.AtomicInteger(0)
        fun publish(n: Int) {
            if (n <= 0 || n < seen.get()) return
            seen.set(n)
            // Never quite full: the step ends when the tool returns, not when
            // the last file lands, and the two are not the same moment.
            trySend(
                Progress(
                    "Retargeting content",
                    (n.toFloat() / total).coerceAtMost(0.99f),
                    "$n of $total bundles",
                ),
            )
        }

        val ticker = launch(Dispatchers.IO) {
            while (isActive) {
                delay(RETARGET_POLL_MS)
                publish(
                    aa.walkTopDown().count {
                        it.isFile && it.name.endsWith(".bundle") && it.lastModified() >= startedAt
                    },
                )
            }
        }

        try {
            for (group in groups) {
                coroutineContext.ensureActive()
                val count = counts.getValue(group)
                if (count == 0) continue
                val before = finished
                // What retarget-tree is documented to print: "  N / M  (Ts)"
                // every hundred bundles, which exec folds in with stdout. It
                // has never been seen to arrive; the count above is what
                // actually moves the bar, and this is kept only because it
                // costs nothing and would be the better answer if it came.
                val r = run(surgery, context, listOf("retarget-tree", group.absolutePath, group.absolutePath)) { line ->
                    PROGRESS.find(line)?.let { m ->
                        val n = m.groupValues[1].toIntOrNull() ?: return@let
                        publish(before + n)
                    }
                }
                finished += count
                LauncherLog.log("retargeted ${group.name}: ${r.output.trim().lines().lastOrNull()}")
                publish(finished)
            }
        } finally {
            ticker.cancel()
        }
        // Recomputed rather than reused: the run just rewrote these files, so
        // the stamp that identifies "already retargeted" is the state they are
        // in now, not the state they were in when the run started.
        contentStampFile(root).writeText(contentStamp(aa))
        send(Progress("Content ready", 1f, "$total bundles"))
    }.flowOn(Dispatchers.IO)

    private val PROGRESS = Regex("""^\s*(\d+)\s*/\s*(\d+)\s""")

    /**
     * How often the bundle tree is counted while the retarget runs.
     *
     * A pass over a couple of thousand files, so not free -- and it competes
     * with the work it is measuring, on the same storage. Slow enough not to
     * matter, often enough that a stalled step is obvious within a screenful
     * of waiting.
     */
    private const val RETARGET_POLL_MS = 5_000L

    // ── the depot ──────────────────────────────────────────────────────────

    /**
     * The depot's player data directory.
     *
     * Found rather than named: the directory carries the game's display name,
     * which is not ours to hardcode and has a space in it.
     *
     * Searched a few levels down rather than only in the depot's top level.
     * A hand-copied depot is a folder someone dragged across from a PC, and it
     * arrives wrapped in whatever their downloader or extractor called it --
     * silksong/, depot_1030303/, the game's own install folder. Every one of
     * those has the right layout inside it, and every one of them used to be
     * reported as "no game files". Nothing downstream wants the depot root:
     * each step resolves off this directory, so what sits above it is free.
     *
     * The search is by level rather than depth-first so that the shallowest
     * copy wins, and it stops at the level where it finds something -- the
     * data directory's own subtree (StreamingAssets/aa, thousands of bundles)
     * is never listed, which matters because this runs on the main thread
     * every time the setup screen is shown.
     */
    fun depotData(depot: File): File? {
        var level = childDirs(depot)
        var depth = 0
        while (true) {
            dataDirIn(level)?.let { return it }
            if (++depth >= DEPOT_SEARCH_DEPTH || level.isEmpty()) break
            level = level.flatMap(::childDirs)
        }
        // Last, so that a stray globalgamemanagers loose in the depot root --
        // someone who copied the inside of the data directory rather than the
        // directory -- is still accepted, but never beats a real one below.
        return depot.takeIf { File(it, "globalgamemanagers").isFile }
    }

    /** How far below the depot root [depotData] will look. */
    private const val DEPOT_SEARCH_DEPTH = 3

    /** What the game's data directory is called, before the game's name. */
    private const val DATA_SUFFIX = "_Data"

    /**
     * The data directory among [dirs], if one of them is.
     *
     * globalgamemanagers is the test, because that is the file every later
     * step actually needs. The name is only a tie-breaker: it decides between
     * two candidates at the same level, so a real download is not passed over
     * for whatever else happens to be sitting beside it.
     */
    private fun dataDirIn(dirs: List<File>): File? {
        val hits = dirs.filter { File(it, "globalgamemanagers").isFile }
        return hits.firstOrNull { it.name.endsWith(DATA_SUFFIX) } ?: hits.firstOrNull()
    }

    private fun childDirs(dir: File): List<File> =
        dir.listFiles().orEmpty().filter { it.isDirectory }.sortedBy { it.name }

    /**
     * A desktop build of Silksong that is not the one this port is made from.
     *
     * The port is built around the LINUX depot, 1030303, and only that one:
     * the retarget reads the Linux player's shaders for their Vulkan slice,
     * and the IL2CPP converter is fed the Linux player's assemblies. See
     * [DepotFetcher.DEPOT_ID].
     *
     * Nothing used to check. Every desktop build has a "<game>_Data" folder
     * with a globalgamemanagers in it, so a Windows or macOS copy passed for
     * the real thing, and the build ran for four minutes and then died inside
     * il2cpp -- which reported the exit code and not the reason, so the report
     * that came back said only "il2cpp failed after 5s: exit 120".
     *
     * The mechanism is worth stating, because it is not obvious that a wrong
     * platform should matter at all when the IL is the same IL. It is the
     * class library. The converter composes its assembly set as unityaot BCL,
     * then the Android player's engine assemblies, then whatever the depot has
     * that neither of those already provides -- and the Windows player ships
     * class library assemblies the unityaot profile has no copy of, so they
     * are not shadowed by name and they land in the set. il2cpp is then
     * handed two class libraries from two profiles at once. That is the
     * failure the header of Il2cppConverter describes: it does not produce a
     * message about profiles, it dies. The count is the tell -- the Linux
     * depot makes a set of 186 assemblies, and the report that provoked this
     * had 200.
     */
    enum class ForeignBuild(val depot: Int, val label: String) {
        WINDOWS(1030301, "Windows"),
        MACOS(1030302, "macOS"),
    }

    /**
     * Which other platform's build this is, or null for the Linux one.
     *
     * By the player library, because it is named for its platform, it sits in
     * a fixed place beside the data directory, and no build carries another's.
     * The macOS layout also gets its bundle recognised: the data directory
     * there is "Contents/Resources/Data", a level deeper than [depotData]
     * looks and not named "_Data" either, so a macOS copy would otherwise
     * reach the screen as "no game data in it" -- true, unhelpful, and the
     * kind of answer that has someone re-copying eight gigabytes.
     *
     * Finding the Linux player is an answer too, and it comes first because
     * it is the common case: the right depot ends this at its own top level
     * without listing anything else.
     *
     * The walk matches [depotData]'s -- the same levels, for the same reason
     * -- except that the bulk directories are pruned. Nothing here needs to
     * look inside a data directory, and StreamingAssets/aa is thousands of
     * files that this must never enumerate: it runs on the main thread every
     * time the setup screen is drawn.
     */
    fun foreignBuild(depot: File): ForeignBuild? {
        if (!depot.isDirectory) return null
        var level = listOf(depot)
        var depth = 0
        while (level.isNotEmpty() && depth <= DEPOT_SEARCH_DEPTH) {
            for (dir in level) {
                val names = dir.list().orEmpty()
                if (names.any { it.equals("UnityPlayer.so", ignoreCase = true) }) return null
                if (names.any { it.equals("UnityPlayer.dll", ignoreCase = true) }) {
                    return ForeignBuild.WINDOWS
                }
                if (names.any { it.equals("UnityPlayer.dylib", ignoreCase = true) }) {
                    return ForeignBuild.MACOS
                }
                if (names.any { it.endsWith(".app", ignoreCase = true) && File(dir, it).isDirectory }) {
                    return ForeignBuild.MACOS
                }
            }
            level = level.flatMap(::childDirs).filterNot { isBulkDir(it.name) }
            depth++
        }
        return null
    }

    /** A directory whose contents are the game's, and are never worth listing. */
    private fun isBulkDir(name: String): Boolean =
        name.endsWith(DATA_SUFFIX) || name == "Data" || name == "StreamingAssets"

    /**
     * What to tell someone holding the wrong build, or null when they are not.
     *
     * Says the platform rather than only the depot number, because the number
     * is what they typed and the platform is what they got wrong.
     */
    fun wrongBuildProblem(depot: File): String? = foreignBuild(depot)?.let {
        "that is the ${it.label} version of Silksong (depot ${it.depot}). " +
            "This port is built from the Linux version, depot ${DepotFetcher.DEPOT_ID}"
    }

    /**
     * Why [depotData] found nothing, in terms of what is actually on the disk.
     *
     * Someone who has copied eight gigabytes to the wrong place is owed more
     * than "not found": the answer is always visible in the directory, and the
     * app is the only thing standing in it. Written for both the screen and
     * the log, because the report that brings this to us is second-hand.
     */
    fun depotProblem(depot: File): String {
        if (!depot.isDirectory) return "that folder does not exist yet"
        val entries = depot.listFiles().orEmpty().sortedBy { it.name }
        if (entries.isEmpty()) return "that folder is empty"

        // Before anything about what is missing: this folder holds a complete
        // and correct copy of the game, and naming what is absent from it
        // would send the reader looking for a copy that went wrong.
        wrongBuildProblem(depot)?.let { return it }

        // A data directory that is there but unusable is its own answer, and a
        // much more likely one than a wrong path: an interrupted copy, or a
        // file manager that gave up on the extensionless files.
        partialDataDir(depot)?.let {
            return "\"${it.name}\" is there, but globalgamemanagers is missing from it -- " +
                "the copy did not finish"
        }
        val names = entries.map { if (it.isDirectory) "${it.name}/" else it.name }
        return "found there instead: " + names.take(6).joinToString(", ") +
            if (names.size > 6) ", and ${names.size - 6} more" else ""
    }

    /**
     * Why [depotData] found nothing, in one phrase and without naming
     * anything the user did not choose to show us.
     *
     * [depotProblem] is written for the screen, where listing the directory is
     * the whole point -- the person standing in front of it needs to see what
     * the app sees. The log is a different audience: it gets copied into bug
     * reports and sent to strangers, and an inventory of someone's Download
     * folder has no business travelling with it. Same answer, no contents.
     */
    fun depotProblemSummary(depot: File): String = when {
        !depot.isDirectory -> "that folder does not exist yet"
        depot.listFiles().orEmpty().isEmpty() -> "that folder is empty"
        foreignBuild(depot) != null -> "that is the ${foreignBuild(depot)?.label} build, not the Linux one"
        partialDataDir(depot) != null -> "a data folder is there, but the copy did not finish"
        else -> "no game data in it"
    }

    /** A directory named like the game's data, but without the file that counts. */
    private fun partialDataDir(depot: File): File? {
        var level = childDirs(depot)
        var depth = 0
        while (level.isNotEmpty() && depth < DEPOT_SEARCH_DEPTH) {
            level.firstOrNull { it.name.endsWith(DATA_SUFFIX) }?.let { return it }
            level = level.flatMap(::childDirs)
            depth++
        }
        return null
    }

    // ── byte-level edits ───────────────────────────────────────────────────

    /**
     * Stamps a SerializedFile's target platform as Android.
     *
     * TargetPlatform is a fixed-width int immediately after the
     * NUL-terminated version string, so it can be poked in place -- but only
     * after any version rewrite, which changes that string's length, has
     * already happened.
     */
    private fun retargetToAndroid(f: File) {
        val b = f.readBytes()
        var i = 48
        while (i < b.size && b[i].toInt() != 0) i++
        if (i + 4 >= b.size) throw IOException("${f.name}: no version string to retarget after")
        val at = i + 1
        // 13 = Android, little endian.
        b[at] = 13; b[at + 1] = 0; b[at + 2] = 0; b[at + 3] = 0
        f.writeBytes(b)
    }

    /** The Android IL2CPP runtime reads boot.config to pick its init path. */
    private fun setScriptingBackend(boot: File) {
        if (!boot.isFile) return
        val kept = boot.readLines().filterNot { it.startsWith("scripting-backend=") }
        boot.writeText((kept + "scripting-backend=il2cpp").joinToString("\n") + "\n")
    }

    /**
     * Tells the player about our assembly, and what to call in it.
     *
     * Two files, and both are required. ScriptingAssemblies.json is the list
     * the player will load at all -- an assembly missing from it is compiled
     * into libil2cpp.so and never touched. RuntimeInitializeOnLoads.json is
     * what Unity actually calls at startup: the [RuntimeInitializeOnLoadMethod]
     * attribute does nothing by itself in a player, because it is the EDITOR
     * that scans for it at build time and writes the result here. There is no
     * editor in this pipeline, so the list is shipped beside the sources and
     * appended.
     *
     * Both edits are idempotent -- an entry already present is not added twice
     * -- because this runs again on every rebuild.
     */
    internal fun registerPatches(profile: GameProfile, img: File, root: File) {
        // Everything managed that the depot did not ship: the port's own
        // patches, the BepInEx shims, and whatever the weaver staged out of
        // the mods folder. An assembly missing from this list is compiled into
        // libil2cpp.so and then never loaded, which looks exactly like a mod
        // that does nothing.
        val staged = Il2cppConverter.asmDir(root)
        val patchAssembly = PackageCompiler.patchAssembly(profile, root)
        val patchAssemblyName = patchAssembly.name.removeSuffix(".dll")
        val names = buildList {
            add(patchAssembly.name)
            for (shim in PackageCompiler.shimAssemblies(root)) add(shim.name)
            for (plugin in Mods.stagedAssemblies(root)) add("$plugin.dll")
        }.distinct().filter { File(staged, it).isFile }

        if (names.isEmpty()) {
            LauncherLog.log("no assemblies of ours to register")
            return
        }

        // ScriptingAssemblies.json: parallel arrays of names and types, and
        // they have to stay the same length. 16 is what the depot's own
        // non-Unity assemblies carry.
        val listFile = File(img, "ScriptingAssemblies.json")
        if (listFile.isFile) {
            val json = org.json.JSONObject(listFile.readText())
            val list = json.getJSONArray("names")
            var added = 0
            for (name in names) {
                val already = (0 until list.length()).any { list.getString(it) == name }
                if (already) continue
                list.put(name)
                json.optJSONArray("types")?.put(16)
                added++
            }
            if (added > 0) {
                listFile.writeText(json.toString())
                LauncherLog.log("registered $added assembly/assemblies in ScriptingAssemblies.json")
            }
        }

        // RuntimeInitializeOnLoads.json: one row per entry point.
        val loadsFile = File(img, "RuntimeInitializeOnLoads.json")
        if (!loadsFile.isFile) return
        val loads = org.json.JSONObject(loadsFile.readText())
        val rows = loads.getJSONArray("root")

        var addedCount = 0

        fun register(assemblyName: String, nameSpace: String, className: String, methodName: String, loadTypes: Int) {
            val exists = (0 until rows.length()).any {
                val r = rows.getJSONObject(it)
                r.optString("assemblyName") == assemblyName &&
                    r.optString("className") == className &&
                    r.optString("methodName") == methodName
            }
            if (exists) return
            rows.put(
                org.json.JSONObject()
                    .put("assemblyName", assemblyName)
                    // The ported patches sit in the global namespace, as the
                    // game's own classes do; only the newer ones are scoped.
                    .put("nameSpace", nameSpace)
                    .put("className", className)
                    .put("methodName", methodName)
                    .put("loadTypes", loadTypes)
                    .put("isUnityClass", false),
            )
            addedCount++
        }

        PackageCompiler.entryPoints(root)?.let { text ->
            val entries = org.json.JSONObject(text).getJSONArray("entryPoints")
            for (i in 0 until entries.length()) {
                val e = entries.getJSONObject(i)
                register(
                    patchAssemblyName,
                    e.optString("nameSpace", ""),
                    e.getString("className"),
                    e.getString("methodName"),
                    e.getInt("loadTypes"),
                )
            }
        }

        // The chainloader. Registered only when there is a BepInEx to call
        // into, and after assemblies are loaded (2) because it finds plugins
        // by reflecting over them.
        if (names.contains("BepInEx.dll")) {
            register("BepInEx", "BepInEx.Bootstrap", "Chainloader", "Start", 2)
        }

        if (addedCount > 0) {
            loadsFile.writeText(loads.toString())
            LauncherLog.log("registered $addedCount entry point(s)")
        }
    }

    /** A UUID derived from the metadata, so it changes when the metadata does. */
    private fun guidFromMetadata(metadata: File): String {
        val sha = MessageDigest.getInstance("SHA-256")
        metadata.inputStream().use { input ->
            val buf = ByteArray(1 shl 20)
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                sha.update(buf, 0, n)
            }
        }
        val seed = sha.digest().joinToString("") { "%02x".format(it) }
        val h = MessageDigest.getInstance("MD5").digest(seed.toByteArray())
            .joinToString("") { "%02x".format(it) }
        return "${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-" +
            "${h.substring(16, 20)}-${h.substring(20, 32)}"
    }

    // ── bundle-surgery ─────────────────────────────────────────────────────

    /**
     * Unpacks bundle-surgery out of the APK.
     *
     * On external storage beside the build: it is read by .NET, not executed,
     * and keeping it next to what it operates on means one place to clear.
     */
    internal fun stageSurgery(root: File, assets: android.content.res.AssetManager): File {
        val dir = File(root, "bundle-surgery")
        dir.mkdirs()
        for (name in assets.list(SURGERY_ASSET_DIR).orEmpty()) {
            val out = File(dir, name)
            assets.open("$SURGERY_ASSET_DIR/$name").use { input ->
                out.outputStream().use { o -> input.copyTo(o) }
            }
        }
        val dll = File(dir, SURGERY_DLL)
        if (!dll.isFile) throw IOException("bundle-surgery is not in the APK")
        return dll
    }

    internal suspend fun run(
        surgery: File,
        context: android.content.Context,
        args: List<String>,
        onLine: (String) -> Unit = {},
    ): Toolchain.Result {
        val r = tryRun(surgery, context, args, onLine)
        if (!r.ok) {
            throw IOException(
                "bundle-surgery ${args.firstOrNull()} failed: " +
                    (r.output.trim().lines().lastOrNull() ?: "exit ${r.code}").take(300),
            )
        }
        return r
    }

    private suspend fun tryRun(
        surgery: File,
        context: android.content.Context,
        args: List<String>,
        onLine: (String) -> Unit = {},
    ): Toolchain.Result =
        MonoRuntime.exec(
            context,
            surgery,
            args,
            cwd = surgery.parentFile,
            onLine = onLine,
        )
}
