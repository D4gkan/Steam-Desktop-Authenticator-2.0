package com.sda.mobile.crypto

import android.util.Base64
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Generates Steam Guard 2FA codes from a base64 shared_secret.
 *
 * This is a direct, byte-for-byte port of SteamGuardAccount.GenerateSteamGuardCodeForTime in
 * src/SteamAuth/SteamGuardAccount.cs on the desktop app - same 30-second time step, same
 * HMAC-SHA1 truncation, same 26-character alphabet. A shared_secret imported from (or exported
 * to) a desktop .maFile produces the identical code stream here.
 */
object SteamGuardCodeGenerator {

    // Steam's custom base-26-ish alphabet (digits 2-9, then a subset of uppercase letters that
    // are visually unambiguous - no 0/O, 1/I, etc.). Order matters - do not "clean up" this list.
    private val STEAM_GUARD_CODE_TRANSLATIONS = byteArrayOf(
        50, 51, 52, 53, 54, 55, 56, 57, // '2'-'9'
        66, 67, 68, 70, 71, 72, 74, 75, // B C D F G H J K
        77, 78, 80, 81, 82, 84, 86, 87, // M N P Q R T V W
        88, 89                          // X Y
    )

    /** @param sharedSecretBase64 the account's shared_secret, as stored in the .maFile. */
    fun generateCode(sharedSecretBase64: String?, unixTimeSeconds: Long): String {
        if (sharedSecretBase64.isNullOrEmpty()) return ""

        val sharedSecret = Base64.decode(sharedSecretBase64, Base64.DEFAULT)

        var time = unixTimeSeconds / 30L
        val timeArray = ByteArray(8)
        for (i in 8 downTo 1) {
            timeArray[i - 1] = time.toByte()
            time = time shr 8
        }

        val mac = Mac.getInstance("HmacSHA1")
        mac.init(SecretKeySpec(sharedSecret, "HmacSHA1"))
        val hashedData = mac.doFinal(timeArray)

        val b = (hashedData[19].toInt() and 0xF)
        var codePoint = (hashedData[b].toInt() and 0x7F) shl 24 or
            ((hashedData[b + 1].toInt() and 0xFF) shl 16) or
            ((hashedData[b + 2].toInt() and 0xFF) shl 8) or
            (hashedData[b + 3].toInt() and 0xFF)

        val codeArray = ByteArray(5)
        val alphabetLen = STEAM_GUARD_CODE_TRANSLATIONS.size
        for (i in 0 until 5) {
            codeArray[i] = STEAM_GUARD_CODE_TRANSLATIONS[Math.floorMod(codePoint, alphabetLen)]
            codePoint /= alphabetLen
        }

        return String(codeArray, Charsets.UTF_8)
    }
}
