package dev.silksong.launcher.runtime

import android.app.ActivityManager
import android.content.Context
import android.os.Process

enum class GameProcessState {
    ACTIVE,
    INACTIVE,
    UNKNOWN,
}

object GameProcessInspector {
    fun inspect(context: Context, processName: String): GameProcessState {
        val manager = context.getSystemService(ActivityManager::class.java)
            ?: return GameProcessState.UNKNOWN
        val processes = manager.runningAppProcesses ?: return GameProcessState.UNKNOWN
        return if (processes.any { it.processName == processName && it.pid != Process.myPid() }) {
            GameProcessState.ACTIVE
        } else {
            GameProcessState.INACTIVE
        }
    }
}
