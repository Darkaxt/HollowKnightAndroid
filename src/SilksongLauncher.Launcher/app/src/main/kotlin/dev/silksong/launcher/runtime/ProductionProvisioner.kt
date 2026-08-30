package dev.silksong.launcher.runtime

import android.content.Context
import dev.silksong.launcher.DepotFetcher
import dev.silksong.launcher.DepotLocation
import dev.silksong.launcher.Il2cppConverter
import dev.silksong.launcher.LauncherLog
import dev.silksong.launcher.MonoRuntime
import dev.silksong.launcher.NativeBuild
import dev.silksong.launcher.PackageCompiler
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.ToolchainFetcher
import dev.silksong.launcher.UnityDex
import dev.silksong.launcher.UnityFetcher
import java.io.File
import java.security.MessageDigest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.withContext

class ProductionProvisioner {
    suspend fun provision(
        request: ProvisionRequest,
        emit: suspend (RuntimeProgress) -> Unit,
    ): RuntimeState {
        val runtime = request.runtime
        val context = runtime.context
        val profile = runtime.profile
        val paths = runtime.paths
        val source = request.source
        require(source !is ProvisionSource.Synthetic) {
            "The production runtime cannot provision synthetic content"
        }

        val depot: File
        val staging: File
        val credentials = when (source) {
            is ProvisionSource.Local -> {
                depot = source.depot
                staging = requireNotNull(paths.depotStaging) { "No depot staging directory" }
                null
            }
            is ProvisionSource.Steam -> {
                depot = source.depot
                staging = source.staging
                source.credentials
            }
            ProvisionSource.Synthetic -> error("checked above")
        }

        val unity = UnityFetcher.rootFor(
            requireNotNull(context.getExternalFilesDir(null)) { "No external files directory" },
            profile,
        )
        val tools = ToolchainFetcher.rootFor(context)
        val out = paths.buildRoot
        val packageDir = paths.packageDir
        val engineDir = File(packageDir, "lib/arm64")

        if (credentials != null && !DepotFetcher.isPresent(profile, depot)) {
            emit(RuntimeProgress("game", -1f, "Downloading game files from Steam"))
            DepotFetcher.download(profile, credentials, depot, staging).collect { event ->
                when (event) {
                    is DepotFetcher.Event.Progress -> emit(
                        RuntimeProgress("game", event.fraction, "${event.bytes / 1024 / 1024} MB"),
                    )
                    is DepotFetcher.Event.Status -> emit(RuntimeProgress("game", -1f, event.message))
                    DepotFetcher.Event.Done -> Unit
                }
            }
        }

        emit(RuntimeProgress("tools", -1f, "Downloading supporting tools"))
        if (!UnityFetcher.isPresent(profile, unity)) {
            UnityFetcher.fetch(profile, unity, paths.installStaging).collect {
                emit(RuntimeProgress("tools", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
            }
        }
        if (!UnityDex.isBuilt(context, unity)) {
            emit(RuntimeProgress("tools", -1f, "Preparing the engine"))
            withContext(Dispatchers.IO) { UnityDex.build(context, unity) }
        }
        paths.installStaging?.let { installStaging ->
            withContext(Dispatchers.IO) {
                UnityFetcher.ensureEngineStaged(profile, unity, installStaging)
                installStaged(installStaging, packageDir, engineDir)
            }
        }
        if (!ToolchainFetcher.isPresent(tools)) {
            ToolchainFetcher.fetch(tools).collect {
                emit(RuntimeProgress("tools", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
            }
        }
        MonoRuntime.stage(context).collect {
            emit(RuntimeProgress("tools", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
        }

        if (PlayerImage.depotData(depot) == null) {
            throw java.io.IOException("the game's files are not on this device")
        }
        PlayerImage.wrongBuildProblem(depot)?.let { throw java.io.IOException(it) }

        emit(RuntimeProgress("build", -1f, "Building ${profile.displayName}"))
        if (!PackageCompiler.isPresent(out)) {
            PackageCompiler.compile(unity, depot, context, out).collect {
                emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
            }
        }
        PackageCompiler.compileIo(unity, depot, context, out, context.assets).collect {
            emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
        }
        PackageCompiler.compilePatches(unity, depot, context, out, context.assets).collect {
            emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
        }
        if (!Il2cppConverter.isPresent(out) || Il2cppConverter.isStale(out)) {
            Il2cppConverter.convert(unity, depot, context, out).collect {
                emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
            }
        }
        NativeBuild.build(unity, tools, out, context.assets, install = engineDir).collect {
            emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
        }

        if (PlayerImage.isCurrent(profile, out, packageDir, depot)) {
            LauncherLog.log("player image is current; not rebuilding or repacking")
        } else {
            PlayerImage.build(
                profile,
                unity,
                depot,
                context,
                out,
                context.assets,
                PlayerImage.contentRootFor(context.packageName, paths),
            ).collect {
                emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
            }
            withContext(Dispatchers.IO) {
                PlayerImage.install(out, packageDir, paths, depot)
                PlayerImage.markCurrent(profile, out, depot)
            }
        }

        withContext(Dispatchers.IO) { DepotLocation.relink(paths, depot) }
        PlayerImage.retargetContent(profile, depot, context, out, context.assets).collect {
            emit(RuntimeProgress("build", it.fraction, listOf(it.step, it.detail).filter(String::isNotEmpty).joinToString(" · ")))
        }
        withContext(Dispatchers.IO) {
            File(packageDir, ".built").writeText(ProductionBuildSignature.compute(context))
        }
        return RuntimeState(true, null, "${profile.displayName} is ready")
    }

    private fun installStaged(staging: File, packageDir: File, engineDir: File) {
        for (source in staging.listFiles().orEmpty()) {
            val destination = when {
                source.name.endsWith(".so") -> File(engineDir, source.name)
                source.name == "data.apk" -> File(packageDir, source.name)
                else -> continue
            }
            requireNotNull(destination.parentFile).mkdirs()
            val part = File(destination.parentFile, "${destination.name}.part")
            source.inputStream().use { input ->
                part.outputStream().use { output -> input.copyTo(output, 1 shl 20) }
            }
            if (!part.renameTo(destination)) {
                part.delete()
                throw java.io.IOException("rename to $destination")
            }
            if (source.name.endsWith(".so")) destination.setExecutable(true, true)
            check(source.delete()) { "Could not remove installed staging file: $source" }
            LauncherLog.log("Installed ${destination.name} (${destination.length()} bytes)")
        }
    }
}

object ProductionBuildSignature {
    fun compute(context: Context): String {
        val digest = MessageDigest.getInstance("SHA-256")
        fun walk(path: String) {
            val names = runCatching { context.assets.list(path) }.getOrNull().orEmpty().sorted()
            if (names.isEmpty()) {
                runCatching { context.assets.open(path).use { digest.update(it.readBytes()) } }
                return
            }
            for (name in names) walk("$path/$name")
        }
        walk("ondevice")
        return "1|" + digest.digest().joinToString("") { "%02x".format(it) }.take(16)
    }
}
