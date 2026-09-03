package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidateSet
import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinCandidate
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.SkinWarning
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer

class SkinCandidateDiscovery(
    catalog: CatalogPathSet,
    private val limits: SkinLimits = SkinLimits.V1,
) {
    private val mapper = SkinCatalogMapper(catalog, limits)

    fun discover(archive: AuthorizedZip): SkinResult<CandidateSet> {
        val regular = archive.archive.entries.filterNot { it.directory }
        val fullPrefixes = linkedMapOf<String, ByteArray>()
        for (entry in regular) {
            val components = archive.canonicalPaths.getValue(entry.centralIndex)
            for (start in 0..components.size - FULL_SUFFIX.size - 1) {
                if (FULL_SUFFIX.indices.all { offset -> components[start + offset].isAscii(FULL_SUFFIX[offset]) }) {
                    val prefixCount = start + FULL_SUFFIX.size + 1
                    val relative = relative(entry, components, prefixCount)
                    if (relative != null && mapper.canMap(relative)) {
                        val prefix = joinRaw(components.take(prefixCount))
                        fullPrefixes.putIfAbsent(prefix.toHex(), prefix)
                    }
                }
            }
        }
        if (fullPrefixes.isNotEmpty()) {
            val prefixes = fullPrefixes.values.toList()
            val outsideMapping = regular.any { entry ->
                val components = archive.canonicalPaths.getValue(entry.centralIndex)
                prefixes.none { prefix -> hasPrefix(components, splitRaw(prefix)) } &&
                    sequenceOf(0, 1, 2).mapNotNull { count -> relative(entry, components, count) }.any(mapper::canMap)
            }
            if (outsideMapping) return SkinResult.Error(SkinImportCode.AMBIGUOUS_LAYOUT, "Mapped assets exist outside full-install candidates")
            return finish(archive, prefixes.map { Prefix(it, 3) })
        }

        val rootMapped = regular.any { entry ->
            val components = archive.canonicalPaths.getValue(entry.centralIndex)
            relative(entry, components, 0)?.let(mapper::canMap) == true
        }
        val wrappers = linkedMapOf<String, WrapperRecognition>()
        for (entry in regular) {
            val components = archive.canonicalPaths.getValue(entry.centralIndex)
            if (components.isEmpty()) continue
            val keyBytes = components.first()
            val key = keyBytes.toHex()
            val recognition = wrappers.getOrPut(key) { WrapperRecognition(keyBytes.copyOf()) }
            if (relative(entry, components, 1)?.let(mapper::canMap) == true) recognition.direct = true
            if (relative(entry, components, 2)?.let(mapper::canMap) == true) {
                val child = joinRaw(components.take(2))
                recognition.children.putIfAbsent(child.toHex(), child)
            }
        }
        val recognizedWrappers = wrappers.values.filter { it.direct || it.children.isNotEmpty() }
        if (rootMapped) {
            if (recognizedWrappers.isNotEmpty()) {
                return SkinResult.Error(SkinImportCode.AMBIGUOUS_LAYOUT, "Root and wrapper layouts are both recognized")
            }
            return finish(archive, listOf(Prefix(ByteArray(0), 0)))
        }
        if (recognizedWrappers.isEmpty()) return SkinResult.Error(SkinImportCode.NO_CANDIDATE, "No finite skin candidate layout was found")
        if (recognizedWrappers.size != 1) {
            return SkinResult.Error(SkinImportCode.AMBIGUOUS_LAYOUT, "More than one first-level wrapper is recognized")
        }
        val wrapper = recognizedWrappers.single()
        return if (wrapper.direct) {
            finish(archive, listOf(Prefix(wrapper.rawPrefix, 1)))
        } else {
            finish(archive, wrapper.children.values.map { Prefix(it, 2) })
        }
    }

    private fun finish(archive: AuthorizedZip, rawPrefixes: List<Prefix>): SkinResult<CandidateSet> {
        val sorted = rawPrefixes.distinctBy { it.bytes.toHex() }
            .sortedWith { left, right -> compareUnsigned(left.bytes, right.bytes) }
        if (sorted.isEmpty()) return SkinResult.Error(SkinImportCode.NO_CANDIDATE, "No skin candidates were found")
        if (sorted.size > limits.candidates) return SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Too many skin candidates")
        val owned = sorted.map { mutableListOf<RawZipEntry>() }
        for (entry in archive.archive.entries.sortedBy { it.centralIndex }) {
            val components = archive.canonicalPaths.getValue(entry.centralIndex)
            val matching = sorted.indices.filter { index ->
                hasPrefix(components, splitRaw(sorted[index].bytes))
            }
            val owner = matching.maxByOrNull { splitRaw(sorted[it].bytes).size } ?: 0
            owned[owner] += entry
        }
        return SkinResult.Ok(
            CandidateSet(
                sorted.indices.map { index -> SkinCandidate(sorted[index].bytes, sorted[index].layout, owned[index]) },
                if (archive.archive.ignoredExtraMetadata) {
                    listOf(SkinWarning("IGNORED_EXTRA_METADATA", ""))
                } else {
                    emptyList()
                },
            ),
        )
    }

    private fun relative(entry: RawZipEntry, rawComponents: List<ByteArray>, prefixCount: Int): String? {
        if (rawComponents.size <= prefixCount) return null
        val components = if (entry.flags and UTF8_FLAG != 0) {
            rawComponents.drop(prefixCount).map { component ->
                Normalizer.normalize(
                    StandardCharsets.UTF_8.newDecoder()
                        .onMalformedInput(CodingErrorAction.REPORT)
                        .onUnmappableCharacter(CodingErrorAction.REPORT)
                        .decode(ByteBuffer.wrap(component))
                        .toString(),
                    Normalizer.Form.NFKC,
                )
            }
        } else {
            rawComponents.drop(prefixCount).map { component ->
                if (component.any { (it.toInt() and 0xff) >= 0x80 }) return null
                component.toString(Charsets.US_ASCII)
            }
        }
        if (components.any { component -> component.any { it.code >= 0x80 } }) return null
        return components.joinToString("/")
    }

    private fun hasPrefix(path: List<ByteArray>, prefix: List<ByteArray>): Boolean =
        path.size >= prefix.size && prefix.indices.all { path[it].contentEquals(prefix[it]) }

    private fun splitRaw(path: ByteArray): List<ByteArray> {
        if (path.isEmpty()) return emptyList()
        val result = mutableListOf<ByteArray>()
        var start = 0
        path.indices.filter { path[it] == '/'.code.toByte() }.forEach { index ->
            result += path.copyOfRange(start, index)
            start = index + 1
        }
        result += path.copyOfRange(start, path.size)
        return result
    }

    private fun joinRaw(components: List<ByteArray>): ByteArray {
        val output = ByteArray(components.sumOf { it.size } + maxOf(0, components.size - 1))
        var offset = 0
        components.forEachIndexed { index, component ->
            if (index > 0) output[offset++] = '/'.code.toByte()
            component.copyInto(output, offset)
            offset += component.size
        }
        return output
    }

    private fun ByteArray.isAscii(expected: String): Boolean =
        size == expected.length && indices.all { (this[it].toInt() and 0xff) == expected[it].code }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    private fun compareUnsigned(left: ByteArray, right: ByteArray): Int =
        dev.silksong.launcher.skins.documents.SkinIdentity.unsignedBytesCompare(left, right)

    private data class Prefix(val bytes: ByteArray, val layout: Int)
    private data class WrapperRecognition(
        val rawPrefix: ByteArray,
        var direct: Boolean = false,
        val children: LinkedHashMap<String, ByteArray> = linkedMapOf(),
    )

    companion object {
        fun discover(paths: AuthorizedZip): SkinResult<CandidateSet> =
            SkinCandidateDiscovery(CatalogPathSet.requirePinned()).discover(paths)

        private const val UTF8_FLAG = 0x0800
        private val FULL_SUFFIX = listOf("hollow_knight_Data", "Managed", "Mods", "CustomKnight")
    }
}
