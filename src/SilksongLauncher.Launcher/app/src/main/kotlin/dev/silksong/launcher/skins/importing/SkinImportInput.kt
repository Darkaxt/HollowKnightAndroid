package dev.silksong.launcher.skins.importing

import java.io.InputStream

sealed interface SkinImportInput {
    val displayName: String?
    fun openOnce(): InputStream

    data class SelectedFile(
        override val displayName: String?,
        val opener: () -> InputStream,
    ) : SkinImportInput {
        override fun openOnce(): InputStream = opener()
    }

    data class ImmediateFolderFile(
        override val displayName: String?,
        val documentId: String,
        val opener: () -> InputStream,
    ) : SkinImportInput {
        override fun openOnce(): InputStream = opener()
    }
}
