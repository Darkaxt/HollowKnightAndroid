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
data class LibraryPack(val id: String, val name: String, val author: String, val candidateKey: String,
    val treeSha256: String, val receiptSha256: String)
data class SkinLibraryDocument(val mode: LibraryMode = LibraryMode.OFF, val selectedPackId: String? = null,
    val packs: List<LibraryPack> = emptyList(), val eligiblePackIds: List<String> = emptyList(),
    val rotationRun: String? = null, val lastDeath: Long = 0, val pendingPackId: String? = null)

/** The only durable configuration document. Eligibility order is explicit, never inferred from display sorting. */
object SkinLibraryCodec {
    const val MAX_BYTES = 256 * 1024
    const val PROFILE = "hollow-knight"
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
        require(value.selectedPackId == null || value.selectedPackId in ids) { "Selected pack is absent" }
        require(value.mode == LibraryMode.OFF || value.selectedPackId != null) { "Select a pack before turning skins ON" }
        require(value.eligiblePackIds.distinct().size == value.eligiblePackIds.size && value.eligiblePackIds.all { it in ids }) { "Invalid eligibility order" }
        require(value.lastDeath >= 0 && (value.rotationRun == null || value.rotationRun.matches(Regex("[0-9a-f]{32}")))) { "Invalid rotation occurrence/run" }
        require(value.rotationRun != null || (value.lastDeath == 0L && value.pendingPackId == null)) { "Rotation work requires a run" }
        require(value.mode == LibraryMode.ROTATE || value.rotationRun == null) { "Rotation work requires ROTATE" }
        require(value.pendingPackId == null || (value.pendingPackId in ids && value.pendingPackId != value.selectedPackId && value.lastDeath > 0)) { "Invalid pending successor" }
    }
    fun encode(value: SkinLibraryDocument): ByteArray {
        validate(value)
        val root = JsonObject().apply {
            addProperty("schemaVersion", 1); addProperty("profileId", PROFILE); addProperty("mode", value.mode.name)
            add("selectedPackId", value.selectedPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            add("packs", JsonArray().apply { value.packs.forEach { p -> add(JsonObject().apply {
                addProperty("id", p.id); addProperty("name", p.name); addProperty("author", p.author)
                addProperty("candidateKey", p.candidateKey); addProperty("treeSha256", p.treeSha256); addProperty("receiptSha256", p.receiptSha256)
            }) } })
            add("eligiblePackIds", JsonArray().apply { value.eligiblePackIds.forEach { add(it) } })
            add("rotationRun", value.rotationRun?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
            addProperty("lastDeath", value.lastDeath)
            add("pendingPackId", value.pendingPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
        }
        return gson.toJson(root).toByteArray(Charsets.UTF_8).also { require(it.size <= MAX_BYTES) { "Library document exceeds byte bound" } }
    }
    fun decode(bytes: ByteArray): SkinLibraryDocument = try {
        val root = strictJson(bytes).asJsonObject
        if (root.has("rotationRun") || root.has("lastDeath") || root.has("pendingPackId"))
            keys(root, "schemaVersion", "profileId", "mode", "selectedPackId", "packs", "eligiblePackIds", "rotationRun", "lastDeath", "pendingPackId")
        else keys(root, "schemaVersion", "profileId", "mode", "selectedPackId", "packs", "eligiblePackIds")
        require(root["schemaVersion"].toString() == "1" && text(root["profileId"]) == PROFILE) { "Unsupported library profile/schema" }
        val packs = root["packs"].asJsonArray.map { item -> item.asJsonObject.let { p ->
            keys(p, "id", "name", "author", "candidateKey", "treeSha256", "receiptSha256")
            LibraryPack(text(p["id"]), text(p["name"]), text(p["author"]), text(p["candidateKey"]), text(p["treeSha256"]), text(p["receiptSha256"]))
        } }
        SkinLibraryDocument(LibraryMode.valueOf(text(root["mode"])), root["selectedPackId"].takeUnless { it.isJsonNull }?.let(::text),
            packs, root["eligiblePackIds"].asJsonArray.map(::text), root["rotationRun"]?.takeUnless { it.isJsonNull }?.let(::text),
            root["lastDeath"]?.let { require(it.toString().matches(Regex("0|[1-9][0-9]{0,18}"))); it.toString().toLong() } ?: 0,
            root["pendingPackId"]?.takeUnless { it.isJsonNull }?.let(::text)).also(::validate)
    } catch (error: Exception) { throw IllegalArgumentException("Invalid skin library: ${error.message}", error) }

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
}
