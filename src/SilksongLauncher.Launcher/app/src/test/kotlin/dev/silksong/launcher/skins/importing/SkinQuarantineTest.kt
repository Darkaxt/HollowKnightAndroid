package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.SkinPaths
import java.io.ByteArrayInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinQuarantineTest {
    private lateinit var root: File
    private lateinit var paths: SkinPaths

    @Before fun setUp() {
        root = File("build/test-skin-quarantine").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun copiesProviderExactlyOnceAndClassifiesMagic() {
        val events = mutableListOf<String>()
        val reservation = RecordingReservation(events)
        val capacity = SkinCapacityReserver { bytes ->
            events += "reserve:$bytes"
            SkinResult.Ok(reservation)
        }
        val bytes = byteArrayOf(0x50, 0x4b, 0x03, 0x04, 1, 2, 3)
        var opens = 0
        val result = SkinQuarantine(paths, AndroidSkinFileSystem(), capacity).copy(
            SkinImportInput.SelectedFile(null) {
                events += "open"
                opens++
                ByteArrayInputStream(bytes)
            },
        )

        assertTrue("Expected quarantine success, got $result", result is SkinResult.Ok)
        val archive = (result as SkinResult.Ok).value
        assertEquals(1, opens)
        assertEquals("reserve:${SkinLimits.V1.quarantineBytes}", events.first())
        assertTrue(events.indexOf("open") > events.indexOfFirst { it.startsWith("reserve:") })
        assertEquals(listOf("transfer:${archive.file.name}:${bytes.size}"), reservation.transfers)
        assertFalse(reservation.released)
        assertTrue(archive.file.toPath().startsWith(paths.staging.toPath()))
        assertArrayEquals(bytes, archive.file.readBytes())
    }

    @Test
    fun `reserve failure performs zero provider opens`() {
        var opens = 0
        val result = SkinQuarantine(
            paths,
            AndroidSkinFileSystem(),
            SkinCapacityReserver { SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "full") },
        ).copy(SkinImportInput.SelectedFile("skin.zip") { opens++; error("must not open") })

        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (result as SkinResult.Error).code)
        assertEquals(0, opens)
        assertNoQuarantineNodes()
    }

    @Test
    fun `rejects containment or alias evidence before open and releases reservation`() {
        val reservation = RecordingReservation(mutableListOf())
        val delegate = AndroidSkinFileSystem()
        val rejecting = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
                throw IllegalStateException("ancestor alias")
            }
        }
        var opens = 0
        val result = SkinQuarantine(paths, rejecting, SkinCapacityReserver { SkinResult.Ok(reservation) }).copy(
            SkinImportInput.SelectedFile("skin.zip") { opens++; ByteArrayInputStream(ByteArray(0)) },
        )

        assertTrue(result is SkinResult.Error)
        assertEquals(0, opens)
        assertTrue(reservation.released)
        assertTrue(reservation.transfers.isEmpty())
        assertNoQuarantineNodes()
    }

    @Test
    fun `copy and sync failures remove contained staging and release reservation`() {
        for (mode in listOf("copy", "sync")) {
            root.deleteRecursively()
            root.mkdirs()
            paths = SkinPaths(root)
            val reservation = RecordingReservation(mutableListOf())
            val delegate = AndroidSkinFileSystem()
            val fs = if (mode == "sync") object :
                SkinFileSystem by delegate,
                SkinFileSystemSecurity by delegate {
                override fun syncFile(file: File) = throw IOException("sync failed")
            } else delegate
            val input = SkinImportInput.SelectedFile("broken.zip") {
                if (mode == "copy") FaultingInputStream() else ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 3, 4))
            }

            val result = SkinQuarantine(paths, fs, SkinCapacityReserver { SkinResult.Ok(reservation) }).copy(input)

            assertTrue("$mode must fail", result is SkinResult.Error)
            assertTrue(reservation.released)
            assertTrue(reservation.transfers.isEmpty())
            assertNoQuarantineNodes()
        }
    }

    @Test
    fun `unsupported and invalid magic release reservation and staging`() {
        val cases = listOf(
            byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00) to SkinImportCode.UNSUPPORTED_RAR,
            byteArrayOf(0x50, 0x4b, 0x03, 0x06) to SkinImportCode.INVALID_INPUT,
        )
        for ((bytes, expected) in cases) {
            val reservation = RecordingReservation(mutableListOf())
            val result = SkinQuarantine(paths, AndroidSkinFileSystem(), SkinCapacityReserver { SkinResult.Ok(reservation) }).copy(
                SkinImportInput.ImmediateFolderFile("skin.bin", "doc") { ByteArrayInputStream(bytes) },
            )
            assertEquals(expected, (result as SkinResult.Error).code)
            assertTrue(reservation.released)
            assertNoQuarantineNodes()
        }
    }

    @Test
    fun `enforces the quarantine bound while streaming and removes partial copy`() {
        val reservation = RecordingReservation(mutableListOf())
        val result = SkinQuarantine(
            paths,
            AndroidSkinFileSystem(),
            SkinCapacityReserver { SkinResult.Ok(reservation) },
            SkinLimits.V1.copy(quarantineBytes = 8),
        ).copy(
            SkinImportInput.ImmediateFolderFile("large.bin", "doc") {
                ByteArrayInputStream(ByteArray(9) { it.toByte() })
            },
        )

        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (result as SkinResult.Error).code)
        assertTrue(reservation.released)
        assertNoQuarantineNodes()
    }

    @Test
    fun `cleanup failure keeps reservation charged while owned bytes may remain`() {
        val delegate = AndroidSkinFileSystem()
        var releaseAttempted = false
        val reservation = object : SkinCapacityReservation {
            override fun transfer(file: File, actualBytes: Long) = Unit
            override fun release() {
                releaseAttempted = true
            }
        }
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                throw IllegalStateException("quarantine cleanup failed")
            }
        }

        val result = SkinQuarantine(paths, failing, SkinCapacityReserver { SkinResult.Ok(reservation) }).copy(
            SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertFalse(releaseAttempted)
        assertTrue(paths.quarantine.walkTopDown().any { it.name == "archive" })
    }

    @Test
    fun `failed quarantine deletion barrier keeps reservation charged`() {
        val delegate = AndroidSkinFileSystem()
        var releaseAttempted = false
        var barrierAttempted = false
        val reservation = object : SkinCapacityReservation {
            override fun transfer(file: File, actualBytes: Long) = Unit
            override fun release() {
                releaseAttempted = true
            }
        }
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun syncDirectory(path: File) {
                if (path.absoluteFile.normalize() == paths.quarantine.absoluteFile.normalize()) {
                    barrierAttempted = true
                    throw IllegalStateException("quarantine deletion barrier failed")
                }
                delegate.syncDirectory(path)
            }
        }

        val result = SkinQuarantine(paths, failing, SkinCapacityReserver { SkinResult.Ok(reservation) }).copy(
            SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertTrue(barrierAttempted)
        assertFalse(releaseAttempted)
        assertNoQuarantineNodes()
    }

    @Test
    fun `pins every V1 import limit`() {
        val value = SkinLimits.V1
        assertEquals(
            listOf<Long>(
                268435456, 4096, 512, 512, 16, 536870912, 100, 128, 64, 205,
                207, 512, 64, 1024, 16777216, 4194304, 268435456, 8192, 33554432,
            ),
            listOf(
                value.quarantineBytes, value.entries.toLong(), value.directories.toLong(),
                value.sourcePathBytes.toLong(), value.sourceDepth.toLong(), value.uncompressedBytes,
                value.expansionRatio.toLong(), value.candidates.toLong(), value.installedPacks.toLong(),
                value.mappings.toLong(), value.regularFiles.toLong(), value.observedNodes.toLong(),
                value.candidateDirectories.toLong(), value.providerRows.toLong(), value.textureBytes,
                value.previewBytes, value.payloadBytes, value.dimension.toLong(), value.decodedPixels,
            ),
        )
    }

    private fun assertNoQuarantineNodes() {
        assertFalse(paths.staging.exists() && paths.staging.walkTopDown().any { it.name.startsWith("quarantine-") })
    }

    private class RecordingReservation(private val events: MutableList<String>) : SkinCapacityReservation {
        var released = false
        val transfers = mutableListOf<String>()
        override fun transfer(file: File, actualBytes: Long) {
            events += "transfer"
            transfers += "transfer:${file.name}:$actualBytes"
        }
        override fun release() {
            events += "release"
            released = true
        }
    }

    private class FaultingInputStream : InputStream() {
        private val prefix = byteArrayOf(0x50, 0x4b, 0x03, 0x04)
        private var index = 0
        override fun read(): Int {
            if (index == prefix.size) throw IOException("copy failed")
            return prefix[index++].toInt() and 0xff
        }
    }
}
