package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.RawZipEntry
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.ZipArchive
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer

class ZipPathAuthority(
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun validate(archive: ZipArchive): SkinResult<AuthorizedZip> = try {
        val paths = linkedMapOf<Int, List<ByteArray>>()
        val exact = HashSet<String>()
        val canonicalNodes = HashMap<String, Pair<String, Boolean>>()
        val implicit = linkedMapOf<String, ByteArray>()

        for (entry in archive.entries) {
            val path = authorize(entry)
            val exactKey = path.rawPath.toHex()
            if (!exact.add(exactKey)) fail(SkinImportCode.PATH_COLLISION, "Duplicate raw ZIP path")
            paths[entry.centralIndex] = path.rawComponents.map(ByteArray::copyOf)

            val components = path.rawComponents
            for (count in 1..components.size) {
                val directory = count < components.size || path.directory
                val bytes = joinRaw(components.take(count))
                val rawKey = bytes.toHex()
                val canonicalKey = collisionKey(path, count)
                val previous = canonicalNodes.putIfAbsent(canonicalKey, rawKey to directory)
                if (previous != null && previous != rawKey to directory) {
                    fail(SkinImportCode.PATH_COLLISION, "ZIP file or directory paths collide after canonical folding")
                }
                if (directory) implicit.putIfAbsent(rawKey, bytes)
            }
        }
        if (implicit.size > limits.directories) fail(SkinImportCode.LIMIT_EXCEEDED, "ZIP has too many directories")
        SkinResult.Ok(
            AuthorizedZip(
                archive,
                paths,
            ),
        )
    } catch (error: PathFailure) {
        SkinResult.Error(error.code, error.message ?: error.code.name)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.PATH_REJECTED, "Path validation failed: ${error.message}")
    }

    private fun authorize(entry: RawZipEntry): ValidatedZipPath {
        val raw = entry.rawName
        if (raw.isEmpty() || raw.size > limits.sourcePathBytes) path("Raw path length is invalid")
        if (raw[0] == '/'.code.toByte() || raw.any { it == 0.toByte() || it == '\\'.code.toByte() }) {
            path("Absolute, NUL, or backslash path is forbidden")
        }
        val directory = raw.last() == '/'.code.toByte()
        val body = if (directory) raw.copyOf(raw.size - 1) else raw
        if (body.isEmpty() || body.lastOrNull() == '/'.code.toByte()) path("Empty path component is forbidden")
        val components = split(body)
        if (components.size > limits.sourceDepth) limit("ZIP path is too deep")
        components.forEachIndexed { index, component -> validateRawComponent(component, index == 0) }

        val utf8 = entry.flags and UTF8_FLAG != 0
        val normalized = if (utf8) {
            components.map { component ->
                val decoded = decodeUtf8(component)
                val value = Normalizer.normalize(decoded, Normalizer.Form.NFKC)
                validateNormalizedComponent(value)
                value
            }
        } else {
            null
        }
        val normalizedText = normalized?.joinToString("/") ?: if (components.all(::isAscii)) {
            components.joinToString("/") { it.toString(Charsets.US_ASCII) }
        } else {
            null
        }
        val collisionKey = collisionKey(components, normalized, utf8)
        return ValidatedZipPath(
            rawPath = raw.copyOf(),
            rawComponents = components.map { it.copyOf() },
            normalizedText = normalizedText,
            normalizedComponents = normalized ?: emptyList(),
            utf8 = utf8,
            collisionKey = collisionKey,
            directory = directory,
            ignoredExtraMetadata = entry.ignoredExtraMetadata,
        )
    }

    private fun validateRawComponent(component: ByteArray, first: Boolean) {
        if (component.isEmpty()) path("Empty path component is forbidden")
        if (component.contentEquals(byteArrayOf('.'.code.toByte())) ||
            component.contentEquals(byteArrayOf('.'.code.toByte(), '.'.code.toByte()))
        ) path("Traversal component is forbidden")
        if (component.last() == '.'.code.toByte() || component.last() == ' '.code.toByte()) {
            path("Trailing dot or space aliases are forbidden")
        }
        if (first && component.size >= 2 && isAsciiLetter(component[0]) && component[1] == ':'.code.toByte()) {
            path("Drive path is forbidden")
        }
        if (isAscii(component)) {
            val text = component.toString(Charsets.US_ASCII)
            if (isDevice(text)) path("Windows device path is forbidden")
        }
    }

    private fun validateNormalizedComponent(component: String) {
        if (component.isEmpty() || component == "." || component == "..") path("Normalized traversal is forbidden")
        if (component.any { it == '/' || it == '\\' || it == '\u0000' }) path("Normalized separator is forbidden")
        if (component.last() == '.' || component.last() == ' ') path("Normalized path alias is forbidden")
        if (isDevice(component)) path("Normalized device path is forbidden")
    }

    private fun collisionKey(path: ValidatedZipPath, componentCount: Int): String =
        collisionKey(
            path.rawComponents.take(componentCount),
            path.normalizedComponents.take(componentCount).takeIf { path.utf8 },
            path.utf8,
        )

    private fun collisionKey(raw: List<ByteArray>, normalized: List<String>?, utf8: Boolean): String {
        if (utf8) {
            val bytes = normalized!!.joinToString("/").toByteArray(Charsets.UTF_8)
            return if (isAscii(bytes)) "a:${asciiFold(bytes).toHex()}" else "u:${asciiFold(bytes).toHex()}"
        }
        val bytes = joinRaw(raw)
        return if (isAscii(bytes)) "a:${asciiFold(bytes).toHex()}" else "r:${asciiFold(bytes).toHex()}"
    }

    private fun split(path: ByteArray): List<ByteArray> {
        val parts = mutableListOf<ByteArray>()
        var start = 0
        for (index in path.indices) {
            if (path[index] == '/'.code.toByte()) {
                if (index == start) path("Empty path component is forbidden")
                parts += path.copyOfRange(start, index)
                start = index + 1
            }
        }
        if (start >= path.size) path("Empty path component is forbidden")
        parts += path.copyOfRange(start, path.size)
        return parts
    }

    private fun decodeUtf8(bytes: ByteArray): String = try {
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
    } catch (_: Exception) {
        path("UTF-8 flagged path is not strict UTF-8")
    }

    private fun isDevice(component: String): Boolean {
        val stem = component.substringBefore('.').uppercase()
        return stem in setOf("CON", "PRN", "AUX", "NUL") || Regex("(?:COM|LPT)[1-9]").matches(stem)
    }

    private fun joinRaw(components: List<ByteArray>): ByteArray {
        val length = components.sumOf { it.size } + maxOf(0, components.size - 1)
        val output = ByteArray(length)
        var offset = 0
        components.forEachIndexed { index, component ->
            if (index > 0) output[offset++] = '/'.code.toByte()
            component.copyInto(output, offset)
            offset += component.size
        }
        return output
    }

    private fun asciiFold(bytes: ByteArray): ByteArray = ByteArray(bytes.size) { index ->
        val value = bytes[index].toInt() and 0xff
        if (value in 0x41..0x5a) (value + 0x20).toByte() else bytes[index]
    }

    private fun isAscii(bytes: ByteArray): Boolean = bytes.all { (it.toInt() and 0xff) < 0x80 }
    private fun isAsciiLetter(value: Byte): Boolean = (value.toInt() and 0xff) in 0x41..0x5a ||
        (value.toInt() and 0xff) in 0x61..0x7a

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private class PathFailure(val code: SkinImportCode, detail: String) : RuntimeException(detail)
    private fun fail(code: SkinImportCode, detail: String): Nothing = throw PathFailure(code, detail)
    private fun path(detail: String): Nothing = fail(SkinImportCode.PATH_REJECTED, detail)
    private fun limit(detail: String): Nothing = fail(SkinImportCode.LIMIT_EXCEEDED, detail)

    companion object {
        fun validate(archive: ZipArchive): SkinResult<AuthorizedZip> = ZipPathAuthority().validate(archive)
        private const val UTF8_FLAG = 0x0800
    }
}

private data class ValidatedZipPath(
    val rawPath: ByteArray,
    val rawComponents: List<ByteArray>,
    val normalizedText: String?,
    val normalizedComponents: List<String>,
    val utf8: Boolean,
    val collisionKey: String,
    val directory: Boolean,
    val ignoredExtraMetadata: Boolean,
)

data class AuthorizedZip(
    val archive: dev.silksong.launcher.skins.contracts.ZipArchive,
    val canonicalPaths: Map<Int, List<ByteArray>>,
)
