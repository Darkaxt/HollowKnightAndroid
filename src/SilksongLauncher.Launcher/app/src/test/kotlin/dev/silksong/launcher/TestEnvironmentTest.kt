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
}
