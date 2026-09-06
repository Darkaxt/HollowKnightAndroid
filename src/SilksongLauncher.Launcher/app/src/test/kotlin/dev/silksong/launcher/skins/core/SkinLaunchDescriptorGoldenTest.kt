package dev.silksong.launcher.skins.core

import com.google.gson.JsonArray
import com.google.gson.JsonElement
import com.google.gson.JsonNull
import com.google.gson.JsonObject
import com.google.gson.JsonPrimitive
import com.google.gson.Strictness
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.InterlockState
import dev.silksong.launcher.skins.session.DescriptorExpectations
import dev.silksong.launcher.skins.session.SkinLaunchDescriptor
import dev.silksong.launcher.skins.session.SkinLaunchDescriptorCodec
import java.io.File
import java.io.StringReader
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

class SkinLaunchDescriptorGoldenTest {
    private val contract = "descriptor-empty-clear-v1"
    private val digest = Regex("[0-9a-f]{64}")
    private val uuid = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val fixtureRelative = "tools/skin-goldens/v1/launch-descriptors.json"

    // Anchored to the test class output, never the worker's runtime CWD.
    private fun repository(): File = generateSequence(File(requireNotNull(requireNotNull(SkinLaunchDescriptorGoldenTest::class.java.protectionDomain).codeSource).location.toURI())) { it.parentFile }
        .firstOrNull { File(it, fixtureRelative).isFile && File(it, "src/SilksongLauncher.Launcher/settings.gradle.kts").isFile }
        ?: error("Cannot locate normative golden repository from test class output")

    private fun source(): String = decode(File(repository(), fixtureRelative).readBytes())
    private fun decode(bytes: ByteArray): String = StandardCharsets.UTF_8.newDecoder()
        .onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)
        .decode(ByteBuffer.wrap(bytes)).toString()

    @Before fun loadCatalog() {
        val result = File(repository(), "docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt").inputStream().use { HollowKnightCatalogPaths.load(it) }
        assertTrue("Pinned normative catalog must load: $result", result is SkinResult.Ok)
    }

    @Test fun pinnedIndependentDescriptors() {
        val cases = load(source())
        assertTrue(cases.size >= 25)
        assertTrue(cases.count { text(it.getAsJsonObject("expected")["outcome"]) == "OK" } >= 5)
        assertTrue(cases.any { text(it.getAsJsonObject("expected")["outcome"]) == "DOCUMENT_INVALID" })
        cases.forEach { case ->
            try { verify(case) } catch (error: AssertionError) { throw AssertionError(text(case["caseId"]), error) }
        }
    }

    @Test fun harnessRejectsWrongOutcomeDamagedLogAndAllRejectParser() {
        val first = load(source()).first { text(it.getAsJsonObject("expected")["outcome"]) == "OK" }
        val wrong = first.deepCopy().apply { add("expected", outer("""{"outcome":"DOCUMENT_INVALID","violation":"FAKE"}""")) }
        assertThrows(AssertionError::class.java) { verify(wrong) }
        val damaged = first.deepCopy()
        damaged.getAsJsonObject("expected").getAsJsonArray("semanticLog")[0].asJsonArray.set(9, JsonPrimitive("1".repeat(64)))
        assertThrows(AssertionError::class.java) { verify(damaged) }
        assertThrows(AssertionError::class.java) {
            verify(first) { _, _, _ -> SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "All reject mutation") }
        }
    }

    @Test fun harnessRejectsMalformedFixtureAndCorruptionBeforeParserInvocation() {
        listOf("{}", """{"schemaVersion":1,"cases":[]}""", """{"schemaVersion":1,"schemaVersion":1,"cases":[]}""").forEach { bad ->
            assertThrows(AssertionError::class.java) { load(bad) }
        }
        val mutations: List<(JsonObject) -> Unit> = listOf(
            { it.addProperty("unused", 0) },
            { it.addProperty("schemaVersion", true) },
            { it.getAsJsonArray("cases").add(it.getAsJsonArray("cases")[0].deepCopy()) },
            { it.getAsJsonArray("cases")[0].asJsonObject.addProperty("oracleContract", "future") },
            { it.getAsJsonArray("cases")[0].asJsonObject.addProperty("unused", 0) },
            { it.getAsJsonArray("cases")[0].asJsonObject.getAsJsonObject("expectations").remove("leaseId") },
            { it.getAsJsonArray("cases")[0].asJsonObject.getAsJsonObject("expected").add("semanticLog", JsonArray()) },
        )
        mutations.forEach { mutate ->
            val root = outer(source()).asJsonObject; mutate(root)
            assertThrows(AssertionError::class.java) { load(root.toString()) }
        }
        val corrupt = load(source()).first().deepCopy()
        val input = corrupt.getAsJsonObject("input")
        input.addProperty("utf8Text", input["utf8Text"].asString + "\n")
        var called = false
        assertThrows(AssertionError::class.java) {
            verify(corrupt) { bytes, hash, expected -> called = true; SkinLaunchDescriptorCodec.parse(bytes, hash, expected) }
        }
        assertFalse(called)
    }

    @Test fun harnessObservesEveryPackObjectTextureFieldAndRecord() {
        for (caseId in listOf("clear-receipt-only-retained", "interlock-armed-distinct", "interlock-rollback-distinct", "interlock-selected-not-active", "interlock-repeated-receipt-retained")) {
        val source = load(source()).single { text(it["caseId"]) == caseId }
        val records = source.getAsJsonObject("expected").getAsJsonArray("semanticLog")
        records.forEachIndexed { i, row ->
            row.asJsonArray.forEachIndexed { j, value ->
                val damaged = source.deepCopy()
                val changed = when {
                    value.isJsonPrimitive && value.asJsonPrimitive.isNumber -> JsonPrimitive(value.asInt + 1)
                    value.isJsonPrimitive && value.asJsonPrimitive.isBoolean -> JsonPrimitive(!value.asBoolean)
                    else -> JsonPrimitive("damaged")
                }
                damaged.getAsJsonObject("expected").getAsJsonArray("semanticLog")[i].asJsonArray.set(j, changed)
                assertThrows(AssertionError::class.java) { verify(damaged) }
            }
            val omitted = source.deepCopy()
            omitted.getAsJsonObject("expected").getAsJsonArray("semanticLog").remove(i)
            assertThrows(AssertionError::class.java) { verify(omitted) }
        }
        val reordered = source.deepCopy()
        val log = reordered.getAsJsonObject("expected").getAsJsonArray("semanticLog")
        val first = log[6].deepCopy(); log.set(6, log[9].deepCopy()); log.set(9, first)
        assertThrows(AssertionError::class.java) { verify(reordered) }
        val wrong = source.deepCopy().apply { add("expected", outer("""{"outcome":"DOCUMENT_INVALID","violation":"FAKE"}""")) }
        assertThrows(AssertionError::class.java) { verify(wrong) }
        assertThrows(AssertionError::class.java) {
            verify(source) { _, _, _ -> SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "All reject mutation") }
        }
        val appended = source.deepCopy(); appended.getAsJsonObject("expected").getAsJsonArray("semanticLog").add(JsonArray().apply { add("unknown") })
        assertThrows(AssertionError::class.java) { verify(appended) }
        }
    }

    @Test fun interlockLogShapeFailsBeforeParser() {
        val source = load(source()).single { text(it["caseId"]) == "interlock-rollback-distinct" }
        for ((row, column, value) in listOf(
            Triple(5, 1, JsonPrimitive("bad-uuid")), Triple(5, 2, JsonPrimitive("UNKNOWN")), Triple(5, 4, JsonPrimitive("bad-hash")),
            Triple(6, 4, JsonPrimitive(91)), Triple(8, 4, JsonPrimitive("092")), Triple(10, 2, JsonPrimitive(0)),
            Triple(10, 1, JsonPrimitive(" x")), Triple(11, 1, JsonPrimitive("lower")), Triple(11, 2, JsonPrimitive("A".repeat(129))),
        )) {
            val bad = source.deepCopy(); bad.getAsJsonObject("expected").getAsJsonArray("semanticLog")[row].asJsonArray.set(column, value)
            var called = false
            assertThrows(AssertionError::class.java) { verify(bad) { bytes, hash, expected -> called = true; SkinLaunchDescriptorCodec.parse(bytes, hash, expected) } }
            assertFalse(called)
        }
    }

    // Gson streaming is used for OUTER fixture data only. Building our own tree
    // here rejects decoded duplicate keys rather than Gson's last-key-wins model.
    private fun outer(source: String): JsonElement = JsonReader(StringReader(source)).use { reader ->
        reader.strictness = Strictness.STRICT
        fun value(depth: Int): JsonElement {
            assertTrue("Fixture nesting bound", depth <= 32)
            return when (reader.peek()) {
                JsonToken.BEGIN_OBJECT -> {
                    val result = JsonObject(); reader.beginObject()
                    while (reader.hasNext()) {
                        val name = reader.nextName(); assertFalse("Duplicate decoded fixture key", result.has(name)); result.add(name, value(depth + 1))
                    }
                    reader.endObject(); result
                }
                JsonToken.BEGIN_ARRAY -> {
                    val result = JsonArray(); reader.beginArray()
                    while (reader.hasNext()) result.add(value(depth + 1))
                    reader.endArray(); result
                }
                JsonToken.STRING -> JsonPrimitive(reader.nextString())
                JsonToken.NUMBER -> {
                    val raw = reader.nextString(); assertTrue(Regex("0|-?[1-9][0-9]*").matches(raw)); JsonPrimitive(raw.toBigInteger())
                }
                JsonToken.BOOLEAN -> JsonPrimitive(reader.nextBoolean())
                JsonToken.NULL -> { reader.nextNull(); JsonNull.INSTANCE }
                else -> throw AssertionError("Invalid fixture token")
            }
        }
        val result = value(0); assertEquals(JsonToken.END_DOCUMENT, reader.peek()); result
    }

    private fun fields(value: JsonElement, names: String): JsonObject {
        assertTrue("Expected fixture object", value.isJsonObject)
        return value.asJsonObject.also { assertEquals(names.split(' ').toSet(), it.keySet()) }
    }
    private fun text(value: JsonElement): String {
        assertTrue("Expected fixture string", value.isJsonPrimitive && value.asJsonPrimitive.isString)
        return value.asString.also { assertTrue("Required nonempty string", it.isNotEmpty()) }
    }
    private fun number(value: JsonElement, expected: Int) {
        assertTrue(value.isJsonPrimitive && value.asJsonPrimitive.isNumber)
        assertEquals(expected.toString(), value.asString)
    }
    private fun sha(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { (it.toInt() and 255).toString(16).padStart(2, '0') }

    private fun load(source: String): List<JsonObject> {
        val root = fields(outer(source), "schemaVersion cases"); number(root["schemaVersion"], 1)
        assertTrue(root["cases"].isJsonArray)
        val ids = mutableSetOf<String>()
        val cases = root.getAsJsonArray("cases").map {
            assertTrue(it.isJsonObject); val case = it.asJsonObject; validate(case)
            assertTrue("Duplicate case ID", ids.add(text(case["caseId"]))); case
        }
        assertTrue("No handled cases", cases.isNotEmpty())
        return cases
    }

    private fun validate(case: JsonObject): ByteArray {
        fields(case, "caseId oracleContract input inputSha256 expectedSha256 expectations expected")
        text(case["caseId"]); assertTrue(text(case["oracleContract"]) in listOf(contract, "descriptor-clear-v1", "descriptor-interlock-v1"))
        assertTrue(case["input"].isJsonObject); val input = case.getAsJsonObject("input")
        assertEquals(1, input.size())
        val bytes = if (input.has("utf8Text")) {
            fields(input, "utf8Text"); val value = input["utf8Text"]
            assertTrue(value.isJsonPrimitive && value.asJsonPrimitive.isString)
            val buffer = StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(value.asString))
            ByteArray(buffer.remaining()).also { buffer.get(it) }
        } else {
            fields(input, "utf8Hex"); val value = input["utf8Hex"]
            assertTrue(value.isJsonPrimitive && value.asJsonPrimitive.isString)
            val hex = value.asString; assertTrue(Regex("(?:[0-9a-f]{2})*").matches(hex))
            hex.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        }
        assertTrue(digest.matches(text(case["inputSha256"]))); assertEquals(text(case["inputSha256"]), sha(bytes))
        assertTrue(digest.matches(text(case["expectedSha256"])))
        val e = fields(case["expectations"], "descriptorId profileId gameVersion catalogId catalogSha256 leaseId")
        e.entrySet().forEach { text(it.value) }
        assertTrue(uuid.matches(text(e["descriptorId"]))); assertTrue(uuid.matches(text(e["leaseId"]))); assertTrue(digest.matches(text(e["catalogSha256"])))
        assertTrue(case["expected"].isJsonObject); val expected = case.getAsJsonObject("expected")
        if (text(expected["outcome"]) == "OK") {
            fields(expected, "outcome semanticLog"); assertTrue(expected["semanticLog"].isJsonArray)
            val log = expected.getAsJsonArray("semanticLog"); assertTrue(log.size() >= 5)
            if (text(case["oracleContract"]) == contract) {
            assertEquals(5, log.size())
            val lengths = listOf(12, 4, 3, 2, 2); val tags = listOf("root", "activation", "visual", "interlock", "packs")
            for (i in 0..4) { assertTrue(log[i].isJsonArray); assertEquals(lengths[i], log[i].asJsonArray.size()); assertEquals(tags[i], text(log[i].asJsonArray[0])) }
            val root = log[0].asJsonArray; number(root[1], 1); for (i in 2..11) text(root[i])
            val activation = log[1].asJsonArray; assertTrue(text(activation[1]) in listOf("OFF", "ON", "ROTATE")); assertTrue(activation[2].isJsonNull); text(activation[3])
            assertEquals("activation", text(log[2].asJsonArray[1])); assertEquals("VANILLA", text(log[2].asJsonArray[2])); assertEquals("CLEAR", text(log[3].asJsonArray[1])); number(log[4].asJsonArray[1], 0)
            } else if (text(case["oracleContract"]) == "descriptor-interlock-v1") validateInterlockLog(log)
            else validateClearLog(log)
        } else {
            fields(expected, "outcome violation"); assertEquals("DOCUMENT_INVALID", text(expected["outcome"])); assertTrue(Regex("[A-Z][A-Z0-9_]*").matches(text(expected["violation"])))
        }
        return bytes
    }

    private fun validateInterlockLog(log: JsonArray) {
        var cursor = 0
        val packId = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
        fun row(tag: String, kinds: String): JsonArray {
            assertTrue("Missing record", cursor < log.size()); val value = log[cursor++]; assertTrue(value.isJsonArray)
            val record = value.asJsonArray; assertEquals(kinds.length + 1, record.size()); assertEquals(tag, text(record[0]))
            kinds.forEachIndexed { i, kind ->
                val field = record[i + 1]
                when (kind) {
                    's' -> text(field)
                    '?' -> if (!field.isJsonNull) assertTrue(packId.matches(text(field)))
                    'i' -> { assertTrue(field.isJsonPrimitive && field.asJsonPrimitive.isNumber); val n = field.asString.toIntOrNull(); assertNotNull(n); assertTrue(n!! >= 0); number(field, n) }
                    'b' -> assertTrue(field.isJsonPrimitive && field.asJsonPrimitive.isBoolean)
                    'd' -> { val decimal = text(field); assertTrue(Regex("0|[1-9][0-9]*").matches(decimal)); assertNotNull(decimal.toLongOrNull()) }
                }
            }
            return record
        }
        fun mode(value: JsonElement) { assertTrue(text(value) in listOf("OFF", "ON", "ROTATE")) }
        fun visual(owner: String) {
            assertTrue(cursor < log.size()); assertTrue(log[cursor].isJsonArray); val next = log[cursor].asJsonArray; assertTrue(next.size() >= 3)
            val kind = text(next[2]); assertTrue(kind in listOf("VANILLA", "PACK"))
            val record = row("visual", if (kind == "PACK") "ssssss" else "ss"); assertEquals(owner, text(record[1]))
            if (kind == "PACK") { assertTrue(packId.matches(text(record[3]))); for (i in 4..6) assertTrue(digest.matches(text(record[i]))) }
        }
        val root = row("root", "isdssssssss"); number(root[1], 1)
        val activation = row("activation", "s?d"); mode(activation[1]); visual("activation")
        val state = text(row("interlock", "s")[1]); assertTrue(state in listOf("CLEAR", "ARMED", "ROLLBACK_FAILED"))
        val count = row("packs", "i")[1].asInt; assertTrue(count in 0..64)
        if (state != "CLEAR") {
            val transaction = row("transaction", "ssss"); assertTrue(uuid.matches(text(transaction[1]))); assertTrue(uuid.matches(text(transaction[3]))); assertTrue(digest.matches(text(transaction[4])))
            assertTrue(text(transaction[2]) in listOf("STARTUP_APPLY", "MODE_ON", "MODE_OFF", "DEATH_ROTATION", "REBIND_APPLY"))
            for (owner in listOf("prior", "target")) { val snapshot = row("snapshot", "ss?d"); assertEquals(owner, text(snapshot[1])); mode(snapshot[2]); visual(owner) }
            val binding = text(row("binding", "sb")[1]); assertEquals(binding, java.text.Normalizer.normalize(binding, java.text.Normalizer.Form.NFKC))
            assertEquals(binding, binding.trim()); assertTrue(binding.codePointCount(0, binding.length) in 1..256)
            val bidi = setOf(0x61c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069)
            assertFalse(binding.codePoints().anyMatch { it < 32 || it in 127..159 || it in bidi || it in 0xd800..0xdfff })
            if (state == "ROLLBACK_FAILED") { val failures = row("failures", "ss"); for (i in 1..2) assertTrue(Regex("[A-Z][A-Z0-9_]{0,127}").matches(text(failures[i]))) }
        }
        for (i in 0 until count) {
            val pack = row("pack", "issssbb"); number(pack[1], i)
            for (owner in if (pack[7].asBoolean) listOf("current", "retained") else listOf("current")) {
                val obj = row("object", "isssssss"); number(obj[1], i); assertEquals(owner, text(obj[2]))
                val textures = row("textures", "isi"); number(textures[1], i); assertEquals(owner, text(textures[2])); val length = textures[3].asInt; assertTrue(length in 1..205)
                for (j in 0 until length) { val texture = row("texture", "isiisssd"); number(texture[1], i); assertEquals(owner, text(texture[2])); number(texture[3], j); assertTrue(texture[4].asInt in 0..204) }
            }
        }
        assertEquals(log.size(), cursor)
    }

    private fun validateClearLog(log: JsonArray) {
        // Verify compares every value, record count and width to typed projection.
        val sizes = mapOf("root" to 12, "activation" to 4, "interlock" to 2, "packs" to 2, "pack" to 8, "object" to 9, "textures" to 4, "texture" to 9)
        log.forEach { row ->
            assertTrue(row.isJsonArray); val record = row.asJsonArray; assertTrue(record.size() > 0)
            val tag = text(record[0])
            if (tag == "visual") assertTrue(record.size() in listOf(3, 7))
            else { assertTrue(sizes.containsKey(tag)); assertEquals(sizes[tag], record.size()) }
        }
    }

    private fun verify(case: JsonObject, parse: (ByteArray, String, DescriptorExpectations) -> SkinResult<SkinLaunchDescriptor> = SkinLaunchDescriptorCodec::parse) {
        val bytes = validate(case); val e = case.getAsJsonObject("expectations")
        val expected = DescriptorExpectations(UUID.fromString(text(e["descriptorId"])), text(e["profileId"]), text(e["gameVersion"]), text(e["catalogId"]), text(e["catalogSha256"]), UUID.fromString(text(e["leaseId"])))
        val result = parse(bytes, text(case["expectedSha256"]), expected)
        val outcome = case.getAsJsonObject("expected")
        if (text(outcome["outcome"]) == "DOCUMENT_INVALID") {
            assertTrue("Expected DOCUMENT_INVALID, got $result", result is SkinResult.Error)
            assertEquals(SkinImportCode.DOCUMENT_INVALID, (result as SkinResult.Error).code)
            return
        }
        assertTrue("Expected typed descriptor, got $result", result is SkinResult.Ok)
        val d = (result as SkinResult.Ok).value; val a = d.activation
        if (text(case["oracleContract"]) != "descriptor-interlock-v1") assertEquals(InterlockState.CLEAR, a.rotationInterlock.state)
        val actual: MutableList<List<Any?>> = mutableListOf(
            listOf("root", d.schemaVersion, d.descriptorId.toString(), d.sessionSequence.toString(), d.profileId, d.gameVersion, d.catalogId, d.catalogSha256, d.registryGenerationId, d.registryGenerationSha256, d.leaseId.toString(), d.leaseTokenSha256),
            listOf("activation", a.mode.name, a.selectedPackId, a.skinStamp.toString()),
            when (val v = a.active) {
                ActiveVisual.Vanilla -> listOf("visual", "activation", "VANILLA")
                is ActiveVisual.Pack -> listOf("visual", "activation", "PACK", v.id, v.treeSha256, v.contentSha256, v.importReceiptSha256)
            }, listOf("interlock", a.rotationInterlock.state.name), listOf("packs", d.packs.size),
        )
        val interlock = a.rotationInterlock
        if (interlock.state != InterlockState.CLEAR) {
            actual.add(listOf("transaction", interlock.transactionId, requireNotNull(interlock.operation).name, interlock.baseGenerationId, interlock.baseGenerationSha256))
            fun snapshot(s: dev.silksong.launcher.skins.registry.ActivationSnapshot, owner: String) {
                actual.add(listOf("snapshot", owner, s.mode.name, s.selectedPackId, s.skinStamp.toString()))
                actual.add(when (val visual = s.active) {
                    ActiveVisual.Vanilla -> listOf("visual", owner, "VANILLA")
                    is ActiveVisual.Pack -> listOf("visual", owner, "PACK", visual.id, visual.treeSha256, visual.contentSha256, visual.importReceiptSha256)
                })
            }
            snapshot(requireNotNull(interlock.prior), "prior"); snapshot(requireNotNull(interlock.target), "target")
            actual.add(listOf("binding", requireNotNull(interlock.bindingToken).value, requireNotNull(interlock.priorEstablishedOnBinding)))
            if (interlock.state == InterlockState.ROLLBACK_FAILED) actual.add(listOf("failures", interlock.originalFailure, interlock.rollbackFailure))
        }
        d.packs.forEachIndexed { i, p ->
            actual.add(listOf("pack", i, p.id, p.name, p.author, p.candidateKey, p.rotationEligible, p.retainedActiveObject != null))
            fun projectObject(o: dev.silksong.launcher.skins.session.DescriptorObjectEnvelope, label: String) {
                actual.add(listOf("object", i, label, o.objectRoot, o.receiptPath, o.treeSha256, o.contentSha256, o.manifestSha256, o.importReceiptSha256))
                actual.add(listOf("textures", i, label, o.textures.size))
                o.textures.forEachIndexed { j, t ->
                    actual.add(listOf("texture", i, label, j, t.ordinal, t.target, t.sourceRelativePath, t.sourceSha256, t.length.toString()))
                }
            }
            projectObject(p.currentObject, "current")
            p.retainedActiveObject?.let { projectObject(it, "retained") }
        }
        val log = outcome.getAsJsonArray("semanticLog")
        assertEquals(actual.size, log.size())
        actual.forEachIndexed { i, record -> assertEquals(record.size, log[i].asJsonArray.size()) }
        actual.forEachIndexed { i, record -> record.forEachIndexed { j, value ->
            val wanted = log[i].asJsonArray[j]
            when (value) { null -> assertTrue(wanted.isJsonNull); is Int -> number(wanted, value); is Boolean -> { assertTrue(wanted.isJsonPrimitive && wanted.asJsonPrimitive.isBoolean); assertEquals(value, wanted.asBoolean) }; else -> assertEquals(value as String, text(wanted)) }
        } }
    }
}
