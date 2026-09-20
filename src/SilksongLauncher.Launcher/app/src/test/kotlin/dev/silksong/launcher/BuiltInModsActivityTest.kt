package dev.silksong.launcher

import android.content.Context
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.skins.ui.SkinsActivity
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertSame
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

    @Test fun `skins row opens the launcher package manager`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()

        modRow(activity, "skins").performClick()

        assertEquals(SkinsActivity::class.java.name, shadowOf(activity).nextStartedActivity.component?.className)
    }

    @Test fun `in-game operations are labeled and never cycled as values`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        enableAndReset(activity)
        val row = modRow(activity, "save_to_slot")

        assertTrue(collectText(row).contains("IN GAME"))
        row.performClick()
        row.performClick()

        assertTrue(
            activity.findViewById<TextView>(R.id.txt_mod_status).text.contains(
                "in-game Mods pane",
                ignoreCase = true,
            ),
        )
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
        assertEquals(
            listOf("GENERAL", "WORLD", "COMBAT", "ENCOUNTERS", "CHARMS", "SAVE STATES", "ECONOMY", "PRESENTATION"),
            groupLabels,
        )
        assertFalse(collectText(list).contains("UNAVAILABLE"))
        assertEquals("MASTER · OFF", activity.findViewById<Button>(R.id.btn_mods_master).text.toString())
        assertTrue(activity.findViewById<TextView>(R.id.txt_mod_detail).text.isNotBlank())
        assertTrue(activity.findViewById<Button>(R.id.btn_reset_mods).text.toString().contains("RESET ALL MODS"))
    }

    @Test fun `initial focus and explicit graph cover every controller target in both directions`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        val master = activity.findViewById<Button>(R.id.btn_mods_master)
        val rows = modRows(activity)
        val controls = listOf<View>(master) + rows + listOf(
            activity.findViewById(R.id.btn_reset_mods),
            activity.findViewById(R.id.btn_plugins),
            buttonNamed(activity, "BACK"),
        )

        assertTrue(master.hasFocus())
        controls.indices.forEach { index ->
            assertSame(controls[(index + 1) % controls.size], controls[index].focusSearch(View.FOCUS_DOWN))
            assertSame(controls[(index - 1 + controls.size) % controls.size], controls[index].focusSearch(View.FOCUS_UP))
        }
    }

    @Test fun `row focus selects its detail and survives value rerender`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        enableAndReset(activity)
        val row = modRow(activity, "damage_received")

        assertTrue(row.requestFocus())
        assertTrue(activity.findViewById<TextView>(R.id.txt_mod_detail).text.startsWith("DAMAGE TAKEN"))
        assertTrue(activity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_DPAD_RIGHT)))

        assertTrue(modRow(activity, "damage_received").hasFocus())
        assertTrue(collectText(modRow(activity, "damage_received")).contains("NO MASK LOSS"))
    }

    @Test fun `left and right cycle the focused row backward and forward with wraparound`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        enableAndReset(activity)
        modRow(activity, "damage_received").requestFocus()

        activity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_DPAD_LEFT))
        assertTrue(collectText(modRow(activity, "damage_received")).contains("INVINCIBLE"))

        activity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_DPAD_RIGHT))
        assertTrue(collectText(modRow(activity, "damage_received")).contains("VANILLA"))
    }

    @Test fun `controller activation keys click the focused control`() {
        val activity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        val master = activity.findViewById<Button>(R.id.btn_mods_master)
        if (master.text.toString().endsWith("ON")) master.performClick()

        for (keyCode in listOf(KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_DPAD_CENTER)) {
            master.requestFocus()
            assertTrue(activity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, keyCode)))
            assertTrue(master.text.toString().endsWith("ON"))
            master.performClick()
        }
    }

    @Test fun `gamepad B and system Back finish the activity`() {
        val gamepadActivity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        assertTrue(gamepadActivity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_BUTTON_B)))
        assertTrue(gamepadActivity.isFinishing)

        val systemActivity = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup().get()
        assertTrue(systemActivity.dispatchKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_BACK)))
        assertTrue(systemActivity.isFinishing)
    }

    @Test fun `detail owns weighted width and activity reapplies immersive fullscreen`() {
        val controller = Robolectric.buildActivity(BuiltInModsActivity::class.java).setup()
        val activity = controller.get()
        val detailParams = activity.findViewById<TextView>(R.id.txt_mod_detail).layoutParams as LinearLayout.LayoutParams
        val immersive = View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
            View.SYSTEM_UI_FLAG_FULLSCREEN or
            View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE

        assertEquals(ViewGroup.LayoutParams.MATCH_PARENT, detailParams.width)
        assertEquals(0, detailParams.height)
        assertEquals(1f, detailParams.weight)
        assertTrue(activity.window.attributes.flags and WindowManager.LayoutParams.FLAG_FULLSCREEN != 0)
        assertEquals(immersive, activity.window.decorView.systemUiVisibility and immersive)

        activity.window.decorView.systemUiVisibility = 0
        controller.pause().resume()
        assertEquals(immersive, activity.window.decorView.systemUiVisibility and immersive)
        activity.window.decorView.systemUiVisibility = 0
        activity.onWindowFocusChanged(true)
        assertEquals(immersive, activity.window.decorView.systemUiVisibility and immersive)
    }

    private fun enableAndReset(activity: BuiltInModsActivity) {
        activity.findViewById<Button>(R.id.btn_reset_mods).performClick()
        val master = activity.findViewById<Button>(R.id.btn_mods_master)
        if (master.text.toString().endsWith("OFF")) master.performClick()
    }

    private fun modRows(activity: BuiltInModsActivity): List<View> {
        val list = activity.findViewById<LinearLayout>(R.id.builtin_mods_list)
        return (0 until list.childCount).map { list.getChildAt(it) }.filter { it.isFocusable }
    }

    private fun modRow(activity: BuiltInModsActivity, id: String): View =
        modRows(activity).single { it.tag == "mod:$id" }

    private fun buttonNamed(activity: BuiltInModsActivity, text: String): Button =
        allViews(activity.findViewById(android.R.id.content))
            .filterIsInstance<Button>()
            .single { it.text.toString() == text }

    private fun allViews(view: View): List<View> = when (view) {
        is ViewGroup -> listOf(view) + (0 until view.childCount).flatMap { allViews(view.getChildAt(it)) }
        else -> listOf(view)
    }

    private fun collectText(view: View): List<String> = when (view) {
        is TextView -> listOf(view.text.toString())
        is ViewGroup -> (0 until view.childCount).flatMap { collectText(view.getChildAt(it)) }
        else -> emptyList()
    }
}
