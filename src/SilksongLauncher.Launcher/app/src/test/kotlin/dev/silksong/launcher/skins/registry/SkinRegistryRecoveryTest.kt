package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinRegistryRecoveryTest {
    private lateinit var profileRoot: File
    private lateinit var skinsRoot: File
    private lateinit var fs: FastSkinFileSystem

    @Before
    fun setUp() {
        profileRoot = File("build/test-skin-registry-recovery").absoluteFile
        profileRoot.deleteRecursively()
        skinsRoot = File(profileRoot, "profiles/hollow-knight/skins")
        skinsRoot.mkdirs()
        fs = FastSkinFileSystem()
    }

    @After
    fun tearDown() {
        profileRoot.deleteRecursively()
    }

    @Test
    fun recoversCurrentThenQualifiedNextThenPrevious() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val first = assertOk(
            store.commit(
                genesis,
                UUID.fromString("10000000-0000-4000-8000-000000000001"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        val second = assertOk(
            store.commit(
                first,
                UUID.fromString("10000000-0000-4000-8000-000000000002"),
                "launcher",
                SkinRegistryMutations().setEligibility(published().id, true),
            ),
        )
        val registry = File(skinsRoot, "registry")
        val secondPointer = File(registry, "current").readBytes()

        assertEquals(second, assertOk(store.recover()))

        File(registry, "current").delete()
        File(registry, "next").writeBytes(secondPointer)
        assertEquals(second, assertOk(store.recover()))

        File(registry, "next").delete()
        assertEquals(first, assertOk(store.recover()))
    }

    @Test
    fun unqualifiedNextNeverOverridesPreviousAndMtimeIsNeverAuthority() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val genesisPointer = File(skinsRoot, "registry/current").readBytes()
        val first = assertOk(
            store.commit(
                genesis,
                UUID.fromString("20000000-0000-4000-8000-000000000001"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        assertOk(
            store.commit(
                first,
                UUID.fromString("20000000-0000-4000-8000-000000000002"),
                "launcher",
                SkinRegistryMutations().setEligibility(published().id, true),
            ),
        )
        val registry = File(skinsRoot, "registry")
        File(registry, "current").delete()
        File(registry, "next").writeBytes(genesisPointer)
        File(registry, "generations").listFiles()!!.forEachIndexed { index, file ->
            file.setLastModified(if (index == 0) Long.MAX_VALUE else 1)
        }

        val recovered = assertOk(store.recover())

        assertEquals(first, recovered)
        assertEquals(1, recovered.sequence)
    }

    @Test
    fun pointedGenerationCorruptionFailsClosedInsteadOfFallingBack() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        assertOk(
            store.commit(
                genesis,
                UUID.fromString("30000000-0000-4000-8000-000000000001"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        val currentGeneration = generationNamedBy(File(skinsRoot, "registry/current"))
        File(currentGeneration, "registry.json").appendText(" ")

        val result = store.recover()

        assertError(SkinImportCode.REGISTRY_CORRUPT, result)
    }

    @Test
    fun validCurrentWinsWithoutReadingCorruptLowerPriorityPointerTarget() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val current = assertOk(
            store.commit(
                genesis,
                UUID.fromString("35000000-0000-4000-8000-000000000001"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        val generations = File(skinsRoot, "registry/generations")
        val rogueId = "35999999-9999-4999-8999-999999999999"
        val rogue = File(generations, RegistryPointer(0, rogueId, hex('0')).directoryName)
        generations.listFiles()!!.first { it.name.startsWith("rg-00000000000000000000-") }
            .copyRecursively(rogue)
        val rogueDigest = File(rogue, "registry.sha256").readText().trim()
        File(skinsRoot, "registry/next").writeBytes(
            RegistryPointerCodec.canonical(RegistryPointer(0, rogueId, rogueDigest)),
        )

        assertEquals(current, assertOk(store.recover()))
    }

    @Test
    fun recoversTornGenesisCurrentFromUniqueCanonicalNext() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val registry = File(skinsRoot, "registry")
        val current = File(registry, "current")
        val canonicalPointer = current.readBytes()
        current.writeText("torn-current")
        File(registry, "next").writeBytes(canonicalPointer)
        assertFalse(File(registry, "previous").exists())

        val recovered = assertOk(store.recover())

        assertEquals(genesis, recovered)
        assertTrue(current.readBytes().contentEquals(canonicalPointer))
        assertFalse(File(registry, "next").exists())
        assertFalse(File(registry, "previous").exists())
    }

    @Test
    fun genesisRecoveryRequiresExactlyOneCanonicalCommittedGeneration() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val current = File(skinsRoot, "registry/current")
        current.delete()

        assertEquals(genesis, assertOk(store.recover()))

        current.delete()
        val generations = File(skinsRoot, "registry/generations")
        generations.listFiles()!!.single().copyRecursively(File(generations, "rogue-generation"))
        assertError(SkinImportCode.REGISTRY_GENESIS_CORRUPT, store.recover())
        assertFalse(current.exists())
    }

    @Test
    fun currentMustHaveBoundedCanonicalParentEvidence() {
        val store = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        val genesis = assertOk(store.recover())
        val first = assertOk(
            store.commit(
                genesis,
                UUID.fromString("40000000-0000-4000-8000-000000000001"),
                "launcher",
                SkinRegistryMutations().install(published()),
            ),
        )
        val second = assertOk(
            store.commit(
                first,
                UUID.fromString("40000000-0000-4000-8000-000000000002"),
                "launcher",
                SkinRegistryMutations().setEligibility(published().id, true),
            ),
        )
        val forged = second.document.copy(parentGenerationId = "49999999-9999-4999-8999-999999999999")
        val bytes = assertOk(SkinRegistryDocumentCodec.canonical(forged))
        val sha256 = MessageDigest.getInstance("SHA-256").digest(bytes)
            .joinToString("") { "%02x".format(it.toInt() and 0xff) }
        val generation = generationNamedBy(File(skinsRoot, "registry/current"))
        File(generation, "registry.json").writeBytes(bytes)
        File(generation, "registry.sha256").writeText("$sha256\n")
        File(skinsRoot, "registry/current").writeBytes(
            RegistryPointerCodec.canonical(RegistryPointer(second.sequence, second.generationId, sha256)),
        )

        assertError(SkinImportCode.REGISTRY_CORRUPT, store.recover())
    }

    @Test
    fun identityChangeDuringPointerReadFailsClosed() {
        val ordinary = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), fs, SkinLockManager(skinsRoot))
        assertOk(ordinary.recover())
        val pointer = File(skinsRoot, "registry/current").absoluteFile.normalize()
        val delegate = fs
        var pointerIdentities = 0
        val changing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun identity(path: File): SkinNodeIdentity {
                val identity = delegate.identity(path)
                if (path.absoluteFile.normalize() != pointer) return identity
                pointerIdentities++
                return if (pointerIdentities >= 2) identity.copy(fileKey = "changed-pointer") else identity
            }
        }

        val result = SkinRegistryStore(skinsRoot, PermissiveTestSkinQuota(skinsRoot), changing, SkinLockManager(skinsRoot)).recover()

        assertError(SkinImportCode.REGISTRY_CORRUPT, result)
    }

    private fun generationNamedBy(pointer: File): File {
        val directoryName = pointer.readLines().first()
        return File(skinsRoot, "registry/generations/$directoryName")
    }

    private fun published() = PublishedSkin(
        id = "local-${hex('a').take(58)}",
        candidateKey = hex('a'),
        name = "Alpha",
        contentSha256 = hex('c'),
        treeSha256 = hex('b'),
        manifestSha256 = hex('e'),
        importReceiptSha256 = hex('d'),
        objectRoot = File(profileRoot, "object"),
        newlyCreatedRoots = emptyList(),
    )

    private fun hex(character: Char) = character.toString().repeat(64)

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }
}
