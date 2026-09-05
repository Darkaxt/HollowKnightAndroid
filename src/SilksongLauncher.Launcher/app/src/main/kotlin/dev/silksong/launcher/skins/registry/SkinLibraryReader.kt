package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.session.LeaseMutationGate
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.io.File

/** Minimal lock-consistent library authority; mutation policy remains outside this reader. */
data class SkinLibrarySnapshot(
    val registryHead: RegistryHead,
    val mutationGate: LeaseMutationGate,
)

class SkinLibraryReader private constructor(
    private val lockManager: SkinLockManager,
    private val registrySnapshot: () -> SkinResult<RegistryHead>,
    private val mutationGate: () -> LeaseMutationGate,
) {
    /** Observational status only, never mutation authorization. No session recovery is invoked here. */
    internal constructor(
        lockManager: SkinLockManager,
        registryStore: SkinRegistryStore,
        observedGate: LeaseMutationGate = LeaseMutationGate.UNKNOWN,
    ) : this(lockManager, registryStore::snapshotForLibrary, { observedGate })

    fun read(): SkinResult<SkinLibrarySnapshot> = try {
        // A read cannot bootstrap the durable lock infrastructure. Its owner must initialize it first.
        val locks = File(lockManager.root, "locks")
        require(Files.isDirectory(locks.toPath(), NOFOLLOW_LINKS) && !Files.isSymbolicLink(locks.toPath()))
        for (name in listOf("session.lock", "registry.lock")) {
            val path = File(locks, name).toPath()
            require(Files.isRegularFile(path, NOFOLLOW_LINKS) && !Files.isSymbolicLink(path))
        }
        lockManager.withSessionThenRegistry {
            when (val recovered = registrySnapshot()) {
                is SkinResult.Error -> recovered
                is SkinResult.Ok -> SkinResult.Ok(SkinLibrarySnapshot(recovered.value, mutationGate()))
            }
        }
    } catch (error: Exception) {
        SkinResult.Error(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            "Cannot read the lock-consistent skin library snapshot: ${error.message}",
        )
    }
}
