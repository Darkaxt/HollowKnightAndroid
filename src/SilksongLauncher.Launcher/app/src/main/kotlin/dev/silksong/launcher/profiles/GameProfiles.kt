package dev.silksong.launcher.profiles

object GameProfiles {
    val all: List<GameProfile> = listOf(HollowKnightProfile, SilksongProfile)

    private val byId = all.associateBy(GameProfile::id).also { indexed ->
        require(indexed.size == all.size) { "Game profile IDs must be unique" }
        require(all.all { it.runtimeStorageKey.matches(Regex("[a-z0-9]{1,4}")) }) {
            "Runtime storage keys must be one to four lowercase ASCII characters"
        }
        require(all.map(GameProfile::runtimeStorageKey).toSet().size == all.size) {
            "Runtime storage keys must be unique"
        }
    }

    fun find(id: String): GameProfile? = byId[id]

    fun require(id: String): GameProfile =
        find(id) ?: throw IllegalArgumentException("Unknown game profile: $id")
}
