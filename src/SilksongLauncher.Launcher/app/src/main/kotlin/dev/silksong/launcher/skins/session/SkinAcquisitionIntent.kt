package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.nio.charset.StandardCharsets
import java.util.UUID

internal enum class SkinAcquisitionPhase {
    PREPARED,
    DESCRIPTOR_DURABLE,
}

/** Durable hash-only authorization for recovering one interrupted acquisition. */
internal data class SkinAcquisitionIntent(
    val phase: SkinAcquisitionPhase,
    val descriptorId: UUID,
    val descriptorSha256: String,
    val descriptorPath: String,
    val leaseId: UUID,
    val leaseTokenSha256: String,
    val sessionSequence: Long,
    val registryGenerationId: String,
    val registrySha256: String,
    val launcherOwner: ProcessIdentity,
)

/** Strict bounded canonical ASCII codec. Raw lease-token material has no field in this format. */
internal object SkinAcquisitionIntentCodec {
    private const val HEADER = "SKIN_ACQUISITION_INTENT_V1"
    private const val MAX_BYTES = 1024
    private val DIGEST = Regex("[0-9a-f]{64}")
    private val DECIMAL = Regex("0|[1-9][0-9]*")
    private val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val FIELDS = setOf(
        "phase",
        "descriptorId",
        "descriptorSha256",
        "descriptorPath",
        "leaseId",
        "leaseTokenSha256",
        "sessionSequence",
        "registryGenerationId",
        "registrySha256",
        "launcherUid",
        "launcherPid",
        "launcherStartToken",
    )
    private val NIL_UUID = UUID(0L, 0L)

    fun canonical(intent: SkinAcquisitionIntent): ByteArray {
        validate(intent)
        return buildString {
            append(HEADER).append('\n')
            append("phase=").append(intent.phase.name).append('\n')
            append("descriptorId=").append(intent.descriptorId).append('\n')
            append("descriptorSha256=").append(intent.descriptorSha256).append('\n')
            append("descriptorPath=").append(intent.descriptorPath).append('\n')
            append("leaseId=").append(intent.leaseId).append('\n')
            append("leaseTokenSha256=").append(intent.leaseTokenSha256).append('\n')
            append("sessionSequence=").append(intent.sessionSequence).append('\n')
            append("registryGenerationId=").append(intent.registryGenerationId).append('\n')
            append("registrySha256=").append(intent.registrySha256).append('\n')
            append("launcherUid=").append(intent.launcherOwner.uid).append('\n')
            append("launcherPid=").append(intent.launcherOwner.pid).append('\n')
            append("launcherStartToken=").append(intent.launcherOwner.processStartToken).append('\n')
        }.toByteArray(StandardCharsets.US_ASCII).also {
            require(it.size <= MAX_BYTES) { "Acquisition intent exceeds its bound" }
        }
    }

    fun parse(bytes: ByteArray): SkinResult<SkinAcquisitionIntent> = try {
        val snapshot = bytes.copyOf()
        require(snapshot.size in 1..MAX_BYTES && snapshot.all { (it.toInt() and 0xff) <= 0x7f })
        val lines = snapshot.toString(StandardCharsets.US_ASCII).split('\n')
        require(lines.size == 14 && lines.first() == HEADER && lines.last().isEmpty())
        val fields = linkedMapOf<String, String>()
        lines.subList(1, 13).forEach { line ->
            val separator = line.indexOf('=')
            require(separator > 0 && separator == line.lastIndexOf('='))
            val key = line.substring(0, separator)
            val value = line.substring(separator + 1)
            require(key in FIELDS && value.isNotEmpty() && fields.put(key, value) == null)
        }
        require(fields.keys == FIELDS)
        val intent = SkinAcquisitionIntent(
            phase = enumValues<SkinAcquisitionPhase>().single { it.name == fields.getValue("phase") },
            descriptorId = parseUuid(fields.getValue("descriptorId")),
            descriptorSha256 = fields.getValue("descriptorSha256"),
            descriptorPath = fields.getValue("descriptorPath"),
            leaseId = parseUuid(fields.getValue("leaseId")),
            leaseTokenSha256 = fields.getValue("leaseTokenSha256"),
            sessionSequence = parseLong(fields.getValue("sessionSequence")),
            registryGenerationId = parseRegistryUuid(fields.getValue("registryGenerationId")),
            registrySha256 = fields.getValue("registrySha256"),
            launcherOwner = ProcessIdentity(
                uid = parseInt(fields.getValue("launcherUid"), allowZero = true),
                pid = parseInt(fields.getValue("launcherPid"), allowZero = false),
                processStartToken = fields.getValue("launcherStartToken"),
            ),
        )
        validate(intent)
        require(canonical(intent).contentEquals(snapshot))
        SkinResult.Ok(intent)
    } catch (_: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Invalid skin acquisition intent")
    }

    private fun validate(intent: SkinAcquisitionIntent) {
        requireUuid(intent.descriptorId)
        requireUuid(intent.leaseId)
        require(intent.descriptorId != intent.leaseId)
        require(DIGEST.matches(intent.descriptorSha256))
        require(DIGEST.matches(intent.leaseTokenSha256))
        require(intent.sessionSequence >= 0L)
        require(intent.descriptorPath == "sessions/${intent.descriptorId}/descriptor.json")
        require(intent.descriptorPath.length <= 128)
        require(parseRegistryUuid(intent.registryGenerationId) == intent.registryGenerationId)
        require(DIGEST.matches(intent.registrySha256))
        require(SkinLeaseStateCodec.validOwner(intent.launcherOwner))
    }

    private fun requireUuid(value: UUID) {
        require(value != NIL_UUID && UUID_TEXT.matches(value.toString()) && UUID.fromString(value.toString()) == value)
    }

    private fun parseUuid(value: String): UUID {
        require(UUID_TEXT.matches(value))
        return UUID.fromString(value).also { require(it != NIL_UUID && it.toString() == value) }
    }

    private fun parseRegistryUuid(value: String): String {
        require(UUID_TEXT.matches(value))
        return UUID.fromString(value).toString().also { require(it == value) }
    }

    private fun parseLong(value: String): Long {
        require(DECIMAL.matches(value))
        return value.toLong().also { require(it >= 0L && it.toString() == value) }
    }

    private fun parseInt(value: String, allowZero: Boolean): Int {
        require(DECIMAL.matches(value))
        return value.toInt().also { require((allowZero && it >= 0 || !allowZero && it > 0) && it.toString() == value) }
    }
}
