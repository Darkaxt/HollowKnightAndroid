package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult

internal fun interface SkinModeAdvancePort {
    fun advance(): SkinResult<Unit>
}

/** The later core executor owns OFF -> ON -> ROTATE -> OFF, including live-proof closure. */
class SkinLibraryController internal constructor(private val modeAdvance: SkinModeAdvancePort) {
    fun advanceMode(): SkinResult<Unit> = modeAdvance.advance()
}

internal object UnavailableSkinModeAdvancePort : SkinModeAdvancePort {
    override fun advance(): SkinResult<Unit> = SkinResult.Error(
        SkinImportCode.LIFECYCLE_BLOCKED,
        "Mode changes are unavailable until the core/runtime executor is connected; no live apply was attempted.",
    )
}
