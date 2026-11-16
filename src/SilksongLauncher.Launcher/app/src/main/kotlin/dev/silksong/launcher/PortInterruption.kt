// PortInterruption — tells the player when Android ended a port.
//
// The port runs inside the launcher process. When the system reclaims that
// process mid-port (#2), nothing in memory survives, so the next start showed
// a plain "Update needed" and no word about what happened. A marker on disk
// covers the run and every ordinary ending clears it, so a marker that is
// still there when the launcher starts means the run was cut off.

package dev.silksong.launcher

import android.content.Context
import java.io.File

object PortInterruption {
    private const val MARKER = "port-running"

    @Volatile private var active = false

    fun begin(context: Context, gameName: String) {
        active = true
        runCatching { marker(context).writeText(gameName) }
    }

    fun end(context: Context) {
        active = false
        marker(context).delete()
    }

    /**
     * The name of the game whose port was cut off, once, at the first start
     * after it; null otherwise. A port still running in this process is not
     * an interruption.
     */
    fun consumeInterrupted(context: Context, portActive: Boolean = active): String? {
        if (portActive) return null
        val file = marker(context)
        if (!file.isFile) return null
        val name = runCatching { file.readText().trim() }.getOrDefault("")
        file.delete()
        return name.ifEmpty { "the game" }
    }

    private fun marker(context: Context) = File(context.filesDir, MARKER)
}
