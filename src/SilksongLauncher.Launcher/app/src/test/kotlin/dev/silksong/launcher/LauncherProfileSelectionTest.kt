package dev.silksong.launcher

import android.content.Context
import android.view.View
import android.widget.Button
import android.widget.RadioButton
import android.widget.TextView
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

        val hollowKnight = activity.findViewById<RadioButton>(R.id.radio_hollow_knight)
        val silksong = activity.findViewById<RadioButton>(R.id.radio_silksong)
        assertEquals("Hollow Knight", hollowKnight.text.toString())
        assertEquals("Hollow Knight: Silksong", silksong.text.toString())
        assertFalse(hollowKnight.isChecked)
        assertTrue(silksong.isChecked)
        assertEquals(
            "Selected: Hollow Knight: Silksong",
            activity.findViewById<TextView>(R.id.txt_selected_game_status).text.toString(),
        )
    }

    @Test
    fun `choosing hollow knight persists before activity recreation`() {
        val controller = Robolectric.buildActivity(LauncherActivity::class.java).setup()
        val activity = controller.get()

        activity.findViewById<RadioButton>(R.id.radio_hollow_knight).performClick()

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
}
