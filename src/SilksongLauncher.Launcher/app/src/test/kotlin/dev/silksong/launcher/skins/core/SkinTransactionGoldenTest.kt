package dev.silksong.launcher.skins.core

import com.google.gson.*
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import dev.silksong.launcher.skins.registry.*
import java.io.File
import java.io.StringReader
import java.nio.ByteBuffer
import java.nio.CharBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import org.junit.Assert.*
import org.junit.Test

class SkinTransactionGoldenTest {
    // These are HARNESS wire/event bounds, never production reducer constraints.
    private class FixtureFormatError(message: String = "Malformed transaction fixture") : RuntimeException(message)
    private class OracleOutOfScope : RuntimeException()
    private fun need(condition: Boolean) { if (!condition) throw FixtureFormatError() }
    private val relative = "tools/skin-goldens/v1/transactions.json"
    private fun source(): String = readSource(File(requireNotNull(requireNotNull(javaClass.protectionDomain).codeSource).location.toURI()))
    // Private harness seam shared with source; the default reader retains precise NIO failures.
    private fun readSource(location: File, fixtureRelative: String = relative, read: (File) -> ByteArray = { java.nio.file.Files.readAllBytes(it.toPath()) }): String {
        val root = generateSequence(location) { it.parentFile }.firstOrNull { File(it, "src/SilksongLauncher.Launcher/settings.gradle.kts").isFile }
            ?: throw FixtureFormatError("Missing transaction fixture repository")
        // NIO distinguishes known absence from access denial; do not blanket-catch IOException.
        val bytes = try { read(File(root, fixtureRelative)) }
        catch (e: java.nio.file.NoSuchFileException) { throw FixtureFormatError(e.message ?: "Missing transaction corpus") }
        return decode(bytes)
    }
    private fun decode(bytes: ByteArray): String = try {
        StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString()
    } catch (e: java.nio.charset.CharacterCodingException) { throw FixtureFormatError(e.message ?: "UTF8") }
    private fun encode(text: String): ByteArray = try {
        val buffer = StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(CharBuffer.wrap(text))
        ByteArray(buffer.remaining()).also { buffer.get(it) }
    } catch (e: java.nio.charset.CharacterCodingException) { throw FixtureFormatError(e.message ?: "UTF8") }
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
                    else -> throw FixtureFormatError()
                }
            }
            value(0).also { need(reader.peek() == JsonToken.END_DOCUMENT) }
        }
    } catch (e: java.io.IOException) { throw FixtureFormatError(e.message ?: "JSON") }
    private fun fields(value: JsonElement?, names: String): JsonObject {
        need(value != null && value.isJsonObject); val obj = value!!.asJsonObject; need(obj.keySet() == names.split(' ').toSet()); return obj
    }
    private fun text(value: JsonElement?): String {
        need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isString); return value!!.asString.also { encode(it) }
    }
    private fun integer(value: JsonElement?): Int {
        need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isNumber)
        return value!!.asString.toIntOrNull() ?: throw FixtureFormatError()
    }
    private fun long(value: JsonElement?): Long {
        val raw = text(value); need(Regex("0|-?[1-9][0-9]*").matches(raw)); return raw.toLongOrNull() ?: throw FixtureFormatError()
    }
    private fun <T> nullable(value: JsonElement, read: (JsonElement) -> T): T? = if (value.isJsonNull) null else read(value)
    private fun bool(value: JsonElement?): Boolean { need(value != null && value.isJsonPrimitive && value.asJsonPrimitive.isBoolean); return value!!.asBoolean }
    private fun optional(value: JsonElement): String? = if (value.isJsonNull) null else text(value)
    private fun readSkinMode(n: JsonElement): SkinMode { val v = text(n); need(v in "OFF ON ROTATE".split(' ')); return SkinMode.valueOf(v) }
    private fun readSkinOperationKind(n: JsonElement): SkinOperationKind { val v = text(n); need(v in "STARTUP_APPLY MODE_ON MODE_OFF DEATH_ROTATION REBIND_APPLY".split(' ')); return SkinOperationKind.valueOf(v) }
    private fun readInterlockState(n: JsonElement): InterlockState { val v = text(n); need(v in "CLEAR ARMED ROLLBACK_FAILED".split(' ')); return InterlockState.valueOf(v) }
    private fun readTransactionPhase(n: JsonElement): TransactionPhase { val v = text(n); need(v in "IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED".split(' ')); return TransactionPhase.valueOf(v) }
    private fun readSkinBindingToken(n: JsonElement): SkinBindingToken { val v = fields(n, "value"); return SkinBindingToken(text(v["value"])) }
    private fun project(v: SkinBindingToken): JsonObject = obj("value" to v.value)
    private fun readActivationSnapshot(n: JsonElement): ActivationSnapshot { val v = fields(n, "mode selectedPackId active skinStamp"); return ActivationSnapshot(readSkinMode(v["mode"]), nullable(v["selectedPackId"]) { text(it) }, readActiveVisual(v["active"]), long(v["skinStamp"])) }
    private fun project(v: ActivationSnapshot): JsonObject = obj("mode" to v.mode.name, "selectedPackId" to v.selectedPackId?.let { it }, "active" to project(v.active), "skinStamp" to v.skinStamp.toString())
    private fun readTransactionEnvelope(n: JsonElement): TransactionEnvelope { val v = fields(n, "transactionId operation baseGenerationId baseGenerationSha256 prior target binding priorEstablishedOnBinding"); return TransactionEnvelope(text(v["transactionId"]), readSkinOperationKind(v["operation"]), text(v["baseGenerationId"]), text(v["baseGenerationSha256"]), readActivationSnapshot(v["prior"]), readActivationSnapshot(v["target"]), readSkinBindingToken(v["binding"]), bool(v["priorEstablishedOnBinding"])) }
    private fun project(v: TransactionEnvelope): JsonObject = obj("transactionId" to v.transactionId, "operation" to v.operation.name, "baseGenerationId" to v.baseGenerationId, "baseGenerationSha256" to v.baseGenerationSha256, "prior" to project(v.prior), "target" to project(v.target), "binding" to project(v.binding), "priorEstablishedOnBinding" to v.priorEstablishedOnBinding)
    private fun readTransactionCorrelation(n: JsonElement): TransactionCorrelation { val v = fields(n, "transactionId binding"); return TransactionCorrelation(text(v["transactionId"]), readSkinBindingToken(v["binding"])) }
    private fun project(v: TransactionCorrelation): JsonObject = obj("transactionId" to v.transactionId, "binding" to project(v.binding))
    private fun readRegistryCommitReceipt(n: JsonElement): RegistryCommitReceipt { val v = fields(n, "expectedGenerationId expectedGenerationSha256 newGenerationId newGenerationSha256"); return RegistryCommitReceipt(text(v["expectedGenerationId"]), text(v["expectedGenerationSha256"]), text(v["newGenerationId"]), text(v["newGenerationSha256"])) }
    private fun project(v: RegistryCommitReceipt): JsonObject = obj("expectedGenerationId" to v.expectedGenerationId, "expectedGenerationSha256" to v.expectedGenerationSha256, "newGenerationId" to v.newGenerationId, "newGenerationSha256" to v.newGenerationSha256)
    private fun readRotationInterlock(n: JsonElement): RotationInterlock { val v = fields(n, "state transactionId operation baseGenerationId baseGenerationSha256 prior target bindingToken priorEstablishedOnBinding originalFailure rollbackFailure"); return RotationInterlock(readInterlockState(v["state"]), nullable(v["transactionId"]) { text(it) }, nullable(v["operation"]) { readSkinOperationKind(it) }, nullable(v["baseGenerationId"]) { text(it) }, nullable(v["baseGenerationSha256"]) { text(it) }, nullable(v["prior"]) { readActivationSnapshot(it) }, nullable(v["target"]) { readActivationSnapshot(it) }, nullable(v["bindingToken"]) { readSkinBindingToken(it) }, nullable(v["priorEstablishedOnBinding"]) { bool(it) }, nullable(v["originalFailure"]) { text(it) }, nullable(v["rollbackFailure"]) { text(it) }) }
    private fun project(v: RotationInterlock): JsonObject = obj("state" to v.state.name, "transactionId" to v.transactionId?.let { it }, "operation" to v.operation?.let { it.name }, "baseGenerationId" to v.baseGenerationId?.let { it }, "baseGenerationSha256" to v.baseGenerationSha256?.let { it }, "prior" to v.prior?.let { project(it) }, "target" to v.target?.let { project(it) }, "bindingToken" to v.bindingToken?.let { project(it) }, "priorEstablishedOnBinding" to v.priorEstablishedOnBinding?.let { it }, "originalFailure" to v.originalFailure?.let { it }, "rollbackFailure" to v.rollbackFailure?.let { it })
    private fun readVerifiedRegistryHead(n: JsonElement): VerifiedRegistryHead { val v = fields(n, "generationId generationSha256 activation interlock"); return VerifiedRegistryHead(text(v["generationId"]), text(v["generationSha256"]), readActivationSnapshot(v["activation"]), readRotationInterlock(v["interlock"])) }
    private fun project(v: VerifiedRegistryHead): JsonObject = obj("generationId" to v.generationId, "generationSha256" to v.generationSha256, "activation" to project(v.activation), "interlock" to project(v.interlock))
    private fun readTransactionState(n: JsonElement): TransactionState { val v = fields(n, "phase interlock binding armCommitReceipt pendingClosure envelope activation originalFailure rollbackFailure completionReceipt failureReceipt"); return TransactionState(readTransactionPhase(v["phase"]), readRotationInterlock(v["interlock"]), nullable(v["binding"]) { readSkinBindingToken(it) }, nullable(v["armCommitReceipt"]) { readRegistryCommitReceipt(it) }, nullable(v["pendingClosure"]) { readActivationSnapshot(it) }, nullable(v["envelope"]) { readTransactionEnvelope(it) }, nullable(v["activation"]) { readActivationSnapshot(it) }, nullable(v["originalFailure"]) { text(it) }, nullable(v["rollbackFailure"]) { text(it) }, nullable(v["completionReceipt"]) { readRegistryCommitReceipt(it) }, nullable(v["failureReceipt"]) { readRegistryCommitReceipt(it) }) }
    private fun project(v: TransactionState): JsonObject = obj("phase" to v.phase.name, "interlock" to project(v.interlock), "binding" to v.binding?.let { project(it) }, "armCommitReceipt" to v.armCommitReceipt?.let { project(it) }, "pendingClosure" to v.pendingClosure?.let { project(it) }, "envelope" to v.envelope?.let { project(it) }, "activation" to v.activation?.let { project(it) }, "originalFailure" to v.originalFailure?.let { it }, "rollbackFailure" to v.rollbackFailure?.let { it }, "completionReceipt" to v.completionReceipt?.let { project(it) }, "failureReceipt" to v.failureReceipt?.let { project(it) })
    private fun readActiveVisual(n: JsonElement): ActiveVisual { need(n.isJsonObject); val v = n.asJsonObject; return when(text(v["type"])) {
        "Vanilla" -> { fields(v, "type"); ActiveVisual.Vanilla }
        "Pack" -> { fields(v, "type id treeSha256 contentSha256 importReceiptSha256"); ActiveVisual.Pack(text(v["id"]), text(v["treeSha256"]), text(v["contentSha256"]), text(v["importReceiptSha256"])) }
        else -> throw FixtureFormatError() } }
    private fun project(value: ActiveVisual): JsonObject = when(value) {
        is ActiveVisual.Vanilla -> obj("type" to "Vanilla")
        is ActiveVisual.Pack -> obj("type" to "Pack", "id" to value.id, "treeSha256" to value.treeSha256, "contentSha256" to value.contentSha256, "importReceiptSha256" to value.importReceiptSha256)
    }
    private fun readTransactionEvent(n: JsonElement): TransactionEvent { need(n.isJsonObject); val v = n.asJsonObject; return when(text(v["type"])) {
        "Begin" -> { fields(v, "type envelope"); TransactionEvent.Begin(readTransactionEnvelope(v["envelope"])) }
        "Prepared" -> { fields(v, "type correlation"); TransactionEvent.Prepared(readTransactionCorrelation(v["correlation"])) }
        "ArmCommitted" -> { fields(v, "type correlation commitReceipt verifiedHead"); TransactionEvent.ArmCommitted(readTransactionCorrelation(v["correlation"]), readRegistryCommitReceipt(v["commitReceipt"]), readVerifiedRegistryHead(v["verifiedHead"])) }
        "ApplyVerified" -> { fields(v, "type correlation"); TransactionEvent.ApplyVerified(readTransactionCorrelation(v["correlation"])) }
        "ApplyFailed" -> { fields(v, "type correlation code"); TransactionEvent.ApplyFailed(readTransactionCorrelation(v["correlation"]), text(v["code"])) }
        "RollbackVerified" -> { fields(v, "type correlation freshBinding"); TransactionEvent.RollbackVerified(readTransactionCorrelation(v["correlation"]), bool(v["freshBinding"])) }
        "RollbackFailed" -> { fields(v, "type correlation code persistedFailureReceipt verifiedHead"); TransactionEvent.RollbackFailed(readTransactionCorrelation(v["correlation"]), text(v["code"]), nullable(v["persistedFailureReceipt"]) { readRegistryCommitReceipt(it) }, nullable(v["verifiedHead"]) { readVerifiedRegistryHead(it) }) }
        "CompletionCommitted" -> { fields(v, "type correlation commitReceipt verifiedHead"); TransactionEvent.CompletionCommitted(readTransactionCorrelation(v["correlation"]), readRegistryCommitReceipt(v["commitReceipt"]), readVerifiedRegistryHead(v["verifiedHead"])) }
        "CompletionRejected" -> { fields(v, "type correlation code"); TransactionEvent.CompletionRejected(readTransactionCorrelation(v["correlation"]), text(v["code"])) }
        "CompletionIndeterminate" -> { fields(v, "type correlation"); TransactionEvent.CompletionIndeterminate(readTransactionCorrelation(v["correlation"])) }
        else -> throw FixtureFormatError() } }
    private fun project(value: TransactionEvent): JsonObject = when(value) {
        is TransactionEvent.Begin -> obj("type" to "Begin", "envelope" to project(value.envelope))
        is TransactionEvent.Prepared -> obj("type" to "Prepared", "correlation" to project(value.correlation))
        is TransactionEvent.ArmCommitted -> obj("type" to "ArmCommitted", "correlation" to project(value.correlation), "commitReceipt" to project(value.commitReceipt), "verifiedHead" to project(value.verifiedHead))
        is TransactionEvent.ApplyVerified -> obj("type" to "ApplyVerified", "correlation" to project(value.correlation))
        is TransactionEvent.ApplyFailed -> obj("type" to "ApplyFailed", "correlation" to project(value.correlation), "code" to value.code)
        is TransactionEvent.RollbackVerified -> obj("type" to "RollbackVerified", "correlation" to project(value.correlation), "freshBinding" to value.freshBinding)
        is TransactionEvent.RollbackFailed -> obj("type" to "RollbackFailed", "correlation" to project(value.correlation), "code" to value.code, "persistedFailureReceipt" to value.persistedFailureReceipt?.let { project(it) }, "verifiedHead" to value.verifiedHead?.let { project(it) })
        is TransactionEvent.CompletionCommitted -> obj("type" to "CompletionCommitted", "correlation" to project(value.correlation), "commitReceipt" to project(value.commitReceipt), "verifiedHead" to project(value.verifiedHead))
        is TransactionEvent.CompletionRejected -> obj("type" to "CompletionRejected", "correlation" to project(value.correlation), "code" to value.code)
        is TransactionEvent.CompletionIndeterminate -> obj("type" to "CompletionIndeterminate", "correlation" to project(value.correlation))
    }
    private fun readSkinCommand(n: JsonElement): SkinCommand { need(n.isJsonObject); val v = n.asJsonObject; return when(text(v["type"])) {
        "Prepare" -> { fields(v, "type envelope desired"); SkinCommand.Prepare(readTransactionEnvelope(v["envelope"]), readActiveVisual(v["desired"])) }
        "Arm" -> { fields(v, "type envelope"); SkinCommand.Arm(readTransactionEnvelope(v["envelope"])) }
        "Apply" -> { fields(v, "type correlation"); SkinCommand.Apply(readTransactionCorrelation(v["correlation"])) }
        "Rollback" -> { fields(v, "type correlation"); SkinCommand.Rollback(readTransactionCorrelation(v["correlation"])) }
        "Commit" -> { fields(v, "type correlation expectedGenerationId expectedGenerationSha256 closure"); SkinCommand.Commit(readTransactionCorrelation(v["correlation"]), text(v["expectedGenerationId"]), text(v["expectedGenerationSha256"]), readActivationSnapshot(v["closure"])) }
        else -> throw FixtureFormatError() } }
    private fun project(value: SkinCommand): JsonObject = when(value) {
        is SkinCommand.Prepare -> obj("type" to "Prepare", "envelope" to project(value.envelope), "desired" to project(value.desired))
        is SkinCommand.Arm -> obj("type" to "Arm", "envelope" to project(value.envelope))
        is SkinCommand.Apply -> obj("type" to "Apply", "correlation" to project(value.correlation))
        is SkinCommand.Rollback -> obj("type" to "Rollback", "correlation" to project(value.correlation))
        is SkinCommand.Commit -> obj("type" to "Commit", "correlation" to project(value.correlation), "expectedGenerationId" to value.expectedGenerationId, "expectedGenerationSha256" to value.expectedGenerationSha256, "closure" to project(value.closure))
    }
    private fun obj(vararg values: Pair<String, Any?>): JsonObject = JsonObject().apply {
        values.forEach { (key, value) -> add(key, when(value) { null -> JsonNull.INSTANCE; is JsonElement -> value; is String -> JsonPrimitive(value); is Int -> JsonPrimitive(value); is Boolean -> JsonPrimitive(value); else -> error("Unhandled projection type") }) }
    }
    private fun sha(bytes: ByteArray) = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { (it.toInt() and 255).toString(16).padStart(2, '0') }
    private fun validate(case: JsonObject): JsonObject {
        fields(case, "caseId oracleContract input inputSha256 expected"); need(text(case["caseId"]).isNotEmpty()); text(case["oracleContract"])
        need(case["input"].isJsonObject); val input = case.getAsJsonObject("input"); need(input.size() == 1)
        val bytes = if (input.has("utf8Text")) encode(text(input["utf8Text"])) else { fields(input, "utf8Hex"); val hex = text(input["utf8Hex"]); need(Regex("(?:[0-9a-f]{2})*").matches(hex)); hex.chunked(2).map { it.toInt(16).toByte() }.toByteArray() }
        val hash = text(case["inputSha256"]); need(Regex("[0-9a-f]{64}").matches(hash) && sha(bytes) == hash)
        val data = fields(parse(decode(bytes)), "initialState events"); val state = readTransactionState(data["initialState"])
        need(data["events"].isJsonArray); val events = data.getAsJsonArray("events"); need(events.size() > 0); events.forEach { readTransactionEvent(it) }
        need(case["expected"].isJsonArray); val rows = case.getAsJsonArray("expected"); need(rows.size() == events.size())
        rows.forEachIndexed { i, r ->
            val row = fields(r, "step inputStateValid stateValid state diagnosis commands"); need(integer(row["step"]) == i)
            bool(row["inputStateValid"]); bool(row["stateValid"]); readTransactionState(row["state"]); text(row["diagnosis"])
            need(row["commands"].isJsonArray); row.getAsJsonArray("commands").forEach { readSkinCommand(it) }
        }
        // Recursively complete parsing before any scope or semantic validity classification.
        val contract = text(case["oracleContract"])
        var phases = "IDLE PREPARING PREPARED ARMED APPLIED COMMITTED"
        var supportedEvents = "Begin Prepared ArmCommitted ApplyVerified CompletionCommitted"
        if (contract == "transaction-failure-blocked-v1") { phases += " ROLLBACK_PENDING ROLLED_BACK BLOCKED"; supportedEvents += " ApplyFailed RollbackVerified RollbackFailed CompletionRejected CompletionIndeterminate" }
        else if (contract == "transaction-rollback-success-v1") { phases += " ROLLBACK_PENDING ROLLED_BACK"; supportedEvents += " ApplyFailed RollbackVerified" }
        else if (contract != "transaction-forward-v1") throw OracleOutOfScope()
        if (bytes.size > 65536 || events.size() > 32 || state.phase.name !in phases.split(' ') || events.any { text(it.asJsonObject["type"]) !in supportedEvents.split(' ') }) throw OracleOutOfScope()
        return data
    }
    private fun load(source: String): List<JsonObject> {
        val root = fields(parse(source), "schemaVersion cases"); need(integer(root["schemaVersion"]) == 1); need(root["cases"].isJsonArray); val ids = mutableSetOf<String>()
        return root.getAsJsonArray("cases").map { need(it.isJsonObject); val c = it.asJsonObject; validate(c); need(ids.add(text(c["caseId"]))); c }.also { need(it.isNotEmpty()) }
    }
    private fun verify(case: JsonObject, decide: (TransactionState, TransactionEvent) -> TransactionDecision = SkinTransactionCore()::decide) {
        val data = validate(case); var state = readTransactionState(data["initialState"]); val core = SkinTransactionCore(); val actual = JsonArray()
        data.getAsJsonArray("events").forEach { event ->
            val before = core.isStateValid(state); val d = decide(state, readTransactionEvent(event)); val commands = JsonArray(); d.commands.forEach { commands.add(project(it)) }
            actual.add(obj("step" to actual.size(), "inputStateValid" to before, "stateValid" to core.isStateValid(d.state), "state" to project(d.state), "diagnosis" to d.diagnosis, "commands" to commands)); state = d.state
        }
        assertEquals(text(case["caseId"]) + " full ordered transaction log", case["expected"], actual)
    }
    private fun reinput(case: JsonObject, data: JsonElement) { val bytes = encode(data.toString()); case.add("input", obj("utf8Text" to decode(bytes))); case.addProperty("inputSha256", sha(bytes)) }
    @Test fun corpusFullForwardLogs() { val cases = load(source()).take(262).filter { text(it["oracleContract"]) == "transaction-forward-v1" }; assertEquals(82, cases.size); assertEquals(164, cases.sumOf { it.getAsJsonArray("expected").size() }); cases.forEach { verify(it) } }
    @Test fun noopAndInvalidSubstitutionsKillPositive() {
        val cases = load(source())
        for (case in listOf(cases.first(), cases.first { text(it["caseId"]) == "rollback-history-unestablished-pack" }, cases.first { text(it["caseId"]) == "failure-history-reject-rollback-persisted" }, cases.first { text(it["caseId"]) == "failure-history-rolled-rejected" }, cases.first { text(it["caseId"]) == "failure-history-applied-indeterminate" }))
            for (diagnosis in listOf("stale-phase", "invalid-state")) assertThrows(AssertionError::class.java) { verify(case) { s, _ -> TransactionDecision(s, diagnosis) } }
    }
    @Test fun replayIsDeterministicAndInputImmutable() { load(source()).forEach { val before = it.toString(); verify(it); verify(it); assertEquals(before, it.toString()) } }
    private fun leaves(n: JsonElement, path: List<String> = emptyList()): List<Pair<List<String>, JsonElement>> = when {
        n.isJsonObject -> n.asJsonObject.entrySet().flatMap { leaves(it.value, path + it.key) }
        n.isJsonArray -> n.asJsonArray.flatMapIndexed { i, v -> leaves(v, path + i.toString()) }
        else -> listOf(path to n)
    }
    private fun child(n: JsonElement, key: String): JsonElement = if(n.isJsonArray) n.asJsonArray[key.toInt()] else n.asJsonObject[key]
    private fun set(n: JsonElement, key: String, v: JsonElement) { if(n.isJsonArray) n.asJsonArray.set(key.toInt(), v) else n.asJsonObject.add(key, v) }
    @Test fun recursiveStateCommandLeavesAndOrderAreObserved() {
        val cases = load(source())
        for (case in listOf(cases.first(), cases.first { text(it["caseId"]) == "rollback-history-unestablished-pack" }, cases.first { text(it["caseId"]) == "failure-history-reject-rollback-persisted" }, cases.first { text(it["caseId"]) == "failure-history-rolled-rejected" }, cases.first { text(it["caseId"]) == "failure-history-applied-indeterminate" }, cases.first { text(it["caseId"]) == "failure-proof-missing-head-repair" }, cases.first { text(it["caseId"]) == "failure-proof-blocked-target-closure-negative" }, cases.first { text(it["caseId"]) == "failure-proof-blocked-prior-closure-repair" })) {
        val pool = mutableMapOf<String, JsonElement>()
        fun collect(n: JsonElement) { if(n.isJsonObject) n.asJsonObject.entrySet().forEach { if(!it.value.isJsonNull) pool[it.key] = it.value.deepCopy(); collect(it.value) } else if(n.isJsonArray) n.asJsonArray.forEach { collect(it) } }
        cases.forEach { collect(it["expected"]) }; pool["originalFailure"] = JsonPrimitive("FAILURE"); pool["rollbackFailure"] = JsonPrimitive("ROLLBACK"); pool["failureReceipt"] = pool.getValue("completionReceipt").deepCopy()
        leaves(case["expected"]).forEach { (path, value) ->
            val key = path.last(); if(key == "step") return@forEach
            val bad = case.deepCopy(); var part: JsonElement = bad["expected"]; path.dropLast(1).forEach { part = child(part, it) }
            if(key == "type") {
                val replacement = when(text(value)) {
                    "Vanilla" -> obj("type" to "Pack", "id" to "changed", "treeSha256" to "x", "contentSha256" to "y", "importReceiptSha256" to "z")
                    "Pack" -> obj("type" to "Vanilla")
                    "Apply" -> obj("type" to "Rollback", "correlation" to part.asJsonObject["correlation"].deepCopy())
                    else -> obj("type" to "Apply", "correlation" to pool.getValue("correlation").deepCopy())
                }
                var parent: JsonElement = bad["expected"]; path.dropLast(2).forEach { parent = child(parent, it) }; set(parent, path[path.size-2], replacement)
            } else {
                val replacement = if(value.isJsonNull) pool.getValue(key).deepCopy() else when(key) {
                    "phase" -> JsonPrimitive(if(text(value) == "IDLE") "ARMED" else "IDLE")
                    "state" -> JsonPrimitive(if(text(value) == "CLEAR") "ARMED" else "CLEAR")
                    "mode" -> JsonPrimitive(if(text(value) == "OFF") "ON" else "OFF")
                    "operation" -> JsonPrimitive(if(text(value) == "MODE_ON") "MODE_OFF" else "MODE_ON")
                    "skinStamp" -> JsonPrimitive(if(text(value) == "7") "8" else "7")
                    else -> if(value.asJsonPrimitive.isBoolean) JsonPrimitive(!bool(value)) else JsonPrimitive(text(value) + "changed")
                }
                set(part, key, replacement)
            }
            assertThrows(AssertionError::class.java) { verify(bad) }
        }
        case.getAsJsonArray("expected").forEachIndexed { i, r ->
            val omitted = case.deepCopy(); omitted.getAsJsonArray("expected").remove(i); assertThrows(FixtureFormatError::class.java) { verify(omitted) }
            if(r.asJsonObject.getAsJsonArray("commands").size() > 0) { val bad = case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.add("commands", JsonArray()); assertThrows(AssertionError::class.java) { verify(bad) } }
        }
        val reordered = case.deepCopy(); val rows = reordered.getAsJsonArray("expected"); val first = rows[0].deepCopy(); rows.set(0, rows[1].deepCopy()); rows.set(1, first); rows.forEachIndexed { i, r -> r.asJsonObject.addProperty("step", i) }; if (case["expected"] != reordered["expected"]) assertThrows(AssertionError::class.java) { verify(reordered) }
        }
    }
    @Test fun failureContractBoundsAndCompleteRepresentationPrecedeReducer() {
        val cases = load(source()).filter { text(it["oracleContract"]) == "transaction-failure-blocked-v1" }
        for (case in cases) for (old in listOf("transaction-forward-v1", "transaction-rollback-success-v1")) {
            val excluded = case.deepCopy(); excluded.addProperty("oracleContract", old)
            var called = false
            assertThrows(OracleOutOfScope::class.java) { verify(excluded) { s, e -> called = true; SkinTransactionCore().decide(s, e) } }
            assertFalse(called)
        }
        for (mode in listOf("contract", "bytes", "events")) {
            val case = cases.first().deepCopy(); val data = validate(case)
            if (mode == "contract") case.addProperty("oracleContract", "future")
            if (mode == "events") while (data.getAsJsonArray("events").size() < 33) {
                val i = data.getAsJsonArray("events").size(); data.getAsJsonArray("events").add(data.getAsJsonArray("events")[0].deepCopy())
                val row = case.getAsJsonArray("expected")[0].deepCopy().asJsonObject; row.addProperty("step", i); case.getAsJsonArray("expected").add(row)
            }
            reinput(case, data)
            if (mode == "bytes") {
                val raw = (text(case.getAsJsonObject("input")["utf8Text"]) + " ".repeat(65537)).toByteArray(Charsets.UTF_8)
                case.add("input", obj("utf8Text" to raw.toString(Charsets.UTF_8))); case.addProperty("inputSha256", sha(raw))
            }
            var called = false
            assertThrows(OracleOutOfScope::class.java) { verify(case) { s, e -> called = true; SkinTransactionCore().decide(s, e) } }; assertFalse(called)
            case.getAsJsonArray("expected")[0].asJsonObject.getAsJsonObject("state").getAsJsonObject("activation").addProperty("skinStamp", "01")
            assertThrows(FixtureFormatError::class.java) { verify(case) }
        }
        for (field in listOf("freshBinding", "priorEstablishedOnBinding")) {
            val case = cases.first().deepCopy(); val data = validate(case)
            if (field == "freshBinding") data.getAsJsonArray("events")[5].asJsonObject.addProperty(field, 1)
            else data.getAsJsonArray("events")[0].asJsonObject.getAsJsonObject("envelope").addProperty(field, 1)
            reinput(case, data); assertThrows(FixtureFormatError::class.java) { verify(case) }
        }
    }
    @Test fun corpusFailureBlockedLogs() {
        // Original 23-case partition is also pinned byte-for-byte by the proof corpus test.
        val cases = load(source()).take(138).filter { text(it["oracleContract"]) == "transaction-failure-blocked-v1" }
        assertEquals(23, cases.size); assertEquals(136, cases.sumOf { it.getAsJsonArray("expected").size() })
        cases.forEach { verify(it) }
    }
    @Test fun corpusFailureProofAndBlockedValidatorLogs() {
        assertEquals("ce51b6aed12113dbdf457edaa8c0518b4ae668883978c541392d886dcbe4b482", sha(encode(source()).take(2373974).toByteArray()))
        val all = load(source()).take(262); assertEquals(262, all.size)
        val cases = all.filter { text(it["caseId"]).startsWith("failure-proof-") }
        assertEquals(124, cases.size); assertEquals(248, cases.sumOf { it.getAsJsonArray("expected").size() })
        cases.forEach { assertEquals("transaction-failure-blocked-v1", text(it["oracleContract"])); verify(it) }
    }
    @Test fun failureProofRepairedPositivesKillSubstitutions() {
        for (case in load(source()).filter { text(it["caseId"]).startsWith("failure-proof-") && text(it["caseId"]).endsWith("-repair") })
            for (diagnosis in listOf("stale-phase", "invalid-state")) assertThrows(AssertionError::class.java) { verify(case) { s, _ -> TransactionDecision(s, diagnosis) } }
    }
    @Test fun corpusSuccessfulRollbackLogs() {
        val cases = load(source()).take(262).filter { text(it["oracleContract"]) == "transaction-rollback-success-v1" }
        assertEquals(33, cases.size); assertEquals(77, cases.sumOf { it.getAsJsonArray("expected").size() }); cases.forEach { verify(it) }
    }
    @Test fun rollbackContractStillExcludesFailureAndBlocked() {
        val baseline = load(source()).first { text(it["caseId"]) == "rollback-history-unestablished-pack" }
        for (mode in listOf("RollbackFailed", "CompletionRejected", "CompletionIndeterminate", "BLOCKED", "contract", "bytes", "events")) {
            val case = baseline.deepCopy(); val data = validate(case)
            when (mode) {
                "BLOCKED" -> { data.getAsJsonObject("initialState").addProperty("phase", mode); data.getAsJsonObject("initialState").add("binding", JsonNull.INSTANCE) }
                "contract" -> case.addProperty("oracleContract", "future")
                "events" -> while(data.getAsJsonArray("events").size() < 33) { val i = data.getAsJsonArray("events").size(); data.getAsJsonArray("events").add(data.getAsJsonArray("events")[0].deepCopy()); val row = case.getAsJsonArray("expected")[0].deepCopy(); row.asJsonObject.addProperty("step", i); case.getAsJsonArray("expected").add(row) }
                "bytes" -> Unit
                else -> {
                    val ev = obj("type" to mode, "correlation" to data.getAsJsonArray("events")[3].asJsonObject["correlation"].deepCopy())
                    if(mode != "CompletionIndeterminate") ev.addProperty("code", "invalid-code")
                    if(mode == "RollbackFailed") { ev.add("persistedFailureReceipt", JsonNull.INSTANCE); ev.add("verifiedHead", JsonNull.INSTANCE) }
                    data.getAsJsonArray("events").set(0, ev)
                }
            }
            reinput(case, data)
            if(mode == "bytes") { val raw = encode(text(case.getAsJsonObject("input")["utf8Text"]) + " ".repeat(65537)); case.getAsJsonObject("input").addProperty("utf8Text", decode(raw)); case.addProperty("inputSha256", sha(raw)) }
            var called = false; assertThrows(OracleOutOfScope::class.java) { verify(case) { s,e -> called = true; SkinTransactionCore().decide(s,e) } }; assertFalse(called)
            data.getAsJsonObject("initialState").getAsJsonObject("activation").addProperty("skinStamp", "01"); reinput(case, data)
            assertThrows(FixtureFormatError::class.java) { verify(case) }
        }
    }
    @Test fun malformedRepresentationPrecedesScopeAndReducer() {
        val location = File(requireNotNull(requireNotNull(javaClass.protectionDomain).codeSource).location.toURI())
        val missing = "tools/skin-goldens/v1/" + java.util.UUID.randomUUID().toString()
        val formatErrors = listOf(
            runCatching { readSource(location, "$missing.json") }.exceptionOrNull(),
            runCatching { readSource(location, "$missing/missing.json") }.exceptionOrNull(),
            runCatching { readSource(File("missing-repository-" + java.util.UUID.randomUUID())) }.exceptionOrNull(),
            runCatching { readSource(location) { byteArrayOf(-1) } }.exceptionOrNull(),
        )
        assertTrue("Known missing/decode errors: $formatErrors", formatErrors.all { it is FixtureFormatError })
        val corpusBytes = encode(source()); assertEquals(source(), readSource(location) { corpusBytes })
        for (error in listOf(java.io.IOException("unexpected read failure"), java.nio.file.AccessDeniedException("permission denied"))) {
            val actual = assertThrows(java.io.IOException::class.java) { readSource(location) { throw error } }
            assertSame(error, actual)
        }
        for(raw in listOf("", "{}", """{"schemaVersion":1,"cases":[]}""", """{"schemaVersion":1,"schemaVersion":1,"cases":[]}""")) assertThrows(FixtureFormatError::class.java) { load(raw) }
        val root = parse(source()).asJsonObject; root.getAsJsonArray("cases").add(root.getAsJsonArray("cases")[0].deepCopy()); assertThrows(FixtureFormatError::class.java) { load(root.toString()) }
        val case = load(source()).first()
        for(stamp in listOf("01", "+1", "-0", "9223372036854775808", "-9223372036854775809")) { val bad = case.deepCopy(); val d = validate(bad); d.getAsJsonObject("initialState").getAsJsonObject("activation").addProperty("skinStamp", stamp); d.getAsJsonObject("initialState").addProperty("phase", "BLOCKED"); reinput(bad, d); assertThrows(FixtureFormatError::class.java) { verify(bad) } }
        for(change in listOf("hash", "fields", "null", "enum", "bool", "duplicate", "unicode", "utf8", "expected")) {
            val bad = case.deepCopy(); val d = validate(bad)
            when(change) {
                "hash" -> bad.addProperty("inputSha256", "0".repeat(64))
                "expected" -> bad.getAsJsonArray("expected")[0].asJsonObject.getAsJsonArray("commands")[0].asJsonObject.getAsJsonObject("desired").addProperty("unknown", 0)
                "duplicate", "unicode", "utf8" -> {
                    val raw = if(change == "utf8") byteArrayOf(-1) else encode(if(change == "duplicate") """{"initialState":0,"initialState":0,"events":[]}""" else """{"x":"\ud800"}""")
                    bad.add("input", obj("utf8Hex" to raw.joinToString("") { (it.toInt() and 255).toString(16).padStart(2,'0') })); bad.addProperty("inputSha256", sha(raw))
                }
                else -> {
                    when(change) {
                        "fields" -> d.getAsJsonObject("initialState").remove("failureReceipt")
                        "null" -> d.getAsJsonArray("events")[0].asJsonObject.add("envelope", JsonNull.INSTANCE)
                        "enum" -> d.getAsJsonObject("initialState").addProperty("phase", "UNKNOWN")
                        "bool" -> d.getAsJsonArray("events")[0].asJsonObject.getAsJsonObject("envelope").addProperty("priorEstablishedOnBinding", 1)
                    }; reinput(bad, d)
                }
            }
            var called = false; assertThrows(FixtureFormatError::class.java) { verify(bad) { s,e -> called = true; SkinTransactionCore().decide(s,e) } }; assertFalse(called)
        }
    }
    @Test fun excludedKnownTypesAndPhasesAreAlwaysScope() {
        val baseline = load(source()).first(); val correlation = validate(baseline).getAsJsonArray("events")[1].asJsonObject["correlation"]
        val excluded = listOf("ApplyFailed", "RollbackVerified", "RollbackFailed", "CompletionRejected", "CompletionIndeterminate")
        for(mode in excluded + listOf("ROLLBACK_PENDING", "ROLLED_BACK", "BLOCKED", "contract", "bytes", "events")) {
            val case = baseline.deepCopy(); val d = validate(case)
            when {
                mode in excluded -> {
                    val event = obj("type" to mode, "correlation" to correlation.deepCopy())
                    if(mode in listOf("ApplyFailed", "RollbackFailed", "CompletionRejected")) event.addProperty("code", "bad-code-is-still-representable")
                    if(mode == "RollbackVerified") event.addProperty("freshBinding", false)
                    if(mode == "RollbackFailed") { event.add("persistedFailureReceipt", JsonNull.INSTANCE); event.add("verifiedHead", JsonNull.INSTANCE) }; d.getAsJsonArray("events").set(0, event)
                }
                mode == "contract" -> case.addProperty("oracleContract", "future")
                mode == "events" -> while(d.getAsJsonArray("events").size() < 33) { val i = d.getAsJsonArray("events").size(); d.getAsJsonArray("events").add(d.getAsJsonArray("events")[0].deepCopy()); val row = case.getAsJsonArray("expected")[0].deepCopy(); row.asJsonObject.addProperty("step", i); case.getAsJsonArray("expected").add(row) }
                mode != "bytes" -> { d.getAsJsonObject("initialState").addProperty("phase", mode); d.getAsJsonObject("initialState").add("binding", JsonNull.INSTANCE) }
            }
            reinput(case, d)
            if(mode == "bytes") { val raw = encode(text(case.getAsJsonObject("input")["utf8Text"]) + " ".repeat(65537)); case.getAsJsonObject("input").addProperty("utf8Text", decode(raw)); case.addProperty("inputSha256", sha(raw)) }
            var called = false; assertThrows(OracleOutOfScope::class.java) { verify(case) { s,e -> called = true; SkinTransactionCore().decide(s,e) } }; assertFalse(called)
            if(mode in excluded) { d.getAsJsonArray("events")[0].asJsonObject.getAsJsonObject("correlation").getAsJsonObject("binding").addProperty("value", 4); reinput(case,d); assertThrows(FixtureFormatError::class.java) { verify(case) } }
        }
    }

    // Task87: exact canonical witnesses, not known-event admission.
    private val dispatchMap = """IDLE|Begin|prepare|mode-on-zero|0
IDLE|Prepared|stale-correlation|wrong-correlation-idle|0
IDLE|ArmCommitted|stale-correlation|dispatch-idle-armcommitted|0
IDLE|ApplyVerified|stale-correlation|dispatch-idle-applyverified|0
IDLE|ApplyFailed|stale-correlation|dispatch-idle-applyfailed|0
IDLE|RollbackVerified|stale-correlation|dispatch-idle-rollbackverified|0
IDLE|RollbackFailed|stale-correlation|dispatch-idle-rollbackfailed|0
IDLE|CompletionCommitted|stale-correlation|dispatch-idle-completioncommitted|0
IDLE|CompletionRejected|stale-correlation|dispatch-idle-completionrejected|0
IDLE|CompletionIndeterminate|stale-correlation|dispatch-idle-completionindeterminate|0
PREPARING|Begin|transaction-in-progress|repair-state-preparing-arm|0
PREPARING|Prepared|arm|mode-on-zero|1
PREPARING|ArmCommitted|stale-phase|dispatch-preparing-armcommitted|0
PREPARING|ApplyVerified|stale-phase|wrong-phase-preparing|0
PREPARING|ApplyFailed|stale-phase|dispatch-preparing-applyfailed|0
PREPARING|RollbackVerified|stale-phase|dispatch-preparing-rollbackverified|0
PREPARING|RollbackFailed|stale-phase|dispatch-preparing-rollbackfailed|0
PREPARING|CompletionCommitted|stale-phase|dispatch-preparing-completioncommitted|0
PREPARING|CompletionRejected|stale-phase|dispatch-preparing-completionrejected|0
PREPARING|CompletionIndeterminate|stale-phase|dispatch-preparing-completionindeterminate|0
PREPARED|Begin|transaction-in-progress|begin-in-progress|0
PREPARED|Prepared|stale-phase|wrong-phase-prepared|0
PREPARED|ArmCommitted|apply|mode-on-zero|2
PREPARED|ApplyVerified|stale-phase|dispatch-prepared-applyverified|0
PREPARED|ApplyFailed|stale-phase|rollback-phase-applyfailed-prepared|0
PREPARED|RollbackVerified|stale-phase|dispatch-prepared-rollbackverified|0
PREPARED|RollbackFailed|stale-phase|dispatch-prepared-rollbackfailed|0
PREPARED|CompletionCommitted|stale-phase|dispatch-prepared-completioncommitted|0
PREPARED|CompletionRejected|stale-phase|dispatch-prepared-completionrejected|0
PREPARED|CompletionIndeterminate|stale-phase|dispatch-prepared-completionindeterminate|0
ARMED|Begin|transaction-in-progress|repair-state-armed-clear|0
ARMED|Prepared|stale-phase|dispatch-armed-prepared|0
ARMED|ArmCommitted|stale-phase|wrong-phase-armed|0
ARMED|ApplyVerified|commit-closure|mode-on-zero|3
ARMED|ApplyFailed|rollback|rollback-history-established-pack|3
ARMED|RollbackVerified|stale-phase|rollback-phase-rollbackverified-armed|0
ARMED|RollbackFailed|stale-phase|dispatch-armed-rollbackfailed|0
ARMED|CompletionCommitted|stale-phase|dispatch-armed-completioncommitted|0
ARMED|CompletionRejected|stale-phase|dispatch-armed-completionrejected|0
ARMED|CompletionIndeterminate|stale-phase|dispatch-armed-completionindeterminate|0
APPLIED|Begin|transaction-in-progress|repair-state-applied-no-closure|0
APPLIED|Prepared|stale-phase|dispatch-applied-prepared|0
APPLIED|ArmCommitted|stale-phase|dispatch-applied-armcommitted|0
APPLIED|ApplyVerified|stale-phase|wrong-phase-applied|0
APPLIED|ApplyFailed|stale-phase|rollback-phase-applyfailed-applied|0
APPLIED|RollbackVerified|stale-phase|dispatch-applied-rollbackverified|0
APPLIED|RollbackFailed|stale-phase|dispatch-applied-rollbackfailed|0
APPLIED|CompletionCommitted|committed|mode-on-zero|4
APPLIED|CompletionRejected|rollback|failure-history-reject-restored-established-pack|4
APPLIED|CompletionIndeterminate|completion-indeterminate|failure-history-applied-indeterminate|4
ROLLBACK_PENDING|Begin|transaction-in-progress|dispatch-rollback-pending-begin|0
ROLLBACK_PENDING|Prepared|stale-phase|dispatch-rollback-pending-prepared|0
ROLLBACK_PENDING|ArmCommitted|stale-phase|dispatch-rollback-pending-armcommitted|0
ROLLBACK_PENDING|ApplyVerified|stale-phase|dispatch-rollback-pending-applyverified|0
ROLLBACK_PENDING|ApplyFailed|stale-phase|dispatch-rollback-pending-applyfailed|0
ROLLBACK_PENDING|RollbackVerified|commit-closure|rollback-history-established-pack|4
ROLLBACK_PENDING|RollbackFailed|rollback-failed|failure-history-reject-rollback-persisted|5
ROLLBACK_PENDING|CompletionCommitted|stale-phase|dispatch-rollback-pending-completioncommitted|0
ROLLBACK_PENDING|CompletionRejected|stale-phase|dispatch-rollback-pending-completionrejected|0
ROLLBACK_PENDING|CompletionIndeterminate|stale-phase|dispatch-rollback-pending-completionindeterminate|0
ROLLED_BACK|Begin|transaction-in-progress|dispatch-rolled-back-begin|0
ROLLED_BACK|Prepared|stale-phase|dispatch-rolled-back-prepared|0
ROLLED_BACK|ArmCommitted|stale-phase|dispatch-rolled-back-armcommitted|0
ROLLED_BACK|ApplyVerified|stale-phase|dispatch-rolled-back-applyverified|0
ROLLED_BACK|ApplyFailed|stale-phase|dispatch-rolled-back-applyfailed|0
ROLLED_BACK|RollbackVerified|stale-phase|rollback-phase-rollbackverified-rolled|0
ROLLED_BACK|RollbackFailed|stale-phase|dispatch-rolled-back-rollbackfailed|0
ROLLED_BACK|CompletionCommitted|committed|rollback-history-established-pack|5
ROLLED_BACK|CompletionRejected|rollback-closure-rejected|failure-history-rolled-rejected|6
ROLLED_BACK|CompletionIndeterminate|completion-indeterminate|failure-history-rolled-indeterminate|6
COMMITTED|Begin|terminal|committed-original-failure-valid|0
COMMITTED|Prepared|terminal|dispatch-committed-prepared|0
COMMITTED|ArmCommitted|terminal|dispatch-committed-armcommitted|0
COMMITTED|ApplyVerified|terminal|dispatch-committed-applyverified|0
COMMITTED|ApplyFailed|terminal|dispatch-committed-applyfailed|0
COMMITTED|RollbackVerified|terminal|dispatch-committed-rollbackverified|0
COMMITTED|RollbackFailed|terminal|dispatch-committed-rollbackfailed|0
COMMITTED|CompletionCommitted|terminal|mode-on-zero|5
COMMITTED|CompletionRejected|terminal|dispatch-committed-completionrejected|0
COMMITTED|CompletionIndeterminate|terminal|dispatch-committed-completionindeterminate|0
BLOCKED|Begin|terminal|failure-terminal-persisted|0
BLOCKED|Prepared|terminal|failure-terminal-persisted|2
BLOCKED|ArmCommitted|terminal|dispatch-blocked-armcommitted|0
BLOCKED|ApplyVerified|terminal|dispatch-blocked-applyverified|0
BLOCKED|ApplyFailed|terminal|failure-terminal-persisted|3
BLOCKED|RollbackVerified|terminal|dispatch-blocked-rollbackverified|0
BLOCKED|RollbackFailed|terminal|failure-terminal-persisted|4
BLOCKED|CompletionCommitted|terminal|dispatch-blocked-completioncommitted|0
BLOCKED|CompletionRejected|terminal|failure-terminal-persisted|5
BLOCKED|CompletionIndeterminate|terminal|failure-terminal-persisted|6"""
    @Test fun corpusCanonicalDispatchLogs() {
        val all = load(source()).take(318); assertEquals(318, all.size)
        assertEquals("a898f1621701a4617279e483255de89089d2e9fc270852a399eb23e8a2b33839", sha(encode(source()).take(4632889).toByteArray()))
        val added = all.drop(262); assertEquals(56, added.size); assertEquals(681, all.sumOf { it.getAsJsonArray("expected").size() })
        for (case in added) {
            val before = case.toString(); val data = validate(case); val row = case.getAsJsonArray("expected")[0].asJsonObject
            assertEquals(1, data.getAsJsonArray("events").size()); assertEquals(1, case.getAsJsonArray("expected").size())
            assertTrue(bool(row["inputStateValid"])); assertTrue(bool(row["stateValid"])); assertEquals(data["initialState"], row["state"]); assertEquals(0, row.getAsJsonArray("commands").size())
            verify(case); verify(case); assertEquals(before, case.toString())
        }
        val cells = mutableSetOf<String>(); var actionCells = 0
        for (line in dispatchMap.lines()) {
            val p = line.trim().split('|'); assertTrue(cells.add(p[0] + "|" + p[1]))
            val case = all.single { text(it["caseId"]) == p[3] }; val data = validate(case); val step = p[4].toInt()
            val input = if (step == 0) data["initialState"] else case.getAsJsonArray("expected")[step - 1].asJsonObject["state"]
            val row = case.getAsJsonArray("expected")[step].asJsonObject
            assertEquals(p[0], text(input.asJsonObject["phase"])); assertEquals(p[1], text(data.getAsJsonArray("events")[step].asJsonObject["type"]))
            assertTrue(bool(row["inputStateValid"])); assertTrue(bool(row["stateValid"])); assertEquals(p[2], text(row["diagnosis"]))
            verify(case) // Full replay protects the exact row and actual command payloads.
            if (p[2] !in listOf("terminal", "stale-phase", "stale-correlation", "transaction-in-progress")) { actionCells++; assertNotEquals(input, row["state"]) }
        }
        assertEquals(90, cells.size); assertEquals(13, actionCells)
        for (phase in "IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED".split(' '))
            for (event in "Begin Prepared ArmCommitted ApplyVerified ApplyFailed RollbackVerified RollbackFailed CompletionCommitted CompletionRejected CompletionIndeterminate".split(' ')) assertTrue(phase + "|" + event in cells)
        for ((contract, count) in listOf("transaction-forward-v1" to 14, "transaction-rollback-success-v1" to 19, "transaction-failure-blocked-v1" to 23)) assertEquals(count, added.count { text(it["oracleContract"]) == contract })
    }
    @Test fun dispatchObserverDiagnosisPhaseAndDefaultSubstitutions() {
        for (case in load(source()).drop(262).take(56)) {
            for (field in listOf("diagnosis", "phase")) {
                val bad = case.deepCopy(); val row = bad.getAsJsonArray("expected")[0].asJsonObject
                if (field == "diagnosis") row.addProperty(field, "observer-damaged")
                else row.getAsJsonObject("state").addProperty(field, if (text(row.getAsJsonObject("state")[field]) == "IDLE") "ARMED" else "IDLE")
                assertThrows(AssertionError::class.java) { verify(bad) }
            }
            for (diagnosis in listOf("stale-phase", "invalid-state")) {
                // Matching stale-phase no-ops are survivors, not isolated guard kills.
                if (text(case.getAsJsonArray("expected")[0].asJsonObject["diagnosis"]) == diagnosis) verify(case) { s,_ -> TransactionDecision(s, diagnosis) }
                else assertThrows(AssertionError::class.java) { verify(case) { s,_ -> TransactionDecision(s, diagnosis) } }
            }
        }
    }
    @Test fun dispatchRecursiveLeavesAreObserved() {
        val cases = load(source())
        for (case in cases.drop(262).take(56)) {
        val pool = mutableMapOf<String, JsonElement>()
        fun collect(n: JsonElement) { if(n.isJsonObject) n.asJsonObject.entrySet().forEach { if(!it.value.isJsonNull) pool[it.key] = it.value.deepCopy(); collect(it.value) } else if(n.isJsonArray) n.asJsonArray.forEach { collect(it) } }
        cases.forEach { collect(it["expected"]) }; pool["originalFailure"] = JsonPrimitive("FAILURE"); pool["rollbackFailure"] = JsonPrimitive("ROLLBACK"); pool["failureReceipt"] = pool.getValue("completionReceipt").deepCopy()
        leaves(case["expected"]).forEach { (path, value) ->
            val key = path.last(); if(key == "step") return@forEach
            val bad = case.deepCopy(); var part: JsonElement = bad["expected"]; path.dropLast(1).forEach { part = child(part, it) }
            if(key == "type") {
                val replacement = when(text(value)) {
                    "Vanilla" -> obj("type" to "Pack", "id" to "changed", "treeSha256" to "x", "contentSha256" to "y", "importReceiptSha256" to "z")
                    "Pack" -> obj("type" to "Vanilla")
                    "Apply" -> obj("type" to "Rollback", "correlation" to part.asJsonObject["correlation"].deepCopy())
                    else -> obj("type" to "Apply", "correlation" to pool.getValue("correlation").deepCopy())
                }
                var parent: JsonElement = bad["expected"]; path.dropLast(2).forEach { parent = child(parent, it) }; set(parent, path[path.size-2], replacement)
            } else {
                val replacement = if(value.isJsonNull) pool.getValue(key).deepCopy() else when(key) {
                    "phase" -> JsonPrimitive(if(text(value) == "IDLE") "ARMED" else "IDLE")
                    "state" -> JsonPrimitive(if(text(value) == "CLEAR") "ARMED" else "CLEAR")
                    "mode" -> JsonPrimitive(if(text(value) == "OFF") "ON" else "OFF")
                    "operation" -> JsonPrimitive(if(text(value) == "MODE_ON") "MODE_OFF" else "MODE_ON")
                    "skinStamp" -> JsonPrimitive(if(text(value) == "7") "8" else "7")
                    else -> if(value.asJsonPrimitive.isBoolean) JsonPrimitive(!bool(value)) else JsonPrimitive(text(value) + "changed")
                }
                set(part, key, replacement)
            }
            assertThrows(AssertionError::class.java) { verify(bad) }
        }
        case.getAsJsonArray("expected").forEachIndexed { i, r ->
            val omitted = case.deepCopy(); omitted.getAsJsonArray("expected").remove(i); assertThrows(FixtureFormatError::class.java) { verify(omitted) }
            if(r.asJsonObject.getAsJsonArray("commands").size() > 0) { val bad = case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.add("commands", JsonArray()); assertThrows(AssertionError::class.java) { verify(bad) } }
        }
        }
    }
    // Task88: representative axes only; syntax-invalid also mismatches valid state identity.
    private val correlationMap = """PREPARING|Prepared|uuid-syntax|correlation-preparing-prepared-uuid-syntax|0|correlation-preparing-prepared-uuid-syntax|1|arm
PREPARING|Prepared|uuid-equality|wrong-correlation-preparing|0|mode-on-zero|1|arm
PREPARING|Prepared|token-syntax|correlation-preparing-prepared-token-syntax|0|correlation-preparing-prepared-token-syntax|1|arm
PREPARING|Prepared|token-equality|correlation-preparing-prepared-token-equality|0|correlation-preparing-prepared-token-equality|1|arm
PREPARED|ArmCommitted|uuid-syntax|correlation-prepared-armcommitted-uuid-syntax|0|correlation-prepared-armcommitted-uuid-syntax|1|apply
PREPARED|ArmCommitted|uuid-equality|correlation-prepared-armcommitted-uuid-equality|0|correlation-prepared-armcommitted-uuid-equality|1|apply
PREPARED|ArmCommitted|token-syntax|correlation-prepared-armcommitted-token-syntax|0|correlation-prepared-armcommitted-token-syntax|1|apply
PREPARED|ArmCommitted|token-equality|correlation-prepared-armcommitted-token-equality|0|correlation-prepared-armcommitted-token-equality|1|apply
ARMED|ApplyVerified|uuid-syntax|correlation-armed-applyverified-uuid-syntax|0|correlation-armed-applyverified-uuid-syntax|1|commit-closure
ARMED|ApplyVerified|uuid-equality|correlation-armed-applyverified-uuid-equality|0|correlation-armed-applyverified-uuid-equality|1|commit-closure
ARMED|ApplyVerified|token-syntax|correlation-armed-applyverified-token-syntax|0|correlation-armed-applyverified-token-syntax|1|commit-closure
ARMED|ApplyVerified|token-equality|correlation-armed-applyverified-token-equality|0|correlation-armed-applyverified-token-equality|1|commit-closure
ARMED|ApplyFailed|uuid-syntax|rollback-correlation-applyfailed-uuid-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|uuid-equality|rollback-correlation-applyfailed-uuid-mismatch|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-syntax|rollback-correlation-applyfailed-token-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-equality|rollback-correlation-applyfailed-token-mismatch|0|rollback-history-unestablished-pack|3|rollback
ROLLBACK_PENDING|RollbackVerified|uuid-syntax|rollback-correlation-rollbackverified-uuid-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|uuid-equality|rollback-correlation-rollbackverified-uuid-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-syntax|rollback-correlation-rollbackverified-token-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-equality|rollback-correlation-rollbackverified-token-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackFailed|uuid-syntax|failure-correlation-rollbackfailed|0|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|uuid-equality|failure-correlation-rollbackfailed|1|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-syntax|failure-correlation-rollbackfailed|2|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-equality|failure-correlation-rollbackfailed|3|failure-history-reject-rollback-unpersisted|5|rollback-failed
APPLIED|CompletionCommitted|uuid-syntax|correlation-applied-completioncommitted-uuid-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|uuid-equality|correlation-applied-completioncommitted-uuid-equality|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-syntax|correlation-applied-completioncommitted-token-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-equality|correlation-applied-completioncommitted-token-equality|0|correlation-applied-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-syntax|correlation-rolled-back-completioncommitted-uuid-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-equality|correlation-rolled-back-completioncommitted-uuid-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-syntax|correlation-rolled-back-completioncommitted-token-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-equality|correlation-rolled-back-completioncommitted-token-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
APPLIED|CompletionRejected|uuid-syntax|failure-correlation-applied-rejected|0|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|uuid-equality|failure-correlation-applied-rejected|1|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-syntax|failure-correlation-applied-rejected|2|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-equality|failure-correlation-applied-rejected|3|failure-history-reject-restored-unestablished-pack|4|rollback
ROLLED_BACK|CompletionRejected|uuid-syntax|failure-correlation-rolled-rejected|0|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|uuid-equality|failure-correlation-rolled-rejected|1|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-syntax|failure-correlation-rolled-rejected|2|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-equality|failure-correlation-rolled-rejected|3|failure-history-rolled-rejected|6|rollback-closure-rejected
APPLIED|CompletionIndeterminate|uuid-syntax|failure-correlation-applied-indeterminate|0|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|uuid-equality|failure-correlation-applied-indeterminate|1|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-syntax|failure-correlation-applied-indeterminate|2|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-equality|failure-correlation-applied-indeterminate|3|failure-history-applied-indeterminate|4|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-syntax|failure-correlation-rolled-indeterminate|0|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-equality|failure-correlation-rolled-indeterminate|1|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-syntax|failure-correlation-rolled-indeterminate|2|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-equality|failure-correlation-rolled-indeterminate|3|failure-history-rolled-indeterminate|6|completion-indeterminate"""
    @Test fun corpusCorrelationAxisLogs() {
        val all = load(source()).take(339); assertEquals(339,all.size); assertEquals(713,all.sumOf { it.getAsJsonArray("expected").size() })
        assertEquals("e346800246c19ef81d59174bc0ad29af1ccd5c1de09e9addb3080511d5a2ec78",sha(encode(source()).take(5014066).toByteArray()))
        val added=all.drop(318); assertEquals(21,added.size); assertEquals(32,added.sumOf { it.getAsJsonArray("expected").size() })
        for ((contract,count,steps) in listOf(Triple("transaction-forward-v1",112,205),Triple("transaction-rollback-success-v1",57,101),Triple("transaction-failure-blocked-v1",170,407))) {
            val partition=all.filter { text(it["oracleContract"])==contract }; assertEquals(count,partition.size); assertEquals(steps,partition.sumOf { it.getAsJsonArray("expected").size() })
        }
        for (case in added) { val before=case.toString(); verify(case); verify(case); assertEquals(before,case.toString()) }
    }
    @Test fun corpusAccepted318Logs() {
        val old=load(source()).take(318); assertEquals(318,old.size); assertEquals(681,old.sumOf { it.getAsJsonArray("expected").size() }); old.forEach { verify(it) }
    }
    @Test fun correlationExact48AxesAndSameSnapshotRepairs() {
        val all=load(source()).associateBy { text(it["caseId"]) }; val cells=mutableSetOf<String>(); var reused=0
        fun input(c:JsonObject,d:JsonObject,i:Int):JsonElement = if(i==0) d["initialState"] else c.getAsJsonArray("expected")[i-1].asJsonObject["state"]
        for (line in correlationMap.lines()) {
            val p=line.trim().split('|'); assertTrue(cells.add(p.take(3).joinToString("|")))
            val case=all.getValue(p[3]); val d=validate(case); val i=p[4].toInt(); val s=input(case,d,i).asJsonObject; val ev=d.getAsJsonArray("events")[i].asJsonObject; val row=case.getAsJsonArray("expected")[i].asJsonObject; val corr=ev.getAsJsonObject("correlation")
            assertEquals(p[0],text(s["phase"])); assertEquals(p[1],text(ev["type"])); assertTrue(SkinTransactionCore().isStateValid(readTransactionState(s)))
            assertTrue(bool(row["inputStateValid"])); assertTrue(bool(row["stateValid"])); assertEquals("stale-correlation",text(row["diagnosis"])); assertEquals(s,row["state"]); assertEquals(0,row.getAsJsonArray("commands").size())
            val idAxis=p[2].startsWith("uuid-"); val syntax=p[2].endsWith("-syntax")
            if(idAxis) {
                assertEquals(s["binding"],corr["binding"]); assertNotEquals(text(s.getAsJsonObject("envelope")["transactionId"]),text(corr["transactionId"]))
                assertEquals(!syntax,Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").matches(text(corr["transactionId"])))
            } else {
                assertEquals(text(s.getAsJsonObject("envelope")["transactionId"]),text(corr["transactionId"])); assertNotEquals(s["binding"],corr["binding"])
                if(syntax) assertEquals(" bad",text(corr.getAsJsonObject("binding")["value"])) else assertTrue(Regex("[A-Za-z0-9-]{1,256}").matches(text(corr.getAsJsonObject("binding")["value"])))
            }
            val positive=all.getValue(p[5]); val pd=validate(positive); val pi=p[6].toInt(); val ps=input(positive,pd,pi); val pe=pd.getAsJsonArray("events")[pi]; val pr=positive.getAsJsonArray("expected")[pi].asJsonObject
            assertEquals(s,ps); val repaired=ev.deepCopy(); repaired.add("correlation",obj("transactionId" to s.getAsJsonObject("envelope")["transactionId"].deepCopy(),"binding" to s["binding"].deepCopy()))
            assertEquals(repaired,pe); assertTrue(bool(pr["inputStateValid"])); assertTrue(bool(pr["stateValid"])); assertEquals(p[7],text(pr["diagnosis"])); assertNotEquals(s,pr["state"])
            verify(case); verify(positive)
            if(!p[3].startsWith("correlation-")) reused++
            else if(p[1]=="CompletionCommitted") { assertNotEquals(p[3],p[5]); assertEquals(1,d.getAsJsonArray("events").size()); assertEquals(0,pi) }
            else { assertEquals(p[3],p[5]); assertEquals(1,pi) }
        }
        assertEquals(48,cells.size); assertEquals(29,reused)
        for(context in listOf("PREPARING|Prepared","PREPARED|ArmCommitted","ARMED|ApplyVerified","ARMED|ApplyFailed","ROLLBACK_PENDING|RollbackVerified","ROLLBACK_PENDING|RollbackFailed","APPLIED|CompletionCommitted","ROLLED_BACK|CompletionCommitted","APPLIED|CompletionRejected","ROLLED_BACK|CompletionRejected","APPLIED|CompletionIndeterminate","ROLLED_BACK|CompletionIndeterminate"))
            for(axis in listOf("uuid-syntax","uuid-equality","token-syntax","token-equality")) assertTrue(context+"|"+axis in cells)
    }
    @Test fun correlationObserverDiagnosisPhaseAndNoopInvalidSubstitutions() {
        for(case in load(source()).drop(318).take(21)) {
            for(diagnosis in listOf("stale-phase","invalid-state")) assertThrows(AssertionError::class.java) { verify(case) { s,_ -> TransactionDecision(s,diagnosis) } }
            case.getAsJsonArray("expected").forEachIndexed { i,_ ->
                for(field in listOf("diagnosis","phase")) {
                    val bad=case.deepCopy(); val row=bad.getAsJsonArray("expected")[i].asJsonObject
                    if(field=="diagnosis") row.addProperty(field,"observer-damaged") else row.getAsJsonObject("state").addProperty(field,"IDLE")
                    assertThrows(AssertionError::class.java) { verify(bad) }
                }
            }
            if(case.getAsJsonArray("expected").size()==2) {
                val bad=case.deepCopy(); val rows=bad.getAsJsonArray("expected"); val first=rows[0].deepCopy(); rows.set(0,rows[1].deepCopy()); rows.set(1,first); rows[0].asJsonObject.addProperty("step",0); rows[1].asJsonObject.addProperty("step",1)
                assertThrows(AssertionError::class.java) { verify(bad) }
            }
        }
    }
    @Test fun correlationRecursiveLeavesAreObserved() {
        val cases = load(source())
        for (case in cases.drop(318).take(21)) {
        val pool = mutableMapOf<String, JsonElement>()
        fun collect(n: JsonElement) { if(n.isJsonObject) n.asJsonObject.entrySet().forEach { if(!it.value.isJsonNull) pool[it.key] = it.value.deepCopy(); collect(it.value) } else if(n.isJsonArray) n.asJsonArray.forEach { collect(it) } }
        cases.forEach { collect(it["expected"]) }; pool["originalFailure"] = JsonPrimitive("FAILURE"); pool["rollbackFailure"] = JsonPrimitive("ROLLBACK"); pool["failureReceipt"] = pool.getValue("completionReceipt").deepCopy()
        leaves(case["expected"]).forEach { (path, value) ->
            val key = path.last(); if(key == "step") return@forEach
            val bad = case.deepCopy(); var part: JsonElement = bad["expected"]; path.dropLast(1).forEach { part = child(part, it) }
            if(key == "type") {
                val replacement = when(text(value)) {
                    "Vanilla" -> obj("type" to "Pack", "id" to "changed", "treeSha256" to "x", "contentSha256" to "y", "importReceiptSha256" to "z")
                    "Pack" -> obj("type" to "Vanilla")
                    "Apply" -> obj("type" to "Rollback", "correlation" to part.asJsonObject["correlation"].deepCopy())
                    else -> obj("type" to "Apply", "correlation" to pool.getValue("correlation").deepCopy())
                }
                var parent: JsonElement = bad["expected"]; path.dropLast(2).forEach { parent = child(parent, it) }; set(parent, path[path.size-2], replacement)
            } else {
                val replacement = if(value.isJsonNull) pool.getValue(key).deepCopy() else when(key) {
                    "phase" -> JsonPrimitive(if(text(value) == "IDLE") "ARMED" else "IDLE")
                    "state" -> JsonPrimitive(if(text(value) == "CLEAR") "ARMED" else "CLEAR")
                    "mode" -> JsonPrimitive(if(text(value) == "OFF") "ON" else "OFF")
                    "operation" -> JsonPrimitive(if(text(value) == "MODE_ON") "MODE_OFF" else "MODE_ON")
                    "skinStamp" -> JsonPrimitive(if(text(value) == "7") "8" else "7")
                    else -> if(value.asJsonPrimitive.isBoolean) JsonPrimitive(!bool(value)) else JsonPrimitive(text(value) + "changed")
                }
                set(part, key, replacement)
            }
            assertThrows(AssertionError::class.java) { verify(bad) }
        }
        case.getAsJsonArray("expected").forEachIndexed { i, r ->
            val omitted = case.deepCopy(); omitted.getAsJsonArray("expected").remove(i); assertThrows(FixtureFormatError::class.java) { verify(omitted) }
            if(r.asJsonObject.getAsJsonArray("commands").size() > 0) { val bad = case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.add("commands", JsonArray()); assertThrows(AssertionError::class.java) { verify(bad) } }
        }
        }
    }
    // Task89 code-only panel; UUID/token, precedence and remaining guard families stay open.
    private val codeNegative = listOf("0A","_A","@A","[A","Aa","A/","A:","A[","A^","A`","A-"," A","A ","A A","A\u0000","A\t","A\n","A\r","A\u007f","A\u009f","Ａ","Aé","A😀","A\u202e")
    private val codeContexts = listOf("applyfailed","rollbackfailed","applied-rejected","rolled-rejected")
    @Test fun corpusAccepted339Logs() {
        val old=load(source()).take(339); assertEquals(339,old.size); assertEquals(713,old.sumOf { it.getAsJsonArray("expected").size() })
        assertEquals("25d9edf7294e4f53bbed4fbc9b5579fac9db93e873edbc4d8d478c32f976047c",sha(encode(source()).take(5228164).toByteArray())); old.forEach { verify(it) }
    }
    @Test fun corpusCodeLexicalLogs() {
        val all=load(source()).take(359); assertEquals(359,all.size); assertEquals(829,all.sumOf { it.getAsJsonArray("expected").size() }); val added=all.drop(339); assertEquals(20,added.size); assertEquals(116,added.sumOf { it.getAsJsonArray("expected").size() })
        for((contract,count,steps) in listOf(Triple("transaction-forward-v1",112,205),Triple("transaction-rollback-success-v1",62,130),Triple("transaction-failure-blocked-v1",185,494))) {
            val part=all.filter { text(it["oracleContract"])==contract }; assertEquals(count,part.size); assertEquals(steps,part.sumOf { it.getAsJsonArray("expected").size() })
        }
        for(case in added) { val before=case.toString(); verify(case); verify(case); assertEquals(before,case.toString()) }
    }
    @Test fun codeExactPanelsAndOriginalSnapshotRepairs() {
        val all=load(source()).associateBy { text(it["caseId"]) }; val seeds=listOf("rollback-code-empty","failure-code-rollbackfailed","failure-code-applied-rejected","failure-code-rolled-rejected")
        val phases=listOf("ARMED","ROLLBACK_PENDING","APPLIED","ROLLED_BACK"); val suffixes=listOf("negative-panel-and-min-repair","uppercase-last","min-plus-one","body-classes","max-minus-one")
        val panels=listOf(codeNegative+"A",listOf("Z"),listOf("A0"),listOf("AZ09_"),listOf("A".repeat(127))); var count=0
        for(n in 0..3) {
            val seed=validate(all.getValue(seeds[n])); val s=seed.getAsJsonObject("initialState"); val original=seed.getAsJsonArray("events")[0].asJsonObject; assertEquals(phases[n],text(s["phase"]))
            for(p in 0..4) {
                val case=all.getValue("lex-code-"+codeContexts[n]+"-"+suffixes[p]); val d=validate(case); assertEquals(s,d["initialState"]); assertEquals(panels[p],d.getAsJsonArray("events").map { text(it.asJsonObject["code"]) })
                for(i in panels[p].indices) {
                    val ev=d.getAsJsonArray("events")[i].asJsonObject; val row=case.getAsJsonArray("expected")[i].asJsonObject; val expectedEvent=original.deepCopy(); expectedEvent.addProperty("code",panels[p][i]); assertEquals(expectedEvent,ev)
                    assertTrue(bool(row["inputStateValid"])); assertTrue(bool(row["stateValid"]))
                    if(p==0 && i<24) {
                        assertEquals("invalid-code",text(row["diagnosis"])); assertEquals(s,row["state"]); assertEquals(0,row.getAsJsonArray("commands").size())
                        val repaired=ev.deepCopy(); repaired.addProperty("code","A"); assertEquals(repaired,d.getAsJsonArray("events")[24]); val core=SkinTransactionCore(); val decision=core.decide(readTransactionState(s),readTransactionEvent(repaired)); assertTrue(core.isStateValid(decision.state)); assertEquals(text(case.getAsJsonArray("expected")[24].asJsonObject["diagnosis"]),decision.diagnosis)
                    } else {
                        val expected=s.deepCopy(); val reverse=n==0 || n==2; expected.addProperty("phase",if(reverse) "ROLLBACK_PENDING" else "BLOCKED")
                        if(reverse) { expected.add("pendingClosure",JsonNull.INSTANCE); expected.addProperty("originalFailure",panels[p][i]) } else expected.addProperty("rollbackFailure",panels[p][i])
                        assertEquals(expected,row["state"]); assertNotEquals(s,row["state"]); assertEquals(if(reverse) "rollback" else if(n==1) "rollback-failed" else "rollback-closure-rejected",text(row["diagnosis"]))
                        val commands=JsonArray(); if(reverse) commands.add(obj("type" to "Rollback","correlation" to ev["correlation"].deepCopy())); assertEquals(commands,row["commands"])
                    }
                }
                verify(case); count++
            }
        }
        assertEquals(20,count)
    }
    @Test fun codeNamed32MalformedControlsPrecedeReducer() {
        val all=load(source()).associateBy { text(it["caseId"]) }; val names=mutableSetOf<String>()
        for(context in codeContexts) for(damage in listOf("null","bool","integer","array","object","missing","high","low")) {
            val bad=all.getValue("lex-code-"+context+"-uppercase-last").deepCopy(); val d=validate(bad); val ev=d.getAsJsonArray("events")[0].asJsonObject
            if(damage=="missing") ev.remove("code") else ev.add("code",when(damage) { "null" -> JsonNull.INSTANCE; "bool" -> JsonPrimitive(true); "integer" -> JsonPrimitive(0); "array" -> JsonArray(); "object" -> JsonObject(); else -> JsonPrimitive("SURROGATE_SENTINEL") })
            var raw=d.toString(); if(damage=="high" || damage=="low") raw=raw.replace("SURROGATE_SENTINEL",if(damage=="high") "\\ud800" else "\\udc00")
            bad.add("input",obj("utf8Text" to raw)); bad.addProperty("inputSha256",sha(encode(raw))); var calls=0
            assertThrows(FixtureFormatError::class.java) { verify(bad) { s,e -> calls++; SkinTransactionCore().decide(s,e) } }; assertEquals(0,calls); assertTrue(names.add(context+"|"+damage))
        }
        assertEquals(32,names.size)
    }
    @Test fun codeObserverDiagnosisPhaseNoopInvalidAndDistinctFinalOrder() {
        for(case in load(source()).drop(339).take(20)) {
            for(diagnosis in listOf("stale-phase","invalid-state")) assertThrows(AssertionError::class.java) { verify(case) { s,_ -> TransactionDecision(s,diagnosis) } }
            if(case.getAsJsonArray("expected").size()==25) {
                val bad=case.deepCopy(); val rows=bad.getAsJsonArray("expected"); val first=rows[0].deepCopy(); rows.set(0,rows[24].deepCopy()); rows.set(24,first); rows[0].asJsonObject.addProperty("step",0); rows[24].asJsonObject.addProperty("step",24); assertThrows(AssertionError::class.java) { verify(bad) }
            }
            case.getAsJsonArray("expected").forEachIndexed { i,row ->
                for(field in listOf("diagnosis","phase")) { val bad=case.deepCopy(); val r=bad.getAsJsonArray("expected")[i].asJsonObject; if(field=="diagnosis") r.addProperty(field,"observer-damaged") else r.getAsJsonObject("state").addProperty(field,"IDLE"); assertThrows(AssertionError::class.java) { verify(bad) } }
                val commands=row.asJsonObject.getAsJsonArray("commands"); if(commands.size()>0) { val bad=case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.getAsJsonArray("commands").add(commands[0].deepCopy()); assertThrows(AssertionError::class.java) { verify(bad) } }
            }
        }
        // Only the changing final positive distinguishes order, not identical negative rows. No multi-command claim.
    }

    @Test fun codeRecursiveLeavesAreObserved() {
        val cases = load(source())
        for (case in cases.drop(339).take(20)) {
        val pool = mutableMapOf<String, JsonElement>()
        fun collect(n: JsonElement) { if(n.isJsonObject) n.asJsonObject.entrySet().forEach { if(!it.value.isJsonNull) pool[it.key] = it.value.deepCopy(); collect(it.value) } else if(n.isJsonArray) n.asJsonArray.forEach { collect(it) } }
        cases.forEach { collect(it["expected"]) }; pool["originalFailure"] = JsonPrimitive("FAILURE"); pool["rollbackFailure"] = JsonPrimitive("ROLLBACK"); pool["failureReceipt"] = pool.getValue("completionReceipt").deepCopy()
        leaves(case["expected"]).forEach { (path, value) ->
            val key = path.last(); if(key == "step") return@forEach
            val bad = case.deepCopy(); var part: JsonElement = bad["expected"]; path.dropLast(1).forEach { part = child(part, it) }
            if(key == "type") {
                val replacement = when(text(value)) {
                    "Vanilla" -> obj("type" to "Pack", "id" to "changed", "treeSha256" to "x", "contentSha256" to "y", "importReceiptSha256" to "z")
                    "Pack" -> obj("type" to "Vanilla")
                    "Apply" -> obj("type" to "Rollback", "correlation" to part.asJsonObject["correlation"].deepCopy())
                    else -> obj("type" to "Apply", "correlation" to pool.getValue("correlation").deepCopy())
                }
                var parent: JsonElement = bad["expected"]; path.dropLast(2).forEach { parent = child(parent, it) }; set(parent, path[path.size-2], replacement)
            } else {
                val replacement = if(value.isJsonNull) pool.getValue(key).deepCopy() else when(key) {
                    "phase" -> JsonPrimitive(if(text(value) == "IDLE") "ARMED" else "IDLE")
                    "state" -> JsonPrimitive(if(text(value) == "CLEAR") "ARMED" else "CLEAR")
                    "mode" -> JsonPrimitive(if(text(value) == "OFF") "ON" else "OFF")
                    "operation" -> JsonPrimitive(if(text(value) == "MODE_ON") "MODE_OFF" else "MODE_ON")
                    "skinStamp" -> JsonPrimitive(if(text(value) == "7") "8" else "7")
                    else -> if(value.asJsonPrimitive.isBoolean) JsonPrimitive(!bool(value)) else JsonPrimitive(text(value) + "changed")
                }
                set(part, key, replacement)
            }
            assertThrows(AssertionError::class.java) { verify(bad) }
        }
        case.getAsJsonArray("expected").forEachIndexed { i, r ->
            val omitted = case.deepCopy(); omitted.getAsJsonArray("expected").remove(i); assertThrows(FixtureFormatError::class.java) { verify(omitted) }
            if(r.asJsonObject.getAsJsonArray("commands").size() > 0) { val bad = case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.add("commands", JsonArray()); assertThrows(AssertionError::class.java) { verify(bad) } }
        }
        }
    }
    // Task90 representative UUID-only panel; equality, length and syntax guards overlap.
    private val uuidPanels = listOf(listOf("","11111111-1111-1111-1111-11111111111","11111111-1111-1111-1111-1111111111111","111111111111-1111-1111-111111111111","11111111--1111-1111-1111-111111111111","1111111-11111-1111-1111-111111111111","11111111_1111-1111-1111-111111111111","11111111111111111111111111111111","{11111111-1111-1111-1111-111111111111}","urn:uuid:11111111-1111-1111-1111-111111111111"),listOf("A1111111-1111-1111-1111-111111111111","F1111111-1111-1111-1111-111111111111","AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA","/1111111-1111-1111-1111-111111111111",":1111111-1111-1111-1111-111111111111","`1111111-1111-1111-1111-111111111111","g1111111-1111-1111-1111-111111111111","G1111111-1111-1111-1111-111111111111"),listOf(" 11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111 ","1111 111-1111-1111-1111-111111111111","\t11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\t","1111\t111-1111-1111-1111-111111111111","\n11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\n","1111\n111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\r","11111111-1111-1111-1111-111111111111\r\n","1111\u0000111-1111-1111-1111-111111111111","1111\u007f111-1111-1111-1111-111111111111","1111\u0085111-1111-1111-1111-111111111111","\u00a011111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\u00a0","1111\u2028111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\u2028"),listOf("\u06601111111-1111-1111-1111-111111111111","\uff101111111-1111-1111-1111-111111111111","\uff411111111-1111-1111-1111-111111111111","\u03b11111111-1111-1111-1111-111111111111","\u04301111111-1111-1111-1111-111111111111","11111111\u20101111-1111-1111-111111111111","\ud83d\ude001111111-1111-1111-1111-111111111111","\ud835\udfce1111111-1111-1111-1111-111111111111"))
    private val uuidPanelNames = listOf("shape","ascii","whitespace","unicode")
    private val uuidPositiveNames = listOf("zero","nine","a","f")
    private val uuidPositiveValues = listOf("00000000-0000-0000-0000-000000000000","99999999-9999-9999-9999-999999999999","aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","ffffffff-ffff-ffff-ffff-ffffffffffff")
    @Test fun corpusAccepted359Logs() {
        val old=load(source()).take(359); assertEquals(359,old.size); assertEquals(829,old.sumOf { it.getAsJsonArray("expected").size() })
        assertEquals("88dc3230cbe4fc346120b26c003aa2e716d71d3ca7b27f2eb66098d9716c5b2d",sha(encode(source()).take(5937735).toByteArray())); old.forEach { verify(it) }
    }
    @Test fun corpusUuidLexicalLogs() {
        val all=load(source()); assertEquals(367,all.size); assertEquals(881,all.sumOf { it.getAsJsonArray("expected").size() })
        assertEquals(8,all.drop(359).size); assertEquals(52,all.drop(359).sumOf { it.getAsJsonArray("expected").size() })
        for((contract,count,steps) in listOf(Triple("transaction-forward-v1",120,257),Triple("transaction-rollback-success-v1",62,130),Triple("transaction-failure-blocked-v1",185,494))) {
            val part=all.filter { text(it["oracleContract"])==contract }; assertEquals(count,part.size); assertEquals(steps,part.sumOf { it.getAsJsonArray("expected").size() })
        }
        for(case in all) { val before=case.toString(); verify(case); assertEquals(before,case.toString()) }
    }
    @Test fun uuidExactPanelsAndOriginalSnapshotRepairs() {
        val all=load(source()).associateBy { text(it["caseId"]) }; val donor=validate(all.getValue("correlation-preparing-prepared-uuid-syntax")); val original=donor.getAsJsonArray("events")[1].asJsonObject; val originalState=donor.getAsJsonObject("initialState"); var negatives=0
        for(p in 0..7) {
            val positive=p>=4; val name=if(positive) "positive-"+uuidPositiveNames[p-4] else uuidPanelNames[p]; val values=if(positive) listOf(uuidPositiveValues[p-4]) else uuidPanels[p]+text(original.getAsJsonObject("correlation")["transactionId"])
            val case=all.getValue("lex-uuid-preparing-prepared-"+name); val d=validate(case); val s=d.getAsJsonObject("initialState"); val initial=originalState.deepCopy(); if(positive) initial.getAsJsonObject("envelope").addProperty("transactionId",values[0])
            assertEquals(initial,s); assertTrue(SkinTransactionCore().isStateValid(readTransactionState(s))); assertEquals("transaction-forward-v1",text(case["oracleContract"]))
            assertEquals(values,d.getAsJsonArray("events").map { text(it.asJsonObject.getAsJsonObject("correlation")["transactionId"]) })
            for(i in values.indices) {
                val ev=d.getAsJsonArray("events")[i].asJsonObject; val row=case.getAsJsonArray("expected")[i].asJsonObject; val expectedEvent=original.deepCopy(); expectedEvent.getAsJsonObject("correlation").addProperty("transactionId",values[i]); assertEquals(expectedEvent,ev)
                assertTrue(bool(row["inputStateValid"])); assertTrue(bool(row["stateValid"]))
                if(!positive && i<values.size-1) {
                    negatives++; assertEquals("stale-correlation",text(row["diagnosis"])); assertEquals(s,row["state"]); assertEquals(0,row.getAsJsonArray("commands").size())
                    val repair=ev.deepCopy(); repair.getAsJsonObject("correlation").addProperty("transactionId",text(original.getAsJsonObject("correlation")["transactionId"])); assertEquals(original,repair)
                    val core=SkinTransactionCore(); val result=core.decide(readTransactionState(originalState),readTransactionEvent(repair)); val out=originalState.deepCopy(); out.addProperty("phase","PREPARED")
                    assertEquals("arm",result.diagnosis); assertTrue(core.isStateValid(result.state)); assertEquals(out,project(result.state)); assertEquals(1,result.commands.size); assertEquals(obj("type" to "Arm","envelope" to originalState["envelope"].deepCopy()),project(result.commands[0]))
                } else {
                    val expected=s.deepCopy(); expected.addProperty("phase","PREPARED"); assertEquals(expected,row["state"]); assertEquals("arm",text(row["diagnosis"]))
                    val commands=JsonArray(); commands.add(obj("type" to "Arm","envelope" to s["envelope"].deepCopy())); assertEquals(commands,row["commands"])
                }
            }
            verify(case)
        }
        assertEquals(44,negatives)
    }
    @Test fun uuidNamed19RepresentationAndScopeControls() {
        val cases=load(source()); val basis=cases.single { text(it["caseId"])=="correlation-preparing-prepared-uuid-syntax" }; val names=mutableSetOf<String>()
        for(damage in listOf("null","bool","integer","array","object","missing","high","low","reversed")) {
            val bad=basis.deepCopy(); val d=validate(bad); val correlation=d.getAsJsonArray("events")[0].asJsonObject.getAsJsonObject("correlation")
            if(damage=="missing") correlation.remove("transactionId") else correlation.add("transactionId",when(damage) { "null" -> JsonNull.INSTANCE; "bool" -> JsonPrimitive(true); "integer" -> JsonPrimitive(1); "array" -> JsonArray(); "object" -> JsonObject(); else -> JsonPrimitive("SURROGATE_SENTINEL") })
            var raw=d.toString(); if(damage in listOf("high","low","reversed")) raw=raw.replace("SURROGATE_SENTINEL",when(damage) { "high" -> "\\ud800"; "low" -> "\\udc00"; else -> "\\udc00\\ud800" })
            bad.add("input",obj("utf8Text" to raw)); bad.addProperty("inputSha256",sha(encode(raw))); var calls=0
            assertThrows(FixtureFormatError::class.java) { verify(bad) { s,e -> calls++; SkinTransactionCore().decide(s,e) } }; assertEquals(0,calls); assertTrue(names.add("Prepared|"+damage))
        }
        for(kind in listOf("ApplyFailed","RollbackVerified","RollbackFailed","CompletionRejected","CompletionIndeterminate")) {
            val seed=cases.flatMap { validate(it).getAsJsonArray("events").toList() }.first { text(it.asJsonObject["type"])==kind }
            for(malformed in listOf(false,true)) {
                val bad=basis.deepCopy(); val d=validate(bad); val ev=seed.deepCopy().asJsonObject; ev.getAsJsonObject("correlation").add("transactionId",if(malformed) JsonNull.INSTANCE else JsonPrimitive("")); d.getAsJsonArray("events").set(0,ev); reinput(bad,d); var calls=0
                if(malformed) assertThrows(FixtureFormatError::class.java) { verify(bad) { s,e -> calls++; SkinTransactionCore().decide(s,e) } } else assertThrows(OracleOutOfScope::class.java) { verify(bad) { s,e -> calls++; SkinTransactionCore().decide(s,e) } }
                assertEquals(0,calls); assertTrue(names.add(kind+"|"+malformed))
            }
        }
        assertEquals(19,names.size)
    }
    @Test fun uuidContractsReplayAndInputImmutability() {
        for(case in load(source()).drop(359).take(8)) {
            val before=case.toString(); verify(case); verify(case); assertEquals(before,case.toString())
            for(contract in listOf("transaction-forward-v1","transaction-rollback-success-v1","transaction-failure-blocked-v1")) { val other=case.deepCopy(); other.addProperty("oracleContract",contract); verify(other) }
            val data=validate(case); val input=data.toString(); var s=readTransactionState(data["initialState"]); val core=SkinTransactionCore()
            for(ev in data.getAsJsonArray("events")) { val e=readTransactionEvent(ev); val a=core.decide(s,e); val b=core.decide(s,e); assertEquals(a.diagnosis,b.diagnosis); assertEquals(project(a.state),project(b.state)); assertEquals(a.commands,b.commands); s=a.state }
            assertEquals(input,data.toString())
        }
    }

    @Test fun uuidObserverDiagnosisPhaseNoopInvalidAndDistinctFinalOrder() {
        for(case in load(source()).drop(359).take(8)) {
            for(diagnosis in listOf("stale-phase","invalid-state")) assertThrows(AssertionError::class.java) { verify(case) { s,_ -> TransactionDecision(s,diagnosis) } }
            if(case.getAsJsonArray("expected").size()>1) {
                val last=case.getAsJsonArray("expected").size()-1; val bad=case.deepCopy(); val rows=bad.getAsJsonArray("expected"); val first=rows[0].deepCopy(); rows.set(0,rows[last].deepCopy()); rows.set(last,first); rows[0].asJsonObject.addProperty("step",0); rows[last].asJsonObject.addProperty("step",last); assertThrows(AssertionError::class.java) { verify(bad) }
            }
            case.getAsJsonArray("expected").forEachIndexed { i,row ->
                for(field in listOf("diagnosis","phase")) { val bad=case.deepCopy(); val r=bad.getAsJsonArray("expected")[i].asJsonObject; if(field=="diagnosis") r.addProperty(field,"observer-damaged") else r.getAsJsonObject("state").addProperty(field,"IDLE"); assertThrows(AssertionError::class.java) { verify(bad) } }
                val commands=row.asJsonObject.getAsJsonArray("commands"); if(commands.size()>0) { val bad=case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.getAsJsonArray("commands").add(commands[0].deepCopy()); assertThrows(AssertionError::class.java) { verify(bad) } }
            }
        }
        // Only the changing final positive distinguishes order, not identical negative rows. No multi-command claim.
    }

    @Test fun uuidRecursiveLeavesAreObserved() {
        val cases = load(source()); var observed=0
        for (case in cases.drop(359).take(8)) {
        val pool = mutableMapOf<String, JsonElement>()
        fun collect(n: JsonElement) { if(n.isJsonObject) n.asJsonObject.entrySet().forEach { if(!it.value.isJsonNull) pool[it.key] = it.value.deepCopy(); collect(it.value) } else if(n.isJsonArray) n.asJsonArray.forEach { collect(it) } }
        cases.forEach { collect(it["expected"]) }; pool["originalFailure"] = JsonPrimitive("FAILURE"); pool["rollbackFailure"] = JsonPrimitive("ROLLBACK"); pool["failureReceipt"] = pool.getValue("completionReceipt").deepCopy()
        leaves(case["expected"]).forEach { (path, value) ->
            val key = path.last(); if(key == "step") return@forEach
            observed++
            val bad = case.deepCopy(); var part: JsonElement = bad["expected"]; path.dropLast(1).forEach { part = child(part, it) }
            if(key == "type") {
                val replacement = when(text(value)) {
                    "Vanilla" -> obj("type" to "Pack", "id" to "changed", "treeSha256" to "x", "contentSha256" to "y", "importReceiptSha256" to "z")
                    "Pack" -> obj("type" to "Vanilla")
                    "Apply" -> obj("type" to "Rollback", "correlation" to part.asJsonObject["correlation"].deepCopy())
                    else -> obj("type" to "Apply", "correlation" to pool.getValue("correlation").deepCopy())
                }
                var parent: JsonElement = bad["expected"]; path.dropLast(2).forEach { parent = child(parent, it) }; set(parent, path[path.size-2], replacement)
            } else {
                val replacement = if(value.isJsonNull) pool.getValue(key).deepCopy() else when(key) {
                    "phase" -> JsonPrimitive(if(text(value) == "IDLE") "ARMED" else "IDLE")
                    "state" -> JsonPrimitive(if(text(value) == "CLEAR") "ARMED" else "CLEAR")
                    "mode" -> JsonPrimitive(if(text(value) == "OFF") "ON" else "OFF")
                    "operation" -> JsonPrimitive(if(text(value) == "MODE_ON") "MODE_OFF" else "MODE_ON")
                    "skinStamp" -> JsonPrimitive(if(text(value) == "7") "8" else "7")
                    else -> if(value.asJsonPrimitive.isBoolean) JsonPrimitive(!bool(value)) else JsonPrimitive(text(value) + "changed")
                }
                set(part, key, replacement)
            }
            assertThrows(AssertionError::class.java) { verify(bad) }
        }
        case.getAsJsonArray("expected").forEachIndexed { i, r ->
            val omitted = case.deepCopy(); omitted.getAsJsonArray("expected").remove(i); assertThrows(FixtureFormatError::class.java) { verify(omitted) }
            if(r.asJsonObject.getAsJsonArray("commands").size() > 0) { val bad = case.deepCopy(); bad.getAsJsonArray("expected")[i].asJsonObject.add("commands", JsonArray()); assertThrows(AssertionError::class.java) { verify(bad) } }
        }
        }
        assertEquals(2440,observed)
    }

}
