package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.SkinNormalizer
import dev.silksong.launcher.skins.importing.SkinObjectBuilder
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.ActivationSnapshot
import dev.silksong.launcher.skins.registry.InterlockState
import dev.silksong.launcher.skins.registry.RegistryHead
import dev.silksong.launcher.skins.registry.RegistryPack
import dev.silksong.launcher.skins.registry.RotationInterlock
import dev.silksong.launcher.skins.registry.SkinActivation
import dev.silksong.launcher.skins.registry.SkinBindingToken
import dev.silksong.launcher.skins.registry.SkinMode
import dev.silksong.launcher.skins.registry.SkinOperationKind
import dev.silksong.launcher.skins.registry.SkinRegistryDocument
import dev.silksong.launcher.skins.registry.SkinRegistryDocumentCodec
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.SkinImportReceiptRepository
import dev.silksong.launcher.skins.storage.SkinObjectPublisher
import dev.silksong.launcher.skins.storage.SkinObjectRepository
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinDescriptorBuilderTest {
    private lateinit var profileRoot: File
    private lateinit var paths: SkinPaths
    private lateinit var catalog: CatalogPathSet
    private val realFileSystem = AndroidSkinFileSystem()
    private val fileSystem: SkinFileSystem = realFileSystem

    @Before
    fun setUp() {
        profileRoot = File("build/test-skin-descriptor-builder").absoluteFile
        profileRoot.deleteRecursively()
        profileRoot.mkdirs()
        paths = SkinPaths(profileRoot)
        paths.quarantine.mkdirs()
        catalog = PinnedCatalogFixture.load()
    }

    @After
    fun tearDown() {
        profileRoot.deleteRecursively()
    }

    @Test
    fun buildsCompleteDescriptorUnionInCatalogOrder() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2, listOf(catalog.paths[0], catalog.paths[2]))
        val betaCurrent = publish("beta", 3)
        val head = head(alphaCurrent, alphaOld, betaCurrent)

        val descriptor = assertOk(
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.fromString("62345678-1234-4234-8234-123456789abc"),
                14,
                head,
                UUID.fromString("72345678-1234-4234-8234-123456789abc"),
                ByteArray(32) { 0x5a },
            ),
        )

        assertEquals(listOf("alpha", "beta"), descriptor.packs.map(DescriptorPackEnvelope::id))
        val alpha = descriptor.packs.first()
        assertEquals(alphaCurrent.treeSha256, alpha.currentObject.treeSha256)
        assertEquals(alphaOld.treeSha256, alpha.retainedActiveObject?.treeSha256)
        assertEquals("objects/sha256/${alphaCurrent.treeSha256.take(2)}/${alphaCurrent.treeSha256}", alpha.currentObject.objectRoot)
        assertEquals(
            "import-receipts/sha256/${alphaCurrent.importReceiptSha256.take(2)}/${alphaCurrent.importReceiptSha256}",
            alpha.currentObject.receiptPath,
        )
        assertEquals(
            alpha.currentObject.textures.map(DescriptorTextureEnvelope::target).sortedBy { catalog.paths.indexOf(it) },
            alpha.currentObject.textures.map(DescriptorTextureEnvelope::target),
        )
        assertEquals(
            alpha.currentObject.textures.map { texture -> catalog.paths.indexOf(texture.target) },
            alpha.currentObject.textures.map(DescriptorTextureEnvelope::ordinal),
        )
        assertEquals(sha256(ByteArray(32) { 0x5a }), descriptor.leaseTokenSha256)
    }

    @Test
    fun includesInterlockTargetSelectionInDescriptorUnion() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        val gammaCurrent = publish("gamma", 4)

        val descriptor = assertOk(
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, head(alphaCurrent, alphaOld, betaCurrent, gammaCurrent), UUID.randomUUID(), ByteArray(32),
            ),
        )

        assertEquals(listOf("alpha", "beta", "gamma"), descriptor.packs.map(DescriptorPackEnvelope::id))
        assertTrue(descriptor.packs.single { it.id == "gamma" }.rotationEligible.not())
    }

    @Test
    fun retainsSameTreeAndContentWhenImportReceiptChanges() {
        val alphaOld = publish("alpha", 1, archiveComment = byteArrayOf(1))
        val alphaCurrent = publish("alpha", 1, archiveComment = byteArrayOf(2))
        val betaCurrent = publish("beta", 3)
        assertEquals(alphaOld.treeSha256, alphaCurrent.treeSha256)
        assertEquals(alphaOld.contentSha256, alphaCurrent.contentSha256)
        assertTrue(alphaOld.importReceiptSha256 != alphaCurrent.importReceiptSha256)

        val descriptor = assertOk(
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, head(alphaCurrent, alphaOld, betaCurrent), UUID.randomUUID(), ByteArray(32),
            ),
        )
        val alpha = descriptor.packs.single { it.id == "alpha" }
        val retained = requireNotNull(alpha.retainedActiveObject)
        assertEquals(alpha.currentObject.treeSha256, retained.treeSha256)
        assertEquals(alpha.currentObject.contentSha256, retained.contentSha256)
        assertEquals(alphaOld.importReceiptSha256, retained.importReceiptSha256)
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)
        assertOk(SkinLaunchDescriptorCodec.parse(canonical, sha256(canonical), expectations(descriptor)))
    }

    @Test
    fun snapshotsMutableRegistryPacksBeforeHeadVerificationAndBuilding() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        val stable = head(alphaCurrent, alphaOld, betaCurrent)
        val mutablePacks = stable.document.packs.toMutableList()
        val packsB = listOf(mutablePacks.first())
        val hostileDocument = stable.document.copy(packs = mutablePacks)
        val hostileHead = RegistryHead(stable.generationId, stable.sequence, stable.sha256, hostileDocument)

        val descriptor = assertOk(
            SkinDescriptorBuilder(paths, fileSystem, catalog).buildAfterRegistrySnapshotForTest(
                UUID.randomUUID(),
                1,
                hostileHead,
                UUID.randomUUID(),
                ByteArray(32),
            ) {
                mutablePacks.clear()
                mutablePacks += packsB
            },
        )

        assertEquals(listOf("alpha", "beta"), descriptor.packs.map(DescriptorPackEnvelope::id))
    }

    @Test
    fun rejectsMissingImmutableObjectEvidence() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        alphaCurrent.objectRoot.deleteRecursively()

        assertError(
            SkinImportCode.OBJECT_CORRUPT,
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, head(alphaCurrent, alphaOld, betaCurrent), UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    @Test
    fun rejectsChangedImmutableObjectEvidence() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        File(alphaCurrent.objectRoot, "pack/assets").listFiles()!!.first().writeBytes(ByteArray(1))

        assertError(
            SkinImportCode.OBJECT_CORRUPT,
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, head(alphaCurrent, alphaOld, betaCurrent), UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    @Test
    fun rejectsCorruptImportReceiptEvidence() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        File(paths.importReceiptRoot(alphaCurrent.importReceiptSha256), "import-receipt.json").writeText("{}")

        assertError(
            SkinImportCode.IMPORT_RECEIPT_CORRUPT,
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, head(alphaCurrent, alphaOld, betaCurrent), UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    @Test
    fun rejectsInjectedAliasedObjectAndReceiptEvidence() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        val registryHead = head(alphaCurrent, alphaOld, betaCurrent)

        assertError(
            SkinImportCode.OBJECT_CORRUPT,
            SkinDescriptorBuilder(paths, aliasing(alphaCurrent.objectRoot), catalog).build(
                UUID.randomUUID(), 1, registryHead, UUID.randomUUID(), ByteArray(32),
            ),
        )
        assertError(
            SkinImportCode.IMPORT_RECEIPT_CORRUPT,
            SkinDescriptorBuilder(paths, aliasing(requireNotNull(paths.importReceiptRoot(alphaCurrent.importReceiptSha256).parentFile)), catalog).build(
                UUID.randomUUID(), 1, registryHead, UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    @Test
    fun rejectsCurrentRegistryMetadataThatDiffersFromVerifiedObject() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val betaCurrent = publish("beta", 3)
        val normal = head(alphaCurrent, alphaOld, betaCurrent).document
        val forgedPacks = normal.packs.map { pack ->
            if (pack.id == "alpha") pack.copy(name = "Forged") else pack
        }
        val forged = normal.copy(packs = forgedPacks)
        val forgedHead = RegistryHead(forged.generationId, forged.sequence, digest(forged), forged)

        assertError(
            SkinImportCode.OBJECT_CORRUPT,
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, forgedHead, UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    @Test
    fun rejectsMultipleRetainedActiveObjectsForOnePack() {
        val alphaOld = publish("alpha", 1)
        val alphaCurrent = publish("alpha", 2)
        val alphaOtherOld = publish("alpha", 4)
        val betaCurrent = publish("beta", 3)
        val normal = head(alphaCurrent, alphaOld, betaCurrent).document
        val oldVisual = ActiveVisual.Pack(
            "alpha",
            alphaOtherOld.treeSha256,
            alphaOtherOld.contentSha256,
            alphaOtherOld.importReceiptSha256,
        )
        val interlock = normal.activation.rotationInterlock.copy(
            target = ActivationSnapshot(SkinMode.ON, "alpha", oldVisual, normal.activation.skinStamp + 1),
        )
        val document = normal.copy(activation = normal.activation.copy(rotationInterlock = interlock))
        val invalidHead = RegistryHead(document.generationId, document.sequence, digest(document), document)

        assertError(
            SkinImportCode.DOCUMENT_INVALID,
            SkinDescriptorBuilder(paths, fileSystem, catalog).build(
                UUID.randomUUID(), 1, invalidHead, UUID.randomUUID(), ByteArray(32),
            ),
        )
    }

    private fun head(
        alphaCurrent: PublishedSkin,
        alphaOld: PublishedSkin,
        betaCurrent: PublishedSkin,
        gammaCurrent: PublishedSkin? = null,
    ): RegistryHead {
        val alpha = RegistryPack(
            "alpha", alphaCurrent.name, "Unknown", alphaCurrent.candidateKey, alphaCurrent.treeSha256,
            alphaCurrent.contentSha256, alphaCurrent.importReceiptSha256, false,
        )
        val beta = RegistryPack(
            "beta", betaCurrent.name, "Unknown", betaCurrent.candidateKey, betaCurrent.treeSha256,
            betaCurrent.contentSha256, betaCurrent.importReceiptSha256, true,
        )
        val gamma = gammaCurrent?.let {
            RegistryPack(
                "gamma", it.name, "Unknown", it.candidateKey, it.treeSha256,
                it.contentSha256, it.importReceiptSha256, false,
            )
        }
        val active = ActiveVisual.Pack("alpha", alphaOld.treeSha256, alphaOld.contentSha256, alphaOld.importReceiptSha256)
        val prior = ActivationSnapshot(SkinMode.ON, "alpha", active, 3)
        val activation = SkinActivation(
            prior.mode,
            prior.selectedPackId,
            prior.active,
            prior.skinStamp,
            RotationInterlock(
                InterlockState.ARMED,
                "82345678-1234-4234-8234-123456789abc",
                SkinOperationKind.MODE_ON,
                "92345678-1234-4234-8234-123456789abc",
                sha256("registry-parent".toByteArray()),
                prior,
                ActivationSnapshot(
                    SkinMode.ON,
                    "gamma".takeIf { gamma != null } ?: "beta",
                    ActiveVisual.Pack("beta", beta.treeSha256, beta.contentSha256, beta.importReceiptSha256),
                    4,
                ),
                SkinBindingToken("verified-binding"),
                true,
                null,
                null,
            ),
        )
        val document = SkinRegistryDocument(
            1,
            "a2345678-1234-4234-8234-123456789abc",
            5,
            "92345678-1234-4234-8234-123456789abc",
            "a2345678-1234-4234-8234-123456789abc",
            "test",
            "hollow-knight",
            "1.5.12620",
            "hk-custom-knight-v3.5.0-205",
            "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a",
            listOfNotNull(alpha, beta, gamma),
            activation,
        )
        return RegistryHead(document.generationId, document.sequence, digest(document), document)
    }

    private fun publish(
        id: String,
        width: Int,
        targets: List<String> = catalog.paths.take(2),
        archiveComment: ByteArray = ByteArray(0),
    ): PublishedSkin {
        val archiveBytes = RawZipFixture.build(
            targets.mapIndexed { index, target ->
                RawZipFixture.Entry(
                    "Pack/$target".toByteArray(),
                    TinyPngFixture.rgba(width + index, 1),
                )
            },
            archiveComment,
        ).bytes
        val stage = File(paths.quarantine, "quarantine-$id-$width").apply { mkdirs() }
        val archive = File(stage, "archive.zip").apply { writeBytes(archiveBytes) }
        val prepared = assertOk(
            SkinNormalizer(catalog, decoder(), fileSystem).prepare(
                QuarantinedArchive(archive, SkinIdentity.sha256(archiveBytes), archiveBytes.size.toLong(), archive.name),
            ),
        ).single() as CandidatePreparationResult.Ready
        val built = assertOk(SkinObjectBuilder(fileSystem, catalog).build(prepared.candidate, id))
        return assertOk(
            SkinObjectPublisher(
                SkinObjectRepository(paths, fileSystem, catalog),
                SkinImportReceiptRepository(paths, fileSystem, catalog),
                fileSystem,
            ).publish(built),
        )
    }

    private fun aliasing(alias: File): SkinFileSystem = object : SkinFileSystem by realFileSystem, SkinFileSystemSecurity by realFileSystem {
        private val aliased = alias.absoluteFile.normalize().toPath()

        override fun isSymbolicLink(file: File): Boolean =
            file.absoluteFile.normalize().toPath() == aliased || realFileSystem.isSymbolicLink(file)

        override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
            if (path.absoluteFile.normalize().toPath().startsWith(aliased)) {
                throw IllegalStateException("injected aliased path component")
            }
            realFileSystem.requireContained(path, owner, allowMissingLeaf)
        }
    }

    private fun decoder() = PngDecoder { _, info ->
        SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
    }

    private fun expectations(value: SkinLaunchDescriptor) = DescriptorExpectations(
        value.descriptorId,
        value.profileId,
        value.gameVersion,
        value.catalogId,
        value.catalogSha256,
        value.leaseId,
    )

    private fun digest(document: SkinRegistryDocument): String = sha256(assertOk(SkinRegistryDocumentCodec.canonical(document)))

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }
}
