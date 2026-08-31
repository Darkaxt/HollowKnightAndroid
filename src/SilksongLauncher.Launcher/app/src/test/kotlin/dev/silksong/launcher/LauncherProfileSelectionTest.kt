package dev.silksong.launcher

import android.content.Context
import android.content.Intent
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.shortcuts.GameShortcutContract
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
class LauncherProfileSelectionTest {
    private lateinit var context: Context

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        SelectedGameStore(context).set(GameProfiles.require("silksong"))
    }

    @Test
    fun `launcher renders both registered games and persisted selection`() {
        val activity = Robolectric.buildActivity(LauncherActivity::class.java).setup().get()

        val hollowKnight = activity.findViewById<GameCardView>(R.id.radio_hollow_knight)
        val silksong = activity.findViewById<GameCardView>(R.id.radio_silksong)
        assertFalse(hollowKnight.isChecked)
        assertTrue(silksong.isChecked)
        assertTrue(collectText(hollowKnight).none { it == "Hollow Knight" })
        assertTrue(collectText(silksong).none { it == "Hollow Knight: Silksong" })
        assertEquals(
            "Not configured",
            activity.findViewById<TextView>(R.id.txt_hollow_knight_status).text.toString(),
        )
        assertEquals(
            requireNotNull(activity.getDrawable(R.drawable.game_card_hollow_knight_hero)).constantState,
            activity.findViewById<ImageView>(R.id.img_hollow_knight_hero).drawable.constantState,
        )
        assertEquals(
            requireNotNull(activity.getDrawable(R.drawable.game_card_silksong_logo)).constantState,
            activity.findViewById<ImageView>(R.id.img_silksong_logo).drawable.constantState,
        )
        val hollowKnightLogo = activity.findViewById<ImageView>(R.id.img_hollow_knight_logo)
        val silksongLogo = activity.findViewById<ImageView>(R.id.img_silksong_logo)
        assertEquals(Gravity.CENTER, (hollowKnightLogo.layoutParams as FrameLayout.LayoutParams).gravity)
        assertEquals(0, (hollowKnightLogo.layoutParams as FrameLayout.LayoutParams).bottomMargin)
        assertEquals(Gravity.CENTER, (silksongLogo.layoutParams as FrameLayout.LayoutParams).gravity)
        assertEquals(0, (silksongLogo.layoutParams as FrameLayout.LayoutParams).bottomMargin)
        val hollowKnightPin = activity.findViewById<Button>(R.id.btn_pin_hollow_knight)
        val silksongPin = activity.findViewById<Button>(R.id.btn_pin_silksong)
        assertEquals(
            Gravity.TOP or Gravity.START,
            (hollowKnightPin.layoutParams as FrameLayout.LayoutParams).gravity,
        )
        assertEquals(
            Gravity.TOP or Gravity.START,
            (silksongPin.layoutParams as FrameLayout.LayoutParams).gravity,
        )
        assertEquals("📌", hollowKnightPin.text.toString())
        assertEquals("📌", silksongPin.text.toString())
        assertEquals("Pin Hollow Knight shortcut", hollowKnightPin.contentDescription.toString())
        assertEquals("Pin Silksong shortcut", silksongPin.contentDescription.toString())
        assertFalse(hollowKnightPin.isEnabled)
        assertFalse(silksongPin.isEnabled)
        assertEquals(
            0,
            activity.resources.getIdentifier(
                "txt_selected_game_status",
                "id",
                activity.packageName,
            ),
        )
        assertEquals(
            0,
            activity.resources.getIdentifier("btn_shortcut", "id", activity.packageName),
        )
    }

    @Test
    fun `choosing hollow knight persists before activity recreation`() {
        val controller = Robolectric.buildActivity(LauncherActivity::class.java).setup()
        val activity = controller.get()

        activity.findViewById<GameCardView>(R.id.radio_hollow_knight).performClick()

        assertEquals("hollow-knight", SelectedGameStore(context).get().id)
    }

    @Test
    fun `production runtime does not show fake evidence banner`() {
        val activity = Robolectric.buildActivity(LauncherActivity::class.java).setup().get()

        assertEquals(View.GONE, activity.findViewById<TextView>(R.id.txt_runtime_banner).visibility)
    }

    @Test
    fun `unready selected profile opens shared setup instead of game`() {
        SelectedGameStore(context).set(GameProfiles.require("hollow-knight"))
        val activity = Robolectric.buildActivity(LauncherActivity::class.java).setup().get()

        activity.findViewById<Button>(R.id.btn_launch).performClick()

        val started = shadowOf(activity).nextStartedActivity
        assertEquals(SetupActivity::class.java.name, started.component?.className)
    }

    @Test
    fun `hollow knight setup does not invoke silksong legacy adoption`() {
        SelectedGameStore(context).set(GameProfiles.require("hollow-knight"))

        Robolectric.buildActivity(SetupActivity::class.java).setup().get()
    }

    @Test
    fun `hollow knight setup names the selected game instead of silksong`() {
        SelectedGameStore(context).set(GameProfiles.require("hollow-knight"))

        val activity = Robolectric.buildActivity(SetupActivity::class.java).setup().get()
        val copy = collectText(activity.findViewById(android.R.id.content))

        assertTrue(copy.any { it == "Hollow Knight" })
        assertFalse(copy.any { it == "Dual Souls" })
        assertTrue(copy.any { it == "Where is your copy of Hollow Knight?" })
        assertFalse(copy.any { it.contains("Silksong") })
    }

    @Test
    fun `direct shortcut persists its exact profile then uses shared launch eligibility`() {
        val shortcut = Intent(context, LauncherActivity::class.java).apply {
            action = GameShortcutContract.ACTION_DIRECT_LAUNCH
            putExtra(GameShortcutContract.PROFILE_ID_EXTRA, "hollow-knight")
        }

        val activity = Robolectric.buildActivity(LauncherActivity::class.java, shortcut).setup().get()
        shadowOf(Looper.getMainLooper()).idle()

        assertEquals("hollow-knight", SelectedGameStore(context).get().id)
        assertEquals(
            SetupActivity::class.java.name,
            shadowOf(activity).nextStartedActivity.component?.className,
        )
    }

    @Test
    fun `direct shortcut rejects an unregistered profile without changing selection`() {
        val shortcut = Intent(context, LauncherActivity::class.java).apply {
            action = GameShortcutContract.ACTION_DIRECT_LAUNCH
            putExtra(GameShortcutContract.PROFILE_ID_EXTRA, "other-game")
        }

        val activity = Robolectric.buildActivity(LauncherActivity::class.java, shortcut).setup().get()
        shadowOf(Looper.getMainLooper()).idle()

        assertEquals("silksong", SelectedGameStore(context).get().id)
        assertEquals(null, shadowOf(activity).nextStartedActivity)
    }

    private fun collectText(view: View): List<String> = when (view) {
        is TextView -> listOf(view.text.toString())
        is ViewGroup -> (0 until view.childCount).flatMap { collectText(view.getChildAt(it)) }
        else -> emptyList()
    }
}
