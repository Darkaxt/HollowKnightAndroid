package dev.silksong.launcher.builtinmods

import dev.silksong.launcher.runtime.GameProcessState

data class BuiltInModsSnapshot(
    val masterEnabled: Boolean,
    val descriptors: List<BuiltInModDescriptor>,
    private val values: Map<String, String>,
) {
    fun value(id: String): String = values.getValue(id)
}

data class BuiltInModsResult(val success: Boolean, val message: String)

/** Launcher-side persistence controller. Gameplay remains owned by the typed game adapters. */
class BuiltInModsController(
    private val gameId: String,
    descriptors: List<BuiltInModDescriptor>,
    private val store: LineModStateStore,
    private val processState: () -> GameProcessState,
) {
    private val descriptors = descriptors.filter { it.actionable }
    private var snapshot = load()

    fun snapshot(): BuiltInModsSnapshot = snapshot

    fun reload() {
        snapshot = load()
    }

    fun setMaster(enabled: Boolean): BuiltInModsResult = mutate {
        store.update(mapOf(masterKey to if (enabled) "1" else "0"))
        reload()
        BuiltInModsResult(true, if (enabled) "Mods enabled for the next launch." else "Mods disabled for the next launch.")
    }

    fun cycle(id: String): BuiltInModsResult = mutate {
        if (!snapshot.masterEnabled) {
            return@mutate BuiltInModsResult(false, "Enable MASTER before changing Mods values.")
        }
        val descriptor = descriptors.firstOrNull { it.id == id }
            ?: return@mutate BuiltInModsResult(false, "That Mods row is not available.")
        val current = snapshot.value(id)
        val currentIndex = descriptor.values.indexOf(current).takeIf { it >= 0 } ?: 0
        val next = descriptor.values[(currentIndex + 1) % descriptor.values.size]
        store.update(mapOf(valueKey(id) to next))
        reload()
        BuiltInModsResult(true, "${descriptor.title}: ${friendly(next)}")
    }

    fun reset(): BuiltInModsResult = mutate {
        store.update(descriptors.associate { valueKey(it.id) to it.defaultValue })
        reload()
        BuiltInModsResult(true, "All Mods values reset.")
    }

    private fun load(): BuiltInModsSnapshot {
        val persisted = store.read()
        val values = descriptors.associate { descriptor ->
            descriptor.id to persisted[valueKey(descriptor.id)]
                .takeIf { it in descriptor.values }
                .orEmpty()
                .ifEmpty { descriptor.defaultValue }
        }
        return BuiltInModsSnapshot(persisted[masterKey] == "1", descriptors, values)
    }

    private inline fun mutate(action: () -> BuiltInModsResult): BuiltInModsResult {
        val state = processState()
        if (state != GameProcessState.INACTIVE) {
            val certainty = if (state == GameProcessState.ACTIVE) "is running" else "state could not be verified"
            return BuiltInModsResult(false, "The selected game $certainty and must be closed before changing Mods.")
        }
        return runCatching(action).getOrElse {
            reload()
            BuiltInModsResult(false, "Mods state was not changed: ${it.message ?: "storage failed"}")
        }
    }

    private val prefix get() = "dualsouls.mods.$gameId."
    private val masterKey get() = prefix + "master"
    private fun valueKey(id: String) = prefix + "value." + id

    companion object {
        fun friendly(value: String): String = value.replace('_', ' ').uppercase()
    }
}
