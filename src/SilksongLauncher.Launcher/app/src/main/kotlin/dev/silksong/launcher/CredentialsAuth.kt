// CredentialsAuth — signing in with an account name and password.
//
// The companion to QrAuth, and not a replacement for it. QR sign-in needs the
// Steam mobile app, so an account guarded by an emailed code cannot use it at
// all: there is nothing to scan with. This is the route for those accounts,
// and for anyone who would simply rather type than reach for a phone.
//
// ── how Steam confirms it ───────────────────────────────────────────────────
//
// Almost every account with the mobile authenticator is confirmed by approving
// a prompt in the Steam app, and that is all this asks for. There is no code
// box in that case: the tap is the confirmation, and JavaSteam polls until it
// arrives.
//
// This is a decision made through IAuthenticator, and it is one-way.
// AuthSession.pollingWaitForResult asks acceptDeviceConfirmation() and commits:
//
//   * true  → poll for the phone tap, and never ask for a code at all.
//   * false → fall through to handleCodeAuth, which AWAITS a code and never
//             polls -- so tapping Approve in the app does nothing, forever.
//
// It answers true. An earlier version answered false, on the reasoning that the
// QR code on the same screen already covered "approve it on your phone", and
// the result was exactly the second bullet: the prompt arrived on the phone,
// approving it changed nothing, and the code had to be typed anyway.
//
// A code is still handled, because some accounts are offered nothing else --
// an email-guarded account never reaches the polling branch, and JavaSteam asks
// for the code the ordinary way. That is the only case in which the code box
// appears, because it is the only case in which it is the way in rather than an
// extra thing to read.

package dev.silksong.launcher

import `in`.dragonbra.javasteam.enums.EOSType
import `in`.dragonbra.javasteam.protobufs.steamclient.SteammessagesAuthSteamclient.EAuthSessionGuardType
import `in`.dragonbra.javasteam.steam.authentication.AuthPollResult
import `in`.dragonbra.javasteam.steam.authentication.AuthSessionDetails
import `in`.dragonbra.javasteam.steam.authentication.CredentialsAuthSession
import `in`.dragonbra.javasteam.steam.authentication.IAuthenticator
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.isActive
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutionException
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

object CredentialsAuth {

    /** Which code Steam is asking for, which decides what the prompt says. */
    enum class CodeKind { Device, Email }

    sealed class Event {
        /** Steam is waiting for the sign-in to be approved in the mobile app. */
        data object Approving : Event()

        /**
         * Steam wants a typed code. Only reached by accounts that cannot be
         * confirmed from the app at all; complete [answer] with what the user
         * types, or exceptionally to abandon the sign-in.
         */
        data class NeedCode(
            val kind: CodeKind,
            val email: String?,
            val previousWasWrong: Boolean,
            val answer: CompletableFuture<String>,
        ) : Event()

        /** Confirmed; we're finishing the handshake. */
        data object Polling : Event()

        /** Successful sign-in. Persist these. */
        data class Success(val accountName: String, val refreshToken: String) : Event()
    }

    fun run(
        session: SteamSession,
        username: String,
        password: String,
        deviceName: String,
    ): Flow<Event> = flow {
        session.connectAndWait()

        val auth = session.steamClient.authentication

        // Raised on JavaSteam's threads, drained by the loop below on ours,
        // because only that one may emit.
        val raised = LinkedBlockingQueue<Event>()
        // Held so a cancelled sign-in can release whoever is waiting on it.
        val outstanding = AtomicReference<CompletableFuture<String>?>(null)

        val authenticator = object : IAuthenticator {
            override fun getDeviceCode(previousCodeWasIncorrect: Boolean): CompletableFuture<String> =
                ask(CodeKind.Device, null, previousCodeWasIncorrect)

            override fun getEmailCode(
                email: String?,
                previousCodeWasIncorrect: Boolean,
            ): CompletableFuture<String> = ask(CodeKind.Email, email, previousCodeWasIncorrect)

            /** See the header: true, so the phone tap is actually watched for. */
            override fun acceptDeviceConfirmation(): CompletableFuture<Boolean> =
                CompletableFuture.completedFuture(true)

            private fun ask(
                kind: CodeKind,
                email: String?,
                retry: Boolean,
            ): CompletableFuture<String> {
                val answer = CompletableFuture<String>()
                outstanding.set(answer)
                raised.add(Event.NeedCode(kind, email, retry, answer))
                return answer
            }
        }

        val details = AuthSessionDetails().apply {
            this.username = username
            this.password = password
            this.authenticator = authenticator
            // Kotlin sees JavaSteam's Java getter+setter pair as a property.
            deviceFriendlyName = deviceName
            // IMPORTANT: leave platformType at its default (SteamClient), for
            // exactly the reason spelled out in QrAuth -- a token issued to any
            // other platform is rejected by SteamUser.logOn() later, and the
            // failure lands nowhere near here.
            clientOSType = EOSType.Android9
        }

        LauncherLog.log("Signing in with an account name and password…")
        val credentialsSession: CredentialsAuthSession = unwrap {
            auth.beginAuthSessionViaCredentials(details).get(30, TimeUnit.SECONDS)
        } ?: throw RuntimeException("beginAuthSessionViaCredentials returned null")

        val canApprove = credentialsSession.allowedConfirmations.any {
            it.confirmationType == EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation
        }

        // pollingWaitForResult() blocks until Steam is satisfied, including
        // however long the user takes to reach for their phone, so it cannot
        // run on the flow's thread.
        val resultRef = AtomicReference<AuthPollResult?>(null)
        val errorRef = AtomicReference<Throwable?>(null)
        val worker = Thread({
            try {
                resultRef.set(credentialsSession.pollingWaitForResult().get())
            } catch (t: Throwable) {
                errorRef.set(t)
            }
        }, "CredentialsAuth-poll").apply { isDaemon = true; start() }

        try {
            if (canApprove) emit(Event.Approving)

            // delay rather than Thread.sleep, and isActive rather than trusting
            // the worker: cancelling the sign-in has to unwind promptly even
            // though the thread above is blocked on Steam, and only a
            // cancellable suspend point makes that happen.
            while (worker.isAlive && currentCoroutineContext().isActive) {
                raised.poll()?.let { emit(it) }
                delay(200)
            }
            // Drain whatever arrived in the last tick before the worker exited.
            raised.poll()?.let { emit(it) }
        } finally {
            // A cancelled sign-in must not leave JavaSteam blocked on a code
            // that is never coming.
            outstanding.get()?.completeExceptionally(RuntimeException("sign-in cancelled"))
        }

        worker.join()
        errorRef.get()?.let { throw unwrapped(it) }
        val result = resultRef.get() ?: throw RuntimeException("polling returned no result")

        emit(Event.Polling)
        LauncherLog.log("Signed in")
        emit(Event.Success(result.accountName, result.refreshToken))
    }.flowOn(Dispatchers.IO)

    /**
     * Runs [block], reporting the cause rather than the wrapper.
     *
     * A wrong password comes back as ExecutionException wrapping
     * AuthenticationException wrapping "InvalidPassword", and the screen has
     * one line to say what went wrong. On a form whose most common failure is
     * a typo, showing the class names instead of the reason is not a small
     * thing.
     */
    private inline fun <T> unwrap(block: () -> T): T =
        try {
            block()
        } catch (e: ExecutionException) {
            throw unwrapped(e)
        }

    private fun unwrapped(t: Throwable): Throwable =
        if (t is ExecutionException && t.cause != null) t.cause!! else t
}
