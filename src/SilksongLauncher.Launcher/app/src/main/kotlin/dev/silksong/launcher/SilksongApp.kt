// SilksongApp — custom Application class for the Silksong APK.
//
// Runs ONCE PER PROCESS at process startup, before any Activity is created.
// That is the only place either of the things below can go: both have to be in
// effect before anything else in the process runs.

package dev.silksong.launcher

import android.app.Application
import android.content.Context
import dev.silksong.launcher.runtime.GameProcessStartup
import dev.silksong.launcher.runtime.ProcessRole

class SilksongApp : Application() {

    // Before any class in this app is loaded, which rules out onCreate.
    //
    // The APK links against com.unity3d.player.* and ships none of it: the
    // player classes are dexed on the device out of the module the app
    // downloads. GameActivity's superclass is one of the types that resolves
    // from there, so the dex has to be in the class loader before the
    // framework instantiates the activity. attachBaseContext is the first
    // point in the process where that is possible.
    override fun attachBaseContext(base: Context) {
        super.attachBaseContext(base)
        val processName = ProcessRole.currentName(this)
            ?: throw IllegalStateException("Could not identify the application process")
        if (ProcessRole.isGameProcess(packageName, processName)) {
            val startup = GameProcessStartup.prepare(this)
            UnityDex.inject(this, startup)
        }
    }

    override fun onCreate() {
        super.onCreate()

        // First, so that everything below is recorded too. The log is written
        // to a file from here on, which is what makes it survivable enough to
        // be worth asking a user for.
        LauncherLog.attach(this)

        // Before anything else, and in every process: JavaSteam's crypto
        // registers itself the first time it is touched, and on Android it
        // registers the wrong thing unless this has run first. See SteamCrypto.
        SteamCrypto.install()
    }
}
