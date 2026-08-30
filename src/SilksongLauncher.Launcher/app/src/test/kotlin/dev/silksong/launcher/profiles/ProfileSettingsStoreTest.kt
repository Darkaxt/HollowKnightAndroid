package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.BuildReset
import dev.silksong.launcher.SettingsStore
import java.io.File
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class ProfileSettingsStoreTest {
    private lateinit var context: Context

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        context.getSharedPreferences("launcher_settings", Context.MODE_PRIVATE).edit().clear().commit()
        context.getSharedPreferences("launcher_settings.migrations", Context.MODE_PRIVATE).edit().clear().commit()
        for (profile in GameProfiles.all) {
            context.getSharedPreferences(
                ProfileSettingsStore.preferenceName(profile),
                Context.MODE_PRIVATE,
            ).edit().clear().commit()
        }
    }

    @After
    fun tearDown() = setUp()

    @Test
    fun `settings are independent for each profile`() {
        val hollowKnight = SettingsStore(context, GameProfiles.require("hollow-knight"))
        val silksong = SettingsStore(context, GameProfiles.require("silksong"))

        hollowKnight.autoPull = true
        hollowKnight.perfOverlay = true

        assertTrue(hollowKnight.autoPull)
        assertTrue(hollowKnight.perfOverlay)
        assertFalse(silksong.autoPull)
        assertFalse(silksong.perfOverlay)
    }

    @Test
    fun `legacy settings are adopted into silksong only once`() {
        context.getSharedPreferences("launcher_settings", Context.MODE_PRIVATE)
            .edit()
            .putBoolean("auto_pull", true)
            .putBoolean("dualscreen_enabled", false)
            .commit()

        val hollowKnight = SettingsStore(context, GameProfiles.require("hollow-knight"))
        val silksong = SettingsStore(context, GameProfiles.require("silksong"))

        assertFalse(hollowKnight.autoPull)
        assertTrue(silksong.autoPull)
        assertFalse(silksong.dualScreen)

        silksong.autoPull = false
        SettingsStore(context, GameProfiles.require("silksong"))
        assertFalse(SettingsStore(context, GameProfiles.require("silksong")).autoPull)
    }

    @Test
    fun `reset clears selected profile settings and preserves the other`() {
        val root = File("build/test-profile-settings-reset").absoluteFile
        root.deleteRecursively()
        val external = File(root, "external").apply { mkdirs() }
        val hollowKnightProfile = GameProfiles.require("hollow-knight")
        val silksongProfile = GameProfiles.require("silksong")
        val hollowKnight = SettingsStore(context, hollowKnightProfile)
        val silksong = SettingsStore(context, silksongProfile)
        hollowKnight.autoPush = true
        silksong.autoPush = true

        BuildReset.clear(
            context,
            ProfileBuildPaths(File(root, "files"), external, hollowKnightProfile),
        )

        assertFalse(SettingsStore(context, hollowKnightProfile).autoPush)
        assertTrue(SettingsStore(context, silksongProfile).autoPush)
        root.deleteRecursively()
    }
}
