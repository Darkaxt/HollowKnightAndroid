package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.contracts.*
import dev.silksong.launcher.skins.importing.*
import dev.silksong.launcher.skins.quota.*
import dev.silksong.launcher.skins.registry.CandidatePreparationSummary
import dev.silksong.launcher.skins.registry.SkinPreparationHandle
import dev.silksong.launcher.skins.storage.*
import dev.silksong.launcher.skins.ui.*
import java.io.File
import java.util.UUID

/** Existing bounded ZIP/PNG pipeline, immutable publication, then one Kotlin metadata update. */
internal class SkinLibraryImporter(private val store: SkinLibraryStore, decoder: PngDecoder) : SkinImportService {
    override val available = true
    private val fs = store.fs
    private val normalizer = SkinNormalizer(store.catalog, decoder, fs)
    private val builder = SkinObjectBuilder(fs)
    private val publisher = DurableDirectoryPublisher(fs)
    private data class Preparation(val owner: File, val candidates: List<CandidatePreparationResult>)
    private val handles = linkedMapOf<UUID, Preparation>()
    // Failed preparation and partial cancellation retain exact owners, never a tree-wide collector.
    private val pendingCleanup = linkedMapOf<UUID, File>()

    override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> = store.locked {
        store.readLocked()
        pendingCleanup.keys.toList().forEach(::retryCleanup)
        require(handles.size + pendingCleanup.size < 4) { "Cancel existing preparations before importing another archive" }
        val reservation = store.quota.reserve(SkinQuotaBudgets.IMPORT_PREPARATION).required()
        val id = UUID.randomUUID(); val owner = File(store.paths.importHandles, id.toString())
        pendingCleanup[id] = owner
        try {
            store.ensureDirectory(store.paths.staging); store.ensureDirectory(store.paths.importHandles)
            store.ensureDirectory(owner)
            val archive = SkinQuarantine(store.paths, fs, SkinQuotaCapacityReserver(store.quota)).copy(input, owner).required()
            val candidates = normalizer.prepare(archive).required()
            val handle = SkinPreparationHandle(id, candidates.map { candidate -> when (candidate) {
                is CandidatePreparationResult.Ready -> candidate.candidate.let {
                    CandidatePreparationSummary(it.rawPrefix.hex(), it.candidateKey, it.name, SkinImportCode.OK, "Validated; ready to import") }
                is CandidatePreparationResult.Rejected -> CandidatePreparationSummary(candidate.rawPrefix.hex(), null, null, candidate.code, candidate.detail)
            } })
            handles[id] = Preparation(owner, candidates)
            pendingCleanup.remove(id)
            SkinResult.Ok(handle)
        } catch (failure: Exception) {
            try { retryCleanup(id) }
            catch (cleanup: Exception) { throw IllegalStateException("${failure.message}; ${cleanup.message}", failure) }
            throw failure
        } finally { reservation.release() }
    }
    override fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>> = store.locked {
        val preparation = handles[handleId] ?: error("Preparation expired; select the archive again")
        val summaries = preparation.candidates.map { candidate -> when (candidate) {
            is CandidatePreparationResult.Rejected -> SkinImportSummary(candidate.rawPrefix.hex(), candidate.code, null, candidate.detail, emptyList())
            is CandidatePreparationResult.Ready -> publish(candidate.candidate, null)
        } }
        cancel(handleId).required()
        SkinResult.Ok(summaries)
    }
    override fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary> = store.locked {
        val preparation = handles[request.handleId] ?: error("Preparation expired; select the archive again")
        val current = store.readLocked()
        val target = current.packs.singleOrNull { it.id == request.target.id } ?: error("Replacement target was removed")
        require(target.treeSha256 == request.target.treeSha256 && target.receiptSha256 == request.target.receiptSha256) {
            "Replacement target changed; refresh and confirm again"
        }
        store.checkEditable(current, target.id)
        val candidate = preparation.candidates.filterIsInstance<CandidatePreparationResult.Ready>()
            .singleOrNull { it.candidate.candidateKey == request.sourceCandidateKey }?.candidate ?: error("Prepared candidate is absent")
        val summary = publish(candidate, target)
        cancel(request.handleId).required()
        SkinResult.Ok(summary)
    }
    override fun cancel(handleId: UUID): SkinResult<Unit> = store.locked {
        // Once cleanup starts, partial preparation bytes cannot be published again.
        handles.remove(handleId)?.let { pendingCleanup[handleId] = it.owner }
        retryCleanup(handleId)
        SkinResult.Ok(Unit)
    }
    override fun retryPendingCleanup(): SkinResult<Unit> = store.locked {
        pendingCleanup.keys.toList().forEach(::retryCleanup)
        SkinResult.Ok(Unit)
    }
    private fun retryCleanup(handleId: UUID) {
        val owner = pendingCleanup[handleId] ?: return
        try { cleanupOwner(owner) }
        catch (failure: Exception) {
            throw IllegalStateException("Retained preparation cleanup failed; retry Cancel or select an archive again: ${failure.message}", failure)
        }
        pendingCleanup.remove(handleId)
    }
    private fun cleanupOwner(owner: File) {
        if (!fs.exists(owner)) return
        fs.requireContained(owner, store.paths.importHandles)
        require(fs.isDirectory(owner)) { "Preparation owner is not a directory" }
        // Fixed pipeline levels, not recursive traversal: a UUID has one quarantine, containing
        // archive + normalized-N. Each candidate/object/receipt is independently below the existing
        // deleteContained node bound. Missing children after a partial cleanup are naturally skipped.
        for (quarantine in fs.listBounded(owner, 1)) {
            require(quarantine.parentFile == owner && quarantine.name.startsWith("quarantine-")) { "Unexpected preparation child" }
            fs.requireContained(quarantine, owner)
            require(fs.isDirectory(quarantine)) { "Quarantine is not a directory" }
            for (entry in fs.listBounded(quarantine, 2).sortedBy { it.name }) {
                require(entry.parentFile == quarantine) { "Unexpected quarantine child" }
                fs.requireContained(entry, owner)
                if (entry.name == "archive") {
                    require(fs.isRegularFile(entry)) { "Quarantine archive is not a regular file" }
                } else {
                    require(entry.name.matches(Regex("normalized-[0-9]+")) && fs.isDirectory(entry)) { "Unexpected normalization root" }
                    // Includes incomplete publication units; listing remains bounded even on failure.
                    for (unit in fs.listBounded(entry, SkinLimits.V1.entries).sortedBy { it.name }) {
                        require(unit.parentFile == entry && (unit.name.matches(Regex("candidate-[0-9]{3}")) ||
                            unit.name.startsWith("object-") || unit.name.startsWith("receipt-"))) { "Unexpected normalization child" }
                        fs.requireContained(unit, owner)
                        require(fs.isDirectory(unit)) { "Normalization unit is not a directory" }
                        fs.deleteContained(unit, owner)
                    }
                    fs.listBounded(entry, 0)
                }
                fs.deleteContained(entry, owner)
            }
            fs.listBounded(quarantine, 0)
            fs.deleteContained(quarantine, owner)
        }
        fs.listBounded(owner, 0)
        fs.deleteContained(owner, store.paths.importHandles)
    }
    private fun publish(candidate: PreparedSkinCandidate, replacing: LibraryPack?): SkinImportSummary {
        val existing = store.readLocked().packs.singleOrNull { it.candidateKey == candidate.candidateKey }
        if (replacing == null && existing != null) {
            store.requireVerified(existing)
            return SkinImportSummary(candidate.rawPrefix.hex(), SkinImportCode.OK, existing.id, "Already installed", emptyList())
        }
        val request = SkinQuotaRequest.profile(*(candidate.payloads.map { it.length } +
            listOf(candidate.importReceiptBytes.size.toLong(), 262144L, 65536L, 4L * SkinLibraryCodec.MAX_BYTES, 4096L)).toLongArray())
        val reserved = store.quota.reserve(request).required()
        try {
            val built = builder.build(candidate, replacing?.id ?: "skin-${candidate.candidateKey.take(32)}").required()
            val objectRoot = store.paths.objectRoot(built.treeSha256)
            publisher.publish(built.ephemeralRoot, objectRoot, store.paths.profileRoot) { store.objects.verify(it, built.treeSha256).required() }.required()
            // Published roots are never deleted, including when later receipt/config publication fails.
            val receiptStage = File(candidate.stagingRoot.parentFile, "receipt-${UUID.randomUUID()}")
            store.ensureDirectory(receiptStage)
            fs.writeNew(File(receiptStage, "import-receipt.json"), built.importReceiptBytes)
            publisher.publish(receiptStage, store.paths.importReceiptRoot(built.importReceiptSha256), store.paths.profileRoot) {
                store.receipts.verify(it, built.importReceiptSha256).required()
            }.required()
            store.install(LibraryPack(built.id, built.name, "Unknown", built.candidateKey, built.treeSha256, built.importReceiptSha256), replacing).required()
            return SkinImportSummary(candidate.rawPrefix.hex(), SkinImportCode.OK, built.id, "Imported; configuration remains under Kotlin control", emptyList())
        } finally { reserved.release() }
    }
    private fun ByteArray.hex() = joinToString("") { "%02x".format(it.toInt() and 255) }
}
