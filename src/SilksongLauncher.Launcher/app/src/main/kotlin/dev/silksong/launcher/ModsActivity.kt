// ModsActivity — what is installed, what worked, and what to do about it.
//
// The screen exists because of one property of this design: mods are compiled
// into the game rather than loaded by it, so a mod that cannot work fails at
// BUILD time. That is a real advantage over a runtime loader -- the failure is
// a line of text before anything is built, rather than a crash twenty minutes
// later on a handheld with no console -- but only if somebody is shown it.
//
// So each plugin is listed with what the weaver made of it: how many patches
// it applied, and every one it could not. The toggle takes effect the next
// time the game starts and costs nothing -- every plugin here is already
// compiled in, and the switch only decides whether its gate is opened. Adding
// or removing a file is the change that needs a rebuild, and the screen says
// so rather than letting the next launch quietly be the old build.

package dev.silksong.launcher

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.SelectedGameStore
import java.io.File

class ModsActivity : Activity() {

    private lateinit var list: LinearLayout
    private lateinit var mods: File
    private val profile by lazy { SelectedGameStore(this).get() }
    private val buildPaths by lazy {
        ProfileBuildPaths(
            filesDir,
            requireNotNull(getExternalFilesDir(null)) { "No external files directory" },
            profile,
        )
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        mods = Mods.dir(this)
        runCatching { Mods.ensure(mods) }
        setContentView(buildUi())
        populate()
    }

    override fun onResume() {
        super.onResume()
        // The folder is edited from outside this app -- over USB, or in a file
        // manager -- so what was on screen a minute ago is not evidence of
        // anything.
        populate()
    }

    private fun buildUi(): View {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(20), dp(20), dp(20), dp(20))
        }

        root.addView(TextView(this).apply {
            text = "Mods"
            setTextColor(Color.WHITE)
            textSize = 22f
            setTypeface(typeface, Typeface.BOLD)
        })

        root.addView(TextView(this).apply {
            text = "BepInEx 5 plugins, compiled into the game. Put DLLs in\n" +
                mods.absolutePath + "\nAdding, removing or disabling one takes effect on the next build."
            setTextColor(Color.parseColor("#7A6E71"))
            textSize = 12f
            setPadding(0, dp(4), 0, dp(12))
        })

        val scroll = ScrollView(this).apply { isFillViewport = true }
        list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(list)
        root.addView(
            scroll,
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f),
        )

        val buttons = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.END
            setPadding(0, dp(12), 0, 0)
        }
        buttons.addView(
            button("Rebuild", primary = true) { rebuild() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginEnd = dp(10)
            },
        )
        buttons.addView(
            button("Close") { finish() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
        )
        root.addView(buttons)

        return root
    }

    private fun populate() {
        list.removeAllViews()

        val found = Mods.all(mods)
        if (found.isEmpty()) {
            list.addView(note("Nothing installed yet."))
            list.addView(
                note(
                    "Transpilers, patch targets chosen at runtime and Reflection.Emit " +
                        "cannot work here; everything else generally does.",
                ),
            )
            return
        }

        val off = Mods.disabled(mods)
        val root = buildPaths.buildRoot
        // Keyed by file name, because that is what the report carries and it
        // is not always what the assembly inside is called.
        val reports = Mods.lastReport(root).associateBy { it.file }

        if (Mods.isStale(mods, root, assets)) {
            list.addView(
                note(
                    "A mod has been added, replaced or removed since the last build. " +
                        "Rebuild to apply it. Switching one on or off does not need one.",
                ),
            )
        }

        for (dll in found) {
            val relative = Mods.relativePath(mods, dll)
            list.addView(row(relative, reports[dll.name], relative !in off))
        }
    }

    private fun row(relative: String, report: Mods.Plugin?, enabled: Boolean): View {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), dp(10), dp(12), dp(10))
            setBackgroundColor(Color.parseColor("#161112"))
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        header.addView(
            TextView(this).apply {
                text = report?.title ?: relative
                setTextColor(Color.WHITE)
                textSize = 15f
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
        )
        @Suppress("DEPRECATION")
        header.addView(
            Switch(this).apply {
                isChecked = enabled
                setOnCheckedChangeListener { _, checked ->
                    Mods.setEnabled(mods, buildPaths.buildRoot, relative, checked)
                    LauncherLog.log("mods: $relative ${if (checked) "enabled" else "disabled"}")
                    populate()
                }
            },
        )
        card.addView(header)

        card.addView(
            TextView(this).apply {
                text = summary(relative, report, enabled)
                setTextColor(Color.parseColor("#7A6E71"))
                textSize = 11f
            },
        )

        for (issue in report?.issues.orEmpty()) {
            card.addView(
                TextView(this).apply {
                    text = "• $issue"
                    setTextColor(Color.parseColor("#C88A94"))
                    textSize = 11f
                    setPadding(0, dp(2), 0, 0)
                },
            )
        }

        val wrapper = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, 0, 0, dp(10))
        }
        wrapper.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        return wrapper
    }

    private fun summary(relative: String, report: Mods.Plugin?, enabled: Boolean): String {
        if (!enabled) return "$relative — off"
        if (report == null) return "$relative — not built yet"
        val version = if (report.version.isEmpty()) "" else " v${report.version}"
        val patches = if (report.patched == 1) "1 patch" else "${report.patched} patches"
        return when (report.status) {
            "Ok" -> "$relative$version — $patches applied"
            "Partial" -> "$relative$version — $patches applied, with problems"
            else -> "$relative$version — not built"
        }
    }

    private fun note(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#7A6E71"))
        textSize = 12f
        setPadding(0, 0, 0, dp(10))
    }

    /**
     * Back to the build screen, which already knows how to do the least work
     * that would apply the change.
     */
    private fun rebuild() {
        try {
            startActivity(Intent(this, SetupActivity::class.java))
            finish()
        } catch (t: Throwable) {
            Toast.makeText(this, "Could not open the build screen: ${t.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun button(label: String, primary: Boolean = false, onClick: () -> Unit) =
        Button(this).apply {
            text = label
            setOnClickListener { onClick() }
            backgroundTintList = android.content.res.ColorStateList.valueOf(
                Color.parseColor(if (primary) "#7D3341" else "#B4AEB2"),
            )
            setTextColor(if (primary) Color.WHITE else Color.parseColor("#0D0A0B"))
            setPadding(paddingLeft, dp(12), paddingRight, dp(12))
        }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
}
