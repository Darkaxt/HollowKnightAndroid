package dev.silksong.launcher.skins

import android.content.res.AssetManager
import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinImportReceiptDocument
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import dev.silksong.launcher.skins.documents.SkinObjectDocument
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.storage.*
import java.io.File
import java.io.InputStream
import java.lang.reflect.Modifier
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SkinTaskOneApiContractTest {
    @Test
    fun `Task 2 compile contract retains exact plan surfaces`() {
        val selected = SkinImportInput.SelectedFile(null) { error("not opened") }
        val immediate = SkinImportInput.ImmediateFolderFile(null, "document:42") { error("not opened") }
        val expansionRatio: Int = SkinLimits.V1.expansionRatio
        assertNull(selected.displayName)
        assertEquals("document:42", immediate.documentId)
        assertEquals(100, expansionRatio)
        assertTrue(SkinResult::class.java.isInterface)

        val catalogConstructor: (ByteArray, String, List<String>) -> CatalogPathSet = ::CatalogPathSet
        val catalogLoader: (AssetManager) -> HollowKnightCatalogPaths = ::HollowKnightCatalogPaths
        val normalizerConstructor: (CatalogPathSet, PngDecoder, SkinFileSystem) -> SkinNormalizer = ::SkinNormalizer
        assertEquals(3, listOf(catalogConstructor, catalogLoader, normalizerConstructor).size)

        val manifestEncoder: (SkinManifestDocument) -> ByteArray = CanonicalJson::manifest
        val objectEncoder: (SkinObjectDocument) -> ByteArray = CanonicalJson::objectDocument
        val receiptEncoder: (SkinImportReceiptDocument) -> ByteArray = CanonicalJson::importReceipt
        val manifestParser: (ByteArray) -> SkinManifestDocument = CanonicalJson::parseManifest
        val objectParser: (ByteArray) -> SkinObjectDocument = CanonicalJson::parseObject
        val receiptParser: (ByteArray) -> SkinImportReceiptDocument = CanonicalJson::parseImportReceipt
        assertEquals(6, listOf(manifestEncoder, objectEncoder, receiptEncoder, manifestParser, objectParser, receiptParser).size)
    }

    @Test
    fun `required importer and storage invocation surfaces compile`() {
        val zipReader: (File, SkinLimits) -> SkinResult<ZipArchive> = BoundedZipReader::read
        val pathAuthority: (ZipArchive) -> SkinResult<AuthorizedZip> = ZipPathAuthority::validate
        val discovery: (AuthorizedZip) -> SkinResult<CandidateSet> = SkinCandidateDiscovery::discover
        val mapper: (SkinCandidate, List<String>) -> SkinResult<CatalogMapping> = SkinCatalogMapper::map
        val pngInspector: (InputStream, Long) -> SkinResult<PngInfo> = PngStructureValidator::inspect
        val builder: (SkinFileSystem) -> SkinObjectBuilder = ::SkinObjectBuilder
        val publisher: (SkinObjectRepository, SkinImportReceiptRepository, SkinFileSystem) -> SkinObjectPublisher =
            ::SkinObjectPublisher
        val durable: (SkinFileSystem) -> DurableDirectoryPublisher = ::DurableDirectoryPublisher
        assertEquals(8, listOf(zipReader, pathAuthority, discovery, mapper, pngInspector, builder, publisher, durable).size)
    }

    @Test
    fun `authorized paths and filesystem abstract surface remain exact`() {
        val authorizedConstructor: (ZipArchive, Map<Int, List<ByteArray>>) -> AuthorizedZip = ::AuthorizedZip
        assertEquals(2, AuthorizedZip::class.java.declaredFields.size)
        assertEquals(setOf("archive", "canonicalPaths"), AuthorizedZip::class.java.declaredFields.map { it.name }.toSet())
        assertEquals(1, listOf(authorizedConstructor).size)

        val downstream = object : SkinFileSystem {
            override fun createDirectory(path: File) = Unit
            override fun writeNew(path: File, bytes: ByteArray) = Unit
            override fun syncFile(path: File) = Unit
            override fun syncDirectory(path: File) = Unit
            override fun atomicMove(source: File, target: File) = Unit
            override fun openNoFollow(path: File): InputStream = InputStream.nullInputStream()
            override fun identity(path: File): SkinNodeIdentity = SkinNodeIdentity("key", 0, true)
            override fun list(path: File): List<File> = emptyList()
            override fun deleteContained(path: File, owner: File) = Unit
        }
        assertEquals(emptyList<File>(), downstream.list(File(".")))

        val expected = setOf(
            "createDirectory", "writeNew", "syncFile", "syncDirectory", "atomicMove",
            "openNoFollow", "identity", "list", "deleteContained",
        )
        val abstractMethods = SkinFileSystem::class.java.declaredMethods
            .filter { Modifier.isAbstract(it.modifiers) }
            .map { it.name }
            .toSet()
        assertEquals(expected, abstractMethods)
    }

    @Test
    fun `built and published models expose exact ownership fields`() {
        val builtFields = BuiltSkin::class.java.declaredFields.map { it.name }.toSet()
        val publishedFields = PublishedSkin::class.java.declaredFields.map { it.name }.toSet()
        assertEquals(
            setOf(
                "id", "candidateKey", "name", "contentSha256", "treeSha256", "manifestSha256",
                "importReceiptSha256", "manifestBytes", "objectBytes", "importReceiptBytes", "ephemeralRoot",
            ),
            builtFields,
        )
        assertEquals(
            setOf(
                "id", "candidateKey", "name", "contentSha256", "treeSha256", "manifestSha256",
                "importReceiptSha256", "objectRoot", "newlyCreatedRoots",
            ),
            publishedFields,
        )
    }
}
