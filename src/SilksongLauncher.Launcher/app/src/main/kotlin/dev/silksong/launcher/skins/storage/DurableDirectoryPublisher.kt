package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File

internal data class DirectoryPublication(
    val root: File,
    val newlyCreated: Boolean,
)

internal data class MovedDirectoryRoot(
    val root: File,
    val identity: SkinNodeIdentity,
)

internal sealed interface DirectoryPublicationResult {
    data class Success(val publication: DirectoryPublication) : DirectoryPublicationResult
    data class Failure(
        val error: SkinResult.Error,
        val movedRoot: MovedDirectoryRoot?,
    ) : DirectoryPublicationResult
}

class DurableDirectoryPublisher(
    private val fs: SkinFileSystem = AndroidSkinFileSystem(),
) {
    fun publish(
        staging: File,
        destination: File,
        profileAncestor: File,
        verify: (File) -> Unit,
    ): SkinResult<File> = when (
        val result = publishDetailed(staging, destination, profileAncestor) { root ->
            try {
                verify(root)
                SkinResult.Ok(Unit)
            } catch (error: Exception) {
                SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, error.message ?: "Published directory verification failed")
            }
        }
    ) {
        is SkinResult.Ok -> SkinResult.Ok(result.value.root)
        is SkinResult.Error -> result
    }

    internal fun prepare(
        stagingRoot: File,
        profileAncestor: File,
    ): SkinResult<SkinNodeIdentity> = try {
        val root = profileAncestor.absoluteFile.normalize()
        val staging = stagingRoot.absoluteFile.normalize()
        require(staging.toPath().startsWith(root.toPath()) && staging != root) { "Staging path escapes profile ancestor" }
        requireDirectory(root, root)
        requireDirectory(staging, root)
        syncStaging(staging, root)
        val identity = fs.identity(staging)
        require(!identity.regularFile) { "Prepared publication root is not a directory" }
        SkinResult.Ok(identity)
    } catch (error: Exception) {
        unavailable("Publication preparation failed: ${error.message}")
    }

    internal fun publishDetailed(
        stagingRoot: File,
        destination: File,
        profileAncestor: File,
        verifier: (File) -> SkinResult<Unit>,
    ): SkinResult<DirectoryPublication> = when (
        val result = publishTracked(stagingRoot, destination, profileAncestor, verifier)
    ) {
        is DirectoryPublicationResult.Success -> SkinResult.Ok(result.publication)
        is DirectoryPublicationResult.Failure -> result.error
    }

    internal fun publishTracked(
        stagingRoot: File,
        destination: File,
        profileAncestor: File,
        verifier: (File) -> SkinResult<Unit>,
        preparedIdentity: SkinNodeIdentity? = null,
    ): DirectoryPublicationResult {
        val root = profileAncestor.absoluteFile.normalize()
        val target = destination.absoluteFile.normalize()
        val staging = stagingRoot.absoluteFile.normalize()
        val targetParent = target.parentFile
            ?: return DirectoryPublicationResult.Failure(unavailable("Publication destination has no parent"), null)
        if (!target.toPath().startsWith(root.toPath()) || target == root ||
            !staging.toPath().startsWith(root.toPath()) || staging == root ||
            target.toPath().startsWith(staging.toPath()) || staging.toPath().startsWith(target.toPath())
        ) {
            return DirectoryPublicationResult.Failure(
                unavailable("Publication path escapes or overlaps its profile ancestor"),
                null,
            )
        }

        var movedRoot: MovedDirectoryRoot? = null
        try {
            requireDirectory(root, root)
            requireFutureContained(target, root)
            if (fs.exists(target)) {
                requireDirectory(target, root)
                val existingIdentity = fs.identity(target).fileKey
                when (val verified = verifier(target)) {
                    is SkinResult.Error -> return DirectoryPublicationResult.Failure(verified, null)
                    is SkinResult.Ok -> Unit
                }
                requireStableDirectory(target, root, existingIdentity)
                syncBarrier(targetParent, root)
                requireStableDirectory(target, root, existingIdentity)
                return DirectoryPublicationResult.Success(DirectoryPublication(target, newlyCreated = false))
            }

            requireDirectory(staging, root)
            createAncestors(targetParent, root)
            val stagingIdentity = if (preparedIdentity == null) {
                syncStaging(staging, root)
                fs.identity(staging)
            } else {
                fs.requireContained(File(staging, ".complete"), root)
                if (fs.identity(staging) != preparedIdentity) {
                    throw IllegalStateException("Prepared staging identity changed")
                }
                preparedIdentity
            }
            fs.requireContained(staging, root)
            fs.requireContained(target, root, allowMissingLeaf = true)
            try {
                fs.atomicMove(staging, target)
                movedRoot = MovedDirectoryRoot(target, stagingIdentity)
            } catch (moveError: Exception) {
                fs.requireContained(target, root, allowMissingLeaf = true)
                if (!fs.exists(target)) throw moveError
                requireDirectory(target, root)
                val existingIdentity = fs.identity(target).fileKey
                when (val verified = verifier(target)) {
                    is SkinResult.Error -> return DirectoryPublicationResult.Failure(verified, null)
                    is SkinResult.Ok -> Unit
                }
                requireStableDirectory(target, root, existingIdentity)
                syncBarrier(targetParent, root)
                requireStableDirectory(target, root, existingIdentity)
                return DirectoryPublicationResult.Success(DirectoryPublication(target, newlyCreated = false))
            }

            fs.requireContained(target, root)
            if (fs.identity(target) != stagingIdentity) throw IllegalStateException("Moved directory identity changed")
            when (val verified = verifier(target)) {
                is SkinResult.Error -> throw VerificationFailure(verified)
                is SkinResult.Ok -> Unit
            }
            requireStableDirectory(target, root, stagingIdentity.fileKey)
            syncBarrier(targetParent, root)
            requireStableDirectory(target, root, stagingIdentity.fileKey)
            return DirectoryPublicationResult.Success(DirectoryPublication(target, newlyCreated = true))
        } catch (error: Exception) {
            val publicationError = if (error is VerificationFailure) {
                error.result
            } else {
                unavailable("Durable publication failed: ${error.message}")
            }
            val cleanupError = try {
                cleanupMoved(movedRoot, targetParent, root)
                null
            } catch (cleanup: Exception) {
                cleanup
            }
            return DirectoryPublicationResult.Failure(
                cleanupError?.let { unavailable("Moved publication cleanup failed: ${it.message}") } ?: publicationError,
                movedRoot,
            )
        }
    }

    private fun requireFutureContained(target: File, owner: File) {
        require(target.toPath().startsWith(owner.toPath()) && target != owner) { "Future path escapes profile ancestor" }
        var cursor = target
        while (!fs.exists(cursor)) {
            cursor = cursor.parentFile ?: throw IllegalStateException("Future path has no existing owner")
        }
        fs.requireContained(cursor, owner)
    }

    private fun createAncestors(parent: File, profileAncestor: File) {
        val relative = profileAncestor.toPath().relativize(parent.toPath())
        var cursor = profileAncestor
        for (name in relative) {
            val child = File(cursor, name.toString())
            fs.requireContained(child, profileAncestor, allowMissingLeaf = true)
            if (!fs.exists(child)) {
                fs.createDirectory(child)
                fs.requireContained(child, profileAncestor)
                fs.syncDirectory(child)
                fs.syncDirectory(cursor)
            } else {
                requireDirectory(child, profileAncestor)
            }
            cursor = child
        }
    }

    private fun syncStaging(root: File, profileAncestor: File) {
        val marker = File(root, ".complete")
        fs.requireContained(marker, profileAncestor, allowMissingLeaf = true)
        if (fs.exists(marker)) throw IllegalStateException("Staging completion marker already exists")
        val seenFiles = mutableListOf<File>()
        val directories = mutableListOf<File>()
        val queue = ArrayDeque<File>()
        queue.addLast(root)
        var nodes = 0
        while (queue.isNotEmpty()) {
            val directory = queue.removeFirst()
            requireDirectory(directory, profileAncestor)
            directories += directory
            for (child in fs.list(directory).sortedBy { it.name }) {
                fs.requireContained(child, profileAncestor)
                nodes++
                if (nodes > 512) throw IllegalStateException("Staging node bound exceeded")
                if (fs.isDirectory(child)) {
                    queue.addLast(child)
                } else {
                    val identity = fs.identity(child)
                    if (!identity.regularFile || !fs.isRegularFile(child) || fs.isSymbolicLink(child)) {
                        throw IllegalStateException("Special staging node is forbidden")
                    }
                    if (seenFiles.any { prior -> fs.sameFile(prior, child) }) {
                        throw IllegalStateException("Hard-linked staging files are forbidden")
                    }
                    seenFiles += child
                    fs.requireContained(child, profileAncestor)
                    fs.syncFile(child)
                    if (fs.identity(child) != identity) throw IllegalStateException("Staging file identity changed")
                }
            }
        }
        directories.sortedByDescending { it.toPath().nameCount }.forEach { directory ->
            fs.requireContained(directory, profileAncestor)
            fs.syncDirectory(directory)
        }
        fs.requireContained(marker, profileAncestor, allowMissingLeaf = true)
        fs.writeNew(marker, ByteArray(0))
        fs.requireContained(marker, profileAncestor)
        fs.syncFile(marker)
        fs.syncDirectory(root)
    }

    private fun syncBarrier(start: File, profileAncestor: File) {
        var cursor: File? = start
        while (cursor != null && cursor.toPath().startsWith(profileAncestor.toPath())) {
            requireDirectory(cursor, profileAncestor)
            fs.syncDirectory(cursor)
            if (cursor == profileAncestor) return
            cursor = cursor.parentFile
        }
        throw IllegalStateException("Durability barrier does not reach profile ancestor")
    }

    private fun cleanupMoved(moved: MovedDirectoryRoot?, parent: File, owner: File) {
        if (moved == null) return
        fs.requireContained(moved.root, owner)
        if (fs.exists(moved.root) && fs.identity(moved.root) == moved.identity) {
            fs.deleteContained(moved.root, owner)
            fs.syncDirectory(parent)
        }
    }

    private fun requireStableDirectory(directory: File, owner: File, expectedIdentity: String) {
        requireDirectory(directory, owner)
        val observed = fs.identity(directory)
        if (observed.regularFile || observed.fileKey != expectedIdentity) {
            throw IllegalStateException("Published directory identity changed")
        }
        fs.requireContained(directory, owner)
        val confirmed = fs.identity(directory)
        if (confirmed.regularFile || confirmed.fileKey != expectedIdentity) {
            throw IllegalStateException("Published directory identity changed")
        }
    }

    private fun requireDirectory(directory: File, owner: File) {
        fs.requireContained(directory, owner)
        if (!fs.exists(directory) || !fs.isDirectory(directory) || fs.isSymbolicLink(directory)) {
            throw IllegalStateException("Unsafe directory: $directory")
        }
        fs.identity(directory)
    }

    private class VerificationFailure(val result: SkinResult.Error) : RuntimeException(result.detail)
    private fun unavailable(detail: String) = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, detail)
}
