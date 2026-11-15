package dev.silksong.launcher

import android.app.Activity
import android.app.Presentation
import android.graphics.Color
import android.graphics.Typeface
import android.hardware.display.DisplayManager
import android.util.TypedValue
import android.view.View
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

/**
 * The live launcher log, on the lower display, while a launcher screen is in front.
 *
 * The home screen no longer carries the raw log; on a dual-screen device the second display would otherwise
 * show the system home screen while the launcher is open. Read-only and non-focusable, so it never takes
 * controller focus. Shown from onResume and dismissed from onPause, so the game always gets the display back
 * before it starts. A device without a presentation display simply gets nothing.
 */
class LowerDisplayLog(
    private val activity: Activity,
    private val header: () -> String,
) {
    private var presentation: Presentation? = null
    private var title: TextView? = null
    private var body: TextView? = null
    private var scroll: ScrollView? = null
    private val listener = LauncherLog.Listener { _, full -> activity.runOnUiThread { render(full) } }

    fun show() {
        if (presentation != null || activity.isFinishing) return
        val display = activity.getSystemService(DisplayManager::class.java)
            ?.getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION)
            ?.firstOrNull() ?: return
        val shown = runCatching {
            Presentation(activity, display).apply {
                setContentView(buildContent(context))
                setCancelable(false)
                show()
            }
        }.onFailure { android.util.Log.w("SilksongLauncher", "lower display unavailable", it) }.getOrNull() ?: return
        presentation = shown
        LauncherLog.addListener(listener)
        render(LauncherLog.snapshot())
    }

    fun hide() {
        LauncherLog.removeListener(listener)
        runCatching { presentation?.dismiss() }
        presentation = null
        title = null
        body = null
        scroll = null
    }

    /** Re-reads [header]; call when the state it describes changes without a log line. */
    fun refreshHeader() {
        title?.text = header()
    }

    private fun render(full: List<String>) {
        val text = body ?: return
        title?.text = header()
        text.text = full.takeLast(MAX_LINES).joinToString("\n")
        scroll?.post { scroll?.fullScroll(View.FOCUS_DOWN) }
    }

    private fun buildContent(context: android.content.Context): View {
        fun dp(value: Int) = (value * context.resources.displayMetrics.density).toInt()
        val root = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(24), dp(18), dp(24), dp(18))
            isFocusable = false
            descendantFocusability = LinearLayout.FOCUS_BLOCK_DESCENDANTS
        }
        root.addView(TextView(context).apply {
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 18f)
            setTypeface(typeface, Typeface.BOLD)
            text = header()
            title = this
        })
        root.addView(TextView(context).apply {
            setTextColor(Color.parseColor("#7A6E71"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setText(R.string.lower_display_title)
            setPadding(0, dp(2), 0, dp(10))
        })
        val logView = TextView(context).apply {
            setTextColor(Color.parseColor("#B5A9AC"))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            typeface = Typeface.MONOSPACE
            isFocusable = false
            body = this
        }
        root.addView(ScrollView(context).apply {
            isFocusable = false
            isVerticalScrollBarEnabled = false
            addView(logView)
            scroll = this
        }, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))
        return root
    }

    private companion object {
        const val MAX_LINES = 300
    }
}
