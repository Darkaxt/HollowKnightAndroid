package dev.silksong.launcher.skins.fixtures

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.channels.Channels
import java.nio.channels.SeekableByteChannel
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.Path
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardOpenOption.CREATE_NEW
import java.nio.file.StandardOpenOption.READ
import java.nio.file.StandardOpenOption.TRUNCATE_EXISTING
import java.nio.file.StandardOpenOption.WRITE
import java.nio.file.attribute.BasicFileAttributes

/** Fast local authority for lease unit tests; production containment remains AndroidSkinFileSystem. */
class FastSkinFileSystem : SkinFileSystem, SkinFileSystemSecurity, SkinFileSystemBoundedListing {
    private val syntheticIdentities = mutableMapOf<Path, String>()
    private var nextSyntheticIdentity = 0L

    override fun exists(file: File): Boolean = Files.exists(file.toPath(), NOFOLLOW_LINKS)

    override fun isDirectory(file: File): Boolean = Files.isDirectory(file.toPath(), NOFOLLOW_LINKS)

    override fun isRegularFile(file: File): Boolean = Files.isRegularFile(file.toPath(), NOFOLLOW_LINKS)

    override fun isSymbolicLink(file: File): Boolean = Files.isSymbolicLink(file.toPath())

    override fun createDirectory(path: File) {
        forgetIdentity(path.toPath())
        Files.createDirectory(path.toPath())
    }

    override fun writeNew(path: File, bytes: ByteArray) {
        openOutput(path, createNew = true).use { it.write(bytes) }
    }

    override fun syncFile(path: File) = Unit

    override fun syncDirectory(path: File) = Unit

    override fun atomicMove(source: File, target: File) {
        val sourcePath = normalized(source)
        val targetPath = normalized(target)
        val synthetic = if (attributes(sourcePath).fileKey() == null) syntheticIdentity(sourcePath) else null
        Files.move(source.toPath(), target.toPath(), ATOMIC_MOVE)
        if (synthetic != null) synchronized(syntheticIdentities) {
            syntheticIdentities.remove(sourcePath)
            syntheticIdentities[targetPath] = synthetic
        }
    }

    override fun openNoFollow(path: File): InputStream = Channels.newInputStream(openSeekableNoFollow(path))

    override fun identity(path: File): SkinNodeIdentity {
        val attributes = attributes(path.toPath())
        return SkinNodeIdentity(
            fileKey = attributes.fileKey()?.toString() ?: syntheticIdentity(path.toPath()),
            size = attributes.size(),
            regularFile = attributes.isRegularFile,
        )
    }

    override fun list(path: File): List<File> = Files.newDirectoryStream(path.toPath()).use { stream ->
        stream.map(Path::toFile).toList()
    }

    override fun listBounded(path: File, maximumEntries: Int): List<File> {
        require(maximumEntries >= 0)
        return Files.newDirectoryStream(path.toPath()).use { stream ->
            val result = ArrayList<File>(maximumEntries)
            val iterator = stream.iterator()
            while (iterator.hasNext()) {
                require(result.size < maximumEntries) { "Bounded directory listing exceeds $maximumEntries entries" }
                result += iterator.next().toFile()
            }
            result
        }
    }

    override fun deleteContained(path: File, owner: File) {
        requireContained(path, owner)
        require(normalized(path) != normalized(owner)) { "Cleanup cannot delete its fixed owner" }
        if (exists(path)) deleteTree(path, owner)
    }

    override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
        val ownerPath = normalized(owner)
        val targetPath = normalized(path)
        require(targetPath.startsWith(ownerPath)) { "Path escapes its fixed owner" }
        require(attributes(ownerPath).isDirectory) { "Fixed owner is not a directory" }
        if (targetPath == ownerPath) return
        val relative = ownerPath.relativize(targetPath)
        var cursor = ownerPath
        for ((index, name) in relative.withIndex()) {
            cursor = cursor.resolve(name)
            if (!Files.exists(cursor, NOFOLLOW_LINKS)) {
                require(allowMissingLeaf && index == relative.nameCount - 1) { "Contained path component is missing" }
                return
            }
            val attributes = attributes(cursor)
            if (index < relative.nameCount - 1) require(attributes.isDirectory) { "Contained ancestor is not a directory" }
        }
    }

    override fun sameFile(left: File, right: File): Boolean {
        attributes(left.toPath())
        attributes(right.toPath())
        return Files.isSameFile(left.toPath(), right.toPath())
    }

    override fun openOutput(file: File, createNew: Boolean): OutputStream {
        if (createNew) forgetIdentity(file.toPath())
        val options = if (createNew) {
            setOf(WRITE, CREATE_NEW, NOFOLLOW_LINKS)
        } else {
            setOf(WRITE, TRUNCATE_EXISTING, NOFOLLOW_LINKS)
        }
        return Channels.newOutputStream(Files.newByteChannel(file.toPath(), options))
    }

    override fun openSeekableNoFollow(file: File): SeekableByteChannel =
        Files.newByteChannel(file.toPath(), setOf(READ, NOFOLLOW_LINKS))

    private fun deleteTree(path: File, owner: File) {
        requireContained(path, owner)
        val attributes = attributes(path.toPath())
        if (attributes.isDirectory) {
            Files.newDirectoryStream(path.toPath()).use { stream ->
                stream.forEach { child ->
                    require(child.normalize().parent == path.toPath().toAbsolutePath().normalize()) {
                        "Cleanup child escapes its parent"
                    }
                    deleteTree(child.toFile(), owner)
                }
            }
        } else {
            require(attributes.isRegularFile) { "Cleanup special node is forbidden" }
        }
        Files.delete(path.toPath())
        forgetIdentity(path.toPath())
    }

    private fun syntheticIdentity(path: Path): String = synchronized(syntheticIdentities) {
        syntheticIdentities.getOrPut(path.toAbsolutePath().normalize()) {
            nextSyntheticIdentity++
            "fast-node-$nextSyntheticIdentity"
        }
    }

    private fun forgetIdentity(path: Path) {
        synchronized(syntheticIdentities) {
            syntheticIdentities.remove(path.toAbsolutePath().normalize())
        }
    }

    private fun attributes(path: Path): BasicFileAttributes {
        require(!Files.isSymbolicLink(path)) { "Symbolic path is forbidden" }
        val attributes = Files.readAttributes(path, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
        require(!attributes.isSymbolicLink && !attributes.isOther && (attributes.isDirectory || attributes.isRegularFile)) {
            "Unsupported filesystem node"
        }
        return attributes
    }

    private fun normalized(file: File): Path = file.toPath().toAbsolutePath().normalize()
}

/** Explicit test-only admission fake. Production constructors still require a bounded authority. */
class PermissiveTestSkinQuota(skinsRoot: File) : SkinQuotaAdmission {
    override val root: File = skinsRoot.absoluteFile.normalize()

    override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> = SkinResult.Ok(
        object : SkinQuotaReservation {
            override fun transfer(anchor: File, actual: SkinQuotaRequest) = Unit
            override fun release() = Unit
        },
    )
}
