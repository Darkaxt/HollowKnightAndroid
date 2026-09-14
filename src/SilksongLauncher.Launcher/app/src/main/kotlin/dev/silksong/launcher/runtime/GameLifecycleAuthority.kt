package dev.silksong.launcher.runtime

import java.io.Closeable
import java.io.File
import java.io.RandomAccessFile
import java.nio.channels.FileChannel
import java.nio.channels.FileLock
import java.nio.channels.OverlappingFileLockException

/** Result of a mutation attempted while holding the game's exclusive lifecycle lease. */
data class InactiveGameResult<T>(val state: GameProcessState, val value: T?)

/**
 * Transient cross-process lifecycle authority for one profile.
 *
 * The game owns one byte-range lock for its process lifetime. The launcher temporarily owns a
 * second range while a game launch is crossing process boundaries. Neither lock carries data, and
 * the operating system releases both automatically if their owning process exits.
 */
class GameLifecycleAuthority(private val root: File) {
    private val lockFile = File(root, LOCK_FILE)
    private val pendingKey = lockFile.toPath().toAbsolutePath().normalize().toString()

    fun <T> runIfInactive(action: () -> T): InactiveGameResult<T> {
        val activeAttempt = tryAcquireLease(ACTIVE_OFFSET)
        if (activeAttempt.isFailure) return InactiveGameResult(GameProcessState.UNKNOWN, null)
        val activeLease = activeAttempt.getOrNull()
            ?: return InactiveGameResult(GameProcessState.ACTIVE, null)
        return activeLease.use {
            val pendingAttempt = tryAcquireLease(PENDING_OFFSET)
            if (pendingAttempt.isFailure) return@use InactiveGameResult(GameProcessState.UNKNOWN, null)
            val pendingLease = pendingAttempt.getOrNull()
                ?: return@use InactiveGameResult(GameProcessState.UNKNOWN, null)
            pendingLease.use { InactiveGameResult(GameProcessState.INACTIVE, action()) }
        }
    }

    fun acquireForGame(): GameLifecycleOwner {
        val attempt = tryAcquireLease(ACTIVE_OFFSET)
        val lease = attempt.getOrElse { throw IllegalStateException("Could not acquire game lifecycle lease", it) }
            ?: throw IllegalStateException("The game lifecycle lease is already owned")
        return GameLifecycleOwner(lease)
    }

    fun markLaunchPending() {
        val activeAttempt = tryAcquireLease(ACTIVE_OFFSET)
        val activeLease = activeAttempt.getOrElse {
            throw IllegalStateException("Could not acquire game lifecycle lease", it)
        } ?: throw IllegalStateException("The game lifecycle lease is already owned")
        activeLease.use {
            synchronized(pendingLock) {
                check(pendingLaunch == null) { "A game launch is already pending" }
                val pendingAttempt = tryAcquireLease(PENDING_OFFSET)
                val pendingLease = pendingAttempt.getOrElse {
                    throw IllegalStateException("Could not acquire launch-transition lease", it)
                } ?: throw IllegalStateException("The launch-transition lease is already owned")
                pendingLaunch = PendingLaunch(pendingKey, pendingLease)
            }
        }
    }

    fun markLaunchCancelled() = clearLaunchPending()

    fun clearLaunchPending() {
        synchronized(pendingLock) {
            val owned = pendingLaunch?.takeIf { it.key == pendingKey } ?: return
            pendingLaunch = null
            owned.lease.close()
        }
    }

    private fun tryAcquireLease(offset: Long): Result<Lease?> = runCatching {
        check(root.mkdirs() || root.isDirectory) { "Could not create game lifecycle directory" }
        val channel = RandomAccessFile(lockFile, "rw").channel
        try {
            val lock = try {
                channel.tryLock(offset, LOCK_LENGTH, false)
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

    class GameLifecycleOwner internal constructor(private val lease: Lease) : Closeable {
        override fun close() = lease.close()
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

    private data class PendingLaunch(val key: String, val lease: Lease)

    companion object {
        private const val LOCK_FILE = "game-lifecycle.lock"
        private const val ACTIVE_OFFSET = 0L
        private const val PENDING_OFFSET = 1L
        private const val LOCK_LENGTH = 1L
        private val pendingLock = Any()
        private var pendingLaunch: PendingLaunch? = null

        fun clearProcessLaunchPending() {
            synchronized(pendingLock) {
                val owned = pendingLaunch
                pendingLaunch = null
                owned?.lease?.close()
            }
        }

        fun forModStateRoot(modStateRoot: File): GameLifecycleAuthority =
            GameLifecycleAuthority(File(modStateRoot, "lifecycle"))
    }
}
