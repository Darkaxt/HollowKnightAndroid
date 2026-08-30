package io.github.darkaxt.dualsouls.lab

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Process

class LabLauncherControlReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        require(intent.action == CLEAN_EXIT_ACTION) { "Unexpected lab launcher control action" }
        Process.killProcess(Process.myPid())
    }

    private companion object {
        const val CLEAN_EXIT_ACTION = "io.github.darkaxt.dualsouls.lab.CLEAN_LAUNCHER_EXIT"
    }
}
