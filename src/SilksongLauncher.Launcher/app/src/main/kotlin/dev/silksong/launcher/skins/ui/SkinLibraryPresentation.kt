package dev.silksong.launcher.skins.ui

internal enum class SkinPackPrimaryAction { ENABLE, DISABLE }
internal enum class SkinPackBadge { INSTALLED, ROTATION_INCLUDED, SELECTED_OFF, ENABLED, ROTATING, PENDING }
internal enum class SkinDeleteBlock { IN_USE }
internal enum class SkinRuntimeKind { NONE, REPORT, UNAVAILABLE }

internal data class SkinPackPresentation(
    val row: SkinPackRow,
    val primaryAction: SkinPackPrimaryAction,
    val badge: SkinPackBadge,
    val deleteBlock: SkinDeleteBlock?,
)

internal data class SkinRuntimePresentation(
    val kind: SkinRuntimeKind,
    val status: String = "",
    val olderConfiguration: Boolean = false,
    val fullReport: String? = null,
)

internal data class SkinLibraryPresentation(
    val requestedMode: String,
    val selectedPackName: String?,
    val runtime: SkinRuntimePresentation,
    val packs: List<SkinPackPresentation>,
)

/** Pure mapping from authority/observation data to compact, non-diagnostic surface states. */
internal object SkinPresentationMapper {
    fun library(state: SkinLibraryViewState): SkinLibraryPresentation = SkinLibraryPresentation(
        requestedMode = state.mode,
        selectedPackName = state.packs.singleOrNull { it.id == state.selectedPackId }?.name,
        runtime = runtime(state.runtimeObservation),
        packs = state.packs.map { pack(it, state.mode, state.pendingPackId) },
    )

    fun pack(row: SkinPackRow, mode: String, pendingPackId: String?): SkinPackPresentation {
        val live = mode != "OFF"
        val pending = row.id == pendingPackId
        val badge = when {
            pending -> SkinPackBadge.PENDING
            row.selected && mode == "ON" -> SkinPackBadge.ENABLED
            row.selected && mode == "ROTATE" -> SkinPackBadge.ROTATING
            row.selected -> SkinPackBadge.SELECTED_OFF
            row.rotationEligible -> SkinPackBadge.ROTATION_INCLUDED
            else -> SkinPackBadge.INSTALLED
        }
        return SkinPackPresentation(
            row = row,
            primaryAction = if (row.selected && live) SkinPackPrimaryAction.DISABLE else SkinPackPrimaryAction.ENABLE,
            badge = badge,
            deleteBlock = if (live && (row.selected || pending)) SkinDeleteBlock.IN_USE else null,
        )
    }

    private fun runtime(report: String?): SkinRuntimePresentation {
        if (report.isNullOrBlank() || report.startsWith("No game-process report")) {
            return SkinRuntimePresentation(SkinRuntimeKind.NONE, fullReport = report)
        }
        if (report.startsWith("Last game report unreadable")) {
            return SkinRuntimePresentation(SkinRuntimeKind.UNAVAILABLE, fullReport = report)
        }
        val status = report.substringAfter(':', report).substringBefore(" ·").trim().take(64)
        return SkinRuntimePresentation(
            kind = SkinRuntimeKind.REPORT,
            status = status,
            olderConfiguration = report.startsWith("Last game report (older configuration)"),
            fullReport = report,
        )
    }
}
