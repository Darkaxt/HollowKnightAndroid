package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.ByteArrayInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class PngValidationTest {
    private lateinit var root: File

    @Before fun setUp() {
        root = File("build/test-png-validation").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun validatesPngStructureAndAndroidDecode() {
        val bytes = TinyPngFixture.rgba(2, 3)
        val info = PngStructureValidator().inspect(bytes)
        assertTrue(info is SkinResult.Ok)
        assertEquals(2, (info as SkinResult.Ok).value.width)
        assertEquals(3, info.value.height)

        val file = File(root, "tiny.png").apply { writeBytes(bytes) }
        val decoded = AndroidPngDecoder().decodeAndRelease(file, info.value)
        assertTrue(decoded is SkinResult.Ok)
        assertEquals(6L, (decoded as SkinResult.Ok).value.pixelCount)
    }

    @Test
    fun `bounds the streamed PNG even when the declared byte count is wrong`() {
        val limits = SkinLimits.V1.copy(textureBytes = 8)
        val result = PngStructureValidator(limits).inspect(
            ByteArrayInputStream(ByteArray(9)),
            byteCount = 8,
        )

        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (result as SkinResult.Error).code)
    }

    @Test
    fun `Android decoder rejects final aliases and identity changes between passes`() {
        val bytes = TinyPngFixture.rgba(2, 3)
        val info = (PngStructureValidator().inspect(bytes) as SkinResult.Ok).value
        val file = File(root, "stable.png").apply { writeBytes(bytes) }
        val link = File(root, "linked.png")
        if (runCatching { Files.createSymbolicLink(link.toPath(), file.toPath()) }.isSuccess) {
            assertTrue(AndroidPngDecoder().decodeAndRelease(link, info) is SkinResult.Error)
        }

        val delegate = AndroidSkinFileSystem()
        var identities = 0
        val changing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun identity(path: File) = delegate.identity(path).let { identity ->
                if (++identities > 1) identity.copy(fileKey = "replacement") else identity
            }
        }
        assertTrue(AndroidPngDecoder(SkinLimits.V1, changing).decodeAndRelease(file, info) is SkinResult.Error)
    }

    @Test
    fun `Android decoder classifies identity and filesystem failures as archive-wide errors`() {
        val bytes = TinyPngFixture.rgba(2, 3)
        val info = (PngStructureValidator().inspect(bytes) as SkinResult.Ok).value
        val file = File(root, "unreadable.png").apply { writeBytes(bytes) }
        val delegate = AndroidSkinFileSystem()
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun openNoFollow(path: File): InputStream = throw IOException("descriptor failed")
        }

        val result = AndroidPngDecoder(SkinLimits.V1, failing).decodeAndRelease(file, info)

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
    }

    @Test
    fun `rejects CRC corruption APNG trailing bytes and dimension bounds`() {
        assertEquals(SkinImportCode.PNG_INVALID, (PngStructureValidator().inspect(TinyPngFixture.corruptCrc()) as SkinResult.Error).code)
        assertEquals(
            SkinImportCode.PNG_INVALID,
            (PngStructureValidator().inspect(TinyPngFixture.withChunk("acTL", ByteArray(8))) as SkinResult.Error).code,
        )
        assertEquals(
            SkinImportCode.PNG_INVALID,
            (PngStructureValidator().inspect(TinyPngFixture.rgba() + byteArrayOf(0)) as SkinResult.Error).code,
        )
        val limits = SkinLimits.V1.copy(dimension = 1)
        assertEquals(
            SkinImportCode.LIMIT_EXCEEDED,
            (PngStructureValidator(limits).inspect(TinyPngFixture.rgba(2, 1)) as SkinResult.Error).code,
        )
    }
}
