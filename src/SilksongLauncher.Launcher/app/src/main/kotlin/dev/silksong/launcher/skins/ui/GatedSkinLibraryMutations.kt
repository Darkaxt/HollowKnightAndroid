package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.registry.RegistryMutation
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.registry.SkinRegistryMutations
import dev.silksong.launcher.skins.registry.SkinRegistryStore
import dev.silksong.launcher.skins.session.LeaseMutationGate
import java.util.UUID

/** Host-only library mutation adapter. No activation closure, live proof, recovery or session acquisition. */
internal class GatedSkinLibraryMutations private constructor(
    private val locks: SkinLockManager,
    private val store: SkinRegistryStore,
    private val gate: () -> LeaseMutationGate,
) : SkinLibraryMutations {
    override val available = true
    override fun select(target: SkinReplaceTarget): SkinResult<Unit> =
        change(target, SkinRegistryMutations().select(target.id))
    override fun eligibility(target: SkinReplaceTarget, eligible: Boolean): SkinResult<Unit> =
        change(target, SkinRegistryMutations().setEligibility(target.id, eligible))

    private fun change(target: SkinReplaceTarget, mutation: RegistryMutation): SkinResult<Unit> = try {
        locks.withSessionThenRegistry {
            if (gate() != LeaseMutationGate.CLEAR) return@withSessionThenRegistry SkinResult.Error(
                SkinImportCode.LIFECYCLE_BLOCKED, "Fresh session observation blocks library mutation",
            )
            val head = when (val read = store.snapshotForLibrary()) {
                is SkinResult.Error -> return@withSessionThenRegistry read
                is SkinResult.Ok -> read.value
            }
            val pack = head.document.packs.singleOrNull { it.id == target.id }
            if (head.sha256 != target.generationSha256 || pack?.treeSha256 != target.treeSha256 ||
                pack?.importReceiptSha256 != target.receiptSha256) return@withSessionThenRegistry SkinResult.Error(
                SkinImportCode.REGISTRY_CONFLICT, "Confirmed pack or registry generation changed",
            )
            // The store admits its own quota reservation before any publication under the same ordered locks.
            when (val result = store.commit(head, UUID.randomUUID(), "skin-library", mutation)) {
                is SkinResult.Error -> result
                is SkinResult.Ok -> SkinResult.Ok(Unit)
            }
        }
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Library mutation unavailable: ${error.message}")
    }
    companion object {
        internal fun forHostTests(locks: SkinLockManager, store: SkinRegistryStore, freshGate: () -> LeaseMutationGate): SkinLibraryMutations {
            require(locks.root == store.root) { "Library authorities use different profiles" }
            return GatedSkinLibraryMutations(locks, store, freshGate)
        }
    }
}
