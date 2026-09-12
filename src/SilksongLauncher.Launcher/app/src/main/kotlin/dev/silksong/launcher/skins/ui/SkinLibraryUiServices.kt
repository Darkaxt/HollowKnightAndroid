package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File

internal interface SkinLibraryMutations {
    val available: Boolean
    fun select(target: SkinReplaceTarget): SkinResult<Unit>
    fun eligibility(target: SkinReplaceTarget, eligible: Boolean): SkinResult<Unit>
    fun remove(target: SkinReplaceTarget): SkinResult<Unit> = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Removal unavailable")
}

internal object UnavailableSkinLibraryMutations : SkinLibraryMutations {
    override val available = false
    override fun select(target: SkinReplaceTarget): SkinResult<Unit> = unavailable()
    override fun eligibility(target: SkinReplaceTarget, eligible: Boolean): SkinResult<Unit> = unavailable()
    private fun unavailable() = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE,
        "Library mutations are unavailable until production storage binding is approved")
}

/** Explicit internal dependency injection; no factory here binds the coordinator or acquires a lease. */
internal class SkinLibraryUiServices(
    val profile: GameProfile,
    val read: () -> SkinResult<SkinLibraryViewState>,
    val imports: SkinImportService,
    val mutations: SkinLibraryMutations,
    val mode: SkinModeAdvancePort,
    val modeAvailable: Boolean = false,
    val simplifiedAuthority: Boolean = false,
    val recover: (() -> SkinResult<Unit>)? = null,
) {
    companion object {
        fun production(context: android.content.Context, profile: GameProfile): SkinLibraryUiServices {
            if (!SkinLibraryService.isVisible(profile)) return production(context.filesDir, profile)
            // Session invokes these ports on its worker: even asset loading and initial directory IO stay off the Activity thread.
            val binding = lazy { bound(dev.silksong.launcher.skins.library.SkinLibraryStore.production(context, profile)) }
            val services by binding
            val imports = object : SkinImportService {
                override val available = true
                override fun prepare(input: dev.silksong.launcher.skins.importing.SkinImportInput) = services.imports.prepare(input)
                override fun commitImport(handleId: java.util.UUID) = services.imports.commitImport(handleId)
                override fun commitReplace(request: SkinReplaceRequest) = services.imports.commitReplace(request)
                override fun cancel(handleId: java.util.UUID) = services.imports.cancel(handleId)
                override fun retryPendingCleanup() =
                    if (binding.isInitialized()) services.imports.retryPendingCleanup() else SkinResult.Ok(Unit)
            }
            val mutations = object : SkinLibraryMutations {
                override val available = true
                override fun select(target: SkinReplaceTarget) = services.mutations.select(target)
                override fun eligibility(target: SkinReplaceTarget, eligible: Boolean) = services.mutations.eligibility(target, eligible)
                override fun remove(target: SkinReplaceTarget) = services.mutations.remove(target)
            }
            return SkinLibraryUiServices(profile, { services.read() }, imports, mutations,
                SkinModeAdvancePort { services.mode.advance() }, modeAvailable = true, simplifiedAuthority = true,
                recover = { requireNotNull(services.recover).invoke() })
        }
        fun bound(store: dev.silksong.launcher.skins.library.SkinLibraryStore): SkinLibraryUiServices {
            val receipts = SkinReceiptSummaryReader { digest -> store.receipts.verify(digest) }
            return SkinLibraryUiServices(
                dev.silksong.launcher.profiles.GameProfiles.require(store.profileId),
                { SkinLibraryService.readLibrary(store, receipts) },
                dev.silksong.launcher.skins.library.SkinLibraryImporter(store, dev.silksong.launcher.skins.importing.AndroidPngDecoder()),
                object : SkinLibraryMutations {
                    override val available = true
                    override fun select(target: SkinReplaceTarget) = store.select(target.id)
                    override fun eligibility(target: SkinReplaceTarget, eligible: Boolean) = store.setEligibility(target.id, eligible)
                    override fun remove(target: SkinReplaceTarget) = store.remove(target.id)
                }, SkinModeAdvancePort { store.advanceMode() }, modeAvailable = true, simplifiedAuthority = true, recover = store::recoverOff,
            )
        }
        fun production(filesDir: File, profile: GameProfile): SkinLibraryUiServices {
            val reader = SkinLibraryService.production(filesDir, profile)
            return SkinLibraryUiServices(profile, reader::refresh, UnavailableSkinImportService,
                UnavailableSkinLibraryMutations, UnavailableSkinModeAdvancePort)
        }
    }
}
