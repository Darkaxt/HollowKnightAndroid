package dev.silksong.launcher

import android.content.Context
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.view.View
import android.view.ViewGroup
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.SelectedGameStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf

@RunWith(RobolectricTestRunner::class)
class BuiltInModsActivityTest {
    private lateinit var context: Context

    @Before fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        SelectedGameStore(context).set(GameProfiles.require("hollow-knight"))
    }

    @Test fun `plugins remains an explicitly secondary route`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()

        activity.findViewById<Button>(R.id.btn_plugins).performClick()

        assertEquals(ModsActivity::class.java.name, shadowOf(activity).nextStartedActivity.component?.className)
    }

    @Test fun `secondary manager labels its contents as plugins`() {
        val activity = Robolectric.buildActivity(ModsActivity::class.java).setup().get()
        val text = collectText(activity.findViewById(android.R.id.content))

        assertTrue(text.contains("Plugins — Hollow Knight"))
        assertTrue(text.contains("Install a plugin from a folder"))
        assertFalse(text.contains("Mods — Hollow Knight"))
    }

    @Test fun `functional rows are grouped uppercase whole-row controls with value and detail`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        val list = activity.findViewById<LinearLayout>(R.id.builtin_mods_list)

        assertTrue(list.childCount > 7)
        val groupLabels = (0 until list.childCount)
            .mapNotNull { list.getChildAt(it) as? TextView }
            .filter { it.tag == "group" }
            .map { it.text.toString() }
        assertEquals(listOf("PRESENTATION", "COMBAT", "PLAYER"), groupLabels)
        assertFalse(groupLabels.any { it.contains("DEFERRED") })
        assertEquals("MASTER · OFF", activity.findViewById<Button>(R.id.btn_mods_master).text.toString())
        assertTrue(activity.findViewById<TextView>(R.id.txt_mod_detail).text.isNotBlank())
        assertTrue(activity.findViewById<Button>(R.id.btn_reset_mods).text.toString().contains("RESET ALL MODS"))
    }

    private fun collectText(view: View): List<String> = when (view) {
        is TextView -> listOf(view.text.toString())
        is ViewGroup -> (0 until view.childCount).flatMap { collectText(view.getChildAt(it)) }
        else -> emptyList()
    }
}
