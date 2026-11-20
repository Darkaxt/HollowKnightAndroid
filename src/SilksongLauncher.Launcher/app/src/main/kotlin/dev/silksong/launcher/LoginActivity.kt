// LoginActivity — the Steam sign-in screen launched by LauncherActivity.
//
// Two ways in, both live at once: the QR code for anyone with the Steam mobile
// app, and account name + password for everyone else. The second is not a
// convenience. QR sign-in is a feature of the mobile authenticator, so an
// account guarded by an emailed code has nothing to scan with and cannot use
// this screen at all without it.
//
// Only one Steam session runs at a time. Signing in with a password tears the
// QR session down first -- leaving a dead QR on screen would be inviting the
// user to scan something that can no longer complete.
//
// Result protocol:
//   RESULT_OK   + intent extras EXTRA_ACCOUNT, EXTRA_TOKEN → caller persists
//   RESULT_CANCELED                                        → user cancelled
//                                                            or error occurred

package dev.silksong.launcher

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.os.Bundle
import android.view.View
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class LoginActivity : Activity() {

    companion object {
        const val EXTRA_ACCOUNT = "account_name"
        const val EXTRA_TOKEN = "refresh_token"
        private const val DEVICE_NAME = "Silksong Launcher (Android)"

        /**
         * #33: Steam ends an unscanned QR session after about a minute. Each
         * end brings a fresh QR this many times (about ten minutes), then the
         * screen clears the code and offers a new one on request instead of
         * keeping the connection, and the screen, alive indefinitely.
         */
        private const val MAX_QR_RENEWALS = 10
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var session: SteamSession? = null
    private var authJob: Job? = null

    /** Set while Steam is waiting for a Guard code the user has not typed yet. */
    private var submitCode: ((String) -> Unit)? = null

    private lateinit var imgQr: ImageView
    private lateinit var progress: ProgressBar
    private lateinit var status: TextView
    private lateinit var btnCancel: Button
    private lateinit var btnSignIn: Button
    private lateinit var editUsername: EditText
    private lateinit var editPassword: EditText
    private lateinit var rowGuard: View
    private lateinit var editGuard: EditText
    private lateinit var btnGuard: Button
    private lateinit var btnQrRetry: Button

    /** Fresh QR sessions started since the last one the user asked for. */
    private var qrRenewals = 0

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        setContentView(R.layout.activity_login)

        imgQr = findViewById(R.id.img_qr)
        progress = findViewById(R.id.login_progress)
        status = findViewById(R.id.txt_login_status)
        btnCancel = findViewById(R.id.btn_cancel)
        btnSignIn = findViewById(R.id.btn_sign_in)
        editUsername = findViewById(R.id.edit_username)
        editPassword = findViewById(R.id.edit_password)
        rowGuard = findViewById(R.id.row_guard)
        editGuard = findViewById(R.id.edit_guard)
        btnGuard = findViewById(R.id.btn_guard)
        btnQrRetry = findViewById(R.id.btn_qr_retry)

        btnCancel.setOnClickListener { cancelAndFinish() }
        btnSignIn.setOnClickListener { startCredentials() }
        btnGuard.setOnClickListener { submitGuard() }
        btnQrRetry.setOnClickListener {
            qrRenewals = 0
            stopAuth()
            startQr()
        }

        editPassword.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) { startCredentials(); true } else false
        }
        editGuard.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) { submitGuard(); true } else false
        }

        startQr()
    }

    private val lowerDisplay by lazy { LowerDisplayLog.forScreen(this, "Steam sign-in") }

    override fun onResume() {
        super.onResume()
        lowerDisplay.show()
    }

    override fun onPause() {
        lowerDisplay.hide()
        super.onPause()
    }

    override fun onDestroy() {
        submitCode = null
        scope.cancel()
        session?.close()
        super.onDestroy()
    }

    // ── QR ──────────────────────────────────────────────────────────────────

    private fun startQr() {
        btnQrRetry.visibility = View.GONE
        progress.visibility = View.VISIBLE
        status.text = getString(
            if (qrRenewals > 0) R.string.login_status_qr_renewing else R.string.login_status_connecting,
        )

        val s = SteamSession()
        session = s

        authJob = scope.launch {
            try {
                QrAuth.run(s, deviceName = DEVICE_NAME)
                    .onEach { event -> handleQrEvent(event) }
                    .catch { t -> onQrEnded(t) }
                    .collect { /* handled via onEach */ }
            } finally {
                s.close()
                // Only if nothing has replaced it: a password sign-in tears
                // this session down and installs its own, and it must not be
                // cleared out from under itself by the job it just cancelled.
                if (session === s) session = null
            }
        }
    }

    private suspend fun handleQrEvent(event: QrAuth.Event) = withContext(Dispatchers.Main) {
        when (event) {
            is QrAuth.Event.ChallengeUrl -> {
                progress.visibility = View.GONE
                status.text = getString(R.string.login_status_waiting)
                val bmp: Bitmap = QrRenderer.render(event.url, sizePx = 800)
                imgQr.setImageBitmap(bmp)
            }
            QrAuth.Event.Polling -> {
                status.text = getString(R.string.login_status_polling)
            }
            is QrAuth.Event.Success -> succeed(event.accountName, event.refreshToken)
        }
    }

    /**
     * #33: an unscanned QR session that Steam ended is replaced with a new one
     * (up to [MAX_QR_RENEWALS]); after that, or on any other failure, the dead
     * code is cleared and "New QR code" offered.
     */
    private fun onQrEnded(t: Throwable) {
        if (isFinishing || isDestroyed) return
        imgQr.setImageBitmap(null)
        if (t is QrAuth.SessionEnded && qrRenewals < MAX_QR_RENEWALS) {
            qrRenewals++
            LauncherLog.log("Renewing the QR code ($qrRenewals/$MAX_QR_RENEWALS)")
            startQr()
            return
        }
        if (t is QrAuth.SessionEnded) {
            LauncherLog.log("QR code expired after $qrRenewals renewals; waiting for New QR code")
            submitCode = null
            progress.visibility = View.GONE
            status.text = getString(R.string.login_status_qr_expired)
        } else {
            showError(t)
        }
        btnQrRetry.visibility = View.VISIBLE
        btnQrRetry.requestFocus()
    }

    // ── account name + password ─────────────────────────────────────────────

    private fun startCredentials() {
        val username = editUsername.text.toString().trim()
        val password = editPassword.text.toString()
        if (username.isEmpty() || password.isEmpty()) {
            status.text = getString(R.string.login_status_need_credentials)
            return
        }

        hideKeyboard()
        stopAuth()
        btnQrRetry.visibility = View.GONE

        // The QR belonged to the session just closed, so it can no longer be
        // completed by scanning it.
        imgQr.setImageBitmap(null)
        rowGuard.visibility = View.GONE
        progress.visibility = View.VISIBLE
        status.text = getString(R.string.login_status_signing_in)

        val s = SteamSession()
        session = s

        authJob = scope.launch {
            try {
                CredentialsAuth.run(s, username, password, DEVICE_NAME)
                    .onEach { event -> handleCredentialsEvent(event) }
                    .catch { t -> showError(t) }
                    .collect { /* handled via onEach */ }
            } finally {
                s.close()
                if (session === s) session = null
            }
        }
    }

    private suspend fun handleCredentialsEvent(event: CredentialsAuth.Event) =
        withContext(Dispatchers.Main) {
            when (event) {
                CredentialsAuth.Event.Approving -> {
                    // No code box: the tap on the phone is the confirmation.
                    submitCode = null
                    rowGuard.visibility = View.GONE
                    progress.visibility = View.VISIBLE
                    status.text = getString(R.string.login_guard_approve)
                }
                is CredentialsAuth.Event.NeedCode -> {
                    // Only accounts Steam offers no approval for reach this.
                    val answer = event.answer
                    submitCode = { code -> answer.complete(code) }
                    progress.visibility = View.GONE
                    rowGuard.visibility = View.VISIBLE
                    editGuard.text.clear()
                    editGuard.requestFocus()
                    status.text = when {
                        event.previousWasWrong -> getString(R.string.login_guard_retry)
                        event.kind == CredentialsAuth.CodeKind.Device ->
                            getString(R.string.login_guard_device)
                        !event.email.isNullOrBlank() ->
                            getString(R.string.login_guard_email, event.email)
                        else -> getString(R.string.login_guard_email_no_address)
                    }
                }
                CredentialsAuth.Event.Polling -> {
                    rowGuard.visibility = View.GONE
                    status.text = getString(R.string.login_status_polling)
                }
                is CredentialsAuth.Event.Success -> succeed(event.accountName, event.refreshToken)
            }
        }

    private fun submitGuard() {
        val submit = submitCode ?: return
        val code = editGuard.text.toString().trim()
        if (code.isEmpty()) return

        submitCode = null
        hideKeyboard()
        rowGuard.visibility = View.GONE
        progress.visibility = View.VISIBLE
        status.text = getString(R.string.login_status_signing_in)
        submit(code)
    }

    // ── shared ──────────────────────────────────────────────────────────────

    private fun succeed(accountName: String, refreshToken: String) {
        progress.visibility = View.GONE
        rowGuard.visibility = View.GONE
        status.text = getString(R.string.login_status_done, accountName)
        setResult(RESULT_OK, Intent().apply {
            putExtra(EXTRA_ACCOUNT, accountName)
            putExtra(EXTRA_TOKEN, refreshToken)
        })
        // Brief beat so the user sees "Signed in as …" before the Activity
        // finishes.
        status.postDelayed({ finish() }, 700)
    }

    private fun showError(t: Throwable) {
        LauncherLog.log("Login error: ${LoginErrors.describe(t)}")
        submitCode = null
        progress.visibility = View.GONE
        rowGuard.visibility = View.GONE
        status.text = getString(
            R.string.login_status_error,
            LoginErrors.userReason(t) ?: getString(R.string.login_error_connection),
        )
    }

    /** Cancels whichever flow is running and releases anything waiting on it. */
    private fun stopAuth() {
        submitCode = null
        authJob?.cancel()
        authJob = null
    }

    private fun hideKeyboard() {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as? InputMethodManager
        imm?.hideSoftInputFromWindow(status.windowToken, 0)
    }

    private fun cancelAndFinish() {
        stopAuth()
        setResult(RESULT_CANCELED)
        finish()
    }
}
