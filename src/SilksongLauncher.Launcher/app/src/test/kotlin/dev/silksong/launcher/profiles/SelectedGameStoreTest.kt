package dev.silksong.launcher.profiles

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class SelectedGameStoreTest {
    private lateinit var context: Context

    @Before
    fun clearSelection() {
        context = ApplicationProvider.getApplicationContext()
        context.getSharedPreferences("selected-game", Context.MODE_PRIVATE)
            .edit()
            .clear()
            .commit()
    }

    @Test
    fun default_selection_is_silksong() {
        assertEquals("silksong", SelectedGameStore(context).get().id)
    }

    @Test
    fun valid_selection_survives_store_recreation() {
        SelectedGameStore(context).set(GameProfiles.require("hollow-knight"))

        assertEquals("hollow-knight", SelectedGameStore(context).get().id)
    }

    @Test
    fun unknown_stored_id_falls_back_to_silksong() {
        context.getSharedPreferences("selected-game", Context.MODE_PRIVATE)
            .edit()
            .putString("profile-id", "unknown")
            .commit()

        assertEquals("silksong", SelectedGameStore(context).get().id)
    }

    @Test
    fun unregistered_profile_cannot_be_persisted() {
        val unregistered = GameProfiles.require("silksong").copy(id = "../escape")

        assertThrows(IllegalArgumentException::class.java) {
            SelectedGameStore(context).set(unregistered)
        }
    }
}
