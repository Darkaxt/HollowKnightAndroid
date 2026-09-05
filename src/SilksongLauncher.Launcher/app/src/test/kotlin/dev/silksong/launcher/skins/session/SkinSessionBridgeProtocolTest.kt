package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.nio.charset.StandardCharsets
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SkinSessionBridgeProtocolTest {
    @Test
    fun canonicalBridgePayloadRoundTripsDeterministically() {
        val canonical = SkinSessionBridgeCodec.canonical(handle)

        assertArrayEquals(canonical, SkinSessionBridgeCodec.canonical(assertOk(SkinSessionBridgeCodec.parse(canonical))))
        assertArrayEquals(canonical, SkinSessionBridgeCodec.canonical(handle))
        assertTrue(canonical.toString(StandardCharsets.US_ASCII).contains("leaseToken=$RAW_TOKEN\n"))
    }

    @Test
    fun handleDiagnosticsRedactTokenWhileDeliveryAndDataClassSemanticsRemainExact() {
        val copy = handle.copy()
        val wrapped = SkinResult.Ok(handle)

        assertEquals(handle, copy)
        assertEquals(RAW_TOKEN, handle.component5())
        assertFalse(handle.toString().contains(RAW_TOKEN))
        assertFalse(wrapped.toString().contains(RAW_TOKEN))
        assertTrue(handle.toString().contains("<redacted>"))
        assertTrue(SkinSessionBridgeCodec.canonical(handle).toString(StandardCharsets.US_ASCII).contains(RAW_TOKEN))
    }

    @Test
    fun bridgeCodecRejectsUnknownDuplicateMissingAndMalformedFields() {
        val canonical = SkinSessionBridgeCodec.canonical(handle).toString(StandardCharsets.US_ASCII)
        val malformed = listOf(
            canonical.replace("descriptorId=", "unknown="),
            canonical.replace("descriptorSha256=", "descriptorId="),
            canonical.replace(Regex("descriptorPath=.*\\n"), ""),
            canonical.replace("sessionSequence=7", "sessionSequence=07"),
            canonical.replace(handle.descriptorId.toString(), handle.descriptorId.toString().uppercase()),
            canonical.replace("descriptorSha256=${"b".repeat(64)}", "descriptorSha256=${"B".repeat(64)}"),
            canonical.replace("descriptorPath=${handle.descriptorPath}", "descriptorPath=/tmp/descriptor.json"),
            canonical.replace("leaseToken=$RAW_TOKEN", "leaseToken=${RAW_TOKEN.dropLast(2)}"),
            canonical.dropLast(1),
            canonical + "unknown=x\n",
        )

        malformed.forEach { assertError(SkinImportCode.DOCUMENT_INVALID, SkinSessionBridgeCodec.parse(it.toByteArray())) }
    }

    @Test
    fun exactIssuedPayloadClaimsOnceAndReplayRejectsBeforeDelegation() {
        val authority = RecordingAuthority()
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))

        assertEquals(claimedHead, assertOk(bridge.claim(issued, gameOwner)))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner))

        assertEquals(1, authority.claimCalls.get())
    }

    @Test
    fun concurrentlyConsumedPayloadDelegatesExactlyOnce() {
        val authority = RecordingAuthority()
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))
        val start = CountDownLatch(1)
        val pool = Executors.newFixedThreadPool(8)
        val results = (0 until 16).map {
            pool.submit<SkinResult<LeaseHead>> {
                start.await()
                bridge.claim(issued.copyOf(), gameOwner)
            }
        }

        start.countDown()
        val completed = results.map { it.get(10, TimeUnit.SECONDS) }
        pool.shutdownNow()

        assertEquals(1, completed.count { it is SkinResult.Ok })
        assertEquals(15, completed.count { it is SkinResult.Error && it.code == SkinImportCode.LIFECYCLE_BLOCKED })
        assertEquals(1, authority.claimCalls.get())
    }

    @Test
    fun changedBindingsRejectWithoutClaimOrMutation() {
        val mismatches = listOf(
            handle.copy(descriptorId = OTHER_DESCRIPTOR, descriptorPath = "sessions/$OTHER_DESCRIPTOR/descriptor.json"),
            handle.copy(descriptorSha256 = "c".repeat(64)),
            handle.copy(descriptorPath = "sessions/$OTHER_DESCRIPTOR/descriptor.json"),
            handle.copy(leaseId = OTHER_LEASE),
            handle.copy(leaseToken = "f".repeat(64)),
            handle.copy(sessionSequence = 8),
        )
        mismatches.forEach { mismatch ->
            val authority = RecordingAuthority()
            val bridge = SkinSessionBridgeProtocol(authority)
            assertOk(bridge.issue(handle))
            val payload = runCatching { SkinSessionBridgeCodec.canonical(mismatch) }
                .getOrElse { SkinSessionBridgeCodec.canonical(handle).copyOf().also { it[it.lastIndex] = 'x'.code.toByte() } }

            assertTrue(bridge.claim(payload, gameOwner) is SkinResult.Error)
            assertEquals(0, authority.claimCalls.get())
            assertEquals(0, authority.closeCalls.get())
        }
    }

    @Test
    fun invalidGameOwnerRejectsWithoutConsumingTheIssuedTransfer() {
        val authority = RecordingAuthority()
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner.copy(pid = 0)))
        assertEquals(claimedHead, assertOk(bridge.claim(issued, gameOwner)))
        assertEquals(1, authority.claimCalls.get())
    }

    @Test
    fun closeDelegatesOnlyForTheExactIssuedBinding() {
        val authority = RecordingAuthority()
        val bridge = SkinSessionBridgeProtocol(authority)
        assertOk(bridge.issue(handle))

        assertError(
            SkinImportCode.LIFECYCLE_BLOCKED,
            bridge.close(handle.copy(leaseToken = "f".repeat(64)), "LAUNCH_FAILED"),
        )
        assertError(
            SkinImportCode.LIFECYCLE_BLOCKED,
            bridge.close(handle, "raw-$RAW_TOKEN"),
        )
        assertEquals(0, authority.closeCalls.get())
        assertEquals(closedHead, assertOk(bridge.close(handle, "LAUNCH_FAILED")))

        assertEquals(0, authority.claimCalls.get())
        assertEquals(1, authority.closeCalls.get())
    }

    @Test
    fun retryableAndThrownClaimFailuresAllowOnlyTheExactSameOwnerToRetry() {
        val authority = RecordingAuthority(
            claimAction = { attempt ->
                when (attempt) {
                    1 -> SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "retry")
                    2 -> throw IllegalStateException("indeterminate")
                    else -> SkinResult.Ok(claimedHead)
                }
            },
        )
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, bridge.claim(issued, gameOwner))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, otherGameOwner))
        assertError(SkinImportCode.INDETERMINATE, bridge.claim(issued, gameOwner))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, otherGameOwner))
        assertEquals(claimedHead, assertOk(bridge.claim(issued, gameOwner)))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner))

        assertEquals(3, authority.claimCalls.get())
        assertEquals(listOf(gameOwner, gameOwner, gameOwner), authority.claimOwners)
    }

    @Test
    fun definitiveClaimRejectionConsumesTransferWithoutRetry() {
        val authority = RecordingAuthority(
            claimAction = { SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "rejected") },
        )
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner))

        assertEquals(1, authority.claimCalls.get())
    }

    @Test
    fun closeCannotOverlapAnInFlightClaim() {
        val entered = CountDownLatch(1)
        val release = CountDownLatch(1)
        val authority = RecordingAuthority(
            claimAction = {
                entered.countDown()
                assertTrue(release.await(10, TimeUnit.SECONDS))
                SkinResult.Ok(claimedHead)
            },
        )
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))
        val pool = Executors.newSingleThreadExecutor()
        val claim = pool.submit<SkinResult<LeaseHead>> { bridge.claim(issued, gameOwner) }
        assertTrue(entered.await(10, TimeUnit.SECONDS))

        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.close(handle, "GAME_EXIT"))
        assertEquals(0, authority.closeCalls.get())

        release.countDown()
        assertEquals(claimedHead, assertOk(claim.get(10, TimeUnit.SECONDS)))
        assertEquals(closedHead, assertOk(bridge.close(handle, "GAME_EXIT")))
        pool.shutdownNow()
    }

    @Test
    fun successfulCloseTerminalizesClaimAndRetainsOnlyRedactedBindingEvidence() {
        val authority = RecordingAuthority()
        val bridge = SkinSessionBridgeProtocol(authority)
        val issued = assertOk(bridge.issue(handle))

        assertEquals(closedHead, assertOk(bridge.close(handle, "LAUNCH_FAILED")))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.claim(issued, gameOwner))
        assertEquals(closedHead, assertOk(bridge.close(handle, "LAUNCH_FAILED")))

        assertEquals(0, authority.claimCalls.get())
        assertEquals(1, authority.closeCalls.get())
        val bindingsField = SkinSessionBridgeProtocol::class.java.getDeclaredField("bindings").apply { isAccessible = true }
        val bindings = bindingsField.get(bridge) as Map<*, *>
        val terminal = bindings.values.single()!!
        assertTrue(terminal.javaClass.simpleName.contains("Closed"))
        assertFalse(terminal.javaClass.declaredFields.any { field ->
            field.isAccessible = true
            val value = field.get(terminal)
            value is SkinLaunchHandle || value == RAW_TOKEN
        })
    }

    @Test
    fun closeFailurePinsReasonAndAllowsOnlyExactIdempotentRetry() {
        val authority = RecordingAuthority(
            closeAction = { attempt ->
                when (attempt) {
                    1 -> SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "retry")
                    2 -> throw IllegalStateException("indeterminate")
                    else -> SkinResult.Ok(closedHead)
                }
            },
        )
        val bridge = SkinSessionBridgeProtocol(authority)
        assertOk(bridge.issue(handle))

        assertError(SkinImportCode.DURABILITY_UNAVAILABLE, bridge.close(handle, "LAUNCH_FAILED"))
        assertError(SkinImportCode.LIFECYCLE_BLOCKED, bridge.close(handle, "OTHER_REASON"))
        assertError(SkinImportCode.INDETERMINATE, bridge.close(handle, "LAUNCH_FAILED"))
        assertEquals(closedHead, assertOk(bridge.close(handle, "LAUNCH_FAILED")))
        assertEquals(closedHead, assertOk(bridge.close(handle, "LAUNCH_FAILED")))

        assertEquals(3, authority.closeCalls.get())
        assertEquals(listOf("LAUNCH_FAILED", "LAUNCH_FAILED", "LAUNCH_FAILED"), authority.closeReasons)
    }

    @Test
    fun bridgeSurfaceHasNoScannerRegistryOrArbitraryFilePort() {
        val methods = SkinSessionBridgeProtocol::class.java.declaredMethods
            .filterNot { it.isSynthetic || !java.lang.reflect.Modifier.isPublic(it.modifiers) }
            .map { it.name }
            .toSet()
        val parameterTypes = SkinSessionBridgeProtocol::class.java.declaredMethods
            .flatMap { it.parameterTypes.toList() }

        assertEquals(setOf("issue", "claim", "close"), methods)
        assertFalse(parameterTypes.any { java.io.File::class.java.isAssignableFrom(it) })
        assertFalse(SkinSessionBridgeAuthority::class.java.declaredMethods.any { method ->
            method.parameterTypes.any { java.io.File::class.java.isAssignableFrom(it) }
        })
    }

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private class RecordingAuthority(
        private val claimAction: (Int) -> SkinResult<LeaseHead> = { SkinResult.Ok(claimedHead) },
        private val closeAction: (Int) -> SkinResult<LeaseHead> = { SkinResult.Ok(closedHead) },
    ) : SkinSessionBridgeAuthority {
        val claimCalls = AtomicInteger()
        val closeCalls = AtomicInteger()
        val claimOwners = mutableListOf<ProcessIdentity>()
        val closeReasons = mutableListOf<String>()

        override fun claim(handle: SkinLaunchHandle, exactGameOwner: ProcessIdentity): SkinResult<LeaseHead> {
            claimOwners += exactGameOwner
            return claimAction(claimCalls.incrementAndGet())
        }

        override fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> {
            closeReasons += reason
            return closeAction(closeCalls.incrementAndGet())
        }
    }

    private companion object {
        val DESCRIPTOR_ID: UUID = UUID.fromString("32345678-1234-4234-8234-123456789abc")
        val LEASE_ID: UUID = UUID.fromString("42345678-1234-4234-8234-123456789abc")
        val OTHER_DESCRIPTOR: UUID = UUID.fromString("52345678-1234-4234-8234-123456789abc")
        val OTHER_LEASE: UUID = UUID.fromString("62345678-1234-4234-8234-123456789abc")
        const val RAW_TOKEN = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        val handle = SkinLaunchHandle(
            DESCRIPTOR_ID,
            "b".repeat(64),
            "sessions/$DESCRIPTOR_ID/descriptor.json",
            LEASE_ID,
            RAW_TOKEN,
            7,
        )
        val gameOwner = ProcessIdentity(1000, 1002, "100")
        val otherGameOwner = ProcessIdentity(1000, 1003, "101")
        val claimedHead = LeaseHead(DESCRIPTOR_ID, LEASE_ID, 1, LeaseState.GAME_OWNED, "c".repeat(64))
        val closedHead = LeaseHead(DESCRIPTOR_ID, LEASE_ID, 2, LeaseState.CLOSED, "d".repeat(64))
    }
}
