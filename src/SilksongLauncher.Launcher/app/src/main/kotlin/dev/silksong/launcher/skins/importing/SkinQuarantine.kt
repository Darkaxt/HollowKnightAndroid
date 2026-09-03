package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicLong

fun interface SkinCapacityReserver {
    fun reserve(bytes: Long): SkinResult<SkinCapacityReservation>
}

interface SkinCapacityReservation {
    /** Transfers this reservation to the successful owned archive lifetime. */
    fun transfer(file: File, actualBytes: Long)
    fun release()
}

object UnboundedSkinCapacityReserver : SkinCapacityReserver {
    override fun reserve(bytes: Long): SkinResult<SkinCapacityReservation> = SkinResult.Ok(
        object : SkinCapacityReservation {
            override fun transfer(file: File, actualBytes: Long) = Unit
            override fun release() = Unit
        },
    )
}

class SkinQuarantine(
    private val paths: SkinPaths,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    private val capacity: SkinCapacityReserver = UnboundedSkinCapacityReserver,
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun copy(input: SkinImportInput): SkinResult<QuarantinedArchive> {
        val reservation = when (val reserved = capacity.reserve(SkinLimits.V1.quarantineBytes)) {
            is SkinResult.Error -> return reserved
            is SkinResult.Ok -> reserved.value
        }
        var stagingRoot: File? = null
        var transferred = false
        try {
            ensureOwnedDirectories()
            val nonce = "${System.nanoTime()}-${NEXT.incrementAndGet()}"
            stagingRoot = File(paths.quarantine, "quarantine-$nonce")
            createOwnedDirectory(stagingRoot, paths.staging)
            val archiveFile = File(stagingRoot, "archive")
            fileSystem.requireContained(archiveFile, paths.staging, allowMissingLeaf = true)

            val digest = MessageDigest.getInstance("SHA-256")
            var count = 0L
            val prefix = ByteArray(8)
            var prefixCount = 0
            input.openOnce().use { provider ->
                fileSystem.requireContained(archiveFile, paths.staging, allowMissingLeaf = true)
                fileSystem.openOutput(archiveFile, createNew = true).use { output ->
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        val read = provider.read(buffer)
                        if (read < 0) break
                        if (read == 0) continue
                        if (count > limits.quarantineBytes - read) throw LimitException()
                        if (prefixCount < prefix.size) {
                            val copied = minOf(read, prefix.size - prefixCount)
                            buffer.copyInto(prefix, prefixCount, 0, copied)
                            prefixCount += copied
                        }
                        output.write(buffer, 0, read)
                        digest.update(buffer, 0, read)
                        count += read
                    }
                }
            }
            fileSystem.requireContained(archiveFile, paths.staging)
            fileSystem.syncFile(archiveFile)
            fileSystem.syncDirectory(stagingRoot)

            val digestHex = digest.digest().toHex()
            val archive = QuarantinedArchive(
                file = archiveFile,
                archiveSha256 = digestHex,
                byteCount = count,
                archiveName = input.displayName ?: "archive-${digestHex.take(12)}",
            )
            when {
                isZip(prefix, prefixCount) -> {
                    reservation.transfer(archiveFile, count)
                    transferred = true
                    return SkinResult.Ok(archive)
                }
                isRar(prefix, prefixCount) -> {
                    return SkinResult.Error(SkinImportCode.UNSUPPORTED_RAR, "RAR archives are not supported")
                }
                else -> return SkinResult.Error(SkinImportCode.INVALID_INPUT, "Input magic is neither ZIP nor RAR")
            }
        } catch (_: LimitException) {
            return SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Quarantine exceeds ${limits.quarantineBytes} bytes")
        } catch (error: Exception) {
            return SkinResult.Error(SkinImportCode.INVALID_INPUT, "Provider quarantine failed: ${error.message}")
        } finally {
            if (!transferred) {
                var cleanupError: Exception? = null
                val owned = stagingRoot
                if (owned != null) {
                    try {
                        if (fileSystem.exists(owned)) fileSystem.deleteContained(owned, paths.staging)
                        fileSystem.syncDirectory(paths.quarantine)
                    } catch (error: Exception) {
                        cleanupError = error
                    }
                }
                if (cleanupError == null) {
                    try {
                        reservation.release()
                    } catch (error: Exception) {
                        cleanupError = error
                    }
                }
                if (cleanupError != null) {
                    return SkinResult.Error(
                        SkinImportCode.DURABILITY_UNAVAILABLE,
                        "Quarantine cleanup failed: ${cleanupError.message}",
                    )
                }
            }
        }
    }

    private fun ensureOwnedDirectories() {
        require(fileSystem.exists(paths.profileRoot) && fileSystem.isDirectory(paths.profileRoot)) {
            "Profile root is unavailable"
        }
        ensureDirectory(paths.root, paths.profileRoot)
        ensureDirectory(paths.staging, paths.profileRoot)
        ensureDirectory(paths.quarantine, paths.profileRoot)
    }

    private fun ensureDirectory(directory: File, owner: File) {
        fileSystem.requireContained(directory, owner, allowMissingLeaf = true)
        if (!fileSystem.exists(directory)) fileSystem.createDirectory(directory)
        fileSystem.requireContained(directory, owner)
        require(fileSystem.isDirectory(directory)) { "Owned staging component is not a directory" }
    }

    private fun createOwnedDirectory(directory: File, owner: File) {
        fileSystem.requireContained(directory, owner, allowMissingLeaf = true)
        fileSystem.createDirectory(directory)
        fileSystem.requireContained(directory, owner)
    }

    private fun isZip(prefix: ByteArray, count: Int): Boolean {
        if (count < 4 || prefix[0] != 0x50.toByte() || prefix[1] != 0x4b.toByte()) return false
        val signature = (prefix[2].toInt() and 0xff) shl 8 or (prefix[3].toInt() and 0xff)
        return signature in setOf(0x0304, 0x0506, 0x0708)
    }

    private fun isRar(prefix: ByteArray, count: Int): Boolean {
        val common = byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07)
        if (count < 7 || !prefix.copyOfRange(0, 6).contentEquals(common)) return false
        return prefix[6] == 0x00.toByte() ||
            (count >= 8 && prefix[6] == 0x01.toByte() && prefix[7] == 0x00.toByte())
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    private class LimitException : Exception()

    private companion object {
        val NEXT = AtomicLong()
    }
}
