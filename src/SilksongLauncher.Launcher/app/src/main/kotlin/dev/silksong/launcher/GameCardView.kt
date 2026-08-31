package dev.silksong.launcher

import android.content.Context
import android.util.AttributeSet
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.Checkable
import android.widget.FrameLayout
import android.widget.RadioButton

/** A whole-card radio control with room for supplied art and status text. */
class GameCardView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0,
) : FrameLayout(context, attrs, defStyleAttr), Checkable {
    private var checked = false

    override fun isChecked(): Boolean = checked

    override fun setChecked(value: Boolean) {
        if (checked == value) return
        checked = value
        refreshDrawableState()
        sendAccessibilityEvent(AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED)
    }

    override fun toggle() {
        setChecked(!checked)
    }

    override fun onCreateDrawableState(extraSpace: Int): IntArray {
        val state = super.onCreateDrawableState(extraSpace + 1)
        if (checked) mergeDrawableStates(state, CHECKED_STATE_SET)
        return state
    }

    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        info.className = RadioButton::class.java.name
        info.isCheckable = true
        info.isChecked = checked
    }

    private companion object {
        val CHECKED_STATE_SET = intArrayOf(android.R.attr.state_checked)
    }
}
