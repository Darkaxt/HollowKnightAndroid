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
    fun current_classic_player_image_splits_into_zip32_apk_and_unity_obb() {
        val rootPath = System.getenv("HOLLOW_KNIGHT_PLAYER_IMAGE_ROOT")
        assumeTrue(!rootPath.isNullOrBlank())
        val context = ApplicationProvider.getApplicationContext<Context>()
        val root = File(rootPath!!)
        val image = File(root, "image")
        assumeTrue(File(image, "globalgamemanagers").isFile)
        assumeTrue(File(image, "Managed/Metadata/global-metadata.dat").isFile)
        val profile = GameProfiles.require("hollow-knight")
        val paths = ProfileBuildPaths(File(root, "files"), File(root, "external"), profile)
        val pkg = File(root, "pkg")

        PlayerImage.install(context, root, pkg, paths, File(root, "unused-classic-depot"))

        val archive = File(pkg, "data.apk")
        val obb = File(pkg, PlayerImage.mainObbName(context))
        assertTrue("classic data.apk must fit ZIP32", archive.length() in 1..0xffffffffL)
        assertTrue("classic main OBB must fit Android's limit", obb.length() in 1..(2L * 1024 * 1024 * 1024))
        val sourceFiles = image.walkTopDown().count { it.isFile }
        ZipFile(archive).use { zip ->
            val entries = Collections.list(zip.entries())
            assertTrue(entries.all { it.method == ZipEntry.STORED })
            assertNotNull(zip.getEntry("assets/bin/Data/globalgamemanagers"))
            assertNotNull(zip.getEntry("assets/bin/Data/Managed/Metadata/global-metadata.dat"))
            assertNotNull(zip.getEntry("assets/bin/Data/Resources/unity default resources"))
            assertNotNull(zip.getEntry("assets/bin/Data/Resources/unity_builtin_extra"))
            assertNotNull(zip.getEntry("assets/unity_obb_guid"))
        }
        ZipFile(obb).use { zip ->
            val entries = Collections.list(zip.entries())
            assertTrue(entries.all { it.method == ZipEntry.STORED })
            assertNotNull(zip.getEntry("unity_obb_guid"))
            assertEquals(sourceFiles + 2, entries.size + ZipFile(archive).use { Collections.list(it.entries()).size })
        }
        for (zip in listOf(archive, obb)) {
            val tail = RandomAccessFile(zip, "r").use { file ->
                val size = minOf(file.length(), 1024L).toInt()
                val bytes = ByteArray(size)
                file.seek(file.length() - size)
                file.readFully(bytes)
                bytes
            }
            assertTrue("ZIP64 end record must be absent", !tail.containsBytes(byteArrayOf(0x50, 0x4b, 0x06, 0x06)))
            assertTrue("ZIP64 locator must be absent", !tail.containsBytes(byteArrayOf(0x50, 0x4b, 0x06, 0x07)))
        }
    }

    private fun ByteArray.containsBytes(needle: ByteArray): Boolean =
        indices.any { start ->
            start + needle.size <= size && needle.indices.all { offset ->
                this[start + offset] == needle[offset]
            }
        }
}
