package dev.silksong.launcher.build

import com.google.gson.Gson
import dev.silksong.launcher.profiles.ProfilePaths
import java.io.File
import java.nio.charset.StandardCharsets
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.FileVisitResult
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.SimpleFileVisitor
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.nio.file.attribute.BasicFileAttributes
import java.security.MessageDigest
import java.util.LinkedHashMap
import java.util.zip.ZipFile

class GenerationPublisher(
    private val paths: ProfilePaths,
) {
    private companion object {
        val ID_PATTERN = Regex("[a-z0-9][a-z0-9._-]{0,63}")
        const val MANIFEST_NAME = "generation.json"
        const val STAGED_ID_NAME = ".generation-id"
        const val MANIFEST_NEXT_NAME = "generation.json.next"
        val SHA256_PATTERN = Regex("[0-9a-f]{64}")
    }

    private data class GenerationManifest(
        val profileId: String? = null,
        val generationId: String? = null,
        val sourceManifestSha256: String? = null,
        val toolchainId: String? = null,
        val patchManifestSha256: String? = null,
        val files: Map<String, String>? = null,
    )

    fun begin(jobId: String, generationId: String): File {
        val job = ownedChild(paths.staging, checkedId("job", jobId))
        checkedId("generation", generationId)
        check(!job.exists()) { "Staging job already exists: $jobId" }
        check(job.mkdirs()) { "Could not create staging job: $job" }
        File(job, STAGED_ID_NAME).writeText(generationId, StandardCharsets.UTF_8)
        return job
    }

    /**
     * Seals a staging tree by hashing every payload and writing its manifest
     * last. Any later payload mutation is therefore detected by [publish].
     */
    fun finalizeGeneration(
        jobId: String,
        generationId: String,
        metadata: GenerationMetadata,
        checkActive: () -> Unit = {},
    ) {
        val checkedJobId = checkedId("job", jobId)
        val checkedGenerationId = checkedId("generation", generationId)
        validateMetadata(metadata)
        val staged = ownedChild(paths.staging, checkedJobId)
        check(staged.isDirectory) { "Staging job does not exist: $checkedJobId" }
        check(File(staged, STAGED_ID_NAME).readText(StandardCharsets.UTF_8) == checkedGenerationId) {
            "Staging job $checkedJobId does not own generation $checkedGenerationId"
        }
        check(!File(staged, MANIFEST_NAME).exists()) { "Generation is already finalized: $checkedJobId" }

        val files = collectPayloadHashes(staged, checkActive)
        check(files.isNotEmpty()) { "Generation has no payload files: $checkedJobId" }
        val manifest = GenerationManifest(
            profileId = paths.profile.id,
            generationId = checkedGenerationId,
            sourceManifestSha256 = metadata.sourceManifestSha256,
            toolchainId = metadata.toolchainId,
            patchManifestSha256 = metadata.patchManifestSha256,
            files = files,
        )
        val next = File(staged, MANIFEST_NEXT_NAME)
        next.writeText(Gson().toJson(manifest), StandardCharsets.UTF_8)
        atomicMove(next.toPath(), File(staged, MANIFEST_NAME).toPath(), replace = false)
    }

    fun publish(jobId: String, generationId: String): InstalledGeneration {
        val checkedJobId = checkedId("job", jobId)
        val checkedGenerationId = checkedId("generation", generationId)
        val staged = ownedChild(paths.staging, checkedJobId)
        check(staged.isDirectory) { "Staging job does not exist: $checkedJobId" }
        check(File(staged, STAGED_ID_NAME).readText(StandardCharsets.UTF_8) == checkedGenerationId) {
            "Staging job $checkedJobId does not own generation $checkedGenerationId"
        }
        validateManifest(staged, checkedGenerationId, verifyPayloads = true)

        Files.createDirectories(paths.generations.toPath())
        val installed = ownedChild(paths.generations, checkedGenerationId)
        check(!installed.exists()) { "Generation already exists: $checkedGenerationId" }
        atomicMove(staged.toPath(), installed.toPath(), replace = false)
        val manifest = try {
            // Reopen after the directory move. This is the tree that will be
            // selected, rather than the path that merely preceded it.
            validateManifest(installed, checkedGenerationId, verifyPayloads = true)
        } catch (t: Throwable) {
            removeOwnedTree(installed.toPath(), paths.generations.toPath())
            throw t
        }

        paths.currentPointer.parentFile?.mkdirs()
        val next = File(paths.currentPointer.parentFile, "${paths.currentPointer.name}.next")
        Files.deleteIfExists(next.toPath())
        Files.write(next.toPath(), checkedGenerationId.toByteArray(StandardCharsets.UTF_8))
        atomicMove(next.toPath(), paths.currentPointer.toPath(), replace = true)
        return manifest.toInstalled(installed)
    }

    fun current(): InstalledGeneration? {
        if (!paths.currentPointer.isFile) return null
        val generationId = checkedId(
            "current generation",
            paths.currentPointer.readText(StandardCharsets.UTF_8),
        )
        val root = ownedChild(paths.generations, generationId)
        check(root.isDirectory) { "Current generation is missing: $generationId" }
        return validateManifest(root, generationId, verifyPayloads = false).toInstalled(root)
    }

    fun discard(jobId: String): Boolean {
        val job = ownedChild(paths.staging, checkedId("job", jobId))
        if (!job.exists() && !Files.isSymbolicLink(job.toPath())) return false
        removeOwnedTree(job.toPath(), paths.staging.toPath())
        return true
    }

    fun clearPublished(): Long {
        var removed = 0L
        if (paths.generations.exists() || Files.isSymbolicLink(paths.generations.toPath())) {
            removed += removeOwnedTree(paths.generations.toPath(), paths.root.toPath())
        }
        for (pointer in listOf(paths.currentPointer, File(paths.root, "${paths.currentPointer.name}.next"))) {
            if (pointer.isFile || Files.isSymbolicLink(pointer.toPath())) {
                removed += runCatching { Files.size(pointer.toPath()) }.getOrDefault(0L)
                Files.delete(pointer.toPath())
            }
        }
        return removed
    }

    fun clearStaged(): Long {
        if (!paths.staging.exists() && !Files.isSymbolicLink(paths.staging.toPath())) return 0L
        return removeOwnedTree(paths.staging.toPath(), paths.root.toPath())
    }

    private fun validateManifest(
        root: File,
        generationId: String,
        verifyPayloads: Boolean,
    ): GenerationManifest {
        val manifestFile = File(root, MANIFEST_NAME)
        check(manifestFile.isFile) { "Generation manifest is missing: $manifestFile" }
        val manifest = runCatching {
            manifestFile.reader(StandardCharsets.UTF_8).use {
                Gson().fromJson(it, GenerationManifest::class.java)
            }
        }.getOrElse { throw IllegalStateException("Generation manifest is invalid", it) }
        check(manifest.profileId == paths.profile.id) {
            "Generation profile mismatch: expected ${paths.profile.id}, got ${manifest.profileId}"
        }
        check(manifest.generationId == generationId) {
            "Generation ID mismatch: expected $generationId, got ${manifest.generationId}"
        }
        val metadata = GenerationMetadata(
            sourceManifestSha256 = manifest.sourceManifestSha256.orEmpty(),
            toolchainId = manifest.toolchainId.orEmpty(),
            patchManifestSha256 = manifest.patchManifestSha256.orEmpty(),
        )
        validateMetadata(metadata)
        val files = manifest.files ?: throw IllegalStateException("Generation file manifest is missing")
        check(files.isNotEmpty()) { "Generation file manifest is empty" }
        for ((relativePath, expectedHash) in files) {
            val payload = payloadFile(root, relativePath)
            check(SHA256_PATTERN.matches(expectedHash)) {
                "Invalid payload hash for $relativePath"
            }
            check(payload.isFile && !Files.isSymbolicLink(payload.toPath())) {
                "Generation payload is missing or linked: $relativePath"
            }
            if (verifyPayloads) {
                val actual = sha256(payload)
                check(actual == expectedHash) {
                    "Generation payload hash mismatch for $relativePath: expected $expectedHash, got $actual"
                }
                verifyZipReadable(payload, relativePath)
            }
        }
        if (verifyPayloads) {
            val actualPaths = collectPayloadPaths(root)
            check(actualPaths == files.keys) {
                "Generation payload listing mismatch: expected ${files.keys.sorted()}, got ${actualPaths.sorted()}"
            }
        }
        return manifest
    }

    private fun GenerationManifest.toInstalled(root: File): InstalledGeneration = InstalledGeneration(
        id = requireNotNull(generationId),
        profileId = requireNotNull(profileId),
        sourceManifestSha256 = requireNotNull(sourceManifestSha256),
        toolchainId = requireNotNull(toolchainId),
        patchManifestSha256 = requireNotNull(patchManifestSha256),
        files = requireNotNull(files).toSortedMap(),
        root = root,
    )

    private fun validateMetadata(metadata: GenerationMetadata) {
        require(SHA256_PATTERN.matches(metadata.sourceManifestSha256)) {
            "Invalid source manifest SHA-256"
        }
        require(metadata.toolchainId.isNotBlank() && metadata.toolchainId.length <= 128) {
            "Invalid toolchain ID"
        }
        require(SHA256_PATTERN.matches(metadata.patchManifestSha256)) {
            "Invalid patch manifest SHA-256"
        }
    }

    private fun collectPayloadHashes(root: File, checkActive: () -> Unit): Map<String, String> {
        val files = LinkedHashMap<String, String>()
        for (relativePath in collectPayloadPaths(root).sorted()) {
            checkActive()
            files[relativePath] = sha256(payloadFile(root, relativePath), checkActive)
        }
        return files
    }

    private fun collectPayloadPaths(root: File): Set<String> {
        val rootPath = root.toPath().toAbsolutePath().normalize()
        val files = linkedSetOf<String>()
        Files.walkFileTree(
            rootPath,
            object : SimpleFileVisitor<Path>() {
                override fun visitFile(file: Path, attrs: BasicFileAttributes): FileVisitResult {
                    check(attrs.isRegularFile && !attrs.isSymbolicLink && !Files.isSymbolicLink(file)) {
                        "Generation payload must be a regular unlinked file: $file"
                    }
                    val relative = rootPath.relativize(file.toAbsolutePath().normalize())
                        .joinToString("/") { it.toString() }
                    if (relative != STAGED_ID_NAME && relative != MANIFEST_NAME &&
                        relative != MANIFEST_NEXT_NAME
                    ) {
                        files += checkedRelativePath(relative)
                    }
                    return FileVisitResult.CONTINUE
                }
            },
        )
        return files
    }

    private fun payloadFile(root: File, relativePath: String): File {
        val checked = checkedRelativePath(relativePath)
        val rootPath = root.toPath().toAbsolutePath().normalize()
        val path = rootPath.resolve(checked.replace('/', File.separatorChar)).normalize()
        require(path.startsWith(rootPath) && path != rootPath) {
            "Payload path escapes generation: $relativePath"
        }
        return path.toFile()
    }

    private fun checkedRelativePath(relativePath: String): String {
        require(relativePath.isNotBlank() && !relativePath.startsWith('/') &&
            !relativePath.startsWith('\\') && !relativePath.contains('\\')
        ) { "Invalid generation payload path: $relativePath" }
        val parts = relativePath.split('/')
        require(parts.none { it.isBlank() || it == "." || it == ".." }) {
            "Invalid generation payload path: $relativePath"
        }
        require(relativePath != STAGED_ID_NAME && relativePath != MANIFEST_NAME &&
            relativePath != MANIFEST_NEXT_NAME
        ) { "Reserved generation payload path: $relativePath" }
        return relativePath
    }

    private fun sha256(file: File, checkActive: () -> Unit = {}): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(1 shl 20)
            while (true) {
                checkActive()
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun verifyZipReadable(file: File, relativePath: String) {
        if (file.extension.lowercase() !in setOf("apk", "jar", "zip", "obb")) return
        runCatching {
            ZipFile(file).use { zip ->
                val buffer = ByteArray(1 shl 16)
                val entries = zip.entries()
                while (entries.hasMoreElements()) {
                    val entry = entries.nextElement()
                    if (!entry.isDirectory) {
                        zip.getInputStream(entry).use { input ->
                            while (input.read(buffer) >= 0) Unit
                        }
                    }
                }
            }
        }.getOrElse { throw IllegalStateException("Generation ZIP is unreadable: $relativePath", it) }
    }

    private fun checkedId(kind: String, value: String): String {
        require(ID_PATTERN.matches(value)) { "Invalid $kind ID: $value" }
        return value
    }

    private fun ownedChild(owner: File, name: String): File {
        val ownerPath = owner.toPath().toAbsolutePath().normalize()
        val childPath = ownerPath.resolve(name).normalize()
        require(childPath.parent == ownerPath) { "Path escapes its owner: $childPath" }
        return childPath.toFile()
    }

    private fun atomicMove(source: Path, target: Path, replace: Boolean) {
        val options = if (replace) arrayOf(ATOMIC_MOVE, REPLACE_EXISTING) else arrayOf(ATOMIC_MOVE)
        try {
            Files.move(source, target, *options)
        } catch (e: AtomicMoveNotSupportedException) {
            throw IllegalStateException("Atomic publication is not supported for $source -> $target", e)
        }
    }

    private fun removeOwnedTree(target: Path, owner: Path): Long {
        val normalizedOwner = owner.toAbsolutePath().normalize()
        val normalizedTarget = target.toAbsolutePath().normalize()
        require(normalizedTarget.startsWith(normalizedOwner) && normalizedTarget != normalizedOwner) {
            "Refusing to remove path outside its owner: $normalizedTarget"
        }
        var bytes = 0L
        Files.walkFileTree(
            normalizedTarget,
            object : SimpleFileVisitor<Path>() {
                override fun visitFile(file: Path, attrs: BasicFileAttributes): FileVisitResult {
                    bytes += attrs.size()
                    Files.delete(file)
                    return FileVisitResult.CONTINUE
                }

                override fun postVisitDirectory(dir: Path, exc: java.io.IOException?): FileVisitResult {
                    if (exc != null) throw exc
                    Files.delete(dir)
                    return FileVisitResult.CONTINUE
                }
            },
        )
        return bytes
    }
}
