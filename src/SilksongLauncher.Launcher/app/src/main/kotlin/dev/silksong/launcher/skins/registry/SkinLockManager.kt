package dev.silksong.launcher.skins.registry

import java.io.File
import java.nio.channels.FileChannel
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.StandardOpenOption.CREATE
import java.nio.file.StandardOpenOption.READ
import java.nio.file.StandardOpenOption.WRITE
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

class SkinLockManager(skinsRoot: File) {
    internal val root = skinsRoot.absoluteFile.normalize()
    private val lockRoot = File(root, "locks")
    private val processLock = processLocks.computeIfAbsent(root.path) { ReentrantLock(true) }

    init {
        require(root.name == "skins" && root.parentFile?.name == SkinRegistryAuthority.PROFILE_ID) {
            "Skin lock root must be the exact Hollow Knight profile skins child"
        }
        require(root.parentFile != null && root != root.parentFile) { "Skin lock root has no profile owner" }
    }

    fun <T> withSessionThenRegistry(action: () -> T): T = processLock.withLock {
        if (processLock.holdCount > 1) return@withLock action()
        ensureLockRoot()
        lockChannel(File(lockRoot, "session.lock")).use { sessionChannel ->
            sessionChannel.lock().use {
                lockChannel(File(lockRoot, "registry.lock")).use { registryChannel ->
                    registryChannel.lock().use { action() }
                }
            }
        }
    }

    private fun ensureLockRoot() {
        val ownerFile = requireNotNull(root.parentFile)
        val owner = ownerFile.toPath()
        require(Files.isDirectory(owner, NOFOLLOW_LINKS) && !Files.isSymbolicLink(owner)) {
            "Skin profile owner must be an existing no-alias directory"
        }
        if (!Files.exists(root.toPath(), NOFOLLOW_LINKS)) {
            Files.createDirectory(root.toPath())
            syncDirectory(root)
            syncDirectory(ownerFile)
        }
        require(Files.isDirectory(root.toPath(), NOFOLLOW_LINKS) && !Files.isSymbolicLink(root.toPath())) {
            "Skin root must be an existing no-alias directory"
        }
        val path = lockRoot.toPath()
        if (!Files.exists(path, NOFOLLOW_LINKS)) {
            Files.createDirectory(path)
            syncDirectory(lockRoot)
            syncDirectory(root)
        }
        require(Files.isDirectory(path, NOFOLLOW_LINKS) && !Files.isSymbolicLink(path)) {
            "Skin lock path is not a no-alias directory"
        }
    }

    private fun syncDirectory(directory: File) {
        try {
            FileChannel.open(
                directory.toPath(),
                setOf<java.nio.file.OpenOption>(READ, NOFOLLOW_LINKS),
            ).use { it.force(true) }
        } catch (error: Exception) {
            if (!System.getProperty("os.name", "").orEmpty().startsWith("Windows", ignoreCase = true)) throw error
        }
    }

    private fun lockChannel(file: File): FileChannel {
        require(file.parentFile == lockRoot && file.name in LOCK_NAMES) { "Unbounded skin lock name" }
        val channel = FileChannel.open(
            file.toPath(),
            setOf<java.nio.file.OpenOption>(WRITE, CREATE, NOFOLLOW_LINKS),
        )
        if (!Files.isRegularFile(file.toPath(), NOFOLLOW_LINKS) || Files.isSymbolicLink(file.toPath())) {
            channel.close()
            throw IllegalStateException("Skin lock is not a no-alias regular file")
        }
        return channel
    }

    private companion object {
        val LOCK_NAMES = setOf("session.lock", "registry.lock")
        val processLocks = ConcurrentHashMap<String, ReentrantLock>()
    }
}
