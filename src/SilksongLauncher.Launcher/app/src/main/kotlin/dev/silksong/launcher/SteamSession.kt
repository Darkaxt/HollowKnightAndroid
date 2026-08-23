// SteamSession — JavaSteam wrapper used by Phase 1 features
// (QR auth + cloud-save list/push/pull).
//
// Owns a single SteamClient + CallbackManager + a pump thread that
// drives runWaitCallbacks. Subscribers register callback handlers and
// receive them on the pump thread (NOT the main thread — UI code must
// post back to the main thread itself).
//
// Lifecycle:
//   connectAndWait(timeout) → blocks until ConnectedCallback fires,
//                             or throws on disconnect/timeout
//   logOn(creds, timeout)   → connect (if needed) + LogOn with the
//                             refresh token from QR auth; blocks until
//                             LoggedOnCallback fires
//   disconnect()           → graceful shutdown; idempotent
//   close()                → disconnect + stop the pump thread; do not
//                            reuse the instance afterwards
//
// We deliberately do not share a single SteamSession across the
// entire app — each user-initiated action (login, list, push, pull)
// opens its own short-lived session. That keeps state simple and
// matches how the .NET launcher does it.

package dev.silksong.launcher

import `in`.dragonbra.javasteam.enums.EResult
import `in`.dragonbra.javasteam.steam.handlers.steamapps.License
import `in`.dragonbra.javasteam.steam.handlers.steamapps.callback.LicenseListCallback
import `in`.dragonbra.javasteam.steam.handlers.steamuser.LogOnDetails
import `in`.dragonbra.javasteam.steam.handlers.steamuser.SteamUser
import `in`.dragonbra.javasteam.steam.handlers.steamuser.callback.LoggedOnCallback
import `in`.dragonbra.javasteam.steam.steamclient.SteamClient
import `in`.dragonbra.javasteam.steam.steamclient.callbackmgr.CallbackManager
import `in`.dragonbra.javasteam.steam.steamclient.callbackmgr.CallbackMsg
import `in`.dragonbra.javasteam.steam.steamclient.callbacks.ConnectedCallback
import `in`.dragonbra.javasteam.steam.steamclient.callbacks.DisconnectedCallback
import `in`.dragonbra.javasteam.util.compat.Consumer
import java.io.Closeable
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class SteamSession : Closeable {
    init {
        // Belt and braces: the Application does this at startup, but this
        // class is the gate to everything that needs it, and getting it wrong
        // fails a long way from the cause.
        SteamCrypto.install()
    }

    private val client = SteamClient()
    private val callbacks = CallbackManager(client)
    private val pumpRunning = AtomicBoolean(true)

    private val pump: Thread = Thread({
        while (pumpRunning.get() && !Thread.currentThread().isInterrupted) {
            try {
                callbacks.runWaitCallbacks(100L)
            } catch (_: InterruptedException) {
                // JavaSteam wraps callbacks in kotlinx-coroutines runBlocking;
                // interrupt propagates as InterruptedException here. That's
                // our exit signal — break the loop.
                break
            } catch (t: Throwable) {
                LauncherLog.log("Steam callback pump error: ${t.message}")
            }
        }
    }, "SteamSession-pump").apply {
        isDaemon = true
        start()
    }

    val steamClient: SteamClient get() = client

    /**
     * The account's licences, as Steam sends them shortly after logon.
     *
     * The depot downloader needs these to work out what the account may
     * download, and they arrive unprompted on their own callback rather than
     * in the logon reply, so they are captured as they go past.
     */
    private val licenseList = AtomicReference<List<License>>(emptyList())
    private val licensesArrived = CountDownLatch(1)

    init {
        subscribe(LicenseListCallback::class.java) { cb ->
            licenseList.set(cb.licenseList)
            licensesArrived.countDown()
        }
    }

    /** Blocks until the licence list has arrived, or gives up and returns what it has. */
    fun licenses(timeoutSeconds: Long = 15): List<License> {
        if (!licensesArrived.await(timeoutSeconds, TimeUnit.SECONDS)) {
            LauncherLog.log("No licence list after ${timeoutSeconds}s; continuing without it")
        }
        return licenseList.get()
    }

    /**
     * Registers a callback handler. The returned [Closeable] should be
     * used to unsubscribe when no longer needed.
     */
    fun <T : CallbackMsg> subscribe(
        type: Class<T>,
        handler: (T) -> Unit,
    ): Closeable {
        return callbacks.subscribe(type, Consumer<T> { handler(it) })
    }

    /**
     * Connects to a CM and blocks until ConnectedCallback fires.
     * Throws on disconnect or timeout. Safe to call once per session.
     */
    fun connectAndWait(timeoutSeconds: Long = 20) {
        val connected = CountDownLatch(1)
        val gotConnect = AtomicBoolean(false)

        val onConnect = subscribe(ConnectedCallback::class.java) {
            gotConnect.set(true)
            connected.countDown()
        }
        val onDisconnect = subscribe(DisconnectedCallback::class.java) { cb ->
            if (connected.count > 0) {
                LauncherLog.log("Steam disconnected before connect (user=${cb.isUserInitiated})")
                connected.countDown()
            }
        }

        try {
            LauncherLog.log("Connecting to Steam CM…")
            client.connect()
            if (!connected.await(timeoutSeconds, TimeUnit.SECONDS))
                throw RuntimeException("timeout waiting for ConnectedCallback")
            if (!gotConnect.get())
                throw RuntimeException("disconnected before connect")
            LauncherLog.log("Connected to Steam CM")
        } finally {
            onConnect.close()
            onDisconnect.close()
        }
    }

    /**
     * Convenience for "connect + LogOn with refresh-token credentials".
     * Blocks until LoggedOnCallback fires with a result; throws on
     * non-OK login result or timeout.
     *
     * Retries on EResult.TryAnotherCM, which is how Steam load-balances
     * — the contacted Connection Manager has told us to reconnect and
     * try a different one. JavaSteam's SteamClient.connect() picks a
     * CM from its internal server list each call, so a fresh
     * disconnect + connect routinely lands us on a different server.
     * We give it a few attempts before giving up.
     */
    fun logOn(
        credentials: TokenStore.Credentials,
        timeoutSeconds: Long = 20,
        maxAttempts: Int = 3,
    ) {
        var lastResult: EResult? = null
        for (attempt in 1..maxAttempts) {
            try {
                logOnOnce(credentials, timeoutSeconds)
                return
            } catch (t: LogOnRejected) {
                lastResult = t.result
                if (t.result != EResult.TryAnotherCM || attempt == maxAttempts) {
                    throw RuntimeException("LogOn failed: ${t.result}")
                }
                LauncherLog.log("LogOn returned TryAnotherCM (attempt $attempt/$maxAttempts), reconnecting…")
                try { client.disconnect() } catch (_: Throwable) { }
                // Brief backoff before the retry to give the CM dispatcher
                // a chance to rotate to a different server.
                try { Thread.sleep(500L * attempt) } catch (_: InterruptedException) {
                    Thread.currentThread().interrupt()
                    throw RuntimeException("LogOn interrupted")
                }
            }
        }
        throw RuntimeException("LogOn failed after $maxAttempts attempts (last: $lastResult)")
    }

    private class LogOnRejected(val result: EResult) : RuntimeException("LogOn rejected: $result")

    private fun logOnOnce(
        credentials: TokenStore.Credentials,
        timeoutSeconds: Long,
    ) {
        connectAndWait(timeoutSeconds)

        val steamUser: SteamUser = client.getHandler(SteamUser::class.java)
            ?: throw RuntimeException("SteamUser handler missing")

        val done = CountDownLatch(1)
        val result = AtomicReference<EResult?>(null)
        val onLoggedOn = subscribe(LoggedOnCallback::class.java) { cb ->
            result.set(cb.result)
            done.countDown()
        }

        try {
            LauncherLog.log("Logging on with the saved refresh token…")
            // LogOnDetails wraps the field-bag the protocol expects.
            // We populate just the bits relevant for refresh-token login:
            // username + accessToken. shouldRememberPassword=true mirrors
            // the .NET launcher; it doesn't affect security here (token
            // lives in TokenStore either way) but keeps Steam from
            // expiring our session prematurely.
            val details = LogOnDetails().apply {
                username = credentials.accountName
                accessToken = credentials.refreshToken
                shouldRememberPassword = true
            }
            steamUser.logOn(details)

            if (!done.await(timeoutSeconds, TimeUnit.SECONDS))
                throw RuntimeException("timeout waiting for LoggedOnCallback")
            val r = result.get() ?: throw RuntimeException("LoggedOnCallback returned null result")
            if (r != EResult.OK)
                throw LogOnRejected(r)
            LauncherLog.log("Logged on")
        } finally {
            onLoggedOn.close()
        }
    }

    fun disconnect() {
        try {
            client.disconnect()
        } catch (_: Throwable) {
            // ignore
        }
    }

    override fun close() {
        disconnect()
        pumpRunning.set(false)
        pump.interrupt()
        try {
            pump.join(2_000)
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }
}
