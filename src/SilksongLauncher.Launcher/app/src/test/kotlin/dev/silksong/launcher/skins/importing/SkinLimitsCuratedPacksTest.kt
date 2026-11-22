package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinLimits
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * #61: the curated Silksong library has packs whose unmapped atlases push them past the old
 * 256 MiB archive/payload and 16 MiB texture limits. Sizes measured from the archives (MiB rounded up).
 */
class SkinLimitsCuratedPacksTest {
    private data class Pack(val name: String, val archiveBytes: Long, val uncompressedMiB: Long, val entries: Int, val largestEntryMiB: Long)

    private val largest = listOf(
        Pack("Wisp Hornet 1.1.1", 281_280_174, 272, 163, 21),
        Pack("SteelSong 1.1.1", 278_339_170, 269, 160, 20),
        Pack("Hornet Grimm Colours 1.0.2", 277_661_960, 290, 148, 24),
        Pack("Hollow Hornet Knight 1.1", 267_977_724, 258, 142, 20),
    )

    @Test
    fun `V1 limits admit every measured curated pack that exceeded the old limits`() {
        val limits = SkinLimits.V1
        for (pack in largest) {
            assertTrue("${pack.name} archive", pack.archiveBytes > 256L shl 20 || pack.uncompressedMiB > 256)
            assertTrue("${pack.name} archive", pack.archiveBytes <= limits.quarantineBytes)
            assertTrue("${pack.name} uncompressed", (pack.uncompressedMiB shl 20) <= limits.uncompressedBytes)
            assertTrue("${pack.name} payload", (pack.uncompressedMiB shl 20) <= limits.payloadBytes)
            assertTrue("${pack.name} entries", pack.entries <= limits.entries)
            assertTrue("${pack.name} texture", (pack.largestEntryMiB shl 20) <= limits.textureBytes)
        }
    }

    @Test
    fun `raising the size limits kept the zip-bomb protections`() {
        val limits = SkinLimits.V1
        assertTrue(limits.expansionRatio <= 100)
        assertTrue(limits.decodedPixels <= 8192L * 4096)
        assertTrue(limits.dimension <= 8192)
        assertTrue(limits.quarantineBytes <= 512L shl 20)
    }
}
