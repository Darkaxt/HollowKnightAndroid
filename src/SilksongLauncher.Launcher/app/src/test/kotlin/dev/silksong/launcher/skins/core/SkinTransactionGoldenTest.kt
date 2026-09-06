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
    @Test fun corpusFullForwardLogs() { val cases = load(source()).filter { text(it["oracleContract"]) == "transaction-forward-v1" }; assertEquals(82, cases.size); assertEquals(164, cases.sumOf { it.getAsJsonArray("expected").size() }); cases.forEach { verify(it) } }
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
        for (case in listOf(cases.first(), cases.first { text(it["caseId"]) == "rollback-history-unestablished-pack" }, cases.first { text(it["caseId"]) == "failure-history-reject-rollback-persisted" }, cases.first { text(it["caseId"]) == "failure-history-rolled-rejected" }, cases.first { text(it["caseId"]) == "failure-history-applied-indeterminate" })) {
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
        val reordered = case.deepCopy(); val rows = reordered.getAsJsonArray("expected"); val first = rows[0].deepCopy(); rows.set(0, rows[1].deepCopy()); rows.set(1, first); rows.forEachIndexed { i, r -> r.asJsonObject.addProperty("step", i) }; assertThrows(AssertionError::class.java) { verify(reordered) }
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
        val cases = load(source()).filter { text(it["oracleContract"]) == "transaction-failure-blocked-v1" }
        assertEquals(23, cases.size); assertEquals(136, cases.sumOf { it.getAsJsonArray("expected").size() })
        cases.forEach { verify(it) }
    }
    @Test fun corpusSuccessfulRollbackLogs() {
        val cases = load(source()).filter { text(it["oracleContract"]) == "transaction-rollback-success-v1" }
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
}
