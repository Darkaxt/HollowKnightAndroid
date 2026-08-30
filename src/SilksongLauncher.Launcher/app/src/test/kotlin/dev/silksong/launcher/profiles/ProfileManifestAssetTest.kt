package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class ProfileManifestAssetTest {
    @Test
    fun current_hollow_knight_manifest_is_exact_and_self_consistent() {
        val context = ApplicationProvider.getApplicationContext<Context>()

        val manifest = ProfileManifestLoader.load(
            context.assets,
            "profiles/hollow-knight-1.5.12620.json",
        )

        assertEquals(1, manifest.schemaVersion)
        assertEquals("hollow-knight", manifest.profileId)
        assertEquals("1.5.12620", manifest.gameVersion)
        assertEquals("6000.0.61f1", manifest.unityVersion)
        assertEquals("linux", manifest.platform)
        assertEquals(1, manifest.converterReportSchema)
        assertEquals(1_748, manifest.requiredFiles.size)
        assertEquals(manifest.computeSha256(), manifest.manifestSha256)
        assertEquals(
            mapOf(
                ManifestAction.TRANSFORM to 1_005,
                ManifestAction.COPY to 737,
                ManifestAction.EXCLUDE to 4,
                ManifestAction.REPLACE_AT_ASSEMBLY to 2,
            ),
            manifest.requiredFiles.groupingBy { it.action }.eachCount(),
        )
        assertEquals(
            ManifestAction.COPY,
            manifest.requiredFiles.single {
                it.relativePath == "Managed/Assembly-CSharp.dll"
            }.action,
        )
        assertEquals(
            ManifestAction.EXCLUDE,
            manifest.requiredFiles.single {
                it.relativePath == "Plugins/libsteam_api.so"
            }.action,
        )
        assertTrue(manifest.requiredFiles.all { it.sha256.matches(SHA_256) })
        assertFalse(manifest.requiredFiles.any { file ->
            file.relativePath.startsWith('/') ||
                file.relativePath.contains('\\') ||
                file.relativePath.split('/').any { it == ".." }
        })
    }

    private companion object {
        val SHA_256 = Regex("^[0-9a-f]{64}$")
    }
}
