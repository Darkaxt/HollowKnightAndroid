package dev.silksong.launcher.shortcuts

import android.content.Context
import android.content.Intent
import android.content.pm.ShortcutInfo
import android.content.pm.ShortcutManager
import android.graphics.drawable.Icon
import dev.silksong.launcher.LauncherActivity
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.runtime.RuntimeState

object GameShortcutContract {
    const val ACTION_DIRECT_LAUNCH = "dev.silksong.launcher.action.DIRECT_LAUNCH"
    const val PROFILE_ID_EXTRA = "dev.silksong.launcher.PROFILE_ID"
}

data class GameShortcutRequest(
    val id: String,
    val shortLabel: String,
    val longLabel: String,
    val iconResource: Int,
    val intent: Intent,
)

enum class GameShortcutResult {
    REQUESTED,
    UNSUPPORTED,
    ALREADY_PINNED,
    NOT_READY,
    REJECTED,
}

interface ShortcutGateway {
    fun isSupported(): Boolean

    fun isPinned(id: String): Boolean

    fun request(shortcut: GameShortcutRequest): Boolean
}

class AndroidShortcutGateway(context: Context) : ShortcutGateway {
    private val app = context.applicationContext
    private val manager = app.getSystemService(ShortcutManager::class.java)

    override fun isSupported(): Boolean = manager?.isRequestPinShortcutSupported == true

    override fun isPinned(id: String): Boolean =
        manager?.pinnedShortcuts.orEmpty().any { it.id == id }

    override fun request(shortcut: GameShortcutRequest): Boolean {
        val service = manager ?: return false
        val info = ShortcutInfo.Builder(app, shortcut.id)
            .setShortLabel(shortcut.shortLabel)
            .setLongLabel(shortcut.longLabel)
            .setIcon(Icon.createWithResource(app, shortcut.iconResource))
            .setIntent(shortcut.intent)
            .build()
        return service.requestPinShortcut(info, null)
    }
}

class GameShortcutController(
    private val context: Context,
    private val gateway: ShortcutGateway = AndroidShortcutGateway(context),
) {
    fun isPinned(profile: GameProfile): Boolean {
        require(GameProfiles.find(profile.id) == profile) {
            "Cannot inspect a shortcut for an unregistered game profile: ${profile.id}"
        }
        return gateway.isPinned(shortcut(profile).id)
    }

    fun request(profile: GameProfile, runtime: RuntimeState): GameShortcutResult {
        require(GameProfiles.find(profile.id) == profile) {
            "Cannot create a shortcut for an unregistered game profile: ${profile.id}"
        }
        if (!runtime.ready) return GameShortcutResult.NOT_READY
        if (!gateway.isSupported()) return GameShortcutResult.UNSUPPORTED

        val shortcut = shortcut(profile)
        if (gateway.isPinned(shortcut.id)) return GameShortcutResult.ALREADY_PINNED
        return if (gateway.request(shortcut)) {
            GameShortcutResult.REQUESTED
        } else {
            GameShortcutResult.REJECTED
        }
    }

    internal fun shortcut(profile: GameProfile): GameShortcutRequest {
        val icon = when (profile.id) {
            "hollow-knight" -> R.drawable.shortcut_hollow_knight
            "silksong" -> R.drawable.shortcut_silksong
            else -> error("No shortcut icon for registered profile ${profile.id}")
        }
        val intent = Intent(context, LauncherActivity::class.java).apply {
            action = GameShortcutContract.ACTION_DIRECT_LAUNCH
            putExtra(GameShortcutContract.PROFILE_ID_EXTRA, profile.id)
            addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        }
        return GameShortcutRequest(
            id = "game-${profile.id}",
            shortLabel = profile.displayName,
            longLabel = "Launch ${profile.displayName}",
            iconResource = icon,
            intent = intent,
        )
    }
}
