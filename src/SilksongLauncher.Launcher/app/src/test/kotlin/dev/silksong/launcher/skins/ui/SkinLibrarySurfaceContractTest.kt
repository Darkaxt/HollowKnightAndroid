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

    @Test fun `production library advertises enabled controls instead of historical retention blocker`() {
        val strings = xml("res/values/strings.xml").getElementsByTagName("string")
        val guidance = (0 until strings.length).map { strings.item(it) as Element }
            .filter { it.getAttribute("name") in setOf("skins_guidance_hollow_knight", "skins_guidance_silksong") }
        assertEquals(2, guidance.size)
        assertTrue(guidance.all { !it.textContent.contains("changes are unavailable") })
    }

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
            val minHeight = button.getAttributeNS(android, "minHeight")
            assertTrue(minHeight.endsWith("dp") && minHeight.removeSuffix("dp").toInt() >= 48)
            assertEquals("wrap_content", button.getAttributeNS(android, "layout_height"))
            assertTrue(button.getAttributeNS(android, "text").startsWith("@string/"))
        }
        val texts = doc.getElementsByTagName("TextView")
        assertTrue((0 until texts.length).any {
            (texts.item(it) as Element).getAttributeNS(android, "accessibilityLiveRegion") == "polite"
        })
    }

    @Test fun `skins hub hierarchy starts with Back and keeps import configuration then installed packs`() {
        val doc = xml("res/layout/activity_skins.xml")
        assertEquals("ScrollView", doc.documentElement.tagName)
        assertEquals("true", doc.documentElement.getAttributeNS(android, "fillViewport"))
        val content = doc.documentElement.childNodes.let { nodes ->
            (0 until nodes.length).map { nodes.item(it) }.filterIsInstance<Element>().single()
        }
        val ids = (0 until content.childNodes.length).map { content.childNodes.item(it) }
            .filterIsInstance<Element>().map { it.getAttributeNS(android, "id") }
        assertEquals("@+id/skins_back", ids.first())
        assertTrue(ids.indexOf("@+id/skins_title") < ids.indexOf("@+id/skins_import"))
        assertTrue(ids.indexOf("@+id/skins_import") < ids.indexOf("@+id/skins_prepared_section"))
        assertTrue(ids.indexOf("@+id/skins_prepared_section") < ids.indexOf("@+id/skins_configuration_card"))
        assertTrue(ids.indexOf("@+id/skins_configuration_card") < ids.indexOf("@+id/skins_installed_title"))
        assertTrue(ids.indexOf("@+id/skins_installed_title") < ids.indexOf("@+id/skins_packs"))
        val buttons = doc.getElementsByTagName("Button")
        val backButton = (0 until buttons.length).map { buttons.item(it) as Element }.single {
            it.getAttributeNS(android, "id") == "@+id/skins_back"
        }
        assertEquals("48dp", backButton.getAttributeNS(android, "minHeight"))
        assertEquals(1, backButton.getElementsByTagName("requestFocus").length)
        val importButton = (0 until buttons.length).map { buttons.item(it) as Element }.single {
            it.getAttributeNS(android, "id") == "@+id/skins_import"
        }
        assertEquals("match_parent", importButton.getAttributeNS(android, "layout_width"))
        assertEquals("56dp", importButton.getAttributeNS(android, "minHeight"))
    }

    @Test fun `launcher Skins layouts expose package management controls only`() {
        val activity = xml("res/layout/activity_skins.xml")
        val activityButtons = activity.getElementsByTagName("Button")
        val activityIds = (0 until activityButtons.length).map {
            (activityButtons.item(it) as Element).getAttributeNS(android, "id")
        }
        assertTrue("Import must remain reachable", "@+id/skins_import" in activityIds)
        assertTrue("Prepared imports must remain committable", "@+id/skins_import_all" in activityIds)
        assertTrue("Package/status details must remain reachable", "@+id/skins_library_details" in activityIds)
        assertFalse("Launcher must not mutate runtime mode", "@+id/skins_advance_mode" in activityIds)

        val pack = xml("res/layout/item_skin_pack.xml")
        val packButtons = pack.getElementsByTagName("Button")
        val packIds = (0 until packButtons.length).map {
            (packButtons.item(it) as Element).getAttributeNS(android, "id")
        }
        assertEquals(
            "Installed cards must offer only deletion and package details/replacement",
            listOf("@+id/skin_pack_delete", "@+id/skin_pack_details"),
            packIds,
        )
    }

    @Test fun `skins cards use labeled touch actions and prepared state is conditional`() {
        val activity = xml("res/layout/activity_skins.xml")
        val prepared = (0 until activity.getElementsByTagName("LinearLayout").length)
            .map { activity.getElementsByTagName("LinearLayout").item(it) as Element }.single {
                it.getAttributeNS(android, "id") == "@+id/skins_prepared_section"
            }
        assertEquals("gone", prepared.getAttributeNS(android, "visibility"))

        for (layout in listOf("item_skin_pack.xml", "item_skin_candidate.xml")) {
            val doc = xml("res/layout/$layout")
            val buttons = doc.getElementsByTagName("Button")
            assertTrue("$layout needs labeled actions", buttons.length > 0)
            for (index in 0 until buttons.length) {
                val button = buttons.item(index) as Element
                assertEquals("48dp", button.getAttributeNS(android, "minHeight"))
                assertTrue(button.getAttributeNS(android, "text").startsWith("@string/"))
            }
        }
    }

    @Test fun `profile guidance is concise and main surface has only polite live regions`() {
        val strings = xml("res/values/strings.xml").getElementsByTagName("string")
        val values = (0 until strings.length).map { strings.item(it) as Element }.associate {
            it.getAttribute("name") to it.textContent
        }
        assertTrue(values.getValue("skins_guidance_hollow_knight").contains("CustomKnight"))
        assertFalse(values.getValue("skins_guidance_silksong").contains("11 exact"))
        assertTrue(values.getValue("skins_guidance_silksong").length < 240)

        val doc = xml("res/layout/activity_skins.xml")
        val live = doc.getElementsByTagName("TextView")
        val liveIds = (0 until live.length).map { live.item(it) as Element }.filter {
            it.hasAttributeNS(android, "accessibilityLiveRegion")
        }.associate { it.getAttributeNS(android, "id") to it.getAttributeNS(android, "accessibilityLiveRegion") }
        assertEquals(mapOf("@+id/skins_notice" to "polite", "@+id/skins_runtime" to "polite"), liveIds)
    }

    @Test fun `settings contains skins and mods beside existing logs surface`() {
        val buttons = xml("res/layout/activity_settings.xml").getElementsByTagName("Button")
        val ids = (0 until buttons.length).map { (buttons.item(it) as Element).getAttributeNS(android, "id") }
        assertTrue("Missing Skins entry", "@+id/btn_settings_skins" in ids)
        assertTrue("Missing Mods entry", "@+id/btn_settings_mods" in ids)
        assertTrue("@+id/btn_settings_logs" in ids)
    }
}
