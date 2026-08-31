package com.sda.mobile.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.longOrNull

/**
 * Mirrors SteamAuth.SessionData on desktop. Newtonsoft.Json serializes these properties using
 * their exact (PascalCase) C# member names since no naming-convention resolver is configured
 * there - so the keys here are intentionally PascalCase too, to keep .maFiles interchangeable
 * between this app and the desktop app.
 */
@Serializable
data class SessionData(
    @SerialName("SteamID") val steamId: Long = 0,
    @SerialName("AccessToken") val accessToken: String? = null,
    @SerialName("RefreshToken") val refreshToken: String? = null,
    @SerialName("SessionID") val sessionId: String? = null
) {
    /** steamLoginSecure cookie value, matching SessionData.GetSteamLoginSecure() on desktop. */
    fun steamLoginSecureCookie(): String = "$steamId%7C%7C$accessToken"

    /** True if [accessToken] is missing, malformed, or its JWT `exp` claim is in the past -
     * matches SessionData.IsAccessTokenExpired() on desktop. The access token is a short-lived
     * JWT (unlike the longer-lived refresh token), so this needs checking regularly - a caller
     * that skips this and just reuses an old access token is exactly what produces Steam's
     * "session expired" / needauth response on confirmation calls. */
    fun isAccessTokenExpired(): Boolean = isJwtExpired(accessToken)

    /** True if [refreshToken] itself is missing/expired - if so, a fresh login (not just a
     * token refresh) is required. */
    fun isRefreshTokenExpired(): Boolean = isJwtExpired(refreshToken)

    private fun isJwtExpired(token: String?): Boolean {
        val exp = jwtExpirySeconds(token) ?: return true
        return System.currentTimeMillis() / 1000 > exp
    }

    private fun jwtExpirySeconds(token: String?): Long? {
        if (token.isNullOrEmpty()) return null
        return try {
            val parts = token.split(".")
            if (parts.size < 2) return null
            var base64 = parts[1].replace('-', '+').replace('_', '/')
            val padding = (4 - base64.length % 4) % 4
            base64 += "=".repeat(padding)
            val payloadBytes = android.util.Base64.decode(base64, android.util.Base64.DEFAULT)
            val payload = Json.parseToJsonElement(String(payloadBytes, Charsets.UTF_8)).jsonObject
            payload["exp"]?.jsonPrimitive?.longOrNull
        } catch (e: Exception) {
            null
        }
    }
}
