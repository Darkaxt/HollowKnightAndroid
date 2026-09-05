package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.ActivationSnapshot
import dev.silksong.launcher.skins.registry.InterlockState
import dev.silksong.launcher.skins.registry.RotationInterlock
import dev.silksong.launcher.skins.registry.SkinActivation
import dev.silksong.launcher.skins.registry.SkinBindingToken
import dev.silksong.launcher.skins.registry.SkinMode
import dev.silksong.launcher.skins.registry.SkinOperationKind
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Before
import org.junit.Test

class SkinLaunchDescriptorTest {
    @Before
    fun setUp() {
        PinnedCatalogFixture.load()
    }

    @Test
    fun roundTripsCompleteStrictLaunchDescriptor() {
        val descriptor = descriptor(interlockedActivation())
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)

        val parsed = assertOk(
            SkinLaunchDescriptorCodec.parse(
                canonical,
                sha256(canonical),
                DescriptorExpectations(
                    descriptor.descriptorId,
                    descriptor.profileId,
                    descriptor.gameVersion,
                    descriptor.catalogId,
                    descriptor.catalogSha256,
                    descriptor.leaseId,
                ),
            ),
        )

        assertEquals(descriptor, parsed)
        assertArrayEquals(canonical, SkinLaunchDescriptorCodec.canonical(parsed))
    }

    @Test
    fun rejectsDescriptorDuplicateUnknownMismatchOrderHashAndPath() {
        val descriptor = descriptor()
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)
        val text = canonical.toString(StandardCharsets.UTF_8)
        val expectations = DescriptorExpectations(
            descriptor.descriptorId,
            descriptor.profileId,
            descriptor.gameVersion,
            descriptor.catalogId,
            descriptor.catalogSha256,
            descriptor.leaseId,
        )
        val duplicate = text.replaceFirst("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")
        val unknown = text.dropLast(1) + ",\"unknown\":true}"
        val withNull = text.replace("\"leaseTokenSha256\":\"${descriptor.leaseTokenSha256}\"", "\"leaseTokenSha256\":null")
        val schemaMismatch = text.replaceFirst("\"schemaVersion\":1", "\"schemaVersion\":2")
        val path = text.replace("objects/sha256/aa/${hex('a')}", "objects/sha256/bb/${hex('a')}")
        val attackerRoot = text.replace("\"objectRoot\":\"objects/", "\"objectRoot\":\"/attacker/objects/")
        val traversal = text.replace("objects/sha256/aa/${hex('a')}", "objects/sha256/aa/../aa/${hex('a')}")
        val crossOwner = text.replace("\"objectRoot\":\"objects/", "\"objectRoot\":\"import-receipts/")
        val uppercaseId = text.replace(descriptor.descriptorId.toString(), descriptor.descriptorId.toString().uppercase())

        listOf(duplicate, unknown, withNull, schemaMismatch, path, attackerRoot, traversal, crossOwner, uppercaseId).forEach { invalid ->
            val bytes = invalid.toByteArray(StandardCharsets.UTF_8)
            assertError(SkinImportCode.DOCUMENT_INVALID, SkinLaunchDescriptorCodec.parse(bytes, sha256(bytes), expectations))
        }
        val beta = descriptor.packs.single().copy(id = "beta", name = "Beta", candidateKey = hex('9'), rotationEligible = true)
        val ordered = descriptor.copy(packs = listOf(descriptor.packs.single(), beta))
        val wrongOrder = swapFirstTwoPackObjects(SkinLaunchDescriptorCodec.canonical(ordered).toString(StandardCharsets.UTF_8))
        val wrongOrderBytes = wrongOrder.toByteArray(StandardCharsets.UTF_8)
        assertError(SkinImportCode.DOCUMENT_INVALID, SkinLaunchDescriptorCodec.parse(wrongOrderBytes, sha256(wrongOrderBytes), expectations))
        val duplicateOwner = ordered.copy(packs = listOf(ordered.packs.first(), beta.copy(candidateKey = ordered.packs.first().candidateKey)))
        assertThrows(IllegalArgumentException::class.java) {
            SkinLaunchDescriptorCodec.canonical(duplicateOwner)
        }
        assertError(
            SkinImportCode.DOCUMENT_INVALID,
            SkinLaunchDescriptorCodec.parse(canonical, hex('f'), expectations),
        )
    }

    @Test
    fun includesInterlockSnapshotSelectionsInDescriptorUnion() {
        val descriptor = descriptorWithSnapshotTargetSelection()
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)
        val expectations = expectations(descriptor)

        val parsed = assertOk(SkinLaunchDescriptorCodec.parse(canonical, sha256(canonical), expectations))
        assertEquals(listOf("alpha", "beta", "gamma"), parsed.packs.map(DescriptorPackEnvelope::id))
        val omitted = removePackObject(canonical.toString(StandardCharsets.UTF_8), "gamma").toByteArray(StandardCharsets.UTF_8)
        assertError(SkinImportCode.DOCUMENT_INVALID, SkinLaunchDescriptorCodec.parse(omitted, sha256(omitted), expectations))
    }

    @Test
    fun usesAbsoluteCatalogOrdinalsForSparseTextures() {
        val catalog = PinnedCatalogFixture.load()
        val sparse = envelope(hex('a'), hex('b'), hex('c'), hex('d'), hex('e')).copy(
            textures = listOf(
                texture(0, catalog.paths[0], hex('e')),
                texture(2, catalog.paths[2], hex('f')),
            ),
        )
        val descriptor = descriptor().copy(packs = listOf(descriptor().packs.single().copy(currentObject = sparse)))
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)
        val parsed = assertOk(SkinLaunchDescriptorCodec.parse(canonical, sha256(canonical), expectations(descriptor)))

        assertEquals(listOf(0, 2), parsed.packs.single().currentObject.textures.map(DescriptorTextureEnvelope::ordinal))
        val forged = canonical.toString(StandardCharsets.UTF_8).replace("\"ordinal\":2", "\"ordinal\":1")
            .toByteArray(StandardCharsets.UTF_8)
        assertError(SkinImportCode.DOCUMENT_INVALID, SkinLaunchDescriptorCodec.parse(forged, sha256(forged), expectations(descriptor)))
    }

    @Test
    fun snapshotsDescriptorBytesBeforeHashingAndDecoding() {
        val descriptor = descriptor()
        val bytes = SkinLaunchDescriptorCodec.canonical(descriptor)
        val expectedSha256 = sha256(bytes)
        val parsed = assertOk(
            SkinLaunchDescriptorCodec.parseAfterSnapshotForTest(bytes, expectedSha256, expectations(descriptor)) {
                bytes.fill('x'.code.toByte())
            },
        )

        assertEquals(descriptor, parsed)
    }

    @Test
    fun enforcesTextureLengthBounds() {
        val maximum = descriptorWithTextureLength(SkinLimits.V1.textureBytes)
        val maximumBytes = SkinLaunchDescriptorCodec.canonical(maximum)
        assertOk(SkinLaunchDescriptorCodec.parse(maximumBytes, sha256(maximumBytes), expectations(maximum)))

        val overflow = descriptorWithTextureLength(Long.MAX_VALUE)
        assertThrows(IllegalArgumentException::class.java) {
            SkinLaunchDescriptorCodec.canonical(overflow)
        }
    }

    @Test
    fun rejectsDeepBroadAndAggregateBoundedJsonBeforeDescentOrAllocation() {
        val expected = expectations(descriptor())
        val deep = ("[".repeat(33) + "0" + "]".repeat(33)).toByteArray(StandardCharsets.UTF_8)
        val broad = ("[" + List(257) { "0" }.joinToString(",") + "]").toByteArray(StandardCharsets.UTF_8)
        val aggregate = buildString {
            append('[')
            repeat(8) { outer ->
                if (outer > 0) append(',')
                append('[')
                repeat(256) { middle ->
                    if (middle > 0) append(',')
                    append('[')
                    append(List(256) { "0" }.joinToString(","))
                    append(']')
                }
                append(']')
            }
            append(']')
        }.toByteArray(StandardCharsets.UTF_8)

        listOf(deep, broad, aggregate).forEach { hostile ->
            assertError(SkinImportCode.DOCUMENT_INVALID, SkinLaunchDescriptorCodec.parse(hostile, sha256(hostile), expected))
        }
    }

    @Test
    fun persistsOnlyLeaseTokenSha256AndDeliversNoRawTokenInDescriptor() {
        val rawLeaseToken = "t".repeat(32).toByteArray(StandardCharsets.US_ASCII)
        val descriptor = descriptor(leaseTokenSha256 = sha256(rawLeaseToken))
        val canonical = SkinLaunchDescriptorCodec.canonical(descriptor)

        assertFalse(canonical.toString(StandardCharsets.UTF_8).contains(rawLeaseToken.toString(StandardCharsets.UTF_8)))
        assertTrue(canonical.toString(StandardCharsets.UTF_8).contains(descriptor.leaseTokenSha256))
        assertTrue(SkinLaunchDescriptor::class.java.declaredFields.none { it.name.contains("raw", ignoreCase = true) })
    }

    @Test
    fun enforcesDescriptorEightMiBBound() {
        val descriptor = descriptor()
        val oversized = ByteArray(8 * 1024 * 1024 + 1)

        assertError(
            SkinImportCode.DOCUMENT_INVALID,
            SkinLaunchDescriptorCodec.parse(
                oversized,
                sha256(oversized),
                DescriptorExpectations(
                    descriptor.descriptorId,
                    descriptor.profileId,
                    descriptor.gameVersion,
                    descriptor.catalogId,
                    descriptor.catalogSha256,
                    descriptor.leaseId,
                ),
            ),
        )
    }

    private fun swapFirstTwoPackObjects(value: String): String {
        val array = value.indexOf("\"packs\":[") + "\"packs\":[".length
        val firstEnd = closingObject(value, array)
        val secondStart = firstEnd + 2
        val secondEnd = closingObject(value, secondStart)
        return value.substring(0, array) + value.substring(secondStart, secondEnd + 1) + "," +
            value.substring(array, firstEnd + 1) + value.substring(secondEnd + 1)
    }

    private fun closingObject(value: String, start: Int): Int {
        var depth = 0
        var quoted = false
        var escaped = false
        for (index in start until value.length) {
            val character = value[index]
            if (quoted) {
                if (escaped) escaped = false else if (character == '\\') escaped = true else if (character == '"') quoted = false
            } else when (character) {
                '"' -> quoted = true
                '{' -> depth++
                '}' -> if (--depth == 0) return index
            }
        }
        error("Missing pack object boundary")
    }

    private fun removePackObject(value: String, id: String): String {
        val array = value.indexOf("\"packs\":[") + "\"packs\":[".length
        var start = array
        while (value[start] != ']') {
            val end = closingObject(value, start)
            if (value.substring(start, end + 1).contains("\"id\":\"$id\"")) {
                val before = if (start == array) start else start - 1
                val after = if (value.getOrNull(end + 1) == ',') end + 2 else end + 1
                return value.substring(0, before) + value.substring(after)
            }
            start = end + 2
        }
        error("Missing descriptor pack: $id")
    }

    private fun expectations(value: SkinLaunchDescriptor) = DescriptorExpectations(
        value.descriptorId,
        value.profileId,
        value.gameVersion,
        value.catalogId,
        value.catalogSha256,
        value.leaseId,
    )

    private fun descriptorWithSnapshotTargetSelection(): SkinLaunchDescriptor {
        val alpha = envelope(hex('a'), hex('b'), hex('c'), hex('d'), hex('e'))
        val beta = envelope(hex('1'), hex('2'), hex('3'), hex('4'), hex('5'))
        val gamma = envelope(hex('6'), hex('7'), hex('8'), hex('9'), hex('a'))
        val prior = ActivationSnapshot(SkinMode.ON, "alpha", ActiveVisual.Pack("alpha", alpha.treeSha256, alpha.contentSha256, alpha.importReceiptSha256), 7)
        val target = ActivationSnapshot(SkinMode.ON, "gamma", ActiveVisual.Pack("beta", beta.treeSha256, beta.contentSha256, beta.importReceiptSha256), 8)
        return descriptor(
            SkinActivation(
                prior.mode,
                prior.selectedPackId,
                prior.active,
                prior.skinStamp,
                RotationInterlock(
                    InterlockState.ARMED,
                    "42345678-1234-4234-8234-123456789abc",
                    SkinOperationKind.MODE_ON,
                    "52345678-1234-4234-8234-123456789abc",
                    hex('f'),
                    prior,
                    target,
                    SkinBindingToken("binding-token"),
                    true,
                    null,
                    null,
                ),
            ),
        ).copy(
            packs = listOf(
                DescriptorPackEnvelope("alpha", "Alpha", "Unknown", hex('7'), false, alpha, null),
                DescriptorPackEnvelope("beta", "Beta", "Unknown", hex('8'), false, beta, null),
                DescriptorPackEnvelope("gamma", "Gamma", "Unknown", hex('9'), false, gamma, null),
            ),
        )
    }

    private fun descriptorWithTextureLength(length: Long): SkinLaunchDescriptor {
        val base = descriptor()
        val current = base.packs.single().currentObject.copy(
            textures = base.packs.single().currentObject.textures.map { it.copy(length = length) },
        )
        return base.copy(packs = listOf(base.packs.single().copy(currentObject = current)))
    }

    private fun descriptor(
        activation: SkinActivation = SkinActivation(
            SkinMode.ON,
            "alpha",
            ActiveVisual.Pack("alpha", hex('a'), hex('b'), hex('c')),
            7,
            RotationInterlock.clear(),
        ),
        leaseTokenSha256: String = hex('f'),
    ): SkinLaunchDescriptor {
        val current = envelope(hex('a'), hex('b'), hex('c'), hex('d'), hex('e'))
        val retained = envelope(hex('1'), hex('2'), hex('3'), hex('4'), hex('5'))
        return SkinLaunchDescriptor(
            schemaVersion = 1,
            descriptorId = UUID.fromString("12345678-1234-4234-8234-123456789abc"),
            sessionSequence = 9,
            profileId = "hollow-knight",
            gameVersion = "1.5.12620",
            catalogId = "hk-custom-knight-v3.5.0-205",
            catalogSha256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a",
            registryGenerationId = "22345678-1234-4234-8234-123456789abc",
            registryGenerationSha256 = hex('6'),
            activation = activation,
            packs = listOf(
                DescriptorPackEnvelope(
                    "alpha",
                    "Alpha",
                    "Unknown",
                    hex('7'),
                    true,
                    current,
                    retained.takeIf { activation.active is ActiveVisual.Pack && activation.active.treeSha256 != current.treeSha256 },
                ),
            ),
            leaseId = UUID.fromString("32345678-1234-4234-8234-123456789abc"),
            leaseTokenSha256 = leaseTokenSha256,
        )
    }

    private fun interlockedActivation(): SkinActivation {
        val old = ActiveVisual.Pack("alpha", hex('1'), hex('2'), hex('3'))
        val prior = ActivationSnapshot(SkinMode.ON, "alpha", old, 7)
        return SkinActivation(
            prior.mode,
            prior.selectedPackId,
            prior.active,
            prior.skinStamp,
            RotationInterlock(
                InterlockState.ARMED,
                "42345678-1234-4234-8234-123456789abc",
                SkinOperationKind.MODE_ON,
                "52345678-1234-4234-8234-123456789abc",
                hex('8'),
                prior,
                ActivationSnapshot(SkinMode.ON, "alpha", old, 8),
                SkinBindingToken("binding-token"),
                true,
                null,
                null,
            ),
        )
    }

    private fun envelope(
        tree: String,
        content: String,
        receipt: String,
        manifest: String,
        source: String,
    ): DescriptorObjectEnvelope = DescriptorObjectEnvelope(
        objectRoot = "objects/sha256/${tree.take(2)}/$tree",
        receiptPath = "import-receipts/sha256/${receipt.take(2)}/$receipt",
        treeSha256 = tree,
        contentSha256 = content,
        manifestSha256 = manifest,
        importReceiptSha256 = receipt,
        textures = listOf(texture(0, "Knight.png", source)),
    )

    private fun texture(ordinal: Int, target: String, source: String) = DescriptorTextureEnvelope(
        ordinal = ordinal,
        target = target,
        sourceRelativePath = "pack/assets/${SkinIdentity.base32DigestHex(source)}",
        sourceSha256 = source,
        length = 67,
    )

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun hex(character: Char): String = character.toString().repeat(64)

    private fun <T> assertOk(result: SkinResult<T>): T {
        assertTrue("Expected success, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun assertError(code: SkinImportCode, result: SkinResult<*>) {
        assertTrue("Expected $code, got $result", result is SkinResult.Error)
        assertEquals(code, (result as SkinResult.Error).code)
    }
}
