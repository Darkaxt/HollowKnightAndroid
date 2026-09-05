package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.ProfilePaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.SkinLibraryReader
import dev.silksong.launcher.skins.registry.SkinLibrarySnapshot
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.registry.SkinRegistryStore
import dev.silksong.launcher.skins.storage.SkinImportReceiptRepository
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.File

/** Display data only: no registry document, staging paths, mode payload or mutation capability. */
internal data class SkinPackRow(
    val id: String,
    val name: String,
    val author: String,
    val candidateKey: String,
    val treeSha256: String,
    val importReceiptSha256: String,
    val selected: Boolean,
    val rotationEligible: Boolean,
    val receipt: SkinReceiptSummary,
)

internal data class SkinLibraryViewState(
    val generationSha256: String,
    val mode: String,
    val selectedPackId: String?,
    val activePackId: String?,
    val rotationOrder: List<String>,
    val interlock: String,
    val originalFailure: String?,
    val rollbackFailure: String?,
    val leaseObservation: String,
    val packs: List<SkinPackRow>,
) {
    // H4-STORAGE-RETENTION-001 / H4-STORAGE-GC-002 still block production mutation binding.
    val mutationsEnabled: Boolean get() = false
}

/** Read-only first launcher slice. Snapshot injection grants no mutation authority. */
internal class SkinLibraryService(
    private val profile: GameProfile,
    private val receipts: SkinReceiptSummaryReader = SkinReceiptSummaryReader.unavailable,
    private val readSnapshot: () -> SkinResult<SkinLibrarySnapshot>,
) {
    fun refresh(): SkinResult<SkinLibraryViewState> {
        if (!isVisible(profile)) return unsupported()
        return when (val result = readSnapshot()) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> {
                val snapshot = result.value
                val document = snapshot.registryHead.document
                if (document.profileId != profile.id) return SkinResult.Error(
                    SkinImportCode.INVALID_INPUT, "Skin snapshot belongs to another profile",
                )
                val activation = document.activation
                val packs = document.packs.sortedBy { it.id }
                SkinResult.Ok(SkinLibraryViewState(
                    snapshot.registryHead.sha256,
                    activation.mode.name,
                    activation.selectedPackId,
                    (activation.active as? ActiveVisual.Pack)?.id,
                    packs.filter { it.rotationEligible }.map { it.id },
                    activation.rotationInterlock.state.name,
                    activation.rotationInterlock.originalFailure,
                    activation.rotationInterlock.rollbackFailure,
                    snapshot.mutationGate.name,
                    packs.map { pack -> SkinPackRow(
                        pack.id, pack.name, pack.author, pack.candidateKey, pack.treeSha256,
                        pack.importReceiptSha256, pack.id == activation.selectedPackId, pack.rotationEligible,
                        receipts.read(pack.candidateKey, pack.importReceiptSha256),
                    ) },
                ))
            }
        }
    }

    companion object {
        fun isVisible(profile: GameProfile): Boolean = profile == HollowKnightProfile

        fun production(filesDir: File, profile: GameProfile): SkinLibraryService = SkinLibraryService(
            profile,
            SkinReceiptSummaryReader { digest ->
                SkinImportReceiptRepository(SkinPaths(ProfilePaths(filesDir, profile).root)).verify(digest)
            },
        ) {
            // Lazy: an unsupported profile does not even construct Hollow Knight storage adapters.
            try {
                val root = ProfilePaths(filesDir, profile).skinsRoot
                val locks = SkinLockManager(root)
                val store = SkinRegistryStore(root, ReadOnlyQuota(root), lockManager = locks)
                // UNKNOWN is an observation, not recovered live lease state. Never manufacture CLEAR.
                SkinLibraryReader(locks, store).read()
            } catch (error: Exception) {
                SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE,
                    "Skin library snapshot unavailable: ${error.message}")
            }
        }

        private fun unsupported() = SkinResult.Error(
            SkinImportCode.INVALID_INPUT, "Skins are available only for Hollow Knight",
        )
    }
}

/** Snapshot reads require no admission. Fail closed if that contract ever regresses. */
private class ReadOnlyQuota(override val root: File) : SkinQuotaAdmission {
    override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> = SkinResult.Error(
        SkinImportCode.DURABILITY_UNAVAILABLE, "Read-only skin library cannot reserve storage or recover state",
    )
}
