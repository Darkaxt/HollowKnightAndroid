package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.PlayerImage
import java.io.File
import java.io.RandomAccessFile
import java.util.Collections
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class HollowKnightRealSourceIntegrationTest {
    @Test
    fun current_source_report_matches_the_committed_exact_manifest() {
        val sourceRoot = System.getenv("HOLLOW_KNIGHT_SOURCE_ROOT")
        val reportPath = System.getenv("HOLLOW_KNIGHT_INVENTORY_REPORT")
        assumeTrue(!sourceRoot.isNullOrBlank() && !reportPath.isNullOrBlank())
        val context = ApplicationProvider.getApplicationContext<Context>()
        val manifest = ProfileManifestLoader.load(
            context.assets,
            "profiles/hollow-knight-1.5.12620.json",
        )
        val report = HollowKnightSourceReportLoader.load(File(reportPath!!))
        val validator = HollowKnightSourceValidator(
            GameProfiles.require("hollow-knight"),
            HollowKnightSourceReporter { report },
        )

        val result = validator.validate(File(sourceRoot!!), manifest)

        assertEquals(SourceValidationStatus.SUPPORTED, result.status)
    }

    @Test
    fun current_classic_player_image_packs_as_a_stored_zip64_archive() {
        val rootPath = System.getenv("HOLLOW_KNIGHT_PLAYER_IMAGE_ROOT")
        assumeTrue(!rootPath.isNullOrBlank())
        val root = File(rootPath!!)
        val image = File(root, "image")
        assumeTrue(File(image, "globalgamemanagers").isFile)
        assumeTrue(File(image, "Managed/Metadata/global-metadata.dat").isFile)
        val profile = GameProfiles.require("hollow-knight")
        val paths = ProfileBuildPaths(File(root, "files"), File(root, "external"), profile)
        val pkg = File(root, "pkg")

        PlayerImage.install(root, pkg, paths, File(root, "unused-classic-depot"))

        val archive = File(pkg, "data.apk")
        assertTrue("classic player image must exceed the ZIP32 archive limit", archive.length() > 0xffffffffL)
        val sourceFiles = image.walkTopDown().count { it.isFile }
        ZipFile(archive).use { zip ->
            val entries = Collections.list(zip.entries())
            assertEquals(sourceFiles, entries.size)
            assertTrue(entries.all { it.method == ZipEntry.STORED })
            assertNotNull(zip.getEntry("assets/bin/Data/globalgamemanagers"))
            assertNotNull(zip.getEntry("assets/bin/Data/Managed/Metadata/global-metadata.dat"))
            assertNotNull(zip.getEntry("assets/bin/Data/Resources/unity default resources"))
            assertNotNull(zip.getEntry("assets/bin/Data/Resources/unity_builtin_extra"))
        }
        val tail = RandomAccessFile(archive, "r").use { file ->
            val size = minOf(file.length(), 1024L).toInt()
            val bytes = ByteArray(size)
            file.seek(file.length() - size)
            file.readFully(bytes)
            bytes
        }
        assertTrue("ZIP64 end record is missing", tail.containsBytes(byteArrayOf(0x50, 0x4b, 0x06, 0x06)))
        assertTrue("ZIP64 locator is missing", tail.containsBytes(byteArrayOf(0x50, 0x4b, 0x06, 0x07)))
    }

    private fun ByteArray.containsBytes(needle: ByteArray): Boolean =
        indices.any { start ->
            start + needle.size <= size && needle.indices.all { offset ->
                this[start + offset] == needle[offset]
            }
        }
}
