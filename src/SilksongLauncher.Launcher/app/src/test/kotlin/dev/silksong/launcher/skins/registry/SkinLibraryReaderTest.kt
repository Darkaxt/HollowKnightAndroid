package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.session.LeaseMutationGate
import java.io.File
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinLibraryReaderTest {
    private lateinit var testRoot: File
    private lateinit var skinsRoot: File
    private lateinit var head: RegistryHead

    @Before
    fun setUp() {
        testRoot = File("build/test-skin-library-reader").absoluteFile
        testRoot.deleteRecursively()
        skinsRoot = File(testRoot, "profiles/hollow-knight/skins").apply { mkdirs() }
        val fs = FastSkinFileSystem()
        head = assertOk(
            SkinRegistryStore(
                skinsRoot,
                PermissiveTestSkinQuota(skinsRoot),
                fs,
                SkinLockManager(skinsRoot),
            ).recover(),
        )
    }

    @After
    fun tearDown() {
        testRoot.deleteRecursively()
    }

    @Test
    fun libraryStatusRemainsReadableUnderActiveLeaseGate() {
        val before = diskSnapshot()
        val reader = realReader(LeaseMutationGate.ACTIVE)

        val snapshot = assertOk(reader.read())

        assertEquals(head, snapshot.registryHead)
        assertEquals(before, diskSnapshot())
        assertEquals(LeaseMutationGate.ACTIVE, snapshot.mutationGate)
    }

    @Test
    fun libraryStatusRemainsReadableUnderUnknownLeaseGate() {
        val before = diskSnapshot()
        val reader = realReader(LeaseMutationGate.UNKNOWN)

        val snapshot = assertOk(reader.read())

        assertEquals(head, snapshot.registryHead)
        assertEquals(before, diskSnapshot())
        assertEquals(LeaseMutationGate.UNKNOWN, snapshot.mutationGate)
    }

    @Test
    fun readWithoutLockInfrastructureFailsWithoutCreatingIt() {
        File(skinsRoot, "locks").deleteRecursively()
        val before = diskSnapshot()
        assertTrue(realReader().read() is SkinResult.Error)
        assertEquals(before, diskSnapshot())
    }

    @Test
    fun virginLibraryReadNeverPublishesGenesis() {
        File(skinsRoot, "registry").deleteRecursively()
        File(skinsRoot, "staging").deleteRecursively()
        val before = diskSnapshot()
        val reader = realReader()
        assertEquals(SkinRegistryAuthority.genesis(), assertOk(reader.read()).registryHead.document)
        assertEquals(before, diskSnapshot())
    }

    @Test
    fun repairNeededGenesisReadNeverRepairsPointers() {
        assertTrue(File(skinsRoot, "registry/current").delete())
        val before = diskSnapshot()
        assertEquals(head, assertOk(realReader().read()).registryHead)
        assertEquals(before, diskSnapshot())
    }

    @Test
    fun corruptVirginEvidenceFailsClosedWithoutMutation() {
        assertTrue(File(skinsRoot, "registry/current").delete())
        File(skinsRoot, "registry/generations/ambiguous").mkdirs()
        val before = diskSnapshot()
        assertTrue(realReader().read() is SkinResult.Error)
        assertEquals(before, diskSnapshot())
    }

    private fun realReader(gate: LeaseMutationGate = LeaseMutationGate.UNKNOWN): SkinLibraryReader {
        val fs = FastSkinFileSystem()
        val quota = PermissiveTestSkinQuota(skinsRoot)
        val locks = SkinLockManager(skinsRoot)
        val store = SkinRegistryStore(skinsRoot, quota, fs, locks)
        return SkinLibraryReader(locks, store, observedGate = gate)
    }

    private fun diskSnapshot(): Map<String, String> = skinsRoot.walkTopDown().associate {
        it.relativeTo(skinsRoot).path to if (it.isDirectory) "directory" else it.readBytes().joinToString(",")
    }

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }
}
