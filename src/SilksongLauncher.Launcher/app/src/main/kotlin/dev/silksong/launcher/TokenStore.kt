// TokenStore — minimal encrypted credential storage for the Steam
// access token + account name returned by QR sign-in.
//
// We deliberately do NOT pull in androidx.security:security-crypto
// (the canonical EncryptedSharedPreferences) because that drags in
// the entire androidx ecosystem (~10 transitive deps), conflicts with
// kotlinx-coroutines version selection, and we only need to encrypt
// two short strings. Hand-rolling AES-256-GCM with a key from the
// Android Keystore (`AndroidKeyStore` provider) gives us TEE-backed
// key protection in ~80 lines.
//
// Storage format per slot:
//   prefs[<key>]      = base64(iv || ciphertext)  // 12-byte GCM IV prepended
//   prefs[<key>__]    = "" (presence marker — also useful for clear())
//
// The key alias "silksong_launcher_v1" is generated lazily on first
// write and never rotated; tokens can be re-acquired by re-signing in.

package dev.silksong.launcher

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class TokenStore(context: Context) {

    private val prefs = context.applicationContext
        .getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    /**
     * Reads previously persisted Steam credentials. Returns null if
     * either field is missing or decryption fails (e.g. user wiped
     * keystore via factory reset — token can't be recovered, treat as
     * "logged out").
     */
    fun read(): Credentials? {
        val account = readString(KEY_ACCOUNT) ?: return null
        val token = readString(KEY_TOKEN) ?: return null
        return Credentials(account, token)
    }

    fun write(creds: Credentials) {
        writeString(KEY_ACCOUNT, creds.accountName)
        writeString(KEY_TOKEN, creds.refreshToken)
    }

    fun clear() {
        prefs.edit().clear().apply()
        // Also drop the keystore key so a future write generates a
        // fresh one (defence in depth: even if the old prefs file
        // leaks, the key it was encrypted under is gone).
        runCatching {
            val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
            ks.deleteEntry(KEY_ALIAS)
        }
    }

    data class Credentials(val accountName: String, val refreshToken: String)

    // ── internals ──────────────────────────────────────────────────────

    private fun readString(name: String): String? {
        val encoded = prefs.getString(name, null) ?: return null
        return try {
            val blob = Base64.decode(encoded, Base64.NO_WRAP)
            require(blob.size >= GCM_IV_BYTES + 1) { "ciphertext too short" }
            val iv = blob.copyOfRange(0, GCM_IV_BYTES)
            val ct = blob.copyOfRange(GCM_IV_BYTES, blob.size)
            val cipher = Cipher.getInstance(TRANSFORM)
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), GCMParameterSpec(GCM_TAG_BITS, iv))
            String(cipher.doFinal(ct), Charsets.UTF_8)
        } catch (_: Throwable) {
            // Tampered, key missing, etc. — treat as no credential.
            null
        }
    }

    private fun writeString(name: String, value: String) {
        val cipher = Cipher.getInstance(TRANSFORM).apply {
            init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        }
        val iv = cipher.iv // GCM generates a fresh 12-byte IV per encryption.
        val ct = cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        val blob = iv + ct
        prefs.edit().putString(name, Base64.encodeToString(blob, Base64.NO_WRAP)).apply()
    }

    private fun getOrCreateKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        val gen = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            "AndroidKeyStore",
        )
        gen.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return gen.generateKey()
    }

    companion object {
        private const val PREFS_NAME = "silksong_launcher_creds_v1"
        private const val KEY_ALIAS = "silksong_launcher_v1"
        private const val KEY_ACCOUNT = "account"
        private const val KEY_TOKEN = "token"
        private const val TRANSFORM = "AES/GCM/NoPadding"
        private const val GCM_IV_BYTES = 12
        private const val GCM_TAG_BITS = 128
    }
}
