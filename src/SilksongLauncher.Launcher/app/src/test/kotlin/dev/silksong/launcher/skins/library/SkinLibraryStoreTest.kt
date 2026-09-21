package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.catalog.SkinCatalogPaths
import dev.silksong.launcher.skins.catalog.SkinCatalogProfiles
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class SkinLibraryStoreTest {
    @get:Rule val temporary = TemporaryFolder()
    private fun store(): SkinLibraryStore {
        PinnedCatalogFixture.load()
        return SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/hollow-knight").apply { mkdirs() }))
    }
    private fun fastStore(): SkinLibraryStore {
        PinnedCatalogFixture.load()
        return SkinLibraryStore(
            SkinPaths(File(temporary.root, "profiles/hollow-knight").apply { mkdirs() }),
            FastSkinFileSystem(),
        )
    }
    private fun silksongStore(): SkinLibraryStore {
        val profile = SkinCatalogProfiles.Silksong
        val file = listOf(
            File("../../../docs/superpowers/specs/data/${profile.assetName}"),
            File("../../docs/superpowers/specs/data/${profile.assetName}"),
            File("docs/superpowers/specs/data/${profile.assetName}"),
        ).map(File::getAbsoluteFile).first(File::isFile)
        val catalog = (file.inputStream().use { SkinCatalogPaths.load(profile, it) } as SkinResult.Ok).value
        return SkinLibraryStore(SkinPaths(File(temporary.root, "profiles/silksong").apply { mkdirs() }), catalog = catalog)
    }
    @Test fun `fresh authority is atomically created OFF without legacy registry`() {
        val store = store(); val document = store.read().required()
        assertEquals(LibraryMode.OFF, document.mode)
        assertTrue(File(store.paths.root, "library.json").isFile)
        assertFalse(File(store.paths.root, "registry").exists())
    }
    @Test fun `malformed new authority never falls through to legacy and recovery is OFF`() {
        val store = store(); store.read()
        File(store.paths.root, "library.json").writeText("not json")
        File(store.paths.root, "registry").mkdir()
        assertTrue(store.read() is SkinResult.Error)
        assertTrue(store.recoverOff() is SkinResult.Ok)
        assertEquals(LibraryMode.OFF, (store.read() as SkinResult.Ok).value.mode)
    }
    @Test fun `nonblocking runtime read does not wait for importer lock`() {
        val store = store(); store.read(); val entered = CountDownLatch(1); val release = CountDownLatch(1)
        val thread = Thread { store.locked { entered.countDown(); check(release.await(5, TimeUnit.SECONDS)); SkinResult.Ok(Unit) } }
        thread.start(); assertTrue(entered.await(5, TimeUnit.SECONDS))
        try { assertTrue(store.read(nonblocking = true) is SkinResult.Error) }
        finally { release.countDown(); thread.join(5000) }
        assertTrue(store.read(nonblocking = true) is SkinResult.Ok)
    }
    @Test fun `selected ON entry cannot be removed and OFF removal retains object bytes`() {
        val store = store(); store.read()
        val pack = LibraryPack("a", "A", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
        File(store.paths.root, "library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(LibraryMode.ON, "a", listOf(pack))))
        val retained = File(store.paths.root, "retained-object").apply { writeText("unchanged") }
        assertTrue(store.remove("a") is SkinResult.Error)
        assertTrue(store.recoverOff() is SkinResult.Ok)
        assertTrue(store.remove("a") is SkinResult.Ok)
        assertEquals("unchanged", retained.readText())
    }
    @Test fun `atomic replacement failure leaves prior configuration authoritative`() {
        checkPublicationFault(afterRename = false)
    }
    @Test fun `post rename directory barrier failure rolls back to previous configuration`() {
        checkPublicationFault(afterRename = true)
    }
    private fun checkPublicationFault(afterRename: Boolean) {
        val original = store(); original.read().required()
        val a = LibraryPack("a", "A", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
        val file = File(original.paths.root, "library.json")
        file.writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(packs = listOf(a))))
        val before = file.readBytes().toList()
        val base = dev.silksong.launcher.skins.storage.AndroidSkinFileSystem()
        var fail = true; var moved = false
        val fs = object : dev.silksong.launcher.skins.storage.SkinFileSystem by base,
            dev.silksong.launcher.skins.storage.SkinFileSystemSecurity by base {
            override fun atomicMove(source: File, destination: File) {
                if (destination == file && fail && !afterRename) { fail = false; throw java.io.IOException("rename fault") }
                base.atomicMove(source,destination)
                if (destination == file) moved = true
            }
            override fun syncDirectory(directory: File) {
                if (afterRename && fail && moved && directory == original.paths.root) { fail = false; throw java.io.IOException("directory barrier fault") }
                base.syncDirectory(directory)
            }
        }
        val faulting = SkinLibraryStore(original.paths, fs)
        assertTrue(faulting.setEligibility("a",true) is SkinResult.Error)
        assertEquals(before,file.readBytes().toList())
        assertTrue(faulting.read().required().eligiblePackIds.isEmpty())
        assertTrue(faulting.setEligibility("a",true) is SkinResult.Ok)
    }
    @Test fun `explicit OFF recovery handles oversized corrupt authority without import admission`() {
        val store = store(); store.read().required()
        File(store.paths.root,"library.json").writeBytes(ByteArray(SkinLibraryCodec.MAX_BYTES+1) { 65 })
        assertTrue(store.read() is SkinResult.Error)
        assertTrue(store.recoverOff() is SkinResult.Ok)
        assertEquals(LibraryMode.OFF,store.read().required().mode)
    }
    @Test fun `one time verified empty legacy migration preserves legacy bytes and never rereads it`() {
        val store = store(); store.paths.root.mkdirs()
        val legacy = dev.silksong.launcher.skins.registry.SkinRegistryStore(store.paths.root, store.quota)
        legacy.recover().required()
        val root = File(store.paths.root,"registry")
        val before = root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() }
        assertEquals(LibraryMode.OFF, store.read().required().mode)
        assertEquals(before,root.walkTopDown().filter { it.isFile }.associate { it.relativeTo(root).path to it.readBytes().toList() })
        File(root,"current").writeText("corrupt old registry")
        assertEquals(LibraryMode.OFF,store.read().required().mode)
    }
    @Test fun `missing current with preserved corrupt authority never remigrates legacy`() {
        val store = store(); store.paths.root.mkdirs()
        dev.silksong.launcher.skins.registry.SkinRegistryStore(store.paths.root,store.quota).recover().required()
        File(store.paths.root,"library.invalid.json").writeText("retained corrupt authority")
        assertTrue(store.read() is SkinResult.Error)
        assertTrue(store.recoverOff() is SkinResult.Ok)
        assertEquals(LibraryMode.OFF,store.read().required().mode)
    }
    @Test fun `independent file lock produces immediate busy rather than blocking or generic IO error`() {
        val store = store(); store.read().required()
        java.nio.channels.FileChannel.open(File(store.paths.root,"library.lock").toPath(),java.nio.file.StandardOpenOption.WRITE).use { channel ->
            channel.lock().use {
                val result = store.read(nonblocking = true) as SkinResult.Error
                assertEquals(dev.silksong.launcher.skins.contracts.SkinImportCode.LIFECYCLE_BLOCKED,result.code)
            }
        }
        assertTrue(store.read(nonblocking = true) is SkinResult.Ok)
    }
    @Test fun `frozen successor is deduplicated even after successful commit`() {
        val store = rotationStore(listOf("a","b","c")); val started = store.startRuntime().required()
        assertNotNull(started.rotationRun); val run = requireNotNull(started.rotationRun)
        val pending = store.confirmDeath(run, 1).required()
        assertEquals("a", pending.selectedPackId); assertEquals("b", pending.pendingPackId)
        assertEquals(pending, store.confirmDeath(run, 1).required())
        val queued = store.confirmDeath(run, 2).required()
        assertEquals(listOf(2L), queued.queuedDeathOccurrences)
        assertEquals("b", queued.pendingPackId)
        assertTrue(store.remove("b") is SkinResult.Error); assertTrue(store.remove("a") is SkinResult.Error)
        assertFalse(store.finishRotation("0".repeat(64), run, 1, "b", pending.packs[1].treeSha256))
        assertEquals(queued, store.read().required())
        assertTrue(store.finishRotation(store.configurationIdentity(queued), run, 1, "b", pending.packs[1].treeSha256))
        val promoted = store.read().required()
        assertEquals("b", promoted.selectedPackId)
        assertEquals(2L, promoted.lastDeath)
        assertEquals("c", promoted.pendingPackId)
        assertTrue(promoted.queuedDeathOccurrences.isEmpty())
        assertEquals(promoted, store.confirmDeath(run, 1).required())
        assertEquals(promoted, store.confirmDeath(run, 2).required())
    }
    @Test fun `cancelling stale active death promotes the exact newer queued occurrence`() {
        val store = rotationStore(listOf("a", "b", "c"))
        val run = requireNotNull(store.startRuntime().required().rotationRun)
        store.confirmDeath(run, 1).required()
        store.confirmDeath(run, 2).required()

        val promoted = store.cancelDeath(run, 1).required()

        assertEquals("a", promoted.selectedPackId)
        assertEquals(2L, promoted.lastDeath)
        assertEquals("b", promoted.pendingPackId)
        assertTrue(promoted.queuedDeathOccurrences.isEmpty())
        assertEquals(promoted, store.cancelDeath(run, 1).required())
    }

    @Test fun `death backlog is durably bounded and overflow fails closed`() {
        val store = rotationStore(listOf("a", "b", "c"))
        val run = requireNotNull(store.startRuntime().required().rotationRun)
        store.confirmDeath(run, 1).required()
        for (occurrence in 2L..(SkinLibraryCodec.MAX_QUEUED_DEATHS + 1L))
            assertTrue(store.confirmDeath(run, occurrence) is SkinResult.Ok)
        val before = store.read().required()
        assertEquals(SkinLibraryCodec.MAX_QUEUED_DEATHS, before.queuedDeathOccurrences.size)
        assertTrue(store.confirmDeath(run, SkinLibraryCodec.MAX_QUEUED_DEATHS + 2L) is SkinResult.Error)
        assertEquals(before, store.read().required())
    }

    @Test fun `ordered zero one outside ring and wrap behavior is explicit`() {
        for ((ring, selected, expected) in listOf(Triple(emptyList(),"a",null), Triple(listOf("a"),"a",null),
            Triple(listOf("b"),"a","b"), Triple(listOf("c","b"),"a","c"), Triple(listOf("c","b"),"b","c"))) {
            val store = rotationStore(ring, selected); val run = requireNotNull(store.startRuntime().required().rotationRun)
            val next = store.confirmDeath(run, 1).required()
            assertEquals(expected, next.pendingPackId); assertEquals(selected,next.selectedPackId); assertEquals(1L,next.lastDeath)
            assertEquals(next,store.confirmDeath(run,1).required())
        }
    }
    @Test fun `OFF cancellation fresh process and stale run never resurrect pending`() {
        val store = rotationStore(listOf("a","b")); val run = requireNotNull(store.startRuntime().required().rotationRun)
        store.confirmDeath(run,1).required(); val restarted = store.startRuntime().required()
        assertNotEquals(run,restarted.rotationRun); assertNull(restarted.pendingPackId); assertEquals(0L,restarted.lastDeath)
        assertTrue(store.confirmDeath(run,1) is SkinResult.Error)
        val current = requireNotNull(restarted.rotationRun); store.confirmDeath(current,1).required()
        val cancelled = store.cancelRotation(current).required(); assertNull(cancelled.pendingPackId); assertNotEquals(current,cancelled.rotationRun)
        store.recoverOff().required(); val off = store.read().required(); assertEquals(LibraryMode.OFF,off.mode); assertNull(off.rotationRun)
        assertEquals("a",off.selectedPackId); assertEquals(3,off.packs.size)
    }
    @Test fun `busy death confirmation does not mutate or lose occurrence`() {
        val store = rotationStore(listOf("a","b")); val run = requireNotNull(store.startRuntime().required().rotationRun)
        java.nio.channels.FileChannel.open(File(store.paths.root,"library.lock").toPath(),java.nio.file.StandardOpenOption.WRITE).use { ch ->
            ch.lock().use { assertTrue(store.confirmDeath(run,1) is SkinResult.Error) }
        }
        assertEquals(0L,store.read().required().lastDeath); assertEquals("b",store.confirmDeath(run,1).required().pendingPackId)
    }
    private fun rotationStore(ring: List<String>, selected: String = "a"): SkinLibraryStore {
        val store = store(); store.read().required()
        val packs = listOf("a","b","c").map { LibraryPack(it,it,"Unknown",it.repeat(64),it.repeat(64),it.repeat(64)) }
        File(store.paths.root,"library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(LibraryMode.ROTATE,selected,packs,ring)))
        return store
    }

    @Test fun `direct enable verifies selects and turns OFF to ON in one mutation`() {
        val store = fastStore()
        val pack = importPack(store, "Azure")

        assertTrue(store.enable(pack.id) is SkinResult.Ok)

        val enabled = store.read().required()
        assertEquals(LibraryMode.ON, enabled.mode)
        assertEquals(pack.id, enabled.selectedPackId)
        assertNull(enabled.rotationRun)
    }

    @Test fun `direct enable preserves ROTATE while renewing its state atomically`() {
        val store = fastStore()
        val first = importPack(store, "First")
        val second = importPack(store, "Second")
        val oldRun = "1".repeat(32)
        File(store.paths.root, "library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(
            mode = LibraryMode.ROTATE,
            selectedPackId = first.id,
            packs = listOf(first, second),
            eligiblePackIds = listOf(first.id, second.id),
            rotationRun = oldRun,
            lastDeath = 4,
            pendingPackId = second.id,
            queuedDeathOccurrences = listOf(5),
        )))

        assertTrue(store.enable(second.id) is SkinResult.Ok)

        val enabled = store.read().required()
        assertEquals(LibraryMode.ROTATE, enabled.mode)
        assertEquals(second.id, enabled.selectedPackId)
        assertEquals(listOf(first.id, second.id), enabled.eligiblePackIds)
        assertNotEquals(oldRun, enabled.rotationRun)
        assertEquals(0L, enabled.lastDeath)
        assertNull(enabled.pendingPackId)
        assertTrue(enabled.queuedDeathOccurrences.isEmpty())
    }

    @Test fun `direct disable requires selected active pack and retains selection and rotation order`() {
        val store = fastStore()
        val first = importPack(store, "First")
        val second = importPack(store, "Second")
        File(store.paths.root, "library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(
            mode = LibraryMode.ROTATE,
            selectedPackId = first.id,
            packs = listOf(first, second),
            eligiblePackIds = listOf(second.id, first.id),
            rotationRun = "2".repeat(32),
        )))

        assertTrue(store.disable(second.id) is SkinResult.Error)
        assertTrue(store.disable(first.id) is SkinResult.Ok)

        val disabled = store.read().required()
        assertEquals(LibraryMode.OFF, disabled.mode)
        assertEquals(first.id, disabled.selectedPackId)
        assertEquals(listOf(second.id, first.id), disabled.eligiblePackIds)
        assertNull(disabled.rotationRun)
        assertTrue(store.disable(first.id) is SkinResult.Error)
    }

    @Test fun `failed direct enable leaves the prior authority unchanged`() {
        val store = fastStore()
        store.read().required()
        val unverified = LibraryPack("missing", "Missing", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
        File(store.paths.root, "library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(packs = listOf(unverified))))
        val before = File(store.paths.root, "library.json").readBytes().toList()

        assertTrue(store.enable(unverified.id) is SkinResult.Error)
        assertEquals(before, File(store.paths.root, "library.json").readBytes().toList())
    }

    @Test fun `stale native configuration mutation is rejected without changing authority`() {
        val store = fastStore()
        val pack = importPack(store, "First")
        val expected = store.configurationIdentity(store.read().required())
        store.confirmPack(expected, pack.id).required()
        val before = store.read().required()

        assertTrue(store.setMode(expected, LibraryMode.ON) is SkinResult.Error)
        assertEquals(before, store.read().required())
    }

    @Test fun `mode and scope mutations renew or cancel rotation state atomically`() {
        val store = fastStore()
        val pack = importPack(store, "First")
        var current = store.read().required()
        store.confirmPack(store.configurationIdentity(current), pack.id).required()
        current = store.read().required()
        store.setMode(store.configurationIdentity(current), LibraryMode.ROTATE).required()
        val rotating = store.read().required()
        assertNotNull(rotating.rotationRun)

        store.setSpriteScope(store.configurationIdentity(rotating), SpriteScope.CHARACTER_HUD).required()
        val scoped = store.read().required()
        assertEquals(SpriteScope.CHARACTER_HUD, scoped.spriteScope)
        assertNotEquals(rotating.rotationRun, scoped.rotationRun)
        assertEquals(0L, scoped.lastDeath)
        assertNull(scoped.pendingPackId)

        store.setMode(store.configurationIdentity(scoped), LibraryMode.OFF).required()
        val off = store.read().required()
        assertEquals(LibraryMode.OFF, off.mode)
        assertNull(off.rotationRun)
    }

    @Test fun `active scope mutation verifies selected pack before publication`() {
        val store = fastStore()
        val pack = LibraryPack("missing", "Missing", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
        val current = SkinLibraryDocument(mode = LibraryMode.ON, selectedPackId = pack.id, packs = listOf(pack))
        val authority = File(store.paths.root, "library.json").apply {
            requireNotNull(parentFile).mkdirs()
            writeBytes(SkinLibraryCodec.encode(current))
        }
        val before = authority.readBytes().toList()

        assertTrue(store.setSpriteScope(store.configurationIdentity(current), SpriteScope.CHARACTER) is SkinResult.Error)
        assertEquals(before, authority.readBytes().toList())
    }

    @Test fun `launcher eligibility changes renew current rotation state`() {
        val store = fastStore()
        val first = importPack(store, "First")
        val second = importPack(store, "Second")
        store.select(first.id).required()
        store.setEligibility(first.id, true).required()
        store.advanceMode().required()
        store.advanceMode().required()
        val before = store.startRuntime().required()
        val run = requireNotNull(before.rotationRun)
        store.confirmDeath(run, 1).required()

        store.setEligibility(second.id, true).required()
        val changed = store.read().required()
        assertNotEquals(run, changed.rotationRun)
        assertEquals(0L, changed.lastDeath)
        assertNull(changed.pendingPackId)
        assertTrue(changed.queuedDeathOccurrences.isEmpty())
    }

    @Test fun `launcher removal of eligible pack renews current rotation state`() {
        val store = fastStore()
        val first = importPack(store, "First")
        val second = importPack(store, "Second")
        val third = importPack(store, "Third")
        store.select(first.id).required()
        store.setEligibility(second.id, true).required()
        store.setEligibility(third.id, true).required()
        store.advanceMode().required()
        store.advanceMode().required()
        val before = store.startRuntime().required()
        val run = requireNotNull(before.rotationRun)
        store.confirmDeath(run, 1).required()

        store.remove(third.id).required()
        val changed = store.read().required()
        assertNotEquals(run, changed.rotationRun)
        assertEquals(0L, changed.lastDeath)
        assertNull(changed.pendingPackId)
        assertFalse(third.id in changed.eligiblePackIds)
    }

    @Test fun `confirm pack selects in OFF and ON but toggles ordered ROTATE eligibility`() {
        val store = fastStore()
        val first = importPack(store, "First")
        val second = importPack(store, "Second")
        fun config() = store.configurationIdentity(store.read().required())

        store.confirmPack(config(), first.id).required()
        assertEquals(first.id, store.read().required().selectedPackId)
        assertEquals(LibraryMode.OFF, store.read().required().mode)

        store.setMode(config(), LibraryMode.ON).required()
        store.confirmPack(config(), second.id).required()
        assertEquals(second.id, store.read().required().selectedPackId)

        store.setMode(config(), LibraryMode.ROTATE).required()
        val beforeToggle = store.read().required()
        store.confirmPack(config(), first.id).required()
        val eligible = store.read().required()
        assertEquals(second.id, eligible.selectedPackId)
        assertEquals(listOf(first.id), eligible.eligiblePackIds)
        assertNotEquals(beforeToggle.rotationRun, eligible.rotationRun)

        store.confirmPack(config(), first.id).required()
        assertTrue(store.read().required().eligiblePackIds.isEmpty())
        assertEquals(second.id, store.read().required().selectedPackId)
    }

    private fun importPack(store: SkinLibraryStore, name: String): LibraryPack {
        val decoder = PngDecoder { _, info ->
            SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
        }
        val archive = RawZipFixture.build(listOf(
            RawZipFixture.Entry("$name/Knight.png".toByteArray(), TinyPngFixture.rgba()),
        )).bytes
        val importer = SkinLibraryImporter(store, decoder)
        val handle = (importer.prepare(SkinImportInput.SelectedFile("$name.zip") { archive.inputStream() }) as SkinResult.Ok).value
        assertTrue(importer.commitImport(handle.handleId) is SkinResult.Ok)
        return store.read().required().packs.single { it.name == name }
    }

    @Test fun `missing runtime observation instruction names the owning profile`() {
        val hollowKnight = store().lastObservation(SkinLibraryDocument())
        val silksong = silksongStore().lastObservation(SkinLibraryDocument())
        assertTrue(hollowKnight.contains("launch Hollow Knight"))
        assertFalse(hollowKnight.contains("Silksong"))
        assertTrue(silksong.contains("launch Silksong"))
        assertFalse(silksong.contains("Hollow Knight"))
    }

    @Test fun `independent store instances merge eligibility updates instead of overwriting snapshots`() {
        val store = store(); store.read()
        val a = LibraryPack("a", "A", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
        val b = a.copy(id = "b", candidateKey = "d".repeat(64))
        File(store.paths.root, "library.json").writeBytes(SkinLibraryCodec.encode(SkinLibraryDocument(packs = listOf(a,b))))
        val other = SkinLibraryStore(store.paths)
        assertTrue(store.setEligibility("b", true) is SkinResult.Ok)
        assertTrue(other.setEligibility("a", true) is SkinResult.Ok)
        assertEquals(listOf("b", "a"), (store.read() as SkinResult.Ok).value.eligiblePackIds)
    }
}
