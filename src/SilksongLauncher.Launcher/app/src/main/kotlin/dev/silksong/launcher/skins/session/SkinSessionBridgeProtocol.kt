package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.nio.charset.StandardCharsets
import java.util.UUID

/** Narrow claim/close authority used by the non-production bridge seam. */
internal interface SkinSessionBridgeAuthority {
    fun claim(handle: SkinLaunchHandle, exactGameOwner: ProcessIdentity): SkinResult<LeaseHead>
    fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead>
}

internal class SkinSessionStoreBridgeAuthority(
    private val store: SkinSessionStore,
) : SkinSessionBridgeAuthority {
    override fun claim(handle: SkinLaunchHandle, exactGameOwner: ProcessIdentity): SkinResult<LeaseHead> =
        store.claim(handle, exactGameOwner)

    override fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> =
        store.close(handle, reason)
}

/** Strict finite canonical transfer codec. Its bytes are delivery-only and must never be persisted. */
internal object SkinSessionBridgeCodec {
    private const val HEADER = "SKIN_SESSION_BRIDGE_V1"
    private const val MAX_BYTES = 512
    private val DIGEST = Regex("[0-9a-f]{64}")
    private val TOKEN = Regex("[0-9a-f]{64}")
    private val DECIMAL = Regex("0|[1-9][0-9]*")
    private val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val FIELDS = setOf(
        "descriptorId",
        "descriptorSha256",
        "descriptorPath",
        "leaseId",
        "leaseToken",
        "sessionSequence",
    )

    fun canonical(handle: SkinLaunchHandle): ByteArray {
        validate(handle)
        return buildString {
            append(HEADER).append('\n')
            append("descriptorId=").append(handle.descriptorId).append('\n')
            append("descriptorSha256=").append(handle.descriptorSha256).append('\n')
            append("descriptorPath=").append(handle.descriptorPath).append('\n')
            append("leaseId=").append(handle.leaseId).append('\n')
            append("leaseToken=").append(handle.leaseToken).append('\n')
            append("sessionSequence=").append(handle.sessionSequence).append('\n')
        }.toByteArray(StandardCharsets.US_ASCII).also {
            require(it.size <= MAX_BYTES) { "Bridge payload exceeds its bound" }
        }
    }

    fun parse(bytes: ByteArray): SkinResult<SkinLaunchHandle> = try {
        val snapshot = bytes.copyOf()
        require(snapshot.size in 1..MAX_BYTES && snapshot.all { (it.toInt() and 0xff) <= 0x7f })
        val lines = snapshot.toString(StandardCharsets.US_ASCII).split('\n')
        require(lines.size == 8 && lines.first() == HEADER && lines.last().isEmpty())
        val fields = linkedMapOf<String, String>()
        lines.subList(1, 7).forEach { line ->
            val separator = line.indexOf('=')
            require(separator > 0 && separator == line.lastIndexOf('='))
            val key = line.substring(0, separator)
            val value = line.substring(separator + 1)
            require(key in FIELDS && value.isNotEmpty() && fields.put(key, value) == null)
        }
        require(fields.keys == FIELDS)
        val handle = SkinLaunchHandle(
            descriptorId = parseUuid(fields.getValue("descriptorId")),
            descriptorSha256 = fields.getValue("descriptorSha256").toCharArray().concatToString(),
            descriptorPath = fields.getValue("descriptorPath").toCharArray().concatToString(),
            leaseId = parseUuid(fields.getValue("leaseId")),
            leaseToken = fields.getValue("leaseToken").toCharArray().concatToString(),
            sessionSequence = parseSequence(fields.getValue("sessionSequence")),
        )
        validate(handle)
        require(canonical(handle).contentEquals(snapshot))
        SkinResult.Ok(handle)
    } catch (_: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Invalid skin session bridge payload")
    }

    internal fun copy(handle: SkinLaunchHandle): SkinLaunchHandle = handle.copy(
        descriptorSha256 = handle.descriptorSha256.toCharArray().concatToString(),
        descriptorPath = handle.descriptorPath.toCharArray().concatToString(),
        leaseToken = handle.leaseToken.toCharArray().concatToString(),
    )

    private fun validate(handle: SkinLaunchHandle) {
        requireUuid(handle.descriptorId)
        requireUuid(handle.leaseId)
        require(handle.descriptorId != handle.leaseId)
        require(DIGEST.matches(handle.descriptorSha256))
        require(TOKEN.matches(handle.leaseToken))
        require(handle.sessionSequence >= 0L)
        require(handle.descriptorPath == "sessions/${handle.descriptorId}/descriptor.json")
        require(handle.descriptorPath.length <= 128)
    }

    private fun requireUuid(value: UUID) {
        val text = value.toString()
        require(UUID_TEXT.matches(text) && UUID.fromString(text).toString() == text && value != NIL_UUID)
    }

    private fun parseUuid(value: String): UUID {
        require(UUID_TEXT.matches(value))
        return UUID.fromString(value).also { require(it.toString() == value && it != NIL_UUID) }
    }

    private fun parseSequence(value: String): Long {
        require(DECIMAL.matches(value))
        return value.toLong().also { require(it >= 0L && it.toString() == value) }
    }

    private val NIL_UUID = UUID(0L, 0L)
}

/**
 * In-memory, one-use bridge protocol. It owns no scanner, filesystem, registry, Android Intent, or launch surface.
 */
internal class SkinSessionBridgeProtocol(
    private val authority: SkinSessionBridgeAuthority,
) {
    private val monitor = Any()
    private val bindings = mutableMapOf<BindingKey, BindingRecord>()

    internal constructor(store: SkinSessionStore) : this(SkinSessionStoreBridgeAuthority(store))

    fun issue(handle: SkinLaunchHandle): SkinResult<ByteArray> {
        val canonical = try {
            SkinSessionBridgeCodec.canonical(handle)
        } catch (_: Exception) {
            return blocked("Skin session bridge handle is invalid")
        }
        val copy = when (val parsed = SkinSessionBridgeCodec.parse(canonical)) {
            is SkinResult.Error -> return blocked("Skin session bridge handle is invalid")
            is SkinResult.Ok -> parsed.value
        }
        val fingerprint = fingerprint(canonical)
        synchronized(monitor) {
            val key = copy.bindingKey()
            when (val existing = bindings[key]) {
                null -> bindings[key] = LiveBinding(SkinSessionBridgeCodec.copy(copy))
                is LiveBinding -> if (existing.handle != copy) {
                    return blocked("Skin session bridge binding conflicts")
                }
                is ClosedBinding -> if (existing.fingerprint != fingerprint) {
                    return blocked("Skin session bridge binding conflicts")
                }
            }
        }
        return SkinResult.Ok(canonical.copyOf())
    }

    fun claim(payload: ByteArray, exactGameOwner: ProcessIdentity): SkinResult<LeaseHead> {
        val handle = when (val parsed = SkinSessionBridgeCodec.parse(payload.copyOf())) {
            is SkinResult.Error -> return parsed
            is SkinResult.Ok -> parsed.value
        }
        if (!SkinLeaseStateCodec.validOwner(exactGameOwner)) {
            return blocked("Skin session bridge game owner is invalid")
        }
        val owner = exactGameOwner.copy(
            processStartToken = exactGameOwner.processStartToken.toCharArray().concatToString(),
        )
        val key = handle.bindingKey()
        val binding = synchronized(monitor) {
            when (val record = bindings[key]) {
                null -> return blocked("Skin session bridge binding was not issued")
                is ClosedBinding -> return blocked("Skin session bridge binding is closed")
                is LiveBinding -> {
                    if (record.handle != handle || record.state != BridgeLifecycle.ISSUED || record.closeRetryReason != null) {
                        return blocked("Skin session bridge binding cannot be claimed")
                    }
                    if (record.claimOwner != null && record.claimOwner != owner) {
                        return blocked("Skin session bridge claim owner mismatches its retry")
                    }
                    record.claimOwner = owner
                    record.state = BridgeLifecycle.CLAIMING
                    record
                }
            }
        }
        val outcome = try {
            authority.claim(SkinSessionBridgeCodec.copy(handle), owner)
        } catch (_: Exception) {
            SkinResult.Error(SkinImportCode.INDETERMINATE, "Skin session bridge claim failed")
        }
        synchronized(monitor) {
            check(bindings[key] === binding && binding.state == BridgeLifecycle.CLAIMING)
            binding.state = when (outcome) {
                is SkinResult.Ok -> BridgeLifecycle.CLAIMED
                is SkinResult.Error -> if (outcome.code in RETRYABLE_CLAIM_CODES) {
                    BridgeLifecycle.ISSUED
                } else {
                    BridgeLifecycle.CLAIM_REJECTED
                }
            }
        }
        return outcome
    }

    fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> {
        if (!CLOSE_REASON.matches(reason)) {
            return blocked("Skin session bridge close reason is invalid")
        }
        val copy = try {
            val canonical = SkinSessionBridgeCodec.canonical(handle)
            when (val parsed = SkinSessionBridgeCodec.parse(canonical)) {
                is SkinResult.Error -> return blocked("Skin session bridge close handle is invalid")
                is SkinResult.Ok -> parsed.value
            }
        } catch (_: Exception) {
            return blocked("Skin session bridge close handle is invalid")
        }
        val key = copy.bindingKey()
        val fingerprint = fingerprint(SkinSessionBridgeCodec.canonical(copy))
        var priorState: BridgeLifecycle? = null
        val binding = synchronized(monitor) {
            when (val record = bindings[key]) {
                null -> return blocked("Skin session bridge binding was not issued")
                is ClosedBinding -> {
                    if (record.fingerprint != fingerprint || record.reason != reason) {
                        return blocked("Skin session bridge terminal close mismatches")
                    }
                    return SkinResult.Ok(record.result)
                }
                is LiveBinding -> {
                    if (record.handle != copy) return blocked("Skin session bridge close binding mismatches")
                    if (record.state == BridgeLifecycle.CLAIMING || record.state == BridgeLifecycle.CLOSING) {
                        return blocked("Skin session bridge binding is in flight")
                    }
                    if (record.closeRetryReason != null && record.closeRetryReason != reason) {
                        return blocked("Skin session bridge close reason mismatches its retry")
                    }
                    priorState = record.state
                    record.closeRetryReason = reason.toCharArray().concatToString()
                    record.state = BridgeLifecycle.CLOSING
                    record
                }
            }
        }
        val outcome = try {
            authority.close(SkinSessionBridgeCodec.copy(copy), reason.toCharArray().concatToString())
        } catch (_: Exception) {
            SkinResult.Error(SkinImportCode.INDETERMINATE, "Skin session bridge close failed")
        }
        synchronized(monitor) {
            check(bindings[key] === binding && binding.state == BridgeLifecycle.CLOSING)
            when (outcome) {
                is SkinResult.Ok -> bindings[key] = ClosedBinding(fingerprint, reason, outcome.value)
                is SkinResult.Error -> binding.state = requireNotNull(priorState)
            }
        }
        return outcome
    }

    private fun SkinLaunchHandle.bindingKey(): BindingKey = BindingKey(descriptorId, leaseId)

    private fun fingerprint(bytes: ByteArray): String = java.security.MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString(separator = "") { "%02x".format(it.toInt() and 0xff) }

    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)

    private data class BindingKey(val descriptorId: UUID, val leaseId: UUID)

    private sealed interface BindingRecord

    private data class LiveBinding(
        val handle: SkinLaunchHandle,
        var state: BridgeLifecycle = BridgeLifecycle.ISSUED,
        var claimOwner: ProcessIdentity? = null,
        var closeRetryReason: String? = null,
    ) : BindingRecord

    private data class ClosedBinding(
        val fingerprint: String,
        val reason: String,
        val result: LeaseHead,
        val state: BridgeLifecycle = BridgeLifecycle.CLOSED,
    ) : BindingRecord

    private enum class BridgeLifecycle {
        ISSUED,
        CLAIMING,
        CLAIMED,
        CLAIM_REJECTED,
        CLOSING,
        CLOSED,
    }

    private companion object {
        val CLOSE_REASON = Regex("[A-Z][A-Z0-9_]{0,127}")
        val RETRYABLE_CLAIM_CODES = setOf(SkinImportCode.DURABILITY_UNAVAILABLE, SkinImportCode.INDETERMINATE)
    }
}
