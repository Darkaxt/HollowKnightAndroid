package dev.silksong.launcher

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import java.io.File

@RunWith(RobolectricTestRunner::class)
class ModsTest {

    @get:Rule
    val temp = TemporaryFolder()

    @Test
    fun `discovery is recursive and excludes the config tree`() {
        val mods = temp.newFolder("mods")
        File(mods, "Top.dll").writeText("top")
        File(mods, "pack/Nested.DLL").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("nested")
        }
        File(mods, "config/NotAPlugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("config")
        }

        assertEquals(
            setOf("Top.dll", "pack${File.separator}Nested.DLL"),
            Mods.all(mods).map { Mods.relativePath(mods, it) }.toSet(),
        )
    }

    @Test
    fun `one shared mod library keeps independent profile build stamps`() {
        val mods = temp.newFolder("shared-mods")
        val plugin = File(mods, "Example.dll").apply { writeText("one") }
        val hollowKnightRoot = temp.newFolder("hollow-knight-build")
        val silksongRoot = temp.newFolder("silksong-build")

        Mods.markCurrent(mods, hollowKnightRoot)

        assertFalse(Mods.isStale(mods, hollowKnightRoot))
        assertTrue(Mods.isStale(mods, silksongRoot))
        plugin.writeText("two")
        assertTrue(Mods.isStale(mods, hollowKnightRoot))
    }

    @Test
    fun `toggle writes the assembly gate without changing the plugin`() {
        val mods = temp.newFolder("toggle-mods")
        val plugin = File(mods, "Example.dll").apply { writeText("plugin") }
        val root = temp.newFolder("toggle-build")
        Mods.reportFile(root).writeText(
            """{"plugins":[{"File":"Example.dll","Assembly":"Example.Plugin","Guid":"example","Name":"Example","Version":"1.0","Status":"Ok","Patched":1,"Issues":[]}]}""",
        )

        Mods.setEnabled(mods, root, "Example.dll", enabled = false)

        assertEquals(setOf("Example.dll"), Mods.disabled(mods))
        assertEquals("Example.Plugin", Mods.gatesFile(mods).readText().trim())
        assertEquals("plugin", plugin.readText())

        Mods.setEnabled(mods, root, "Example.dll", enabled = true)

        assertTrue(Mods.disabled(mods).isEmpty())
        assertFalse(Mods.gatesFile(mods).exists())
        assertEquals("plugin", plugin.readText())
    }
}
