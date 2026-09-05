package dev.silksong.launcher.skins.fixtures

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.channels.SeekableByteChannel

class FaultingSkinFileSystem(
    private val delegate: SkinFileSystem = AndroidSkinFileSystem(),
) : SkinFileSystem by delegate, SkinFileSystemSecurity, SkinFileSystemBoundedListing {
    private val security = delegate as? SkinFileSystemSecurity
        ?: throw IllegalArgumentException("Delegate lacks skin filesystem security capability")
    private val boundedListing = delegate as? SkinFileSystemBoundedListing
    val events = mutableListOf<String>()
    var failOnEvent: String? = null
    var failOnOccurrence: Int = 1
    var skipPhysicalSyncs: Boolean = false
    var beforeContainment: ((path: File, owner: File, allowMissingLeaf: Boolean) -> Unit)? = null
    var beforeDelete: ((path: File, owner: File) -> Unit)? = null
    var afterMove: ((source: File, target: File) -> Unit)? = null
    private val occurrences = mutableMapOf<String, Int>()
    private val contentReads = mutableMapOf<String, Int>()

    fun contentReadCount(path: File): Int = contentReads.getOrDefault(path.absoluteFile.normalize().path, 0)

    fun clearContentReadCounts() {
        contentReads.clear()
    }

    private fun event(value: String) {
        events += value
        val occurrence = occurrences.getOrDefault(value, 0) + 1
        occurrences[value] = occurrence
        if (value == failOnEvent && occurrence == failOnOccurrence) {
            throw IllegalStateException("injected failure: $value#$occurrence")
        }
    }

    override fun exists(file: File): Boolean = security.exists(file)
    override fun isDirectory(file: File): Boolean = security.isDirectory(file)
    override fun isRegularFile(file: File): Boolean = security.isRegularFile(file)
    override fun isSymbolicLink(file: File): Boolean = security.isSymbolicLink(file)
    override fun sameFile(left: File, right: File): Boolean = security.sameFile(left, right)
    override fun openSeekableNoFollow(file: File): SeekableByteChannel =
        security.openSeekableNoFollow(file)

    override fun createDirectory(path: File) {
        event("mkdir:${path.name}")
        delegate.createDirectory(path)
    }

    override fun writeNew(path: File, bytes: ByteArray) {
        event("write-new:${path.name}")
        delegate.writeNew(path, bytes)
    }

    override fun openNoFollow(path: File): InputStream {
        event("read:${path.name}")
        val key = path.absoluteFile.normalize().path
        contentReads[key] = contentReads.getOrDefault(key, 0) + 1
        return delegate.openNoFollow(path)
    }

    override fun openOutput(file: File, createNew: Boolean): OutputStream {
        event("write-existing:${file.name}")
        return delegate.openOutput(file, createNew)
    }

    override fun syncFile(path: File) {
        event("sync-file:${path.name}")
        if (!skipPhysicalSyncs) delegate.syncFile(path)
    }

    override fun syncDirectory(path: File) {
        event("sync-dir:${path.name}")
        if (!skipPhysicalSyncs) delegate.syncDirectory(path)
    }

    override fun atomicMove(source: File, target: File) {
        event("rename:${source.name}->${target.name}")
        delegate.atomicMove(source, target)
        afterMove?.invoke(source, target)
    }

    override fun deleteContained(path: File, owner: File) {
        event("delete-contained:${path.name}")
        beforeDelete?.invoke(path, owner)
        delegate.deleteContained(path, owner)
    }

    override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
        event("contained:${path.name}")
        beforeContainment?.invoke(path, owner, allowMissingLeaf)
        delegate.requireContained(path, owner, allowMissingLeaf)
    }

    override fun identity(path: File): SkinNodeIdentity = delegate.identity(path)

    override fun listBounded(path: File, maximumEntries: Int): List<File> {
        event("list-bounded:${path.name}:$maximumEntries")
        val bounded = boundedListing
            ?: throw IllegalStateException("Bounded skin filesystem listing capability is unavailable")
        return bounded.listBounded(path, maximumEntries)
    }
}
