package dev.silksong.launcher.skins.fixtures

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.channels.SeekableByteChannel

class FaultingSkinFileSystem(
    private val delegate: SkinFileSystem = AndroidSkinFileSystem(),
) : SkinFileSystem by delegate, SkinFileSystemSecurity {
    private val security = delegate as? SkinFileSystemSecurity
        ?: throw IllegalArgumentException("Delegate lacks skin filesystem security capability")
    val events = mutableListOf<String>()
    var failOnEvent: String? = null
    var failOnOccurrence: Int = 1
    private val occurrences = mutableMapOf<String, Int>()

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

    override fun openNoFollow(path: File): InputStream = delegate.openNoFollow(path)
    override fun openOutput(file: File, createNew: Boolean): OutputStream = delegate.openOutput(file, createNew)

    override fun syncFile(path: File) {
        event("sync-file:${path.name}")
        delegate.syncFile(path)
    }

    override fun syncDirectory(path: File) {
        event("sync-dir:${path.name}")
        delegate.syncDirectory(path)
    }

    override fun atomicMove(source: File, target: File) {
        event("rename:${source.name}->${target.name}")
        delegate.atomicMove(source, target)
    }

    override fun deleteContained(path: File, owner: File) {
        event("delete-contained:${path.name}")
        delegate.deleteContained(path, owner)
    }

    override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
        event("contained:${path.name}")
        delegate.requireContained(path, owner, allowMissingLeaf)
    }

    override fun identity(path: File): SkinNodeIdentity = delegate.identity(path)
}
