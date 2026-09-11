package dev.silksong.launcher.runtime

import android.content.Context
import com.google.gson.JsonArray
import com.google.gson.JsonObject
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
        factory = { SkinLibraryRuntimeAccess(SkinLibraryStore.production(context, profile)) }
    }
    @JvmStatic fun readConfiguration(): String {
        try { GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" }) }
        catch (error: Exception) { return failure("PROFILE_REJECTED", error.message.orEmpty()) }
        return try {
            val current = access ?: requireNotNull(factory) { "Skin runtime is not initialized" }.invoke().also { access = it }
            current.readConfiguration()
        } catch (error: Exception) { failure(SkinImportCode.DURABILITY_UNAVAILABLE.name, error.message.orEmpty()) }
    }
    @JvmStatic fun reportResult(configSha256: String, activePackId: String, activeTreeSha256: String, status: String, detail: String): Boolean =
        try {
            GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" })
            access?.report(configSha256, activePackId, activeTreeSha256, status, detail) ?: false
        } catch (_: Exception) { false } // Caller retries at next poll; no configuration write is attempted.
    @JvmStatic fun confirmDeath(run: String, occurrence: Long): Boolean = try {
        GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" })
        access?.confirmDeath(run, occurrence) ?: false
    } catch (_: Exception) { false }
    @JvmStatic fun cancelDeath(run: String, occurrence: Long): Boolean = try {
        GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" })
        access?.cancelDeath(run, occurrence) ?: false
    } catch (_: Exception) { false }
    @JvmStatic fun cancelRotation(run: String): Boolean = try {
        GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" })
        access?.cancelRotation(run) ?: false
    } catch (_: Exception) { false }
    @JvmStatic fun reportRotation(config: String, run: String, occurrence: Long, id: String, tree: String, status: String, detail: String): Boolean = try {
        GameProcessStartup.requireProfile(requireNotNull(launchedProfile) { "Skin runtime is not initialized" })
        access?.reportRotation(config, run, occurrence, id, tree, status, detail) ?: false
    } catch (_: Exception) { false }
    internal fun resetForTests() = synchronized(this) { launchedProfile = null; factory = null; access = null }
    internal fun failure(code: String, detail: String) = JsonObject().apply {
        addProperty("ok", false); addProperty("code", code); addProperty("detail", detail.take(1024))
    }.toString()
}

internal class SkinLibraryRuntimeAccess(private val store: SkinLibraryStore) {
    private var verifiedPack: LibraryPack? = null
    private var verifiedManifest: SkinManifestDocument? = null
    private var initialized = false
    fun readConfiguration(): String {
        val result = store.locked(nonblocking = true) {
            val document = if (!initialized) store.startRuntime().required().also { initialized = true } else store.readLocked()
            val wire = JsonObject().apply {
                addProperty("ok", true); addProperty("profileId", store.profileId)
                addProperty("configSha256", store.configurationIdentity(document)); addProperty("mode", document.mode.name)
                addProperty("rotationRun", document.rotationRun.orEmpty()); addProperty("lastDeath", document.lastDeath)
                addProperty("pendingOccurrence", if (document.pendingPackId == null) 0 else document.lastDeath)
                addProperty("rotationDetail", if (document.eligiblePackIds.isEmpty()) "No eligible skins; death rotation is a no-op" else
                    if (document.eligiblePackIds.size == 1 && document.eligiblePackIds[0] == document.selectedPackId) "Only selected skin is eligible; unchanged" else "")
            }
            if (document.mode != LibraryMode.OFF) {
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
    fun reportRotation(config: String, run: String, occurrence: Long, id: String, tree: String, status: String, detail: String): Boolean =
        store.locked(nonblocking = true) {
            val value = store.readLocked()
            require(initialized && value.rotationRun == run && value.lastDeath == occurrence && value.pendingPackId == id)
            check(store.recordObservation(config, id, tree, status, detail)) { "Rotation report is busy or stale; retry same successor" }
            if (status == "Applied" || status == "Unchanged")
                check(store.finishRotation(config, run, occurrence, id, tree)) { "Successor publication failed; retry same successor" }
            SkinResult.Ok(Unit)
        } is SkinResult.Ok
    fun report(config: String, id: String, tree: String, status: String, detail: String) = store.recordObservation(config, id, tree, status, detail)
}
