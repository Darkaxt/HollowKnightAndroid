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

class GenerationPublisher(
    private val paths: ProfilePaths,
) {
    private companion object {
        val ID_PATTERN = Regex("[a-z0-9][a-z0-9._-]{0,63}")
        const val MANIFEST_NAME = "generation.json"
        const val STAGED_ID_NAME = ".generation-id"
    }

    private data class GenerationManifest(
        val profileId: String? = null,
        val generationId: String? = null,
    )

    fun begin(jobId: String, generationId: String): File {
        val job = ownedChild(paths.staging, checkedId("job", jobId))
        checkedId("generation", generationId)
        check(!job.exists()) { "Staging job already exists: $jobId" }
        check(job.mkdirs()) { "Could not create staging job: $job" }
        File(job, STAGED_ID_NAME).writeText(generationId, StandardCharsets.UTF_8)
        return job
    }

    fun publish(jobId: String, generationId: String): InstalledGeneration {
        val checkedJobId = checkedId("job", jobId)
        val checkedGenerationId = checkedId("generation", generationId)
        val staged = ownedChild(paths.staging, checkedJobId)
        check(staged.isDirectory) { "Staging job does not exist: $checkedJobId" }
        check(File(staged, STAGED_ID_NAME).readText(StandardCharsets.UTF_8) == checkedGenerationId) {
            "Staging job $checkedJobId does not own generation $checkedGenerationId"
        }
        validateManifest(staged, checkedGenerationId)

        Files.createDirectories(paths.generations.toPath())
        val installed = ownedChild(paths.generations, checkedGenerationId)
        check(!installed.exists()) { "Generation already exists: $checkedGenerationId" }
        atomicMove(staged.toPath(), installed.toPath(), replace = false)

        paths.currentPointer.parentFile?.mkdirs()
        val next = File(paths.currentPointer.parentFile, "${paths.currentPointer.name}.next")
        Files.deleteIfExists(next.toPath())
        Files.write(next.toPath(), checkedGenerationId.toByteArray(StandardCharsets.UTF_8))
        atomicMove(next.toPath(), paths.currentPointer.toPath(), replace = true)
        return InstalledGeneration(checkedGenerationId, installed)
    }

    fun current(): InstalledGeneration? {
        if (!paths.currentPointer.isFile) return null
        val generationId = checkedId(
            "current generation",
            paths.currentPointer.readText(StandardCharsets.UTF_8),
        )
        val root = ownedChild(paths.generations, generationId)
        check(root.isDirectory) { "Current generation is missing: $generationId" }
        validateManifest(root, generationId)
        return InstalledGeneration(generationId, root)
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

    private fun validateManifest(root: File, generationId: String) {
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
