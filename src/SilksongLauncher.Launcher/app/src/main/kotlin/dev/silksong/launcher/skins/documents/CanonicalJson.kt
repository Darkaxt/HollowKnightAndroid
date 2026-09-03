package dev.silksong.launcher.skins.documents

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinAlias
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.SkinWarning
import java.net.URI
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer
import java.util.TreeMap

object CanonicalJson {
    private val SHA256 = Regex("[0-9a-f]{64}")
    private val DIGEST_NAME = Regex("[a-z2-7]{52}")
    private val ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    private val DECIMAL = Regex("0|[1-9][0-9]*")
    private val HEX = Regex("(?:[0-9a-f]{2}){0,512}")
    private val ALIAS_RULES = setOf(
        "ASCII_CASE_FOLD", "ROOT_CHARM", "HUD", "DREAM_NAIL", "VOID_SPELLS", "DEATH_PT",
        "GOD_FINDER", "ELEGENT_KEY",
    )
    private val WARNING_CODES = listOf(
        "IGNORED_NESTED_ARCHIVE", "IGNORED_SWAP", "IGNORED_CINEMATICS", "IGNORED_REPLACE_AUDIO",
        "IGNORED_HP_BAR", "IGNORED_CONFIG_OR_TEXT", "IGNORED_ALTERNATE", "IGNORED_PATH_ENCODING",
        "IGNORED_EXTRA_METADATA", "IGNORED_UNKNOWN",
    )

    fun manifest(document: SkinManifestDocument): ByteArray = encodeManifest(document)

    fun manifest(document: SkinManifestDocument, catalog: CatalogPathSet): ByteArray =
        encodeManifest(document, catalog)

    fun encodeManifest(document: SkinManifestDocument, catalog: CatalogPathSet? = null): ByteArray {
        validateManifest(document, catalog)
        val fields = linkedMapOf<String, JValue>(
            "schemaVersion" to JNumber(document.schemaVersion.toString()),
            "id" to JString(document.id),
            "name" to JString(document.name),
            "author" to JString(document.author),
            "contentSha256" to JString(document.contentSha256),
            "games" to JObject(document.games.mapValues { (_, game) -> gameValue(game) }),
        )
        document.license?.let { fields["license"] = JString(it) }
        document.source?.let { fields["source"] = JString(it) }
        document.homepage?.let { fields["homepage"] = JString(it) }
        document.attribution?.let { fields["attribution"] = JString(it) }
        document.preview?.let { fields["preview"] = JString(it) }
        return render(JObject(fields))
    }

    fun objectDocument(document: SkinObjectDocument): ByteArray = encodeObject(document)

    fun encodeObject(document: SkinObjectDocument): ByteArray {
        validateObject(document)
        return render(
            JObject(
                mapOf(
                    "schemaVersion" to JNumber(document.schemaVersion.toString()),
                    "treeSha256" to JString(document.treeSha256),
                    "contentSha256" to JString(document.contentSha256),
                    "manifestSha256" to JString(document.manifestSha256),
                    "fileCount" to JString(document.fileCount.toString()),
                    "payloadBytes" to JString(document.payloadBytes.toString()),
                    "files" to JArray(
                        document.files.map { file ->
                            JObject(
                                mapOf(
                                    "path" to JString(file.path),
                                    "length" to JString(file.length.toString()),
                                    "sha256" to JString(file.sha256),
                                ),
                            )
                        },
                    ),
                ),
            ),
        )
    }

    fun importReceipt(document: SkinImportReceiptDocument): ByteArray = encodeImportReceipt(document)

    fun importReceipt(document: SkinImportReceiptDocument, catalog: CatalogPathSet): ByteArray =
        encodeImportReceipt(document, catalog)

    fun encodeImportReceipt(document: SkinImportReceiptDocument, catalog: CatalogPathSet? = null): ByteArray {
        validateReceipt(document, catalog)
        val fields = linkedMapOf<String, JValue>(
            "schemaVersion" to JNumber(document.schemaVersion.toString()),
            "normalizerVersion" to JString(document.normalizerVersion),
            "candidateKey" to JString(document.candidateKey),
            "archiveSha256" to JString(document.archiveSha256),
            "archiveName" to JString(document.archiveName),
            "candidateRawPathHex" to JString(document.candidateRawPathHex),
            "layoutCode" to JNumber(document.layoutCode.toString()),
            "signatureStatus" to JString(document.signatureStatus),
            "aliases" to JArray(document.aliases.map(::aliasValue)),
            "warnings" to JArray(document.warnings.map(::warningValue)),
        )
        document.source?.let { fields["source"] = JString(it) }
        document.homepage?.let { fields["homepage"] = JString(it) }
        return render(JObject(fields)).also {
            require(it.size <= 8 * 1024 * 1024) { "Import receipt exceeds 8 MiB" }
        }
    }

    fun parseManifest(bytes: ByteArray): SkinManifestDocument = unwrap(parseManifestResult(bytes, null))

    fun parseManifest(bytes: ByteArray, catalog: CatalogPathSet): SkinManifestDocument =
        unwrap(parseManifestResult(bytes, catalog))

    fun tryParseManifest(bytes: ByteArray, catalog: CatalogPathSet): SkinResult<SkinManifestDocument> =
        parseManifestResult(bytes, catalog)

    fun parseObject(bytes: ByteArray): SkinObjectDocument = unwrap(parseObjectResult(bytes))

    fun tryParseObject(bytes: ByteArray): SkinResult<SkinObjectDocument> = parseObjectResult(bytes)

    fun parseImportReceipt(bytes: ByteArray): SkinImportReceiptDocument =
        unwrap(parseImportReceiptResult(bytes, null))

    fun parseImportReceipt(bytes: ByteArray, catalog: CatalogPathSet): SkinImportReceiptDocument =
        unwrap(parseImportReceiptResult(bytes, catalog))

    fun tryParseImportReceipt(bytes: ByteArray, catalog: CatalogPathSet): SkinResult<SkinImportReceiptDocument> =
        parseImportReceiptResult(bytes, catalog)

    private fun parseManifestResult(bytes: ByteArray, catalog: CatalogPathSet?): SkinResult<SkinManifestDocument> {
        if (bytes.size > 65536) {
            return SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Skin manifest exceeds 64 KiB")
        }
        return parseDocument(bytes) { value ->
            val root = value.objectWithKeys(
                required = setOf("schemaVersion", "id", "name", "author", "contentSha256", "games"),
                optional = setOf("license", "source", "homepage", "attribution", "preview"),
            )
            val gamesObject = root.required("games").objectWithKeys(setOf("hollow-knight"), emptySet())
            val game = parseGame(gamesObject.getValue("hollow-knight"))
            SkinManifestDocument(
                schemaVersion = root.int("schemaVersion"),
                id = root.string("id"),
                name = root.string("name"),
                author = root.string("author"),
                contentSha256 = root.string("contentSha256"),
                games = mapOf("hollow-knight" to game),
                license = root.optionalString("license"),
                source = root.optionalString("source"),
                homepage = root.optionalString("homepage"),
                attribution = root.optionalString("attribution"),
                preview = root.optionalString("preview"),
            ).also { validateManifest(it, catalog) }
        }
    }

    private fun parseObjectResult(bytes: ByteArray): SkinResult<SkinObjectDocument> = parseDocument(bytes) { value ->
        val root = value.objectWithKeys(
            setOf("schemaVersion", "treeSha256", "contentSha256", "manifestSha256", "fileCount", "payloadBytes", "files"),
            emptySet(),
        )
        val files = root.required("files").array().map { row ->
            val fields = row.objectWithKeys(setOf("path", "length", "sha256"), emptySet())
            SkinFileDocument(fields.string("path"), fields.unsignedLong("length"), fields.string("sha256"))
        }
        SkinObjectDocument(
            schemaVersion = root.int("schemaVersion"),
            treeSha256 = root.string("treeSha256"),
            contentSha256 = root.string("contentSha256"),
            manifestSha256 = root.string("manifestSha256"),
            fileCount = root.unsignedLong("fileCount").also { require(it <= Int.MAX_VALUE) }.toInt(),
            payloadBytes = root.unsignedLong("payloadBytes"),
            files = files,
        ).also(::validateObject)
    }

    private fun parseImportReceiptResult(
        bytes: ByteArray,
        catalog: CatalogPathSet?,
    ): SkinResult<SkinImportReceiptDocument> {
        if (bytes.size > 8 * 1024 * 1024) {
            return SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Import receipt exceeds 8 MiB")
        }
        return parseDocument(bytes) { value ->
            val root = value.objectWithKeys(
                setOf(
                    "schemaVersion", "normalizerVersion", "candidateKey", "archiveSha256", "archiveName",
                    "candidateRawPathHex", "layoutCode", "signatureStatus", "aliases", "warnings",
                ),
                setOf("source", "homepage"),
            )
            val aliases = root.required("aliases").array().map { row ->
                val fields = row.objectWithKeys(setOf("sourceRawPathHex", "target", "rule"), emptySet())
                SkinAlias(fields.string("sourceRawPathHex"), fields.string("target"), fields.string("rule"))
            }
            val warnings = root.required("warnings").array().map { row ->
                val fields = row.objectWithKeys(setOf("code", "sourceRawPathHex"), emptySet())
                SkinWarning(fields.string("code"), fields.string("sourceRawPathHex"))
            }
            SkinImportReceiptDocument(
                schemaVersion = root.int("schemaVersion"),
                normalizerVersion = root.string("normalizerVersion"),
                candidateKey = root.string("candidateKey"),
                archiveSha256 = root.string("archiveSha256"),
                archiveName = root.string("archiveName"),
                candidateRawPathHex = root.string("candidateRawPathHex"),
                layoutCode = root.int("layoutCode"),
                signatureStatus = root.string("signatureStatus"),
                source = root.optionalString("source"),
                homepage = root.optionalString("homepage"),
                aliases = aliases,
                warnings = warnings,
            ).also { validateReceipt(it, catalog) }
        }
    }

    private fun <T> unwrap(result: SkinResult<T>): T = when (result) {
        is SkinResult.Ok -> result.value
        is SkinResult.Error -> throw IllegalArgumentException(result.detail)
    }

    private inline fun <T> parseDocument(bytes: ByteArray, decode: (JValue) -> T): SkinResult<T> = try {
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
        require(!text.startsWith('﻿')) { "BOM is forbidden" }
        val value = Parser(text).parse()
        val decoded = decode(value)
        val canonical = when (decoded) {
            is SkinManifestDocument -> encodeManifest(decoded)
            is SkinObjectDocument -> encodeObject(decoded)
            is SkinImportReceiptDocument -> encodeImportReceipt(decoded)
            else -> error("Unsupported document")
        }
        require(canonical.contentEquals(bytes)) { "Document is not canonical RFC 8785 JSON" }
        SkinResult.Ok(decoded)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Invalid canonical JSON")
    }

    private fun gameValue(game: SkinGameDocument): JValue = JObject(
        mapOf(
            "gameVersion" to JString(game.gameVersion),
            "catalogId" to JString(game.catalogId),
            "assetRoot" to JString(game.assetRoot),
            "textures" to JObject(game.textures.mapValues { JString(it.value) }),
        ),
    )

    private fun aliasValue(alias: SkinAlias): JValue = JObject(
        mapOf(
            "sourceRawPathHex" to JString(alias.sourceRawPathHex),
            "target" to JString(alias.target),
            "rule" to JString(alias.rule),
        ),
    )

    private fun warningValue(warning: SkinWarning): JValue = JObject(
        mapOf(
            "code" to JString(warning.code),
            "sourceRawPathHex" to JString(warning.sourceRawPathHex),
        ),
    )

    private fun parseGame(value: JValue): SkinGameDocument {
        val fields = value.objectWithKeys(setOf("gameVersion", "catalogId", "assetRoot", "textures"), emptySet())
        val textures = fields.required("textures").objectValue().mapValues { (_, item) -> item.string() }
        return SkinGameDocument(
            fields.string("gameVersion"),
            fields.string("catalogId"),
            fields.string("assetRoot"),
            textures,
        )
    }

    private fun validateManifest(document: SkinManifestDocument, catalog: CatalogPathSet? = null) {
        val authority = (catalog ?: CatalogPathSet.requirePinned()).revalidate()
        require(document.schemaVersion == 1)
        require(ID.matches(document.id) && document.id.length <= 64)
        requireDisplayName(document.name, 80)
        requireDisplayName(document.author, 80)
        require(SHA256.matches(document.contentSha256))
        require(document.games.keys == setOf("hollow-knight"))
        val game = document.games.getValue("hollow-knight")
        require(game.gameVersion == "1.5.12620")
        require(game.catalogId == "hk-custom-knight-v3.5.0-205")
        require(game.assetRoot == "assets")
        require(game.textures.isNotEmpty() && game.textures.size <= 205)
        game.textures.forEach { (target, source) ->
            require(target in authority.pathSet && DIGEST_NAME.matches(source))
        }
        document.license?.let { requireDisplayText(it, 80) }
        document.source?.let(::requireHttps)
        document.homepage?.let(::requireHttps)
        document.attribution?.let { requireDisplayText(it, 512) }
        document.preview?.let { preview ->
            requireSafeRelativePath(preview)
            require(preview.endsWith(".png"))
            val texturePaths = game.textures.values.map { asciiFold("${game.assetRoot}/$it") }.toSet()
            require(asciiFold(preview) !in texturePaths)
        }
    }

    private fun validateObject(document: SkinObjectDocument) {
        require(document.schemaVersion == 1)
        require(SHA256.matches(document.treeSha256) && SHA256.matches(document.contentSha256) && SHA256.matches(document.manifestSha256))
        require(document.fileCount == document.files.size && document.fileCount in 1..207)
        require(document.payloadBytes in 0..268435456)
        require(document.files.map { it.path }.distinct().size == document.files.size)
        require(document.files.map { asciiFold(it.path) }.distinct().size == document.files.size)
        require(document.files == document.files.sortedWith { a, b -> SkinIdentity.unsignedUtf8Compare(a.path, b.path) })
        document.files.forEach {
            requireSafeRelativePath(it.path)
            require(it.length >= 0 && SHA256.matches(it.sha256))
        }
        require(document.files.any { it.path == "skin.json" })
    }

    private fun validateReceipt(document: SkinImportReceiptDocument, catalog: CatalogPathSet? = null) {
        val authority = (catalog ?: CatalogPathSet.requirePinned()).revalidate()
        require(document.schemaVersion == 1 && document.normalizerVersion == "hkzip-v1")
        require(SHA256.matches(document.candidateKey) && SHA256.matches(document.archiveSha256))
        requireArchiveName(document.archiveName)
        require(HEX.matches(document.candidateRawPathHex))
        require(document.layoutCode in 0..3)
        require(
            SkinIdentity.candidateKey(
                document.archiveSha256,
                document.candidateRawPathHex.hexBytes(),
                document.layoutCode,
            ) == document.candidateKey,
        )
        require(document.signatureStatus == "UNVERIFIED_SOURCE")
        document.source?.let(::requireHttps)
        document.homepage?.let(::requireHttps)
        require(document.aliases.size <= 205)
        val aliasSources = HashSet<String>()
        val aliasTargets = HashSet<String>()
        val candidatePrefix = document.candidateRawPathHex.hexBytes()
        document.aliases.forEach {
            require(HEX.matches(it.sourceRawPathHex) && it.sourceRawPathHex.isNotEmpty())
            require(it.target in authority.pathSet && it.rule in ALIAS_RULES)
            requireFiniteAlias(candidatePrefix, it)
            require(aliasSources.add(it.sourceRawPathHex) && aliasTargets.add(it.target))
        }
        val entryWarningCount = document.warnings.count { it.sourceRawPathHex.isNotEmpty() }
        val archiveWarningCount = document.warnings.size - entryWarningCount
        require(entryWarningCount <= 4096 && archiveWarningCount <= 10)
        val entrySources = HashSet<String>()
        var archiveRows = false
        var previousArchivePriority = -1
        val archiveCodes = HashSet<String>()
        document.warnings.forEach { warning ->
            require(warning.code in WARNING_CODES && HEX.matches(warning.sourceRawPathHex))
            if (warning.sourceRawPathHex.isEmpty()) {
                archiveRows = true
                val priority = WARNING_CODES.indexOf(warning.code)
                require(priority > previousArchivePriority && archiveCodes.add(warning.code))
                previousArchivePriority = priority
            } else {
                require(!archiveRows && entrySources.add(warning.sourceRawPathHex))
            }
        }
    }

    private fun requireFiniteAlias(candidatePrefix: ByteArray, alias: SkinAlias) {
        val source = alias.sourceRawPathHex.hexBytes()
        val relativeBytes = if (candidatePrefix.isEmpty()) {
            source
        } else {
            require(source.size > candidatePrefix.size && source.copyOfRange(0, candidatePrefix.size).contentEquals(candidatePrefix))
            require(source[candidatePrefix.size] == '/'.code.toByte())
            source.copyOfRange(candidatePrefix.size + 1, source.size)
        }
        require(relativeBytes.isNotEmpty() && relativeBytes.all { (it.toInt() and 0xff) in 0x20..0x7e })
        val relative = relativeBytes.toString(Charsets.US_ASCII)
        val expected = when (alias.rule) {
            "ASCII_CASE_FOLD" -> alias.target.takeIf { relative != it && asciiFold(relative) == asciiFold(it) }
            "ROOT_CHARM" -> relative.takeIf { '/' !in it && it.startsWith("Charm_") }?.let { "Charms/$it" }
            "HUD" -> "Hud.png".takeIf { relative == "HUD.png" }
            "DREAM_NAIL" -> "Dreamnail.png".takeIf { relative == "DreamNail.png" }
            "VOID_SPELLS" -> "VoidSpells.png".takeIf { relative == "Voidspells.png" }
            "DEATH_PT" -> "Deathpt.png".takeIf { relative == "DeathPt.png" }
            "GOD_FINDER" -> relative.takeIf { it.startsWith("Inventory/Godfinder_") }
                ?.replaceFirst("Inventory/Godfinder_", "Inventory/GodFinder_")
            "ELEGENT_KEY" -> "Inventory/ElegentKey.png".takeIf { relative == "Inventory/ElegantKey.png" }
            else -> null
        }
        require(expected == alias.target) { "Receipt alias is not a finite catalog transformation" }
    }

    private fun requireArchiveName(value: String) {
        requireDisplayText(value, 128)
        require(value == value.trim() && '/' !in value && '\\' !in value)
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value)
    }

    private fun requireDisplayName(value: String, maximum: Int) {
        requireDisplayText(value, maximum)
        require(value == value.trim() && '/' !in value && '\\' !in value)
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value)
    }

    private fun requireDisplayText(value: String, maximum: Int) {
        val count = value.codePointCount(0, value.length)
        require(count in 1..maximum)
        require(!hasUnpairedSurrogate(value))
        require(value.none { it.isISOControl() || it in BIDI_CONTROLS })
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

    private fun requireHttps(value: String) {
        require(value.length <= 2048)
        val uri = URI(value)
        require(uri.isAbsolute && uri.scheme == "https" && !uri.host.isNullOrEmpty() && uri.userInfo == null)
    }

    private fun requireSafeRelativePath(path: String) {
        require(path.toByteArray(Charsets.UTF_8).size <= 240 && path.isNotEmpty())
        require('/' in path || path != ".")
        require(!path.startsWith('/') && '\\' !in path && '%' !in path && '\u0000' !in path)
        val parts = path.split('/')
        require(parts.size <= 4)
        require(parts.all { part ->
            part.length in 1..64 && SEGMENT.matches(part) && part != "." && part != ".." &&
                !part.endsWith('.') && !part.endsWith(' ') && !isDevice(part)
        })
    }

    private fun String.hexBytes(): ByteArray = ByteArray(length / 2) { index ->
        substring(index * 2, index * 2 + 2).toInt(16).toByte()
    }

    private fun asciiFold(value: String): String = buildString(value.length) {
        value.forEach { character -> append(if (character in 'A'..'Z') character + 32 else character) }
    }

    private fun isDevice(segment: String): Boolean {
        val stem = segment.substringBefore('.').uppercase()
        return stem in setOf("CON", "PRN", "AUX", "NUL") ||
            Regex("(?:COM|LPT)[1-9]").matches(stem)
    }

    private fun render(value: JValue): ByteArray = buildString { appendValue(value) }.toByteArray(StandardCharsets.UTF_8)

    private fun StringBuilder.appendValue(value: JValue) {
        when (value) {
            is JObject -> {
                append('{')
                var first = true
                for ((key, item) in TreeMap(value.values)) {
                    if (!first) append(',')
                    first = false
                    appendString(key)
                    append(':')
                    appendValue(item)
                }
                append('}')
            }
            is JArray -> {
                append('[')
                value.values.forEachIndexed { index, item ->
                    if (index > 0) append(',')
                    appendValue(item)
                }
                append(']')
            }
            is JString -> appendString(value.value)
            is JNumber -> append(value.raw)
            JNull -> append("null")
        }
    }

    private fun StringBuilder.appendString(value: String) {
        append('"')
        for (character in value) {
            when (character) {
                '"' -> append("\\\"")
                '\\' -> append("\\\\")
                '\b' -> append("\\b")
                '\t' -> append("\\t")
                '\n' -> append("\\n")
                '' -> append("\\f")
                '\r' -> append("\\r")
                else -> if (character.code < 0x20) append("\\u%04x".format(character.code)) else append(character)
            }
        }
        append('"')
    }

    private sealed interface JValue
    private data class JObject(val values: Map<String, JValue>) : JValue
    private data class JArray(val values: List<JValue>) : JValue
    private data class JString(val value: String) : JValue
    private data class JNumber(val raw: String) : JValue
    private data object JNull : JValue

    private class Parser(private val source: String) {
        private var index = 0

        fun parse(): JValue {
            val value = value()
            require(index == source.length) { "Trailing JSON data" }
            return value
        }

        private fun value(): JValue {
            require(index < source.length) { "Unexpected end of JSON" }
            return when (source[index]) {
                '{' -> objectValue()
                '[' -> arrayValue()
                '"' -> JString(string())
                'n' -> literal("null", JNull)
                '-', in '0'..'9' -> number()
                else -> error("Invalid JSON token")
            }
        }

        private fun objectValue(): JObject {
            index++
            val values = linkedMapOf<String, JValue>()
            if (take('}')) return JObject(values)
            while (true) {
                require(source.getOrNull(index) == '"') { "Object key must be a string" }
                val key = string()
                require(key !in values) { "Duplicate object key: $key" }
                require(take(':')) { "Missing object colon" }
                values[key] = value()
                if (take('}')) return JObject(values)
                require(take(',')) { "Missing object comma" }
            }
        }

        private fun arrayValue(): JArray {
            index++
            val values = mutableListOf<JValue>()
            if (take(']')) return JArray(values)
            while (true) {
                values += value()
                if (take(']')) return JArray(values)
                require(take(',')) { "Missing array comma" }
            }
        }

        private fun string(): String {
            require(take('"'))
            val output = StringBuilder()
            while (true) {
                require(index < source.length) { "Unterminated string" }
                val character = source[index++]
                when {
                    character == '"' -> return output.toString()
                    character == '\\' -> {
                        require(index < source.length)
                        when (val escaped = source[index++]) {
                            '"', '\\', '/' -> output.append(escaped)
                            'b' -> output.append('\b')
                            'f' -> output.append('')
                            'n' -> output.append('\n')
                            'r' -> output.append('\r')
                            't' -> output.append('\t')
                            'u' -> {
                                require(index + 4 <= source.length)
                                val code = source.substring(index, index + 4).toInt(16)
                                output.append(code.toChar())
                                index += 4
                            }
                            else -> error("Invalid string escape")
                        }
                    }
                    character.code < 0x20 -> error("Unescaped control character")
                    else -> output.append(character)
                }
            }
        }

        private fun number(): JNumber {
            val start = index
            if (take('-')) Unit
            require(index < source.length && source[index].isDigit())
            if (source[index] == '0') index++ else while (source.getOrNull(index)?.isDigit() == true) index++
            if (source.getOrNull(index) == '.' || source.getOrNull(index) == 'e' || source.getOrNull(index) == 'E') {
                error("Only integer JSON numbers are supported")
            }
            return JNumber(source.substring(start, index))
        }

        private fun <T : JValue> literal(text: String, value: T): T {
            require(source.startsWith(text, index))
            index += text.length
            return value
        }

        private fun take(character: Char): Boolean {
            if (source.getOrNull(index) != character) return false
            index++
            return true
        }
    }

    private fun JValue.objectValue(): Map<String, JValue> = (this as? JObject)?.values ?: error("Expected object")
    private fun JValue.objectWithKeys(required: Set<String>, optional: Set<String>): Map<String, JValue> {
        val values = objectValue()
        require(values.keys.containsAll(required) && values.keys.all { it in required || it in optional }) { "Unknown or missing field" }
        return values
    }
    private fun JValue.array(): List<JValue> = (this as? JArray)?.values ?: error("Expected array")
    private fun JValue.string(): String = (this as? JString)?.value ?: error("Expected string")
    private fun Map<String, JValue>.required(name: String): JValue = get(name) ?: error("Missing field: $name")
    private fun Map<String, JValue>.string(name: String): String = required(name).string()
    private fun Map<String, JValue>.optionalString(name: String): String? = get(name)?.string()
    private fun Map<String, JValue>.int(name: String): Int {
        val raw = (required(name) as? JNumber)?.raw ?: error("Expected integer: $name")
        require(DECIMAL.matches(raw))
        return raw.toInt()
    }
    private fun Map<String, JValue>.unsignedLong(name: String): Long {
        val raw = string(name)
        require(DECIMAL.matches(raw))
        return raw.toLong()
    }

    private val SEGMENT = Regex("[A-Za-z0-9][A-Za-z0-9._-]*")
    private val BIDI_CONTROLS = setOf(
        '؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮',
        '⁦', '⁧', '⁨', '⁩',
    )
}
