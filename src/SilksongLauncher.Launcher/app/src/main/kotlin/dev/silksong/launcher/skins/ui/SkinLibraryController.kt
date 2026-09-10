package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult

internal fun interface SkinModeAdvancePort {
    fun advance(): SkinResult<Unit>
}

/** Advances requested configuration only. Runtime application is reported separately, never inferred here. */
class SkinLibraryController internal constructor(private val modeAdvance: SkinModeAdvancePort) {
    fun advanceMode(): SkinResult<Unit> = modeAdvance.advance()
}

internal object UnavailableSkinModeAdvancePort : SkinModeAdvancePort {
    override fun advance(): SkinResult<Unit> = SkinResult.Error(
        SkinImportCode.LIFECYCLE_BLOCKED,
        "Mode changes are unavailable until the core/runtime executor is connected; no live apply was attempted.",
    )
}
