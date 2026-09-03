package dev.silksong.launcher.skins.fixtures

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File

object PinnedCatalogFixture {
    fun load(): CatalogPathSet {
        val resource = javaClass.classLoader?.getResourceAsStream(HollowKnightCatalogPaths.ASSET_NAME)
        val input = resource ?: catalogFile().inputStream()
        return input.use {
            when (val result = HollowKnightCatalogPaths.load(it)) {
                is SkinResult.Ok -> result.value
                is SkinResult.Error -> error(result.detail)
            }
        }
    }

    private fun catalogFile(): File = listOf(
        File("../../../docs/superpowers/specs/data/${HollowKnightCatalogPaths.ASSET_NAME}"),
        File("../../docs/superpowers/specs/data/${HollowKnightCatalogPaths.ASSET_NAME}"),
        File("docs/superpowers/specs/data/${HollowKnightCatalogPaths.ASSET_NAME}"),
    ).map(File::getAbsoluteFile).firstOrNull(File::isFile)
        ?: error("Pinned Hollow Knight catalog fixture is unavailable")
}
