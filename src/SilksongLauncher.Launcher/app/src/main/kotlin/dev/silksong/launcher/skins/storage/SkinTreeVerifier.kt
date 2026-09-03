package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.StagedPayload
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import java.io.File
import java.security.MessageDigest

class SkinTreeVerifier(
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    private val limits: SkinLimits = SkinLimits.V1,
    private val catalog: CatalogPathSet = CatalogPathSet.requirePinned(),
    private val profileAncestor: File? = null,
) {
    fun verify(root: File, expectedTreeSha256: String? = null): SkinResult<SkinManifestDocument> = try {
        val containmentOwner = profileAncestor ?: deriveProfileAncestor(root)
        fileSystem.requireContained(root, containmentOwner)
        if (!safeDirectory(root)) corrupt("Object root is not a safe directory")
        val marker = File(root, ".complete")
        val objectFile = File(root, "object.json")
        val pack = File(root, "pack")
        fileSystem.requireContained(marker, containmentOwner)
        fileSystem.requireContained(objectFile, containmentOwner)
        fileSystem.requireContained(pack, containmentOwner)
        if (!safeFile(marker) || fileSystem.identity(marker).size != 0L) corrupt("Completion marker is absent or non-empty")
        if (!safeFile(objectFile) || !safeDirectory(pack)) corrupt("Object metadata or pack is absent")
        val objectBytes = readStable(objectFile, 262144)
        val objectDocument = when (val parsed = CanonicalJson.tryParseObject(objectBytes)) {
            is SkinResult.Ok -> parsed.value
            is SkinResult.Error -> corrupt(parsed.detail)
        }
        if (expectedTreeSha256 != null && objectDocument.treeSha256 != expectedTreeSha256) corrupt("Tree digest differs from expected")
        if (root.name.matches(Regex("[0-9a-f]{64}"))) {
            if (root.name != objectDocument.treeSha256 || root.parentFile?.name != root.name.take(2)) {
                corrupt("Object path digest or shard mismatch")
            }
        }

        val actual = collectTree(root, pack, containmentOwner)
        val expectedRoot = setOf(".complete", "object.json", "pack")
        if (fileSystem.list(root).map { it.name }.toSet() != expectedRoot) corrupt("Object root contains undeclared nodes")
        if (actual.files.keys != objectDocument.files.map { it.path }.toSet()) corrupt("Pack tree does not match object file rows")
        val expectedDirectories = objectDocument.files.flatMap { row ->
            val parts = row.path.split('/')
            (1 until parts.size).map { count -> parts.take(count).joinToString("/") }
        }.toSet()
        if (actual.directories != expectedDirectories) corrupt("Pack contains undeclared or missing directories")

        val manifestFile = actual.files.getValue("skin.json")
        val manifestBytes = readStable(manifestFile, 65536)
        if (SkinIdentity.sha256(manifestBytes) != objectDocument.manifestSha256) corrupt("Manifest digest mismatch")
        val manifest = when (val parsed = CanonicalJson.tryParseManifest(manifestBytes, catalog)) {
            is SkinResult.Ok -> parsed.value
            is SkinResult.Error -> corrupt(parsed.detail)
        }
        if (manifest.contentSha256 != objectDocument.contentSha256) corrupt("Manifest content identity mismatch")
        val game = manifest.games.getValue("hollow-knight")
        val texturePaths = game.textures.values.map { "assets/$it" }.toSet()
        val payloadPaths = objectDocument.files.map { it.path }.filterNot { it == "skin.json" }.toSet()
        val declaredPayloadPaths = texturePaths.toMutableSet()
        manifest.preview?.let(declaredPayloadPaths::add)
        if (payloadPaths != declaredPayloadPaths) corrupt("Manifest and object payload declarations differ")

        if (fileSystem.sameFile(marker, objectFile) ||
            actual.files.values.any { file -> fileSystem.sameFile(marker, file) || fileSystem.sameFile(objectFile, file) }
        ) {
            corrupt("Object files contain hard-link aliases")
        }

        var payloadBytes = 0L
        val identityRows = mutableListOf<StagedPayload>()
        for (row in objectDocument.files) {
            val file = actual.files.getValue(row.path)
            val identity = fileSystem.identity(file)
            if (!identity.regularFile || identity.size != row.length) corrupt("Object file size changed")
            if (row.path != "skin.json") {
                val maximum = if (row.path == manifest.preview) limits.previewBytes else limits.textureBytes
                if (row.length > maximum) corrupt("Object payload exceeds its per-file bound")
            }
            val digest = hashStable(file, identity)
            if (digest != row.sha256) corrupt("Object file digest mismatch: ${row.path}")
            if (row.path != "skin.json") {
                payloadBytes = Math.addExact(payloadBytes, row.length)
                identityRows += StagedPayload(row.path, row.sha256, row.length, file)
            }
        }
        if (payloadBytes != objectDocument.payloadBytes || payloadBytes > limits.payloadBytes) corrupt("Object payload byte count mismatch")
        if (SkinIdentity.contentSha256(identityRows) != objectDocument.contentSha256) corrupt("Object content digest mismatch")
        if (SkinIdentity.treeSha256(objectDocument.files) != objectDocument.treeSha256) corrupt("Object tree digest mismatch")
        SkinResult.Ok(manifest)
    } catch (error: CorruptTree) {
        SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, error.message ?: "Object tree is corrupt")
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, "Object verification failed: ${error.message}")
    }

    private fun collectTree(root: File, pack: File, containmentOwner: File): TreeSnapshot {
        val result = linkedMapOf<String, File>()
        val actualDirectories = linkedSetOf<String>()
        val seenFiles = mutableListOf<File>()
        var nodes = fileSystem.list(root).size
        var directories = 1
        val queue = ArrayDeque<File>()
        queue += pack
        while (queue.isNotEmpty()) {
            val directory = queue.removeFirst()
            fileSystem.requireContained(directory, containmentOwner)
            if (!safeDirectory(directory)) corrupt("Linked or special directory is forbidden")
            for (child in fileSystem.list(directory).sortedBy { it.name }) {
                fileSystem.requireContained(child, containmentOwner)
                nodes++
                if (nodes > limits.observedNodes) corrupt("Observed node bound exceeded")
                if (fileSystem.isSymbolicLink(child)) corrupt("Symbolic link is forbidden")
                if (fileSystem.isDirectory(child)) {
                    directories++
                    if (directories > limits.candidateDirectories) corrupt("Directory bound exceeded")
                    val relativeDirectory = pack.toPath().relativize(child.toPath()).joinToString("/") { it.toString() }
                    actualDirectories += relativeDirectory
                    queue += child
                } else {
                    if (!safeFile(child)) corrupt("Special file is forbidden")
                    val identity = fileSystem.identity(child)
                    if (seenFiles.any { prior -> fileSystem.sameFile(prior, child) }) corrupt("Hard-link alias is forbidden")
                    seenFiles += child
                    val relative = pack.toPath().relativize(child.toPath()).joinToString("/") { it.toString() }
                    if (relative in result) corrupt("Duplicate payload path")
                    result[relative] = child
                }
            }
        }
        if (result.size > limits.regularFiles) corrupt("Regular file bound exceeded")
        return TreeSnapshot(result, actualDirectories)
    }

    private data class TreeSnapshot(
        val files: Map<String, File>,
        val directories: Set<String>,
    )

    private fun readStable(file: File, maximumBytes: Long): ByteArray {
        val before = fileSystem.identity(file)
        if (!before.regularFile || before.size > maximumBytes) corrupt("File exceeds its document bound")
        val bytes = fileSystem.openNoFollow(file).use { input -> input.readBytes() }
        val after = fileSystem.identity(file)
        if (before != after || bytes.size.toLong() != before.size) corrupt("File changed while reading")
        return bytes
    }

    private fun hashStable(file: File, before: dev.silksong.launcher.skins.contracts.SkinNodeIdentity): String {
        val digest = MessageDigest.getInstance("SHA-256")
        var count = 0L
        fileSystem.openNoFollow(file).use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                count = Math.addExact(count, read.toLong())
                if (count > before.size) corrupt("File grew while hashing")
                digest.update(buffer, 0, read)
            }
        }
        val after = fileSystem.identity(file)
        if (before != after || count != before.size) corrupt("File changed while hashing")
        return digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }

    private fun deriveProfileAncestor(root: File): File {
        var cursor: File? = root.absoluteFile.normalize()
        while (cursor != null && cursor.name != "skins") cursor = cursor.parentFile
        return cursor?.parentFile ?: corrupt("Object root has no fixed SkinPaths profile ancestor")
    }

    private fun safeDirectory(file: File): Boolean = fileSystem.exists(file) && fileSystem.isDirectory(file) && !fileSystem.isSymbolicLink(file)
    private fun safeFile(file: File): Boolean = fileSystem.exists(file) && fileSystem.isRegularFile(file) && !fileSystem.isSymbolicLink(file)
    private class CorruptTree(detail: String) : RuntimeException(detail)
    private fun corrupt(detail: String): Nothing = throw CorruptTree(detail)
}
