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
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.atomic.AtomicLong

fun interface SkinCapacityReserver {
    fun reserve(bytes: Long): SkinResult<SkinCapacityReservation>
}

/** Optional stable identity for reconciling failed cleanup across reconstructed capacity adapters. */
internal interface SkinCapacityReconciliationIdentity {
    val capacityReconciliationIdentity: Any
}

internal class SkinObjectReconciliationIdentity(private val owner: Any) {
    override fun equals(other: Any?): Boolean =
        other is SkinObjectReconciliationIdentity && owner === other.owner

    override fun hashCode(): Int = System.identityHashCode(owner)
}

private fun SkinCapacityReserver.reconciliationIdentity(): Any =
    (this as? SkinCapacityReconciliationIdentity)?.capacityReconciliationIdentity
        ?: SkinObjectReconciliationIdentity(this)

interface SkinCapacityReservation {
    /** Transfers this reservation to the successful owned archive lifetime. */
    fun transfer(file: File, actualBytes: Long)
    fun release()
}

class SkinQuarantine(
    private val paths: SkinPaths,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    private val capacity: SkinCapacityReserver,
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun copy(input: SkinImportInput): SkinResult<QuarantinedArchive> = copyOwned(input, paths.quarantine)

    /** Caller-owned import-handle seam; the owner must already be an exact canonical UUID child. */
    internal fun copy(input: SkinImportInput, owner: File): SkinResult<QuarantinedArchive> {
        val normalized = owner.absoluteFile.normalize()
        val expectedParent = paths.importHandles.absoluteFile.normalize()
        val canonical = runCatching { UUID.fromString(normalized.name) }.getOrNull()
        if (normalized.parentFile != expectedParent || canonical?.toString() != normalized.name) {
            return SkinResult.Error(SkinImportCode.INVALID_INPUT, "Quarantine owner is not a canonical import handle")
        }
        return copyOwned(input, normalized)
    }

    private fun copyOwned(input: SkinImportInput, quarantineOwner: File): SkinResult<QuarantinedArchive> {
        if (!PendingQuarantineCleanups.reconcile(capacity.reconciliationIdentity())) {
            return SkinResult.Error(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                "A prior quarantine cleanup still has ambiguous evidence",
            )
        }
        val reservation = when (val reserved = capacity.reserve(limits.quarantineBytes)) {
            is SkinResult.Error -> return reserved
            is SkinResult.Ok -> reserved.value
        }
        var stagingRoot: File? = null
        var cleanupAncestors: Map<File, String> = emptyMap()
        var copiedBytes = 0L
        var transferred = false
        try {
            ensureOwnedDirectories(quarantineOwner)
            cleanupAncestors = ancestorIdentities(quarantineOwner)
            val nonce = "${System.nanoTime()}-${NEXT.incrementAndGet()}"
            stagingRoot = File(quarantineOwner, "quarantine-$nonce")
            createOwnedDirectory(stagingRoot, paths.staging)
            val archiveFile = File(stagingRoot, "archive")
            fileSystem.requireContained(archiveFile, paths.staging, allowMissingLeaf = true)

            val digest = MessageDigest.getInstance("SHA-256")
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
                        if (copiedBytes > limits.quarantineBytes - read) throw LimitException()
                        if (prefixCount < prefix.size) {
                            val copied = minOf(read, prefix.size - prefixCount)
                            buffer.copyInto(prefix, prefixCount, 0, copied)
                            prefixCount += copied
                        }
                        output.write(buffer, 0, read)
                        digest.update(buffer, 0, read)
                        copiedBytes += read
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
                byteCount = copiedBytes,
                archiveName = input.displayName ?: "archive-${digestHex.take(12)}",
            )
            when {
                isZip(prefix, prefixCount) -> {
                    reservation.transfer(archiveFile, copiedBytes)
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
                        fileSystem.syncDirectory(quarantineOwner)
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
                    val pending = PendingQuarantineCleanup(
                        reservation,
                        owned,
                        paths.staging,
                        quarantineOwner,
                        fileSystem,
                        paths.profileRoot,
                        cleanupAncestors,
                    )
                    if (!pending.settleCurrentEvidence()) {
                        PendingQuarantineCleanups.retain(capacity.reconciliationIdentity(), pending)
                    }
                    return SkinResult.Error(
                        SkinImportCode.DURABILITY_UNAVAILABLE,
                        "Quarantine cleanup failed: ${cleanupError.message}",
                    )
                }
            }
        }
    }

    private fun ancestorIdentities(owner: File): Map<File, String> {
        val profile = paths.profileRoot.absoluteFile.normalize()
        val identities = linkedMapOf<File, String>()
        var cursor = owner.absoluteFile.normalize()
        while (true) {
            fileSystem.requireContained(cursor, profile)
            require(fileSystem.isDirectory(cursor) && !fileSystem.isSymbolicLink(cursor))
            val identity = fileSystem.identity(cursor)
            require(!identity.regularFile && identity.fileKey.isNotBlank())
            identities[cursor] = identity.fileKey
            if (cursor == profile) return identities
            cursor = requireNotNull(cursor.parentFile)
        }
    }

    private fun ensureOwnedDirectories(quarantineOwner: File) {
        require(fileSystem.exists(paths.profileRoot) && fileSystem.isDirectory(paths.profileRoot)) {
            "Profile root is unavailable"
        }
        ensureDirectory(paths.root, paths.profileRoot)
        ensureDirectory(paths.staging, paths.profileRoot)
        if (quarantineOwner == paths.quarantine.absoluteFile.normalize()) {
            ensureDirectory(paths.quarantine, paths.profileRoot)
        } else {
            fileSystem.requireContained(paths.importHandles, paths.staging)
            fileSystem.requireContained(quarantineOwner, paths.importHandles)
            require(fileSystem.isDirectory(paths.importHandles) && !fileSystem.isSymbolicLink(paths.importHandles)) {
                "Import handle root is unsafe"
            }
            require(fileSystem.isDirectory(quarantineOwner) && !fileSystem.isSymbolicLink(quarantineOwner)) {
                "Import handle owner is unsafe"
            }
        }
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

/** Keeps an ambiguous failed-cleanup reservation reachable and retries it before this reserver admits more work. */
private object PendingQuarantineCleanups {
    private val entries = mutableMapOf<Any, MutableList<PendingQuarantineCleanup>>()

    @Synchronized
    fun retain(identity: Any, cleanup: PendingQuarantineCleanup) {
        entries.getOrPut(identity, ::mutableListOf).add(cleanup)
    }

    @Synchronized
    fun reconcile(identity: Any): Boolean {
        val pending = entries[identity] ?: return true
        pending.removeAll(PendingQuarantineCleanup::retry)
        if (pending.isEmpty()) entries.remove(identity)
        return pending.isEmpty()
    }
}

private class PendingQuarantineCleanup(
    private val reservation: SkinCapacityReservation,
    private val owned: File?,
    private val staging: File,
    private val quarantine: File,
    private val fs: SkinFileSystem,
    private val profile: File,
    private val ancestorIdentities: Map<File, String>,
) {
    fun retry(): Boolean {
        try {
            requireOriginalAncestors()
            owned?.let { directory ->
                if (fs.exists(directory)) fs.deleteContained(directory, staging)
                fs.syncDirectory(quarantine)
            }
        } catch (_: Exception) {
            // Exact post-failure evidence below decides whether the reservation can be settled.
        }
        return settleCurrentEvidence()
    }

    fun settleCurrentEvidence(): Boolean = when (val evidence = evidence()) {
        CleanupEvidence.Absent -> try {
            reservation.release()
            true
        } catch (_: Exception) {
            false
        }
        is CleanupEvidence.Archive -> try {
            reservation.transfer(evidence.file, evidence.logicalBytes)
            true
        } catch (_: Exception) {
            false
        }
        CleanupEvidence.Ambiguous -> false
    }

    private fun evidence(): CleanupEvidence {
        return try {
            val directory = owned ?: return CleanupEvidence.Absent
            requireOriginalAncestors()
            if (!fs.exists(directory)) {
                return if (hasStableAbsentSubtree(directory)) CleanupEvidence.Absent else CleanupEvidence.Ambiguous
            }
            fs.requireContained(directory, staging, allowMissingLeaf = true)
            val existsBefore = fs.exists(directory)
            val existsAfter = fs.exists(directory)
            if (existsBefore != existsAfter) return CleanupEvidence.Ambiguous
            if (!existsBefore) return CleanupEvidence.Absent
            if (!fs.isDirectory(directory) || fs.isSymbolicLink(directory)) return CleanupEvidence.Ambiguous

            val archive = File(directory, "archive")
            fs.requireContained(archive, staging, allowMissingLeaf = true)
            val archiveExistsBefore = fs.exists(archive)
            val archiveExistsAfter = fs.exists(archive)
            if (!archiveExistsBefore || archiveExistsBefore != archiveExistsAfter) return CleanupEvidence.Ambiguous
            if (!fs.isRegularFile(archive) || fs.isSymbolicLink(archive)) return CleanupEvidence.Ambiguous
            val identity = fs.identity(archive)
            if (!identity.regularFile || identity.size < 0L || fs.identity(archive) != identity) {
                CleanupEvidence.Ambiguous
            } else {
                CleanupEvidence.Archive(archive, identity.size)
            }
        } catch (_: Exception) {
            CleanupEvidence.Ambiguous
        }
    }

    private fun requireOriginalAncestors() {
        require(ancestorIdentities.isNotEmpty() && fs.exists(profile)) { "Original quarantine profile is absent" }
        for ((ancestor, fileKey) in ancestorIdentities) {
            if (!fs.exists(ancestor)) continue
            fs.requireContained(ancestor, profile)
            require(fs.isDirectory(ancestor) && !fs.isSymbolicLink(ancestor)) { "Quarantine ancestor is aliased" }
            val identity = fs.identity(ancestor)
            // Android inode keys survive child changes. The weak Windows host fallback may not;
            // changed evidence remains ambiguous rather than weakening physical-owner checks.
            require(!identity.regularFile && identity.fileKey == fileKey) { "Quarantine ancestor was replaced" }
        }
    }

    private fun hasStableAbsentSubtree(directory: File): Boolean {
        var missing = directory.absoluteFile.normalize()
        val owner = staging.absoluteFile.normalize()
        while (missing != owner && missing.toPath().startsWith(owner.toPath()) && !fs.exists(missing)) {
            val parent = requireNotNull(missing.parentFile)
            if (fs.exists(parent)) {
                fs.requireContained(parent, staging)
                val before = fs.identity(parent)
                require(!before.regularFile && fs.isDirectory(parent) && !fs.isSymbolicLink(parent))
                // Only the immediate missing child is passed to the containment authority.
                fs.requireContained(missing, staging, allowMissingLeaf = true)
                val absent = !fs.exists(missing)
                requireOriginalAncestors()
                return absent && !fs.exists(missing) && fs.identity(parent) == before
            }
            missing = parent
        }
        return false
    }

    private sealed interface CleanupEvidence {
        data object Absent : CleanupEvidence
        data object Ambiguous : CleanupEvidence
        data class Archive(val file: File, val logicalBytes: Long) : CleanupEvidence
    }
}
