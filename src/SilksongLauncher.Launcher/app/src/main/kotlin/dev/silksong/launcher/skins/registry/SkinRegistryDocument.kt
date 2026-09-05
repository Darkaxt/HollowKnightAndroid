package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer
import java.util.TreeMap
import java.util.UUID

internal object SkinRegistryAuthority {
    const val SCHEMA_VERSION = 1
    const val PROFILE_ID = "hollow-knight"
    const val GAME_VERSION = "1.5.12620"
    const val CATALOG_ID = "hk-custom-knight-v3.5.0-205"
    const val CATALOG_SHA256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a"
    const val GENESIS_ID = "00000000-0000-0000-0000-000000000000"
    const val GENESIS_WRITER = "genesis"
    const val MAX_PACKS = 64

    fun genesis() = SkinRegistryDocument(
        SCHEMA_VERSION,
        GENESIS_ID,
        0,
        null,
        GENESIS_ID,
        GENESIS_WRITER,
        PROFILE_ID,
        GAME_VERSION,
        CATALOG_ID,
        CATALOG_SHA256,
        emptyList(),
        SkinActivation(SkinMode.OFF, null, ActiveVisual.Vanilla, 0, RotationInterlock.clear()),
    )
}

data class RegistryPack(
    val id: String,
    val name: String,
    val author: String,
    val candidateKey: String,
    val treeSha256: String,
    val contentSha256: String,
    val importReceiptSha256: String,
    val rotationEligible: Boolean,
)

data class SkinRegistryDocument(
    val schemaVersion: Int,
    val generationId: String,
    val sequence: Long,
    val parentGenerationId: String?,
    val operationId: String,
    val writer: String,
    val profileId: String,
    val gameVersion: String,
    val catalogId: String,
    val catalogSha256: String,
    val packs: List<RegistryPack>,
    val activation: SkinActivation,
)

fun interface RegistryMutation {
    fun apply(current: SkinRegistryDocument): SkinResult<SkinRegistryDocument>
}

data class RegistryHead(
    val generationId: String,
    val sequence: Long,
    val sha256: String,
    val document: SkinRegistryDocument,
)

internal object SkinRegistryDocumentCodec {
    private val sha256 = Regex("[0-9a-f]{64}")
    private val uuidText = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val idText = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    private val failureCode = Regex("[A-Z][A-Z0-9_]{0,127}")
    private val decimal = Regex("0|[1-9][0-9]*")

    fun canonical(registry: SkinRegistryDocument): SkinResult<ByteArray> = result { encode(registry) }

    fun parse(bytes: ByteArray): SkinResult<SkinRegistryDocument> = result {
        require(bytes.size <= MAX_BYTES) { "Registry document exceeds its byte bound" }
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
        require(!text.startsWith('﻿')) { "Registry BOM is forbidden" }
        val json = Parser(text).parse()
        require(!json.containsNull()) { "Null registry fields are forbidden" }
        val decoded = decodeDocument(json)
        require(encode(decoded).contentEquals(bytes)) { "Registry document is not canonical JSON" }
        decoded
    }

    private inline fun <T> result(action: () -> T): SkinResult<T> = try {
        SkinResult.Ok(action())
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.REGISTRY_CORRUPT, error.message ?: "Invalid skin registry document")
    }

    private fun encode(registry: SkinRegistryDocument): ByteArray {
        validate(registry)
        return render(documentValue(registry))
    }

    private fun validate(registry: SkinRegistryDocument) {
        require(registry.schemaVersion == SkinRegistryAuthority.SCHEMA_VERSION) { "Unknown registry schema" }
        requireUuid(registry.generationId, "generation ID")
        require(registry.sequence >= 0) { "Negative registry sequence" }
        requireUuid(registry.operationId, "operation ID")
        registry.parentGenerationId?.let { requireUuid(it, "parent generation ID") }
        require(registry.profileId == SkinRegistryAuthority.PROFILE_ID) { "Wrong registry profile" }
        require(registry.gameVersion == SkinRegistryAuthority.GAME_VERSION) { "Wrong registry game version" }
        require(registry.catalogId == SkinRegistryAuthority.CATALOG_ID) { "Wrong registry catalog" }
        require(registry.catalogSha256 == SkinRegistryAuthority.CATALOG_SHA256) { "Wrong registry catalog digest" }
        requireDisplay(registry.writer, 80, "writer")
        if (registry.sequence == 0L) {
            require(registry == SkinRegistryAuthority.genesis()) { "Sequence zero is not deterministic genesis" }
        } else {
            require(registry.generationId != SkinRegistryAuthority.GENESIS_ID) { "Child reuses genesis ID" }
            require(registry.parentGenerationId != null && registry.parentGenerationId != registry.generationId) {
                "Child has no valid parent"
            }
        }
        require(registry.packs.size <= SkinRegistryAuthority.MAX_PACKS) { "Installed pack bound exceeded" }
        require(registry.packs == registry.packs.sortedBy(RegistryPack::id)) { "Packs are not in ASCII ID order" }
        val ids = linkedSetOf<String>()
        val owners = linkedSetOf<String>()
        registry.packs.forEach { pack ->
            require(idText.matches(pack.id) && ids.add(pack.id)) { "Invalid or duplicate pack ID" }
            requireDisplay(pack.name, 80, "pack name")
            requireDisplay(pack.author, 80, "pack author")
            requireDigest(pack.candidateKey, "candidate key")
            require(owners.add(pack.candidateKey)) { "Duplicate candidate ownership" }
            requireDigest(pack.treeSha256, "tree digest")
            requireDigest(pack.contentSha256, "content digest")
            requireDigest(pack.importReceiptSha256, "receipt digest")
        }
        validateActivation(registry.activation, registry.packs.associateBy(RegistryPack::id))
        if (registry.activation.rotationInterlock.state == InterlockState.ARMED) {
            require(registry.activation.rotationInterlock.baseGenerationId == registry.parentGenerationId) {
                "Armed interlock does not name its immediate base generation"
            }
        }
    }

    private fun validateActivation(value: SkinActivation, packs: Map<String, RegistryPack>) {
        require(value.skinStamp >= 0) { "Negative skin stamp" }
        validateSelected(value.selectedPackId, packs)
        validateVisual(value.active, packs)
        val lock = value.rotationInterlock
        val optional = listOf(
            lock.transactionId,
            lock.operation,
            lock.baseGenerationId,
            lock.baseGenerationSha256,
            lock.prior,
            lock.target,
            lock.bindingToken,
            lock.priorEstablishedOnBinding,
            lock.originalFailure,
            lock.rollbackFailure,
        )
        if (lock.state == InterlockState.CLEAR) {
            require(optional.all { it == null }) { "Clear interlock carries transaction data" }
            return
        }
        requireUuid(requireNotNull(lock.transactionId), "transaction ID")
        require(lock.operation != null) { "Missing interlock operation" }
        requireUuid(requireNotNull(lock.baseGenerationId), "base generation ID")
        requireDigest(requireNotNull(lock.baseGenerationSha256), "base generation digest")
        val prior = requireNotNull(lock.prior) { "Missing prior snapshot" }
        val target = requireNotNull(lock.target) { "Missing target snapshot" }
        validateSnapshot(prior, packs)
        validateSnapshot(target, packs)
        val nextStamp = Math.addExact(prior.skinStamp, 1L)
        require(target.skinStamp == nextStamp) { "Interlock target stamp is not the next stamp" }
        requireToken(requireNotNull(lock.bindingToken).value)
        require(lock.priorEstablishedOnBinding != null) { "Missing prior-binding proof" }
        require(value.snapshot() == prior) { "Outer activation does not equal interlock prior" }
        when (lock.state) {
            InterlockState.ARMED -> require(lock.originalFailure == null && lock.rollbackFailure == null) {
                "Armed interlock carries failures"
            }
            InterlockState.ROLLBACK_FAILED -> {
                requireFailure(requireNotNull(lock.originalFailure))
                requireFailure(requireNotNull(lock.rollbackFailure))
            }
            InterlockState.CLEAR -> error("unreachable")
        }
    }

    private fun validateSnapshot(value: ActivationSnapshot, packs: Map<String, RegistryPack>) {
        require(value.skinStamp >= 0) { "Negative snapshot stamp" }
        validateSelected(value.selectedPackId, packs)
        validateVisual(value.active, packs)
    }

    private fun validateSelected(value: String?, packs: Map<String, RegistryPack>) {
        if (value != null) require(value in packs) { "Selected pack is not installed" }
    }

    private fun validateVisual(value: ActiveVisual, packs: Map<String, RegistryPack>) {
        if (value is ActiveVisual.Pack) {
            require(value.id in packs) { "Active pack is not installed" }
            requireDigest(value.treeSha256, "active tree digest")
            requireDigest(value.contentSha256, "active content digest")
            requireDigest(value.importReceiptSha256, "active receipt digest")
        }
    }

    private fun requireUuid(value: String, label: String) {
        require(uuidText.matches(value) && UUID.fromString(value).toString() == value) { "Invalid $label" }
    }

    private fun requireDigest(value: String, label: String) {
        require(sha256.matches(value)) { "Invalid $label" }
    }

    private fun requireDisplay(value: String, maximum: Int, label: String) {
        require(value.codePointCount(0, value.length) in 1..maximum && value == value.trim()) { "Invalid $label" }
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value) { "Non-normalized $label" }
        require(!hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in bidiControls }) {
            "Invalid $label characters"
        }
        require('/' !in value && '\\' !in value) { "Invalid $label separator" }
    }

    private fun requireToken(value: String) {
        require(value.codePointCount(0, value.length) in 1..256 && value == value.trim()) { "Invalid binding token" }
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value) { "Non-normalized binding token" }
        require(!hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in bidiControls })
    }

    private fun requireFailure(value: String) {
        require(failureCode.matches(value)) { "Invalid stable interlock failure code" }
    }

    private fun documentValue(value: SkinRegistryDocument): JValue {
        val fields = linkedMapOf<String, JValue>(
            "schemaVersion" to JNumber(value.schemaVersion.toString()),
            "generationId" to JString(value.generationId),
            "sequence" to JString(value.sequence.toString()),
            "operationId" to JString(value.operationId),
            "writer" to JString(value.writer),
            "profileId" to JString(value.profileId),
            "gameVersion" to JString(value.gameVersion),
            "catalogId" to JString(value.catalogId),
            "catalogSha256" to JString(value.catalogSha256),
            "packs" to JArray(value.packs.map(::packValue)),
            "activation" to activationValue(value.activation),
        )
        value.parentGenerationId?.let { fields["parentGenerationId"] = JString(it) }
        return JObject(fields)
    }

    private fun packValue(value: RegistryPack) = JObject(
        mapOf(
            "id" to JString(value.id),
            "name" to JString(value.name),
            "author" to JString(value.author),
            "candidateKey" to JString(value.candidateKey),
            "treeSha256" to JString(value.treeSha256),
            "contentSha256" to JString(value.contentSha256),
            "importReceiptSha256" to JString(value.importReceiptSha256),
            "rotationEligible" to JBoolean(value.rotationEligible),
        ),
    )

    private fun activationValue(value: SkinActivation): JValue {
        val fields = linkedMapOf<String, JValue>(
            "mode" to JString(value.mode.name),
            "active" to visualValue(value.active),
            "skinStamp" to JString(value.skinStamp.toString()),
            "rotationInterlock" to interlockValue(value.rotationInterlock),
        )
        value.selectedPackId?.let { fields["selectedPackId"] = JString(it) }
        return JObject(fields)
    }

    private fun snapshotValue(value: ActivationSnapshot): JValue {
        val fields = linkedMapOf<String, JValue>(
            "mode" to JString(value.mode.name),
            "active" to visualValue(value.active),
            "skinStamp" to JString(value.skinStamp.toString()),
        )
        value.selectedPackId?.let { fields["selectedPackId"] = JString(it) }
        return JObject(fields)
    }

    private fun visualValue(value: ActiveVisual): JValue = when (value) {
        ActiveVisual.Vanilla -> JObject(mapOf("kind" to JString("VANILLA")))
        is ActiveVisual.Pack -> JObject(
            mapOf(
                "kind" to JString("PACK"),
                "id" to JString(value.id),
                "treeSha256" to JString(value.treeSha256),
                "contentSha256" to JString(value.contentSha256),
                "importReceiptSha256" to JString(value.importReceiptSha256),
            ),
        )
    }

    private fun interlockValue(value: RotationInterlock): JValue {
        if (value.state == InterlockState.CLEAR) return JObject(mapOf("state" to JString("CLEAR")))
        val fields = linkedMapOf<String, JValue>(
            "state" to JString(value.state.name),
            "transactionId" to JString(requireNotNull(value.transactionId)),
            "operation" to JString(requireNotNull(value.operation).name),
            "baseGenerationId" to JString(requireNotNull(value.baseGenerationId)),
            "baseGenerationSha256" to JString(requireNotNull(value.baseGenerationSha256)),
            "prior" to snapshotValue(requireNotNull(value.prior)),
            "target" to snapshotValue(requireNotNull(value.target)),
            "bindingToken" to JString(requireNotNull(value.bindingToken).value),
            "priorEstablishedOnBinding" to JBoolean(requireNotNull(value.priorEstablishedOnBinding)),
        )
        value.originalFailure?.let { fields["originalFailure"] = JString(it) }
        value.rollbackFailure?.let { fields["rollbackFailure"] = JString(it) }
        return JObject(fields)
    }

    private fun decodeDocument(value: JValue): SkinRegistryDocument {
        val fields = value.objectWithKeys(
            setOf(
                "schemaVersion", "generationId", "sequence", "operationId", "writer", "profileId",
                "gameVersion", "catalogId", "catalogSha256", "packs", "activation",
            ),
            setOf("parentGenerationId"),
        )
        return SkinRegistryDocument(
            fields.int("schemaVersion"),
            fields.string("generationId"),
            fields.unsignedLong("sequence"),
            fields.optionalString("parentGenerationId"),
            fields.string("operationId"),
            fields.string("writer"),
            fields.string("profileId"),
            fields.string("gameVersion"),
            fields.string("catalogId"),
            fields.string("catalogSha256"),
            fields.required("packs").array().map(::decodePack),
            decodeActivation(fields.required("activation")),
        )
    }

    private fun decodePack(value: JValue): RegistryPack {
        val fields = value.objectWithKeys(
            setOf("id", "name", "author", "candidateKey", "treeSha256", "contentSha256", "importReceiptSha256", "rotationEligible"),
            emptySet(),
        )
        return RegistryPack(
            fields.string("id"),
            fields.string("name"),
            fields.string("author"),
            fields.string("candidateKey"),
            fields.string("treeSha256"),
            fields.string("contentSha256"),
            fields.string("importReceiptSha256"),
            fields.boolean("rotationEligible"),
        )
    }

    private fun decodeActivation(value: JValue): SkinActivation {
        val fields = value.objectWithKeys(setOf("mode", "active", "skinStamp", "rotationInterlock"), setOf("selectedPackId"))
        return SkinActivation(
            enumValue(fields.string("mode")),
            fields.optionalString("selectedPackId"),
            decodeVisual(fields.required("active")),
            fields.unsignedLong("skinStamp"),
            decodeInterlock(fields.required("rotationInterlock")),
        )
    }

    private fun decodeSnapshot(value: JValue): ActivationSnapshot {
        val fields = value.objectWithKeys(setOf("mode", "active", "skinStamp"), setOf("selectedPackId"))
        return ActivationSnapshot(
            enumValue(fields.string("mode")),
            fields.optionalString("selectedPackId"),
            decodeVisual(fields.required("active")),
            fields.unsignedLong("skinStamp"),
        )
    }

    private fun decodeVisual(value: JValue): ActiveVisual {
        val fields = value.objectValue()
        return when (fields.string("kind")) {
            "VANILLA" -> {
                fields.requireKeys(setOf("kind"), emptySet())
                ActiveVisual.Vanilla
            }
            "PACK" -> {
                fields.requireKeys(setOf("kind", "id", "treeSha256", "contentSha256", "importReceiptSha256"), emptySet())
                ActiveVisual.Pack(
                    fields.string("id"),
                    fields.string("treeSha256"),
                    fields.string("contentSha256"),
                    fields.string("importReceiptSha256"),
                )
            }
            else -> error("Unknown active visual kind")
        }
    }

    private fun decodeInterlock(value: JValue): RotationInterlock {
        val fields = value.objectValue()
        return when (val state = enumValue<InterlockState>(fields.string("state"))) {
            InterlockState.CLEAR -> {
                fields.requireKeys(setOf("state"), emptySet())
                RotationInterlock.clear()
            }
            InterlockState.ARMED, InterlockState.ROLLBACK_FAILED -> {
                val required = mutableSetOf(
                    "state", "transactionId", "operation", "baseGenerationId", "baseGenerationSha256",
                    "prior", "target", "bindingToken", "priorEstablishedOnBinding",
                )
                if (state == InterlockState.ROLLBACK_FAILED) required += setOf("originalFailure", "rollbackFailure")
                fields.requireKeys(required, emptySet())
                RotationInterlock(
                    state,
                    fields.string("transactionId"),
                    enumValue<SkinOperationKind>(fields.string("operation")),
                    fields.string("baseGenerationId"),
                    fields.string("baseGenerationSha256"),
                    decodeSnapshot(fields.required("prior")),
                    decodeSnapshot(fields.required("target")),
                    SkinBindingToken(fields.string("bindingToken")),
                    fields.boolean("priorEstablishedOnBinding"),
                    fields.optionalString("originalFailure"),
                    fields.optionalString("rollbackFailure"),
                )
            }
        }
    }

    private inline fun <reified T : Enum<T>> enumValue(value: String): T =
        enumValues<T>().singleOrNull { it.name == value } ?: error("Unknown ${T::class.java.simpleName}")

    private fun render(value: JValue): ByteArray = buildString { appendValue(value) }.toByteArray(StandardCharsets.UTF_8)

    private fun StringBuilder.appendValue(value: JValue) {
        when (value) {
            is JObject -> {
                append('{')
                TreeMap(value.values).entries.forEachIndexed { index, (key, item) ->
                    if (index > 0) append(',')
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
            is JBoolean -> append(if (value.value) "true" else "false")
            JNull -> append("null")
        }
    }

    private fun StringBuilder.appendString(value: String) {
        append('"')
        value.forEach { character ->
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
    private data class JBoolean(val value: Boolean) : JValue
    private data object JNull : JValue

    private class Parser(private val source: String) {
        private var index = 0

        fun parse(): JValue {
            val value = value()
            require(index == source.length) { "Trailing registry JSON data" }
            return value
        }

        private fun value(): JValue {
            require(index < source.length) { "Unexpected end of registry JSON" }
            return when (source[index]) {
                '{' -> objectValue()
                '[' -> arrayValue()
                '"' -> JString(string())
                't' -> literal("true", JBoolean(true))
                'f' -> literal("false", JBoolean(false))
                'n' -> literal("null", JNull)
                '-', in '0'..'9' -> number()
                else -> error("Invalid registry JSON token")
            }
        }

        private fun objectValue(): JObject {
            index++
            val values = linkedMapOf<String, JValue>()
            if (take('}')) return JObject(values)
            while (true) {
                require(source.getOrNull(index) == '"') { "Registry object key must be a string" }
                val key = string()
                require(key !in values) { "Duplicate registry key: $key" }
                require(take(':')) { "Missing registry object colon" }
                values[key] = value()
                if (take('}')) return JObject(values)
                require(take(',')) { "Missing registry object comma" }
            }
        }

        private fun arrayValue(): JArray {
            index++
            val values = mutableListOf<JValue>()
            if (take(']')) return JArray(values)
            while (true) {
                values += value()
                if (take(']')) return JArray(values)
                require(take(',')) { "Missing registry array comma" }
            }
        }

        private fun string(): String {
            require(take('"'))
            val output = StringBuilder()
            while (true) {
                require(index < source.length) { "Unterminated registry string" }
                val character = source[index++]
                when {
                    character == '"' -> return output.toString()
                    character == '\\' -> {
                        require(index < source.length) { "Incomplete registry escape" }
                        when (val escaped = source[index++]) {
                            '"', '\\', '/' -> output.append(escaped)
                            'b' -> output.append('\b')
                            'f' -> output.append('')
                            'n' -> output.append('\n')
                            'r' -> output.append('\r')
                            't' -> output.append('\t')
                            'u' -> {
                                require(index + 4 <= source.length) { "Incomplete registry unicode escape" }
                                output.append(source.substring(index, index + 4).toInt(16).toChar())
                                index += 4
                            }
                            else -> error("Invalid registry escape")
                        }
                    }
                    character.code < 0x20 -> error("Unescaped registry control")
                    else -> output.append(character)
                }
            }
        }

        private fun number(): JNumber {
            val start = index
            if (take('-')) Unit
            require(index < source.length && source[index].isDigit()) { "Invalid registry number" }
            if (source[index] == '0') index++ else while (source.getOrNull(index)?.isDigit() == true) index++
            require(source.getOrNull(index) !in listOf('.', 'e', 'E')) { "Only integer registry numbers are supported" }
            return JNumber(source.substring(start, index))
        }

        private fun <T : JValue> literal(text: String, value: T): T {
            require(source.startsWith(text, index)) { "Invalid registry literal" }
            index += text.length
            return value
        }

        private fun take(character: Char): Boolean {
            if (source.getOrNull(index) != character) return false
            index++
            return true
        }
    }

    private fun JValue.containsNull(): Boolean = when (this) {
        JNull -> true
        is JObject -> values.values.any { it.containsNull() }
        is JArray -> values.any { it.containsNull() }
        is JString, is JNumber, is JBoolean -> false
    }

    private fun JValue.objectValue(): Map<String, JValue> = (this as? JObject)?.values ?: error("Expected registry object")
    private fun JValue.objectWithKeys(required: Set<String>, optional: Set<String>): Map<String, JValue> =
        objectValue().also { it.requireKeys(required, optional) }

    private fun Map<String, JValue>.requireKeys(required: Set<String>, optional: Set<String>) {
        require(keys.containsAll(required) && keys.all { it in required || it in optional }) {
            "Unknown or missing registry field"
        }
    }

    private fun JValue.array(): List<JValue> = (this as? JArray)?.values ?: error("Expected registry array")
    private fun JValue.string(): String = (this as? JString)?.value ?: error("Expected registry string")
    private fun JValue.boolean(): Boolean = (this as? JBoolean)?.value ?: error("Expected registry boolean")
    private fun Map<String, JValue>.required(name: String): JValue = get(name) ?: error("Missing registry field: $name")
    private fun Map<String, JValue>.string(name: String): String = required(name).string()
    private fun Map<String, JValue>.boolean(name: String): Boolean = required(name).boolean()
    private fun Map<String, JValue>.optionalString(name: String): String? = get(name)?.string()

    private fun Map<String, JValue>.int(name: String): Int {
        val raw = (required(name) as? JNumber)?.raw ?: error("Expected registry integer")
        require(decimal.matches(raw)) { "Non-canonical registry integer" }
        return raw.toInt()
    }

    private fun Map<String, JValue>.unsignedLong(name: String): Long {
        val raw = string(name)
        require(decimal.matches(raw)) { "Non-canonical unsigned decimal" }
        return raw.toLong()
    }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            when {
                value[index].isHighSurrogate() -> {
                    if (index + 1 >= value.length || !value[index + 1].isLowSurrogate()) return true
                    index += 2
                }
                value[index].isLowSurrogate() -> return true
                else -> index++
            }
        }
        return false
    }

    private val bidiControls = setOf(
        '؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮',
        '⁦', '⁧', '⁨', '⁩',
    )
    private const val MAX_BYTES = 8 * 1024 * 1024
}
