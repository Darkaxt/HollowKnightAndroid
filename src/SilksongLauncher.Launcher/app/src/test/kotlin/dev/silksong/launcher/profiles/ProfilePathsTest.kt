package dev.silksong.launcher.profiles

import java.io.File
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class ProfilePathsTest {
    @Test
    fun profiles_never_share_generated_paths() {
        val filesDir = File("build/test-profile-paths")
        val hk = ProfilePaths(filesDir, GameProfiles.require("hollow-knight"))
        val ss = ProfilePaths(filesDir, GameProfiles.require("silksong"))

        assertNotEquals(hk.generations.canonicalPath, ss.generations.canonicalPath)
        assertTrue(hk.staging.toPath().startsWith(hk.root.toPath()))
        assertTrue(ss.logs.toPath().startsWith(ss.root.toPath()))
    }

    @Test
    fun profile_mod_state_paths_are_isolated_under_each_external_root() {
        val filesDir = File("build/test-profile-paths/files")
        val externalDir = File("build/test-profile-paths/external")
        val hk = ProfileBuildPaths(filesDir, externalDir, GameProfiles.require("hollow-knight"))
        val ss = ProfileBuildPaths(filesDir, externalDir, GameProfiles.require("silksong"))

        assertNotEquals(hk.modStateRoot.canonicalPath, ss.modStateRoot.canonicalPath)
        assertTrue(hk.modStateRoot.toPath().startsWith(hk.externalRoot.toPath()))
        assertTrue(ss.modStateRoot.toPath().startsWith(ss.externalRoot.toPath()))
    }

    @Test
    fun unregistered_or_unsafe_profile_ids_are_rejected() {
        val filesDir = File("build/test-profile-paths")
        val base = GameProfiles.require("silksong")

        listOf("../escape", "..\\escape", "/absolute", "C:\\absolute", "silksong-evil")
            .forEach { unsafeId ->
                assertThrows(IllegalArgumentException::class.java) {
                    ProfilePaths(filesDir, base.copy(id = unsafeId))
                }
            }
    }

    @Test
    fun sibling_prefixes_do_not_count_as_containment() {
        val filesDir = File("build/test-profile-paths").absoluteFile
        val paths = ProfilePaths(filesDir, GameProfiles.require("silksong"))
        val sibling = File(paths.root.parentFile, "silksong-evil").toPath().normalize()

        assertTrue(!sibling.startsWith(paths.root.toPath()))
    }
}
