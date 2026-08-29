package dev.silksong.launcher.profiles

import android.content.Context

class SelectedGameStore(context: Context) {
    private val preferences =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun get(): GameProfile =
        preferences.getString(PROFILE_ID_KEY, null)
            ?.let(GameProfiles::find)
            ?: GameProfiles.require(DEFAULT_PROFILE_ID)

    fun set(profile: GameProfile) {
        require(GameProfiles.find(profile.id) == profile) {
            "Cannot select an unregistered game profile: ${profile.id}"
        }
        preferences.edit().putString(PROFILE_ID_KEY, profile.id).apply()
    }

    private companion object {
        const val PREFERENCES_NAME = "selected-game"
        const val PROFILE_ID_KEY = "profile-id"
        const val DEFAULT_PROFILE_ID = "silksong"
    }
}
