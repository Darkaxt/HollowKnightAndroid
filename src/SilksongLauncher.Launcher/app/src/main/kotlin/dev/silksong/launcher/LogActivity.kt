// LogActivity — the log, on screen, in a form that can be sent to someone.
//
// This exists because of the bugs that only happen on hardware nobody working
// on the port owns. The launcher already kept a log, but it was memory in one
// process and a panel on one screen: by the time a user was asked for it, the
// app had been restarted and it was gone, and even when it was there it could
// not be got out of the phone.
//
// So: everything on disk (LauncherLog.attach), every session, and two ways out
// of the device. Share is first because it is the one that reaches another
// person -- mail, chat, an issue -- without anybody having to explain what a
// clipboard is on a handheld with no keyboard.
//
// Built in code rather than XML for the same reason SetupActivity is: it is one
// screen of three buttons and a scroller, and keeping it here means the whole
// thing is readable in one place.

package dev.silksong.launcher

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
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
import android.widget.TextView
import android.widget.Toast

class LogActivity : Activity() {

    private lateinit var text: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
        text.text = report()
    }

    /**
     * What actually gets sent.
     *
     * The device line goes first and is repeated here even though attach()
     * already wrote one per session: a log trimmed to its last 500 lines can
     * lose the header, and a report whose first question is "which phone?" is
     * a round trip nobody needs.
     */
    private fun report(): String {
        val history = LauncherLog.history(this)
        val body = if (history.isNotBlank()) history else LauncherLog.snapshot().joinToString("\n")
        return buildString {
            append("SilksongAndroid ").append(appVersion()).append('\n')
            append(LauncherLog.deviceSummary()).append('\n')
            append('\n')
            append(if (body.isBlank()) "(no log yet)" else body)
        }
    }

    /** Read from the package rather than a constant, so it cannot disagree with the APK. */
    private fun appVersion(): String =
        try {
            packageManager.getPackageInfo(packageName, 0).versionName ?: "?"
        } catch (t: Throwable) {
            "?"
        }

    private fun buildUi(): View {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(20), dp(20), dp(20), dp(20))
        }

        root.addView(TextView(this).apply {
            text = "Logs"
            setTextColor(Color.WHITE)
            textSize = 22f
            setTypeface(typeface, Typeface.BOLD)
        })

        root.addView(TextView(this).apply {
            text = "Everything the launcher has recorded, including previous runs. " +
                "Send this when reporting a problem."
            setTextColor(Color.parseColor("#7A6E71"))
            textSize = 12f
            setPadding(0, dp(4), 0, dp(12))
        })

        // Weight 1 so the scroller takes the space the buttons do not, rather
        // than the buttons being pushed off a long log.
        val scroll = ScrollView(this).apply { isFillViewport = true }
        text = TextView(this).apply {
            setTextColor(Color.parseColor("#B5A9AC"))
            textSize = 11f
            typeface = Typeface.MONOSPACE
            // The point of the screen: a long press selects, which is the only
            // copy-out that works when the share sheet has nothing useful on it.
            setTextIsSelectable(true)
        }
        scroll.addView(text)
        root.addView(scroll, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))

        val buttons = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.END
            setPadding(0, dp(12), 0, 0)
        }
        buttons.addView(button("Share", primary = true) { share() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginEnd = dp(10)
            })
        buttons.addView(button("Copy") { copy() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginEnd = dp(10)
            })
        buttons.addView(button("Close") { finish() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        root.addView(buttons)

        return root
    }

    private fun button(label: String, primary: Boolean = false, onClick: () -> Unit) =
        Button(this).apply {
            text = label
            setOnClickListener { onClick() }
            backgroundTintList = android.content.res.ColorStateList.valueOf(
                Color.parseColor(if (primary) "#7D3341" else "#B4AEB2"))
            setTextColor(if (primary) Color.WHITE else Color.parseColor("#0D0A0B"))
            setPadding(paddingLeft, dp(12), paddingRight, dp(12))
        }

    private fun share() {
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_SUBJECT, "SilksongAndroid log")
            putExtra(Intent.EXTRA_TEXT, report())
        }
        try {
            startActivity(Intent.createChooser(intent, "Send the log"))
        } catch (t: Throwable) {
            // A device with nothing that accepts text is unusual but not
            // impossible, and falling back beats an unexplained dead button.
            copy()
        }
    }

    private fun copy() {
        try {
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            clipboard.setPrimaryClip(ClipData.newPlainText("SilksongAndroid log", report()))
            Toast.makeText(this, "Log copied", Toast.LENGTH_SHORT).show()
        } catch (t: Throwable) {
            Toast.makeText(this, "Could not copy: ${t.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
}
