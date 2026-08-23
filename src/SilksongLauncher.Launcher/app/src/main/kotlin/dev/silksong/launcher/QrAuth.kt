// QrAuth — the Steam Guard mobile QR sign-in flow over JavaSteam.
//
// Flow:
//   1. session.connectAndWait()
//   2. authentication.beginAuthSessionViaQR(details) → QrAuthSession
//   3. Show session.challengeUrl as a QR code to the user; subscribe to
//      challengeUrlChanged so we re-render when Steam rotates the URL.
//   4. session.pollingWaitForResult() → AuthPollResult (account name +
//      refresh token) when the user confirms on their phone.
//
// All Steam I/O happens on the SteamSession's pump thread + JavaSteam's
// own coroutine dispatchers. The Kotlin Flow we return emits on the
// Main dispatcher so the LoginActivity can update UI directly.

package dev.silksong.launcher

import `in`.dragonbra.javasteam.enums.EOSType
import `in`.dragonbra.javasteam.steam.authentication.AuthPollResult
import `in`.dragonbra.javasteam.steam.authentication.AuthSessionDetails
import `in`.dragonbra.javasteam.steam.authentication.IChallengeUrlChanged
import `in`.dragonbra.javasteam.steam.authentication.QrAuthSession
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

object QrAuth {

    /** Phases emitted by [run]. */
    sealed class Event {
        /** First QR URL ready, or it just rotated. Render this into a QR code. */
        data class ChallengeUrl(val url: String) : Event()

        /** Steam has confirmed the scan on the phone; we're finishing the handshake. */
        data object Polling : Event()

        /** Successful sign-in. Persist these. */
        data class Success(val accountName: String, val refreshToken: String) : Event()
    }

    /**
     * Runs the QR sign-in to completion (or throws on cancellation/timeout/error).
     * Emits [Event]s as the flow progresses. Caller should treat the
     * first [Event.Success] as terminal.
     */
    fun run(session: SteamSession, deviceName: String): Flow<Event> = flow {
        session.connectAndWait()

        val auth = session.steamClient.authentication

        val details = AuthSessionDetails().apply {
            // Kotlin sees JavaSteam's Java getter+setter pair as a property.
            deviceFriendlyName = deviceName
            // IMPORTANT: leave platformType at default (SteamClient). The
            // refresh tokens that come back are bound to whatever platform
            // we authenticate as — and only SteamClient-platform tokens
            // can be passed to SteamUser.logOn(). If we set MobileApp
            // here the QR scan completes but the later logOn() returns
            // EResult.InvalidPassword.
            clientOSType = EOSType.Android9
        }

        LauncherLog.log("Requesting QR challenge…")
        val qrSession: QrAuthSession = auth.beginAuthSessionViaQR(details)
            .get(20, TimeUnit.SECONDS)
            ?: throw RuntimeException("beginAuthSessionViaQR returned null")

        // Emit the initial URL immediately so the UI can render the QR.
        emit(Event.ChallengeUrl(qrSession.challengeUrl))

        // Steam rotates the URL periodically (~30s). We can't suspend
        // inside a non-suspending JavaSteam callback, so we publish
        // the latest URL through an AtomicReference and re-emit on
        // the polling loop below.
        val latestUrl = AtomicReference(qrSession.challengeUrl)
        qrSession.challengeUrlChanged = IChallengeUrlChanged { s ->
            // s is a Java platform type — assume non-null (JavaSteam never
            // passes null to this callback).
            latestUrl.set(s!!.challengeUrl)
            LauncherLog.log("QR URL rotated")
        }

        // pollingWaitForResult() blocks the calling thread until the
        // user scans + confirms, or the session times out. Run it on
        // a worker thread; the outer flow's coroutine context handles
        // cancellation by interrupting back here.
        val resultRef = AtomicReference<AuthPollResult?>(null)
        val errorRef = AtomicReference<Throwable?>(null)
        val worker = Thread({
            try {
                resultRef.set(qrSession.pollingWaitForResult().get())
            } catch (t: Throwable) {
                errorRef.set(t)
            }
        }, "QrAuth-poll").apply { isDaemon = true; start() }

        // While the worker is polling, periodically re-emit the
        // current URL so the UI re-renders if it rotated.
        var lastEmitted = latestUrl.get()
        while (worker.isAlive) {
            val current = latestUrl.get()
            if (current != lastEmitted) {
                emit(Event.ChallengeUrl(current))
                lastEmitted = current
            }
            try {
                Thread.sleep(500)
            } catch (_: InterruptedException) {
                break
            }
        }

        worker.join()
        errorRef.get()?.let { throw it }
        val result = resultRef.get() ?: throw RuntimeException("polling returned no result")

        emit(Event.Polling)
        LauncherLog.log("Signed in")
        emit(Event.Success(result.accountName, result.refreshToken))
    }.flowOn(Dispatchers.IO)
}
