package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.File
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.Paths
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

class SkinFileSystemContainmentTest {
    private lateinit var root: File
    private lateinit var owner: File
    private val fs = AndroidSkinFileSystem()

    @Before fun setUp() {
        root = File("build/test-skin-containment").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        owner = File(root, "profile").apply { mkdirs() }
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun `rejects lexical escape final alias and ancestor alias`() {
        assertThrows(Exception::class.java) { fs.requireContained(File(owner, "../escape"), owner, true) }
        val outside = File(root, "outside").apply { mkdirs() }
        val finalLink = File(owner, "final-link")
        val ancestorLink = File(owner, "ancestor-link")
        val links = runCatching {
            Files.createSymbolicLink(finalLink.toPath(), outside.toPath())
            Files.createSymbolicLink(ancestorLink.toPath(), outside.toPath())
        }.isSuccess
        assumeTrue(links)
        assertThrows(Exception::class.java) { fs.requireContained(finalLink, owner) }
        assertThrows(Exception::class.java) { fs.requireContained(File(ancestorLink, "child"), owner, true) }
    }

    @Test
    fun `bounded cleanup rejects hardlink reuse before deleting any node`() {
        val target = File(owner, "cleanup").apply { mkdirs() }
        val first = File(target, "first").apply { writeText("keep") }
        val second = File(target, "second")
        assumeTrue(runCatching { Files.createLink(second.toPath(), first.toPath()) }.isSuccess)

        assertThrows(Exception::class.java) { fs.deleteContained(target, owner) }
        assertTrue(first.exists())
        assertTrue(second.exists())
    }

    @Test
    fun `bounded cleanup never traverses an escape alias`() {
        val outside = File(root, "outside.txt").apply { writeText("keep") }
        val target = File(owner, "cleanup").apply { mkdirs() }
        val link = File(target, "escape")
        assumeTrue(runCatching { Files.createSymbolicLink(link.toPath(), outside.toPath()) }.isSuccess)

        assertThrows(Exception::class.java) { fs.deleteContained(target, owner) }
        assertTrue(outside.exists())
        assertTrue(target.exists())
    }

    @Test
    fun `rejects same-device nested mount and unavailable or unstable mount identity`() {
        val nested = File(owner, "nested").apply { mkdirs() }
        val child = File(nested, "child").apply { writeText("payload") }
        val ownerPath = owner.toPath().toAbsolutePath().normalize()
        val nestedPath = nested.toPath().toAbsolutePath().normalize()
        val sameDeviceBind = SkinMountIdentityProvider { path ->
            SkinMountIdentity(
                device = "device-7",
                mountId = if (path.toAbsolutePath().normalize().startsWith(nestedPath)) "bind-22" else "profile-11",
            )
        }
        assertThrows(Exception::class.java) {
            AndroidSkinFileSystem(sameDeviceBind).requireContained(child, owner)
        }

        assertThrows(Exception::class.java) {
            AndroidSkinFileSystem(SkinMountIdentityProvider { null }).requireContained(child, owner)
        }

        var ownerReads = 0
        val unstable = SkinMountIdentityProvider { path: Path ->
            if (path.toAbsolutePath().normalize() == ownerPath) {
                SkinMountIdentity("device-7", if (++ownerReads == 1) "profile-11" else "profile-12")
            } else {
                SkinMountIdentity("device-7", "profile-11")
            }
        }
        assertThrows(Exception::class.java) { AndroidSkinFileSystem(unstable).requireContained(child, owner) }
    }

    @Test
    fun `mountinfo parser selects one exact longest mount and verifies the real device`() {
        val rows = listOf(
            "10 1 8:1 / / rw - ext4 /dev/root rw",
            "11 10 8:1 /bind /profile/nested rw - ext4 /dev/root rw",
        ).joinToString("\n", postfix = "\n").toByteArray()
        val selected = SkinMountInfoParser.select(Paths.get("/profile/nested/file"), 2049L, rows)

        assertEquals("2049|8:1", selected?.device)
        assertTrue(selected?.mountId.orEmpty().startsWith("11|8:1|/bind|"))
        assertEquals(null, SkinMountInfoParser.select(Paths.get("/profile/nested/file"), 2050L, rows))

        val ambiguous = rows + "12 10 8:1 /other /profile/nested rw - ext4 /dev/root rw\n".toByteArray()
        assertEquals(null, SkinMountInfoParser.select(Paths.get("/profile/nested/file"), 2049L, ambiguous))
        assertThrows(Exception::class.java) {
            SkinMountInfoParser.select(Paths.get("/profile/nested/file"), 2049L, byteArrayOf(0xff.toByte()))
        }
        assertThrows(Exception::class.java) {
            SkinMountInfoParser.select(Paths.get("/profile/nested/file"), 2049L, "broken\n".toByteArray())
        }
    }

    @Test
    fun `rejects an injected filesystem without the security capability`() {
        val stage = File(owner, "bare-stage").apply {
            mkdirs()
            File(this, "payload").writeText("payload")
        }
        val destination = File(owner, "objects/bare")
        val bare = object : SkinFileSystem by fs {}

        val result = DurableDirectoryPublisher(bare).publishDetailed(stage, destination, owner) {
            SkinResult.Ok(Unit)
        }

        assertTrue(result is SkinResult.Error)
        assertFalse(destination.exists())
        assertTrue(stage.exists())
    }

    @Test
    fun `fake ancestor mount device null and identity flip evidence all fail closed`() {
        for (reason in listOf("ancestor alias", "mount change", "device change", "null identity")) {
            val stage = File(owner, "stage-${reason.hashCode()}").apply {
                mkdirs()
                File(this, "payload").writeText("payload")
            }
            val destination = File(owner, "objects/${reason.hashCode()}")
            val rejecting = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
                override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
                    throw IllegalStateException(reason)
                }
            }
            val result = DurableDirectoryPublisher(rejecting).publishDetailed(stage, destination, owner) { SkinResult.Ok(Unit) }
            assertTrue(result is SkinResult.Error)
            assertFalse(destination.exists())
            assertTrue(stage.exists())
        }

        val stage = File(owner, "identity-stage").apply {
            mkdirs()
            File(this, "payload").writeText("payload")
        }
        val changing = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            private var payloadReads = 0
            override fun identity(path: File): SkinNodeIdentity {
                val value = fs.identity(path)
                if (path.name == "payload" && ++payloadReads > 1) return value.copy(fileKey = "flipped")
                return value
            }
        }
        val result = DurableDirectoryPublisher(changing).publishDetailed(stage, File(owner, "objects/flip"), owner) {
            SkinResult.Ok(Unit)
        }
        assertTrue(result is SkinResult.Error)
        assertFalse(File(owner, "objects/flip").exists())
    }
}
