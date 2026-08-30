package dev.silksong.launcher.runtime

import android.content.Context
import android.content.pm.PackageManager

object LauncherRuntimeProvider {
    const val LAB_PACKAGE = "io.github.darkaxt.dualsouls.emutest"
    const val META_DATA_CLASS = "io.github.darkaxt.dualsouls.LAUNCHER_RUNTIME"

    fun from(context: Context): LauncherRuntime {
        val applicationContext = context.applicationContext
        @Suppress("DEPRECATION")
        val applicationInfo = applicationContext.packageManager.getApplicationInfo(
            applicationContext.packageName,
            PackageManager.GET_META_DATA,
        )
        return resolve(
            packageName = applicationContext.packageName,
            className = applicationInfo.metaData?.getString(META_DATA_CLASS),
            classLoader = applicationContext.classLoader,
        )
    }

    internal fun resolve(
        packageName: String,
        className: String?,
        classLoader: ClassLoader,
    ): LauncherRuntime {
        if (className.isNullOrBlank()) {
            check(packageName != LAB_PACKAGE) {
                "The Dual Souls lab package must declare $META_DATA_CLASS"
            }
            return ProductionLauncherRuntime()
        }
        check(packageName == LAB_PACKAGE) {
            "Only $LAB_PACKAGE may declare an alternate launcher runtime"
        }
        return try {
            val type = Class.forName(className, true, classLoader)
            check(LauncherRuntime::class.java.isAssignableFrom(type)) {
                "Configured launcher runtime does not implement LauncherRuntime: $className"
            }
            type.getDeclaredConstructor().newInstance() as LauncherRuntime
        } catch (t: Throwable) {
            if (t is IllegalStateException) throw t
            throw IllegalStateException("Could not load launcher runtime: $className", t)
        }
    }
}
