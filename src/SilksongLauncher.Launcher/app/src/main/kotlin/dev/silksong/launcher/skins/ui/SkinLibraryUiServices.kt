package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File

internal interface SkinLibraryMutations {
    val available: Boolean
    fun select(target: SkinReplaceTarget): SkinResult<Unit>
    fun eligibility(target: SkinReplaceTarget, eligible: Boolean): SkinResult<Unit>
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
) {
    companion object {
        fun production(filesDir: File, profile: GameProfile): SkinLibraryUiServices {
            val reader = SkinLibraryService.production(filesDir, profile)
            return SkinLibraryUiServices(profile, reader::refresh, UnavailableSkinImportService,
                UnavailableSkinLibraryMutations, UnavailableSkinModeAdvancePort)
        }
    }
}
