package dev.silksong.launcher.runtime

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import dev.silksong.launcher.skins.catalog.*
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.library.*
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

/** Bounded host transport fixture, not Android JNI or live-device proof. The feedback option
 * accepts only actual outcomes exported by the C# production owner/shared-session contract. */
class SkinProductionTransportTest {
    @get:Rule val temporary = TemporaryFolder()
    private fun store(profile: String): SkinLibraryStore {
        val owner = SkinCatalogProfiles.require(profile)
        val asset = listOf(File("../../../docs/superpowers/specs/data/${owner.assetName}"),
            File("../../docs/superpowers/specs/data/${owner.assetName}"), File("docs/superpowers/specs/data/${owner.assetName}")).first(File::isFile)
        val catalog = asset.inputStream().use { SkinCatalogPaths.load(owner, it) }.required()
        return SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/$profile").apply { mkdirs() }), FastSkinFileSystem(), catalog)
    }
    private fun import(store: SkinLibraryStore, name: String): LibraryPack {
        val path = if (store.profileId == "silksong") "Assets/Collections/Hornet Cln Data/atlas0.png" else "Knight.png"
        val archive = RawZipFixture.build(listOf(RawZipFixture.Entry("$name/$path".toByteArray(), TinyPngFixture.rgba(if (store.profileId == "silksong") 2048 else 1, if (store.profileId == "silksong") 2048 else 1)))).bytes
        val importer = SkinLibraryImporter(store, PngDecoder { _, info -> SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong()*info.height)) })
        val handle = importer.prepare(SkinImportInput.SelectedFile("$name.zip") { archive.inputStream() }).required()
        assertTrue(handle.candidates.toString(), handle.candidates.all { it.candidateKey != null })
        importer.commitImport(handle.handleId).required()
        return store.read().required().packs.single { it.name == name }
    }
    @Test fun `production native mutation access emits requests and accepts only current actual owner feedback`() {
        val feedbackFile = System.getenv("SKIN_RUNTIME_FEEDBACK_INPUT")?.let(::File)
        val feedback = feedbackFile?.let { JsonParser.parseString(it.readText()).asJsonArray }
        val fixtures = JsonArray()
        var acceptedOutcomes = 0
        for (profile in listOf("hollow-knight", "silksong")) {
            val store = store(profile); val a = import(store, "A"); val b = import(store, "B")
            val access = SkinLibraryRuntimeAccess(store)
            val outcomes = feedback?.single { it.asJsonObject["profile"].asString == profile }?.asJsonObject
            fun accept(name: String, request: JsonObject, pack: LibraryPack?, slot: Int = 0) {
                if (outcomes == null) return
                val outcome = requireNotNull(outcomes[name]) { "Missing actual production outcome $profile/$name" }.asJsonObject
                assertEquals(request["configSha256"], outcome["configSha256"])
                assertEquals(slot, outcome["saveSlot"].asInt)
                assertEquals(if (pack != null) "Applied" else if (name == "otherSaveDefault") "Unchanged" else "Restored", outcome["status"].asString)
                assertEquals(pack?.id.orEmpty(), outcome["activePackId"].asString)
                assertEquals(pack?.treeSha256.orEmpty(), outcome["activeTreeSha256"].asString)
                val previous = store.read().required().saveAffinities
                assertTrue(access.report(outcome["configSha256"].asString, pack?.id.orEmpty(), pack?.treeSha256.orEmpty(),
                    outcome["status"].asString, "same production loaded owner feedback", slot))
                assertEquals((previous.filterNot { it.slot == slot } + SaveSkinAffinity(slot, pack?.id, pack?.treeSha256)).sortedBy { it.slot },
                    store.read().required().saveAffinities)
                acceptedOutcomes++
            }
            fun wire(slot: Int = 0) = JsonParser.parseString(access.readConfiguration(slot)).asJsonObject
            fun fixtureCopy(value: JsonObject) = value.deepCopy().apply { if (has("root")) addProperty("root", "HOST_OBJECT_ROOT") }
            var current = wire()
            assertEquals(0, current["saveSlot"].asInt)
            val initialConfig = current["configSha256"].asString
            assertFalse(access.confirmPack(if (profile == "silksong") "hollow-knight" else "silksong", initialConfig, a.id))
            assertTrue(access.confirmPack(profile, initialConfig, a.id))
            current = wire()
            assertTrue(store.read().required().saveAffinities.isEmpty())
            assertTrue(access.setMode(profile, current["configSha256"].asString, "ON"))
            val wireA = wire()
            assertTrue(store.read().required().saveAffinities.isEmpty()) // intent is never visible apply
            accept("a", wireA, a)
            assertTrue(access.confirmPack(profile, wireA["configSha256"].asString, b.id))
            val wireB = wire()
            if (feedback != null) assertEquals(a.id, store.read().required().saveAffinities.single().packId)
            assertFalse(access.report(wireA["configSha256"].asString, a.id, a.treeSha256, "Applied", "stale configuration", 0))
            accept("b", wireB, b)
            assertTrue(access.setMode(profile, wireB["configSha256"].asString, "OFF"))
            val wireDefault = wire()
            if (feedback == null) assertTrue(store.read().required().saveAffinities.isEmpty())
            else assertEquals(b.id, store.read().required().saveAffinities.single().packId)
            accept("default", wireDefault, null)
            val otherDefault = wire(1)
            assertEquals("OFF", otherDefault["mode"].asString)
            accept("otherSaveDefault", otherDefault, null, 1)
            assertTrue(access.confirmPack(profile, otherDefault["configSha256"].asString, a.id))
            assertTrue(access.setMode(profile, wire(1)["configSha256"].asString, "ON"))
            val otherImported = wire(1)
            accept("otherSaveImported", otherImported, a, 1)
            fixtures.add(JsonObject().apply {
                addProperty("profile", profile); addProperty("initialConfig", initialConfig)
                addProperty("aId", a.id); addProperty("bId", b.id)
                add("a", fixtureCopy(wireA)); add("b", fixtureCopy(wireB)); add("default", fixtureCopy(wireDefault))
                add("otherSaveDefault", fixtureCopy(otherDefault)); add("otherSaveImported", fixtureCopy(otherImported))
                add("payloads", JsonObject().apply { for (request in listOf(wireA, wireB)) {
                    for (entry in request["textures"].asJsonArray) {
                        val path = entry.asJsonObject["path"].asString
                        addProperty(path, java.util.Base64.getEncoder().encodeToString(File(request["root"].asString, path).readBytes()))
                    }
                } })
            })
            if (feedback != null) {
                val reopened = SkinLibraryStore(store.paths, store.fs, store.catalog)
                reopened.startRuntime().required(); reopened.bindSave(0).required()
                assertEquals(LibraryMode.OFF, reopened.read().required().mode)
                assertNull(reopened.read().required().selectedPackId)
                reopened.bindSave(1).required()
                assertEquals(a.id, reopened.read().required().selectedPackId)
            }
        }
        if (feedback != null) {
            assertEquals(10, acceptedOutcomes)
            println("Actual production outcomes durably accepted: $acceptedOutcomes across two profiles and two slots each")
        }
        System.getenv("SKIN_RUNTIME_FIXTURE_OUTPUT")?.let { File(it).writeText(fixtures.toString()) }
    }
}
