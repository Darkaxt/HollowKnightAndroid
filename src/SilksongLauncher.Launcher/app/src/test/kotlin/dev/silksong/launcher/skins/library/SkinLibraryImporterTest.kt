package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.storage.*
import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.ui.SkinImportWorkflow
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SkinLibraryImporterTest {
    @get:Rule val temporary = TemporaryFolder()
    private fun store(): SkinLibraryStore { PinnedCatalogFixture.load(); return SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })) }
    private val decoder = PngDecoder { _, info -> SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong()*info.height)) }
    private fun input() = SkinImportInput.SelectedFile("ordinary.zip") {
        RawZipFixture.build(listOf(RawZipFixture.Entry("My skin/Knight.png".toByteArray(), TinyPngFixture.rgba()))).bytes.inputStream()
    }
    @Test fun `ordinary ZIP imports without metadata then select mode cycle and logical removal preserve immutable bytes`() {
        val store = store(); val importer = SkinLibraryImporter(store, decoder)
        val prepared = importer.prepare(input()).required()
        val installed = importer.commitImport(prepared.handleId).required().single()
        val document = store.read().required(); val pack = document.packs.single()
        assertEquals(pack.id, installed.installedId); assertEquals(LibraryMode.OFF, document.mode)
        assertNull(document.selectedPackId); assertTrue(document.eligiblePackIds.isEmpty())
        val root = store.paths.objectRoot(pack.treeSha256)
        val bytes = root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() }
        assertTrue(store.select(pack.id) is SkinResult.Ok); assertTrue(store.setEligibility(pack.id,true) is SkinResult.Ok)
        for (mode in listOf(LibraryMode.ON, LibraryMode.ROTATE, LibraryMode.OFF)) {
            assertTrue(store.advanceMode() is SkinResult.Ok); assertEquals(mode, store.read().required().mode)
        }
        assertTrue(store.remove(pack.id) is SkinResult.Ok)
        assertEquals(bytes, root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() })
        assertFalse(File(store.paths.root,"registry").exists())
    }
    @Test fun `same archive imported twice reuses installed immutable identity`() {
        val store = store(); val importer = SkinLibraryImporter(store, decoder)
        repeat(2) { importer.commitImport(importer.prepare(input()).required().handleId).required() }
        assertEquals(1,store.read().required().packs.size)
    }
    @Test fun `retained orphan bytes count and admission rejection happens before provider open while OFF remains available`() {
        val store = store(); store.read().required()
        val before = store.quota.usage().required().profileBytes
        File(store.paths.root,"retained-orphan").writeBytes(ByteArray(65537))
        assertTrue(store.quota.usage().required().profileBytes >= before + 65537)
        val reserved = store.quota.reserve(dev.silksong.launcher.skins.quota.SkinQuotaRequest.profile(700L*1024*1024)).required()
        try {
            var opens = 0
            val result = SkinLibraryImporter(store,decoder).prepare(SkinImportInput.SelectedFile("bounded.zip") { opens++; input().openOnce() })
            assertEquals(SkinImportCode.PROFILE_QUOTA_EXCEEDED,(result as SkinResult.Error).code)
            assertEquals(0,opens); assertTrue(store.recoverOff() is SkinResult.Ok)
        } finally { reserved.release() }
    }
    @Test fun `published orphan survives configuration failure and retry reuses its exact bytes`() {
        PinnedCatalogFixture.load()
        val paths = SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })
        val base = dev.silksong.launcher.skins.storage.AndroidSkinFileSystem(); var fail = false
        val fs = object : dev.silksong.launcher.skins.storage.SkinFileSystem by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemSecurity by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by base {
            override fun atomicMove(source: File,destination: File) {
                if (fail && destination.name == "library.json") { fail = false; throw java.io.IOException("config publication failed") }
                base.atomicMove(source,destination)
            }
        }
        val store = SkinLibraryStore(paths,fs); val importer = SkinLibraryImporter(store,decoder)
        val handle = importer.prepare(input()).required(); fail = true
        assertTrue(importer.commitImport(handle.handleId) is SkinResult.Error)
        assertTrue(store.read().required().packs.isEmpty())
        val objectRoot = requireNotNull(paths.objects.walkTopDown().single { it.name == "object.json" }.parentFile)
        val bytes = objectRoot.walkTopDown().filter { it.isFile }.associate { it.relativeTo(objectRoot).path to it.readBytes().toList() }
        assertTrue(bytes.isNotEmpty()); assertTrue(store.quota.usage().required().profileBytes > 0)
        importer.commitImport(handle.handleId).required()
        assertEquals(1,store.read().required().packs.size)
        assertEquals(bytes,objectRoot.walkTopDown().filter { it.isFile }.associate { it.relativeTo(objectRoot).path to it.readBytes().toList() })
    }
    @Test fun `selected ON replacement is rejected then OFF replacement retains previous object`() {
        val store = store(); val importer = SkinLibraryImporter(store,decoder)
        importer.commitImport(importer.prepare(input()).required().handleId).required()
        val old = store.read().required().packs.single(); store.select(old.id).required(); store.advanceMode().required()
        val prepared = importer.prepare(SkinImportInput.SelectedFile("other.zip") {
            RawZipFixture.build(listOf(RawZipFixture.Entry("Other/Knight.png".toByteArray(),TinyPngFixture.rgba(2,1)))).bytes.inputStream()
        }).required()
        val target = dev.silksong.launcher.skins.ui.SkinReplaceTarget(old.id,"0".repeat(64),old.treeSha256,old.receiptSha256)
        val request = dev.silksong.launcher.skins.ui.SkinReplaceRequest(prepared.handleId,prepared.candidates.single().candidateKey!!,target)
        assertTrue(importer.commitReplace(request) is SkinResult.Error)
        store.recoverOff().required(); importer.commitReplace(request).required()
        val changed = store.read().required().packs.single()
        assertEquals(old.id,changed.id); assertNotEquals(old.treeSha256,changed.treeSha256)
        assertTrue(store.paths.objectRoot(old.treeSha256).isDirectory)
        store.objects.verify(old.treeSha256).required(); store.objects.verify(changed.treeSha256).required()
    }
    @Test fun `cancellation failure retains retry handle and only removes its owned preparation`() {
        PinnedCatalogFixture.load()
        val paths = SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })
        val base = dev.silksong.launcher.skins.storage.AndroidSkinFileSystem(); var failingOwner: File? = null
        val fs = object : dev.silksong.launcher.skins.storage.SkinFileSystem by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemSecurity by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by base {
            override fun deleteContained(file: File,owner: File) {
                if (file == failingOwner) { failingOwner = null; throw java.io.IOException("cleanup fault") }
                base.deleteContained(file,owner)
            }
        }
        val store = SkinLibraryStore(paths,fs); val importer = SkinLibraryImporter(store,decoder)
        val prepared = importer.prepare(input()).required(); val owned = File(paths.importHandles,prepared.handleId.toString())
        val unrelated = File(paths.importHandles,"unrelated").apply { writeText("retained") }; failingOwner = owned
        assertTrue(importer.cancel(prepared.handleId) is SkinResult.Error); assertTrue(owned.exists())
        assertTrue(importer.cancel(prepared.handleId) is SkinResult.Ok); assertFalse(owned.exists())
        assertEquals("retained",unrelated.readText()); assertTrue(importer.commitImport(prepared.handleId) is SkinResult.Error)
        assertTrue(store.read().required().packs.isEmpty())
    }
    @Test fun `failed preparation cleanup blocks provider admission until exact owner retry succeeds`() {
        PinnedCatalogFixture.load()
        val paths = SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })
        val base = AndroidSkinFileSystem(); var denyOwnerCleanup = true; var opens = 0
        val fs = object : SkinFileSystem by base, SkinFileSystemSecurity by base, SkinFileSystemBoundedListing by base {
            override fun deleteContained(file: File,owner: File) {
                if (denyOwnerCleanup && file.parentFile == paths.importHandles) throw java.io.IOException("owner cleanup denied")
                base.deleteContained(file,owner)
            }
        }
        val store = SkinLibraryStore(paths,fs); val importer = SkinLibraryImporter(store,decoder)
        repeat(5) {
            val workflow = SkinImportWorkflow(importer)
            val result = workflow.prepare(listOf(SkinImportInput.SelectedFile("malformed.zip") {
                opens++
                ByteArray(65537) { 65 }.also { it[0]=0x50; it[1]=0x4b; it[2]=3; it[3]=4 }.inputStream()
            })).required().single().result
            assertTrue(result is SkinResult.Error); assertTrue(workflow.handles().isEmpty())
            workflow.cancel().required()
        }
        val owners = paths.importHandles.listFiles()!!.filter { it.isDirectory }
        assertEquals("Pending cleanup must run before reopening a provider",1,opens)
        assertEquals("Failed owners count toward bounded preparation ownership",1,owners.size)
        val pending = owners.single()
        val unrelated = File(paths.importHandles,"unrelated").apply { mkdirs() }
        val sentinel = File(unrelated,"keep").apply { writeText("unrelated bytes") }
        store.recoverOff().required(); assertTrue(pending.exists())
        denyOwnerCleanup = false
        val next = importer.prepare(SkinImportInput.SelectedFile("retry.zip") {
            assertFalse("Cleanup must finish before new provider IO",pending.exists())
            input().openOnce()
        }).required()
        importer.cancel(next.handleId).required()
        assertEquals("unrelated bytes",sentinel.readText())
        assertEquals(listOf(unrelated),paths.importHandles.listFiles()!!.toList())
        assertEquals(LibraryMode.OFF,store.read().required().mode)
    }
    private fun largeInput(): SkinImportInput {
        val targets = CatalogPathSet.requirePinned().paths.take(170)
        val entries = (1..3).flatMap { skin -> targets.mapIndexed { index,target ->
            RawZipFixture.Entry("Bundle/Skin$skin/$target".toByteArray(),TinyPngFixture.rgba(index+1,1))
        } }
        assertEquals(510,entries.size)
        val bytes = RawZipFixture.build(entries).bytes
        return SkinImportInput.SelectedFile("three-skins.zip") { bytes.inputStream() }
    }
    @Test fun `accepted 520 node preparation cancels with unchanged production deletion bound`() {
        PinnedCatalogFixture.load()
        val paths = SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })
        // Fast preparation retains the real normalizer; every deletion uses the production bound.
        val fast = FastSkinFileSystem(); val production = AndroidSkinFileSystem()
        val fs = object : SkinFileSystem by fast, SkinFileSystemSecurity by fast, SkinFileSystemBoundedListing by fast {
            override fun deleteContained(file: File,owner: File) = production.deleteContained(file,owner)
        }
        val store = SkinLibraryStore(paths,fs); val importer = SkinLibraryImporter(store,decoder)
        val handle = importer.prepare(largeInput()).required()
        assertEquals(3,handle.candidates.size); assertTrue(handle.candidates.all { it.code == SkinImportCode.OK })
        val owner = File(paths.importHandles,handle.handleId.toString())
        assertEquals(520,owner.walkTopDown().count())
        val unrelated = File(paths.importHandles,"unrelated").apply { writeText("keep") }
        val result = importer.cancel(handle.handleId)
        assertTrue(result.toString(),result is SkinResult.Ok)
        assertFalse(owner.exists()); assertEquals("keep",unrelated.readText())
        assertTrue(store.read().required().packs.isEmpty())
    }
    @Test fun `interrupted candidate cleanup makes bounded progress and retries incomplete owner safely`() {
        PinnedCatalogFixture.load()
        val paths = SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() })
        val fast = FastSkinFileSystem(); val production = AndroidSkinFileSystem()
        var armed = false; var interrupted = false
        val fs = object : SkinFileSystem by fast, SkinFileSystemSecurity by fast, SkinFileSystemBoundedListing by fast {
            override fun deleteContained(file: File,owner: File) {
                if (armed && file.name == "candidate-001") {
                    armed = false; interrupted = true; throw java.io.IOException("between bounded cleanup steps")
                }
                production.deleteContained(file,owner)
            }
        }
        val store = SkinLibraryStore(paths,fs); val importer = SkinLibraryImporter(store,decoder)
        val handle = importer.prepare(largeInput()).required()
        val owner = File(paths.importHandles,handle.handleId.toString())
        assertEquals(520,owner.walkTopDown().count())
        val unrelated = File(paths.importHandles,"unrelated").apply { mkdirs() }
        val sentinel = File(unrelated,"keep").apply { writeText("unrelated bytes") }; armed = true
        assertTrue(importer.cancel(handle.handleId) is SkinResult.Error)
        assertTrue("Cancellation must reach bounded candidate steps",interrupted)
        assertTrue(owner.walkTopDown().count() in 1 until 520)
        assertFalse(owner.walkTopDown().any { it.name == "candidate-000" })
        assertTrue("Partially cancelled preparations cannot publish",importer.commitImport(handle.handleId) is SkinResult.Error)
        importer.cancel(handle.handleId).required()
        assertFalse(owner.exists()); assertEquals("unrelated bytes",sentinel.readText())
        val next = importer.prepare(input()).required(); importer.cancel(next.handleId).required()
        assertEquals(listOf(unrelated),paths.importHandles.listFiles()!!.toList())
        assertTrue(store.read().required().packs.isEmpty())
    }
    @Test fun `invalid archive leaves OFF configuration and no installed entries`() {
        val store = store(); val importer = SkinLibraryImporter(store, decoder)
        val result = importer.prepare(SkinImportInput.SelectedFile("broken.zip") { "not zip".byteInputStream() })
        assertTrue(result is SkinResult.Error); assertTrue(store.read().required().packs.isEmpty())
    }
}
