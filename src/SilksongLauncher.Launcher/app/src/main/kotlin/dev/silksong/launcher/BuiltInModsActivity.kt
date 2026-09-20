package dev.silksong.launcher

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.silksong.launcher.builtinmods.BuiltInModCatalog
import dev.silksong.launcher.builtinmods.BuiltInModControlKind
import dev.silksong.launcher.builtinmods.BuiltInModDescriptor
import dev.silksong.launcher.builtinmods.BuiltInModsController
import dev.silksong.launcher.builtinmods.LineModStateStore
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.runtime.GameLifecycleAuthority
import dev.silksong.launcher.skins.ui.SkinsActivity
import java.io.File

/** Launcher presentation of the selected game's built-in typed Mods/Cheats catalog. */
class BuiltInModsActivity : Activity() {
    private val profile by lazy { SelectedGameStore(this).get() }
    private val paths by lazy {
        ProfileBuildPaths(filesDir, requireNotNull(getExternalFilesDir(null)), profile)
    }
    private val controller by lazy {
        BuiltInModsController(
            profile.id,
            BuiltInModCatalog.forGame(profile.id),
            LineModStateStore(File(paths.modStateRoot, "builtin-state.txt")),
            GameLifecycleAuthority.forModStateRoot(paths.modStateRoot),
        )
    }

    private lateinit var list: LinearLayout
    private lateinit var master: Button
    private lateinit var detail: TextView
    private lateinit var status: TextView
    private lateinit var reset: Button
    private lateinit var plugins: Button
    private lateinit var back: Button
    private val rowIds = mutableMapOf<String, Int>()
    private val rows = linkedMapOf<String, View>()
    private var selectedId: String? = null
    private var focusedKey = MASTER_FOCUS

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        applyImmersiveFullscreen()
        selectedId = controller.snapshot().descriptors.firstOrNull()?.id
        setContentView(buildUi())
        render()
    }

    override fun onResume() {
        super.onResume()
        applyImmersiveFullscreen()
        if (::list.isInitialized) {
            controller.reload()
            render()
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) applyImmersiveFullscreen()
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean = when (event.keyCode) {
        KeyEvent.KEYCODE_BACK, KeyEvent.KEYCODE_BUTTON_B -> {
            if (event.action == KeyEvent.ACTION_DOWN) finish()
            true
        }
        KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_DPAD_CENTER -> {
            if (event.action == KeyEvent.ACTION_DOWN) focusTarget(focusedKey).performClick()
            true
        }
        KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT -> {
            val id = focusedKey.takeIf { it.startsWith(ROW_FOCUS_PREFIX) }
                ?.removePrefix(ROW_FOCUS_PREFIX)
            if (id == null) {
                super.dispatchKeyEvent(event)
            } else {
                if (event.action == KeyEvent.ACTION_DOWN) {
                    val delta = if (event.keyCode == KeyEvent.KEYCODE_DPAD_LEFT) -1 else 1
                    show(controller.cycle(id, delta).message)
                }
                true
            }
        }
        else -> super.dispatchKeyEvent(event)
    }

    private fun buildUi(): LinearLayout {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(20), dp(16), dp(20), dp(16))
        }
        val header = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL }
        header.addView(label("MODS — ${profile.displayName.uppercase()}", 22f, bold = true), horizontalWeight(1f))
        master = action("", primary = true) {
            show(controller.setMaster(!controller.snapshot().masterEnabled).message)
        }.apply {
            id = R.id.btn_mods_master
            tag = MASTER_FOCUS
            trackFocus(MASTER_FOCUS)
        }
        header.addView(master)
        root.addView(header)

        root.addView(label("BUILT-IN GAMEPLAY & PRESENTATION", 11f, Color.parseColor("#7A6E71")))
        val body = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        list = LinearLayout(this).apply { id = R.id.builtin_mods_list; orientation = LinearLayout.VERTICAL }
        val scroll = ScrollView(this).apply { addView(list) }
        body.addView(scroll, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 0.55f))

        val details = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(8), 0, 0)
        }
        detail = label("", 15f).apply { id = R.id.txt_mod_detail }
        status = label("", 12f, Color.parseColor("#C88A94")).apply { id = R.id.txt_mod_status }
        details.addView(detail, verticalWeight(1f))
        details.addView(label("Tap a row to select it; tap it again to change its value.", 11f, Color.parseColor("#7A6E71")))
        details.addView(status)
        reset = action("RESET ALL MODS") { show(controller.reset().message) }.apply {
            id = R.id.btn_reset_mods
            tag = RESET_FOCUS
            trackFocus(RESET_FOCUS)
        }
        details.addView(reset)
        plugins = action("PLUGINS") {
            startActivity(Intent(this@BuiltInModsActivity, ModsActivity::class.java))
        }.apply {
            id = R.id.btn_plugins
            tag = PLUGINS_FOCUS
            trackFocus(PLUGINS_FOCUS)
        }
        details.addView(plugins)
        back = action("BACK") { finish() }.apply {
            id = View.generateViewId()
            tag = BACK_FOCUS
            trackFocus(BACK_FOCUS)
        }
        details.addView(back)
        body.addView(details, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 0.45f))
        root.addView(body, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        return root
    }

    private fun render() {
        val restoreFocus = currentFocus?.tag?.toString() ?: focusedKey
        val snapshot = controller.snapshot()
        master.text = if (snapshot.masterEnabled) "MASTER · ON" else "MASTER · OFF"
        list.removeAllViews()
        rows.clear()
        var group: String? = null
        snapshot.descriptors.forEach { descriptor ->
            if (group != descriptor.group) {
                group = descriptor.group
                list.addView(label(descriptor.group, 11f, Color.parseColor("#7A6E71"), bold = true).apply {
                    tag = "group"
                    setPadding(dp(8), dp(10), dp(8), dp(2))
                })
            }
            val row = modRow(descriptor, snapshot.value(descriptor.id))
            rows[descriptor.id] = row
            list.addView(row)
        }
        refreshSelection()
        configureFocusGraph()
        focusTarget(restoreFocus).requestFocus()
    }

    private fun refreshSelection() {
        val snapshot = controller.snapshot()
        rows.forEach { (id, row) ->
            row.setBackgroundColor(Color.parseColor(if (selectedId == id) "#2A2022" else "#161112"))
        }
        val selected = snapshot.descriptors.firstOrNull { it.id == selectedId }
        detail.text = selected?.let {
            val value = if (it.isAvailable) {
                displayValue(it, snapshot.value(it.id))
            } else {
                "UNAVAILABLE"
            }
            buildString {
                append("${it.title}\n\nVALUE  $value\n\n${it.description}")
                if (!it.isAvailable) append("\n\n${it.unavailableReason}")
            }
        }.orEmpty()
    }

    private fun modRow(descriptor: BuiltInModDescriptor, value: String): LinearLayout =
        LinearLayout(this).apply {
            id = rowIds.getOrPut(descriptor.id) { View.generateViewId() }
            tag = ROW_FOCUS_PREFIX + descriptor.id
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            isClickable = true
            isFocusable = true
            foreground = getDrawable(R.drawable.focus_on_dark)
            setPadding(dp(12), dp(10), dp(12), dp(10))
            val rowEnabled = controller.snapshot().masterEnabled && descriptor.isAvailable
            addView(label(descriptor.title, 15f, if (rowEnabled) Color.WHITE else Color.GRAY), horizontalWeight(1f))
            val displayValue = if (descriptor.isAvailable) displayValue(descriptor, value) else "UNAVAILABLE"
            addView(label(displayValue, 14f, Color.parseColor("#C88A94")))
            setOnFocusChangeListener { _, hasFocus ->
                if (hasFocus) {
                    focusedKey = tag.toString()
                    if (selectedId != descriptor.id) {
                        selectedId = descriptor.id
                        status.text = ""
                        refreshSelection()
                    }
                }
            }
            setOnClickListener {
                if (selectedId != descriptor.id) {
                    selectedId = descriptor.id
                    status.text = ""
                    refreshSelection()
                } else {
                    activate(descriptor)
                }
            }
        }

    private fun activate(descriptor: BuiltInModDescriptor) {
        if (!descriptor.isAvailable) {
            show(descriptor.unavailableReason)
            return
        }
        when (descriptor.controlKind) {
            BuiltInModControlKind.Choice -> show(controller.cycle(descriptor.id).message)
            BuiltInModControlKind.Route -> {
                if (descriptor.contractId == "skins") {
                    startActivity(Intent(this, SkinsActivity::class.java))
                } else {
                    show("${descriptor.title} is available from the in-game Mods pane.")
                }
            }
            BuiltInModControlKind.Command ->
                show("${descriptor.title} is available from the in-game Mods pane.")
        }
    }

    private fun displayValue(descriptor: BuiltInModDescriptor, value: String): String =
        when (descriptor.controlKind) {
            BuiltInModControlKind.Choice -> BuiltInModsController.friendly(value)
            BuiltInModControlKind.Route -> if (descriptor.contractId == "skins") "OPEN" else "IN GAME"
            BuiltInModControlKind.Command -> "IN GAME"
        }

    private fun configureFocusGraph() {
        val controls = listOf<View>(master) + rows.values + listOf(reset, plugins, back)
        controls.indices.forEach { index ->
            controls[index].nextFocusDownId = controls[(index + 1) % controls.size].id
            controls[index].nextFocusUpId = controls[(index - 1 + controls.size) % controls.size].id
        }
    }

    private fun focusTarget(key: String): View = when (key) {
        MASTER_FOCUS -> master
        RESET_FOCUS -> reset
        PLUGINS_FOCUS -> plugins
        BACK_FOCUS -> back
        else -> rows[key.removePrefix(ROW_FOCUS_PREFIX)] ?: master
    }

    private fun View.trackFocus(key: String) {
        setOnFocusChangeListener { _, hasFocus -> if (hasFocus) focusedKey = key }
    }

    private fun show(message: String) {
        status.text = message
        render()
        status.text = message
    }

    @Suppress("DEPRECATION")
    private fun applyImmersiveFullscreen() {
        window.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN)
        window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
            View.SYSTEM_UI_FLAG_FULLSCREEN or
            View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE
    }

    private fun label(text: String, size: Float, color: Int = Color.WHITE, bold: Boolean = false) =
        TextView(this).apply {
            this.text = text
            textSize = size
            setTextColor(color)
            if (bold) setTypeface(typeface, Typeface.BOLD)
        }

    private fun action(text: String, primary: Boolean = false, click: () -> Unit) = Button(this).apply {
        this.text = text
        isFocusable = true
        foreground = getDrawable(if (primary) R.drawable.focus_on_dark else R.drawable.focus_on_light)
        setOnClickListener { click() }
        backgroundTintList = android.content.res.ColorStateList.valueOf(
            Color.parseColor(if (primary) "#7D3341" else "#B4AEB2"),
        )
        setTextColor(if (primary) Color.WHITE else Color.parseColor("#0D0A0B"))
    }

    private fun horizontalWeight(value: Float) =
        LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, value)

    private fun verticalWeight(value: Float) =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, value)

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    companion object {
        private const val MASTER_FOCUS = "focus:master"
        private const val ROW_FOCUS_PREFIX = "mod:"
        private const val RESET_FOCUS = "focus:reset"
        private const val PLUGINS_FOCUS = "focus:plugins"
        private const val BACK_FOCUS = "focus:back"
    }
}
