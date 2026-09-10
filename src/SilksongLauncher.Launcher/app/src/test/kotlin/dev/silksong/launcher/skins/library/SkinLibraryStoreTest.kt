package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
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
        assertEquals(pending, store.confirmDeath(run, 2).required()) // another callback cannot replace frozen work
        assertTrue(store.remove("b") is SkinResult.Error); assertTrue(store.remove("a") is SkinResult.Error)
        assertFalse(store.finishRotation("0".repeat(64), run, 1, "b", pending.packs[1].treeSha256))
        assertEquals(pending, store.read().required())
        assertTrue(store.finishRotation(store.configurationIdentity(pending), run, 1, "b", pending.packs[1].treeSha256))
        val completed = store.read().required(); assertEquals("b", completed.selectedPackId); assertNull(completed.pendingPackId)
        assertEquals(completed, store.confirmDeath(run, 1).required())
        assertEquals("c", store.confirmDeath(run, 2).required().pendingPackId)
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
