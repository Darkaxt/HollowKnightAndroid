package dev.silksong.launcher

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest

class ModsDisplayCoordinatorTest {

    @get:Rule
    val temp = TemporaryFolder()

    @Test
    fun `display model reads authorities once and digests each plugin once`() {
        val first = File(temp.root, "First.dll")
        val second = File(temp.root, "pack/Second.dll")
        val contents = mapOf(first to "first".toByteArray(), second to "second".toByteArray())
        val source = CountingSource(
            plugins = listOf(first, second),
            contents = contents,
            published = ModsDisplayCoordinator.Published(
                stamp = "stale",
                built = mapOf(
                    "First.dll" to sha256(contents.getValue(first)),
                    "pack/Second.dll" to sha256(contents.getValue(second)),
                ),
                reports = emptyMap(),
            ),
            disabled = setOf("pack/Second.dll"),
        )

        val result = ModsDisplayCoordinator(source).load()
        val model = (result as ModsDisplayCoordinator.Result.Ready).model

        assertEquals(1, source.pluginsCalls)
        assertEquals(1, source.disabledCalls)
        assertEquals(1, source.publishedCalls)
        assertEquals(1, source.assetCalls)
        assertEquals(mapOf(first to 1, second to 1), source.openCalls)
        assertTrue(model.stale)
        assertEquals(listOf(true, true), model.rows.map { it.built })
        assertEquals(listOf(true, false), model.rows.map { it.enabled })
    }

    @Test
    fun `display model surfaces discovery and digest failures`() {
        val discovery = CountingSource(discoveryFailure = IOException("recovery is ambiguous"))
        val discoveryResult = ModsDisplayCoordinator(discovery).load()
        assertTrue(discoveryResult is ModsDisplayCoordinator.Result.Failed)
        assertTrue((discoveryResult as ModsDisplayCoordinator.Result.Failed).message.contains("recovery is ambiguous"))
        assertEquals(1, discovery.pluginsCalls)

        val plugin = File(temp.root, "Broken.dll")
        val hashing = CountingSource(
            plugins = listOf(plugin),
            contents = mapOf(plugin to "broken".toByteArray()),
            digestFailure = IOException("hash read failed"),
        )
        val hashingResult = ModsDisplayCoordinator(hashing).load()
        assertTrue(hashingResult is ModsDisplayCoordinator.Result.Failed)
        assertTrue((hashingResult as ModsDisplayCoordinator.Result.Failed).message.contains("hash read failed"))
        assertEquals(mapOf(plugin to 1), hashing.openCalls)
    }

    private class CountingSource(
        private val plugins: List<File> = emptyList(),
        private val contents: Map<File, ByteArray> = emptyMap(),
        private val published: ModsDisplayCoordinator.Published? = null,
        private val disabled: Set<String> = emptySet(),
        private val discoveryFailure: IOException? = null,
        private val digestFailure: IOException? = null,
    ) : ModsDisplayCoordinator.Source {
        var pluginsCalls = 0
        var disabledCalls = 0
        var publishedCalls = 0
        var assetCalls = 0
        val openCalls = linkedMapOf<File, Int>()

        override fun plugins(): List<File> {
            pluginsCalls++
            discoveryFailure?.let { throw it }
            return plugins
        }

        override fun relativePath(file: File): String =
            if (file.parentFile == file.parentFile?.parentFile) file.name
            else if (file.name == "Second.dll") "pack/Second.dll" else file.name

        override fun open(file: File): InputStream {
            openCalls[file] = openCalls.getOrDefault(file, 0) + 1
            digestFailure?.let { throw it }
            return ByteArrayInputStream(contents.getValue(file))
        }

        override fun appendStampAssets(digest: MessageDigest) {
            assetCalls++
        }

        override fun disabled(): Set<String> {
            disabledCalls++
            return disabled
        }

        override fun published(): ModsDisplayCoordinator.Published? {
            publishedCalls++
            return published
        }
    }

    private fun sha256(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
}
