package io.github.darkaxt.dualsouls.lab

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Process

class LabGameControlReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        require(intent.action == CLEAN_EXIT_ACTION) { "Unexpected lab game control action" }
        val profileId = requireNotNull(intent.getStringExtra(LabLauncherRuntime.PROFILE_ID_EXTRA))
        val generationId = requireNotNull(intent.getStringExtra(LabLauncherRuntime.GENERATION_ID_EXTRA))
        LabLauncherRuntime.recordCleanExit(context, profileId, generationId)
        Process.killProcess(Process.myPid())
    }

    private companion object {
        const val CLEAN_EXIT_ACTION = "io.github.darkaxt.dualsouls.lab.CLEAN_EXIT"
    }
}
