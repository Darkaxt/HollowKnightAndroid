package dev.silksong.launcher.skins.catalog

import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.PngStructureValidator
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.library.SkinLibraryImporter
import dev.silksong.launcher.skins.library.SkinLibraryStore
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

/**
 * Opt-in probe: imports every archive in DUALSOULS_SKIN_PROBE_DIR through the real Silksong
 * importer and writes what each pack mapped and ignored to DUALSOULS_SKIN_PROBE_OUT.
 * Skipped unless both variables are set; real skin packs are never part of the repository.
 * PNG headers are validated; pixel decoding is Android-only and is not exercised here.
 */
class LocalSkinPackProbeTest {
    @get:Rule val temp = TemporaryFolder()

    @Test
    fun reportLocalPacks() {
        val dir = System.getenv("DUALSOULS_SKIN_PROBE_DIR")?.let(::File)
        val out = System.getenv("DUALSOULS_SKIN_PROBE_OUT")?.let(::File)
        assumeTrue(dir != null && dir.isDirectory && out != null)
        val catalog = File(CATALOG).takeIf(File::isFile)
            ?: File("../../../$CATALOG").takeIf(File::isFile)
            ?: error("catalog not found")
        val paths = catalog.inputStream().use { input ->
            when (val loaded = SkinCatalogPaths.load(SkinCatalogProfiles.Silksong, input)) {
                is SkinResult.Ok -> loaded.value
                is SkinResult.Error -> error(loaded.detail)
            }
        }
        val decoder = PngDecoder { file, _ ->
            when (val info = PngStructureValidator().inspect(file)) {
                is SkinResult.Ok -> SkinResult.Ok(DecodeResult(info.value.width, info.value.height,
                    info.value.width.toLong() * info.value.height))
                is SkinResult.Error -> info
            }
        }
        val report = StringBuilder()
        dir!!.listFiles()!!.filter(File::isFile).sortedBy(File::getName).forEachIndexed { index, archive ->
            report.append("=== ").append(archive.name).append('\n')
            val store = SkinLibraryStore(SkinPaths(temp.newFolder("p$index", "profiles", "silksong")), catalog = paths)
            val importer = SkinLibraryImporter(store, decoder)
            val handle = when (val prepared = importer.prepare(SkinImportInput.SelectedFile(archive.name) { archive.inputStream() })) {
                is SkinResult.Ok -> prepared.value
                is SkinResult.Error -> { report.append("PREPARE ").append(prepared.code).append(": ").append(prepared.detail).append('\n'); return@forEachIndexed }
            }
            handle.candidates.forEach { report.append("candidate ").append(it.name).append(' ').append(it.code).append(": ").append(it.detail).append('\n') }
            if (handle.candidates.none { it.candidateKey != null }) return@forEachIndexed
            when (val committed = importer.commitImport(handle.handleId)) {
                is SkinResult.Error -> report.append("COMMIT ").append(committed.code).append(": ").append(committed.detail).append('\n')
                is SkinResult.Ok -> committed.value.forEach { summary ->
                    report.append("summary ").append(summary.code).append(' ').append(summary.detail).append('\n')
                    summary.warnings.forEach { report.append("  warn ").append(it).append('\n') }
                }
            }
            val library = (store.read() as SkinResult.Ok).value
            library.packs.forEach { pack ->
                val textures = store.requireVerified(pack).games.getValue("silksong").textures.keys
                report.append("mapped ").append(textures.size).append('/').append(paths.paths.size).append('\n')
                textures.sorted().forEach { report.append("  + ").append(it).append('\n') }
                paths.paths.filter { it !in textures }.forEach { report.append("  - ").append(it).append('\n') }
            }
        }
        out!!.writeText(report.toString())
    }

    private companion object {
        const val CATALOG = "docs/superpowers/specs/data/silksong-skin-catalog-v1.txt"
    }
}
