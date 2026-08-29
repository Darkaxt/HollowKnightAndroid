package dev.silksong.launcher.profiles

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test

class GameProfilesTest {
    @Test
    fun registry_contains_both_games() {
        assertEquals(
            setOf("hollow-knight", "silksong"),
            GameProfiles.all.map { it.id }.toSet(),
        )
    }

    @Test
    fun windows_sources_are_not_accepted() {
        assertFalse(
            GameProfiles.require("hollow-knight")
                .acceptedPlatforms
                .contains("WindowsPlayer"),
        )
    }

    @Test
    fun profiles_pin_their_own_unity_versions() {
        assertEquals("6000.0.61f1", GameProfiles.require("hollow-knight").unityVersion)
        assertEquals("6000.0.50f1", GameProfiles.require("silksong").unityVersion)
    }

    @Test
    fun profiles_target_the_current_linux_game_versions() {
        assertEquals("1.5.12620", GameProfiles.require("hollow-knight").currentGameVersion)
        assertEquals("1.0.29980", GameProfiles.require("silksong").currentGameVersion)
    }

    @Test
    fun old_hollow_knight_is_only_a_backward_compatibility_reference() {
        assertEquals(
            setOf("1.5.12612"),
            GameProfiles.require("hollow-knight").backwardCompatibilityVersions,
        )
        assertEquals(
            emptySet<String>(),
            GameProfiles.require("silksong").backwardCompatibilityVersions,
        )
    }

    @Test
    fun profiles_keep_distinct_content_layouts() {
        assertEquals(ContentLayout.CLASSIC_PLAYER, GameProfiles.require("hollow-knight").contentLayout)
        assertEquals(ContentLayout.ADDRESSABLES, GameProfiles.require("silksong").contentLayout)
    }

    @Test
    fun unknown_profile_ids_fail_closed() {
        assertNull(GameProfiles.find("unknown"))
        assertThrows(IllegalArgumentException::class.java) {
            GameProfiles.require("unknown")
        }
    }
}
