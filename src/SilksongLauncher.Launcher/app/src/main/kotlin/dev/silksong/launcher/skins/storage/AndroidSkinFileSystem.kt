package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.channels.Channels
import java.nio.channels.FileChannel
import java.nio.channels.SeekableByteChannel
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.FileStore
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.Path
import java.nio.file.Paths
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardOpenOption.CREATE_NEW
import java.nio.file.StandardOpenOption.READ
import java.nio.file.StandardOpenOption.TRUNCATE_EXISTING
import java.nio.file.StandardOpenOption.WRITE
import java.nio.file.attribute.BasicFileAttributes
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.nio.file.attribute.PosixFilePermissions

internal data class SkinMountIdentity(val device: String, val mountId: String)

internal fun interface SkinMountIdentityProvider {
    fun identity(path: Path): SkinMountIdentity?
}

internal object SkinMountInfoParser {
    fun select(path: Path, deviceNumber: Long, bytes: ByteArray): SkinMountIdentity? {
        require(bytes.size <= MAX_BYTES) { "Mount table exceeds its bound" }
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
        val lines = text.lineSequence().filter(String::isNotEmpty).toList()
        require(lines.size <= MAX_LINES) { "Mount table has too many rows" }
        val target = path.toAbsolutePath().normalize()
        val expectedDevice = linuxDevice(deviceNumber)
        val candidates = lines.mapNotNull { line ->
            val fields = line.split(' ')
            val separator = fields.indexOf("-")
            require(separator >= 6 && fields.size > separator + 2) { "Mount table row is malformed" }
            require(fields[0].toLongOrNull() != null && fields[1].toLongOrNull() != null) {
                "Mount table identity is malformed"
            }
            require(Regex("[0-9]+:[0-9]+").matches(fields[2])) { "Mount table device is malformed" }
            val mountPoint = Paths.get(unescape(fields[4])).toAbsolutePath().normalize()
            if (fields[2] != expectedDevice || !target.startsWith(mountPoint)) return@mapNotNull null
            MountRow(
                mountPoint.nameCount,
                "${fields[0]}|${fields[2]}|${unescape(fields[3])}|$mountPoint|${fields[separator + 1]}|${fields[separator + 2]}",
            )
        }
        val longest = candidates.maxOfOrNull(MountRow::depth) ?: return null
        val exact = candidates.filter { it.depth == longest }
        if (exact.size != 1) return null
        return SkinMountIdentity("$deviceNumber|$expectedDevice", exact.single().identity)
    }

    private fun linuxDevice(device: Long): String {
        val major = ((device ushr 8) and 0xfffL) or ((device ushr 32) and 0xfffff000L)
        val minor = (device and 0xffL) or ((device ushr 12) and 0xffffff00L)
        return "$major:$minor"
    }

    private fun unescape(value: String): String = Regex("\\\\([0-7]{3})").replace(value) { match ->
        match.groupValues[1].toInt(8).toChar().toString()
    }

    private data class MountRow(val depth: Int, val identity: String)

    const val MAX_BYTES = 1024 * 1024
    const val MAX_LINES = 4096
}

private object PlatformSkinMountIdentityProvider : SkinMountIdentityProvider {
    override fun identity(path: Path): SkinMountIdentity? = if (isWindowsHost()) {
        val absolute = path.toAbsolutePath().normalize()
        val root = absolute.root?.toString() ?: return null
        val store = Files.getFileStore(absolute)
        val storeId = listOf(store.name(), store.type(), store.isReadOnly.toString(), store.totalSpace.toString()).joinToString("|")
        SkinMountIdentity(device = "$root|$storeId", mountId = "windows:$root|$storeId")
    } else {
        val device = (Files.getAttribute(path, "unix:dev", NOFOLLOW_LINKS) as? Number)?.toLong()
            ?: return null
        SkinMountInfoParser.select(path, device, mountInfoBytes())
    }

    private fun mountInfoBytes(): ByteArray {
        val source = Paths.get("/proc/self/mountinfo")
        val output = ByteArrayOutputStream()
        Files.newInputStream(source, READ).use { input ->
            val buffer = ByteArray(8192)
            var count = 0
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                count = Math.addExact(count, read)
                require(count <= SkinMountInfoParser.MAX_BYTES) { "Mount table exceeds its bound" }
                output.write(buffer, 0, read)
            }
        }
        return output.toByteArray()
    }

    private fun isWindowsHost(): Boolean =
        System.getProperty("os.name", "").orEmpty().startsWith("Windows", ignoreCase = true)
}

class AndroidSkinFileSystem private constructor(
    private val mountIdentityProvider: SkinMountIdentityProvider,
    @Suppress("UNUSED_PARAMETER") marker: Boolean,
) : SkinFileSystem, SkinFileSystemSecurity {
    constructor() : this(PlatformSkinMountIdentityProvider, true)
    internal constructor(mountIdentityProvider: SkinMountIdentityProvider) : this(mountIdentityProvider, true)
    override fun exists(file: File): Boolean = Files.exists(file.toPath(), NOFOLLOW_LINKS)
    override fun isDirectory(file: File): Boolean = Files.isDirectory(file.toPath(), NOFOLLOW_LINKS)
    override fun isRegularFile(file: File): Boolean = Files.isRegularFile(file.toPath(), NOFOLLOW_LINKS)
    override fun isSymbolicLink(file: File): Boolean = Files.isSymbolicLink(file.toPath())

    override fun createDirectory(path: File) {
        try {
            Files.createDirectory(
                path.toPath(),
                PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rwx------")),
            )
        } catch (_: UnsupportedOperationException) {
            Files.createDirectory(path.toPath())
        }
    }

    override fun writeNew(path: File, bytes: ByteArray) {
        openOutput(path, createNew = true).use { it.write(bytes) }
    }

    override fun openNoFollow(path: File): InputStream = Channels.newInputStream(
        openSeekableNoFollow(path),
    )

    override fun openSeekableNoFollow(file: File): SeekableByteChannel =
        Files.newByteChannel(file.toPath(), setOf<java.nio.file.OpenOption>(READ, NOFOLLOW_LINKS))

    override fun openOutput(file: File, createNew: Boolean): OutputStream {
        val options = if (createNew) {
            setOf<java.nio.file.OpenOption>(WRITE, CREATE_NEW, NOFOLLOW_LINKS)
        } else {
            setOf<java.nio.file.OpenOption>(WRITE, TRUNCATE_EXISTING, NOFOLLOW_LINKS)
        }
        return Channels.newOutputStream(Files.newByteChannel(file.toPath(), options))
    }

    override fun syncFile(path: File) {
        FileChannel.open(path.toPath(), WRITE, NOFOLLOW_LINKS).use { it.force(true) }
    }

    override fun syncDirectory(path: File) {
        try {
            FileChannel.open(path.toPath(), READ, NOFOLLOW_LINKS).use { it.force(true) }
        } catch (error: Exception) {
            // Windows cannot open directory FileChannels. This host-only fallback does
            // not apply on Android/Linux, where an unavailable barrier fails closed.
            if (!System.getProperty("os.name", "").orEmpty().startsWith("Windows", ignoreCase = true)) throw error
        }
    }

    override fun atomicMove(source: File, target: File) {
        try {
            Files.move(source.toPath(), target.toPath(), ATOMIC_MOVE)
        } catch (error: AtomicMoveNotSupportedException) {
            throw IllegalStateException("Atomic directory rename is unavailable", error)
        }
    }

    override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
        val ownerPath = owner.toPath().toAbsolutePath().normalize()
        val targetPath = path.toPath().toAbsolutePath().normalize()
        require(targetPath.startsWith(ownerPath)) { "Path escapes its fixed owner" }

        val ownerEvidence = evidence(ownerPath)
        require(ownerEvidence.directory) { "Fixed owner is not a directory" }
        val ownerStore = ownerEvidence.store
        val ownerDevice = ownerEvidence.device
        val ownerMount = ownerEvidence.mountId
        val snapshots = mutableListOf(ownerPath to ownerEvidence)
        val seenKeys = linkedSetOf(ownerEvidence.fileKey)
        var cursor = ownerPath
        if (targetPath != ownerPath) {
            val relative = ownerPath.relativize(targetPath)
            for ((index, name) in relative.withIndex()) {
                cursor = cursor.resolve(name)
                if (!Files.exists(cursor, NOFOLLOW_LINKS)) {
                    require(allowMissingLeaf && index == relative.nameCount - 1) { "Contained path component is missing" }
                    break
                }
                val current = evidence(cursor)
                require(current.store == ownerStore) { "Contained path crosses a file-store boundary" }
                require(current.device == ownerDevice) { "Contained path crosses a device boundary" }
                require(current.mountId == ownerMount) { "Contained path crosses a mount boundary" }
                if (current.strongIdentity) {
                    require(seenKeys.add(current.fileKey)) {
                        "Contained path contains an identity alias at $cursor (${current.fileKey})"
                    }
                }
                if (index < relative.nameCount - 1) require(current.directory) { "Contained ancestor is not a directory" }
                snapshots += cursor to current
            }
        }
        for ((component, before) in snapshots) {
            require(evidence(component).stableEquals(before)) { "Contained path identity changed" }
        }
    }

    override fun deleteContained(path: File, owner: File) {
        requireContained(path, owner)
        val ownerPath = owner.toPath().toAbsolutePath().normalize()
        val targetPath = path.toPath().toAbsolutePath().normalize()
        require(targetPath != ownerPath) { "Cleanup cannot delete its fixed owner" }
        if (!Files.exists(targetPath, NOFOLLOW_LINKS)) return

        val nodes = mutableListOf<Pair<Path, Evidence>>()
        val files = mutableListOf<Path>()
        val directories = mutableListOf<Path>()
        val seenRegularFiles = mutableListOf<Path>()
        val queue = ArrayDeque<Path>()
        queue.addLast(targetPath)
        while (queue.isNotEmpty()) {
            val current = queue.removeFirst()
            requireContained(current.toFile(), owner)
            val currentEvidence = evidence(current)
            nodes += current to currentEvidence
            require(nodes.size <= MAX_DELETE_NODES) { "Contained cleanup node bound exceeded" }
            if (currentEvidence.directory) {
                directories.add(current)
                Files.newDirectoryStream(current).use { stream ->
                    for (child in stream) {
                        require(child.normalize().parent == current) { "Cleanup child escapes its parent" }
                        if (Files.isSymbolicLink(child)) throw IllegalStateException("Cleanup alias is forbidden")
                        val childEvidence = evidence(child)
                        require(childEvidence.store == currentEvidence.store) { "Cleanup crosses a file-store boundary" }
                        require(childEvidence.device == currentEvidence.device) { "Cleanup crosses a device boundary" }
                        require(childEvidence.mountId == currentEvidence.mountId) { "Cleanup crosses a mount boundary" }
                        if (childEvidence.directory) {
                            queue.addLast(child)
                        } else {
                            require(childEvidence.regularFile) { "Cleanup special node is forbidden" }
                            if (seenRegularFiles.any { Files.isSameFile(it, child) }) {
                                throw IllegalStateException("Cleanup hard-link alias is forbidden")
                            }
                            seenRegularFiles.add(child)
                            nodes += child to childEvidence
                            require(nodes.size <= MAX_DELETE_NODES) { "Contained cleanup node bound exceeded" }
                            files.add(child)
                        }
                    }
                }
            } else {
                require(currentEvidence.regularFile) { "Cleanup special node is forbidden" }
                files.add(current)
            }
        }
        nodes.forEach { (node, before) ->
            require(evidence(node).stableEquals(before)) { "Cleanup identity changed" }
        }
        files.distinct().forEach { child ->
            requireContained(child.toFile(), owner)
            Files.delete(child)
        }
        directories.sortedByDescending { it.nameCount }.forEach { directory ->
            requireContained(directory.toFile(), owner)
            Files.delete(directory)
        }
    }

    override fun list(path: File): List<File> = Files.newDirectoryStream(path.toPath()).use { stream ->
        stream.map(Path::toFile).toList()
    }

    override fun identity(path: File): SkinNodeIdentity {
        val evidence = evidence(path.toPath())
        return SkinNodeIdentity(evidence.fileKey, evidence.size, evidence.regularFile)
    }

    override fun sameFile(left: File, right: File): Boolean = Files.isSameFile(left.toPath(), right.toPath())

    private fun evidence(path: Path): Evidence {
        require(!Files.isSymbolicLink(path)) { "Symbolic or reparse path is forbidden" }
        val before = Files.readAttributes(path, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
        require(!before.isSymbolicLink && !before.isOther) { "Alias or special path is forbidden" }
        require(before.isDirectory || before.isRegularFile) { "Unsupported filesystem node" }
        if (before.isRegularFile && !isWindowsHost()) {
            val links = try {
                (Files.getAttribute(path, "unix:nlink", NOFOLLOW_LINKS) as Number).toLong()
            } catch (error: Exception) {
                throw IllegalStateException("Hard-link identity evidence is unavailable", error)
            }
            require(links == 1L) { "Hard-linked filesystem node is forbidden" }
        }
        val mountBefore = mountIdentityProvider.identity(path)
            ?: throw IllegalStateException("Mount or device identity is unavailable")
        val key = identityKey(before)
        val store = storeEvidence(Files.getFileStore(path))
        val after = Files.readAttributes(path, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
        val afterKey = identityKey(after)
        val mountAfter = mountIdentityProvider.identity(path)
            ?: throw IllegalStateException("Mount or device identity is unavailable")
        require(mountBefore == mountAfter) { "Mount or device identity changed while inspected" }
        require(
            key == afterKey && before.isDirectory == after.isDirectory &&
                before.isRegularFile == after.isRegularFile && (!before.isRegularFile || before.size() == after.size()),
        ) { "Filesystem identity changed while inspected" }
        val fullKey = "${mountBefore.device}|${mountBefore.mountId}|${key.value}"
        return Evidence(
            fullKey,
            before.size(),
            before.isRegularFile,
            before.isDirectory,
            store,
            mountBefore.device,
            mountBefore.mountId,
            key.strong,
        )
    }

    private fun identityKey(attributes: BasicFileAttributes): IdentityKey {
        attributes.fileKey()?.toString()?.let { return IdentityKey(it, strong = true) }
        if (!isWindowsHost()) throw IllegalStateException("Filesystem identity is unavailable")
        // Windows host JVMs can omit fileKey. This move-stable weak fallback is
        // deliberately unavailable on Android/Linux, where missing inode evidence fails closed.
        return IdentityKey(
            "windows-host:${attributes.creationTime()}:${attributes.lastModifiedTime()}:${attributes.isDirectory}:${attributes.size()}",
            strong = false,
        )
    }

    private fun isWindowsHost(): Boolean =
        System.getProperty("os.name", "").orEmpty().startsWith("Windows", ignoreCase = true)

    private fun storeEvidence(store: FileStore): StoreEvidence = try {
        StoreEvidence(store.name(), store.type(), store.isReadOnly, store.totalSpace)
    } catch (error: Exception) {
        throw IllegalStateException("File-store evidence is unavailable", error)
    }

    private data class IdentityKey(val value: String, val strong: Boolean)

    private data class Evidence(
        val fileKey: String,
        val size: Long,
        val regularFile: Boolean,
        val directory: Boolean,
        val store: StoreEvidence,
        val device: String,
        val mountId: String,
        val strongIdentity: Boolean,
    ) {
        fun stableEquals(other: Evidence): Boolean =
            fileKey == other.fileKey && strongIdentity == other.strongIdentity &&
                regularFile == other.regularFile && directory == other.directory &&
                (!regularFile || size == other.size) && store == other.store &&
                device == other.device && mountId == other.mountId
    }

    private data class StoreEvidence(
        val name: String,
        val type: String,
        val readOnly: Boolean,
        val totalSpace: Long,
    )

    private companion object {
        const val MAX_DELETE_NODES = 512
    }
}
