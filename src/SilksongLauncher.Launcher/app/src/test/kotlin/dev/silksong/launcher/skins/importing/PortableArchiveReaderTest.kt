package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinArchiveFormat
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.RawRarFixture
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.File
import java.io.IOException
import java.nio.channels.SeekableByteChannel
import java.security.MessageDigest
import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry
import org.apache.commons.compress.archivers.sevenz.SevenZMethod
import org.apache.commons.compress.archivers.sevenz.SevenZMethodConfiguration
import org.apache.commons.compress.archivers.sevenz.SevenZOutputFile
import org.tukaani.xz.LZMA2Options
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class PortableArchiveReaderTest {
    private lateinit var root: File
    private val fs = AndroidSkinFileSystem()

    @Before fun setUp() {
        root = File("build/test-portable-archive-reader").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun `lists and verifies bounded 7z and RAR archives through one backend`() {
        val bytes = "skin-payload".toByteArray()
        val archives = listOf(
            sevenZip("skin.7z", "Wrapper/Knight.png", bytes) to SkinArchiveFormat.SEVEN_Z,
            File(root, "skin.rar").apply {
                writeBytes(RawRarFixture.build(listOf(RawRarFixture.Entry("Wrapper/Knight.png", bytes))))
            } to SkinArchiveFormat.RAR,
        )

        for ((file, format) in archives) {
            val result = PortableArchiveReader(fs = fs).read(file, format)
            assertTrue("Expected $format success, got $result", result is SkinResult.Ok)
            val archive = (result as SkinResult.Ok).value
            assertEquals(format, archive.format)
            val entry = archive.entries.single { !it.directory }
            assertEquals("Wrapper/Knight.png", entry.rawName.toString(Charsets.UTF_8))
            assertEquals(bytes.size.toLong(), entry.uncompressedSize)
            assertTrue(ZipPathAuthority().validate(archive) is SkinResult.Ok)
        }
    }

    @Test
    fun `extracts only selected entries into caller-owned regular files`() {
        val selected = "selected-payload".toByteArray()
        val ignored = "ignored-payload".toByteArray()
        val file = sevenZip(
            "selected.7z",
            linkedMapOf("Pack/Knight.png" to selected, "Pack/readme.txt" to ignored),
        )
        val archive = (PortableArchiveReader(fs = fs).read(file, SkinArchiveFormat.SEVEN_Z) as SkinResult.Ok).value
        val entry = archive.entries.single { it.rawName.toString(Charsets.UTF_8).endsWith("Knight.png") }
        val destination = File(root, "owned/Knight.png")
        destination.parentFile.mkdirs()

        val result = PortableArchiveReader(fs = fs).extract(
            archive,
            mapOf(entry.centralIndex to destination),
            root,
        )

        assertTrue("Expected extraction success, got $result", result is SkinResult.Ok)
        assertEquals(selected.toList(), destination.readBytes().toList())
        val extraction = (result as SkinResult.Ok).value.getValue(entry.centralIndex)
        assertEquals(selected.size.toLong(), extraction.length)
        assertEquals(sha256(selected), extraction.sha256)
        assertEquals(listOf("Knight.png"), destination.parentFile.listFiles().orEmpty().map { it.name })
    }

    @Test
    fun `rejects links format mismatches and declared expansion beyond limits`() {
        val mismatch = sevenZip("mismatch.7z", "Pack/Knight.png", byteArrayOf(1))
        val wrongFormat = PortableArchiveReader(fs = fs).read(mismatch, SkinArchiveFormat.RAR)
        assertEquals(SkinImportCode.INVALID_INPUT, (wrongFormat as SkinResult.Error).code)

        val bounded = PortableArchiveReader(
            limits = SkinLimits.V1.copy(uncompressedBytes = 3, textureBytes = 3),
            fs = fs,
        ).read(sevenZip("large.7z", "Pack/Knight.png", ByteArray(4)), SkinArchiveFormat.SEVEN_Z)
        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (bounded as SkinResult.Error).code)

        val link = PortableArchiveReader(fs = fs).read(sevenZipLink(), SkinArchiveFormat.SEVEN_Z)
        assertEquals(SkinImportCode.INVALID_INPUT, (link as SkinResult.Error).code)

        val encryptedRar = rar(
            "encrypted.rar",
            RawRarFixture.Entry("Pack/Knight.png", byteArrayOf(1), flags = 0x0004),
        )
        val encrypted = PortableArchiveReader(fs = fs).read(encryptedRar, SkinArchiveFormat.RAR)
        assertEquals(SkinImportCode.INVALID_INPUT, (encrypted as SkinResult.Error).code)

        val symlinkRar = rar(
            "symlink.rar",
            RawRarFixture.Entry("Pack/Knight.png", "target".toByteArray(), host = 3, attributes = 0xa1ff),
        )
        val symlink = PortableArchiveReader(fs = fs).read(symlinkRar, SkinArchiveFormat.RAR)
        assertEquals(SkinImportCode.INVALID_INPUT, (symlink as SkinResult.Error).code)

        val regularUnixRar = rar(
            "regular-unix.rar",
            RawRarFixture.Entry("Pack/Knight.png", byteArrayOf(1), host = 3, attributes = 0x81a4),
        )
        assertTrue(PortableArchiveReader(fs = fs).read(regularUnixRar, SkinArchiveFormat.RAR) is SkinResult.Ok)
    }

    @Test
    fun `bounds decoder memory and removes partial selected output on failure`() {
        val file = sevenZipLzma("bounded.7z", "Pack/Knight.png", ByteArray(8))
        val memoryBound = PortableArchiveReader(
            limits = SkinLimits.V1.copy(decoderMemoryBytes = 1024),
            fs = fs,
        ).read(file, SkinArchiveFormat.SEVEN_Z)
        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (memoryBound as SkinResult.Error).code)

        val archive = (PortableArchiveReader(fs = fs).read(file, SkinArchiveFormat.SEVEN_Z) as SkinResult.Ok).value
        val entry = archive.entries.single()
        val destination = File(root, "owned/Knight.png").also { it.parentFile.mkdirs() }
        val extraction = PortableArchiveReader(
            limits = SkinLimits.V1.copy(textureBytes = 3),
            fs = fs,
        ).extract(archive, mapOf(entry.centralIndex to destination), root)
        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (extraction as SkinResult.Error).code)
        assertFalse(destination.exists())
    }

    @Test
    fun `surfaces cleanup failure instead of hiding partial selected output`() {
        val file = sevenZip("cleanup.7z", "Pack/Knight.png", ByteArray(8))
        val archive = (PortableArchiveReader(fs = fs).read(file, SkinArchiveFormat.SEVEN_Z) as SkinResult.Ok).value
        val entry = archive.entries.single()
        val destination = File(root, "cleanup-owned/Knight.png").also { it.parentFile.mkdirs() }
        val failingCleanup = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun deleteContained(path: File, owner: File) {
                throw IOException("cleanup failed")
            }
        }

        val result = PortableArchiveReader(
            limits = SkinLimits.V1.copy(textureBytes = 3),
            fs = failingCleanup,
        ).extract(archive, mapOf(entry.centralIndex to destination), root)

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertTrue(destination.exists())
        fs.deleteContained(destination, root)
    }

    @Test
    fun `opens both portable formats only through injected no-follow channels`() {
        var opens = 0
        val tracking = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun openSeekableNoFollow(file: File): SeekableByteChannel =
                fs.openSeekableNoFollow(file).also { opens++ }
        }
        val files = listOf(
            sevenZip("tracked.7z", "Pack/Knight.png", byteArrayOf(1)) to SkinArchiveFormat.SEVEN_Z,
            File(root, "tracked.rar").apply {
                writeBytes(RawRarFixture.build(listOf(RawRarFixture.Entry("Pack/Knight.png", byteArrayOf(1)))))
            } to SkinArchiveFormat.RAR,
        )

        files.forEach { (file, format) ->
            assertTrue(PortableArchiveReader(fs = tracking).read(file, format) is SkinResult.Ok)
        }
        assertEquals(4, opens)
    }

    private fun rar(name: String, entry: RawRarFixture.Entry): File =
        File(root, name).apply { writeBytes(RawRarFixture.build(listOf(entry))) }

    private fun sevenZipLink(): File {
        val archive = File(root, "link.7z")
        SevenZOutputFile(archive).use { output ->
            output.setContentCompression(SevenZMethod.COPY)
            val entry = SevenZArchiveEntry().apply {
                name = "Pack/Knight.png"
                size = 6
                setHasStream(true)
                setHasWindowsAttributes(true)
                windowsAttributes = 0xa000 shl 16
            }
            output.putArchiveEntry(entry)
            output.write("target".toByteArray())
            output.closeArchiveEntry()
        }
        return archive
    }

    private fun sevenZipLzma(name: String, path: String, bytes: ByteArray): File {
        val archive = File(root, name)
        SevenZOutputFile(archive).use { output ->
            output.setContentMethods(
                listOf(SevenZMethodConfiguration(SevenZMethod.LZMA2, LZMA2Options(0))),
            )
            val source = File(root, "lzma-source").apply { writeBytes(bytes) }
            val entry = output.createArchiveEntry(source, path)
            output.putArchiveEntry(entry)
            output.write(bytes)
            output.closeArchiveEntry()
            source.delete()
        }
        return archive
    }

    private fun sevenZip(name: String, path: String, bytes: ByteArray): File =
        sevenZip(name, linkedMapOf(path to bytes))

    private fun sevenZip(name: String, entries: LinkedHashMap<String, ByteArray>): File {
        val archive = File(root, name)
        SevenZOutputFile(archive).use { output ->
            output.setContentCompression(SevenZMethod.COPY)
            entries.forEach { (path, bytes) ->
                val source = File(root, "source-${path.hashCode()}").apply { writeBytes(bytes) }
                val entry = output.createArchiveEntry(source, path)
                output.putArchiveEntry(entry)
                output.write(bytes)
                output.closeArchiveEntry()
                source.delete()
            }
        }
        return archive
    }

    private fun sha256(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it.toInt() and 0xff) }
}
