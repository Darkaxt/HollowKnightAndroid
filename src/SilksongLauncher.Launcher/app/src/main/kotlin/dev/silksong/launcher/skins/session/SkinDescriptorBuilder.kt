package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinObjectDocument
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.RegistryHead
import dev.silksong.launcher.skins.registry.RegistryPack
import dev.silksong.launcher.skins.registry.SkinRegistryDocumentCodec
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinImportReceiptRepository
import dev.silksong.launcher.skins.storage.SkinObjectRepository
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.security.MessageDigest
import java.text.Normalizer
import java.util.UUID

/** Builds a descriptor only from the supplied verified registry head and immutable storage evidence. */
class SkinDescriptorBuilder(
    internal val paths: SkinPaths,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    private val catalog: CatalogPathSet = CatalogPathSet.requirePinned(),
) {
    private val objects = SkinObjectRepository(paths, fileSystem, catalog)
    private val receipts = SkinImportReceiptRepository(paths, fileSystem, catalog)

    fun build(
        descriptorId: UUID,
        sessionSequence: Long,
        registryHead: RegistryHead,
        leaseId: UUID,
        rawLeaseToken: ByteArray,
    ): SkinResult<SkinLaunchDescriptor> = buildInternal(
        descriptorId,
        sessionSequence,
        registryHead,
        leaseId,
        rawLeaseToken,
        null,
    )

    internal fun buildAfterRegistrySnapshotForTest(
        descriptorId: UUID,
        sessionSequence: Long,
        registryHead: RegistryHead,
        leaseId: UUID,
        rawLeaseToken: ByteArray,
        afterSnapshot: () -> Unit,
    ): SkinResult<SkinLaunchDescriptor> = buildInternal(
        descriptorId,
        sessionSequence,
        registryHead,
        leaseId,
        rawLeaseToken,
        afterSnapshot,
    )

    private fun buildInternal(
        descriptorId: UUID,
        sessionSequence: Long,
        registryHead: RegistryHead,
        leaseId: UUID,
        rawLeaseToken: ByteArray,
        afterRegistrySnapshot: (() -> Unit)?,
    ): SkinResult<SkinLaunchDescriptor> = try {
        require(rawLeaseToken.size == 32) { "Lease token is not 256 bits" }
        require(sessionSequence >= 0L) { "Negative session sequence" }
        catalog.revalidate()
        val headSnapshot = registryHead.copy(document = registryHead.document.copy(packs = registryHead.document.packs.toList()))
        afterRegistrySnapshot?.invoke()
        verifyHead(headSnapshot)
        val document = headSnapshot.document
        val packsById = document.packs.associateBy(RegistryPack::id)
        require(packsById.size == document.packs.size) { "Registry head has duplicate pack IDs" }

        val visualReferences = linkedMapOf<String, MutableSet<VisualReference>>()
        fun reference(visual: ActiveVisual) {
            if (visual !is ActiveVisual.Pack) return
            require(visual.id in packsById) { "Registry visual references an absent pack" }
            visualReferences.getOrPut(visual.id) { linkedSetOf() } += VisualReference(
                visual.treeSha256,
                visual.contentSha256,
                visual.importReceiptSha256,
            )
        }
        reference(document.activation.active)
        document.activation.rotationInterlock.prior?.let { reference(it.active) }
        document.activation.rotationInterlock.target?.let { reference(it.active) }

        val union = linkedSetOf<String>()
        fun select(id: String?) {
            if (id == null) return
            require(id in packsById) { "Registry selection references an absent pack" }
            union += id
        }
        select(document.activation.selectedPackId)
        select(document.activation.rotationInterlock.prior?.selectedPackId)
        select(document.activation.rotationInterlock.target?.selectedPackId)
        document.packs.filter(RegistryPack::rotationEligible).forEach { union += it.id }
        union += visualReferences.keys

        val envelopes = union.map { id ->
            val pack = requireNotNull(packsById[id])
            val current = verifyObject(
                pack.treeSha256,
                pack.contentSha256,
                pack.importReceiptSha256,
                expectedCandidateKey = pack.candidateKey,
                expectedPack = pack,
                requireCurrentMetadata = true,
            )
            val currentIdentity = VisualReference(pack.treeSha256, pack.contentSha256, pack.importReceiptSha256)
            val retainedReferences = visualReferences[id].orEmpty().filter { it != currentIdentity }.toSet()
            require(retainedReferences.size <= 1) { "More than one retained active object is required for $id" }
            val retained = retainedReferences.singleOrNull()?.let { old ->
                require(old != currentIdentity) { "Retained active object duplicates current object" }
                verifyObject(
                    old.treeSha256,
                    old.contentSha256,
                    old.importReceiptSha256,
                    expectedCandidateKey = null,
                    expectedPack = pack,
                    requireCurrentMetadata = false,
                )
            }
            DescriptorPackEnvelope(
                pack.id,
                pack.name,
                pack.author,
                pack.candidateKey,
                pack.rotationEligible,
                current,
                retained,
            )
        }.sortedWith { left, right ->
            val name = SkinIdentity.unsignedUtf8Compare(
                Normalizer.normalize(left.name, Normalizer.Form.NFKC),
                Normalizer.normalize(right.name, Normalizer.Form.NFKC),
            )
            if (name != 0) name else SkinIdentity.unsignedUtf8Compare(left.id, right.id)
        }

        SkinLaunchDescriptor(
            schemaVersion = 1,
            descriptorId = descriptorId,
            sessionSequence = sessionSequence,
            profileId = document.profileId,
            gameVersion = document.gameVersion,
            catalogId = document.catalogId,
            catalogSha256 = document.catalogSha256,
            registryGenerationId = headSnapshot.generationId,
            registryGenerationSha256 = headSnapshot.sha256,
            activation = document.activation,
            packs = envelopes,
            leaseId = leaseId,
            leaseTokenSha256 = sha256(rawLeaseToken),
        ).also(SkinLaunchDescriptorCodec::canonical).let { SkinResult.Ok(it) }
    } catch (failure: DescriptorBuildFailure) {
        SkinResult.Error(failure.code, failure.message ?: failure.code.name)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Launch descriptor build failed: ${error.message}")
    }

    private fun verifyHead(head: RegistryHead) {
        val canonical = when (val encoded = SkinRegistryDocumentCodec.canonical(head.document)) {
            is SkinResult.Ok -> encoded.value
            is SkinResult.Error -> fail(encoded.code, encoded.detail)
        }
        require(head.generationId == head.document.generationId) { "Registry head generation ID differs from document" }
        require(head.sequence == head.document.sequence) { "Registry head sequence differs from document" }
        require(head.sha256 == SkinIdentity.sha256(canonical)) { "Registry head digest differs from document" }
    }

    private fun verifyObject(
        treeSha256: String,
        contentSha256: String,
        importReceiptSha256: String,
        expectedCandidateKey: String?,
        expectedPack: RegistryPack,
        requireCurrentMetadata: Boolean,
    ): DescriptorObjectEnvelope {
        val root = paths.objectRoot(treeSha256)
        val receiptRoot = paths.importReceiptRoot(importReceiptSha256)
        requireImmutableContainment(root, SkinImportCode.OBJECT_CORRUPT, "Immutable object root is outside the profile")
        requireImmutableContainment(receiptRoot, SkinImportCode.IMPORT_RECEIPT_CORRUPT, "Immutable receipt root is outside the profile")
        val firstManifest = verifyObjectResult(root, treeSha256)
        val receipt = verifyReceiptResult(receiptRoot, importReceiptSha256)
        if (expectedCandidateKey != null && receipt.candidateKey != expectedCandidateKey) {
            fail(SkinImportCode.IMPORT_RECEIPT_CORRUPT, "Current receipt candidate does not match registry ownership")
        }
        val objectDocument = readObjectDocument(root)
        val finalManifest = verifyObjectResult(root, treeSha256)
        if (firstManifest != finalManifest || finalManifest.id != expectedPack.id ||
            (requireCurrentMetadata && (finalManifest.name != expectedPack.name || finalManifest.author != expectedPack.author)) ||
            finalManifest.contentSha256 != contentSha256 || objectDocument.treeSha256 != treeSha256 ||
            objectDocument.contentSha256 != contentSha256
        ) {
            fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object evidence changed or mismatches registry")
        }
        if (objectDocument.manifestSha256 != SkinIdentity.sha256(readStable(File(root, "pack/skin.json"), 65536))) {
            fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object manifest digest mismatches")
        }
        val textures = textures(objectDocument, finalManifest)
        if (verifyObjectResult(root, treeSha256) != finalManifest) {
            fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object evidence changed during descriptor construction")
        }
        verifyReceiptResult(receiptRoot, importReceiptSha256)
        return DescriptorObjectEnvelope(
            objectRoot = objectRelativePath(treeSha256),
            receiptPath = receiptRelativePath(importReceiptSha256),
            treeSha256 = treeSha256,
            contentSha256 = contentSha256,
            manifestSha256 = objectDocument.manifestSha256,
            importReceiptSha256 = importReceiptSha256,
            textures = textures,
        )
    }

    private fun verifyObjectResult(root: File, treeSha256: String) = when (val verified = objects.verify(root, treeSha256)) {
        is SkinResult.Ok -> verified.value
        is SkinResult.Error -> fail(verified.code, verified.detail)
    }

    private fun verifyReceiptResult(root: File, sha256: String) = when (val verified = receipts.verify(root, sha256)) {
        is SkinResult.Ok -> verified.value
        is SkinResult.Error -> fail(verified.code, verified.detail)
    }

    private fun readObjectDocument(root: File): SkinObjectDocument {
        val objectFile = File(root, "object.json")
        val bytes = readStable(objectFile, 262144)
        return when (val parsed = CanonicalJson.tryParseObject(bytes)) {
            is SkinResult.Ok -> parsed.value
            is SkinResult.Error -> fail(SkinImportCode.OBJECT_CORRUPT, parsed.detail)
        }
    }

    private fun textures(
        objectDocument: SkinObjectDocument,
        manifest: dev.silksong.launcher.skins.documents.SkinManifestDocument,
    ): List<DescriptorTextureEnvelope> {
        val files = objectDocument.files.associateBy { it.path }
        val mappings = manifest.games.getValue("hollow-knight").textures
        return catalog.paths.mapIndexedNotNull { ordinal, target ->
            val sourceName = mappings[target] ?: return@mapIndexedNotNull null
            val source = files["assets/$sourceName"]
                ?: fail(SkinImportCode.OBJECT_CORRUPT, "Manifest texture has no immutable object row")
            if (SkinIdentity.base32DigestHex(source.sha256) != sourceName) {
                fail(SkinImportCode.OBJECT_CORRUPT, "Manifest texture source name does not bind its digest")
            }
            DescriptorTextureEnvelope(
                ordinal,
                target,
                "pack/assets/$sourceName",
                source.sha256,
                source.length,
            )
        }.also { textures ->
            if (textures.size != mappings.size) fail(SkinImportCode.OBJECT_CORRUPT, "Manifest has out-of-catalog texture mappings")
        }
    }

    private fun readStable(file: File, maximumBytes: Long): ByteArray = try {
        fileSystem.requireContained(file, paths.profileRoot)
        if (!fileSystem.isRegularFile(file) || fileSystem.isSymbolicLink(file)) {
            fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object document is unsafe")
        }
        val before = fileSystem.identity(file)
        if (!before.regularFile || before.size > maximumBytes) fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object document exceeds bound")
        val bytes = fileSystem.openNoFollow(file).use { it.readBytes() }
        if (fileSystem.identity(file) != before || bytes.size.toLong() != before.size) {
            fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object document changed while reading")
        }
        bytes
    } catch (failure: DescriptorBuildFailure) {
        throw failure
    } catch (error: Exception) {
        fail(SkinImportCode.OBJECT_CORRUPT, "Immutable object document verification failed: ${error.message}")
    }

    private fun requireImmutableContainment(path: File, code: SkinImportCode, detail: String) {
        try {
            fileSystem.requireContained(path, paths.profileRoot)
        } catch (error: Exception) {
            fail(code, "$detail: ${error.message}")
        }
    }

    private fun objectRelativePath(treeSha256: String): String = "objects/sha256/${treeSha256.take(2)}/$treeSha256"

    private fun receiptRelativePath(importReceiptSha256: String): String =
        "import-receipts/sha256/${importReceiptSha256.take(2)}/$importReceiptSha256"

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun fail(code: SkinImportCode, detail: String): Nothing = throw DescriptorBuildFailure(code, detail)

    private data class VisualReference(
        val treeSha256: String,
        val contentSha256: String,
        val importReceiptSha256: String,
    )

    private class DescriptorBuildFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
}
