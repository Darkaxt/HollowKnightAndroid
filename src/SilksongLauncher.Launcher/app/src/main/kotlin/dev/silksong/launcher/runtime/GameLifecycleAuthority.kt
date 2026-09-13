package dev.silksong.launcher.runtime

import java.io.Closeable
import java.io.File
import java.io.RandomAccessFile
import java.nio.channels.FileChannel
import java.nio.channels.FileLock
import java.nio.channels.OverlappingFileLockException
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING

/** Result of a mutation attempted while holding the game's exclusive lifecycle lease. */
data class InactiveGameResult<T>(val state: GameProcessState, val value: T?)

/**
 * Cooperative cross-process lifecycle authority for one profile.
 *
 * The game owns the file lock for its entire process lifetime. Launcher writes are allowed only
 * while holding that same lock and after a clean inactive state has been established. A stale
 * active/pending or malformed state therefore fails closed without process enumeration.
 */
class GameLifecycleAuthority(private val root: File) {
    private val lockFile = File(root, LOCK_FILE)
    private val stateFile = File(root, STATE_FILE)

    fun <T> runIfInactive(action: () -> T): InactiveGameResult<T> {
        val attempt = tryAcquireLease()
        if (attempt.isFailure) return InactiveGameResult(GameProcessState.UNKNOWN, null)
        val lease = attempt.getOrNull()
            ?: return InactiveGameResult(GameProcessState.ACTIVE, null)
        return lease.use {
            when (readState()) {
                LifecycleState.CLOSED, null -> InactiveGameResult(GameProcessState.INACTIVE, action())
                LifecycleState.ACTIVE, LifecycleState.PENDING, LifecycleState.MALFORMED ->
                    InactiveGameResult(GameProcessState.UNKNOWN, null)
            }
        }
    }

    fun acquireForGame(): GameLifecycleOwner {
        val attempt = tryAcquireLease()
        val lease = attempt.getOrElse { throw IllegalStateException("Could not acquire game lifecycle lease", it) }
            ?: throw IllegalStateException("The game lifecycle lease is already owned")
        return try {
            writeState(LifecycleState.ACTIVE)
            GameLifecycleOwner(lease) { state -> writeState(state) }
        } catch (failure: Throwable) {
            lease.close()
            throw failure
        }
    }

    fun markLaunchPending() {
        writeWithExclusiveLease(LifecycleState.PENDING)
    }

    fun markLaunchCancelled() {
        writeWithExclusiveLease(LifecycleState.CLOSED)
    }

    private fun writeWithExclusiveLease(state: LifecycleState) {
        val attempt = tryAcquireLease()
        val lease = attempt.getOrElse { throw IllegalStateException("Could not acquire game lifecycle lease", it) }
            ?: throw IllegalStateException("The game lifecycle lease is already owned")
        lease.use { writeState(state) }
    }

    private fun tryAcquireLease(): Result<Lease?> = runCatching {
        check(root.mkdirs() || root.isDirectory) { "Could not create game lifecycle directory" }
        val channel = RandomAccessFile(lockFile, "rw").channel
        try {
            val lock = try {
                channel.tryLock()
            } catch (_: OverlappingFileLockException) {
                null
            }
            if (lock == null) {
                channel.close()
                null
            } else {
                Lease(channel, lock)
            }
        } catch (failure: Throwable) {
            runCatching { channel.close() }
            throw failure
        }
    }

    private fun readState(): LifecycleState? {
        if (!stateFile.isFile) return null
        return runCatching {
            when (stateFile.readText(Charsets.UTF_8)) {
                "ACTIVE\n" -> LifecycleState.ACTIVE
                "PENDING\n" -> LifecycleState.PENDING
                "CLOSED\n" -> LifecycleState.CLOSED
                else -> LifecycleState.MALFORMED
            }
        }.getOrDefault(LifecycleState.MALFORMED)
    }

    private fun writeState(state: LifecycleState) {
        check(root.mkdirs() || root.isDirectory) { "Could not create game lifecycle directory" }
        val temp = File(root, "$STATE_FILE.tmp")
        temp.writeText("${state.wire}\n", Charsets.UTF_8)
        runCatching { Files.move(temp.toPath(), stateFile.toPath(), ATOMIC_MOVE, REPLACE_EXISTING) }
            .recoverCatching { Files.move(temp.toPath(), stateFile.toPath(), REPLACE_EXISTING) }
            .getOrElse {
                temp.delete()
                throw IllegalStateException("Could not publish game lifecycle state", it)
            }
    }

    class GameLifecycleOwner internal constructor(
        private val lease: Lease,
        private val publish: (LifecycleState) -> Unit,
    ) : Closeable {
        private var closed = false

        fun markStarted() = publishWhileOwned(LifecycleState.ACTIVE)

        fun markStopped() = publishWhileOwned(LifecycleState.CLOSED)

        private fun publishWhileOwned(state: LifecycleState) {
            check(!closed) { "The game lifecycle lease is closed" }
            publish(state)
        }

        override fun close() {
            if (!closed) {
                closed = true
                lease.close()
            }
        }
    }

    internal class Lease(
        private val channel: FileChannel,
        private val lock: FileLock,
    ) : Closeable {
        override fun close() {
            runCatching { lock.release() }
            runCatching { channel.close() }
        }
    }

    internal enum class LifecycleState(val wire: String) {
        ACTIVE("ACTIVE"),
        PENDING("PENDING"),
        CLOSED("CLOSED"),
        MALFORMED(""),
    }

    companion object {
        private const val LOCK_FILE = "game-lifecycle.lock"
        private const val STATE_FILE = "game-lifecycle.state"

        fun forModStateRoot(modStateRoot: File): GameLifecycleAuthority =
            GameLifecycleAuthority(File(modStateRoot, "lifecycle"))
    }
}
