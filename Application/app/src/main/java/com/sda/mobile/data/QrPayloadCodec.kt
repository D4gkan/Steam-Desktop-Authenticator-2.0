package com.sda.mobile.data

import com.sda.mobile.model.SteamGuardAccount
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream

/**
 * Compact, versioned transport for .maFile QR codes. Steam session JWTs make the plaintext
 * JSON large enough to produce an extremely dense QR code, while gzip reduces the repeated
 * JSON/JWT text substantially. This is transport encoding only, not encryption.
 */
object QrPayloadCodec {
    const val PREFIX = "sda-mafile:v1:"

    fun encode(plaintext: String): String {
        val compressed = ByteArrayOutputStream().use { output ->
            GZIPOutputStream(output).use { it.write(plaintext.toByteArray(Charsets.UTF_8)) }
            output.toByteArray()
        }
        val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(compressed)
        return PREFIX + encoded
    }

    fun decodeIfEncoded(payload: String): String {
        val normalized = payload.removePrefix("\uFEFF").trim()
        if (!normalized.startsWith(PREFIX)) return normalized

        val encoded = normalized.removePrefix(PREFIX)
        require(encoded.isNotEmpty()) { "That QR code has an empty SDA payload." }

        return try {
            val compressed = Base64.getUrlDecoder().decode(encoded)
            GZIPInputStream(ByteArrayInputStream(compressed)).bufferedReader(Charsets.UTF_8).use { it.readText() }
        } catch (error: Exception) {
            throw IllegalArgumentException("That QR code has an invalid SDA payload.", error)
        }
    }
}

internal fun SteamGuardAccount.withoutQrSessionCredentials(): SteamGuardAccount = copy(
    session = session?.copy(
        accessToken = null,
        refreshToken = null,
        sessionId = null
    )
)
