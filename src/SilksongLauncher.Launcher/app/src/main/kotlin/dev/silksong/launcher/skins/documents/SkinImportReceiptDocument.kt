package dev.silksong.launcher.skins.documents

import dev.silksong.launcher.skins.contracts.SkinAlias
import dev.silksong.launcher.skins.contracts.SkinWarning

data class SkinImportReceiptDocument(
    val schemaVersion: Int = 1,
    val normalizerVersion: String = "hkzip-v1",
    val candidateKey: String,
    val archiveSha256: String,
    val archiveName: String,
    val candidateRawPathHex: String,
    val layoutCode: Int,
    val signatureStatus: String = "UNVERIFIED_SOURCE",
    val source: String? = null,
    val homepage: String? = null,
    val aliases: List<SkinAlias> = emptyList(),
    val warnings: List<SkinWarning> = emptyList(),
)
