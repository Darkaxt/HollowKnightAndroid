package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.TreeMap
import java.util.UUID

enum class LeaseState { LAUNCH_PENDING, GAME_OWNED, CLOSED }

enum class LeaseMutationGate { CLEAR, ACTIVE, UNKNOWN }

/** Strict build-configured identity for the one target game process; no package is assumed by the store. */
data class SkinTargetProcess(
    val packageName: String,
    val processName: String,
) {
    init {
        require(validAndroidPackageName(packageName)) { "Invalid target package name" }
        require(
            processName == packageName ||
                (processName.startsWith("$packageName:") && validAndroidProcessSuffix(processName.substring(packageName.length + 1))),
        ) { "Target process does not exactly belong to its package" }
        require(processName.length <= 255) { "Target process name is too long" }
    }
}

private fun validAndroidPackageName(value: String): Boolean =
    value.length in 3..255 && value.split('.').let { parts ->
        parts.size >= 2 && parts.all(::validAndroidNameSegment)
    }

private fun validAndroidProcessSuffix(value: String): Boolean =
    value.isNotEmpty() && value.split('.').all(::validAndroidNameSegment)

private fun validAndroidNameSegment(value: String): Boolean =
    value.isNotEmpty() && value[0].isAsciiAndroidNameStart() && value.drop(1).all { it.isAsciiAndroidNamePart() }

private fun Char.isAsciiAndroidNameStart(): Boolean = this in 'a'..'z' || this in 'A'..'Z'
private fun Char.isAsciiAndroidNamePart(): Boolean = isAsciiAndroidNameStart() || this in '0'..'9' || this == '_'

/** The raw lease token is delivery-only and must never be persisted. */
data class SkinLaunchHandle(
    val descriptorId: UUID,
    val descriptorSha256: String,
    val descriptorPath: String,
    val leaseId: UUID,
    val leaseToken: String,
    val sessionSequence: Long,
) {
    override fun toString(): String =
        "SkinLaunchHandle(descriptorId=$descriptorId, descriptorSha256=$descriptorSha256, " +
            "descriptorPath=$descriptorPath, leaseId=$leaseId, leaseToken=<redacted>, " +
            "sessionSequence=$sessionSequence)"
}

data class LeaseHead(
    val descriptorId: UUID,
    val leaseId: UUID,
    val transitionSequence: Long,
    val state: LeaseState,
    val sha256: String,
)

/** One immutable, complete lease-state directory binds this document to its descriptor and parent transition. */
data class LeaseStateDocument(
    val schemaVersion: Int,
    val leaseId: UUID,
    val leaseTokenSha256: String,
    val profileId: String,
    val sessionSequence: Long,
    val transitionSequence: Long,
    val transitionId: UUID,
    val parentTransitionId: UUID?,
    val state: LeaseState,
    val descriptorId: UUID,
    val descriptorSha256: String,
    val registryGenerationId: String,
    val registrySha256: String,
    val launcherOwner: ProcessIdentity,
    val gameOwner: ProcessIdentity?,
    val closeReason: String?,
)

/** Strict canonical codec for immutable lease-state evidence. */
object SkinLeaseStateCodec {
    private const val SCHEMA_VERSION = 1
    private const val MAX_BYTES = 64 * 1024
    private const val PROFILE_ID = "hollow-knight"
    private const val MAX_DEPTH = 8
    private const val MAX_NODES = 128
    private val DIGEST = Regex("[0-9a-f]{64}")
    private val DECIMAL = Regex("0|[1-9][0-9]*")
    private val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val CLOSE_REASON = Regex("[A-Z][A-Z0-9_]{0,127}")

    fun canonical(value: LeaseStateDocument): ByteArray {
        validate(value)
        return render(documentValue(value)).also { require(it.size <= MAX_BYTES) { "Lease state exceeds 64 KiB" } }
    }

    fun parse(bytes: ByteArray): SkinResult<LeaseStateDocument> = try {
        require(bytes.size <= MAX_BYTES) { "Lease state exceeds 64 KiB" }
        val snapshot = bytes.copyOf()
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(snapshot))
            .toString()
        require(!text.startsWith('﻿')) { "Lease state BOM is forbidden" }
        val json = Parser(text).parse()
        require(!json.containsNull()) { "Lease state null fields are forbidden" }
        val decoded = decode(json)
        validate(decoded)
        require(canonical(decoded).contentEquals(snapshot)) { "Lease state is not canonical JSON" }
        SkinResult.Ok(decoded)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Invalid lease state")
    }

    internal fun head(value: LeaseStateDocument): LeaseHead {
        val bytes = canonical(value)
        return LeaseHead(value.descriptorId, value.leaseId, value.transitionSequence, value.state, sha256(bytes))
    }

    internal fun requireImmediateChild(child: LeaseStateDocument, parent: LeaseStateDocument) {
        validate(child)
        validate(parent)
        require(child.transitionSequence == Math.addExact(parent.transitionSequence, 1L)) {
            "Lease transition sequence is not adjacent"
        }
        require(child.parentTransitionId == parent.transitionId) { "Lease transition parent ID mismatches" }
        require(child.leaseId == parent.leaseId && child.leaseTokenSha256 == parent.leaseTokenSha256) {
            "Lease token identity changed"
        }
        require(child.profileId == parent.profileId && child.sessionSequence == parent.sessionSequence) {
            "Lease session identity changed"
        }
        require(child.descriptorId == parent.descriptorId && child.descriptorSha256 == parent.descriptorSha256) {
            "Lease descriptor identity changed"
        }
        require(child.registryGenerationId == parent.registryGenerationId && child.registrySha256 == parent.registrySha256) {
            "Lease registry identity changed"
        }
        require(child.launcherOwner == parent.launcherOwner) { "Lease launcher owner changed" }
        when (parent.state) {
            LeaseState.LAUNCH_PENDING -> when (child.state) {
                LeaseState.GAME_OWNED -> require(child.gameOwner != null && child.closeReason == null) {
                    "Game claim shape is invalid"
                }
                LeaseState.CLOSED -> require(child.gameOwner == null && child.closeReason != null) {
                    "Pending close shape is invalid"
                }
                LeaseState.LAUNCH_PENDING -> error("Lease cannot remain launch pending")
            }
            LeaseState.GAME_OWNED -> {
                require(child.state == LeaseState.CLOSED && child.gameOwner == parent.gameOwner && child.closeReason != null) {
                    "Game-owned lease may only close with its exact owner"
                }
            }
            LeaseState.CLOSED -> error("Closed lease is terminal")
        }
    }

    internal fun rawTokenSha256(rawToken: String): String {
        require(Regex("[0-9a-f]{64}").matches(rawToken)) { "Lease token is not lowercase 256-bit hex" }
        val bytes = ByteArray(32) { index -> rawToken.substring(index * 2, index * 2 + 2).toInt(16).toByte() }
        return sha256(bytes)
    }

    internal fun validOwner(value: ProcessIdentity): Boolean = try {
        requireOwner(value, "process owner")
        true
    } catch (_: Exception) {
        false
    }

    private fun validate(value: LeaseStateDocument) {
        require(value.schemaVersion == SCHEMA_VERSION) { "Unknown lease state schema" }
        requireUuid(value.leaseId, "lease ID")
        requireDigest(value.leaseTokenSha256, "lease token digest")
        require(value.profileId == PROFILE_ID) { "Wrong lease profile" }
        require(value.sessionSequence >= 0L) { "Negative lease session sequence" }
        require(value.transitionSequence in 0L..2L) { "Lease transition sequence is outside its bound" }
        requireUuid(value.transitionId, "transition ID")
        value.parentTransitionId?.let { requireUuid(it, "parent transition ID") }
        requireUuid(value.descriptorId, "descriptor ID")
        requireDigest(value.descriptorSha256, "descriptor digest")
        requireUuidText(value.registryGenerationId, "registry generation ID")
        requireDigest(value.registrySha256, "registry digest")
        requireOwner(value.launcherOwner, "launcher owner")
        value.gameOwner?.let { requireOwner(it, "game owner") }
        value.closeReason?.let { require(CLOSE_REASON.matches(it)) { "Invalid close reason" } }
        when (value.state) {
            LeaseState.LAUNCH_PENDING -> require(
                value.transitionSequence == 0L && value.parentTransitionId == null &&
                    value.gameOwner == null && value.closeReason == null,
            ) { "Launch-pending state shape is invalid" }
            LeaseState.GAME_OWNED -> require(
                value.transitionSequence == 1L && value.parentTransitionId != null &&
                    value.gameOwner != null && value.closeReason == null,
            ) { "Game-owned state shape is invalid" }
            LeaseState.CLOSED -> require(
                value.parentTransitionId != null && value.closeReason != null &&
                    ((value.transitionSequence == 1L && value.gameOwner == null) ||
                        (value.transitionSequence == 2L && value.gameOwner != null)),
            ) { "Closed state shape is invalid" }
        }
    }

    private fun requireOwner(value: ProcessIdentity, label: String) {
        require(value.uid >= 0 && value.pid > 0) { "Invalid $label UID or PID" }
        require(value.processStartToken.length <= 20 && DECIMAL.matches(value.processStartToken)) {
            "Invalid $label start token"
        }
    }

    private fun requireUuid(value: UUID, label: String) = requireUuidText(value.toString(), label)

    private fun requireUuidText(value: String, label: String) {
        require(UUID_TEXT.matches(value) && UUID.fromString(value).toString() == value) { "Invalid $label" }
    }

    private fun requireDigest(value: String, label: String) {
        require(DIGEST.matches(value)) { "Invalid $label" }
    }

    private fun documentValue(value: LeaseStateDocument): JValue {
        val fields = linkedMapOf<String, JValue>(
            "schemaVersion" to JNumber(value.schemaVersion.toString()),
            "leaseId" to JString(value.leaseId.toString()),
            "leaseTokenSha256" to JString(value.leaseTokenSha256),
            "profileId" to JString(value.profileId),
            "sessionSequence" to JString(value.sessionSequence.toString()),
            "transitionSequence" to JString(value.transitionSequence.toString()),
            "transitionId" to JString(value.transitionId.toString()),
            "state" to JString(value.state.name),
            "descriptorId" to JString(value.descriptorId.toString()),
            "descriptorSha256" to JString(value.descriptorSha256),
            "registryGenerationId" to JString(value.registryGenerationId),
            "registrySha256" to JString(value.registrySha256),
            "launcherOwner" to ownerValue(value.launcherOwner),
        )
        value.parentTransitionId?.let { fields["parentTransitionId"] = JString(it.toString()) }
        value.gameOwner?.let { fields["gameOwner"] = ownerValue(it) }
        value.closeReason?.let { fields["closeReason"] = JString(it) }
        return JObject(fields)
    }

    private fun ownerValue(value: ProcessIdentity): JValue = JObject(
        mapOf(
            "uid" to JNumber(value.uid.toString()),
            "pid" to JNumber(value.pid.toString()),
            "processStartToken" to JString(value.processStartToken),
        ),
    )

    private fun decode(value: JValue): LeaseStateDocument {
        val fields = value.objectWithKeys(
            required = setOf(
                "schemaVersion", "leaseId", "leaseTokenSha256", "profileId", "sessionSequence", "transitionSequence",
                "transitionId", "state", "descriptorId", "descriptorSha256", "registryGenerationId", "registrySha256", "launcherOwner",
            ),
            optional = setOf("parentTransitionId", "gameOwner", "closeReason"),
        )
        return LeaseStateDocument(
            schemaVersion = fields.int("schemaVersion"),
            leaseId = UUID.fromString(fields.string("leaseId")),
            leaseTokenSha256 = fields.string("leaseTokenSha256"),
            profileId = fields.string("profileId"),
            sessionSequence = fields.unsignedLong("sessionSequence"),
            transitionSequence = fields.unsignedLong("transitionSequence"),
            transitionId = UUID.fromString(fields.string("transitionId")),
            parentTransitionId = fields.optionalString("parentTransitionId")?.let(UUID::fromString),
            state = enumValue(fields.string("state")),
            descriptorId = UUID.fromString(fields.string("descriptorId")),
            descriptorSha256 = fields.string("descriptorSha256"),
            registryGenerationId = fields.string("registryGenerationId"),
            registrySha256 = fields.string("registrySha256"),
            launcherOwner = decodeOwner(fields.required("launcherOwner")),
            gameOwner = fields.optional("gameOwner")?.let(::decodeOwner),
            closeReason = fields.optionalString("closeReason"),
        )
    }

    private fun decodeOwner(value: JValue): ProcessIdentity {
        val fields = value.objectWithKeys(setOf("uid", "pid", "processStartToken"), emptySet())
        return ProcessIdentity(fields.int("uid"), fields.int("pid"), fields.string("processStartToken"))
    }

    private fun enumValue(value: String): LeaseState =
        enumValues<LeaseState>().singleOrNull { it.name == value } ?: error("Unknown lease state")

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
            is JString -> appendString(value.value)
            is JNumber -> append(value.raw)
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
    private data class JString(val value: String) : JValue
    private data class JNumber(val raw: String) : JValue
    private data object JNull : JValue

    private class Parser(private val source: String) {
        private var index = 0
        private var nodes = 0

        fun parse(): JValue {
            val value = value(0)
            require(index == source.length) { "Trailing lease state JSON data" }
            return value
        }

        private fun value(depth: Int): JValue {
            require(depth <= MAX_DEPTH) { "Lease state nesting exceeds its bound" }
            reserveNode()
            require(index < source.length) { "Unexpected end of lease state" }
            return when (source[index]) {
                '{' -> objectValue(depth + 1)
                '"' -> JString(string())
                'n' -> literal("null", JNull)
                '-', in '0'..'9' -> number()
                else -> error("Invalid lease state JSON token")
            }
        }

        private fun objectValue(childDepth: Int): JObject {
            index++
            val values = linkedMapOf<String, JValue>()
            if (take('}')) return JObject(values)
            while (true) {
                require(values.size < 32) { "Lease state object exceeds its field bound" }
                require(source.getOrNull(index) == '"') { "Lease state object key must be a string" }
                val key = string()
                require(key !in values) { "Duplicate lease state key: $key" }
                require(take(':')) { "Missing lease state object colon" }
                values[key] = value(childDepth)
                if (take('}')) return JObject(values)
                require(take(',')) { "Missing lease state object comma" }
            }
        }

        private fun string(): String {
            require(take('"'))
            val output = StringBuilder()
            while (true) {
                require(index < source.length) { "Unterminated lease state string" }
                when (val character = source[index++]) {
                    '"' -> return output.toString()
                    '\\' -> {
                        require(index < source.length) { "Incomplete lease state escape" }
                        when (val escaped = source[index++]) {
                            '"', '\\', '/' -> output.append(escaped)
                            'b' -> output.append('\b')
                            'f' -> output.append('')
                            'n' -> output.append('\n')
                            'r' -> output.append('\r')
                            't' -> output.append('\t')
                            'u' -> {
                                require(index + 4 <= source.length) { "Incomplete lease state unicode escape" }
                                output.append(source.substring(index, index + 4).toInt(16).toChar())
                                index += 4
                            }
                            else -> error("Invalid lease state escape")
                        }
                    }
                    else -> {
                        require(character.code >= 0x20) { "Unescaped lease state control" }
                        output.append(character)
                    }
                }
            }
        }

        private fun number(): JNumber {
            val start = index
            if (take('-')) Unit
            require(index < source.length && source[index].isDigit()) { "Invalid lease state number" }
            if (source[index] == '0') index++ else while (source.getOrNull(index)?.isDigit() == true) index++
            require(source.getOrNull(index) !in listOf('.', 'e', 'E')) { "Only integer lease state numbers are supported" }
            return JNumber(source.substring(start, index))
        }

        private fun <T : JValue> literal(text: String, value: T): T {
            require(source.startsWith(text, index)) { "Invalid lease state literal" }
            index += text.length
            return value
        }

        private fun reserveNode() {
            require(nodes < MAX_NODES) { "Lease state JSON node bound exceeded" }
            nodes++
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
        is JString, is JNumber -> false
    }

    private fun JValue.objectValue(): Map<String, JValue> = (this as? JObject)?.values ?: error("Expected lease state object")
    private fun JValue.objectWithKeys(required: Set<String>, optional: Set<String>): Map<String, JValue> =
        objectValue().also { it.requireKeys(required, optional) }

    private fun Map<String, JValue>.requireKeys(required: Set<String>, optional: Set<String>) {
        require(keys.containsAll(required) && keys.all { it in required || it in optional }) { "Unknown or missing lease state field" }
    }

    private fun Map<String, JValue>.required(name: String): JValue = get(name) ?: error("Missing lease state field: $name")
    private fun Map<String, JValue>.optional(name: String): JValue? = get(name)
    private fun Map<String, JValue>.string(name: String): String = (required(name) as? JString)?.value
        ?: error("Expected lease state string")
    private fun Map<String, JValue>.optionalString(name: String): String? = optional(name)?.let {
        (it as? JString)?.value ?: error("Expected lease state string")
    }

    private fun Map<String, JValue>.int(name: String): Int {
        val raw = (required(name) as? JNumber)?.raw ?: error("Expected lease state integer")
        require(DECIMAL.matches(raw)) { "Non-canonical lease state integer" }
        return raw.toInt()
    }

    private fun Map<String, JValue>.unsignedLong(name: String): Long {
        val raw = string(name)
        require(DECIMAL.matches(raw)) { "Non-canonical unsigned decimal" }
        return raw.toLong()
    }

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }
}
