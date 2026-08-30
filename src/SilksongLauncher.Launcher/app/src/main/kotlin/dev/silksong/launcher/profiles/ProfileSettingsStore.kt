package dev.silksong.launcher.profiles

import android.content.Context
import android.content.SharedPreferences

class ProfileSettingsStore(
    context: Context,
    val profile: GameProfile,
) {
    val preferences: SharedPreferences

    init {
        require(GameProfiles.find(profile.id) == profile) {
            "Profile settings require an exact registered profile: ${profile.id}"
        }
        val app = context.applicationContext
        preferences = app.getSharedPreferences(preferenceName(profile), Context.MODE_PRIVATE)
        if (profile.id == "silksong") adoptLegacySilksongSettings(app)
    }

    private fun adoptLegacySilksongSettings(context: Context) {
        val migrations = context.getSharedPreferences(MIGRATIONS_NAME, Context.MODE_PRIVATE)
        if (migrations.getBoolean(SILKSONG_MIGRATION, false)) return

        val legacy = context.getSharedPreferences(LEGACY_NAME, Context.MODE_PRIVATE)
        val edit = preferences.edit()
        for (key in KNOWN_BOOLEAN_KEYS) {
            if (legacy.contains(key)) edit.putBoolean(key, legacy.getBoolean(key, false))
        }
        check(edit.commit()) { "Could not adopt legacy Silksong launcher settings" }
        check(migrations.edit().putBoolean(SILKSONG_MIGRATION, true).commit()) {
            "Could not record legacy Silksong settings adoption"
        }
    }

    companion object {
        private const val LEGACY_NAME = "launcher_settings"
        private const val MIGRATIONS_NAME = "launcher_settings.migrations"
        private const val SILKSONG_MIGRATION = "silksong-v1"
        private val KNOWN_BOOLEAN_KEYS = listOf(
            "auto_pull",
            "auto_push",
            "perf_overlay",
            "skip_intro",
            "dualscreen_enabled",
        )

        fun preferenceName(profile: GameProfile): String {
            require(GameProfiles.find(profile.id) == profile) {
                "Profile settings require an exact registered profile: ${profile.id}"
            }
            return "launcher_settings.${profile.id}"
        }
    }
}
