package dev.silksong.launcher.skins.library

import com.google.gson.GsonBuilder
import com.google.gson.JsonArray
import com.google.gson.JsonElement
import com.google.gson.JsonNull
import com.google.gson.JsonObject
import com.google.gson.JsonPrimitive
import com.google.gson.Strictness
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import java.io.StringReader
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction

enum class LibraryMode { OFF, ON, ROTATE }
enum class SpriteScope { ALL, CHARACTER_HUD, CHARACTER }
data class LibraryPack(val id: String, val name: String, val author: String, val candidateKey: String,
    val treeSha256: String, val receiptSha256: String)
data class SaveSkinAffinity(val slot: Int, val packId: String? = null, val treeSha256: String? = null)
data class SkinLibraryDocument(val mode: LibraryMode = LibraryMode.OFF, val selectedPackId: String? = null,
    val packs: List<LibraryPack> = emptyList(), val eligiblePackIds: List<String> = emptyList(),
    val rotationRun: String? = null, val lastDeath: Long = 0, val pendingPackId: String? = null,
    val pendingVanilla: Boolean = false, val queuedDeathOccurrences: List<Long> = emptyList(),
    val spriteScope: SpriteScope = SpriteScope.ALL, val activeSaveSlot: Int? = null,
    val saveAffinities: List<SaveSkinAffinity> = emptyList(), val configurationGeneration: Long = 0,
    val unboundSelectionPending: Boolean = false)

/** The only durable configuration document. Eligibility order is explicit, never inferred from display sorting. */
object SkinLibraryCodec {
    const val MAX_BYTES = 256 * 1024
    const val MAX_QUEUED_DEATHS = 32
    const val PROFILE = "hollow-knight"
    private fun requireProfile(profileId: String): String {
        require(profileId == "hollow-knight" || profileId == "silksong") { "Unsupported skin library profile" }
        return profileId
    }
    private val gson = GsonBuilder().serializeNulls().create()
    private val id = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    fun digest(value: String) = value.matches(Regex("[0-9a-f]{64}"))
    fun validate(value: SkinLibraryDocument) {
        require(value.packs.size <= 64) { "Library is limited to 64 packs" }
        val ids = value.packs.map { it.id }
        require(ids.distinct().size == ids.size && value.packs.map { it.candidateKey }.distinct().size == ids.size) { "Duplicate library pack" }
        value.packs.forEach { pack ->
            require(id.matches(pack.id) && digest(pack.candidateKey) && digest(pack.treeSha256) && digest(pack.receiptSha256)) { "Invalid pack identity" }
            require(listOf(pack.name, pack.author).all { it.isNotBlank() && it.length <= 160 && it.none(Char::isISOControl) }) { "Invalid pack display text" }
        }
        require(!value.unboundSelectionPending || value.activeSaveSlot == null) { "Unbound intent cannot belong to an admitted save" }
        require(value.configurationGeneration >= 0) { "Invalid configuration generation" }
        require(value.activeSaveSlot == null || value.activeSaveSlot in 0..4) { "Unsupported save slot" }
        require(value.saveAffinities.size <= 5 && value.saveAffinities.map { it.slot }.distinct().size == value.saveAffinities.size) { "Save affinity bound/duplicate" }
        value.saveAffinities.forEach { affinity ->
            require(affinity.slot in 0..4) { "Unsupported affinity slot" }
            if (affinity.packId == null) require(affinity.treeSha256 == null) { "Default affinity contains imported data" }
            else require(value.packs.any { it.id == affinity.packId && it.treeSha256 == affinity.treeSha256 }) { "Confirmed affinity pack is absent or replaced" }
        }
        require(value.selectedPackId == null || value.selectedPackId in ids) { "Selected pack is absent" }
        require(value.mode != LibraryMode.ON || value.selectedPackId != null) { "Select a pack before turning skins ON" }
        require(value.eligiblePackIds.distinct().size == value.eligiblePackIds.size && value.eligiblePackIds.all { it in ids }) { "Invalid eligibility order" }
        require(value.lastDeath >= 0 && (value.rotationRun == null || value.rotationRun.matches(Regex("[0-9a-f]{32}")))) { "Invalid rotation occurrence/run" }
        require(value.rotationRun != null || (value.lastDeath == 0L && value.pendingPackId == null && !value.pendingVanilla && value.queuedDeathOccurrences.isEmpty())) { "Rotation work requires a run" }
        require(value.mode == LibraryMode.ROTATE || value.rotationRun == null) { "Rotation work requires ROTATE" }
        require(value.pendingPackId == null || (value.pendingPackId in ids && value.pendingPackId != value.selectedPackId && value.lastDeath > 0)) { "Invalid pending successor" }
        require(!value.pendingVanilla || (value.pendingPackId == null && value.selectedPackId != null && value.lastDeath > 0)) { "Invalid pending default successor" }
        require(value.queuedDeathOccurrences.size <= MAX_QUEUED_DEATHS &&
            value.queuedDeathOccurrences.zipWithNext().all { (left, right) -> left < right } &&
            value.queuedDeathOccurrences.all { it > value.lastDeath }) { "Invalid queued death occurrences" }
        require(value.queuedDeathOccurrences.isEmpty() || value.pendingPackId != null || value.pendingVanilla) { "Queued deaths require an active successor" }
    }
    fun encode(value: SkinLibraryDocument, profileId: String = PROFILE): ByteArray {
        val owner = requireProfile(profileId)
        validate(value)
        val root = JsonObject().apply {
            addProperty("schemaVersion", 2); addProperty("profileId", owner); addProperty("mode", value.mode.name)
            addProperty("configurationGeneration", value.configurationGeneration)
            addProperty("unboundSelectionPending", value.unboundSelectionPending)
            add("activeSaveSlot", value.activeSaveSlot?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            add("saveAffinities", JsonArray().apply { value.saveAffinities.sortedBy { it.slot }.forEach { affinity ->
                add(JsonObject().apply {
                    addProperty("slot", affinity.slot)
                    add("packId", affinity.packId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
                    add("treeSha256", affinity.treeSha256?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
                })
            } })
            addProperty("spriteScope", value.spriteScope.name)
            add("selectedPackId", value.selectedPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            add("packs", JsonArray().apply { value.packs.forEach { p -> add(JsonObject().apply {
                addProperty("id", p.id); addProperty("name", p.name); addProperty("author", p.author)
                addProperty("candidateKey", p.candidateKey); addProperty("treeSha256", p.treeSha256); addProperty("receiptSha256", p.receiptSha256)
            }) } })
            add("eligiblePackIds", JsonArray().apply { value.eligiblePackIds.forEach { add(it) } })
            add("rotationRun", value.rotationRun?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            addProperty("lastDeath", value.lastDeath)
            add("pendingPackId", value.pendingPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            addProperty("pendingVanilla", value.pendingVanilla)
            add("queuedDeathOccurrences", JsonArray().apply { value.queuedDeathOccurrences.forEach { add(it) } })
        }
        return gson.toJson(root).toByteArray(Charsets.UTF_8).also { require(it.size <= MAX_BYTES) { "Library document exceeds byte bound" } }
    }
    fun decode(bytes: ByteArray, profileId: String = PROFILE): SkinLibraryDocument = try {
        val owner = requireProfile(profileId)
        val root = strictJson(bytes).asJsonObject
        val baseKeys = mutableListOf("schemaVersion", "profileId", "mode", "selectedPackId", "packs", "eligiblePackIds")
        if (root.has("spriteScope")) baseKeys += "spriteScope"
        if (root.has("queuedDeathOccurrences"))
            baseKeys += listOf("rotationRun", "lastDeath", "pendingPackId", "queuedDeathOccurrences")
        else if (root.has("rotationRun") || root.has("lastDeath") || root.has("pendingPackId"))
            baseKeys += listOf("rotationRun", "lastDeath", "pendingPackId")
        if (root.has("pendingVanilla")) baseKeys += "pendingVanilla"
        val schema = root["schemaVersion"].toString()
        require(schema == "1" || schema == "2") { "Unsupported library schema" }
        if (schema == "2") baseKeys += listOf("activeSaveSlot", "saveAffinities", "configurationGeneration", "unboundSelectionPending")
        keys(root, *baseKeys.toTypedArray())
        require(text(root["profileId"]) == owner) { "Unsupported library profile/schema" }
        val mode = LibraryMode.valueOf(text(root["mode"]))
        val spriteScope = root["spriteScope"]?.let { SpriteScope.valueOf(text(it)) } ?: legacySpriteScope(mode, owner)
        val packs = root["packs"].asJsonArray.map { item -> item.asJsonObject.let { p ->
            keys(p, "id", "name", "author", "candidateKey", "treeSha256", "receiptSha256")
            LibraryPack(text(p["id"]), text(p["name"]), text(p["author"]), text(p["candidateKey"]), text(p["treeSha256"]), text(p["receiptSha256"]))
        } }
        fun occurrence(element: JsonElement): Long {
            require(element.isJsonPrimitive && element.asJsonPrimitive.isNumber &&
                element.toString().matches(Regex("0|[1-9][0-9]{0,18}")))
            return element.toString().toLong()
        }
        SkinLibraryDocument(
            mode = mode,
            selectedPackId = root["selectedPackId"].takeUnless { it.isJsonNull }?.let(::text),
            packs = packs,
            eligiblePackIds = root["eligiblePackIds"].asJsonArray.map(::text),
            rotationRun = root["rotationRun"]?.takeUnless { it.isJsonNull }?.let(::text),
            lastDeath = root["lastDeath"]?.let(::occurrence) ?: 0,
            pendingPackId = root["pendingPackId"]?.takeUnless { it.isJsonNull }?.let(::text),
            pendingVanilla = root["pendingVanilla"]?.let(::boolean) ?: false,
            queuedDeathOccurrences = root["queuedDeathOccurrences"]?.asJsonArray?.map(::occurrence) ?: emptyList(),
            spriteScope = spriteScope,
            configurationGeneration = root["configurationGeneration"]?.let(::occurrence) ?: 0,
            unboundSelectionPending = if (schema == "1") mode != LibraryMode.OFF && !root["selectedPackId"].isJsonNull
                else boolean(root["unboundSelectionPending"]),
            activeSaveSlot = root["activeSaveSlot"]?.takeUnless { it.isJsonNull }?.let { occurrence(it).also { slot -> require(slot in 0..4) }.toInt() },
            saveAffinities = root["saveAffinities"]?.asJsonArray?.map { item -> item.asJsonObject.let { affinity ->
                keys(affinity, "slot", "packId", "treeSha256")
                SaveSkinAffinity(occurrence(affinity["slot"]).also { require(it in 0..4) }.toInt(),
                    affinity["packId"].takeUnless { it.isJsonNull }?.let(::text),
                    affinity["treeSha256"].takeUnless { it.isJsonNull }?.let(::text))
            } } ?: emptyList(),
        ).also(::validate)
    } catch (error: Exception) { throw IllegalArgumentException("Invalid skin library: ${error.message}", error) }

    internal fun legacySpriteScope(mode: LibraryMode, profileId: String): SpriteScope {
        val owner = requireProfile(profileId)
        return if (mode != LibraryMode.ROTATE) SpriteScope.ALL
        else if (owner == "silksong") SpriteScope.CHARACTER else SpriteScope.CHARACTER_HUD
    }

    internal fun strictJson(bytes: ByteArray, maximum: Int = MAX_BYTES): JsonElement {
        require(bytes.size in 1..maximum) { "JSON byte bound exceeded" }
        val text = Charsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes)).toString()
        val reader = JsonReader(StringReader(text)).apply { strictness = Strictness.STRICT }
        var nodes = 0
        fun read(depth: Int): JsonElement {
            require(depth <= 12 && ++nodes <= 8192) { "JSON structure bound exceeded" }
            return when (reader.peek()) {
                JsonToken.BEGIN_OBJECT -> JsonObject().apply {
                    reader.beginObject()
                    while (reader.hasNext()) { val key = reader.nextName(); require(!has(key)) { "Duplicate JSON key" }; add(key, read(depth + 1)) }
                    reader.endObject()
                }
                JsonToken.BEGIN_ARRAY -> JsonArray().apply { reader.beginArray(); while (reader.hasNext()) add(read(depth + 1)); reader.endArray() }
                JsonToken.STRING -> JsonPrimitive(reader.nextString().also { require(it.length <= 4096) })
                JsonToken.NUMBER -> JsonPrimitive(reader.nextString().toBigDecimal())
                JsonToken.BOOLEAN -> JsonPrimitive(reader.nextBoolean())
                JsonToken.NULL -> { reader.nextNull(); JsonNull.INSTANCE }
                else -> error("Invalid JSON token")
            }
        }
        return reader.use { read(0).also { require(reader.peek() == JsonToken.END_DOCUMENT) { "Trailing JSON" } } }
    }
    private fun keys(value: JsonObject, vararg keys: String) { require(value.keySet() == keys.toSet()) { "Invalid library fields" } }
    private fun text(value: JsonElement): String { require(value.isJsonPrimitive && value.asJsonPrimitive.isString) { "Expected string" }; return value.asString }
    private fun boolean(value: JsonElement): Boolean { require(value.isJsonPrimitive && value.asJsonPrimitive.isBoolean) { "Expected boolean" }; return value.asBoolean }
}
