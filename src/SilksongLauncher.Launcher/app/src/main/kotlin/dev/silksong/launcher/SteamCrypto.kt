// SteamCrypto — makes "BC" mean the real BouncyCastle.
//
// Android ships its own cut-down provider under the name BC, and
// Security.insertProviderAt does nothing at all when a provider of that name
// is already installed -- it returns -1 rather than throwing. JavaSteam
// registers BouncyCastle that way and then asks for algorithms from "BC", so
// on Android it silently gets Android's provider instead, which does not have
// them. That surfaces much later as
//
//     NoSuchAlgorithmException: no such algorithm: SHA-1 for provider BC
//
// in the middle of a depot download, with nothing to connect it to a provider
// that failed to register at startup.
//
// Removing Android's first makes the insert succeed, so the name resolves to
// the full implementation. This has to happen before anything touches
// JavaSteam's CryptoHelper, whose static initialiser is what does the
// registering -- so it runs from Application.onCreate, before any Steam code
// is reachable.

package dev.silksong.launcher

import org.bouncycastle.jce.provider.BouncyCastleProvider
import java.security.Security

object SteamCrypto {

    private var installed = false

    @Synchronized
    fun install() {
        if (installed) return
        installed = true
        try {
            val existing = Security.getProvider(PROVIDER)
            if (existing != null && existing::class.java != BouncyCastleProvider::class.java) {
                Security.removeProvider(PROVIDER)
            }
            // Appended rather than inserted at the front, and the difference
            // is worth several megabytes a second. JavaSteam names this
            // provider explicitly, so it finds it wherever it sits; but
            // putting it first makes it the default for everything else too,
            // including the TLS underneath the CDN downloads -- which then
            // runs pure-Java AES instead of the platform's hardware-backed
            // implementation, and the download becomes CPU-bound.
            Security.addProvider(BouncyCastleProvider())
            val at = Security.getProviders().indexOfFirst { it.name == PROVIDER }
            LauncherLog.log("BouncyCastle available as \"$PROVIDER\" (position ${at + 1} of " +
                "${Security.getProviders().size})")
        } catch (t: Throwable) {
            LauncherLog.log("Could not install BouncyCastle: $t")
        }
    }

    private const val PROVIDER = "BC"
}
