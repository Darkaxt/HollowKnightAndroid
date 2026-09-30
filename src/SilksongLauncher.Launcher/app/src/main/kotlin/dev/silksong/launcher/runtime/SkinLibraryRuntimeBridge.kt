package dev.silksong.launcher.runtime

import android.content.Context
import com.google.gson.JsonArray
import com.google.gson.JsonNull
import com.google.gson.JsonObject
import com.google.gson.JsonPrimitive
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import dev.silksong.launcher.skins.library.*
import java.io.File

/** JNI transport only. Profile identity is captured once from the verified game-process startup. */
object SkinLibraryRuntimeBridge {
    @Volatile private var launchedProfile: String? = null
    @Volatile private var access: SkinLibraryRuntimeAccess? = null
    private var factory: (() -> SkinLibraryRuntimeAccess)? = null
    internal fun initialize(context: Context, profileId: String) = synchronized(this) {
        check(launchedProfile == null || launchedProfile == profileId) { "Skin runtime is already bound to another launched profile" }
        if (launchedProfile != null) return@synchronized
        launchedProfile = profileId
        val profile = GameProfiles.require(profileId)
        factory = { SkinLibraryRuntimeAccess(SkinLibraryStore.production(context, profile, SkinRuntimeIo::mayPublish)) }
    }
    private var io = SkinRuntimeIo()
    @Volatile private var configurationEdge = 0L
    private val pending = failure("LIFECYCLE_BLOCKED", "Skin event is pending; bounded owner retries")
    @JvmStatic @JvmOverloads fun readConfiguration(saveSlot: Int = -1): String = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(0, "configuration:$saveSlot:$configurationEdge", pending) { runtimeAccess().readConfiguration(saveSlot) }
    } catch (error: Exception) { failure("PROFILE_REJECTED", error.message.orEmpty()) }
    @JvmStatic fun readMenuSnapshot(profileId: String): String = try {
        requireProfile(profileId)
        io.poll(1, "menu:$profileId:$configurationEdge", pending) { runtimeAccess().readMenuSnapshot(profileId) }
    } catch (error: Exception) { failure("PROFILE_REJECTED", error.message.orEmpty()) }
    @JvmStatic fun setMode(profileId: String, expectedConfiguration: String, mode: String): Boolean = try {
        requireProfile(profileId)
        configurationEdge++
        io.poll(2, "mode:$profileId:$expectedConfiguration:$mode", false) {
            runtimeAccess().setMode(profileId, expectedConfiguration, mode)
        }
    } catch (_: Exception) { false }
    @JvmStatic fun setSpriteScope(profileId: String, expectedConfiguration: String, scope: String): Boolean = try {
        requireProfile(profileId)
        configurationEdge++
        io.poll(2, "scope:$profileId:$expectedConfiguration:$scope", false) {
            runtimeAccess().setSpriteScope(profileId, expectedConfiguration, scope)
        }
    } catch (_: Exception) { false }
    @JvmStatic fun confirmPack(profileId: String, expectedConfiguration: String, packId: String): Boolean = try {
        requireProfile(profileId)
        configurationEdge++
        io.poll(2, "pack:$profileId:$expectedConfiguration:$packId", false) {
            runtimeAccess().confirmPack(profileId, expectedConfiguration, packId)
        }
    } catch (_: Exception) { false }
    @JvmStatic @JvmOverloads fun reportResult(configSha256: String, activePackId: String, activeTreeSha256: String,
        status: String, detail: String, saveSlot: Int = -1): Boolean = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(3, "result:$saveSlot:$configSha256:$activePackId:$activeTreeSha256:$status:$detail", false) {
            runtimeAccess().report(configSha256, activePackId, activeTreeSha256, status, detail, saveSlot)
        }
    } catch (_: Exception) { false }
    @JvmStatic fun confirmDeath(run: String, occurrence: Long): Boolean = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(4, "death:$run:$occurrence", false) { runtimeAccess().confirmDeath(run, occurrence) }
    } catch (_: Exception) { false }
    @JvmStatic fun cancelDeath(run: String, occurrence: Long): Boolean = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(4, "cancel-death:$run:$occurrence", false) { runtimeAccess().cancelDeath(run, occurrence) }
    } catch (_: Exception) { false }
    @JvmStatic fun cancelRotation(run: String): Boolean = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(4, "cancel:$run", false) { runtimeAccess().cancelRotation(run) }
    } catch (_: Exception) { false }
    @JvmStatic @JvmOverloads fun reportRotation(config: String, run: String, occurrence: Long, id: String, tree: String,
        status: String, detail: String, saveSlot: Int = -1): Boolean = try {
        requireProfile(requireNotNull(launchedProfile))
        io.poll(3, "rotation:$saveSlot:$config:$run:$occurrence:$id:$tree:$status:$detail", false) {
            runtimeAccess().reportRotation(config, run, occurrence, id, tree, status, detail, saveSlot)
        }
    } catch (_: Exception) { false }
    private fun requireProfile(profileId: String) {
        check(profileId == requireNotNull(launchedProfile) { "Skin runtime is not initialized" }) {
            "Native skin menu belongs to another launched profile"
        }
        GameProcessStartup.requireProfile(profileId)
    }
    private fun runtimeAccess(): SkinLibraryRuntimeAccess = access ?: synchronized(this) {
        access ?: requireNotNull(factory) { "Skin runtime is not initialized" }.invoke().also { access = it }
    }
    internal fun resetForTests() = synchronized(this) {
        io.close(); io = SkinRuntimeIo(); launchedProfile = null; factory = null; access = null
    }
    internal fun failure(code: String, detail: String) = JsonObject().apply {
        addProperty("ok", false); addProperty("code", code); addProperty("detail", detail.take(1024))
    }.toString()
}

internal class SkinLibraryRuntimeAccess(private val store: SkinLibraryStore) {
    private var verifiedPack: LibraryPack? = null
    private var verifiedManifest: SkinManifestDocument? = null
    private var initialized = false
    fun readMenuSnapshot(profileId: String): String {
        if (profileId != store.profileId) return SkinLibraryRuntimeBridge.failure("PROFILE_REJECTED", "Native skin menu belongs to another profile")
        val result = store.locked(nonblocking = true) {
            val document = if (!initialized) store.startRuntime().required().also { initialized = true } else store.readLocked()
            val packs = document.packs // metadata only; mutations/successful outcomes re-verify immutable objects and receipts
            val evidence = store.menuEvidence(document)
            val wire = JsonObject().apply {
                addProperty("ok", true)
                addProperty("profileId", store.profileId)
                addProperty("configSha256", store.configurationIdentity(document))
                addProperty("operationId", evidence.operation.id)
                addProperty("operationGeneration", evidence.operation.generation)
                addProperty("operationKind", evidence.operation.kind)
                addProperty("featureId", evidence.operation.featureId)
                addProperty("evidenceState", evidence.state)
                add("observation", evidence.observation ?: JsonNull.INSTANCE)
                addProperty("mode", document.mode.name)
                addProperty("spriteScope", document.spriteScope.name)
                add("selectedPackId", document.selectedPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
                add("eligiblePackIds", JsonArray().apply { document.eligiblePackIds.forEach(::add) })
                add("rotationRun", document.rotationRun?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
                addProperty("lastDeath", document.lastDeath)
                add("pendingPackId", document.pendingPackId?.let(::JsonPrimitive) ?: JsonNull.INSTANCE)
                addProperty("pendingVanilla", document.pendingVanilla)
                add("queuedDeathOccurrences", JsonArray().apply { document.queuedDeathOccurrences.forEach(::add) })
                add("packs", JsonArray().apply { packs.forEach { pack ->
                    add(JsonObject().apply {
                        addProperty("id", pack.id); addProperty("name", pack.name); addProperty("author", pack.author)
                    })
                } })
            }.toString()
            require(wire.toByteArray(Charsets.UTF_8).size <= MAX_MENU_SNAPSHOT_BYTES) { "Native skin menu snapshot exceeds byte bound" }
            SkinResult.Ok(wire)
        }
        return when (result) {
            is SkinResult.Ok -> result.value
            is SkinResult.Error -> SkinLibraryRuntimeBridge.failure(result.code.name, result.detail)
        }
    }
    fun setMode(profileId: String, expectedConfiguration: String, mode: String): Boolean =
        profileId == store.profileId && runCatching { LibraryMode.valueOf(mode) }.getOrNull()?.let {
            store.setMode(expectedConfiguration, it) is SkinResult.Ok
        } == true
    fun setSpriteScope(profileId: String, expectedConfiguration: String, scope: String): Boolean =
        profileId == store.profileId && runCatching { SpriteScope.valueOf(scope) }.getOrNull()?.let {
            store.setSpriteScope(expectedConfiguration, it) is SkinResult.Ok
        } == true
    fun confirmPack(profileId: String, expectedConfiguration: String, packId: String): Boolean =
        profileId == store.profileId && store.confirmPack(expectedConfiguration, packId) is SkinResult.Ok

    fun readConfiguration(saveSlot: Int = -1): String {
        val result = store.locked(nonblocking = true) {
            require(saveSlot in -1..4) { "Unsupported loaded save slot" }
            val started = if (!initialized) store.startRuntime().required().also { initialized = true } else store.readLocked()
            val document = if (started.activeSaveSlot != saveSlot.takeUnless { it == -1 }) store.bindSave(saveSlot).required() else started
            val vanilla = document.mode != LibraryMode.OFF && (document.pendingVanilla ||
                (document.pendingPackId == null && document.selectedPackId == null))
            val wire = JsonObject().apply {
                addProperty("ok", true); addProperty("profileId", store.profileId)
                addProperty("configSha256", store.configurationIdentity(document)); addProperty("mode", document.mode.name)
                addProperty("saveSlot", document.activeSaveSlot ?: -1)
                addProperty("spriteScope", document.spriteScope.name)
                addProperty("rotationRun", document.rotationRun.orEmpty()); addProperty("lastDeath", document.lastDeath)
                addProperty("pendingOccurrence", if (document.pendingPackId == null && !document.pendingVanilla) 0 else document.lastDeath)
                addProperty("vanilla", vanilla)
                addProperty("rotationDetail", if (document.eligiblePackIds.isEmpty() && document.selectedPackId == null)
                    "Only the default skin is eligible; unchanged" else "")
            }
            if (document.mode != LibraryMode.OFF && !vanilla) {
                val pack = document.packs.single { it.id == (document.pendingPackId ?: document.selectedPackId) }
                if (verifiedPack != pack) {
                    verifiedManifest = store.requireVerified(pack); verifiedPack = pack
                }
                val game = requireNotNull(verifiedManifest).games.getValue(store.profileId)
                wire.addProperty("packId", pack.id); wire.addProperty("treeSha256", pack.treeSha256)
                wire.addProperty("root", File(store.paths.objectRoot(pack.treeSha256), "pack").absolutePath)
                wire.add("textures", JsonArray().apply { game.textures.forEach { (target, payload) ->
                    add(JsonObject().apply { addProperty("target", target); addProperty("path", "${game.assetRoot}/$payload") })
                } })
            }
            SkinResult.Ok(wire.toString())
        }
        return when (result) {
            is SkinResult.Ok -> result.value
            is SkinResult.Error -> SkinLibraryRuntimeBridge.failure(result.code.name, result.detail)
        }
    }
    fun confirmDeath(run: String, occurrence: Long) = initialized && store.confirmDeath(run, occurrence) is SkinResult.Ok
    fun cancelDeath(run: String, occurrence: Long) = initialized && store.cancelDeath(run, occurrence) is SkinResult.Ok
    fun cancelRotation(run: String) = initialized && store.cancelRotation(run) is SkinResult.Ok
    fun reportRotation(config: String, run: String, occurrence: Long, id: String, tree: String,
        status: String, detail: String, saveSlot: Int = -1): Boolean = initialized && store.recordRotationObservation(
            config, run, occurrence, id, tree, status, detail, saveSlot.takeUnless { it == -1 },
        )
    fun report(config: String, id: String, tree: String, status: String, detail: String, saveSlot: Int = -1) =
        initialized && store.recordObservation(config, id, tree, status, detail, saveSlot.takeUnless { it == -1 })

    companion object { const val MAX_MENU_SNAPSHOT_BYTES = SkinLibraryCodec.MAX_BYTES }
}
