package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FastSkinFileSystem
import dev.silksong.launcher.skins.quota.SkinAllocatedBytes
import dev.silksong.launcher.skins.quota.SkinAllocatedBytesAuthority
import dev.silksong.launcher.skins.quota.SkinQuota
import dev.silksong.launcher.skins.quota.SkinQuotaCapacityReserver
import dev.silksong.launcher.skins.quota.SkinQuotaLimits
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
        root = File("build/test-skin-quarantine/hollow-knight").absoluteFile
        root.parentFile?.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
    }

    @After fun tearDown() { root.parentFile?.deleteRecursively() }

    @Test
    fun failedProviderAndTransientDeleteReconcileAfterHandleOwnerRemoval() {
        paths.root.mkdirs()
        val delegate = FastSkinFileSystem()
        var failDelete = true
        var aliasAncestor = false
        val fs = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by delegate {
            override fun isSymbolicLink(file: File): Boolean =
                (aliasAncestor && file.absoluteFile.normalize() == paths.importHandles.absoluteFile.normalize()) ||
                    delegate.isSymbolicLink(file)

            override fun deleteContained(path: File, owner: File) {
                if (failDelete && path.name.startsWith("quarantine-")) {
                    failDelete = false
                    throw IOException("transient empty quarantine deletion failure")
                }
                delegate.deleteContained(path, owner)
            }
        }
        val quota = SkinQuota.testing(paths.root, delegate,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            SkinQuotaLimits(profileBytes = 4096, sessionBytes = 4096))
        val capacity = SkinQuotaCapacityReserver(quota)
        val quarantine = SkinQuarantine(paths, fs, capacity, SkinLimits.V1.copy(quarantineBytes = 4096))
        val staging = dev.silksong.launcher.skins.registry.SkinImportHandleStaging(paths, fs)
        val owner = staging.createOwner(java.util.UUID.randomUUID())
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(SkinImportInput.SelectedFile("failed.zip") { throw IOException("provider failed") },
                owner) as SkinResult.Error).code)
        assertTrue(owner.listFiles().orEmpty().single().isDirectory)
        // This is the coordinator's outer failure cleanup, after quarantine retained its reservation.
        staging.cleanup(owner)
        assertFalse(owner.exists())
        var unsafeOpens = 0
        val unsafeRetry = SkinImportInput.SelectedFile("blocked.zip") {
            unsafeOpens++
            error("Ambiguous ancestor must block before provider access")
        }
        aliasAncestor = true
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(unsafeRetry, owner) as SkinResult.Error).code)
        aliasAncestor = false
        val savedAncestor = File(paths.staging, "saved-import-handles")
        val originalAncestorKey = delegate.identity(paths.importHandles).fileKey
        delegate.atomicMove(paths.importHandles, savedAncestor)
        delegate.createDirectory(paths.importHandles)
        assertTrue("Actual replacement must have a different node identity",
            originalAncestorKey != delegate.identity(paths.importHandles).fileKey)
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(unsafeRetry, owner) as SkinResult.Error).code)
        assertEquals(0, unsafeOpens)
        delegate.deleteContained(paths.importHandles, paths.staging)
        delegate.atomicMove(savedAncestor, paths.importHandles)
        val retryOwner = staging.createOwner(java.util.UUID.randomUUID())
        var opens = 0
        val retry = quarantine.copy(SkinImportInput.SelectedFile("retry.zip") {
            opens++
            ByteArrayInputStream(byteArrayOf(0x50, 0x4b, 3, 4))
        }, retryOwner)
        assertTrue("Expected reconciled reservation and successful retry, got $retry", retry is SkinResult.Ok)
        assertEquals(1, opens)
        staging.cleanup(retryOwner)
    }

    @Test
    fun windowsWeakAncestorIdentityFailsClosedInsteadOfReleasingReservation() {
        org.junit.Assume.assumeTrue(System.getProperty("os.name").orEmpty().startsWith("Windows"))
        paths.quarantine.mkdirs()
        val delegate = AndroidSkinFileSystem()
        val before = delegate.identity(paths.quarantine).fileKey
        org.junit.Assume.assumeTrue(before.contains("windows-host:"))
        var blockDelete = true
        val fs = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                if (blockDelete) throw IOException("retain empty quarantine")
                delegate.deleteContained(path, owner)
            }
        }
        val reservation = RecordingReservation(mutableListOf())
        var reservations = 0
        val capacity = SkinCapacityReserver { reservations++; SkinResult.Ok(reservation) }
        val quarantine = SkinQuarantine(paths, fs, capacity)
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(SkinImportInput.SelectedFile("failed.zip") { throw IOException("provider failed") })
                as SkinResult.Error).code)
        val after = delegate.identity(paths.quarantine).fileKey
        assertTrue("Windows fallback includes mutable directory metadata: $before -> $after", before != after)
        assertFalse(reservation.released)
        assertTrue(reservation.transfers.isEmpty())
        blockDelete = false
        var opens = 0
        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(SkinImportInput.SelectedFile("blocked.zip") { opens++; error("must not reopen") })
                as SkinResult.Error).code)
        assertEquals(0, opens)
        assertEquals(1, reservations)
        assertFalse(reservation.released)
    }

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
    fun `cleanup failure transfers reservation to exact remaining archive evidence`() {
        // Stable physical-node model; Windows AndroidSkinFileSystem uses a mutable weak identity fallback.
        val delegate = FastSkinFileSystem()
        var releaseAttempted = false
        var transferAttempted = false
        val reservation = object : SkinCapacityReservation {
            override fun transfer(file: File, actualBytes: Long) {
                transferAttempted = true
            }
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
        assertTrue(transferAttempted)
        assertFalse(releaseAttempted)
        assertTrue(paths.quarantine.walkTopDown().any { it.name == "archive" })
    }

    @Test
    fun `failed quarantine deletion barrier releases reservation after exact absence`() {
        // Stable physical-node model; weak Windows ancestor identities must instead fail closed.
        val delegate = FastSkinFileSystem()
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
        assertTrue(releaseAttempted)
        assertNoQuarantineNodes()
    }

    @Test
    fun `ambiguous cleanup retains a reachable owner and retries before reserving again`() {
        val delegate = FastSkinFileSystem()
        var cleanupBlocked = true
        var archiveEvidenceAmbiguous = true
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                if (cleanupBlocked) throw IllegalStateException("delete blocked")
                delegate.deleteContained(path, owner)
            }

            override fun isRegularFile(file: File): Boolean {
                if (archiveEvidenceAmbiguous && file.name == "archive") {
                    throw IllegalStateException("archive evidence unavailable")
                }
                return delegate.isRegularFile(file)
            }
        }
        val reservations = mutableListOf<RecordingReservation>()
        val capacity = SkinCapacityReserver {
            SkinResult.Ok(RecordingReservation(mutableListOf()).also(reservations::add))
        }
        val quarantine = SkinQuarantine(paths, failing, capacity)

        assertEquals(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            (quarantine.copy(
                SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertEquals(1, reservations.size)
        assertFalse(reservations.single().released)
        assertTrue(reservations.single().transfers.isEmpty())

        cleanupBlocked = false
        archiveEvidenceAmbiguous = false
        val retry = quarantine.copy(
            SkinImportInput.SelectedFile("invalid-again.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )

        assertEquals(SkinImportCode.INVALID_INPUT, (retry as SkinResult.Error).code)
        assertEquals(2, reservations.size)
        assertTrue(reservations.all(RecordingReservation::released))
        assertNoQuarantineNodes()
    }

    @Test
    fun `generic equal reservers keep distinct object reconciliation identities`() {
        class EqualCapacity : SkinCapacityReserver {
            val reservations = mutableListOf<RecordingReservation>()

            override fun reserve(bytes: Long): SkinResult<SkinCapacityReservation> =
                SkinResult.Ok(RecordingReservation(mutableListOf()).also(reservations::add))

            override fun equals(other: Any?): Boolean = other is EqualCapacity
            override fun hashCode(): Int = 1
        }

        val delegate = FastSkinFileSystem()
        var cleanupBlocked = true
        var archiveEvidenceAmbiguous = true
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                if (cleanupBlocked) throw IllegalStateException("delete blocked")
                delegate.deleteContained(path, owner)
            }

            override fun isRegularFile(file: File): Boolean {
                if (archiveEvidenceAmbiguous && file.name == "archive") {
                    throw IllegalStateException("archive evidence unavailable")
                }
                return delegate.isRegularFile(file)
            }
        }
        val firstCapacity = EqualCapacity()
        val secondCapacity = EqualCapacity()
        val firstQuarantine = SkinQuarantine(paths, failing, firstCapacity)

        assertEquals(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            (firstQuarantine.copy(
                SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertEquals(
            SkinImportCode.INVALID_INPUT,
            (SkinQuarantine(paths, delegate, secondCapacity).copy(
                SkinImportInput.SelectedFile("unrelated.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertFalse(firstCapacity.reservations.single().released)

        cleanupBlocked = false
        archiveEvidenceAmbiguous = false
        assertEquals(
            SkinImportCode.INVALID_INPUT,
            (firstQuarantine.copy(
                SkinImportInput.SelectedFile("retry.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertTrue(firstCapacity.reservations.first().released)
    }

    @Test
    fun `reconstructed real adapter reconciles ambiguous cleanup by physical root identity`() {
        val delegate = FastSkinFileSystem()
        var cleanupBlocked = true
        var archiveEvidenceAmbiguous = true
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                if (cleanupBlocked) throw IllegalStateException("delete blocked")
                delegate.deleteContained(path, owner)
            }

            override fun isRegularFile(file: File): Boolean {
                if (archiveEvidenceAmbiguous && file.name == "archive") {
                    throw IllegalStateException("archive evidence unavailable")
                }
                return delegate.isRegularFile(file)
            }
        }
        val quotaLimits = SkinQuotaLimits(
            profileBytes = 4096,
            sessionBytes = 4096,
            allocationBlockBytes = 4096,
        )
        val firstQuota = SkinQuota.testing(
            paths.root,
            delegate,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            quotaLimits,
        )
        val smallLimits = SkinLimits.V1.copy(quarantineBytes = 4096)

        assertEquals(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            (SkinQuarantine(paths, failing, SkinQuotaCapacityReserver(firstQuota), smallLimits).copy(
                SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )

        cleanupBlocked = false
        archiveEvidenceAmbiguous = false
        val aliasRoot = File(paths.root.parentFile, "ignored/../skins")
        val reconstructedQuota = SkinQuota.testing(
            aliasRoot,
            delegate,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            quotaLimits,
        )
        val retry = SkinQuarantine(
            paths,
            delegate,
            SkinQuotaCapacityReserver(reconstructedQuota),
            smallLimits,
        ).copy(
            SkinImportInput.SelectedFile("invalid-again.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )

        assertEquals(SkinImportCode.INVALID_INPUT, (retry as SkinResult.Error).code)
        assertNoQuarantineNodes()
    }

    @Test
    fun `failed deletion rebases reservation to remaining evidence so the next reservation does not leak`() {
        paths.root.mkdirs()
        val delegate = FastSkinFileSystem()
        var failDeletion = true
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun deleteContained(path: File, owner: File) {
                if (failDeletion) {
                    failDeletion = false
                    throw IllegalStateException("delete failed")
                }
                delegate.deleteContained(path, owner)
            }
        }
        val quota = SkinQuota.testing(
            paths.root,
            delegate,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            SkinQuotaLimits(profileBytes = 8192, sessionBytes = 8192),
        )
        val capacity = SkinQuotaCapacityReserver(quota)
        val smallLimits = SkinLimits.V1.copy(quarantineBytes = 4096)

        assertEquals(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            (SkinQuarantine(paths, failing, capacity, smallLimits).copy(
                SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertTrue(paths.quarantine.walkTopDown().any { it.name == "archive" })

        val retry = SkinQuarantine(paths, delegate, capacity, smallLimits).copy(
            SkinImportInput.SelectedFile("invalid-again.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )
        assertEquals(SkinImportCode.INVALID_INPUT, (retry as SkinResult.Error).code)
    }

    @Test
    fun `failed deletion barrier releases absent evidence so the next reservation can retry`() {
        paths.root.mkdirs()
        val delegate = FastSkinFileSystem()
        var failBarrier = true
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun syncDirectory(path: File) {
                if (failBarrier && path.absoluteFile.normalize() == paths.quarantine.absoluteFile.normalize()) {
                    failBarrier = false
                    throw IllegalStateException("barrier failed")
                }
                delegate.syncDirectory(path)
            }
        }
        val quota = SkinQuota.testing(
            paths.root,
            delegate,
            SkinAllocatedBytesAuthority { SkinAllocatedBytes.Unavailable },
            SkinQuotaLimits(profileBytes = 4096, sessionBytes = 4096),
        )
        val capacity = SkinQuotaCapacityReserver(quota)
        val smallLimits = SkinLimits.V1.copy(quarantineBytes = 4096)

        assertEquals(
            SkinImportCode.DURABILITY_UNAVAILABLE,
            (SkinQuarantine(paths, failing, capacity, smallLimits).copy(
                SkinImportInput.SelectedFile("invalid.bin") { ByteArrayInputStream("invalid".toByteArray()) },
            ) as SkinResult.Error).code,
        )
        assertNoQuarantineNodes()

        val retry = SkinQuarantine(paths, delegate, capacity, smallLimits).copy(
            SkinImportInput.SelectedFile("invalid-again.bin") { ByteArrayInputStream("invalid".toByteArray()) },
        )
        assertEquals(SkinImportCode.INVALID_INPUT, (retry as SkinResult.Error).code)
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
