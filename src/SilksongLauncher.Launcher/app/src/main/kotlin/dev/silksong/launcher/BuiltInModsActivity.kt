package dev.silksong.launcher

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import dev.silksong.launcher.builtinmods.BuiltInModCatalog
import dev.silksong.launcher.builtinmods.BuiltInModDescriptor
import dev.silksong.launcher.builtinmods.BuiltInModsController
import dev.silksong.launcher.builtinmods.LineModStateStore
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.runtime.GameLifecycleAuthority
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
    private var selectedId: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
        selectedId = controller.snapshot().descriptors.firstOrNull()?.id
        render()
    }

    override fun onResume() {
        super.onResume()
        if (::list.isInitialized) {
            controller.reload()
            render()
        }
    }

    private fun buildUi(): LinearLayout {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(20), dp(16), dp(20), dp(16))
        }
        val header = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL }
        header.addView(label("MODS — ${profile.displayName.uppercase()}", 22f, bold = true), weight(1f))
        master = action("", primary = true) {
            show(controller.setMaster(!controller.snapshot().masterEnabled).message)
        }.apply { id = R.id.btn_mods_master }
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
        details.addView(detail, weight(1f))
        details.addView(label("Tap a row to select it; tap it again to change its value.", 11f, Color.parseColor("#7A6E71")))
        details.addView(status)
        details.addView(action("RESET ALL MODS") { show(controller.reset().message) }.apply { id = R.id.btn_reset_mods })
        details.addView(action("PLUGINS") {
            startActivity(Intent(this@BuiltInModsActivity, ModsActivity::class.java))
        }.apply { id = R.id.btn_plugins })
        details.addView(action("BACK") { finish() })
        body.addView(details, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 0.45f))
        root.addView(body, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        return root
    }

    private fun render() {
        val snapshot = controller.snapshot()
        master.text = if (snapshot.masterEnabled) "MASTER · ON" else "MASTER · OFF"
        list.removeAllViews()
        var group: String? = null
        snapshot.descriptors.forEach { descriptor ->
            if (group != descriptor.group) {
                group = descriptor.group
                list.addView(label(descriptor.group, 11f, Color.parseColor("#7A6E71"), bold = true).apply {
                    tag = "group"
                    setPadding(dp(8), dp(10), dp(8), dp(2))
                })
            }
            list.addView(modRow(descriptor, snapshot.value(descriptor.id)))
        }
        val selected = snapshot.descriptors.firstOrNull { it.id == selectedId }
        detail.text = selected?.let {
            "${it.title}\n\nVALUE  ${BuiltInModsController.friendly(snapshot.value(it.id))}\n\n${it.description}"
        }.orEmpty()
    }

    private fun modRow(descriptor: BuiltInModDescriptor, value: String): LinearLayout =
        LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            isClickable = true
            isFocusable = true
            setPadding(dp(12), dp(10), dp(12), dp(10))
            setBackgroundColor(Color.parseColor(if (selectedId == descriptor.id) "#2A2022" else "#161112"))
            addView(label(descriptor.title, 15f, if (controller.snapshot().masterEnabled) Color.WHITE else Color.GRAY), weight(1f))
            addView(label(BuiltInModsController.friendly(value), 14f, Color.parseColor("#C88A94")))
            setOnClickListener {
                if (selectedId != descriptor.id) {
                    selectedId = descriptor.id
                    status.text = ""
                    render()
                } else {
                    show(controller.cycle(descriptor.id).message)
                }
            }
        }

    private fun show(message: String) {
        status.text = message
        render()
        status.text = message
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
        setOnClickListener { click() }
        backgroundTintList = android.content.res.ColorStateList.valueOf(
            Color.parseColor(if (primary) "#7D3341" else "#B4AEB2"),
        )
        setTextColor(if (primary) Color.WHITE else Color.parseColor("#0D0A0B"))
    }

    private fun weight(value: Float) = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, value)
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
}
