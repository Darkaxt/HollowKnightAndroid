package dev.silksong.launcher.profiles

object GameProfiles {
    val all: List<GameProfile> = listOf(HollowKnightProfile, SilksongProfile)

    private val byId = all.associateBy(GameProfile::id).also { indexed ->
        require(indexed.size == all.size) { "Game profile IDs must be unique" }
    }

    fun find(id: String): GameProfile? = byId[id]

    fun require(id: String): GameProfile =
        find(id) ?: throw IllegalArgumentException("Unknown game profile: $id")
}
