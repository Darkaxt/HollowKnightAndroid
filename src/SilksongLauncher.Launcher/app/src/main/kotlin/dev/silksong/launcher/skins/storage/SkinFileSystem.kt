package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.channels.SeekableByteChannel

interface SkinFileSystem {
    fun createDirectory(path: File)
    fun writeNew(path: File, bytes: ByteArray)
    fun syncFile(path: File)
    fun syncDirectory(path: File)
    fun atomicMove(source: File, target: File)
    fun openNoFollow(path: File): InputStream
    fun identity(path: File): SkinNodeIdentity
    fun list(path: File): List<File>
    fun deleteContained(path: File, owner: File)
}

internal interface SkinFileSystemSecurity {
    fun exists(file: File): Boolean
    fun isDirectory(file: File): Boolean
    fun isRegularFile(file: File): Boolean
    fun isSymbolicLink(file: File): Boolean
    fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean = false)
    fun sameFile(left: File, right: File): Boolean
    fun openOutput(file: File, createNew: Boolean = true): OutputStream
    fun openSeekableNoFollow(file: File): SeekableByteChannel
}

private fun SkinFileSystem.security(): SkinFileSystemSecurity =
    this as? SkinFileSystemSecurity
        ?: throw IllegalStateException("Skin filesystem security capability is unavailable")

internal fun SkinFileSystem.exists(file: File): Boolean = security().exists(file)
internal fun SkinFileSystem.isDirectory(file: File): Boolean = security().isDirectory(file)
internal fun SkinFileSystem.isRegularFile(file: File): Boolean = security().isRegularFile(file)
internal fun SkinFileSystem.isSymbolicLink(file: File): Boolean = security().isSymbolicLink(file)
internal fun SkinFileSystem.requireContained(path: File, owner: File, allowMissingLeaf: Boolean = false) =
    security().requireContained(path, owner, allowMissingLeaf)
internal fun SkinFileSystem.sameFile(left: File, right: File): Boolean = security().sameFile(left, right)
internal fun SkinFileSystem.openOutput(file: File, createNew: Boolean = true): OutputStream =
    security().openOutput(file, createNew)
internal fun SkinFileSystem.openSeekableNoFollow(file: File): SeekableByteChannel =
    security().openSeekableNoFollow(file)
internal fun SkinFileSystem.openInput(file: File): InputStream = openNoFollow(file)
