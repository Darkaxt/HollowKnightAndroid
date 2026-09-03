package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture.LocalTuple
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import java.io.File
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class BoundedZipReaderTest {
    private lateinit var root: File
    private var next = 0

    @Before fun setUp() {
        root = File("build/test-bounded-zip").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun `bit3 clear requires a full local tuple while bit3 set accepts zero or full 12 and 16 byte descriptors`() {
        for (method in listOf(0, 8)) {
            assertOk(RawZipFixture.one(method = method, descriptor = false, localTuple = LocalTuple.FULL))
            assertError(
                RawZipFixture.one(method = method, descriptor = false, localTuple = LocalTuple.ZERO),
                SkinImportCode.ZIP_CORRUPT,
            )
            for (tuple in listOf(LocalTuple.ZERO, LocalTuple.FULL)) {
                assertOk(RawZipFixture.one(method = method, descriptor = true, descriptorSignature = false, localTuple = tuple))
                assertOk(RawZipFixture.one(method = method, descriptor = true, descriptorSignature = true, localTuple = tuple))
            }
        }
    }

    @Test
    fun rejectsAbsentUnexpectedDuplicateAndTruncatedDescriptors() {
        val unsigned = RawZipFixture.one(descriptor = true, descriptorSignature = false)
        val signed = RawZipFixture.one(descriptor = true, descriptorSignature = true)
        val signedStart = localDataEnd(signed)
        val signedBytes = signed.bytes.copyOfRange(signedStart, signed.centralOffset)

        val absent = replaceLocalTail(unsigned, ByteArray(0))
        val truncated = replaceLocalTail(signed, signedBytes.copyOf(signedBytes.size - 1))
        val duplicate = replaceLocalTail(signed, signedBytes + signedBytes)
        val unexpected = RawZipFixture.one(
            descriptor = true,
            descriptorSignature = true,
            localTuple = LocalTuple.FULL,
        ).let { built ->
            val localFlags = RawZipFixture.patchLe16(built.bytes, built.localOffsets.single() + 6, 0x0800)
            built.copy(bytes = RawZipFixture.patchLe16(localFlags, built.centralOffset + 8, 0x0800))
        }

        listOf(absent, truncated, duplicate, unexpected).forEach {
            assertError(it, SkinImportCode.ZIP_CORRUPT)
        }
    }

    @Test
    fun `rejects a descriptor window that is simultaneously signed and unsigned`() {
        val word = byteArrayOf(0x50, 0x4b, 0x07, 0x08)
        val result = BoundedZipReader.classifyDescriptor(
            word + word + word + word,
            BoundedZipReader.DESCRIPTOR_SIGNATURE,
            BoundedZipReader.DESCRIPTOR_SIGNATURE,
            BoundedZipReader.DESCRIPTOR_SIGNATURE,
        )

        assertEquals(SkinImportCode.ZIP_CORRUPT, (result as SkinResult.Error).code)
    }

    @Test
    fun `declared and streamed CRC size and ratio checks are independent`() {
        val crc = RawZipFixture.one(data = "payload".toByteArray()).let { built ->
            val local = RawZipFixture.patchLe32(built.bytes, built.localOffsets.single() + 14, 1)
            built.copy(bytes = RawZipFixture.patchLe32(local, built.centralOffset + 16, 1))
        }
        assertError(crc, SkinImportCode.ZIP_CORRUPT)

        val streamedSize = RawZipFixture.one(data = "payload".toByteArray(), method = 8).let { built ->
            val declared = "payload".length + 1L
            val local = RawZipFixture.patchLe32(built.bytes, built.localOffsets.single() + 22, declared)
            built.copy(bytes = RawZipFixture.patchLe32(local, built.centralOffset + 24, declared))
        }
        assertError(streamedSize, SkinImportCode.ZIP_CORRUPT)
        assertError(RawZipFixture.one(data = ByteArray(20_000), method = 8), SkinImportCode.LIMIT_EXCEEDED)

        val declaredTotal = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("A.bin".toByteArray(), ByteArray(6)),
                RawZipFixture.Entry("B.bin".toByteArray(), ByteArray(6)),
            ),
        )
        assertError(declaredTotal, SkinImportCode.LIMIT_EXCEEDED, SkinLimits.V1.copy(uncompressedBytes = 10))

        val streamedBound = RawZipFixture.one(data = ByteArray(100), method = 8).let { built ->
            val local = RawZipFixture.patchLe32(built.bytes, built.localOffsets.single() + 22, 50)
            built.copy(bytes = RawZipFixture.patchLe32(local, built.centralOffset + 24, 50))
        }
        assertError(streamedBound, SkinImportCode.LIMIT_EXCEEDED, SkinLimits.V1.copy(uncompressedBytes = 60))

        val compressedTrailingByte = appendCompressedByte(RawZipFixture.one(data = "payload".toByteArray(), method = 8))
        assertError(compressedTrailingByte, SkinImportCode.ZIP_CORRUPT)
    }

    @Test
    fun `rejects malformed forbidden and zip64 extras plus impossible payload ranges`() {
        val malformed = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1), centralExtra = byteArrayOf(1, 0, 5, 0))),
        )
        assertError(malformed, SkinImportCode.ZIP_CORRUPT)
        val unicodePath = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1), centralExtra = byteArrayOf(0x75, 0x70, 0, 0))),
        )
        assertError(unicodePath, SkinImportCode.PATH_REJECTED)
        val zip64Extra = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1), centralExtra = byteArrayOf(1, 0, 0, 0))),
        )
        assertError(zip64Extra, SkinImportCode.UNSUPPORTED_ZIP)

        val range = RawZipFixture.one().let { built ->
            val local = RawZipFixture.patchLe32(built.bytes, built.localOffsets.single() + 18, 1000)
            built.copy(bytes = RawZipFixture.patchLe32(local, built.centralOffset + 20, 1000))
        }
        assertError(range, SkinImportCode.ZIP_CORRUPT)
    }

    @Test
    fun acceptsStoreAndDeflateDescriptorMatrix() {
        for (method in listOf(0, 8)) {
            assertOk(RawZipFixture.one(method = method))
            for (signature in listOf(false, true)) {
                for (tuple in listOf(LocalTuple.ZERO, LocalTuple.FULL)) {
                    assertOk(
                        RawZipFixture.one(
                            method = method,
                            descriptor = true,
                            descriptorSignature = signature,
                            localTuple = tuple,
                        ),
                    )
                }
            }
        }
    }

    @Test
    fun rejectsDescriptorTupleAndZipStructureMatrix() {
        for (tuple in listOf(LocalTuple.PARTIAL, LocalTuple.MISMATCH, LocalTuple.SENTINEL)) {
            assertError(RawZipFixture.one(descriptor = true, localTuple = tuple), SkinImportCode.ZIP_CORRUPT)
        }

        val descriptor = RawZipFixture.one(descriptor = true, descriptorSignature = true)
        assertError(descriptor.copy(bytes = descriptor.bytes.copyOf(descriptor.bytes.size - 1)), SkinImportCode.ZIP_CORRUPT)

        val multiDisk = RawZipFixture.one().let {
            it.copy(bytes = RawZipFixture.patchLe16(it.bytes, it.eocdOffset + 4, 1))
        }
        assertError(multiDisk, SkinImportCode.UNSUPPORTED_ZIP)

        val zip64 = RawZipFixture.one().let {
            it.copy(bytes = RawZipFixture.patchLe32(it.bytes, it.centralOffset + 20, 0xffffffffL))
        }
        assertError(zip64, SkinImportCode.UNSUPPORTED_ZIP)

        val overlap = RawZipFixture.one().let {
            it.copy(bytes = RawZipFixture.patchLe32(it.bytes, it.centralOffset + 42, (it.centralOffset - 2).toLong()))
        }
        assertError(overlap, SkinImportCode.ZIP_CORRUPT)
    }

    @Test
    fun enforcesDeclaredAndStreamedRatioWithoutOverflow() {
        val bomb = RawZipFixture.one(data = ByteArray(20_000), method = 8)
        assertError(bomb, SkinImportCode.LIMIT_EXCEEDED)

        val limits = SkinLimits.V1.copy(uncompressedBytes = Long.MAX_VALUE)
        val overRatio = declaredSizes(RawZipFixture.one(), compressed = 42_949_672L, uncompressed = 0xffff_fffeL)
        assertError(overRatio, SkinImportCode.LIMIT_EXCEEDED, limits)
        val atRatio = declaredSizes(RawZipFixture.one(), compressed = 42_949_673L, uncompressed = 0xffff_fffeL)
        assertError(atRatio, SkinImportCode.ZIP_CORRUPT, limits)

        val empty = RawZipFixture.one(data = ByteArray(0), method = 0)
        assertOk(empty)
    }

    @Test
    fun `rejects a nonzero CRC on an empty directory payload`() {
        val directory = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Pack/".toByteArray(), ByteArray(0))),
        )
        val localPatched = RawZipFixture.patchLe32(directory.bytes, directory.localOffsets.single() + 14, 1)
        val patched = directory.copy(
            bytes = RawZipFixture.patchLe32(localPatched, directory.centralOffset + 16, 1),
        )

        assertError(patched, SkinImportCode.ZIP_CORRUPT)
    }

    @Test
    fun `enforces local name bounds before reading local fields`() {
        val built = RawZipFixture.one()
        val oversizedLocalName = built.copy(
            bytes = RawZipFixture.patchLe16(built.bytes, built.localOffsets.single() + 26, 513),
        )
        assertError(oversizedLocalName, SkinImportCode.PATH_REJECTED)
    }

    @Test
    fun `rejects CRC mismatch duplicate local ranges and trailing bytes`() {
        val valid = RawZipFixture.one()
        val crcMismatch = valid.copy(bytes = RawZipFixture.patchLe32(valid.bytes, valid.centralOffset + 16, 1))
        assertError(crcMismatch, SkinImportCode.ZIP_CORRUPT)

        val duplicated = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1)),
                RawZipFixture.Entry("Other.png".toByteArray(), byteArrayOf(2)),
            ),
        ).let { built ->
            val secondCentral = built.centralOffset + 46 + "Knight.png".length
            built.copy(bytes = RawZipFixture.patchLe32(built.bytes, secondCentral + 42, 0))
        }
        assertError(duplicated, SkinImportCode.ZIP_CORRUPT)

        val trailing = valid.copy(bytes = valid.bytes + byteArrayOf(0))
        assertError(trailing, SkinImportCode.ZIP_CORRUPT)
    }

    @Test
    fun `no-follow ZIP authority rejects final aliases and identity changes during reads`() {
        val archive = write(RawZipFixture.one().bytes)
        val link = File(root, "archive-link.zip")
        if (runCatching { Files.createSymbolicLink(link.toPath(), archive.toPath()) }.isSuccess) {
            assertTrue(BoundedZipReader().read(link) is SkinResult.Error)
        }

        val delegate = AndroidSkinFileSystem()
        var reads = 0
        val changing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun identity(path: File) = delegate.identity(path).let { identity ->
                if (path.absoluteFile.normalize() == archive.absoluteFile.normalize() && ++reads > 1) {
                    identity.copy(fileKey = "replacement")
                } else {
                    identity
                }
            }
        }
        assertTrue(BoundedZipReader(SkinLimits.V1, changing).read(archive) is SkinResult.Error)

        var seekableOpens = 0
        val tracking = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun openSeekableNoFollow(file: File) =
                delegate.openSeekableNoFollow(file).also { seekableOpens++ }
        }
        assertTrue(BoundedZipReader(SkinLimits.V1, tracking).read(archive) is SkinResult.Ok)
        assertEquals(1, seekableOpens)
    }

    private fun localDataEnd(built: RawZipFixture.Built): Int {
        val local = built.localOffsets.single()
        val nameLength = BoundedZipReader.le16(built.bytes, local + 26)
        val extraLength = BoundedZipReader.le16(built.bytes, local + 28)
        val compressed = BoundedZipReader.le32(built.bytes, built.centralOffset + 20).toInt()
        return local + 30 + nameLength + extraLength + compressed
    }

    private fun replaceLocalTail(built: RawZipFixture.Built, replacement: ByteArray): RawZipFixture.Built {
        val dataEnd = localDataEnd(built)
        val bytes = built.bytes.copyOfRange(0, dataEnd) + replacement +
            built.bytes.copyOfRange(built.centralOffset, built.bytes.size)
        val delta = dataEnd + replacement.size - built.centralOffset
        val centralOffset = built.centralOffset + delta
        val eocdOffset = built.eocdOffset + delta
        return built.copy(
            bytes = RawZipFixture.patchLe32(bytes, eocdOffset + 16, centralOffset.toLong()),
            centralOffset = centralOffset,
            eocdOffset = eocdOffset,
        )
    }

    private fun appendCompressedByte(built: RawZipFixture.Built): RawZipFixture.Built {
        val compressed = BoundedZipReader.le32(built.bytes, built.centralOffset + 20)
        val extended = replaceLocalTail(built, byteArrayOf(0))
        var bytes = RawZipFixture.patchLe32(
            extended.bytes,
            extended.localOffsets.single() + 18,
            compressed + 1,
        )
        bytes = RawZipFixture.patchLe32(bytes, extended.centralOffset + 20, compressed + 1)
        return extended.copy(bytes = bytes)
    }

    private fun declaredSizes(
        built: RawZipFixture.Built,
        compressed: Long,
        uncompressed: Long,
    ): RawZipFixture.Built {
        var bytes = RawZipFixture.patchLe32(built.bytes, built.localOffsets.single() + 18, compressed)
        bytes = RawZipFixture.patchLe32(bytes, built.localOffsets.single() + 22, uncompressed)
        bytes = RawZipFixture.patchLe32(bytes, built.centralOffset + 20, compressed)
        bytes = RawZipFixture.patchLe32(bytes, built.centralOffset + 24, uncompressed)
        return built.copy(bytes = bytes)
    }

    private fun assertOk(built: RawZipFixture.Built) {
        val result = BoundedZipReader().read(write(built.bytes))
        assertTrue("Expected OK, got $result", result is SkinResult.Ok)
    }

    private fun assertError(
        built: RawZipFixture.Built,
        code: SkinImportCode,
        limits: SkinLimits = SkinLimits.V1,
    ) {
        val result = BoundedZipReader(limits).read(write(built.bytes))
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private fun write(bytes: ByteArray): File = File(root, "archive-${next++}.bin").apply { writeBytes(bytes) }
}
