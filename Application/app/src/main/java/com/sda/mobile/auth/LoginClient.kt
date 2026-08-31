package com.sda.mobile.auth

import com.sda.mobile.crypto.RsaPasswordEncryptor
import com.sda.mobile.model.*
import com.sda.mobile.network.ApiEndpoints
import com.sda.mobile.network.SteamHttpClient
import kotlinx.coroutines.delay
import kotlinx.serialization.json.Json
import okhttp3.OkHttpClient

/**
 * Logs in to a Steam account over plain HTTPS, without SteamKit2.
 *
 * The desktop app authenticates via SteamKit2's CredentialsAuthSession, which is backed by a
 * binary CM-socket connection to Steam's client network - that's a .NET-only dependency
 * (SteamKit2), so it isn't available here. Steam's IAuthenticationService also exposes the
 * *same* handshake as plain HTTPS/JSON endpoints (this is the flow other non-.NET Steam login
 * implementations use too): GetPasswordRSAPublicKey -> BeginAuthSessionViaCredentials ->
 * (UpdateAuthSessionWithSteamGuardCode if a code is required) -> PollAuthSessionStatus.
 *
 * NOTE: this HTTP surface was reconstructed from the desktop app's SteamAuth library (which
 * calls the sibling ITwoFactorService/IPhoneService endpoints the exact same way - see
 * network/SteamApi.kt) plus Steam's publicly documented IAuthenticationService shape. It has
 * not been exercised against live Steam from this build environment (no network access to
 * api.steampowered.com here) - test it against a real account (ideally a throwaway/alt first)
 * and expect to debug field names/enum values if Steam has changed anything.
 */
class LoginClient(private val client: OkHttpClient = SteamHttpClient.newClient()) {
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    sealed class LoginStep {
        /** Steam needs a code before it will finish the login. [guardType] tells you which
         * kind - EMAIL_CODE (check your inbox), DEVICE_CODE (Steam Guard Mobile TOTP code from
         * whatever app is currently the account's authenticator), or DEVICE_CONFIRMATION
         * (approve from the existing linked mobile app - no code to type). */
        data class NeedsGuardCode(val guardType: AuthGuardType, val associatedMessage: String?) : LoginStep()
        data class Success(val session: SessionData) : LoginStep()
        data class Failed(val reason: String) : LoginStep()
    }

    private var clientId: String = ""
    private var requestId: String = ""
    private var pollIntervalSeconds: Double = 5.0
    private var steamId: Long = 0

    /** Step 1: submit credentials. Returns whether a guard code is needed next, or that login
     * already succeeded (rare with credentials alone, but possible with a saved device token). */
    suspend fun beginLogin(accountName: String, password: String): LoginStep {
        // Unlike almost every other IAuthenticationService call, GetPasswordRSAPublicKey only
        // accepts HTTP GET - POSTing to it (as this used to) gets Steam's front door proxy to
        // reject the request before it even reaches the API, with an HTML "Method Not Allowed"
        // body instead of JSON. That's the exact "Unexpected JSON token... had '<' instead"
        // crash: the app tried to parse that HTML error page as JSON.
        val rsaUrl = ApiEndpoints.GET_PASSWORD_RSA_KEY + "?account_name=" +
            java.net.URLEncoder.encode(accountName, "UTF-8")
        val rsaEnvelope = json.decodeFromString<RsaPublicKeyEnvelope>(
            SteamHttpClient.get(client, rsaUrl)
        )
        val rsa = rsaEnvelope.response ?: return LoginStep.Failed("Could not fetch Steam's RSA login key.")

        val encryptedPassword = RsaPasswordEncryptor.encryptPassword(password, rsa.modulus, rsa.exponent)

        val beginEnvelope = json.decodeFromString<BeginAuthSessionEnvelope>(
            SteamHttpClient.post(
                client, ApiEndpoints.BEGIN_AUTH_SESSION_VIA_CREDENTIALS, mapOf(
                    "account_name" to accountName,
                    "encrypted_password" to encryptedPassword,
                    "encryption_timestamp" to rsa.timestamp,
                    "remember_login" to "false",
                    "platform_type" to "3", // k_EAuthTokenPlatformType_MobileApp
                    "persistence" to "0",   // ephemeral session, matches desktop's IsPersistentSession = false
                    "website_id" to "Mobile",
                    "device_friendly_name" to "SDA Mobile (Android)"
                )
            )
        )
        val begin = beginEnvelope.response ?: return LoginStep.Failed("Steam login failed: empty response.")

        clientId = begin.clientId
        requestId = begin.requestId
        pollIntervalSeconds = begin.interval
        steamId = begin.steamId.toLongOrNull() ?: 0L

        val guard = begin.allowedConfirmations.firstOrNull { AuthGuardType.fromId(it.confirmationType) != AuthGuardType.NONE }
            ?: return pollForResult()

        return LoginStep.NeedsGuardCode(AuthGuardType.fromId(guard.confirmationType), guard.associatedMessage)
    }

    /** Step 2 (only if beginLogin returned NeedsGuardCode): submit the code the user typed in
     * (or that was generated locally from an already-linked account's shared_secret). */
    suspend fun submitGuardCode(code: String, guardType: AuthGuardType): LoginStep {
        val codeType = when (guardType) {
            AuthGuardType.EMAIL_CODE -> 2
            AuthGuardType.DEVICE_CODE -> 3
            else -> 3
        }
        SteamHttpClient.post(
            client, ApiEndpoints.UPDATE_AUTH_SESSION_WITH_STEAM_GUARD_CODE, mapOf(
                "client_id" to clientId,
                "steamid" to steamId.toString(),
                "code" to code,
                "code_type" to codeType.toString()
            )
        )
        return pollForResult()
    }

    /** Polls PollAuthSessionStatus until Steam returns tokens, the session is refused, or we
     * give up after ~2 minutes (mirrors the desktop app's PollingWaitForResultAsync loop). */
    private suspend fun pollForResult(): LoginStep {
        val deadline = System.currentTimeMillis() + 120_000
        while (System.currentTimeMillis() < deadline) {
            val envelope = json.decodeFromString<PollAuthSessionEnvelope>(
                SteamHttpClient.post(
                    client, ApiEndpoints.POLL_AUTH_SESSION_STATUS,
                    mapOf("client_id" to clientId, "request_id" to requestId)
                )
            )
            val poll = envelope.response
            if (poll?.refreshToken != null) {
                return LoginStep.Success(
                    SessionData(
                        steamId = steamId,
                        accessToken = poll.accessToken,
                        refreshToken = poll.refreshToken,
                        sessionId = null
                    )
                )
            }
            delay((pollIntervalSeconds * 1000).toLong().coerceAtLeast(1000))
        }
        return LoginStep.Failed("Timed out waiting for Steam Guard confirmation.")
    }
}
