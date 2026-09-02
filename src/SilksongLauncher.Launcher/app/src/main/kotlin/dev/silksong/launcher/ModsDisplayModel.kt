package dev.silksong.launcher

import android.content.res.AssetManager
import dev.silksong.launcher.build.GenerationPublisher
import java.io.File
import java.io.InputStream
import java.security.MessageDigest
import java.util.concurrent.CancellationException

/** Builds the complete mods-screen snapshot off the main thread. */
internal class ModsDisplayCoordinator(private val source: Source) {

    internal interface Source {
        fun plugins(): List<File>
        fun relativePath(file: File): String
        fun open(file: File): InputStream
        fun appendStampAssets(digest: MessageDigest)
        fun disabled(): Set<String>
        fun published(): Published?
    }

    internal data class Published(
        val stamp: String,
        val built: Map<String, String>,
        val reports: Map<String, Mods.Plugin>,
    )

    internal data class Row(
        val relative: String,
        val report: Mods.Plugin?,
        val enabled: Boolean,
        val built: Boolean,
    )

    internal data class Model(val rows: List<Row>, val stale: Boolean)

    internal sealed interface Result {
        data class Ready(val model: Model) : Result
        data class Failed(val message: String, val failure: Throwable) : Result
    }

    fun load(): Result = try {
        val aggregate = MessageDigest.getInstance("SHA-256")
        val plugins = source.plugins()
        val digests = LinkedHashMap<String, Pair<File, String>>(plugins.size)
        for (file in plugins) {
            val relative = source.relativePath(file)
            aggregate.update(relative.toByteArray())
            val fileDigest = MessageDigest.getInstance("SHA-256")
            source.open(file).use { input -> updateBoth(input, fileDigest, aggregate) }
            digests[relative] = file to fileDigest.hex()
        }
        if (plugins.isNotEmpty()) source.appendStampAssets(aggregate)

        val disabled = source.disabled()
        val published = source.published()
        val stale = published == null || published.stamp != aggregate.hex()
        val rows = digests.map { (relative, value) ->
            val (file, digest) = value
            Row(
                relative = relative,
                report = published?.reports?.get(file.name),
                enabled = relative !in disabled,
                built = when {
                    published == null -> false
                    published.built.isEmpty() -> !stale
                    else -> published.built[relative] == digest
                },
            )
        }
        Result.Ready(Model(rows.toList(), stale))
    } catch (cancelled: CancellationException) {
        throw cancelled
    } catch (failure: Throwable) {
        Result.Failed(
            message = failure.message?.takeIf(String::isNotBlank) ?: failure.toString(),
            failure = failure,
        )
    }

    private fun updateBoth(input: InputStream, first: MessageDigest, second: MessageDigest) {
        val buffer = ByteArray(1 shl 16)
        while (true) {
            val count = input.read(buffer)
            if (count < 0) return
            if (count == 0) {
                val one = input.read()
                if (one < 0) return
                first.update(one.toByte())
                second.update(one.toByte())
            } else {
                first.update(buffer, 0, count)
                second.update(buffer, 0, count)
            }
        }
    }

    private fun MessageDigest.hex(): String =
        digest().joinToString("") { "%02x".format(it) }
}

/** File-backed authority adapter. Every method is invoked at most once per refresh. */
internal class ProductionModsDisplaySource(
    private val mods: File,
    private val state: File,
    private val publisher: GenerationPublisher,
    private val assets: AssetManager,
) : ModsDisplayCoordinator.Source {
    override fun plugins(): List<File> = Mods.all(mods)

    override fun relativePath(file: File): String = Mods.relativePath(mods, file)

    override fun open(file: File): InputStream = file.inputStream()

    override fun appendStampAssets(digest: MessageDigest) = Mods.appendStampAssets(digest, assets)

    override fun disabled(): Set<String> = Mods.disabled(state)

    override fun published(): ModsDisplayCoordinator.Published? {
        val generation = publisher.current() ?: return null
        val root = Mods.publishedMetadataRoot(generation)
        return ModsDisplayCoordinator.Published(
            stamp = Mods.recordedStamp(root),
            built = Mods.builtStrict(root),
            reports = Mods.lastReportStrict(root).associateBy { it.file },
        )
    }
}
