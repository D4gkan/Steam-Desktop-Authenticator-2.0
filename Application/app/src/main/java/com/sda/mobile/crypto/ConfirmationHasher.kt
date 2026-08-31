package com.sda.mobile.crypto

import android.util.Base64
import java.net.URLEncoder
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Port of SteamGuardAccount._generateConfirmationHashForTime / GenerateConfirmationQueryParams
 * from the desktop app. Produces the "k" query parameter Steam expects on every mobileconf
 * request (list, accept, deny).
 */
object ConfirmationHasher {

    /** @param identitySecretBase64 the account's identity_secret, as stored in the .maFile. */
    fun generateConfirmationHash(identitySecretBase64: String, time: Long, tag: String?): String {
        val decodedSecret = Base64.decode(identitySecretBase64, Base64.DEFAULT)

        val tagLen = when {
            tag == null -> 0
            tag.length > 32 -> 32
            else -> tag.length
        }
        val bufferLen = 8 + tagLen
        val buffer = ByteArray(bufferLen)

        var t = time
        for (i in 8 downTo 1) {
            buffer[i - 1] = t.toByte()
            t = t shr 8
        }
        if (tag != null && tagLen > 0) {
            val tagBytes = tag.toByteArray(Charsets.UTF_8)
            System.arraycopy(tagBytes, 0, buffer, 8, tagLen)
        }

        val mac = Mac.getInstance("HmacSHA1")
        mac.init(SecretKeySpec(decodedSecret, "HmacSHA1"))
        val hashed = mac.doFinal(buffer)

        val encoded = Base64.encodeToString(hashed, Base64.NO_WRAP)
        // Mirror WebUtility.UrlEncode (space -> '+', not %20) via URLEncoder.
        return URLEncoder.encode(encoded, "UTF-8")
    }

    /**
     * Builds the full set of query parameters ("p","a","k","t","m","tag") that every
     * mobileconf endpoint request needs, matching GenerateConfirmationQueryParamsAsNVC exactly.
     */
    fun buildConfirmationParams(
        deviceId: String,
        steamId64: Long,
        identitySecretBase64: String,
        time: Long,
        tag: String
    ): Map<String, String> {
        require(deviceId.isNotEmpty()) { "Device ID is not present" }
        return linkedMapOf(
            "p" to deviceId,
            "a" to steamId64.toString(),
            "k" to generateConfirmationHash(identitySecretBase64, time, tag),
            "t" to time.toString(),
            "m" to "react",
            "tag" to tag
        )
    }
}
