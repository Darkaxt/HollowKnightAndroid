package dev.silksong.launcher.runtime

import com.google.gson.JsonParser
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.catalog.SkinCatalogPaths
import dev.silksong.launcher.skins.catalog.SkinCatalogProfiles
import dev.silksong.launcher.skins.library.*
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.fixtures.*
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
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

    @Test fun `ordinary default restore writes one bounded correlated redacted observation`() {
        PinnedCatalogFixture.load()
        val store = SkinLibraryStore(SkinPaths(File(temporary.root,"profiles/hollow-knight").apply { mkdirs() }))
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val config = wire["configSha256"].asString
        val configuration = File(store.paths.root,"library.json"); val original = configuration.readBytes().toList()

        assertTrue(access.report(config,"","","Restored",
            "drive C:\\Users\\Private Person\\skin.png\nunc \\\\server\\private\\skin.png\nunix /home/private/skin.png"))

        assertEquals(original,configuration.readBytes().toList())
        val report = File(store.paths.root,"library.observation.json")
        assertTrue(report.length() < 16384)
        val observation = JsonParser.parseString(report.readText()).asJsonObject
        assertEquals(setOf(
            "schemaVersion", "profileId", "featureId", "operationId", "operationGeneration",
            "operationKind", "rotationRun", "requestConfigSha256", "resultingConfigSha256",
            "resolvedKind", "resolvedPackId", "resolvedTreeSha256", "resolvedReceiptSha256",
            "activeKind", "activePackId", "activeTreeSha256", "activeReceiptSha256",
            "status", "detail", "observedAtMillis",
        ), observation.keySet())
        assertEquals(1, observation["schemaVersion"].asInt)
        assertEquals("hollow-knight", observation["profileId"].asString)
        assertEquals("SKIN-LIVE-DEFAULT", observation["featureId"].asString)
        assertEquals(config, observation["operationId"].asString)
        assertEquals(0L, observation["operationGeneration"].asLong)
        assertEquals("RESTORE_DEFAULT", observation["operationKind"].asString)
        assertEquals("", observation["rotationRun"].asString)
        assertEquals(config, observation["requestConfigSha256"].asString)
        assertEquals(config, observation["resultingConfigSha256"].asString)
        assertEquals("DEFAULT", observation["resolvedKind"].asString)
        assertEquals("", observation["resolvedPackId"].asString)
        assertEquals("DEFAULT", observation["activeKind"].asString)
        assertEquals("Restored", observation["status"].asString)
        assertTrue(observation["detail"].asString.contains("[REDACTED_PATH]"))
        for (secret in listOf("Private Person", "server", "private", "home"))
            assertFalse(observation["detail"].asString.contains(secret, ignoreCase = true))
        assertTrue(observation["detail"].asString.length <= 1024)
        val first = report.readBytes().toList()
        assertTrue(access.report(config,"","","Restored", observation["detail"].asString))
        assertEquals("An identical report must not rewrite the bounded latest observation", first, report.readBytes().toList())
        assertFalse(access.report("0".repeat(64),"","","Restored","stale"))
        assertEquals(first,report.readBytes().toList()); assertEquals(original,configuration.readBytes().toList())
        assertFalse(access.report(config,"","","BOGUS","invalid status"))
    }

    @Test fun `ordinary imported apply derives feature target and verified immutable receipt`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val pack = store.read().required().packs.single { it.id == wire["packId"].asString }

        assertTrue(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256, "Applied", "live apply complete"))

        val observation = observation(store)
        assertEquals("SKIN-LIVE-IMPORTED", observation["featureId"].asString)
        assertEquals("APPLY_IMPORTED", observation["operationKind"].asString)
        assertEquals("IMPORTED", observation["resolvedKind"].asString)
        assertEquals(pack.id, observation["resolvedPackId"].asString)
        assertEquals(pack.treeSha256, observation["resolvedTreeSha256"].asString)
        assertEquals(pack.receiptSha256, observation["resolvedReceiptSha256"].asString)
        assertEquals("IMPORTED", observation["activeKind"].asString)
        assertEquals(pack.receiptSha256, observation["activeReceiptSha256"].asString)
        assertTrue(store.receipts.verify(observation["resolvedReceiptSha256"].asString) is SkinResult.Ok)
        assertFalse(store.paths.root.walkTopDown().any {
            it.isFile && (it.name.contains("journal", true) || it.name.contains("history", true) || it.name.contains("events", true))
        })

        File(store.paths.importReceiptRoot(pack.receiptSha256), "import-receipt.json").appendText("corrupt")
        val before = File(store.paths.root, "library.observation.json").readBytes().toList()
        assertFalse(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256, "Applied", "must verify receipt again"))
        assertEquals(before, File(store.paths.root, "library.observation.json").readBytes().toList())
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
        val run = first["rotationRun"].asString
        val before = store.read().required()
        val original = requireNotNull(before.selectedPackId)
        val originalPack = before.packs.single { it.id == original }
        val pending = store.confirmDeath(run,1).required(); val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertEquals(pending.pendingPackId,wire["packId"].asString); assertEquals(1L,wire["pendingOccurrence"].asLong)
        val config = wire["configSha256"].asString; val id = wire["packId"].asString; val tree = wire["treeSha256"].asString

        assertTrue(access.reportRotation(config,run,1,originalPack.id,originalPack.treeSha256,"AwaitingTargets","awaiting live targets"))
        assertEquals(original,store.read().required().selectedPackId)
        var evidence = observation(store)
        assertEquals("SKIN-DEATH-ROTATION", evidence["featureId"].asString)
        assertEquals("ROTATE_IMPORTED", evidence["operationKind"].asString)
        assertEquals(config, evidence["operationId"].asString)
        assertEquals(1L, evidence["operationGeneration"].asLong)
        assertEquals(run, evidence["rotationRun"].asString)
        assertEquals(id, evidence["resolvedPackId"].asString)
        assertEquals(pending.packs.single { it.id == id }.receiptSha256, evidence["resolvedReceiptSha256"].asString)
        assertEquals(originalPack.id, evidence["activePackId"].asString)
        assertEquals(config, evidence["resultingConfigSha256"].asString)

        assertTrue(access.reportRotation(config,run,1,originalPack.id,originalPack.treeSha256,"Failed","retry same"))
        assertEquals(original,store.read().required().selectedPackId)
        assertEquals("Failed", observation(store)["status"].asString)
        val failed = File(store.paths.root, "library.observation.json").readBytes().toList()
        assertFalse(access.reportRotation(config,"0".repeat(32),1,id,tree,"Applied","stale run"))
        assertFalse(access.reportRotation(config,run,2,id,tree,"Applied","stale occurrence"))
        assertEquals(failed, File(store.paths.root, "library.observation.json").readBytes().toList())
        java.nio.channels.FileChannel.open(File(store.paths.root,"library.lock").toPath(),java.nio.file.StandardOpenOption.WRITE).use { ch ->
            ch.lock().use { assertFalse(access.reportRotation(config,run,1,id,tree,"Applied","busy")) }
        }
        assertEquals(pending.pendingPackId,store.read().required().pendingPackId)

        assertTrue(access.reportRotation(config,run,1,id,tree,"Applied","committed"))
        val committed = store.read().required()
        assertEquals(id,committed.selectedPackId)
        assertNull(committed.pendingPackId)
        evidence = observation(store)
        assertEquals(config, evidence["requestConfigSha256"].asString)
        assertEquals(store.configurationIdentity(committed), evidence["resultingConfigSha256"].asString)
        assertEquals("Applied", evidence["status"].asString)
        assertEquals(id, evidence["activePackId"].asString)
        assertEquals(pending.packs.single { it.id == id }.receiptSha256, evidence["activeReceiptSha256"].asString)
        assertNull(store.confirmDeath(run,1).required().pendingPackId)
    }

    @Test fun `accepted rotation report retries evidence publication after durable successor commit`() {
        val fs = FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }
        val store = importedStore(fs)
        val access = SkinLibraryRuntimeAccess(store)
        val first = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val run = first["rotationRun"].asString
        val pending = store.confirmDeath(run, 1).required()
        val target = pending.packs.single { it.id == pending.pendingPackId }
        val config = store.configurationIdentity(pending)
        var rejectObservation = true
        fs.beforeContainment = { path, _, _ ->
            if (rejectObservation && path.name == "library.observation.json") {
                rejectObservation = false
                throw IllegalStateException("injected observation publication failure")
            }
        }

        assertFalse(access.reportRotation(config, run, 1, target.id, target.treeSha256, "Applied", "committed"))
        val committed = store.read().required()
        assertEquals(target.id, committed.selectedPackId)
        assertNull(committed.pendingPackId)
        assertFalse(File(store.paths.root, "library.observation.json").exists())

        fs.beforeContainment = null
        assertTrue(access.reportRotation(config, run, 1, target.id, target.treeSha256, "Applied", "committed"))
        val evidence = observation(store)
        assertEquals(config, evidence["requestConfigSha256"].asString)
        assertEquals(store.configurationIdentity(committed), evidence["resultingConfigSha256"].asString)
        assertEquals(target.receiptSha256, evidence["resolvedReceiptSha256"].asString)
    }

    @Test fun `identical uncertain evidence retries file and directory durability before acceptance`() {
        PinnedCatalogFixture.load()
        val fs = FaultingSkinFileSystem(FastSkinFileSystem()).apply { skipPhysicalSyncs = true }
        val store = SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/hollow-knight").apply { mkdirs() }), fs)
        val access = SkinLibraryRuntimeAccess(store)
        val config = JsonParser.parseString(access.readConfiguration()).asJsonObject["configSha256"].asString
        val report = File(store.paths.root, "library.observation.json")
        val directoryEvent = "sync-dir:${store.paths.root.name}"
        fs.afterMove = { _, target ->
            if (target == report) {
                fs.failOnEvent = directoryEvent
                fs.failOnOccurrence = fs.events.count { it == directoryEvent } + 1
            }
        }
        assertFalse(access.report(config, "", "", "Restored", "complete"))
        assertTrue(report.isFile) // rename succeeded; publication remains uncertain
        val retained = report.readBytes().toList()
        fs.afterMove = null
        fs.failOnEvent = "sync-file:library.observation.json"
        fs.failOnOccurrence = fs.events.count { it == fs.failOnEvent } + 1
        assertFalse(access.report(config, "", "", "Restored", "complete"))
        assertEquals(retained, report.readBytes().toList())

        // The configuration read has its own barrier; fail the subsequent evidence barrier.
        fs.failOnEvent = directoryEvent
        fs.failOnOccurrence = fs.events.count { it == directoryEvent } + 2
        assertFalse(access.report(config, "", "", "Restored", "complete"))
        fs.failOnEvent = null
        fs.events.clear()
        assertTrue(access.report(config, "", "", "Restored", "complete"))
        assertEquals(retained, report.readBytes().toList())
        val barrier = fs.events.indexOf("sync-file:library.observation.json")
        assertTrue(barrier >= 0)
        assertEquals(directoryEvent, fs.events[barrier + 1])
    }

    @Test fun `Silksong imported and default evidence use its exact pinned catalog and receipt`() {
        val store = importedStore(FastSkinFileSystem(), silksong = true)
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertEquals("silksong", wire["profileId"].asString)
        val pack = store.read().required().packs.single { it.id == wire["packId"].asString }
        assertTrue(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256, "Applied", "live imported"))
        var evidence = observation(store)
        assertEquals("silksong", evidence["profileId"].asString)
        assertEquals("SKIN-LIVE-IMPORTED", evidence["featureId"].asString)
        assertEquals(pack.receiptSha256, evidence["resolvedReceiptSha256"].asString)
        assertEquals(pack.receiptSha256, evidence["activeReceiptSha256"].asString)
        assertTrue(store.receipts.verify(pack.receiptSha256) is SkinResult.Ok)
        assertEquals("TERMINAL", JsonParser.parseString(access.readMenuSnapshot("silksong")).asJsonObject["evidenceState"].asString)

        store.setMode(wire["configSha256"].asString, LibraryMode.OFF).required()
        val config = store.configurationIdentity(store.read().required())
        assertFalse(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256, "Applied", "stale"))
        assertTrue(access.report(config, "", "", "Restored", "live default"))
        evidence = observation(store)
        assertEquals("silksong", evidence["profileId"].asString)
        assertEquals("SKIN-LIVE-DEFAULT", evidence["featureId"].asString)
        assertEquals("RESTORE_DEFAULT", evidence["operationKind"].asString)
        assertEquals("DEFAULT", evidence["activeKind"].asString)
        assertEquals("", evidence["resolvedReceiptSha256"].asString)
        assertEquals("Restored", evidence["status"].asString)
    }

    @Test fun `Silksong rotation evidence correlates imported and default outcomes and rejects retired work`() {
        val store = importedStore(FastSkinFileSystem(), silksong = true)
        val access = SkinLibraryRuntimeAccess(store)
        val run = JsonParser.parseString(access.readConfiguration()).asJsonObject["rotationRun"].asString
        val pending = store.confirmDeath(run, 1).required()
        val target = pending.packs.single { it.id == pending.pendingPackId }
        val config = store.configurationIdentity(pending)
        assertTrue(access.reportRotation(config, run, 1, "", "", "Failed", "apply failed after restore"))
        assertEquals("Failed", observation(store)["status"].asString)
        assertTrue(access.reportRotation(config, run, 1, target.id, target.treeSha256, "Applied", "rotation complete"))
        var evidence = observation(store)
        assertEquals("silksong", evidence["profileId"].asString)
        assertEquals("SKIN-DEATH-ROTATION", evidence["featureId"].asString)
        assertEquals("ROTATE_IMPORTED", evidence["operationKind"].asString)
        assertEquals(1L, evidence["operationGeneration"].asLong)
        assertEquals(run, evidence["rotationRun"].asString)
        assertEquals(target.receiptSha256, evidence["resolvedReceiptSha256"].asString)
        assertEquals("TERMINAL", JsonParser.parseString(access.readMenuSnapshot("silksong")).asJsonObject["evidenceState"].asString)

        val toDefault = store.confirmDeath(run, 2).required()
        assertTrue(toDefault.pendingVanilla)
        assertFalse(access.reportRotation(config, run, 1, target.id, target.treeSha256, "Applied", "retired"))
        assertTrue(access.reportRotation(store.configurationIdentity(toDefault), run, 2, "", "", "Restored", "default complete"))
        evidence = observation(store)
        assertEquals("ROTATE_DEFAULT", evidence["operationKind"].asString)
        assertEquals(2L, evidence["operationGeneration"].asLong)
        assertEquals("DEFAULT", evidence["resolvedKind"].asString)
        assertEquals("DEFAULT", evidence["activeKind"].asString)
        assertEquals("", evidence["activeReceiptSha256"].asString)
        assertEquals(store.configurationIdentity(store.read().required()), evidence["resultingConfigSha256"].asString)
        assertEquals("TERMINAL", JsonParser.parseString(access.readMenuSnapshot("silksong")).asJsonObject["evidenceState"].asString)
    }

    @Test fun `cross-profile observations and stale reports cannot become current evidence`() {
        val hk = importedStore(FastSkinFileSystem())
        val ss = importedStore(FastSkinFileSystem(), silksong = true)
        val hkAccess = SkinLibraryRuntimeAccess(hk)
        val ssAccess = SkinLibraryRuntimeAccess(ss)
        val hkWire = JsonParser.parseString(hkAccess.readConfiguration()).asJsonObject
        val ssWire = JsonParser.parseString(ssAccess.readConfiguration()).asJsonObject
        for ((store, access, wire) in listOf(Triple(hk, hkAccess, hkWire), Triple(ss, ssAccess, ssWire))) {
            assertTrue(access.report(wire["configSha256"].asString, wire["packId"].asString, wire["treeSha256"].asString, "Applied", "current"))
            val foreign = if (store === hk) ssWire else hkWire
            val retained = File(store.paths.root, "library.observation.json").readBytes().toList()
            assertFalse(access.report(foreign["configSha256"].asString, foreign["packId"].asString,
                foreign["treeSha256"].asString, "Applied", "foreign"))
            assertEquals(retained, File(store.paths.root, "library.observation.json").readBytes().toList())
        }
        val hkReport = File(hk.paths.root, "library.observation.json")
        val ssReport = File(ss.paths.root, "library.observation.json")
        val hkBytes = hkReport.readBytes(); val ssBytes = ssReport.readBytes()
        hkReport.writeBytes(ssBytes); ssReport.writeBytes(hkBytes)
        assertEquals("UNREADABLE", JsonParser.parseString(hkAccess.readMenuSnapshot("hollow-knight")).asJsonObject["evidenceState"].asString)
        assertEquals("UNREADABLE", JsonParser.parseString(ssAccess.readMenuSnapshot("silksong")).asJsonObject["evidenceState"].asString)
    }

    @Test fun `matching menu evidence rejects a forged receipt identity`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val pack = store.read().required().packs.single { it.id == wire["packId"].asString }
        assertTrue(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256, "Applied", "verified"))
        val file = File(store.paths.root, "library.observation.json")
        val forged = observation(store).apply {
            addProperty("resolvedReceiptSha256", "0".repeat(64))
        }
        file.writeText(forged.toString())

        val snapshot = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("UNREADABLE", snapshot["evidenceState"].asString)
        assertTrue(snapshot["observation"].isJsonNull)
    }

    @Test fun `pending vanilla wire restores and commits default as the active rotation member`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val first = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val run = first["rotationRun"].asString
        val current = store.read().required()
        val selected = requireNotNull(current.selectedPackId)
        val other = current.packs.single { it.id != selected }
        val toOther = store.confirmDeath(run, 1).required()
        assertEquals(other.id, toOther.pendingPackId)
        assertTrue(store.finishRotation(
            store.configurationIdentity(toOther), run, 1, other.id, other.treeSha256,
        ))

        val pending = store.confirmDeath(run, 2).required()
        assertTrue(pending.pendingVanilla)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertTrue(wire["vanilla"].asBoolean)
        assertEquals(2L, wire["pendingOccurrence"].asLong)
        assertFalse(wire.has("packId"))
        assertFalse(wire.has("textures"))

        assertTrue(access.reportRotation(
            wire["configSha256"].asString, run, 2, "", "", "Restored", "default restored",
        ))
        val defaultActive = store.read().required()
        assertNull(defaultActive.selectedPackId)
        assertFalse(defaultActive.pendingVanilla)
        val evidence = observation(store)
        assertEquals("SKIN-DEATH-ROTATION", evidence["featureId"].asString)
        assertEquals("ROTATE_DEFAULT", evidence["operationKind"].asString)
        assertEquals("DEFAULT", evidence["resolvedKind"].asString)
        assertEquals("", evidence["resolvedReceiptSha256"].asString)
        assertEquals("DEFAULT", evidence["activeKind"].asString)
        assertEquals(2L, evidence["operationGeneration"].asLong)
        assertEquals(store.configurationIdentity(defaultActive), evidence["resultingConfigSha256"].asString)
        val stable = JsonParser.parseString(access.readConfiguration()).asJsonObject
        assertTrue(stable["vanilla"].asBoolean)
        assertEquals(0L, stable["pendingOccurrence"].asLong)
    }

    @Test fun `menu snapshot distinguishes pending stale matching and completed rotation evidence`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val initial = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("PENDING", initial["evidenceState"].asString)
        assertEquals(initial["configSha256"].asString, initial["operationId"].asString)
        assertEquals(0L, initial["operationGeneration"].asLong)
        assertTrue(initial["observation"].isJsonNull)

        val initialConfig = initial["configSha256"].asString
        val selected = initial["selectedPackId"].asString
        val selectedPack = store.read().required().packs.single { it.id == selected }
        assertTrue(access.report(initialConfig, selectedPack.id, selectedPack.treeSha256, "Applied", "current"))
        val matching = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("TERMINAL", matching["evidenceState"].asString)
        assertEquals(initialConfig, matching["observation"].asJsonObject["operationId"].asString)

        assertTrue(access.setSpriteScope("hollow-knight", initialConfig, SpriteScope.CHARACTER.name))
        val stale = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("STALE", stale["evidenceState"].asString)
        assertNotEquals(stale["operationId"].asString, stale["observation"].asJsonObject["operationId"].asString)

        val changed = store.read().required()
        val changedPack = changed.packs.single { it.id == changed.selectedPackId }
        assertTrue(access.report(store.configurationIdentity(changed), changedPack.id, changedPack.treeSha256,
            "AwaitingTargets", "waiting"))
        val pending = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("PENDING", pending["evidenceState"].asString)
        assertEquals("AwaitingTargets", pending["observation"].asJsonObject["status"].asString)

        val run = requireNotNull(changed.rotationRun)
        val rotation = store.confirmDeath(run, changed.lastDeath + 1).required()
        val rotationConfig = store.configurationIdentity(rotation)
        val target = rotation.packs.single { it.id == rotation.pendingPackId }
        assertTrue(access.reportRotation(rotationConfig, run, rotation.lastDeath, target.id, target.treeSha256,
            "Applied", "rotation complete"))
        val completed = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("TERMINAL", completed["evidenceState"].asString)
        assertEquals("SKIN-DEATH-ROTATION", completed["observation"].asJsonObject["featureId"].asString)
        assertEquals(completed["configSha256"].asString,
            completed["observation"].asJsonObject["resultingConfigSha256"].asString)
    }

    @Test fun `matching menu evidence rejects non-string fields and unverified receipt identities`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val pack = store.read().required().packs.single { it.id == wire["packId"].asString }
        assertTrue(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256,
            "Applied", "verified"))
        val report = File(store.paths.root, "library.observation.json")
        val valid = report.readText()

        JsonParser.parseString(valid).asJsonObject.also {
            it.addProperty("detail", 7)
            report.writeText(it.toString())
        }
        var snapshot = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("UNREADABLE", snapshot["evidenceState"].asString)

        JsonParser.parseString(valid).asJsonObject.also {
            it.addProperty("resolvedReceiptSha256", "0".repeat(64))
            report.writeText(it.toString())
        }
        snapshot = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
        assertEquals("UNREADABLE", snapshot["evidenceState"].asString)
    }

    @Test fun `matching menu evidence preserves distinct pending and failure outcomes`() {
        val store = importedStore()
        val access = SkinLibraryRuntimeAccess(store)
        val wire = JsonParser.parseString(access.readConfiguration()).asJsonObject
        val pack = store.read().required().packs.single { it.id == wire["packId"].asString }
        for ((status, state) in listOf(
            "AwaitingTargets" to "PENDING",
            "Failed" to "TERMINAL",
            "Rejected" to "TERMINAL",
            "RestoreFailed" to "TERMINAL",
        )) {
            assertTrue(access.report(wire["configSha256"].asString, pack.id, pack.treeSha256,
                status, "outcome $status"))
            val snapshot = JsonParser.parseString(access.readMenuSnapshot("hollow-knight")).asJsonObject
            assertEquals(state, snapshot["evidenceState"].asString)
            assertEquals(status, snapshot["observation"].asJsonObject["status"].asString)
        }
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
    private fun observation(store: SkinLibraryStore) = JsonParser.parseString(
        File(store.paths.root, "library.observation.json").readText(),
    ).asJsonObject

    // Evidence/identity unit tests do not need repeated host mount probes; filesystem
    // containment is covered by its own suite. Fault wrappers still assert all barriers.
    private fun importedStore(fs: SkinFileSystem = FastSkinFileSystem(), silksong: Boolean = false): SkinLibraryStore {
        val catalog = if (silksong) {
            val profile = SkinCatalogProfiles.Silksong
            val file = listOf(
                File("../../../docs/superpowers/specs/data/${profile.assetName}"),
                File("../../docs/superpowers/specs/data/${profile.assetName}"),
                File("docs/superpowers/specs/data/${profile.assetName}"),
            ).map(File::getAbsoluteFile).first(File::isFile)
            file.inputStream().use { SkinCatalogPaths.load(profile, it) }.required()
        } else PinnedCatalogFixture.load()
        val store = SkinLibraryStore(
            SkinPaths(File(temporary.root,"profiles/${catalog.profile.profileId}").apply { mkdirs() }),
            fs, catalog,
        )
        val target = if (silksong) catalog.paths.first() else "Knight.png"
        val dimensions = catalog.profile.textureDimensions[target]
        val png = TinyPngFixture.rgba(dimensions?.width ?: 1, dimensions?.height ?: 1)
        val importer = SkinLibraryImporter(store,PngDecoder { _, i -> SkinResult.Ok(DecodeResult(i.width,i.height,i.width.toLong()*i.height)) })
        for (name in listOf("Alpha","Beta")) {
            val input = SkinImportInput.SelectedFile("$name.zip") { RawZipFixture.build(listOf(RawZipFixture.Entry("$name/$target".toByteArray(),png))).bytes.inputStream() }
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
