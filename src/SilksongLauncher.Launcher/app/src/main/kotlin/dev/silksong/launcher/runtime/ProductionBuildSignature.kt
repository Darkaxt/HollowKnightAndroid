package dev.silksong.launcher.runtime

import android.content.Context
import java.security.MessageDigest

object ProductionBuildSignature {
    fun compute(context: Context): String {
        val digest = MessageDigest.getInstance("SHA-256")
        fun walk(path: String) {
            val names = runCatching { context.assets.list(path) }.getOrNull().orEmpty().sorted()
            if (names.isEmpty()) {
                runCatching { context.assets.open(path).use { digest.update(it.readBytes()) } }
                return
            }
            for (name in names) walk("$path/$name")
        }
        walk("ondevice")
        return "1|" + digest.digest().joinToString("") { "%02x".format(it) }.take(16)
    }
}
