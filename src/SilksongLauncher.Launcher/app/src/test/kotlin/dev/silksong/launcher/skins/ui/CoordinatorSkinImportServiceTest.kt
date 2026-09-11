package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile
import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.fixtures.PermissiveTestSkinQuota
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.importing.SkinQuarantine
import dev.silksong.launcher.skins.quota.SkinQuotaCapacityReserver
import dev.silksong.launcher.skins.registry.*
import dev.silksong.launcher.skins.session.LeaseMutationGate
import dev.silksong.launcher.skins.storage.SkinPaths
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayInputStream
import java.io.File
import java.util.UUID

class CoordinatorSkinImportServiceTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `host adapter cannot bootstrap an unbound coordinator or open provider`() {
        var opened = false
        val service = CoordinatorSkinImportService.forHostTests(HollowKnightProfile)
        val result = service.prepare(SkinImportInput.SelectedFile("x") { opened = true; error("opened") })
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertFalse(opened)
        assertThrows(IllegalArgumentException::class.java) { CoordinatorSkinImportService.forHostTests(SilksongProfile) }
    }

    @Test fun `scoped singleton owns one copy and closes its private owner after import`() = withBinding {
        var opens = 0
        val service = CoordinatorSkinImportService.forHostTests(HollowKnightProfile)
        val prepared = (service.prepare(SkinImportInput.SelectedFile("input.txt") {
            opens++; ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 3, 4))
        }) as SkinResult.Ok).value
        assertEquals(1, opens)
        assertTrue(paths.importHandleOwner(prepared.handleId).isDirectory)
        val results = (service.commitImport(prepared.handleId) as SkinResult.Ok).value
        assertEquals(listOf("61", "62"), results.map { it.rawPrefixHex })
        assertTrue(results.all { it.code == SkinImportCode.NO_CANDIDATE && it.installedId == null })
        assertFalse(paths.importHandleOwner(prepared.handleId).exists())
        assertEquals(1, opens)
    }

    @Test fun `scoped cancel cleans coordinator owned staging`() = withBinding {
        val service = CoordinatorSkinImportService.forHostTests(HollowKnightProfile)
        val handle = (service.prepare(SkinImportInput.SelectedFile("x") {
            ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 3, 4))
        }) as SkinResult.Ok).value
        assertTrue(service.cancel(handle.handleId) is SkinResult.Ok)
        assertFalse(paths.importHandleOwner(handle.handleId).exists())
    }

    private lateinit var paths: SkinPaths
    private fun withBinding(action: () -> Unit) {
        paths = SkinPaths(File(temporary.root, "profiles/hollow-knight").apply { mkdirs() })
        paths.root.mkdirs()
        val fs = FastSkinFileSystem()
        val quota = PermissiveTestSkinQuota(paths.root)
        val operations = object : SkinImportCoordinatorOperations {
            override fun mutationGate() = LeaseMutationGate.CLEAR
            override fun normalize(archive: QuarantinedArchive) = SkinResult.Ok(listOf(
                CandidatePreparationResult.Rejected("a".toByteArray(), SkinImportCode.NO_CANDIDATE, "first rejection"),
                CandidatePreparationResult.Rejected("b".toByteArray(), SkinImportCode.NO_CANDIDATE, "second rejection"),
            ))
            override fun verify(prepared: PreparedSkinCandidate): SkinResult<Unit> = error("unused")
            override fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin> = error("unused")
            override fun discard(built: BuiltSkin): SkinResult<Unit> = error("unused")
            override fun publish(built: BuiltSkin): SkinResult<PublishedSkin> = error("unused")
            override fun recoverRegistry(): SkinResult<RegistryHead> = error("unused")
            override fun commitRegistry(expected: RegistryHead, operationId: UUID, mutation: RegistryMutation): SkinResult<RegistryHead> = error("unused")
            override fun referenceSnapshot(): SkinResult<Set<String>> = error("unused")
            override fun discardUnreferenced(published: PublishedSkin, referencedDigests: Set<String>): SkinResult<Unit> = error("unused")
            override fun settlePublications(referencedDigests: Set<String>): SkinResult<Unit> = error("unused")
        }
        SkinImportCoordinator.withTestBinding(SkinImportCoordinatorDependencies(paths, fs, SkinLockManager(paths.root),
            quota, SkinQuarantine(paths, fs, SkinQuotaCapacityReserver(quota)), operations), action)
    }
}
