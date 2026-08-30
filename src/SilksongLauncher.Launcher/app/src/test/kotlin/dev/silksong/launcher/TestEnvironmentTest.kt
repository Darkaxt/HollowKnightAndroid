package dev.silksong.launcher

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class TestEnvironmentTest {
    @Test
    fun robolectric_loads_launcher_resources() {
        val context = ApplicationProvider.getApplicationContext<Context>()

        assertEquals("Dual Souls", context.getString(R.string.launcher_app_name))
    }

    @Test
    fun save_transfer_actions_use_import_export_labels_without_arrows() {
        val context = ApplicationProvider.getApplicationContext<Context>()

        assertEquals("Import saves", context.getString(R.string.action_pull_saves))
        assertEquals("Import saves", context.getString(R.string.action_pull_saves_busy))
        assertEquals("Export saves", context.getString(R.string.action_push_saves))
        assertEquals("Export saves", context.getString(R.string.action_push_saves_busy))
    }
}
