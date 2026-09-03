package dev.silksong.launcher.skins.storage

import java.io.File

class SkinPaths(profileRoot: File) {
    val profileRoot: File = profileRoot.absoluteFile.normalize()
    val root: File = File(this.profileRoot, "skins")
    val objects: File = File(root, "objects/sha256")
    val importReceipts: File = File(root, "import-receipts/sha256")
    val staging: File = File(root, "staging")
    val quarantine: File = File(staging, "quarantine")
    val publicationCleanup: File = File(staging, "publication-cleanup")

    fun objectRoot(treeSha256: String): File = sharded(objects, treeSha256)
    fun importReceiptRoot(importReceiptSha256: String): File = sharded(importReceipts, importReceiptSha256)

    private fun sharded(owner: File, digest: String): File {
        require(Regex("[0-9a-f]{64}").matches(digest)) { "Invalid skin digest" }
        return File(File(owner, digest.take(2)), digest)
    }
}
