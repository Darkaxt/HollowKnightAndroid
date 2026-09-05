package dev.silksong.launcher.skins.quota

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinCapacityReconciliationIdentity
import dev.silksong.launcher.skins.importing.SkinCapacityReservation
import dev.silksong.launcher.skins.importing.SkinCapacityReserver
import dev.silksong.launcher.skins.importing.SkinObjectReconciliationIdentity
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.listBounded
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.io.IOException
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.util.concurrent.ConcurrentHashMap

private const val MAX_LEASE_STATE_BYTES = 64L * 1024
private const val TRANSITION_METADATA_BLOCKS = 10
private const val RECOVERY_METADATA_BLOCKS = 64 * 6 + 8

/** Fixed admission limits. Retention selection and garbage collection are intentionally outside this MVP. */
internal data class SkinQuotaLimits(
    val profileBytes: Long = PROFILE_BYTES,
    val sessionBytes: Long = SESSION_BYTES,
    val allocationBlockBytes: Long = FALLBACK_BLOCK_BYTES,
    val lifecycleMarginBytes: Long = 0L,
) {
    init {
        require(profileBytes in 1..PROFILE_BYTES)
        require(sessionBytes in 1..SESSION_BYTES && sessionBytes <= profileBytes)
        require(allocationBlockBytes == FALLBACK_BLOCK_BYTES)
        require(lifecycleMarginBytes in 0..sessionBytes)
    }

    companion object {
        const val PROFILE_BYTES = 1024L * 1024 * 1024
        const val SESSION_BYTES = 96L * 1024 * 1024
        const val FALLBACK_BLOCK_BYTES = 4096L
        const val LIFECYCLE_MARGIN_BYTES =
            2L * (MAX_LEASE_STATE_BYTES + TRANSITION_METADATA_BLOCKS * FALLBACK_BLOCK_BYTES) +
                2L * (MAX_LEASE_STATE_BYTES + RECOVERY_METADATA_BLOCKS * FALLBACK_BLOCK_BYTES)
        val V1 = SkinQuotaLimits(lifecycleMarginBytes = LIFECYCLE_MARGIN_BYTES)
    }
}

data class SkinQuotaUsage(
    val profileBytes: Long,
    val sessionBytes: Long,
) {
    init {
        require(profileBytes >= 0)
        require(sessionBytes in 0..profileBytes)
    }

    companion object {
        val ZERO = SkinQuotaUsage(0, 0)
    }
}

enum class SkinQuotaScope {
    PROFILE,
    SESSIONS,
}

/** Logical lengths of regular files that one operation plans to add. */
class SkinQuotaRequest private constructor(
    val scope: SkinQuotaScope,
    val logicalFileLengths: List<Long>,
    private val admissionClass: AdmissionClass,
) {
    internal val lifecycle: Boolean get() = admissionClass == AdmissionClass.LIFECYCLE

    init {
        require(logicalFileLengths.isNotEmpty())
        require(logicalFileLengths.all { it >= 0 })
    }

    override fun equals(other: Any?): Boolean =
        other is SkinQuotaRequest && scope == other.scope && logicalFileLengths == other.logicalFileLengths &&
            admissionClass == other.admissionClass

    override fun hashCode(): Int = 31 * (31 * scope.hashCode() + logicalFileLengths.hashCode()) + admissionClass.hashCode()

    override fun toString(): String =
        "SkinQuotaRequest(scope=$scope, logicalFileLengths=$logicalFileLengths, lifecycle=$lifecycle)"

    companion object {
        fun profile(vararg logicalFileLengths: Long): SkinQuotaRequest =
            SkinQuotaRequest(SkinQuotaScope.PROFILE, logicalFileLengths.toList(), AdmissionClass.ORDINARY)

        fun sessions(vararg logicalFileLengths: Long): SkinQuotaRequest =
            SkinQuotaRequest(SkinQuotaScope.SESSIONS, logicalFileLengths.toList(), AdmissionClass.ORDINARY)

        /** Fixed lifecycle classes only; callers cannot choose privileged lengths. */
        internal fun lifecycle(kind: LifecycleKind): SkinQuotaRequest {
            val metadataBlocks = when (kind) {
                LifecycleKind.RECOVERY -> RECOVERY_METADATA_BLOCKS
                LifecycleKind.CLAIM, LifecycleKind.CLOSE -> TRANSITION_METADATA_BLOCKS
            }
            return SkinQuotaRequest(
                SkinQuotaScope.SESSIONS,
                listOf(MAX_LEASE_STATE_BYTES) + List(metadataBlocks) { 1L },
                AdmissionClass.LIFECYCLE,
            )
        }
    }

    private enum class AdmissionClass {
        ORDINARY,
        LIFECYCLE,
    }

    internal enum class LifecycleKind {
        RECOVERY,
        CLAIM,
        CLOSE,
    }
}

/** Conservative fixed budgets include each maximum document and every final/temporary metadata block at peak. */
internal object SkinQuotaBudgets {
    private const val MAX_DOCUMENT_BYTES = 8L * 1024 * 1024
    // Generation digest/marker plus current/previous/next and their conservative temporary peaks.
    private const val REGISTRY_METADATA_BLOCKS = 8
    // Descriptor digest/marker, intent pair, sequence pair, state digest/marker, and four pointer pairs.
    private const val ACQUISITION_METADATA_BLOCKS = 16
    private const val IMPORT_PUBLICATION_METADATA_BLOCKS = 8
    private const val MAX_MANIFEST_BYTES = 64L * 1024
    private const val MAX_OWNERSHIP_PLAN_BYTES = 16L * 1024

    val IMPORT_PREPARATION = SkinQuotaRequest.profile(
        SkinLimits.V1.uncompressedBytes,
        *metadataBlocks(SkinLimits.V1.entries),
    )
    val REGISTRY_PUBLICATION = SkinQuotaRequest.profile(
        MAX_DOCUMENT_BYTES,
        *metadataBlocks(REGISTRY_METADATA_BLOCKS),
    )
    val SESSION_CLAIM = SkinQuotaRequest.lifecycle(SkinQuotaRequest.LifecycleKind.CLAIM)
    val SESSION_CLOSE = SkinQuotaRequest.lifecycle(SkinQuotaRequest.LifecycleKind.CLOSE)
    val SESSION_RECOVERY = SkinQuotaRequest.lifecycle(SkinQuotaRequest.LifecycleKind.RECOVERY)

    /** Reserved from ordinary admissions in both caps; exact lifecycle requests may consume it. */
    val LIFECYCLE_MARGIN_BYTES = SkinQuotaLimits.LIFECYCLE_MARGIN_BYTES

    /**
     * One pre-mutation ordinary reservation covers bounded recovery, possible registry genesis,
     * successful acquisition publication, and the extra close transition on acquisition failure.
     */
    val SESSION_ACQUISITION = SkinQuotaRequest.sessions(
        *(
            SESSION_RECOVERY.logicalFileLengths +
                REGISTRY_PUBLICATION.logicalFileLengths +
                listOf(MAX_DOCUMENT_BYTES, MAX_LEASE_STATE_BYTES) +
                metadataBlocks(ACQUISITION_METADATA_BLOCKS).toList() +
                SESSION_CLOSE.logicalFileLengths
            ).toLongArray(),
    )

    fun importStaging(logicalFileLengths: List<Long>): SkinQuotaRequest {
        require(logicalFileLengths.size <= SkinLimits.V1.entries) { "Prepared staging file count exceeds the V1 bound" }
        require(logicalFileLengths.all { it in 0..SkinLimits.V1.textureBytes }) {
            "Prepared staging file length exceeds the V1 texture bound"
        }
        val total = logicalFileLengths.fold(0L, Math::addExact)
        require(total <= SkinLimits.V1.uncompressedBytes) { "Prepared staging bytes exceed the V1 extraction bound" }
        return SkinQuotaRequest.profile(*(logicalFileLengths.ifEmpty { listOf(0L) }).toLongArray())
    }

    fun importCandidate(payloadLengths: List<Long>, receiptBytes: Long): SkinQuotaRequest {
        require(payloadLengths.isNotEmpty() && payloadLengths.size <= SkinLimits.V1.mappings) {
            "Candidate payload count is outside the V1 bound"
        }
        require(payloadLengths.all { it in 0..SkinLimits.V1.textureBytes }) {
            "Candidate payload length exceeds the V1 texture bound"
        }
        val payloadBytes = payloadLengths.fold(0L, Math::addExact)
        require(payloadBytes <= SkinLimits.V1.payloadBytes) { "Candidate payload bytes exceed the V1 bound" }
        require(receiptBytes in 1..MAX_DOCUMENT_BYTES) { "Import receipt bytes are outside the V1 document bound" }
        val lengths = payloadLengths + listOf(
            MAX_MANIFEST_BYTES,
            MAX_DOCUMENT_BYTES,
            receiptBytes,
            MAX_OWNERSHIP_PLAN_BYTES,
        ) + metadataBlocks(IMPORT_PUBLICATION_METADATA_BLOCKS).toList() +
            REGISTRY_PUBLICATION.logicalFileLengths
        return SkinQuotaRequest.profile(*lengths.toLongArray())
    }

    fun fallbackCharge(request: SkinQuotaRequest): Long = request.logicalFileLengths.fold(0L) { total, length ->
        Math.addExact(total, roundedLogicalLength(length, SkinQuotaLimits.FALLBACK_BLOCK_BYTES))
    }

    private fun metadataBlocks(count: Int) = LongArray(count) { 1L }
}

interface SkinQuotaReservation {
    /** Completes one reservation only after its owned bytes are visible beneath the exact bound root. */
    fun transfer(anchor: File, actual: SkinQuotaRequest)

    /** Releases an untransferred reservation. Repeated release is harmless. */
    fun release()
}

interface SkinQuotaAdmission {
    val root: File
    fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation>
}

/** Stable no-alias physical ledger identity; intentionally unavailable to generic admissions. */
internal interface SkinQuotaReconciliationIdentity {
    val quotaReconciliationIdentity: Any
}

/** Mandatory bridge from bounded profile admission to the existing one-copy quarantine contract. */
class SkinQuotaCapacityReserver(
    private val quota: SkinQuotaAdmission,
) : SkinCapacityReserver, SkinCapacityReconciliationIdentity {
    override val capacityReconciliationIdentity: Any =
        (quota as? SkinQuotaReconciliationIdentity)?.quotaReconciliationIdentity
            ?: SkinObjectReconciliationIdentity(quota)

    override fun reserve(bytes: Long): SkinResult<SkinCapacityReservation> {
        val request = try {
            SkinQuotaRequest.profile(bytes)
        } catch (error: Exception) {
            return SkinResult.Error(
                SkinImportCode.PROFILE_QUOTA_EXCEEDED,
                "Quarantine quota request is invalid: ${error.message}",
            )
        }
        return when (val reserved = quota.reserve(request)) {
            is SkinResult.Error -> reserved
            is SkinResult.Ok -> SkinResult.Ok(
                object : SkinCapacityReservation {
                    override fun transfer(file: File, actualBytes: Long) {
                        reserved.value.transfer(file, SkinQuotaRequest.profile(actualBytes))
                    }

                    override fun release() {
                        reserved.value.release()
                    }
                },
            )
        }
    }
}

sealed interface SkinAllocatedBytes {
    data class Available(val bytes: Long) : SkinAllocatedBytes
    data object Unavailable : SkinAllocatedBytes
    data class Ambiguous(val detail: String) : SkinAllocatedBytes
}

fun interface SkinAllocatedBytesAuthority {
    fun read(file: File): SkinAllocatedBytes
}

/** Uses POSIX allocated 512-byte block evidence when the platform exposes it, without querying FileStore. */
object PlatformSkinAllocatedBytesAuthority : SkinAllocatedBytesAuthority {
    override fun read(file: File): SkinAllocatedBytes = try {
        val value = Files.getAttribute(file.toPath(), "unix:blocks", NOFOLLOW_LINKS)
        val blocks = (value as? Number)?.toLong()
            ?: return SkinAllocatedBytes.Ambiguous("Allocated block metadata is not numeric")
        if (blocks < 0) {
            SkinAllocatedBytes.Ambiguous("Allocated block metadata is negative")
        } else {
            SkinAllocatedBytes.Available(Math.multiplyExact(blocks, 512L))
        }
    } catch (_: UnsupportedOperationException) {
        SkinAllocatedBytes.Unavailable
    } catch (_: IllegalArgumentException) {
        SkinAllocatedBytes.Unavailable
    } catch (error: ArithmeticException) {
        SkinAllocatedBytes.Ambiguous("Allocated byte metadata overflowed")
    } catch (error: IOException) {
        SkinAllocatedBytes.Ambiguous("Allocated byte metadata is unreadable: ${error.message}")
    } catch (error: SecurityException) {
        SkinAllocatedBytes.Ambiguous("Allocated byte metadata is denied: ${error.message}")
    }
}

internal fun interface SkinQuotaAccountingAuthority {
    fun measure(): SkinQuotaUsage
}

/**
 * Read-only, no-follow accounting for the exact Hollow Knight skins tree.
 * Every failure is ambiguous evidence and therefore causes admission to fail closed.
 */
internal class SkinTreeQuotaAccounting(
    skinsRoot: File,
    private val fs: SkinFileSystem,
    private val allocatedBytes: SkinAllocatedBytesAuthority,
    private val fallbackBlockBytes: Long,
    private val maxObservedNodes: Int = MAX_OBSERVED_NODES,
) : SkinQuotaAccountingAuthority {
    private val root = exactRoot(skinsRoot)
    private val profileRoot = requireNotNull(root.parentFile)
    private val sessionsRoot = File(root, "sessions").absoluteFile.normalize()

    init {
        require(fallbackBlockBytes > 0)
        require(maxObservedNodes > 0)
    }

    override fun measure(): SkinQuotaUsage {
        if (!fs.exists(root)) {
            require(fs.exists(profileRoot) && fs.isDirectory(profileRoot) && !fs.isSymbolicLink(profileRoot)) {
                "Exact Hollow Knight profile root is unavailable"
            }
            fs.requireContained(root, profileRoot, allowMissingLeaf = true)
            return SkinQuotaUsage.ZERO
        }
        fs.requireContained(root, root)
        require(fs.isDirectory(root) && !fs.isSymbolicLink(root)) { "Skin root is not a no-alias directory" }

        val queue = ArrayDeque<File>()
        val observed = mutableListOf<Pair<File, SkinNodeIdentity>>()
        val identities = linkedSetOf<String>()
        val paths = linkedSetOf<String>()
        queue.addLast(root)
        var discoveredNodes = 1
        var profileBytes = 0L
        var sessionBytes = 0L
        while (queue.isNotEmpty()) {
            val file = queue.removeFirst().absoluteFile.normalize()
            require(file == root || file.toPath().startsWith(root.toPath())) { "Quota node escapes the exact skin root" }
            require(paths.add(file.path)) { "Quota tree repeats a path" }
            require(paths.size <= maxObservedNodes) { "Quota tree exceeds its accounting bound" }
            fs.requireContained(file, root)
            require(!fs.isSymbolicLink(file)) { "Quota tree contains an alias" }
            val before = fs.identity(file)
            require(before.fileKey.isNotBlank() && identities.add(before.fileKey)) {
                "Quota tree contains ambiguous or duplicate identity evidence"
            }

            val regular = fs.isRegularFile(file)
            val directory = fs.isDirectory(file)
            require(regular.xor(directory)) { "Quota tree contains an unknown filesystem node" }
            require(before.regularFile == regular) { "Quota node type evidence disagrees" }
            if (regular) {
                require(before.size >= 0) { "Quota file length is unknown" }
                val charge = stableAllocatedCharge(file, before.size)
                profileBytes = Math.addExact(profileBytes, charge)
                if (file.toPath().startsWith(sessionsRoot.toPath())) {
                    sessionBytes = Math.addExact(sessionBytes, charge)
                }
            } else {
                val remainingNodes = maxObservedNodes - discoveredNodes
                val first = stableChildren(file, remainingNodes)
                val second = stableChildren(file, remainingNodes)
                require(first.map(File::getPath) == second.map(File::getPath)) {
                    "Quota directory membership changed while inspected"
                }
                discoveredNodes = Math.addExact(discoveredNodes, first.size)
                require(discoveredNodes <= maxObservedNodes) { "Quota tree exceeds its accounting bound" }
                first.forEach(queue::addLast)
            }
            require(fs.identity(file) == before) { "Quota node identity changed while inspected" }
            observed += file to before
        }
        observed.forEach { (file, identity) ->
            require(fs.identity(file) == identity) { "Quota tree identity changed before accounting completed" }
        }
        return SkinQuotaUsage(profileBytes, sessionBytes)
    }

    private fun stableChildren(directory: File, maximumEntries: Int): List<File> {
        val parent = directory.absoluteFile.normalize().toPath()
        return fs.listBounded(directory, maximumEntries).map { child ->
            val normalized = child.absoluteFile.normalize()
            require(normalized.toPath().parent == parent) { "Quota child is outside its exact parent" }
            require(normalized.toPath().startsWith(root.toPath())) { "Quota child escapes the exact skin root" }
            normalized
        }.sortedBy(File::getPath).also { children ->
            require(children.map(File::getPath).distinct().size == children.size) {
                "Quota directory contains duplicate child evidence"
            }
        }
    }

    private fun stableAllocatedCharge(file: File, logicalLength: Long): Long {
        val first = allocatedBytes.read(file)
        val second = allocatedBytes.read(file)
        return when {
            first is SkinAllocatedBytes.Available && second is SkinAllocatedBytes.Available -> {
                require(first.bytes >= 0 && first.bytes == second.bytes) {
                    "Allocated byte evidence is negative or changed"
                }
                first.bytes
            }
            first === SkinAllocatedBytes.Unavailable && second === SkinAllocatedBytes.Unavailable ->
                roundedLogicalLength(logicalLength, fallbackBlockBytes)
            first is SkinAllocatedBytes.Ambiguous -> error(first.detail)
            second is SkinAllocatedBytes.Ambiguous -> error(second.detail)
            else -> error("Allocated byte availability changed while inspected")
        }
    }

    companion object {
        const val MAX_OBSERVED_NODES = 65_536
    }
}

/** Thread-safe admission and outstanding-reservation authority for one exact profile. */
class SkinQuota private constructor(
    skinsRoot: File,
    private val fs: SkinFileSystem,
    private val accounting: SkinQuotaAccountingAuthority,
    configuration: Configuration,
) : SkinQuotaAdmission, SkinQuotaReconciliationIdentity {
    private val limits = configuration.limits
    override val root = exactRoot(skinsRoot)
    private val profileRoot = requireNotNull(root.parentFile)
    private val profileAuthority = physicalRootAuthority(root, fs)
    private val physicalKey = profileAuthority.ledgerKey
    override val quotaReconciliationIdentity: Any = physicalKey
    private val ledger = ledgers.computeIfAbsent(physicalKey) { ReservationLedger() }

    constructor(
        skinsRoot: File,
        fs: SkinFileSystem = AndroidSkinFileSystem(),
        allocatedBytes: SkinAllocatedBytesAuthority = PlatformSkinAllocatedBytesAuthority,
    ) : this(
        exactRoot(skinsRoot),
        fs,
        SkinTreeQuotaAccounting(
            exactRoot(skinsRoot),
            fs,
            allocatedBytes,
            SkinQuotaLimits.FALLBACK_BLOCK_BYTES,
        ),
        Configuration(SkinQuotaLimits.V1),
    )

    fun usage(): SkinResult<SkinQuotaUsage> = synchronized(ledger) {
        try {
            SkinResult.Ok(measureOwnedTree())
        } catch (error: Exception) {
            quotaError("Skin quota evidence is unavailable: ${error.message}")
        }
    }

    override fun reserve(request: SkinQuotaRequest): SkinResult<SkinQuotaReservation> = synchronized(ledger) {
        try {
            val charge = charge(request)
            val usage = measureOwnedTree()
            val profileTotal = Math.addExact(Math.addExact(usage.profileBytes, ledger.reservedProfileBytes), charge.profileBytes)
            val sessionTotal = Math.addExact(Math.addExact(usage.sessionBytes, ledger.reservedSessionBytes), charge.sessionBytes)
            val profileAdmissionLimit = if (request.lifecycle) {
                limits.profileBytes
            } else {
                Math.subtractExact(limits.profileBytes, limits.lifecycleMarginBytes)
            }
            val sessionAdmissionLimit = if (request.lifecycle) {
                limits.sessionBytes
            } else {
                Math.subtractExact(limits.sessionBytes, limits.lifecycleMarginBytes)
            }
            if (profileTotal > profileAdmissionLimit || sessionTotal > sessionAdmissionLimit) {
                return@synchronized quotaError(
                    "Skin profile quota exceeded (profile=$profileTotal/$profileAdmissionLimit, " +
                        "sessions=$sessionTotal/$sessionAdmissionLimit, lifecycle=${request.lifecycle})",
                )
            }
            ledger.reservedProfileBytes = Math.addExact(ledger.reservedProfileBytes, charge.profileBytes)
            ledger.reservedSessionBytes = Math.addExact(ledger.reservedSessionBytes, charge.sessionBytes)
            SkinResult.Ok(Reservation(request.scope, charge))
        } catch (error: Exception) {
            quotaError("Skin quota evidence or arithmetic is unsafe: ${error.message}")
        }
    }

    private fun measureOwnedTree(): SkinQuotaUsage {
        verifyProfileOwner()
        val usage = accounting.measure()
        verifyProfileOwner()
        return usage
    }

    private fun verifyProfileOwner() {
        require(stableProfileIdentity(profileRoot, fs) == profileAuthority.profileIdentity) {
            "Exact Hollow Knight profile owner changed"
        }
    }

    private fun charge(request: SkinQuotaRequest): SkinQuotaUsage {
        val bytes = request.logicalFileLengths.fold(0L) { total, length ->
            Math.addExact(total, roundedLogicalLength(length, limits.allocationBlockBytes))
        }
        return when (request.scope) {
            SkinQuotaScope.PROFILE -> SkinQuotaUsage(bytes, 0)
            SkinQuotaScope.SESSIONS -> SkinQuotaUsage(bytes, bytes)
        }
    }

    private data class Configuration(val limits: SkinQuotaLimits)

    private inner class Reservation(
        private val scope: SkinQuotaScope,
        private val reserved: SkinQuotaUsage,
    ) : SkinQuotaReservation {
        private var state: ReservationState = ReservationState.Open

        override fun transfer(anchor: File, actual: SkinQuotaRequest) = synchronized(ledger) {
            val normalizedAnchor = anchor.absoluteFile.normalize()
            require(normalizedAnchor != root && normalizedAnchor.toPath().startsWith(root.toPath())) {
                "Transferred quota anchor escapes its exact profile"
            }
            require(actual.scope == scope) { "Transferred quota scope changed" }
            if (scope == SkinQuotaScope.SESSIONS) {
                val sessions = File(root, "sessions").absoluteFile.normalize()
                require(normalizedAnchor.toPath().startsWith(sessions.toPath())) {
                    "Session quota anchor is outside the exact sessions root"
                }
            }
            val actualCharge = charge(actual)
            require(actualCharge.profileBytes <= reserved.profileBytes && actualCharge.sessionBytes <= reserved.sessionBytes) {
                "Transferred quota exceeds its reservation"
            }
            when (val current = state) {
                ReservationState.Open -> {
                    verifyProfileOwner()
                    removeOutstanding(reserved)
                    state = ReservationState.Transferred(normalizedAnchor, actual)
                }
                is ReservationState.Transferred -> require(
                    current.anchor == normalizedAnchor && current.actual == actual,
                ) { "Quota reservation was transferred differently" }
                ReservationState.Released -> error("Released quota reservation cannot transfer")
            }
        }

        override fun release() = synchronized(ledger) {
            when (state) {
                ReservationState.Open -> {
                    removeOutstanding(reserved)
                    state = ReservationState.Released
                }
                ReservationState.Released, is ReservationState.Transferred -> Unit
            }
        }
    }

    private fun removeOutstanding(charge: SkinQuotaUsage) {
        ledger.reservedProfileBytes = Math.subtractExact(ledger.reservedProfileBytes, charge.profileBytes)
        ledger.reservedSessionBytes = Math.subtractExact(ledger.reservedSessionBytes, charge.sessionBytes)
        require(ledger.reservedProfileBytes >= 0 && ledger.reservedSessionBytes >= 0) { "Quota reservation ledger underflowed" }
    }

    private sealed interface ReservationState {
        data object Open : ReservationState
        data object Released : ReservationState
        data class Transferred(val anchor: File, val actual: SkinQuotaRequest) : ReservationState
    }

    private data class LedgerKey(val physicalProfileIdentity: String, val exactChildName: String)
    private data class ProfileAuthority(val ledgerKey: LedgerKey, val profileIdentity: String)

    private class ReservationLedger {
        var reservedProfileBytes = 0L
        var reservedSessionBytes = 0L
    }

    private fun quotaError(detail: String) = SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, detail)

    companion object {
        private val ledgers = ConcurrentHashMap<LedgerKey, ReservationLedger>()

        internal fun testing(
            skinsRoot: File,
            fs: SkinFileSystem,
            allocatedBytes: SkinAllocatedBytesAuthority,
            limits: SkinQuotaLimits,
        ): SkinQuota = SkinQuota(
            skinsRoot,
            fs,
            SkinTreeQuotaAccounting(skinsRoot, fs, allocatedBytes, limits.allocationBlockBytes),
            Configuration(limits),
        )

        internal fun testing(
            skinsRoot: File,
            fs: SkinFileSystem,
            accounting: SkinQuotaAccountingAuthority,
            limits: SkinQuotaLimits,
        ): SkinQuota = SkinQuota(skinsRoot, fs, accounting, Configuration(limits))

        private fun physicalRootAuthority(root: File, fs: SkinFileSystem): ProfileAuthority {
            val profileRoot = requireNotNull(root.parentFile)
            val profileBefore = stableProfileIdentity(profileRoot, fs)
            require(!fs.isSymbolicLink(root)) { "Exact Hollow Knight skins root is aliased" }
            if (fs.exists(root)) {
                require(fs.isDirectory(root)) { "Exact Hollow Knight skins root is not a directory" }
                fs.requireContained(root, profileRoot)
                val rootBefore = fs.identity(root)
                require(rootBefore.fileKey.isNotBlank() && !rootBefore.regularFile) {
                    "Exact Hollow Knight skins identity is unavailable"
                }
                require(fs.identity(root) == rootBefore) { "Exact Hollow Knight skins identity changed" }
            } else {
                fs.requireContained(root, profileRoot, allowMissingLeaf = true)
                require(!fs.exists(root)) { "Exact Hollow Knight skins root appeared while inspected" }
            }
            require(stableProfileIdentity(profileRoot, fs) == profileBefore) {
                "Exact Hollow Knight profile identity changed"
            }
            return ProfileAuthority(LedgerKey(profileBefore, root.name), profileBefore)
        }

        private fun stableProfileIdentity(profileRoot: File, fs: SkinFileSystem): String {
            require(fs.exists(profileRoot) && fs.isDirectory(profileRoot) && !fs.isSymbolicLink(profileRoot)) {
                "Exact Hollow Knight profile root is unavailable or aliased"
            }
            return fs.identity(profileRoot).also { identity ->
                require(identity.fileKey.isNotBlank() && !identity.regularFile) {
                    "Exact Hollow Knight profile identity is unavailable"
                }
            }.fileKey
        }
    }
}

private fun exactRoot(file: File): File = file.absoluteFile.normalize().also { root ->
    require(root.name == "skins" && root.parentFile?.name == "hollow-knight") {
        "Quota root must be the exact Hollow Knight profile skins child"
    }
    require(root.parentFile != null && root != root.parentFile) { "Quota root has no exact profile owner" }
}

private fun roundedLogicalLength(length: Long, blockBytes: Long): Long {
    require(length >= 0 && blockBytes > 0)
    if (length == 0L) return 0L
    val remainder = length % blockBytes
    return if (remainder == 0L) length else Math.addExact(length, blockBytes - remainder)
}
