package dev.silksong.launcher.skins.core

import com.google.gson.*
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import dev.silksong.launcher.skins.registry.SkinBindingToken
import java.io.File
import java.io.StringReader
import java.nio.ByteBuffer
import java.nio.CharBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import org.junit.Assert.*
import org.junit.Test

class SkinLifecycleGoldenTest {
    // These are HARNESS wire/event bounds, never production reducer constraints.
    private val stateFields = "armedHero pendingEpoch stableCount currentHero currentSkin lastConfirmedEpoch armedSkin armedOccurrence occurrenceHighWater"
    private val flags = "acceptingInput fullDamageMode canTakeDamage playable paused cutscene sceneTransition"
    private val diagnoses = "invalid-state same-binding rebound unbound stale-binding invalid-occurrence consumed-occurrence candidate-already-armed pending-epoch armed unarmed-after-death mismatched-occurrence death-confirmed stale-observation no-pending-epoch unstable-observation stabilizing stable-respawn epoch-overflow".split(' ')
    private class FixtureError(message: String = "Malformed lifecycle fixture") : RuntimeException(message)
    private class ScopeError : RuntimeException()
    private fun need(condition: Boolean) { if (!condition) throw FixtureError() }
    private val relative = "tools/skin-goldens/v1/lifecycle.json"
    private fun source(): String {
        val location = File(requireNotNull(requireNotNull(javaClass.protectionDomain).codeSource).location.toURI())
        val root = generateSequence(location) { it.parentFile }.firstOrNull { File(it, relative).isFile && File(it, "src/SilksongLauncher.Launcher/settings.gradle.kts").isFile } ?: error("Missing lifecycle fixture repository")
        return decode(File(root, relative).readBytes())
    }
    private fun decode(bytes: ByteArray): String = try {
        StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString()
    } catch (e: java.nio.charset.CharacterCodingException) { throw FixtureError(e.message ?: "UTF8") }
    private fun encode(text: String): ByteArray = try {
        val buffer = StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(CharBuffer.wrap(text))
        ByteArray(buffer.remaining()).also { buffer.get(it) }
    } catch (e: java.nio.charset.CharacterCodingException) { throw FixtureError(e.message ?: "UTF8") }
    private fun parse(text: String): JsonElement = try {
        JsonReader(StringReader(text)).use { reader ->
            reader.strictness = Strictness.STRICT
            fun value(depth: Int): JsonElement {
                need(depth <= 32)
                return when (reader.peek()) {
                    JsonToken.BEGIN_OBJECT -> {
                        val result = JsonObject(); reader.beginObject()
                        while (reader.hasNext()) { val key = reader.nextName(); encode(key); need(!result.has(key)); result.add(key, value(depth + 1)) }
                        reader.endObject(); result
                    }
                    JsonToken.BEGIN_ARRAY -> { val result = JsonArray(); reader.beginArray(); while (reader.hasNext()) result.add(value(depth + 1)); reader.endArray(); result }
                    JsonToken.STRING -> JsonPrimitive(reader.nextString().also { encode(it) })
                    JsonToken.NUMBER -> { val raw = reader.nextString(); need(Regex("0|-?[1-9][0-9]*").matches(raw)); JsonPrimitive(raw.toBigInteger()) }
                    JsonToken.BOOLEAN -> JsonPrimitive(reader.nextBoolean())
                    JsonToken.NULL -> { reader.nextNull(); JsonNull.INSTANCE }
                    else -> throw FixtureError()
                }
            }
            value(0).also { need(reader.peek() == JsonToken.END_DOCUMENT) }
        }
    } catch (e: java.io.IOException) { throw FixtureError(e.message ?: "JSON") }
    private fun fields(value: JsonElement?, names: String): JsonObject {
        need(value != null && value.isJsonObject); val obj = value!!.asJsonObject; need(obj.keySet() == names.split(' ').toSet()); return obj
    }
    private fun text(value: JsonElement?): String {
        need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isString); return value!!.asString.also { encode(it) }
    }
    private fun integer(value: JsonElement?): Int {
        need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isNumber)
        return value!!.asString.toIntOrNull() ?: throw FixtureError()
    }
    private fun uint(value: JsonElement?): ULong {
        val raw = text(value); need(Regex("0|[1-9][0-9]*").matches(raw)); return raw.toULongOrNull() ?: throw FixtureError()
    }
    private fun bool(value: JsonElement?): Boolean { need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isBoolean); return value!!.asBoolean }
    private fun optional(value: JsonElement): String? = if (value.isJsonNull) null else text(value)
    private fun epoch(value: JsonElement): DeathEpoch? = if (value.isJsonNull) null else DeathEpoch(uint(value))
    private fun state(value: JsonElement): LifecycleState {
        val s = fields(value, stateFields)
        // Representability only. Production observe owns all state invariants.
        return LifecycleState(armedHero = optional(s["armedHero"])?.let(::HeroBindingToken), pendingEpoch = epoch(s["pendingEpoch"]), stableCount = integer(s["stableCount"]),
            currentHero = optional(s["currentHero"])?.let(::HeroBindingToken), currentSkin = optional(s["currentSkin"])?.let(::SkinBindingToken), lastConfirmedEpoch = DeathEpoch(uint(s["lastConfirmedEpoch"])),
            armedSkin = optional(s["armedSkin"])?.let(::SkinBindingToken), armedOccurrence = if (s["armedOccurrence"].isJsonNull) null else DeathOccurrenceToken(uint(s["armedOccurrence"])), occurrenceHighWater = DeathOccurrenceToken(uint(s["occurrenceHighWater"])))
    }
    private fun signal(value: JsonElement): LifecycleSignal {
        need(value.isJsonObject); val v = value.asJsonObject; val type = text(v["type"])
        need(type in listOf("Rebind", "BeforeDeath", "AfterDeath", "Update"))
        fields(v, "type hero skin" + when (type) { "Update" -> " health $flags"; "Rebind" -> ""; else -> " occurrence" })
        val hero = HeroBindingToken(text(v["hero"])); val skin = SkinBindingToken(text(v["skin"]))
        return when (type) {
            "Rebind" -> LifecycleSignal.Rebind(hero, skin)
            "BeforeDeath" -> LifecycleSignal.BeforeDeath(hero, skin, DeathOccurrenceToken(uint(v["occurrence"])))
            "AfterDeath" -> LifecycleSignal.AfterDeath(hero, skin, DeathOccurrenceToken(uint(v["occurrence"])))
            else -> LifecycleSignal.Update(HeroObservation(hero, skin, bool(v["acceptingInput"]), bool(v["fullDamageMode"]), integer(v["health"]), bool(v["canTakeDamage"]), bool(v["playable"]), bool(v["paused"]), bool(v["cutscene"]), bool(v["sceneTransition"])))
        }
    }
    private fun sha(bytes: ByteArray) = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { (it.toInt() and 255).toString(16).padStart(2, '0') }
    private fun validate(case: JsonObject): JsonObject {
        fields(case, "caseId oracleContract input inputSha256 expected"); need(text(case["caseId"]).isNotEmpty())
        if (text(case["oracleContract"]) != "lifecycle-core-v1") throw ScopeError()
        need(case["input"].isJsonObject); val input = case.getAsJsonObject("input"); need(input.size() == 1)
        val bytes = if (input.has("utf8Text")) encode(text(input["utf8Text"])) else {
            fields(input, "utf8Hex"); val hex = text(input["utf8Hex"]); need(Regex("(?:[0-9a-f]{2})*").matches(hex)); hex.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        }
        val hash = text(case["inputSha256"]); need(Regex("[0-9a-f]{64}").matches(hash) && sha(bytes) == hash)
        if (bytes.size > 65536) throw ScopeError()
        val data = fields(parse(decode(bytes)), "initialState events"); state(data["initialState"])
        need(data["events"].isJsonArray); val events = data.getAsJsonArray("events"); need(events.size() > 0)
        if (events.size() > 32) throw ScopeError()
        events.forEach { signal(it) }
        need(case["expected"].isJsonArray); val expected = case.getAsJsonArray("expected"); need(expected.size() == events.size())
        expected.forEachIndexed { i, r ->
            val row = fields(r, "step state diagnosis confirmedEpoch stableToken"); need(integer(row["step"]) == i); state(row["state"]); need(text(row["diagnosis"]) in diagnoses)
            if (!row["confirmedEpoch"].isJsonNull) uint(row["confirmedEpoch"])
            if (!row["stableToken"].isJsonNull) { val token = fields(row["stableToken"], "deathEpoch hero skin"); uint(token["deathEpoch"]); text(token["hero"]); text(token["skin"]) }
        }
        return data
    }
    private fun load(source: String): List<JsonObject> {
        val root = fields(parse(source), "schemaVersion cases"); need(integer(root["schemaVersion"]) == 1); need(root["cases"].isJsonArray)
        val ids = mutableSetOf<String>()
        return root.getAsJsonArray("cases").map { need(it.isJsonObject); val c = it.asJsonObject; validate(c); need(ids.add(text(c["caseId"]))); c }.also { need(it.isNotEmpty()) }
    }
    private fun obj(vararg values: Pair<String, Any?>): JsonObject = JsonObject().apply {
        values.forEach { (key, value) -> add(key, when (value) { null -> JsonNull.INSTANCE; is JsonElement -> value; is Int -> JsonPrimitive(value); is String -> JsonPrimitive(value); else -> error("Unhandled projection type") }) }
    }
    private fun project(step: Int, d: LifecycleDecision): JsonObject {
        val s = d.state
        return obj("step" to step, "diagnosis" to d.diagnosis, "confirmedEpoch" to d.confirmedEpoch?.value?.toString(),
            "stableToken" to d.stableToken?.let { obj("deathEpoch" to it.deathEpoch.value.toString(), "hero" to it.hero.value, "skin" to it.skin.value) },
            "state" to obj("armedHero" to s.armedHero?.value, "pendingEpoch" to s.pendingEpoch?.value?.toString(), "stableCount" to s.stableCount, "currentHero" to s.currentHero?.value,
                "currentSkin" to s.currentSkin?.value, "lastConfirmedEpoch" to s.lastConfirmedEpoch.value.toString(), "armedSkin" to s.armedSkin?.value, "armedOccurrence" to s.armedOccurrence?.value?.toString(), "occurrenceHighWater" to s.occurrenceHighWater.value.toString()))
    }
    private fun verify(case: JsonObject, observe: (LifecycleState, LifecycleSignal) -> LifecycleDecision = SkinLifecycleCore()::observe) {
        val data = validate(case); var state = state(data["initialState"]); val actual = JsonArray()
        data.getAsJsonArray("events").forEach { val decision = observe(state, signal(it)); actual.add(project(actual.size(), decision)); state = decision.state }
        assertEquals(text(case["caseId"]) + " full ordered lifecycle log", case["expected"], actual)
    }
    private fun reinput(case: JsonObject, data: JsonElement) { val raw = encode(data.toString()); case.add("input", obj("utf8Text" to decode(raw))); case.addProperty("inputSha256", sha(raw)) }
    @Test fun corpusHashesFullLogsPositiveAndNoop() {
        val cases = load(source()); assertEquals(36, cases.size); assertEquals(152, cases.sumOf { it.getAsJsonArray("expected").size() }); cases.forEach { verify(it) }
        assertTrue(cases.any { c -> c.getAsJsonArray("expected").any { !it.asJsonObject["stableToken"].isJsonNull } })
        assertTrue(cases.any { c -> c.getAsJsonArray("expected").any { text(it.asJsonObject["diagnosis"]) == "same-binding" } })
    }
    @Test fun everyFieldAndRecordOrderIsObserved() {
        val case = load(source()).first()
        fun leaves(value: JsonElement, path: List<String> = emptyList()): List<List<String>> = if (value.isJsonObject) value.asJsonObject.entrySet().flatMap { leaves(it.value, path + it.key) } else listOf(path)
        case.getAsJsonArray("expected").forEachIndexed { i, row ->
            leaves(row).forEach { path ->
                val bad = case.deepCopy(); var part = bad.getAsJsonArray("expected")[i].asJsonObject
                path.dropLast(1).forEach { part = part.getAsJsonObject(it) }
                val key = path.last(); val old = part[key]
                part.add(key, when (key) {
                    "stableToken" -> obj("deathEpoch" to "7", "hero" to "damaged", "skin" to "damaged")
                    "diagnosis" -> JsonPrimitive(if (text(old) == "armed") "same-binding" else "armed")
                    "pendingEpoch", "lastConfirmedEpoch", "armedOccurrence", "occurrenceHighWater", "confirmedEpoch", "deathEpoch" -> JsonPrimitive("7")
                    else -> if (old.isJsonPrimitive && old.asJsonPrimitive.isNumber) JsonPrimitive(integer(old) + 1) else JsonPrimitive("damaged")
                })
                if (key == "step") assertThrows(FixtureError::class.java) { verify(bad) }
                else assertThrows(AssertionError::class.java) { verify(bad) }
            }
            for (change in listOf("omit", "extra", "reorder")) {
                val bad = case.deepCopy(); val log = bad.getAsJsonArray("expected")
                when (change) { "omit" -> log.remove(i); "extra" -> log[i].asJsonObject.addProperty("unknown", 1); else -> { val j = (i + 1) % log.size(); val first = log[i].deepCopy(); log.set(i, log[j].deepCopy()); log.set(j, first) } }
                assertThrows(FixtureError::class.java) { verify(bad) }
            }
        }
        val reordered = case.deepCopy(); val records = reordered.getAsJsonArray("expected")
        val first = records[3].deepCopy(); records.set(3, records[5].deepCopy()); records.set(5, first)
        records.forEachIndexed { i, record -> record.asJsonObject.addProperty("step", i) }
        assertThrows(AssertionError::class.java) { verify(reordered) }
    }
    @Test fun noopAndInvalidSubstitutionsKillPositive() {
        for (diagnosis in listOf("same-binding", "invalid-state")) assertThrows(AssertionError::class.java) { verify(load(source()).first()) { state, _ -> LifecycleDecision(state, null, null, diagnosis) } }
    }
    @Test fun invalidFixturesFailBeforeReducer() {
        for (text in listOf("{}", "", """{"schemaVersion":1,"cases":[]}""", """{"schemaVersion":1,"schemaVersion":1,"cases":[]}""")) assertThrows(FixtureError::class.java) { load(text) }
        val root = parse(source()).asJsonObject; root.getAsJsonArray("cases").add(root.getAsJsonArray("cases")[0].deepCopy()); assertThrows(FixtureError::class.java) { load(root.toString()) }
        val case = load(source()).first()
        val mutations = mutableListOf<(JsonObject) -> Unit>({ it.addProperty("unknown", 1) }, { it.addProperty("inputSha256", "0".repeat(64)) }, { it.add("expected", JsonArray()) }, { it.getAsJsonObject("input").addProperty("utf8Hex", "00") })
        for ((field, value) in listOf("stableCount" to JsonPrimitive(true), "stableCount" to JsonPrimitive(2147483648L), "lastConfirmedEpoch" to JsonPrimitive("01"), "lastConfirmedEpoch" to JsonPrimitive("18446744073709551616"), "lastConfirmedEpoch" to JsonNull.INSTANCE, "currentHero" to JsonPrimitive(3), "unknown" to JsonPrimitive(0))) {
            mutations.add { val d = parse(text(it.getAsJsonObject("input")["utf8Text"])).asJsonObject; d.getAsJsonObject("initialState").add(field, value); reinput(it, d) }
        }
        for ((field, value) in listOf("type" to JsonPrimitive("Unknown"), "hero" to JsonNull.INSTANCE, "health" to JsonPrimitive(true), "health" to JsonPrimitive(1.5), "health" to JsonPrimitive(-2147483649L), "paused" to JsonPrimitive(1), "unknown" to JsonPrimitive(0))) {
            mutations.add { val d = parse(text(it.getAsJsonObject("input")["utf8Text"])).asJsonObject; d.getAsJsonArray("events")[0].asJsonObject.add(field, value); reinput(it, d) }
        }
        mutations.forEach { mutation -> val bad = case.deepCopy(); mutation(bad); var called = false; assertThrows(FixtureError::class.java) { verify(bad) { s, e -> called = true; SkinLifecycleCore().observe(s, e) } }; assertFalse(called) }
    }
    @Test fun unsupportedScopeIsNotReducerDiagnosis() {
        for (mode in listOf("contract", "bytes", "events")) {
            val case = load(source()).first()
            when (mode) {
                "contract" -> case.addProperty("oracleContract", "future")
                "bytes" -> { val raw = encode(" ".repeat(65537)); case.add("input", obj("utf8Text" to decode(raw))); case.addProperty("inputSha256", sha(raw)) }
                else -> { val d = validate(case); val events = d.getAsJsonArray("events"); while (events.size() < 33) events.add(events[0].deepCopy()); reinput(case, d) }
            }
            var called = false; assertThrows(ScopeError::class.java) { verify(case) { s, e -> called = true; SkinLifecycleCore().observe(s, e) } }; assertFalse(called)
        }
    }
    @Test fun replayIsDeterministicAndImmutable() {
        load(source()).forEach { case ->
            val before = case.toString(); verify(case); verify(case); assertEquals(before, case.toString())
            val d = validate(case); val state = state(d["initialState"]); val signal = signal(d.getAsJsonArray("events")[0]); val copy = state.copy()
            assertEquals(SkinLifecycleCore().observe(state, signal), SkinLifecycleCore().observe(state, signal)); assertEquals(copy, state)
        }
    }
}
