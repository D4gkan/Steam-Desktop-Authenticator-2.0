package com.sda.mobile.data

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * Equivalent role to the desktop app's ICredentialStore implementations (Windows Credential
 * Manager / macOS Keychain / Linux Secret Service): stores a Steam account's password so the
 * app can silently re-login when a session expires, without the user re-typing it.
 *
 * Backed by EncryptedSharedPreferences, whose key is itself generated and held inside the
 * Android Keystore (hardware-backed on most devices) - the password ciphertext at rest is
 * useless without that key, which never leaves secure hardware. This is the Android-native
 * equivalent of a platform secret store, so there is no "not supported" case to handle here
 * the way the desktop Linux build has to (see LinuxSecretServiceCredentialStore on desktop) -
 * it's always available on API 26+.
 */
class CredentialStore(context: Context) {
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs = EncryptedSharedPreferences.create(
        context,
        "sda_saved_logins",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    fun savePassword(steamId64: Long, password: String) {
        prefs.edit().putString(keyFor(steamId64), password).apply()
    }

    fun getPassword(steamId64: Long): String? = prefs.getString(keyFor(steamId64), null)

    fun clearPassword(steamId64: Long) {
        prefs.edit().remove(keyFor(steamId64)).apply()
    }

    fun hasSavedPassword(steamId64: Long): Boolean = getPassword(steamId64) != null

    private fun keyFor(steamId64: Long) = "pw_$steamId64"
}
