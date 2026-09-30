package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.catalog.SkinCatalogPaths
import dev.silksong.launcher.skins.catalog.SkinCatalogProfiles
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SkinSaveAffinityTest {
    @get:Rule val temporary = TemporaryFolder()
    private fun store(profile: String): SkinLibraryStore {
        val owner = if (profile == "silksong") SkinCatalogProfiles.Silksong else SkinCatalogProfiles.HollowKnight
        val file = listOf(File("../../../docs/superpowers/specs/data/${owner.assetName}"),
            File("../../docs/superpowers/specs/data/${owner.assetName}"), File("docs/superpowers/specs/data/${owner.assetName}")).first(File::isFile)
        val catalog = file.inputStream().use { SkinCatalogPaths.load(owner, it) }.required()
        return SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/$profile").apply { mkdirs() }), FastSkinFileSystem(), catalog)
    }
    private fun import(store: SkinLibraryStore, name: String): LibraryPack {
        val target = if (store.profileId == "silksong") "Assets/Collections/Hornet Cln Data/atlas0.png" else "Knight.png"
        val bytes = RawZipFixture.build(listOf(RawZipFixture.Entry("$name/$target".toByteArray(), TinyPngFixture.rgba(if (store.profileId == "silksong") 2048 else 1, if (store.profileId == "silksong") 2048 else 1)))).bytes
        val importer = SkinLibraryImporter(store, PngDecoder { _, info -> SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong()*info.height)) })
        val prepared = importer.prepare(SkinImportInput.SelectedFile("$name.zip") { bytes.inputStream() }).required()
        assertTrue(prepared.candidates.toString(), prepared.candidates.all { it.candidateKey != null })
        importer.commitImport(prepared.handleId).required()
        return store.read().required().packs.single { it.name == name }
    }
    private fun confirm(store: SkinLibraryStore, slot: Int, pack: LibraryPack?) {
        val document = store.read().required()
        assertTrue(store.recordObservation(store.configurationIdentity(document), pack?.id.orEmpty(), pack?.treeSha256.orEmpty(),
            if (pack == null) "Restored" else "Applied", "visible", slot))
    }
    @Test fun `schema one migration never invents a confirmed save and schema two is bounded`() {
        for (profile in listOf("hollow-knight", "silksong")) {
            val legacy = """{"schemaVersion":1,"profileId":"$profile","mode":"OFF","selectedPackId":null,"packs":[],"eligiblePackIds":[]}""".toByteArray()
            val migrated = SkinLibraryCodec.decode(legacy, profile)
            assertTrue(migrated.saveAffinities.isEmpty())
            assertNull(migrated.activeSaveSlot)
            val current = migrated.copy(activeSaveSlot = 1, saveAffinities = listOf(SaveSkinAffinity(1, null), SaveSkinAffinity(2, null)))
            assertEquals(current, SkinLibraryCodec.decode(SkinLibraryCodec.encode(current, profile), profile))
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.encode(current.copy(saveAffinities = listOf(SaveSkinAffinity(5, null))), profile) }
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.encode(current.copy(saveAffinities = listOf(SaveSkinAffinity(1, null), SaveSkinAffinity(1, null))), profile) }
        }
    }
    @Test fun `valid ON legacy registry migrates as unbound intent and survives first loaded slot without fabricated affinity`() {
        val store = store("hollow-knight"); val a = import(store, "A")
        val manifest = store.requireVerified(a)
        val registry = SkinRegistryStore(store.paths.root, store.quota, store.fs)
        val genesis = registry.recover().required()
        val id = java.util.UUID.randomUUID().toString()
        val old = genesis.document.copy(generationId = id, sequence = 1, parentGenerationId = genesis.generationId,
            operationId = id, writer = "test", packs = listOf(RegistryPack(a.id, a.name, a.author, a.candidateKey,
                a.treeSha256, manifest.contentSha256, a.receiptSha256, false)),
            activation = genesis.document.activation.copy(mode = SkinMode.ON, selectedPackId = a.id,
                active = ActiveVisual.Pack(a.id, a.treeSha256, manifest.contentSha256, a.receiptSha256), skinStamp = 1))
        // Seed only legacy skin configuration using the existing canonical/pointer fixture seams.
        val bytes = SkinRegistryDocumentCodec.canonical(old).required()
        val sha = dev.silksong.launcher.skins.documents.SkinIdentity.sha256(bytes)
        val pointer = RegistryPointer(1, id, sha)
        val root = File(store.paths.root, "registry")
        val generation = File(root, "generations/${pointer.directoryName}").apply { mkdirs() }
        File(generation, "registry.json").writeBytes(bytes)
        File(generation, "registry.sha256").writeText("$sha\n")
        File(generation, ".complete").writeBytes(byteArrayOf())
        File(root, "current").writeBytes(RegistryPointerCodec.canonical(pointer))
        assertEquals(SkinMode.ON, registry.snapshotForLibrary().required().document.activation.mode)
        val before = root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() }
        for (name in listOf("library.json", "library.backup.json")) assertTrue(File(store.paths.root, name).delete())
        val migrated = store.read().required()
        assertEquals(LibraryMode.ON, migrated.mode); assertTrue(migrated.saveAffinities.isEmpty())
        store.startRuntime().required()
        val admitted = store.bindSave(0).required()
        assertEquals(LibraryMode.ON, admitted.mode); assertEquals(a.id, admitted.selectedPackId)
        assertFalse(admitted.unboundSelectionPending); assertTrue(admitted.saveAffinities.isEmpty())
        assertEquals(before, root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() })
        confirm(store, 0, a)
        assertEquals(LibraryMode.OFF, store.bindSave(2).required().mode)
        assertEquals(listOf(SaveSkinAffinity(0, a.id, a.treeSha256)), store.read().required().saveAffinities)
    }

    @Test fun `two real saves in each profile restore only confirmed imported or true default across reopen`() {
        for (profile in listOf("hollow-knight", "silksong")) {
            val store = store(profile); val a = import(store, "A"); val b = import(store, "B")
            store.bindSave(1).required(); store.enable(a.id).required(); confirm(store, 1, a)
            store.bindSave(2).required(); assertEquals(LibraryMode.OFF, store.read().required().mode)
            store.enable(b.id).required(); confirm(store, 2, b)
            val reopened = SkinLibraryStore(store.paths, store.fs, store.catalog)
            reopened.startRuntime().required(); reopened.bindSave(1).required()
            assertEquals(a.id, reopened.read().required().selectedPackId)
            reopened.bindSave(2).required(); assertEquals(b.id, reopened.read().required().selectedPackId)
            reopened.recoverOff().required(); confirm(reopened, 2, null)
            reopened.bindSave(1).required(); assertEquals(a.id, reopened.read().required().selectedPackId)
            reopened.bindSave(2).required(); assertEquals(LibraryMode.OFF, reopened.read().required().mode)
            assertNull(reopened.read().required().selectedPackId)
        }
    }
    @Test fun `intent failed apply restore failure and stale save or config cannot replace affinity`() {
        val store = store("hollow-knight"); val a = import(store, "A"); val b = import(store, "B")
        store.bindSave(1).required(); store.enable(a.id).required(); confirm(store, 1, a)
        store.enable(b.id).required(); val request = store.read().required(); val config = store.configurationIdentity(request)
        assertEquals(a.id, request.saveAffinities.single().packId)
        assertTrue(store.recordObservation(config, a.id, a.treeSha256, "Failed", "failure", 1))
        assertEquals(a.id, store.read().required().saveAffinities.single().packId)
        store.recoverOff().required(); val off = store.read().required()
        assertTrue(store.recordObservation(store.configurationIdentity(off), a.id, a.treeSha256, "RestoreFailed", "failure", 1))
        assertEquals(a.id, store.read().required().saveAffinities.single().packId)
        store.bindSave(2).required()
        assertFalse(store.recordObservation(config, b.id, b.treeSha256, "Applied", "stale", 1))
        assertFalse(store.recordObservation(store.configurationIdentity(store.read().required()), "", "", "Restored", "wrong slot", 1))
        store.bindSave(1).required(); assertEquals(a.id, store.read().required().selectedPackId)
    }
    @Test fun `launcher unbound intent reaches first admitted save once but never fabricates or leaks failed affinity`() {
        for (profile in listOf("hollow-knight", "silksong")) {
            val store = store(profile); val a = import(store, "A")
            store.enable(a.id).required()
            assertTrue(store.read().required().unboundSelectionPending)
            assertTrue(store.read().required().saveAffinities.isEmpty())
            store.startRuntime().required(); val admitted = store.bindSave(0).required()
            assertEquals(LibraryMode.ON, admitted.mode); assertEquals(a.id, admitted.selectedPackId)
            assertFalse(admitted.unboundSelectionPending)
            assertTrue(store.recordObservation(store.configurationIdentity(admitted), "", "", "Failed", "apply failed", 0))
            assertTrue(store.read().required().saveAffinities.isEmpty())
            val other = store.bindSave(2).required()
            assertEquals(LibraryMode.OFF, other.mode); assertNull(other.selectedPackId)
            assertTrue(other.saveAffinities.isEmpty())
            store.bindSave(-1).required(); store.enable(a.id).required(); store.bindSave(0).required(); confirm(store, 0, a)
            val reopened = SkinLibraryStore(store.paths, store.fs, store.catalog)
            reopened.startRuntime().required(); assertEquals(a.id, reopened.bindSave(0).required().selectedPackId)
        }
    }

    @Test fun `affinity cannot commit before durable configuration publication and recovery is current only`() {
        val store = store("hollow-knight"); val a = import(store, "A"); val b = import(store, "B")
        store.bindSave(0).required(); store.enable(a.id).required(); confirm(store, 0, a)
        store.enable(b.id).required(); val request = store.read().required(); val config = store.configurationIdentity(request)
        val base = store.fs as FastSkinFileSystem
        var fault = true
        val fs = object : dev.silksong.launcher.skins.storage.SkinFileSystem by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemSecurity by base {
            override fun atomicMove(source: File, target: File) {
                if (target.name == "library.json" && fault) { fault = false; throw java.io.IOException("durability fixture fault") }
                base.atomicMove(source, target)
            }
        }
        val faulting = SkinLibraryStore(store.paths, fs, store.catalog)
        assertFalse(faulting.recordObservation(config, b.id, b.treeSha256, "Applied", "visible but not durable", 0))
        assertEquals(a.id, store.read().required().saveAffinities.single().packId)
        assertEquals("STALE", store.menuEvidence(store.read().required()).state)
        assertTrue(faulting.recordObservation(config, b.id, b.treeSha256, "Applied", "current visuals recovered", 0))
        assertEquals(b.id, store.read().required().saveAffinities.single().packId)
        assertEquals("TERMINAL", store.menuEvidence(store.read().required()).state)
        val restarted = store.startRuntime().required()
        assertNotEquals(config, store.configurationIdentity(restarted))
        assertFalse(store.recordObservation(config, b.id, b.treeSha256, "Applied", "old process evidence", 0))
        store.bindSave(0).required(); assertEquals(b.id, store.read().required().selectedPackId)
    }

    @Test fun `failed target corruption and removed active identity remain truthful without changing confirmed affinity`() {
        val store = store("hollow-knight"); val a = import(store, "A"); val b = import(store, "B")
        store.bindSave(0).required(); store.enable(a.id).required(); confirm(store, 0, a)
        store.enable(b.id).required(); val request = store.read().required(); val config = store.configurationIdentity(request)
        File(store.paths.objectRoot(b.treeSha256), "pack/skin.json").appendText("corrupt")
        assertTrue(store.recordObservation(config, a.id, a.treeSha256, "Failed", "target corrupt", 0))
        assertEquals("Failed", store.menuEvidence(request).observation?.get("status")?.asString)
        assertFalse(store.recordObservation(config, b.id, b.treeSha256, "Applied", "cannot confirm corrupt target", 0))
        assertEquals(a.id, store.read().required().saveAffinities.single().packId)
        store.recoverOff().required(); store.remove(a.id).required(); val removed = store.read().required()
        assertTrue(store.recordObservation(store.configurationIdentity(removed), a.id, a.treeSha256, "RestoreFailed", "old active identity retained", 0))
        assertEquals("RestoreFailed", store.menuEvidence(removed).observation?.get("status")?.asString)
        assertTrue(store.read().required().saveAffinities.isEmpty())
    }

    @Test fun `corrupt remembered choice admits typed slot without confirming it and native default recovers only that slot`() {
        for (profile in listOf("hollow-knight", "silksong")) {
            val store = store(profile); val a = import(store, "A"); val b = import(store, "B")
            store.bindSave(0).required(); store.enable(a.id).required(); confirm(store, 0, a)
            store.bindSave(1).required(); store.enable(b.id).required(); confirm(store, 1, b)
            store.bindSave(0).required()
            val confirmed = store.read().required().saveAffinities
            File(store.paths.objectRoot(b.treeSha256), "pack/skin.json").appendText("corrupt")
            val access = dev.silksong.launcher.runtime.SkinLibraryRuntimeAccess(store)
            val rejected = com.google.gson.JsonParser.parseString(access.readConfiguration(1)).asJsonObject
            assertFalse(rejected["ok"].asBoolean)
            assertTrue(rejected["detail"].asString.isNotEmpty())
            assertEquals(1, store.read().required().activeSaveSlot)
            assertEquals(confirmed, store.read().required().saveAffinities)
            val menu = com.google.gson.JsonParser.parseString(access.readMenuSnapshot(profile)).asJsonObject
            assertTrue(access.setMode(profile, menu["configSha256"].asString, "OFF"))
            val recovered = com.google.gson.JsonParser.parseString(access.readConfiguration(1)).asJsonObject
            assertTrue(recovered.toString(), recovered["ok"].asBoolean)
            assertEquals("OFF", recovered["mode"].asString)
            assertTrue(access.report(recovered["configSha256"].asString, "", "", "Restored", "current real default", 1))
            assertEquals(listOf(SaveSkinAffinity(0, a.id, a.treeSha256), SaveSkinAffinity(1, null)), store.read().required().saveAffinities)
            assertEquals(a.id, store.bindSave(0).required().selectedPackId)
        }
    }

    @Test fun `normal death successor commits once to bound save and slot change cancels frozen work`() {
        val store = store("hollow-knight"); val a = import(store, "A"); val b = import(store, "B")
        store.bindSave(1).required(); store.enable(a.id).required(); confirm(store, 1, a)
        store.setEligibility(a.id, true).required(); store.setEligibility(b.id, true).required()
        store.setMode(store.configurationIdentity(store.read().required()), LibraryMode.ROTATE).required()
        val run = requireNotNull(store.read().required().rotationRun)
        val next = store.confirmDeath(run, 1).required()
        assertTrue(store.recordRotationObservation(store.configurationIdentity(next), run, 1, b.id, b.treeSha256, "Applied", "visible", 1))
        assertEquals(b.id, store.read().required().saveAffinities.single().packId)
        val vanilla = store.confirmDeath(run, 2).required(); assertTrue(vanilla.pendingVanilla)
        assertTrue(store.recordRotationObservation(store.configurationIdentity(vanilla), run, 2, "", "", "Restored", "vanilla", 1))
        assertNull(store.read().required().saveAffinities.single().packId)
        val pending = store.confirmDeath(run, 3).required()
        store.bindSave(2).required()
        assertTrue(store.confirmDeath(run, 4) is SkinResult.Error)
        assertFalse(store.recordRotationObservation(store.configurationIdentity(pending), run, 3, a.id, a.treeSha256, "Applied", "retired", 1))
        assertEquals(listOf(SaveSkinAffinity(1, null)), store.read().required().saveAffinities)
    }
}
