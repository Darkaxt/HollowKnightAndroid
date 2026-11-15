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
    fun save_transfer_actions_name_steam_cloud_without_arrows() {
        val context = ApplicationProvider.getApplicationContext<Context>()

        assertEquals("Download from Steam Cloud", context.getString(R.string.action_pull_saves))
        assertEquals("Downloading from Steam Cloud…", context.getString(R.string.action_pull_saves_busy))
        assertEquals("Upload to Steam Cloud", context.getString(R.string.action_push_saves))
        assertEquals("Uploading to Steam Cloud…", context.getString(R.string.action_push_saves_busy))
    }
}
