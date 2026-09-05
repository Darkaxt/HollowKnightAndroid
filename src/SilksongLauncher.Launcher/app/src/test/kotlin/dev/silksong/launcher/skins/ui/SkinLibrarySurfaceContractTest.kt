package dev.silksong.launcher.skins.ui

import org.junit.Assert.*
import org.junit.Test
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element

/** Host-only contracts: no Android runtime or provider is started. */
class SkinLibrarySurfaceContractTest {
    private val android = "http://schemas.android.com/apk/res/android"
    private fun xml(path: String) = DocumentBuilderFactory.newInstance().apply {
        isNamespaceAware = true
    }.newDocumentBuilder().parse(File("src/main/$path"))

    @Test fun `controller exposes only a zero argument advance operation`() {
        val type = Class.forName("dev.silksong.launcher.skins.ui.SkinLibraryController")
        val methods = type.declaredMethods.filter { java.lang.reflect.Modifier.isPublic(it.modifiers) }
        assertEquals(listOf("advanceMode"), methods.map { it.name })
        assertEquals(0, methods.single().parameterCount)
        assertEquals("dev.silksong.launcher.skins.contracts.SkinResult", methods.single().returnType.name)
    }

    @Test fun `skins activity is private and in launcher process`() {
        val nodes = xml("AndroidManifest.xml").getElementsByTagName("activity")
        val activity = (0 until nodes.length).map { nodes.item(it) as Element }.singleOrNull {
            it.getAttributeNS(android, "name") == "dev.silksong.launcher.skins.ui.SkinsActivity"
        }
        assertNotNull("SkinsActivity must be registered", activity)
        assertEquals("false", activity!!.getAttributeNS(android, "exported"))
        assertEquals(":launcher", activity.getAttributeNS(android, "process"))
    }

    @Test fun `skins screen scrolls and all buttons have labeled 48dp touch targets`() {
        val file = File("src/main/res/layout/activity_skins.xml")
        assertTrue("Skins layout must exist", file.isFile)
        val doc = xml("res/layout/activity_skins.xml")
        assertEquals("ScrollView", doc.documentElement.tagName)
        assertEquals("true", doc.documentElement.getAttributeNS(android, "fillViewport"))
        val buttons = doc.getElementsByTagName("Button")
        assertTrue(buttons.length >= 5)
        for (index in 0 until buttons.length) {
            val button = buttons.item(index) as Element
            assertEquals("48dp", button.getAttributeNS(android, "minHeight"))
            assertEquals("wrap_content", button.getAttributeNS(android, "layout_height"))
            assertTrue(button.getAttributeNS(android, "text").startsWith("@string/"))
        }
        val texts = doc.getElementsByTagName("TextView")
        assertTrue((0 until texts.length).any {
            (texts.item(it) as Element).getAttributeNS(android, "accessibilityLiveRegion") == "polite"
        })
    }

    @Test fun `settings contains skins and mods beside existing logs surface`() {
        val buttons = xml("res/layout/activity_settings.xml").getElementsByTagName("Button")
        val ids = (0 until buttons.length).map { (buttons.item(it) as Element).getAttributeNS(android, "id") }
        assertTrue("Missing Skins entry", "@+id/btn_settings_skins" in ids)
        assertTrue("Missing Mods entry", "@+id/btn_settings_mods" in ids)
        assertTrue("@+id/btn_settings_logs" in ids)
    }
}
