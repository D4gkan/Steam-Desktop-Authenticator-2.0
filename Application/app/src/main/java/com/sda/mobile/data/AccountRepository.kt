package com.sda.mobile.data

import android.content.Context
import com.sda.mobile.crypto.FileEncryptor
import com.sda.mobile.model.Manifest
import com.sda.mobile.model.ManifestEntry
import com.sda.mobile.model.SteamGuardAccount
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Port of Core/Manifest.cs. Same responsibilities, same on-disk shapes (manifest.json + one
 * .maFile per account, in a "maFiles" directory) so a manifest/.maFile pair exported from the
 * desktop app - or transferred over the QR flow - loads here unmodified, and vice versa.
 *
 * Storage lives in the app's private files directory (Context.filesDir), which on Android is
 * sandboxed to this app and excluded from Android backups (see AndroidManifest's
 * allowBackup="false") - nothing else on the device can read it without root.
 */
class AccountRepository(context: Context) {
    private val maDir = File(context.filesDir, "maFiles").apply { if (!exists()) mkdirs() }
    private val manifestFile = File(maDir, "manifest.json")
    private val json = Json { ignoreUnknownKeys = true; prettyPrint = false; isLenient = true }

    private fun normalizeJsonText(text: String): String = text.removePrefix("\uFEFF").trim()

    class IncorrectPassKeyException : Exception("That passkey is invalid.")

    suspend fun loadManifest(): Manifest = withContext(Dispatchers.IO) {
        if (!manifestFile.exists()) return@withContext Manifest()
        try {
            val loaded = json.decodeFromString<Manifest>(normalizeJsonText(manifestFile.readText()))
            // Mirror RecomputeExistingEntries(): drop entries whose backing file went missing.
            val existing = loaded.entries.filter { File(maDir, it.filename).exists() }
            if (existing.size != loaded.entries.size) {
                val fixed = loaded.copy(entries = existing, encrypted = if (existing.isEmpty()) false else loaded.encrypted)
                saveManifest(fixed)
                fixed
            } else loaded
        } catch (e: Exception) {
            Manifest()
        }
    }

    private fun saveManifest(manifest: Manifest) {
        manifestFile.writeText(json.encodeToString(Manifest.serializer(), manifest))
    }

    /** @param passKey required if the manifest is encrypted; returns an empty list (not an
     * error) if encrypted and no/incorrect key is supplied, matching GetAllAccounts() on desktop. */
    suspend fun getAllAccounts(manifest: Manifest, passKey: String? = null): List<SteamGuardAccount> =
        withContext(Dispatchers.IO) {
            if (manifest.encrypted && passKey == null) return@withContext emptyList()

            manifest.entries.mapNotNull { entry ->
                val file = File(maDir, entry.filename)
                if (!file.exists()) return@mapNotNull null
                var text = normalizeJsonText(file.readText())
                if (manifest.encrypted) {
                    val iv = entry.iv ?: return@mapNotNull null
                    val salt = entry.salt ?: return@mapNotNull null
                    text = FileEncryptor.decryptData(passKey!!, salt, iv, text) ?: return@mapNotNull null
                }
                runCatching { json.decodeFromString<SteamGuardAccount>(normalizeJsonText(text)) }.getOrNull()
            }
        }

    suspend fun verifyPasskey(manifest: Manifest, passkey: String?): Boolean = withContext(Dispatchers.IO) {
        if (!manifest.encrypted || manifest.entries.isEmpty()) return@withContext true
        if (passkey == null) return@withContext false
        getAllAccounts(manifest, passkey).isNotEmpty()
    }

    /** Encrypts (newKey != null) or decrypts (newKey == null) every stored account in place.
     * Returns the updated manifest, or null if oldKey was wrong. */
    suspend fun changeEncryptionKey(manifest: Manifest, oldKey: String?, newKey: String?): Manifest? =
        withContext(Dispatchers.IO) {
            if (manifest.encrypted && !verifyPasskey(manifest, oldKey)) return@withContext null

            val toEncrypt = newKey != null
            val newEntries = manifest.entries.map { entry ->
                val file = File(maDir, entry.filename)
                if (!file.exists()) return@map entry

                var contents = file.readText()
                if (manifest.encrypted) {
                    val decrypted = FileEncryptor.decryptData(oldKey!!, entry.salt!!, entry.iv!!, contents)
                        ?: return@withContext null
                    contents = decrypted
                }

                var newSalt: String? = null
                var newIv: String? = null
                var toWrite = contents
                if (toEncrypt) {
                    newSalt = FileEncryptor.getRandomSalt()
                    newIv = FileEncryptor.getInitializationVector()
                    toWrite = FileEncryptor.encryptData(newKey!!, newSalt, newIv, contents)
                }
                file.writeText(toWrite)
                entry.copy(iv = newIv, salt = newSalt)
            }

            val updated = manifest.copy(entries = newEntries, encrypted = toEncrypt)
            saveManifest(updated)
            updated
        }

    /** Writes/overwrites one account's .maFile and updates its manifest entry. */
    suspend fun saveAccount(manifest: Manifest, account: SteamGuardAccount, encrypt: Boolean, passKey: String? = null): Manifest? =
        withContext(Dispatchers.IO) {
            if (encrypt && passKey.isNullOrEmpty()) return@withContext null
            if (!encrypt && manifest.encrypted) return@withContext null

            var salt: String? = null
            var iv: String? = null
            var contents = json.encodeToString(SteamGuardAccount.serializer(), account)
            if (encrypt) {
                salt = FileEncryptor.getRandomSalt()
                iv = FileEncryptor.getInitializationVector()
                contents = FileEncryptor.encryptData(passKey!!, salt, iv, contents)
            }

            val filename = "${account.steamId64}.maFile"
            val newEntry = ManifestEntry(iv = iv, salt = salt, filename = filename, steamId = account.steamId64)

            val existingIndex = manifest.entries.indexOfFirst { it.steamId == account.steamId64 }
            val newEntries = if (existingIndex >= 0) {
                manifest.entries.toMutableList().apply { set(existingIndex, newEntry) }
            } else {
                manifest.entries + newEntry
            }

            File(maDir, filename).writeText(contents)
            val updated = manifest.copy(entries = newEntries, encrypted = encrypt || manifest.encrypted)
            saveManifest(updated)
            updated
        }

    suspend fun removeAccount(manifest: Manifest, account: SteamGuardAccount, deleteFile: Boolean = true): Manifest =
        withContext(Dispatchers.IO) {
            val entry = manifest.entries.firstOrNull { it.steamId == account.steamId64 } ?: return@withContext manifest
            val remaining = manifest.entries - entry
            if (deleteFile) File(maDir, entry.filename).delete()
            val updated = manifest.copy(entries = remaining, encrypted = if (remaining.isEmpty()) false else manifest.encrypted)
            saveManifest(updated)
            updated
        }

    /** Imports a plaintext .maFile's contents (from a file picker or a decoded QR payload).
     * If the manifest is currently encrypted, the imported account is encrypted with the same
     * passkey so every stored account stays under one key, matching desktop behavior. */
    suspend fun importPlaintextMaFile(manifest: Manifest, fileText: String, currentPassKey: String? = null): Result<Manifest> =
        withContext(Dispatchers.IO) {
            val normalizedText = runCatching { QrPayloadCodec.decodeIfEncoded(fileText) }
                .getOrElse { error -> return@withContext Result.failure(error) }
            val account = runCatching { json.decodeFromString<SteamGuardAccount>(normalizedText) }.getOrNull()
                ?: return@withContext Result.failure(IllegalArgumentException("That file isn't a valid (unencrypted) .maFile."))

            val updated = saveAccount(manifest, account, encrypt = manifest.encrypted, passKey = currentPassKey)
                ?: return@withContext Result.failure(IncorrectPassKeyException())
            Result.success(updated)
        }

    /** Plaintext JSON for exporting an account (file share or QR code) - same shape a fresh,
     * unencrypted .maFile has on desktop. */
    fun exportAccountPlaintext(account: SteamGuardAccount): String =
        json.encodeToString(SteamGuardAccount.serializer(), account)

    /** Compact QR transport; deliberately separate from plaintext file export. */
    fun exportAccountQrPayload(account: SteamGuardAccount): String {
        // Access/refresh JWTs dominate the payload and create a QR code whose modules are too
        // small for phone cameras to resolve reliably from a desktop display. They are not
        // needed for Steam Guard code generation; keeping SteamID lets the imported account be
        // stored normally, while the app can ask for login later if confirmations are opened.
        val qrAccount = account.withoutQrSessionCredentials()
        return QrPayloadCodec.encode(exportAccountPlaintext(qrAccount))
    }
}
