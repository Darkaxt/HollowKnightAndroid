package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.SkinNormalizer
import dev.silksong.launcher.skins.importing.SkinObjectBuilder
import java.io.File
import java.util.Base64
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinObjectPublisherTest {
    private lateinit var root: File
    private lateinit var paths: SkinPaths
    private lateinit var catalog: CatalogPathSet
    private val realFs = AndroidSkinFileSystem()

    @Before fun setUp() {
        root = File("build/test-skin-object-publisher").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
        paths.quarantine.mkdirs()
        catalog = PinnedCatalogFixture.load()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun publishesReceiptThenObjectOnlyForAcceptedBuild() {
        val fs = FaultingSkinFileSystem()
        val built = built("local-accepted")
        val result = publisher(fs).publish(built)

        assertTrue("Expected publication, got $result", result is SkinResult.Ok)
        val published = (result as SkinResult.Ok).value
        assertEquals(
            setOf(paths.importReceiptRoot(built.importReceiptSha256), paths.objectRoot(built.treeSha256)),
            published.newlyCreatedRoots.toSet(),
        )
        assertTrue(File(paths.importReceiptRoot(built.importReceiptSha256), ".complete").isFile)
        assertTrue(File(published.objectRoot, ".complete").isFile)
        assertTrue(fs.events.indexOf("write-new:plan") < fs.events.indexOfFirst { it.startsWith("rename:receipt-") })
        assertTrue(fs.events.indexOf("sync-file:plan") < fs.events.indexOfFirst { it.startsWith("rename:receipt-") })
        assertTrue(fs.events.indexOfFirst { it.startsWith("rename:receipt-") } < fs.events.indexOfFirst { it.startsWith("rename:object-") })
        val record = paths.publicationCleanup.listFiles().orEmpty().single()
        val planLines = File(record, "plan").readLines()
        assertEquals(listOf("receipt", "object"), planLines.drop(3).map { it.substringBefore('\t') })
        val plannedIdentities = planLines.drop(3).associate { line ->
            val fields = line.split('\t')
            fields[0] to String(Base64.getUrlDecoder().decode(fields[2]), Charsets.UTF_8)
        }
        assertEquals(realFs.identity(paths.importReceiptRoot(built.importReceiptSha256)).fileKey, plannedIdentities["receipt"])
        assertEquals(realFs.identity(paths.objectRoot(built.treeSha256)).fileKey, plannedIdentities["object"])
        assertFalse(built.ephemeralRoot.exists())
    }

    @Test
    fun `idempotent reuse reports no created roots and can never delete existing immutable roots`() {
        val firstBuilt = built("local-same")
        val first = (publisher().publish(firstBuilt) as SkinResult.Ok).value
        val secondBuilt = built("local-same")
        val second = (publisher().publish(secondBuilt) as SkinResult.Ok).value

        assertEquals(2, first.newlyCreatedRoots.size)
        assertTrue(second.newlyCreatedRoots.isEmpty())
        assertFalse(secondBuilt.ephemeralRoot.exists())
        assertTrue(publisher().discardUnreferenced(second, emptySet()) is SkinResult.Ok)
        assertTrue(first.objectRoot.exists())
        assertTrue(paths.importReceiptRoot(first.importReceiptSha256).exists())
    }

    @Test
    fun `discard never deletes a root reused by a later publication`() {
        val first = (publisher().publish(built("local-reuse-owner")) as SkinResult.Ok).value
        val second = (publisher().publish(built("local-reuse-owner")) as SkinResult.Ok).value
        assertTrue(second.newlyCreatedRoots.isEmpty())

        val result = publisher().discardUnreferenced(first, emptySet())

        assertTrue(result is SkinResult.Ok)
        assertTrue(first.objectRoot.exists())
        assertTrue(paths.importReceiptRoot(first.importReceiptSha256).exists())
    }

    @Test
    fun `failed CAS cleanup deletes only newly-created unreferenced digests`() {
        val published = (publisher().publish(built("local-cas")) as SkinResult.Ok).value
        val receipt = paths.importReceiptRoot(published.importReceiptSha256)

        val retained = publisher().discardUnreferenced(published, setOf(published.treeSha256))

        assertTrue(retained is SkinResult.Ok)
        assertTrue(publisher().discardUnreferenced(published, setOf(published.treeSha256)) is SkinResult.Ok)
        assertTrue(published.objectRoot.exists())
        assertFalse(receipt.exists())
    }

    @Test
    fun `receipt object and barrier failures always remove ephemeral roots and owned immutable orphans`() {
        val failures = listOf(
            "sync-file:import-receipt.json" to 1,
            "sync-file:object.json" to 1,
            "sync-dir:objects" to 3,
        )
        for ((index, failure) in failures.withIndex()) {
            val (event, occurrence) = failure
            root.deleteRecursively()
            root.mkdirs()
            paths = SkinPaths(root)
            paths.quarantine.mkdirs()
            val built = built("local-fault-$index")
            val fs = FaultingSkinFileSystem().apply {
                failOnEvent = event
                failOnOccurrence = occurrence
            }

            val result = publisher(fs).publish(built)

            assertTrue("$event must fail", result is SkinResult.Error)
            assertFalse("$event left ephemeral root", built.ephemeralRoot.exists())
            assertFalse(paths.objectRoot(built.treeSha256).exists())
            assertFalse(paths.importReceiptRoot(built.importReceiptSha256).exists())
        }
    }

    @Test
    fun `object failure after receipt reuse retains the pre-existing receipt and removes ephemeral root`() {
        val first = built("local-first")
        assertTrue(publisher().publish(first) is SkinResult.Ok)
        val reusedReceipt = paths.importReceiptRoot(first.importReceiptSha256)
        val second = built("local-second")
        assertEquals(first.importReceiptSha256, second.importReceiptSha256)
        val fs = FaultingSkinFileSystem().apply { failOnEvent = "sync-file:object.json" }

        val result = publisher(fs).publish(second)

        assertTrue(result is SkinResult.Error)
        assertTrue(reusedReceipt.exists())
        assertFalse(paths.objectRoot(second.treeSha256).exists())
        assertFalse(second.ephemeralRoot.exists())
    }

    @Test
    fun `filesystem inspection failure is returned and still removes ephemeral root`() {
        val built = built("local-inspection")
        val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
            override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
                if (path.absoluteFile.normalize() == built.ephemeralRoot.absoluteFile.normalize()) {
                    throw IllegalStateException("identity unavailable")
                }
                realFs.requireContained(path, owner, allowMissingLeaf)
            }
        }

        val result = publisher(fs).publish(built)

        assertTrue(result is SkinResult.Error)
        assertFalse(built.ephemeralRoot.exists())
    }

    @Test
    fun `post-rename cleanup faults retain durable ownership for bounded recovery`() {
        for (cleanupFault in listOf("delete", "sync")) {
            root.deleteRecursively()
            root.mkdirs()
            paths = SkinPaths(root)
            paths.quarantine.mkdirs()
            val built = built("local-post-rename-$cleanupFault")
            val receiptRoot = paths.importReceiptRoot(built.importReceiptSha256).absoluteFile.normalize()
            var objectPublicationFailed = false
            var cleanupFailureInjected = false
            val objectRoot = paths.objectRoot(built.treeSha256).absoluteFile.normalize()
            val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
                override fun atomicMove(source: File, target: File) {
                    if (target.absoluteFile.normalize() == objectRoot && !objectPublicationFailed) {
                        objectPublicationFailed = true
                        throw IllegalStateException("object publication failed")
                    }
                    realFs.atomicMove(source, target)
                }

                override fun deleteContained(path: File, owner: File) {
                    if (cleanupFault == "delete" && path.absoluteFile.normalize() == receiptRoot && !cleanupFailureInjected) {
                        cleanupFailureInjected = true
                        throw IllegalStateException("immutable cleanup delete failed")
                    }
                    realFs.deleteContained(path, owner)
                }

                override fun syncDirectory(path: File) {
                    if (cleanupFault == "sync" && objectPublicationFailed && path.absoluteFile.normalize() == receiptRoot.parentFile &&
                        !cleanupFailureInjected
                    ) {
                        cleanupFailureInjected = true
                        throw IllegalStateException("immutable cleanup barrier failed")
                    }
                    realFs.syncDirectory(path)
                }
            }

            val result = publisher(fs).publish(built)

            assertTrue(result is SkinResult.Error)
            assertTrue(cleanupFailureInjected)
            assertTrue(paths.publicationCleanup.listFiles().orEmpty().isNotEmpty())
            assertTrue(publisher().recoverOwnedPublications(emptySet()) is SkinResult.Ok)
            assertFalse(receiptRoot.exists())
            assertFalse(paths.objectRoot(built.treeSha256).exists())
            assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
        }
    }

    @Test
    fun `recovery retries a failed deletion parent barrier before dropping ownership`() {
        val built = built("local-restart-barrier")
        val receiptRoot = paths.importReceiptRoot(built.importReceiptSha256).absoluteFile.normalize()
        val objectRoot = paths.objectRoot(built.treeSha256).absoluteFile.normalize()
        var objectPublicationFailed = false
        var cleanupParentSyncAttempts = 0
        val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
            override fun atomicMove(source: File, target: File) {
                if (target.absoluteFile.normalize() == objectRoot && !objectPublicationFailed) {
                    objectPublicationFailed = true
                    throw IllegalStateException("object publication failed")
                }
                realFs.atomicMove(source, target)
            }

            override fun syncDirectory(path: File) {
                if (
                    objectPublicationFailed &&
                    path.absoluteFile.normalize() == receiptRoot.parentFile &&
                    !receiptRoot.exists()
                ) {
                    cleanupParentSyncAttempts++
                    if (cleanupParentSyncAttempts == 1) {
                        throw IllegalStateException("deletion parent barrier failed")
                    }
                }
                realFs.syncDirectory(path)
            }
        }

        assertTrue(publisher(fs).publish(built) is SkinResult.Error)
        assertFalse(receiptRoot.exists())
        assertEquals(1, cleanupParentSyncAttempts)
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isNotEmpty())

        assertTrue(publisher(fs).recoverOwnedPublications(emptySet()) is SkinResult.Ok)
        assertTrue(cleanupParentSyncAttempts >= 2)
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
    }

    @Test
    fun `recovery removes incomplete ownership staging after record creation crashes`() {
        for (crashPoint in listOf("directory", "partial-plan")) {
            root.deleteRecursively()
            root.mkdirs()
            paths = SkinPaths(root)
            paths.quarantine.mkdirs()
            val built = built("local-incomplete-$crashPoint")
            val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
                override fun createDirectory(path: File) {
                    realFs.createDirectory(path)
                    if (
                        crashPoint == "directory" &&
                        path.parentFile?.absoluteFile?.normalize() ==
                            paths.publicationCleanup.absoluteFile.normalize() &&
                        path.name.startsWith("pending-")
                    ) {
                        throw SimulatedProcessDeath()
                    }
                }

                override fun writeNew(path: File, bytes: ByteArray) {
                    if (crashPoint == "partial-plan" && path.name == "plan") {
                        realFs.openOutput(path, createNew = true).use { output ->
                            output.write(bytes, 0, bytes.size / 2)
                        }
                        throw SimulatedProcessDeath()
                    }
                    realFs.writeNew(path, bytes)
                }
            }

            assertThrows(SimulatedProcessDeath::class.java) {
                publisher(fs).publish(built)
            }
            assertTrue(
                paths.publicationCleanup.listFiles().orEmpty()
                    .single().name.startsWith("pending-"),
            )

            assertTrue(publisher().recoverOwnedPublications(emptySet()) is SkinResult.Ok)
            assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
        }
    }

    @Test
    fun `recovery closes a committed ownership record before shard parents exist`() {
        val built = built("local-pre-shard-crash")
        val receiptRoot = paths.importReceiptRoot(built.importReceiptSha256)
        val objectRoot = paths.objectRoot(built.treeSha256)
        val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
            override fun atomicMove(source: File, target: File) {
                realFs.atomicMove(source, target)
                if (
                    target.parentFile?.absoluteFile?.normalize() ==
                        paths.publicationCleanup.absoluteFile.normalize() &&
                    target.name.startsWith("publication-")
                ) {
                    throw SimulatedProcessDeath()
                }
            }
        }

        assertThrows(SimulatedProcessDeath::class.java) {
            publisher(fs).publish(built)
        }
        assertFalse(requireNotNull(receiptRoot.parentFile).exists())
        assertFalse(requireNotNull(objectRoot.parentFile).exists())
        assertTrue(
            paths.publicationCleanup.listFiles().orEmpty()
                .single().name.startsWith("publication-"),
        )

        assertTrue(publisher().recoverOwnedPublications(emptySet()) is SkinResult.Ok)
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
    }

    @Test
    fun `recovery retries a failed ownership record deletion barrier`() {
        val published = (publisher().publish(built("local-record-barrier")) as SkinResult.Ok).value
        var recordDeleted = false
        var cleanupBarrierAttempts = 0
        val fs = object : SkinFileSystem by realFs, SkinFileSystemSecurity by realFs {
            override fun deleteContained(path: File, owner: File) {
                realFs.deleteContained(path, owner)
                if (
                    path.parentFile?.absoluteFile?.normalize() ==
                        paths.publicationCleanup.absoluteFile.normalize()
                ) {
                    recordDeleted = true
                }
            }

            override fun syncDirectory(path: File) {
                if (
                    recordDeleted &&
                    path.absoluteFile.normalize() == paths.publicationCleanup.absoluteFile.normalize()
                ) {
                    cleanupBarrierAttempts++
                    if (cleanupBarrierAttempts == 1) {
                        throw IllegalStateException("ownership deletion barrier failed")
                    }
                }
                realFs.syncDirectory(path)
            }
        }

        assertTrue(publisher(fs).recoverOwnedPublications(emptySet()) is SkinResult.Error)
        assertFalse(published.objectRoot.exists())
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
        assertEquals(1, cleanupBarrierAttempts)

        assertTrue(publisher(fs).recoverOwnedPublications(emptySet()) is SkinResult.Ok)
        assertTrue(cleanupBarrierAttempts >= 2)
    }

    @Test
    fun `recovery preserves referenced and reused roots and removes unreferenced owned roots`() {
        val referencedBuilt = built("local-referenced")
        val referenced = (publisher().publish(referencedBuilt) as SkinResult.Ok).value

        assertTrue(
            publisher().recoverOwnedPublications(
                setOf(referenced.treeSha256, referenced.importReceiptSha256),
            ) is SkinResult.Ok,
        )
        assertTrue(referenced.objectRoot.exists())
        assertTrue(paths.importReceiptRoot(referenced.importReceiptSha256).exists())
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())

        val reused = (publisher().publish(built("local-referenced")) as SkinResult.Ok).value
        assertTrue(reused.newlyCreatedRoots.isEmpty())
        assertTrue(publisher().recoverOwnedPublications(emptySet()) is SkinResult.Ok)
        assertTrue(reused.objectRoot.exists())
        assertTrue(paths.importReceiptRoot(reused.importReceiptSha256).exists())

        val orphan = (publisher().publish(built("local-orphan")) as SkinResult.Ok).value
        assertTrue(publisher().recoverOwnedPublications(emptySet()) is SkinResult.Ok)
        assertFalse(orphan.objectRoot.exists())
        assertTrue(paths.importReceiptRoot(orphan.importReceiptSha256).exists())
        assertTrue(paths.publicationCleanup.listFiles().orEmpty().isEmpty())
    }

    @Test
    fun `recovery rejects ownership record overflow before reading any record`() {
        paths.publicationCleanup.mkdirs()
        repeat(129) { File(paths.publicationCleanup, "record-$it").mkdirs() }

        val result = publisher().recoverOwnedPublications(emptySet())

        assertTrue(result is SkinResult.Error)
        assertEquals(129, paths.publicationCleanup.listFiles().orEmpty().size)
    }

    private class SimulatedProcessDeath : Error()

    private fun publisher(fs: SkinFileSystem = realFs): SkinObjectPublisher = SkinObjectPublisher(
        SkinObjectRepository(paths, fs, catalog),
        SkinImportReceiptRepository(paths, fs, catalog),
        fs,
    )

    private fun built(id: String): dev.silksong.launcher.skins.contracts.BuiltSkin =
        (SkinObjectBuilder(realFs).build(prepared(), id) as SkinResult.Ok).value

    private fun prepared(): PreparedSkinCandidate {
        val archiveBytes = RawZipFixture.one(name = "Pack/Knight.png", data = TinyPngFixture.rgba()).bytes
        val stage = File(paths.quarantine, "quarantine-${System.nanoTime()}").apply { mkdirs() }
        val archive = File(stage, "archive").apply { writeBytes(archiveBytes) }
        val quarantined = QuarantinedArchive(archive, SkinIdentity.sha256(archive), archive.length(), "archive.zip")
        val decoder = PngDecoder { _, info ->
            SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
        }
        val results = (SkinNormalizer(catalog, decoder, realFs).prepare(quarantined) as SkinResult.Ok).value
        return (results.single() as CandidatePreparationResult.Ready).candidate
    }
}
