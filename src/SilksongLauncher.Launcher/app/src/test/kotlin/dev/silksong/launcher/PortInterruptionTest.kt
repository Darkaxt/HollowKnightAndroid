package dev.silksong.launcher

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class PortInterruptionTest {
    private val context: Context = ApplicationProvider.getApplicationContext()

    @Test
    fun `a port that ended normally leaves nothing to report`() {
        PortInterruption.begin(context, "Hollow Knight")
        PortInterruption.end(context)
        assertNull(PortInterruption.consumeInterrupted(context, portActive = false))
    }

    @Test
    fun `a port cut off by the process dying is reported once`() {
        PortInterruption.begin(context, "Hollow Knight")
        // A new process: the in-memory flag is gone, the marker is not.
        assertEquals("Hollow Knight", PortInterruption.consumeInterrupted(context, portActive = false))
        assertNull(PortInterruption.consumeInterrupted(context, portActive = false))
        PortInterruption.end(context)
    }

    @Test
    fun `a port still running in this process is not an interruption`() {
        PortInterruption.begin(context, "Silksong")
        assertNull(PortInterruption.consumeInterrupted(context, portActive = true))
        PortInterruption.end(context)
        assertNull(PortInterruption.consumeInterrupted(context, portActive = false))
    }
}
