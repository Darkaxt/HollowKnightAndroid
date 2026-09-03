package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.BuiltSkin
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.StagedPayload
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinFileDocument
import dev.silksong.launcher.skins.documents.SkinGameDocument
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import dev.silksong.launcher.skins.documents.SkinObjectDocument
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import dev.silksong.launcher.skins.storage.sameFile
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicLong

class SkinObjectBuilder(
    private val fs: SkinFileSystem,
) {
    private val catalog: CatalogPathSet = CatalogPathSet.requirePinned()
    private val limits: SkinLimits = SkinLimits.V1

    internal constructor(fs: SkinFileSystem, catalog: CatalogPathSet, limits: SkinLimits = SkinLimits.V1) : this(fs) {
        require(catalog.sha256 == this.catalog.sha256 && catalog.exactBytes.contentEquals(this.catalog.exactBytes))
        configuredLimits = limits
    }

    private var configuredLimits: SkinLimits = limits

    fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin> {
        var ephemeralRoot: File? = null
        var stagingOwner: File? = null
        return try {
            val activeLimits = configuredLimits
            catalog.revalidate()
            if (!ID.matches(id) || id.length > 64) invalid("Explicit skin ID is invalid")
            if (prepared.payloads.isEmpty() || prepared.mappings.isEmpty()) invalid("Prepared candidate has no payload")
            if (prepared.payloads.size > activeLimits.mappings || prepared.mappings.size > activeLimits.mappings) {
                invalid("Prepared candidate exceeds mapping or payload bounds")
            }
            if (prepared.mappings.keys.any { it !in catalog.pathSet }) invalid("Prepared mapping target is outside the pinned catalog")
            val receipt = when (val parsed = CanonicalJson.tryParseImportReceipt(prepared.importReceiptBytes, catalog)) {
                is SkinResult.Error -> invalid(parsed.detail)
                is SkinResult.Ok -> parsed.value
            }
            if (SkinIdentity.sha256(prepared.importReceiptBytes) != prepared.importReceiptSha256) {
                invalid("Prepared import receipt identity is inconsistent")
            }
            if (receipt.candidateKey != prepared.candidateKey ||
                receipt.layoutCode != prepared.layoutCode ||
                receipt.candidateRawPathHex != prepared.rawPrefix.toHex() ||
                SkinIdentity.candidateKey(receipt.archiveSha256, prepared.rawPrefix, prepared.layoutCode) != prepared.candidateKey
            ) {
                invalid("Prepared candidate framing differs from its receipt")
            }
            stagingOwner = stagingOwner(prepared.stagingRoot)
            fs.requireContained(prepared.stagingRoot, stagingOwner)

            val payloadNames = linkedSetOf<String>()
            var payloadBytes = 0L
            prepared.payloads.forEach { payload ->
                requirePayload(payload, prepared.stagingRoot, stagingOwner)
                val expectedName = SkinIdentity.base32DigestHex(payload.sha256)
                if (payload.relativePath != "assets/$expectedName" || !payloadNames.add(expectedName)) {
                    invalid("Prepared payload identity or path is inconsistent")
                }
                if (payload.length > activeLimits.textureBytes || payloadBytes > activeLimits.payloadBytes - payload.length) {
                    invalid("Prepared payload bounds are exceeded")
                }
                payloadBytes += payload.length
            }
            if (prepared.mappings.values.toSet() != payloadNames) invalid("Prepared mappings and payloads do not have exact closure")
            validateStaging(prepared, stagingOwner, activeLimits)
            if (SkinIdentity.contentSha256(prepared.payloads) != prepared.contentSha256) {
                invalid("Prepared content identity is inconsistent")
            }

            val owner = prepared.stagingRoot.parentFile ?: invalid("Prepared candidate has no staging parent")
            ephemeralRoot = File(owner, "object-${prepared.candidateKey.take(12)}-$id-${NEXT.incrementAndGet()}")
            createDirectory(ephemeralRoot, stagingOwner)
            createDirectory(File(ephemeralRoot, "pack"), stagingOwner)
            createDirectory(File(ephemeralRoot, "pack/assets"), stagingOwner)

            val copied = mutableListOf<StagedPayload>()
            for (payload in prepared.payloads) {
                val before = requirePayload(payload, prepared.stagingRoot, stagingOwner)
                val destination = File(ephemeralRoot, "pack/${payload.relativePath}")
                fs.requireContained(destination, stagingOwner, allowMissingLeaf = true)
                fs.openOutput(destination, createNew = true).use { output ->
                    fs.openNoFollow(payload.file).use { input -> input.copyTo(output, 64 * 1024) }
                }
                fs.requireContained(destination, stagingOwner)
                val copiedIdentity = fs.identity(destination)
                val sourceAfter = fs.identity(payload.file)
                if (before != sourceAfter || copiedIdentity.size != payload.length || hashStable(destination, copiedIdentity) != payload.sha256) {
                    invalid("Payload changed while building object")
                }
                copied += payload.copy(file = destination)
            }

            val manifest = SkinManifestDocument(
                id = id,
                name = prepared.name,
                author = "Unknown",
                attribution = "Unknown",
                contentSha256 = prepared.contentSha256,
                games = mapOf(
                    "hollow-knight" to SkinGameDocument(
                        gameVersion = "1.5.12620",
                        catalogId = HollowKnightCatalogPaths.CATALOG_ID,
                        assetRoot = "assets",
                        textures = prepared.mappings,
                    ),
                ),
            )
            val manifestBytes = CanonicalJson.manifest(manifest, catalog)
            if (manifestBytes.size > 65536) invalid("Manifest exceeds 64 KiB")
            writeNew(File(ephemeralRoot, "pack/skin.json"), manifestBytes, stagingOwner)
            val manifestSha = SkinIdentity.sha256(manifestBytes)
            val files = (copied.map { SkinFileDocument(it.relativePath, it.length, it.sha256) } +
                SkinFileDocument("skin.json", manifestBytes.size.toLong(), manifestSha))
                .sortedWith { left, right -> SkinIdentity.unsignedUtf8Compare(left.path, right.path) }
            val treeSha = SkinIdentity.treeSha256(files)
            val objectDocument = SkinObjectDocument(
                treeSha256 = treeSha,
                contentSha256 = prepared.contentSha256,
                manifestSha256 = manifestSha,
                fileCount = files.size,
                payloadBytes = copied.sumOf { it.length },
                files = files,
            )
            val objectBytes = CanonicalJson.objectDocument(objectDocument)
            writeNew(File(ephemeralRoot, "object.json"), objectBytes, stagingOwner)
            SkinResult.Ok(
                BuiltSkin(
                    id = id,
                    candidateKey = prepared.candidateKey,
                    name = prepared.name,
                    contentSha256 = prepared.contentSha256,
                    treeSha256 = treeSha,
                    manifestSha256 = manifestSha,
                    importReceiptSha256 = prepared.importReceiptSha256,
                    manifestBytes = manifestBytes,
                    objectBytes = objectBytes,
                    importReceiptBytes = prepared.importReceiptBytes.copyOf(),
                    ephemeralRoot = ephemeralRoot,
                ),
            )
        } catch (error: BuildFailure) {
            failedBuild(
                ephemeralRoot,
                stagingOwner,
                SkinResult.Error(error.code, error.message ?: error.code.name),
            )
        } catch (error: Exception) {
            failedBuild(
                ephemeralRoot,
                stagingOwner,
                SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Object build failed: ${error.message}"),
            )
        }
    }

    fun discard(built: BuiltSkin): SkinResult<Unit> = try {
        val owner = stagingOwner(built.ephemeralRoot)
        if (fs.exists(built.ephemeralRoot)) fs.deleteContained(built.ephemeralRoot, owner)
        SkinResult.Ok(Unit)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Object discard failed: ${error.message}")
    }

    private fun validateStaging(prepared: PreparedSkinCandidate, owner: File, limits: SkinLimits) {
        val expectedFiles = prepared.payloads.map { it.relativePath }.toSet()
        val expectedDirectories = expectedFiles.flatMap { path ->
            val parts = path.split('/')
            (1 until parts.size).map { count -> parts.take(count).joinToString("/") }
        }.toSet()
        val actualFiles = linkedSetOf<String>()
        val actualDirectories = linkedSetOf<String>()
        val physicalFiles = mutableListOf<File>()
        val queue = ArrayDeque<File>()
        queue.addLast(prepared.stagingRoot)
        var nodes = 0
        var directories = 1
        while (queue.isNotEmpty()) {
            val directory = queue.removeFirst()
            fs.requireContained(directory, owner)
            if (!fs.isDirectory(directory) || fs.isSymbolicLink(directory)) invalid("Prepared staging directory is unsafe")
            for (child in fs.list(directory)) {
                fs.requireContained(child, owner)
                nodes++
                if (nodes > limits.observedNodes) invalid("Prepared staging node bound is exceeded")
                val relative = prepared.stagingRoot.toPath().relativize(child.toPath()).joinToString("/") { it.toString() }
                if (fs.isDirectory(child)) {
                    directories++
                    if (directories > limits.candidateDirectories || relative !in expectedDirectories) {
                        invalid("Prepared staging contains an undeclared directory")
                    }
                    actualDirectories += relative
                    queue.addLast(child)
                } else {
                    val identity = fs.identity(child)
                    if (!identity.regularFile || !fs.isRegularFile(child) || relative !in expectedFiles) {
                        invalid("Prepared staging contains an undeclared or special file")
                    }
                    if (physicalFiles.any { prior -> fs.sameFile(prior, child) }) invalid("Prepared staging contains hard-link aliases")
                    physicalFiles.add(child)
                    actualFiles += relative
                }
            }
        }
        if (actualFiles != expectedFiles || actualDirectories != expectedDirectories) {
            invalid("Prepared staging tree does not exactly match payload declarations")
        }
    }

    private fun requirePayload(payload: StagedPayload, root: File, owner: File): SkinNodeIdentity {
        fs.requireContained(payload.file, owner)
        val normalizedRoot = root.absoluteFile.normalize().toPath()
        val normalizedFile = payload.file.absoluteFile.normalize().toPath()
        if (!normalizedFile.startsWith(normalizedRoot) || !fs.isRegularFile(payload.file) || fs.isSymbolicLink(payload.file)) {
            invalid("Prepared payload escapes staging")
        }
        if (!payload.relativePath.matches(Regex("assets/[a-z2-7]{52}"))) invalid("Prepared payload path is invalid")
        if (payload.length < 0 || !payload.sha256.matches(Regex("[0-9a-f]{64}"))) invalid("Prepared payload identity is invalid")
        val identity = fs.identity(payload.file)
        if (!identity.regularFile || identity.size != payload.length) invalid("Prepared payload identity changed")
        return identity
    }

    private fun createDirectory(directory: File, owner: File) {
        fs.requireContained(directory, owner, allowMissingLeaf = true)
        fs.createDirectory(directory)
        fs.requireContained(directory, owner)
    }

    private fun writeNew(file: File, bytes: ByteArray, owner: File) {
        fs.requireContained(file, owner, allowMissingLeaf = true)
        fs.writeNew(file, bytes)
        fs.requireContained(file, owner)
    }

    private fun hashStable(file: File, before: SkinNodeIdentity): String {
        val digest = MessageDigest.getInstance("SHA-256")
        var count = 0L
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                count = Math.addExact(count, read.toLong())
                if (count > before.size) invalid("Payload grew while hashing")
                digest.update(buffer, 0, read)
            }
        }
        if (fs.identity(file) != before || count != before.size) invalid("Payload changed while hashing")
        return digest.digest().toHex()
    }

    private fun stagingOwner(path: File): File {
        var cursor: File? = path.absoluteFile.normalize()
        while (cursor != null && cursor.name != "staging") cursor = cursor.parentFile
        val owner = cursor ?: invalid("Owned staging root is not beneath SkinPaths.staging")
        if (owner.parentFile?.name != "skins") invalid("Owned staging root has no fixed skins ancestor")
        return owner
    }

    private fun failedBuild(
        root: File?,
        owner: File?,
        failure: SkinResult.Error,
    ): SkinResult.Error = try {
        cleanup(root, owner)
        failure
    } catch (cleanup: Exception) {
        SkinResult.Error(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            "Ephemeral object cleanup failed: ${cleanup.message}; original failure: ${failure.detail}",
        )
    }

    private fun cleanup(root: File?, owner: File?) {
        if (root == null || owner == null) return
        if (fs.exists(root)) fs.deleteContained(root, owner)
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private class BuildFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun invalid(detail: String): Nothing = throw BuildFailure(SkinImportCode.DOCUMENT_INVALID, detail)

    private companion object {
        val ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
        val NEXT = AtomicLong()
    }
}
