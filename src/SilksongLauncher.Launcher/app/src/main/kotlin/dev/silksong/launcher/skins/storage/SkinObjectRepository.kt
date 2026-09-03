package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import java.io.File

class SkinObjectRepository(
    internal val paths: SkinPaths,
    fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    catalog: CatalogPathSet = CatalogPathSet.requirePinned(),
) {
    internal val verifier = SkinTreeVerifier(fileSystem, catalog = catalog, profileAncestor = paths.profileRoot)

    fun verify(treeSha256: String): SkinResult<SkinManifestDocument> =
        verifier.verify(paths.objectRoot(treeSha256), treeSha256)

    fun verify(root: File, treeSha256: String): SkinResult<SkinManifestDocument> =
        if (root.absoluteFile.normalize() != paths.objectRoot(treeSha256).absoluteFile.normalize()) {
            SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, "Object root is outside its exact immutable digest path")
        } else {
            verifier.verify(root, treeSha256)
        }
}
