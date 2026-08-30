package dev.silksong.launcher.runtime

import android.content.Context
import java.security.MessageDigest

object ProductionBuildSignature {
    fun compute(context: Context): String {
        return "1|" + computeSha256(context).take(16)
    }

    fun computeSha256(context: Context): String {
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
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}
