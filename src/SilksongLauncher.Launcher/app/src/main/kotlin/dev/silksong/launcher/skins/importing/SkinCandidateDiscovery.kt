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
        val prefixes = linkedMapOf<String, Prefix>()
        for (entry in regular) {
            val components = archive.canonicalPaths.getValue(entry.centralIndex)
            val prefixCount = (0 until components.size).firstOrNull { count ->
                relative(entry, components, count)?.let(mapper::canMap) == true
            } ?: continue
            val rawPrefix = joinRaw(components.take(prefixCount))
            prefixes.putIfAbsent(rawPrefix.toHex(), Prefix(rawPrefix, layoutCode(components.take(prefixCount))))
        }
        if (prefixes.isEmpty()) {
            return SkinResult.Error(SkinImportCode.NO_CANDIDATE, "No catalog-backed skin candidate was found")
        }
        val recognized = prefixes.values.toList()
        if (recognized.size > 1 && recognized.any { it.bytes.isEmpty() }) {
            return SkinResult.Error(SkinImportCode.AMBIGUOUS_LAYOUT, "Root and wrapped skin layouts are both present")
        }
        val outermost = recognized.filterNot { candidate ->
            val candidateComponents = splitRaw(candidate.bytes)
            recognized.any { possibleParent ->
                possibleParent.bytes.isNotEmpty() && possibleParent !== candidate &&
                    hasPrefix(candidateComponents, splitRaw(possibleParent.bytes))
            }
        }
        if (outermost.size > 1 && ambiguousPrefixes(outermost)) {
            return SkinResult.Error(SkinImportCode.AMBIGUOUS_LAYOUT, "Candidate roots do not form one unambiguous skin collection")
        }
        return finish(archive, outermost)
    }

    private fun ambiguousPrefixes(prefixes: List<Prefix>): Boolean {
        val components = prefixes.map { splitRaw(it.bytes) }
        if (components.any { it.isEmpty() }) return true
        for (left in components.indices) {
            for (right in components.indices) {
                if (left != right && hasPrefix(components[right], components[left])) return true
            }
        }
        val parents = components.map { joinRaw(it.dropLast(1)).toHex() }.toSet()
        return parents.size != 1 || parents.single().isEmpty()
    }

    private fun layoutCode(prefix: List<ByteArray>): Int = when {
        containsFullInstallSuffix(prefix) -> 3
        prefix.isEmpty() -> 0
        prefix.size == 1 -> 1
        else -> 2
    }

    private fun containsFullInstallSuffix(prefix: List<ByteArray>): Boolean =
        (0..prefix.size - FULL_SUFFIX.size).any { start ->
            FULL_SUFFIX.indices.all { offset -> prefix[start + offset].isAscii(FULL_SUFFIX[offset]) }
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

    companion object {
        fun discover(paths: AuthorizedZip): SkinResult<CandidateSet> =
            SkinCandidateDiscovery(CatalogPathSet.requirePinned()).discover(paths)

        private const val UTF8_FLAG = 0x0800
        private val FULL_SUFFIX = listOf("hollow_knight_Data", "Managed", "Mods", "CustomKnight")
    }
}
