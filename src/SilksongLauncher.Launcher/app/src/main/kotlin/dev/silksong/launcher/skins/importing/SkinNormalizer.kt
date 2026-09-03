package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.CatalogMapping
import dev.silksong.launcher.skins.contracts.PngInfo
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.SkinWarning
import dev.silksong.launcher.skins.contracts.StagedPayload
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinImportReceiptDocument
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.openSeekableNoFollow
import dev.silksong.launcher.skins.storage.requireContained
import java.io.EOFException
import java.io.File
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.text.Normalizer
import java.util.concurrent.atomic.AtomicLong
import java.util.zip.Inflater
import java.util.zip.InflaterInputStream

class SkinNormalizer(
    private val catalog: CatalogPathSet,
    private val decoder: PngDecoder,
    private val fs: SkinFileSystem,
) {
    private var limits: SkinLimits = SkinLimits.V1

    internal constructor(
        catalog: CatalogPathSet,
        decoder: PngDecoder,
        fs: SkinFileSystem,
        limits: SkinLimits,
    ) : this(catalog, decoder, fs) {
        this.limits = limits
    }

    fun prepare(quarantined: QuarantinedArchive): SkinResult<List<CandidatePreparationResult>> {
        val stagingOwner = try {
            stagingOwner(quarantined.file)
        } catch (error: Exception) {
            return SkinResult.Error(SkinImportCode.INVALID_INPUT, "Quarantined archive is outside owned staging: ${error.message}")
        }
        var normalizationRoot: File? = null
        var outcome: SkinResult<List<CandidatePreparationResult>>? = null
        var cleanupFailure: Exception? = null
        try {
            outcome = prepareOwned(quarantined, stagingOwner) { normalizationRoot = it }
        } catch (failure: PreparationFailure) {
            outcome = SkinResult.Error(failure.code, failure.message ?: failure.code.name)
        } catch (error: Exception) {
            outcome = SkinResult.Error(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                "Secure archive preparation failed: ${error.message}",
            )
        } finally {
            val root = normalizationRoot
            if (outcome !is SkinResult.Ok && root != null) {
                try {
                    if (fs.exists(root)) fs.deleteContained(root, stagingOwner)
                } catch (error: Exception) {
                    cleanupFailure = error
                }
            }
        }
        return cleanupFailure?.let {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Normalization cleanup failed: ${it.message}")
        } ?: requireNotNull(outcome)
    }

    private fun prepareOwned(
        quarantined: QuarantinedArchive,
        stagingOwner: File,
        ownRoot: (File) -> Unit,
    ): SkinResult<List<CandidatePreparationResult>> {
        catalog.revalidate()
        fs.requireContained(quarantined.file, stagingOwner)
        if (!archiveUnchanged(quarantined, stagingOwner)) {
            return SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "Quarantined archive identity changed")
        }
        val zip = when (val read = BoundedZipReader(limits, fs).read(quarantined.file)) {
            is SkinResult.Error -> return read
            is SkinResult.Ok -> read.value
        }
        val authorized = when (val paths = ZipPathAuthority(limits).validate(zip)) {
            is SkinResult.Error -> return paths
            is SkinResult.Ok -> paths.value
        }
        val candidates = when (val discovered = SkinCandidateDiscovery(catalog, limits).discover(authorized)) {
            is SkinResult.Error -> return discovered
            is SkinResult.Ok -> discovered.value
        }
        val archiveIdentity = fs.identity(quarantined.file)
        if (!archiveIdentity.regularFile || archiveIdentity.size != quarantined.byteCount) {
            return SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "Quarantined archive identity changed")
        }

        val stagingRoot = File(quarantined.file.parentFile, "normalized-${NEXT.incrementAndGet()}")
        ownRoot(stagingRoot)
        createDirectory(stagingRoot, stagingOwner)
        val mapper = SkinCatalogMapper(catalog, limits)
        val results = mutableListOf<CandidatePreparationResult>()
        for ((index, candidate) in candidates.candidates.withIndex()) {
            val key = try {
                SkinIdentity.candidateKey(quarantined.archiveSha256, candidate.rawPrefix, candidate.layoutCode)
            } catch (error: Exception) {
                abort(SkinImportCode.DOCUMENT_INVALID, error.message.orEmpty())
            }
            val candidateRoot = File(stagingRoot, "candidate-${index.toString().padStart(3, '0')}")
            createDirectory(candidateRoot, stagingOwner)
            when (val mapped = mapper.map(candidate, authorized)) {
                is SkinResult.Error -> {
                    if (mapped.code !in MAPPING_REJECTION_CODES) abort(mapped.code, mapped.detail)
                    cleanup(candidateRoot, stagingOwner)
                    results += CandidatePreparationResult.Rejected(candidate.rawPrefix, mapped.code, mapped.detail)
                }
                is SkinResult.Ok -> {
                    val firstMapped = mapped.value.textures.values.minByOrNull { it.centralIndex }
                    if (firstMapped == null) {
                        cleanup(candidateRoot, stagingOwner)
                        results += CandidatePreparationResult.Rejected(
                            candidate.rawPrefix,
                            SkinImportCode.NO_CANDIDATE,
                            "Candidate has no mapped textures",
                        )
                        continue
                    }
                    val prepared = prepareCandidate(
                        quarantined,
                        authorized,
                        archiveIdentity,
                        candidate.rawPrefix,
                        candidate.layoutCode,
                        firstMapped.flags,
                        key,
                        mapped.value,
                        candidates.warnings.takeIf { index == 0 }.orEmpty(),
                        candidateRoot,
                        stagingOwner,
                    )
                    when (prepared) {
                        is SkinResult.Ok -> results += CandidatePreparationResult.Ready(prepared.value)
                        is SkinResult.Error -> {
                            if (prepared.code !in CANDIDATE_REJECTION_CODES) abort(prepared.code, prepared.detail)
                            cleanup(candidateRoot, stagingOwner)
                            results += CandidatePreparationResult.Rejected(candidate.rawPrefix, prepared.code, prepared.detail)
                        }
                    }
                }
            }
        }
        return if (!archiveUnchanged(quarantined, stagingOwner) || fs.identity(quarantined.file) != archiveIdentity) {
            SkinResult.Error(SkinImportCode.ZIP_CORRUPT, "Quarantined archive identity changed during preparation")
        } else {
            SkinResult.Ok(results)
        }
    }

    private fun archiveUnchanged(quarantined: QuarantinedArchive, owner: File): Boolean {
        fs.requireContained(quarantined.file, owner)
        val before = fs.identity(quarantined.file)
        return before.regularFile && before.size == quarantined.byteCount &&
            hashStable(quarantined.file, before) == quarantined.archiveSha256
    }

    private fun prepareCandidate(
        quarantined: QuarantinedArchive,
        archive: AuthorizedZip,
        archiveIdentity: SkinNodeIdentity,
        rawPrefix: ByteArray,
        layoutCode: Int,
        candidateFlags: Int,
        candidateKey: String,
        mapping: CatalogMapping,
        archiveWarnings: List<SkinWarning>,
        stagingRoot: File,
        stagingOwner: File,
    ): SkinResult<PreparedSkinCandidate> = try {
        if (mapping.textures.isEmpty()) reject(SkinImportCode.NO_CANDIDATE, "Candidate has no mapped textures")
        if (mapping.textures.keys.any { it !in catalog.pathSet }) {
            abort(SkinImportCode.DOCUMENT_INVALID, "Mapping target is outside pinned catalog")
        }
        val sourceEntries = mapping.textures.values.distinctBy { it.centralIndex }.sortedBy { it.centralIndex }
        if (sourceEntries.size > limits.mappings) reject(SkinImportCode.LIMIT_EXCEEDED, "Candidate has too many payloads")
        val sourceNames = mutableMapOf<Int, String>()
        val payloadByPath = linkedMapOf<String, StagedPayload>()
        var payloadBytes = 0L
        fs.requireContained(archive.archive.file, stagingOwner)
        if (fs.identity(archive.archive.file) != archiveIdentity) {
            abort(SkinImportCode.ZIP_CORRUPT, "Archive identity changed before extraction")
        }
        fs.openSeekableNoFollow(archive.archive.file).use { zip ->
            if (zip.size() != archiveIdentity.size || fs.identity(archive.archive.file) != archiveIdentity) {
                abort(SkinImportCode.ZIP_CORRUPT, "Archive identity changed before extraction")
            }
            for (entry in sourceEntries) {
                if (entry.uncompressedSize > limits.textureBytes) reject(SkinImportCode.LIMIT_EXCEEDED, "Texture exceeds byte bound")
                val temporary = File(stagingRoot, ".source-${entry.centralIndex}.tmp")
                fs.requireContained(temporary, stagingOwner, allowMissingLeaf = true)
                val (length, sha256) = extract(zip, entry, temporary, stagingOwner)
                if (length != entry.uncompressedSize || length > limits.textureBytes) {
                    cleanupFile(temporary, stagingOwner)
                    reject(SkinImportCode.LIMIT_EXCEEDED, "Extracted texture exceeds declared or bounded size")
                }
                val inspected = inspect(temporary, length)
                val decoded = when (val result = decoder.decodeAndRelease(temporary, inspected)) {
                    is SkinResult.Error -> {
                        cleanupFile(temporary, stagingOwner)
                        if (result.code !in PNG_REJECTION_CODES) abort(result.code, result.detail)
                        reject(result.code, result.detail)
                    }
                    is SkinResult.Ok -> result.value
                }
                val decodedPixels = decoded.width.toLong() * decoded.height.toLong()
                if (decoded.width != inspected.width || decoded.height != inspected.height ||
                    decoded.pixelCount != decodedPixels || decodedPixels > limits.decodedPixels
                ) {
                    cleanupFile(temporary, stagingOwner)
                    reject(SkinImportCode.PNG_INVALID, "Android decode differs from validated PNG structure")
                }
                val name = SkinIdentity.base32DigestHex(sha256)
                val relative = "assets/$name"
                val destination = File(stagingRoot, relative)
                val destinationParent = destination.parentFile
                    ?: abort(SkinImportCode.DOCUMENT_INVALID, "Payload destination has no parent")
                if (!fs.exists(destinationParent)) createDirectory(destinationParent, stagingOwner)
                val existing = payloadByPath[relative]
                if (existing == null) {
                    payloadBytes = checkedAdd(payloadBytes, length)
                    if (payloadBytes > limits.payloadBytes) {
                        cleanupFile(temporary, stagingOwner)
                        reject(SkinImportCode.LIMIT_EXCEEDED, "Candidate payload bytes exceed bound")
                    }
                    fs.requireContained(temporary, stagingOwner)
                    fs.requireContained(destination, stagingOwner, allowMissingLeaf = true)
                    fs.atomicMove(temporary, destination)
                    fs.requireContained(destination, stagingOwner)
                    payloadByPath[relative] = StagedPayload(relative, sha256, length, destination)
                } else {
                    if (existing.length != length || !filesEqual(existing.file, temporary, stagingOwner)) {
                        cleanupFile(temporary, stagingOwner)
                        reject(SkinImportCode.TARGET_COLLISION, "Digest destination does not contain identical bytes")
                    }
                    cleanupFile(temporary, stagingOwner)
                }
                sourceNames[entry.centralIndex] = name
            }
            if (zip.size() != archiveIdentity.size || fs.identity(archive.archive.file) != archiveIdentity) {
                abort(SkinImportCode.ZIP_CORRUPT, "Archive identity changed during extraction")
            }
        }
        if (fs.identity(archive.archive.file) != archiveIdentity) {
            abort(SkinImportCode.ZIP_CORRUPT, "Archive identity changed during extraction")
        }
        val mappings = linkedMapOf<String, String>()
        catalog.paths.forEach { target ->
            mapping.textures[target]?.let { source -> mappings[target] = sourceNames.getValue(source.centralIndex) }
        }
        val payloads = payloadByPath.values.sortedWith { left, right ->
            SkinIdentity.unsignedUtf8Compare(left.relativePath, right.relativePath)
        }
        val contentSha256 = SkinIdentity.contentSha256(payloads)
        val warnings = mapping.warnings + archiveWarnings
        val entryWarnings = warnings.count { it.sourceRawPathHex.isNotEmpty() }
        val archiveWarningCount = warnings.size - entryWarnings
        if (entryWarnings > 4096 || archiveWarningCount > 10) {
            reject(SkinImportCode.LIMIT_EXCEEDED, "Too many entry or archive import warnings")
        }
        val receipt = SkinImportReceiptDocument(
            candidateKey = candidateKey,
            archiveSha256 = quarantined.archiveSha256,
            archiveName = canonicalArchiveName(quarantined.archiveName, quarantined.archiveSha256),
            candidateRawPathHex = rawPrefix.toHex(),
            layoutCode = layoutCode,
            aliases = mapping.aliases,
            warnings = warnings,
        )
        val receiptBytes = try {
            CanonicalJson.importReceipt(receipt, catalog)
        } catch (error: IllegalArgumentException) {
            if (error.message?.contains("8 MiB") == true) reject(SkinImportCode.LIMIT_EXCEEDED, error.message.orEmpty())
            abort(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Receipt is invalid")
        }
        SkinResult.Ok(
            PreparedSkinCandidate(
                candidateKey,
                rawPrefix.copyOf(),
                layoutCode,
                candidateName(rawPrefix, candidateFlags, candidateKey),
                contentSha256,
                receiptBytes,
                SkinIdentity.sha256(receiptBytes),
                payloads,
                mappings,
                stagingRoot,
            ),
        )
    } catch (error: CandidateFailure) {
        SkinResult.Error(error.code, error.message ?: error.code.name)
    }

    private fun inspect(file: File, length: Long): PngInfo {
        val before = fs.identity(file)
        if (!before.regularFile || before.size != length) {
            abort(SkinImportCode.DURABILITY_UNAVAILABLE, "Extracted PNG identity is invalid")
        }
        val tracked = FailureTrackingInputStream(fs.openNoFollow(file))
        val result = tracked.use { PngStructureValidator(limits).inspect(it, length) }
        tracked.failure?.let { throw it }
        if (fs.identity(file) != before) {
            abort(SkinImportCode.DURABILITY_UNAVAILABLE, "Extracted PNG identity changed while inspected")
        }
        return when (result) {
            is SkinResult.Error -> {
                if (result.code !in PNG_REJECTION_CODES) abort(result.code, result.detail)
                reject(result.code, result.detail)
            }
            is SkinResult.Ok -> result.value
        }
    }

    private fun extract(
        zip: SeekableByteChannel,
        entry: RawZipEntry,
        destination: File,
        owner: File,
    ): Pair<Long, String> {
        val digest = MessageDigest.getInstance("SHA-256")
        var count = 0L
        fs.requireContained(destination, owner, allowMissingLeaf = true)
        fs.openOutput(destination, createNew = true).use { output ->
            val source: InputStream = when (entry.method) {
                0 -> SliceInputStream(zip, entry.dataOffset, entry.compressedSize)
                8 -> InflaterInputStream(SliceInputStream(zip, entry.dataOffset, entry.compressedSize), Inflater(true), 64 * 1024)
                else -> abort(SkinImportCode.UNSUPPORTED_ZIP, "Unsupported compression method")
            }
            source.use { input ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    if (read == 0) continue
                    count = checkedAdd(count, read.toLong())
                    if (count > limits.textureBytes || count > entry.uncompressedSize) {
                        reject(SkinImportCode.LIMIT_EXCEEDED, "Texture extraction exceeds bound")
                    }
                    output.write(buffer, 0, read)
                    digest.update(buffer, 0, read)
                }
            }
        }
        fs.requireContained(destination, owner)
        fs.syncFile(destination)
        return count to digest.digest().toHex()
    }

    private fun candidateName(rawPrefix: ByteArray, flags: Int, candidateKey: String): String {
        if (rawPrefix.isEmpty()) return fallbackName(candidateKey)
        val leaf = rawPrefix.copyOfRange(rawPrefix.indexOfLast { it == '/'.code.toByte() } + 1, rawPrefix.size)
        val candidate = if (flags and 0x0800 != 0) {
            runCatching {
                Normalizer.normalize(
                    StandardCharsets.UTF_8.newDecoder()
                        .onMalformedInput(CodingErrorAction.REPORT)
                        .onUnmappableCharacter(CodingErrorAction.REPORT)
                        .decode(ByteBuffer.wrap(leaf))
                        .toString(),
                    Normalizer.Form.NFKC,
                )
            }.getOrNull()
        } else if (leaf.all { (it.toInt() and 0xff) in 0x20..0x7e }) {
            leaf.toString(Charsets.US_ASCII)
        } else null
        return if (candidate != null && validName(candidate)) candidate else fallbackName(candidateKey)
    }

    private fun canonicalArchiveName(name: String, archiveSha256: String): String {
        val normalized = Normalizer.normalize(name, Normalizer.Form.NFKC)
        return if (validArchiveName(normalized)) normalized else "archive-${archiveSha256.take(12)}"
    }

    private fun validName(value: String): Boolean {
        val scalars = value.codePointCount(0, value.length)
        return scalars in 1..80 && value == value.trim() && '/' !in value && '\\' !in value &&
            !hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in BIDI_CONTROLS }
    }

    private fun validArchiveName(value: String): Boolean {
        val scalars = value.codePointCount(0, value.length)
        return scalars in 1..128 && value == value.trim() && '/' !in value && '\\' !in value &&
            !hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in BIDI_CONTROLS }
    }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            val character = value[index]
            when {
                character.isHighSurrogate() -> {
                    if (index + 1 >= value.length || !value[index + 1].isLowSurrogate()) return true
                    index += 2
                }
                character.isLowSurrogate() -> return true
                else -> index++
            }
        }
        return false
    }

    private fun filesEqual(left: File, right: File, owner: File): Boolean {
        fs.requireContained(left, owner)
        fs.requireContained(right, owner)
        val leftIdentity = fs.identity(left)
        val rightIdentity = fs.identity(right)
        if (leftIdentity.size != rightIdentity.size) return false
        fs.openNoFollow(left).use { a ->
            fs.openNoFollow(right).use { b ->
                val aa = ByteArray(64 * 1024)
                val bb = ByteArray(64 * 1024)
                while (true) {
                    val ac = a.read(aa)
                    val bc = b.read(bb)
                    if (ac != bc) return false
                    if (ac < 0) break
                    if (!aa.copyOf(ac).contentEquals(bb.copyOf(bc))) return false
                }
            }
        }
        return fs.identity(left) == leftIdentity && fs.identity(right) == rightIdentity
    }

    private fun hashStable(file: File, before: SkinNodeIdentity): String {
        val digest = MessageDigest.getInstance("SHA-256")
        var count = 0L
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                count = checkedAdd(count, read.toLong())
                if (count > before.size) return ""
                digest.update(buffer, 0, read)
            }
        }
        return if (fs.identity(file) == before && count == before.size) digest.digest().toHex() else ""
    }

    private fun createDirectory(directory: File, owner: File) {
        fs.requireContained(directory, owner, allowMissingLeaf = true)
        fs.createDirectory(directory)
        fs.requireContained(directory, owner)
    }

    private fun cleanup(target: File, owner: File) {
        if (fs.exists(target)) fs.deleteContained(target, owner)
    }

    private fun cleanupFile(target: File, owner: File) {
        if (!fs.exists(target)) return
        fs.deleteContained(target, owner)
    }

    private fun stagingOwner(path: File): File {
        var cursor: File? = path.absoluteFile.normalize()
        while (cursor != null && cursor.name != "staging") cursor = cursor.parentFile
        val owner = requireNotNull(cursor) { "No SkinPaths.staging ancestor" }
        require(owner.parentFile?.name == "skins") { "No fixed skins ancestor" }
        return owner
    }

    private fun fallbackName(candidateKey: String) = "Imported skin ${candidateKey.take(12)}"

    private fun checkedAdd(left: Long, right: Long): Long {
        if (left < 0 || right < 0 || left > Long.MAX_VALUE - right) reject(SkinImportCode.LIMIT_EXCEEDED, "Size overflow")
        return left + right
    }

    private class SliceInputStream(
        private val channel: SeekableByteChannel,
        offset: Long,
        private var remaining: Long,
    ) : InputStream() {
        init { channel.position(offset) }

        override fun read(): Int {
            if (remaining == 0L) return -1
            val target = ByteBuffer.allocate(1)
            if (channel.read(target) < 0) throw EOFException()
            remaining--
            return target.array()[0].toInt() and 0xff
        }

        override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
            if (remaining == 0L) return -1
            val count = minOf(length.toLong(), remaining).toInt()
            val target = ByteBuffer.wrap(buffer, offset, count)
            while (target.hasRemaining()) {
                if (channel.read(target) < 0) throw EOFException()
            }
            remaining -= count
            return count
        }
    }

    private class FailureTrackingInputStream(
        private val delegate: InputStream,
    ) : InputStream() {
        var failure: Exception? = null
            private set

        override fun read(): Int = track { delegate.read() }
        override fun read(buffer: ByteArray, offset: Int, length: Int): Int =
            track { delegate.read(buffer, offset, length) }
        override fun close() = delegate.close()

        private inline fun <T> track(read: () -> T): T = try {
            read()
        } catch (error: Exception) {
            failure = error
            throw error
        }
    }

    private class CandidateFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private class PreparationFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun reject(code: SkinImportCode, detail: String): Nothing {
        require(code in CANDIDATE_REJECTION_CODES) { "Non-candidate error cannot be rejected: $code" }
        throw CandidateFailure(code, detail)
    }
    private fun abort(code: SkinImportCode, detail: String): Nothing = throw PreparationFailure(code, detail)
    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private companion object {
        val BIDI_CONTROLS = setOf('؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮', '⁦', '⁧', '⁨', '⁩')
        val MAPPING_REJECTION_CODES = setOf(SkinImportCode.TARGET_COLLISION, SkinImportCode.LIMIT_EXCEEDED)
        val PNG_REJECTION_CODES = setOf(SkinImportCode.PNG_INVALID, SkinImportCode.LIMIT_EXCEEDED)
        val CANDIDATE_REJECTION_CODES = setOf(
            SkinImportCode.NO_CANDIDATE,
            SkinImportCode.TARGET_COLLISION,
            SkinImportCode.PNG_INVALID,
            SkinImportCode.LIMIT_EXCEEDED,
        )
        val NEXT = AtomicLong()
    }
}
