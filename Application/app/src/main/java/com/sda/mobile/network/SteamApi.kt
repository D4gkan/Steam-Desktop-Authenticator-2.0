package com.sda.mobile.network

import com.sda.mobile.crypto.ConfirmationHasher
import com.sda.mobile.model.*
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import okhttp3.OkHttpClient

/**
 * Direct port of the HTTP calls in src/SteamAuth/SteamGuardAccount.cs and
 * AuthenticatorLinker.cs (the ITwoFactorService / IPhoneService / mobileconf endpoints - the
 * plain HTTP surface both this app and the desktop app's SteamAuth library talk to). The
 * desktop app's *login* step additionally goes through SteamKit2's CM-socket-backed
 * CredentialsAuthSession; see auth/LoginClient.kt for why this app instead talks to the same
 * IAuthenticationService HTTP endpoints directly.
 */
class SteamApi(private val client: OkHttpClient = SteamHttpClient.newClient()) {
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    // ---- Confirmations ----

    fun parseConfirmationFailure(obj: kotlinx.serialization.json.JsonObject): String {
        val needAuth = obj["needauth"]?.jsonPrimitive()?.toBooleanStrictOrNull() ?: false
        val rawMessage = obj["message"]?.jsonPrimitive()
        val normalized = rawMessage?.lowercase()

        if (needAuth) return "Session expired. Please log in again."
        if (normalized != null && (normalized.contains("needs authentication") ||
                normalized.contains("invalid access token") ||
                normalized.contains("access_token"))) {
            return "Session expired. Please log in again."
        }
        if (rawMessage != null) return rawMessage
        return "Steam returned an error while fetching confirmations."
    }

    suspend fun fetchConfirmations(account: SteamGuardAccount, time: Long): List<Confirmation> {
        val url = buildConfirmationUrl(account, time, "conf")
        val cookieClient = sessionCookieClient(account)
        val response = SteamHttpClient.get(cookieClient, url)
        val obj = json.parseToJsonElement(response).jsonObject
        val success = obj["success"]?.jsonPrimitive()?.toBooleanStrictOrNull() ?: false
        if (!success) {
            throw SteamApiException(parseConfirmationFailure(obj))
        }
        val needAuth = obj["needauth"]?.jsonPrimitive()?.toBooleanStrictOrNull() ?: false
        if (needAuth) throw SteamApiException("Session expired. Please log in again.")

        return obj["conf"]?.jsonArray?.map { Confirmation.fromJson(it.jsonObject) } ?: emptyList()
    }

    suspend fun answerConfirmation(account: SteamGuardAccount, confirmation: Confirmation, allow: Boolean, time: Long): Boolean {
        val op = if (allow) "allow" else "cancel"
        val tag = op
        val params = ConfirmationHasher.buildConfirmationParams(
            deviceId = account.deviceId.orEmpty(),
            steamId64 = account.steamId64,
            identitySecretBase64 = account.identitySecret.orEmpty(),
            time = time,
            tag = tag
        )
        val query = buildString {
            append("?op=").append(op).append('&')
            append(params.entries.joinToString("&") { (k, v) -> "$k=$v" })
            append("&cid=").append(confirmation.id).append("&ck=").append(confirmation.key)
        }
        val response = SteamHttpClient.get(sessionCookieClient(account), ApiEndpoints.MOBILECONF_AJAXOP + query)
        return json.decodeFromString<SendConfirmationResponse>(response).success
    }

    suspend fun answerMultipleConfirmations(account: SteamGuardAccount, confirmations: List<Confirmation>, allow: Boolean, time: Long): Boolean {
        val op = if (allow) "allow" else "cancel"
        val tag = op
        val params = ConfirmationHasher.buildConfirmationParams(
            deviceId = account.deviceId.orEmpty(),
            steamId64 = account.steamId64,
            identitySecretBase64 = account.identitySecret.orEmpty(),
            time = time,
            tag = tag
        )
        val query = buildString {
            append("op=").append(op).append('&')
            append(params.entries.joinToString("&") { (k, v) -> "$k=$v" })
            for (c in confirmations) append("&cid[]=").append(c.id).append("&ck[]=").append(c.key)
        }
        val response = SteamHttpClient.postRaw(
            sessionCookieClient(account), ApiEndpoints.MOBILECONF_MULTIAJAXOP, query,
            "application/x-www-form-urlencoded; charset=UTF-8"
        )
        return json.decodeFromString<SendConfirmationResponse>(response).success
    }

    private fun buildConfirmationUrl(account: SteamGuardAccount, time: Long, tag: String): String {
        val params = ConfirmationHasher.buildConfirmationParams(
            deviceId = account.deviceId.orEmpty(),
            steamId64 = account.steamId64,
            identitySecretBase64 = account.identitySecret.orEmpty(),
            time = time,
            tag = tag
        )
        return ApiEndpoints.MOBILECONF_GETLIST + "?" + params.entries.joinToString("&") { (k, v) -> "$k=$v" }
    }

    /** Confirmation endpoints are cookie-authenticated (steamLoginSecure/sessionid), unlike the
     * bearer-token ITwoFactorService calls below - matches SessionData.GetCookies() on desktop.
     * Builds a fresh client sharing the base client's connection pool/timeouts but with a
     * cookie jar seeded for this specific account's session, so concurrent calls for different
     * accounts never share cookies. */
    private fun sessionCookieClient(account: SteamGuardAccount): OkHttpClient {
        val session = account.session ?: throw SteamApiException("Account has no session")
        val jar = SteamHttpClient.SessionCookieJar()
        val domains = listOf("steamcommunity.com", "store.steampowered.com")
        jar.setForDomains(domains, "steamLoginSecure", session.steamLoginSecureCookie())
        jar.setForDomains(domains, "sessionid", session.sessionId ?: java.util.UUID.randomUUID().toString().replace("-", "").take(32))
        jar.setForDomains(domains, "mobileClient", "android")
        jar.setForDomains(domains, "mobileClientVersion", "777777 3.6.4")
        return client.newBuilder().cookieJar(jar).build()
    }

    /** Cheap session refresh: mints a new (short-lived) access token from the still-valid
     * refresh token, without requiring the password or a Steam Guard code again. Matches
     * SessionData.RefreshAccessToken() on desktop. Callers should check
     * SessionData.isAccessTokenExpired() first and call this before any bearer/cookie
     * authenticated request (e.g. fetching confirmations) if it's expired - otherwise Steam
     * replies as if the whole session were dead ("session expired, please log in again"),
     * even though only this cheap step was actually needed. */
    suspend fun refreshAccessToken(refreshToken: String, steamId64: Long): GenerateAccessTokenResponse {
        val response = SteamHttpClient.post(
            client, ApiEndpoints.GENERATE_ACCESS_TOKEN_FOR_APP, mapOf(
                "refresh_token" to refreshToken,
                "steamid" to steamId64.toString(),
                "renewal_type" to "0"
            )
        )
        return json.decodeFromString<GenerateAccessTokenEnvelope>(response).response
            ?: throw SteamApiException("Steam didn't return a refreshed access token.")
    }

    // ---- Authenticator link/unlink (bearer-token authenticated via access_token query param) ----

    suspend fun addAuthenticator(accessToken: String, steamId64: Long, deviceId: String): AddAuthenticatorResponse {
        val url = "${ApiEndpoints.ADD_AUTHENTICATOR}/?access_token=$accessToken"
        val response = SteamHttpClient.post(
            client, url, mapOf(
                "steamid" to steamId64.toString(),
                "authenticator_type" to "1",
                "device_identifier" to deviceId,
                "sms_phone_id" to "1",
                "version" to "2"
            )
        )
        return json.decodeFromString<AddAuthenticatorEnvelope>(response).response
            ?: throw SteamApiException("Empty AddAuthenticator response")
    }

    suspend fun finalizeAddAuthenticator(accessToken: String, steamId64: Long, authenticatorCode: String, time: Long, smsCode: String): FinalizeAuthenticatorResponse {
        val url = "${ApiEndpoints.FINALIZE_ADD_AUTHENTICATOR}/?access_token=$accessToken"
        val response = SteamHttpClient.post(
            client, url, mapOf(
                "steamid" to steamId64.toString(),
                "authenticator_code" to authenticatorCode,
                "authenticator_time" to time.toString(),
                "activation_code" to smsCode,
                "validate_sms_code" to "1"
            )
        )
        return json.decodeFromString<FinalizeAuthenticatorEnvelope>(response).response
            ?: throw SteamApiException("Empty FinalizeAddAuthenticator response")
    }

    suspend fun removeAuthenticator(accessToken: String, revocationCode: String, scheme: Int = 1): RemoveAuthenticatorResponse {
        val response = SteamHttpClient.post(
            client, "${ApiEndpoints.REMOVE_AUTHENTICATOR}?access_token=$accessToken", mapOf(
                "revocation_code" to revocationCode,
                "revocation_reason" to "1",
                "steamguard_scheme" to scheme.toString()
            )
        )
        return json.decodeFromString<RemoveAuthenticatorEnvelope>(response).response
            ?: throw SteamApiException("Empty RemoveAuthenticator response")
    }

    // ---- Phone number linking (required before an authenticator can be added, if the
    //      account has no phone number on file yet) ----

    suspend fun getAccountPhoneStatus(accessToken: String): AccountPhoneStatusResponse {
        val response = SteamHttpClient.post(client, "${ApiEndpoints.ACCOUNT_PHONE_STATUS}?access_token=$accessToken", emptyMap())
        return json.decodeFromString<AccountPhoneStatusEnvelope>(response).response ?: AccountPhoneStatusResponse()
    }

    suspend fun getUserCountry(accessToken: String, steamId64: Long): String? {
        val response = SteamHttpClient.post(
            client, "${ApiEndpoints.GET_USER_COUNTRY}?access_token=$accessToken",
            mapOf("steamid" to steamId64.toString())
        )
        return json.decodeFromString<GetUserCountryEnvelope>(response).response?.country
    }

    suspend fun setAccountPhoneNumber(accessToken: String, phoneNumber: String, countryCode: String): SetAccountPhoneNumberResponse {
        val response = SteamHttpClient.post(
            client, "${ApiEndpoints.SET_ACCOUNT_PHONE_NUMBER}?access_token=$accessToken",
            mapOf("phone_number" to phoneNumber, "phone_country_code" to countryCode)
        )
        return json.decodeFromString<SetAccountPhoneNumberEnvelope>(response).response
            ?: throw SteamApiException("Empty SetAccountPhoneNumber response")
    }

    suspend fun isWaitingForEmailConfirmation(accessToken: String): Boolean {
        val response = SteamHttpClient.post(client, "${ApiEndpoints.IS_WAITING_FOR_EMAIL_CONFIRMATION}?access_token=$accessToken", emptyMap())
        return json.decodeFromString<IsWaitingForEmailEnvelope>(response).response?.awaitingEmailConfirmation ?: false
    }

    suspend fun sendPhoneVerificationCode(accessToken: String) {
        SteamHttpClient.post(client, "${ApiEndpoints.SEND_PHONE_VERIFICATION_CODE}?access_token=$accessToken", emptyMap())
    }

    suspend fun verifyPhoneWithCode(accessToken: String, code: String) {
        SteamHttpClient.post(client, "${ApiEndpoints.VERIFY_ACCOUNT_PHONE_WITH_CODE}/?access_token=$accessToken", mapOf("code" to code))
    }
}

class SteamApiException(message: String) : Exception(message)

private fun kotlinx.serialization.json.JsonElement.jsonPrimitive(): String? =
    (this as? kotlinx.serialization.json.JsonPrimitive)?.contentOrNull
