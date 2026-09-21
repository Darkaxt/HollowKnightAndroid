package dev.silksong.launcher.runtime

import com.google.gson.JsonParser
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.library.*
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SkinLibraryRuntimeBridgeTest {
    @get:Rule val temporary = TemporaryFolder()
    @Test fun `JNI authority returns OFF for exact captured Hollow Knight owner`() {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }))
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertTrue(wire["ok"].asBoolean); assertEquals("hollow-knight",wire["profileId"].asString)
        assertEquals("OFF",wire["mode"].asString); assertEquals("ALL", wire["spriteScope"].asString)
        assertEquals(64,wire["configSha256"].asString.length)
    }
    @Test fun `runtime payload transports sprite scope independently of mode including OFF`() {
        val store = importedStore()
        var document = store.read().required()
        store.setSpriteScope(store.configurationIdentity(document), SpriteScope.CHARACTER_HUD).required()
        val access = SkinLibraryRuntimeAccess(store)

        var wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertEquals("ROTATE", wire["mode"].asString)
        assertEquals("CHARACTER_HUD", wire["spriteScope"].asString)
        assertTrue(wire.has("textures"))

        document = store.read().required()
        store.setMode(store.configurationIdentity(document), LibraryMode.OFF).required()
        wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertEquals("OFF", wire["mode"].asString)
        assertEquals("CHARACTER_HUD", wire["spriteScope"].asString)
        assertFalse(wire.has("textures"))
    }

    @Test fun `bounded last observation never writes configuration and stale observations are rejected`() {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }))
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val configuration = File(store.paths.root,"library.json"); val original = configuration.readBytes().toList()
        assertTrue(access.report(wire["configSha256"].asString,"","","Restored","x".repeat(100000)))
        assertEquals(original,configuration.readBytes().toList())
        val report = File(store.paths.root,"library.observation.json")
        assertTrue(report.length() < 16384)
        assertEquals(1024,JsonParser.parseString(report.readText()).asJsonObject["detail"].asString.length)
        val before = report.readBytes().toList()
        assertFalse(access.report("0".repeat(64),"","","Restored","stale"))
        assertEquals(before,report.readBytes().toList()); assertEquals(original,configuration.readBytes().toList())
        assertFalse(access.report(wire["configSha256"].asString,"","","BOGUS","invalid status"))
    }
    @Test fun `busy JNI read is retryable and leaves previous observation untouched`() {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }))
        val access = SkinLibraryRuntimeAccess(store); access.readConfiguration()
        val entered = java.util.concurrent.CountDownLatch(1); val release = java.util.concurrent.CountDownLatch(1)
        val thread = Thread { store.locked { entered.countDown(); check(release.await(5,java.util.concurrent.TimeUnit.SECONDS)); dev.silksong.launcher.skins.contracts.SkinResult.Ok(Unit) } }
        thread.start(); assertTrue(entered.await(5,java.util.concurrent.TimeUnit.SECONDS))
        try {
            val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
            assertFalse(wire["ok"].asBoolean); assertEquals("LIFECYCLE_BLOCKED",wire["code"].asString)
        } finally { release.countDown(); thread.join(5000) }
        assertTrue(JsonParser.parseString(access.readConfiguration()).asJsonObject["ok"].asBoolean)
    }
    @Test fun `pending wire freezes verified candidate and only successful matching report selects it`() {
        val store = importedStore(); val access = SkinLibraryRuntimeAccess(store)
        val first = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertTrue("Runtime must publish its renewed rotation run", first.has("rotationRun"))
        val run = first["rotationRun"].asString; val original = store.read().required().selectedPackId
        val pending = store.confirmDeath(run,1).required(); val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertEquals(pending.pendingPackId,wire["packId"].asString); assertEquals(1L,wire["pendingOccurrence"].asLong)
        val config = wire["configSha256"].asString; val id = wire["packId"].asString; val tree = wire["treeSha256"].asString
        assertTrue(access.reportRotation(config,run,1,id,tree,"Failed","retry same")); assertEquals(original,store.read().required().selectedPackId)
        assertFalse(access.reportRotation(config,"0".repeat(32),1,id,tree,"Applied","stale run"))
        java.nio.channels.FileChannel.open(File(store.paths.root,"library.lock").toPath(),java.nio.file.StandardOpenOption.WRITE).use { ch ->
            ch.lock().use { assertFalse(access.reportRotation(config,run,1,id,tree,"Applied","busy")) }
        }
        assertEquals(pending.pendingPackId,store.read().required().pendingPackId)
        assertTrue(access.reportRotation(config,run,1,id,tree,"Applied","")); assertEquals(id,store.read().required().selectedPackId)
        assertNull(store.confirmDeath(run,1).required().pendingPackId)
    }
    @Test fun `exact JNI death cancellation preserves and promotes newer occurrence`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val run = JsonParser.parseString(access.readConfiguration()).asJsonObject["rotationRun"].asString
        store.confirmDeath(run, 1).required()
        store.confirmDeath(run, 2).required()

        assertTrue(access.cancelDeath(run, 1))
        val promoted = store.read().required()
        assertEquals(2L, promoted.lastDeath)
        assertNotNull(promoted.pendingPackId)
        assertTrue(promoted.queuedDeathOccurrences.isEmpty())
        assertTrue(access.cancelDeath(run, 1))
    }

    @Test fun `fresh runtime drops stale work and manual selection renews run`() {
        val store = importedStore(); val access = SkinLibraryRuntimeAccess(store)
        val first = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertTrue(first.has("rotationRun")); val run = first["rotationRun"].asString
        store.confirmDeath(run,1).required(); val next = JsonParser.parseString(SkinLibraryRuntimeAccess(store).readConfiguration()).asJsonObject
        assertNotEquals(run,next["rotationRun"].asString); assertEquals(0L,next["pendingOccurrence"].asLong)
        val before = store.read().required(); store.select(requireNotNull(before.selectedPackId)).required()
        assertNotEquals(before.rotationRun,store.read().required().rotationRun)
        store.advanceMode().required(); assertEquals(LibraryMode.OFF,store.read().required().mode); assertNull(store.read().required().rotationRun)
    }
    @Test fun `launched profile rejection is distinct from retryable storage failure`() {
        GameProcessStartup.resetForTests()
        try {
            assertEquals("PROFILE_REJECTED", JsonParser.parseString(SkinLibraryRuntimeBridge.readConfiguration()).asJsonObject["code"].asString)
            GameProcessStartup.installForTests(GameProcessStartupSnapshot("silksong","generation","toolchain","pkg","native","data","unity","dex","mods"))
            assertEquals("PROFILE_REJECTED", JsonParser.parseString(SkinLibraryRuntimeBridge.readConfiguration()).asJsonObject["code"].asString)
            assertFalse(SkinLibraryRuntimeBridge.confirmDeath("a".repeat(32),1))
            assertFalse(SkinLibraryRuntimeBridge.cancelDeath("a".repeat(32),1))
            assertFalse(SkinLibraryRuntimeBridge.cancelRotation("a".repeat(32)))
            assertFalse(JsonParser.parseString(SkinLibraryRuntimeBridge.readMenuSnapshot("silksong")).asJsonObject["ok"].asBoolean)
            assertFalse(SkinLibraryRuntimeBridge.setMode("silksong", "a".repeat(64), "OFF"))
            assertFalse(SkinLibraryRuntimeBridge.setSpriteScope("silksong", "a".repeat(64), "ALL"))
            assertFalse(SkinLibraryRuntimeBridge.confirmPack("silksong", "a".repeat(64), "a"))
        } finally { GameProcessStartup.resetForTests() }
    }
    private fun importedStore(): SkinLibraryStore {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }))
        val importer = SkinLibraryImporter(store,PngDecoder { _, i -> SkinResult.Ok(DecodeResult(i.width,i.height,i.width.toLong()*i.height)) })
        for (name in listOf("Alpha","Beta")) {
            val input = SkinImportInput.SelectedFile("$name.zip") { RawZipFixture.build(listOf(RawZipFixture.Entry("$name/Knight.png".toByteArray(),TinyPngFixture.rgba()))).bytes.inputStream() }
            importer.commitImport(importer.prepare(input).required().handleId).required()
        }
        val packs = store.read().required().packs; store.select(packs[0].id).required()
        packs.forEach { store.setEligibility(it.id,true).required() }; store.advanceMode().required(); store.advanceMode().required()
        return store
    }

    @Test fun `native menu snapshot is bounded profile checked and contains exact compatible packs`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val wireText = access.readMenuSnapshot("hollow-knight")
        val wire = JsonParser.parseString(wireText).asJsonObject
        val document = store.read().required()

        assertTrue(wire["ok"].asBoolean)
        assertEquals("hollow-knight", wire["profileId"].asString)
        assertEquals(store.configurationIdentity(document), wire["configSha256"].asString)
        assertEquals(document.mode.name, wire["mode"].asString)
        assertEquals(document.spriteScope.name, wire["spriteScope"].asString)
        assertEquals(document.selectedPackId, wire["selectedPackId"].asString)
        assertEquals(document.packs.map { it.id }, wire["packs"].asJsonArray.map { it.asJsonObject["id"].asString })
        assertTrue(wireText.toByteArray().size <= SkinLibraryRuntimeAccess.MAX_MENU_SNAPSHOT_BYTES)

        val rejected = JsonParser.parseString(access.readMenuSnapshot("silksong")).asJsonObject
        assertFalse(rejected["ok"].asBoolean)
        assertEquals("PROFILE_REJECTED", rejected["code"].asString)
    }

    @Test fun `native menu mutations require profile and current configuration`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val current = store.read().required()
        val config = store.configurationIdentity(current)
        val selected = requireNotNull(current.selectedPackId)

        assertFalse(access.setMode("silksong", config, LibraryMode.ON.name))
        assertFalse(access.setSpriteScope("hollow-knight", "0".repeat(64), SpriteScope.CHARACTER.name))
        assertFalse(access.confirmPack("hollow-knight", "0".repeat(64), selected))
        assertTrue(access.setSpriteScope("hollow-knight", config, SpriteScope.CHARACTER.name))
        assertEquals(SpriteScope.CHARACTER, store.read().required().spriteScope)
    }

    @Test fun `JNI malformed authority returns error instead of fallback configuration`() {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }));store.read()
        File(store.paths.root,"library.json").writeText("bad")
        assertFalse(JsonParser.parseString(SkinLibraryRuntimeAccess(store).readConfiguration()).asJsonObject["ok"].asBoolean)
    }
}
