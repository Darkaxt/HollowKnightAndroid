package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CatalogMapping
import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinAlias
import dev.silksong.launcher.skins.contracts.SkinCandidate
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.SkinWarning
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer

class SkinCatalogMapper(
    private val catalog: CatalogPathSet,
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun map(candidate: SkinCandidate, archive: AuthorizedZip): SkinResult<CatalogMapping> {
        try {
            catalog.revalidate()
        } catch (error: Exception) {
            return SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Pinned catalog authority changed")
        }
        val mapped = linkedMapOf<String, RawZipEntry>()
        val aliases = mutableListOf<SkinAlias>()
        val warnings = mutableListOf<SkinWarning>()
        for (entry in candidate.entries.sortedBy { it.centralIndex }) {
            val components = archive.canonicalPaths[entry.centralIndex]
                ?: return SkinResult.Error(SkinImportCode.PATH_REJECTED, "Authorized path is missing")
            val rawPath = entry.rawName
            if (entry.directory) {
                if (entry.ignoredExtraMetadata) {
                    warnings += SkinWarning("IGNORED_EXTRA_METADATA", rawPath.toHex())
                }
                continue
            }
            val relative = relativeAscii(entry, components, candidate.rawPrefix)
            val resolution = relative?.let(::resolve)
            if (resolution != null) {
                if (mapped.putIfAbsent(resolution.target, entry) != null) {
                    return SkinResult.Error(
                        SkinImportCode.TARGET_COLLISION,
                        "Multiple sources resolve to ${resolution.target}",
                    )
                }
                resolution.rule?.let { rule -> aliases += SkinAlias(rawPath.toHex(), resolution.target, rule) }
                if (entry.ignoredExtraMetadata) {
                    warnings += SkinWarning("IGNORED_EXTRA_METADATA", rawPath.toHex())
                }
            } else {
                warnings += SkinWarning(warningCode(entry, components, relative), rawPath.toHex())
            }
        }
        if (mapped.size > limits.mappings) {
            return SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Candidate has too many texture mappings")
        }
        val ordered = linkedMapOf<String, RawZipEntry>()
        catalog.paths.forEach { target -> mapped[target]?.let { ordered[target] = it } }
        return SkinResult.Ok(CatalogMapping(ordered, aliases, warnings))
    }

    internal fun canMap(relativePath: String): Boolean = resolve(relativePath) != null

    internal fun relativeAscii(entry: RawZipEntry, rawComponents: List<ByteArray>, rawPrefix: ByteArray): String? {
        val prefix = splitRaw(rawPrefix)
        if (rawComponents.size <= prefix.size) return null
        for (index in prefix.indices) {
            if (!rawComponents[index].contentEquals(prefix[index])) return null
        }
        val components = if (entry.flags and UTF8_FLAG != 0) {
            rawComponents.drop(prefix.size).map { component ->
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
            val remaining = rawComponents.drop(prefix.size)
            if (remaining.any { part -> part.any { (it.toInt() and 0xff) >= 0x80 } }) return null
            remaining.map { it.toString(Charsets.US_ASCII) }
        }
        if (components.any { component -> component.any { it.code >= 0x80 } }) return null
        return components.joinToString("/")
    }

    private fun resolve(path: String): Resolution? {
        if (path in catalog.pathSet) return Resolution(path, null)

        val caseMatches = catalog.asciiFolded[asciiFold(path)].orEmpty()
        caseMatches.singleOrNull()?.let { return Resolution(it, "ASCII_CASE_FOLD") }

        val aliases = buildList {
            if (!path.contains('/') && path.startsWith("Charm_")) add("Charms/$path" to "ROOT_CHARM")
            replaceExact(path, "HUD.png", "Hud.png")?.let { add(it to "HUD") }
            replaceExact(path, "DreamNail.png", "Dreamnail.png")?.let { add(it to "DREAM_NAIL") }
            replaceExact(path, "Voidspells.png", "VoidSpells.png")?.let { add(it to "VOID_SPELLS") }
            replaceExact(path, "DeathPt.png", "Deathpt.png")?.let { add(it to "DEATH_PT") }
            if (path.startsWith("Inventory/Godfinder_")) {
                add(path.replaceFirst("Inventory/Godfinder_", "Inventory/GodFinder_") to "GOD_FINDER")
            }
            if (path == "Inventory/ElegantKey.png") add("Inventory/ElegentKey.png" to "ELEGENT_KEY")
        }.filter { it.first in catalog.pathSet }
        aliases.singleOrNull()?.let { return Resolution(it.first, it.second) }
        return null
    }

    private fun warningCode(entry: RawZipEntry, rawComponents: List<ByteArray>, relative: String?): String {
        val display = relative ?: normalizedDisplay(entry, rawComponents).orEmpty()
        val components = display.split('/')
        val lower = display.lowercase()
        return when {
            lower.endsWith(".zip") || lower.endsWith(".rar") || lower.endsWith(".7z") -> "IGNORED_NESTED_ARCHIVE"
            components.any { it.equals("Swap", true) } -> "IGNORED_SWAP"
            components.any { it.equals("Cinematics", true) } -> "IGNORED_CINEMATICS"
            components.any { it.equals("ReplaceAudio", true) } -> "IGNORED_REPLACE_AUDIO"
            lower.contains("hpbar") || lower.contains("hp_bar") || lower.contains("hp bar") || lower.contains("healthbar") -> "IGNORED_HP_BAR"
            lower.endsWith(".txt") || lower.endsWith(".json") || lower.endsWith(".xml") ||
                lower.endsWith(".ini") || lower.endsWith(".cfg") || lower.endsWith(".md") -> "IGNORED_CONFIG_OR_TEXT"
            components.any { it.equals("Alt", true) || it.equals("Alternate", true) || it.equals("Alternates", true) } ||
                lower.substringAfterLast('/').contains("_alt") -> "IGNORED_ALTERNATE"
            relative == null && entry.rawName.any { (it.toInt() and 0xff) >= 0x80 } -> "IGNORED_PATH_ENCODING"
            entry.ignoredExtraMetadata -> "IGNORED_EXTRA_METADATA"
            else -> "IGNORED_UNKNOWN"
        }
    }

    private fun normalizedDisplay(entry: RawZipEntry, rawComponents: List<ByteArray>): String? =
        if (entry.flags and UTF8_FLAG != 0) {
            rawComponents.joinToString("/") { component ->
                Normalizer.normalize(
                    StandardCharsets.UTF_8.newDecoder()
                        .onMalformedInput(CodingErrorAction.REPORT)
                        .onUnmappableCharacter(CodingErrorAction.REPORT)
                        .decode(ByteBuffer.wrap(component))
                        .toString(),
                    Normalizer.Form.NFKC,
                )
            }
        } else if (rawComponents.all { component -> component.all { (it.toInt() and 0xff) < 0x80 } }) {
            rawComponents.joinToString("/") { it.toString(Charsets.US_ASCII) }
        } else {
            null
        }

    private fun replaceExact(path: String, source: String, target: String): String? = if (path == source) target else null

    private fun splitRaw(path: ByteArray): List<ByteArray> {
        if (path.isEmpty()) return emptyList()
        val output = mutableListOf<ByteArray>()
        var start = 0
        for (index in path.indices) {
            if (path[index] == '/'.code.toByte()) {
                output += path.copyOfRange(start, index)
                start = index + 1
            }
        }
        output += path.copyOfRange(start, path.size)
        return output
    }

    private fun asciiFold(value: String): String = buildString(value.length) {
        value.forEach { append(if (it in 'A'..'Z') it + 32 else it) }
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    private data class Resolution(val target: String, val rule: String?)

    companion object {
        private const val UTF8_FLAG = 0x0800

        fun map(candidate: SkinCandidate, paths: List<String>): SkinResult<CatalogMapping> {
            val catalog = try {
                CatalogPathSet.requirePinned()
            } catch (error: Exception) {
                return SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Pinned catalog is unavailable")
            }
            if (paths != catalog.paths) {
                return SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Mapper paths differ from pinned catalog order")
            }
            val archive = dev.silksong.launcher.skins.contracts.ZipArchive(java.io.File("."), candidate.entries)
            return when (val authorized = ZipPathAuthority.validate(archive)) {
                is SkinResult.Error -> authorized
                is SkinResult.Ok -> SkinCatalogMapper(catalog).map(candidate, authorized.value)
            }
        }
    }
}
