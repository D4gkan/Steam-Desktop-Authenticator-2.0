package com.sda.mobile.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// --- IAuthenticationService/GetPasswordRSAPublicKey/v1 ---

@Serializable
data class RsaPublicKeyEnvelope(@SerialName("response") val response: RsaPublicKeyResponse? = null)

@Serializable
data class RsaPublicKeyResponse(
    @SerialName("publickey_mod") val modulus: String = "",
    @SerialName("publickey_exp") val exponent: String = "",
    @SerialName("timestamp") val timestamp: String = ""
)

// --- IAuthenticationService/BeginAuthSessionViaCredentials/v1 ---

@Serializable
data class BeginAuthSessionEnvelope(@SerialName("response") val response: BeginAuthSessionResponse? = null)

@Serializable
data class BeginAuthSessionResponse(
    @SerialName("client_id") val clientId: String = "",
    @SerialName("request_id") val requestId: String = "",
    @SerialName("interval") val interval: Double = 5.0,
    @SerialName("allowed_confirmations") val allowedConfirmations: List<AllowedConfirmation> = emptyList(),
    @SerialName("steamid") val steamId: String = "",
    @SerialName("weak_token") val weakToken: String? = null
)

@Serializable
data class AllowedConfirmation(
    @SerialName("confirmation_type") val confirmationType: Int = 0,
    @SerialName("associated_message") val associatedMessage: String? = null
)

/** Mirrors Steam's EAuthSessionGuardType enum. */
enum class AuthGuardType(val id: Int) {
    UNKNOWN(0),
    NONE(1),
    EMAIL_CODE(2),
    DEVICE_CODE(3), // TOTP, generated locally from shared_secret
    DEVICE_CONFIRMATION(4), // approve from an existing linked mobile authenticator
    EMAIL_CONFIRMATION(5),
    MACHINE_TOKEN(6);

    companion object {
        fun fromId(id: Int) = entries.firstOrNull { it.id == id } ?: UNKNOWN
    }
}

// --- IAuthenticationService/PollAuthSessionStatus/v1 ---

@Serializable
data class PollAuthSessionEnvelope(@SerialName("response") val response: PollAuthSessionResponse? = null)

@Serializable
data class PollAuthSessionResponse(
    @SerialName("refresh_token") val refreshToken: String? = null,
    @SerialName("access_token") val accessToken: String? = null,
    @SerialName("had_remote_interaction") val hadRemoteInteraction: Boolean = false,
    @SerialName("account_name") val accountName: String? = null,
    @SerialName("new_guard_data") val newGuardData: String? = null
)

// --- ITwoFactorService/AddAuthenticator/v1 ---

@Serializable
data class AddAuthenticatorEnvelope(@SerialName("response") val response: AddAuthenticatorResponse? = null)

@Serializable
data class AddAuthenticatorResponse(
    @SerialName("status") val status: Int = 0,
    @SerialName("shared_secret") val sharedSecret: String? = null,
    @SerialName("serial_number") val serialNumber: String? = null,
    @SerialName("revocation_code") val revocationCode: String? = null,
    @SerialName("uri") val uri: String? = null,
    @SerialName("server_time") val serverTime: Long = 0,
    @SerialName("account_name") val accountName: String? = null,
    @SerialName("token_gid") val tokenGid: String? = null,
    @SerialName("identity_secret") val identitySecret: String? = null,
    @SerialName("secret_1") val secret1: String? = null,
    @SerialName("phone_number_hint") val phoneNumberHint: String? = null
)

// --- ITwoFactorService/FinalizeAddAuthenticator/v1 ---

@Serializable
data class FinalizeAuthenticatorEnvelope(@SerialName("response") val response: FinalizeAuthenticatorResponse? = null)

@Serializable
data class FinalizeAuthenticatorResponse(
    @SerialName("success") val success: Boolean = false,
    @SerialName("want_more") val wantMore: Boolean = false,
    @SerialName("server_time") val serverTime: Long = 0,
    @SerialName("status") val status: Int = 0
)

// --- ITwoFactorService/RemoveAuthenticator/v1 ---

@Serializable
data class RemoveAuthenticatorEnvelope(@SerialName("response") val response: RemoveAuthenticatorResponse? = null)

@Serializable
data class RemoveAuthenticatorResponse(
    @SerialName("success") val success: Boolean = false,
    @SerialName("revocation_attempts_remaining") val revocationAttemptsRemaining: Int = 0
)

// --- IPhoneService / phone-linking flow ---

@Serializable
data class AccountPhoneStatusEnvelope(@SerialName("response") val response: AccountPhoneStatusResponse? = null)

@Serializable
data class AccountPhoneStatusResponse(@SerialName("verified_phone") val verifiedPhone: Boolean = false)

@Serializable
data class SetAccountPhoneNumberEnvelope(@SerialName("response") val response: SetAccountPhoneNumberResponse? = null)

@Serializable
data class SetAccountPhoneNumberResponse(
    @SerialName("confirmation_email_address") val confirmationEmailAddress: String? = null,
    @SerialName("phone_number_formatted") val phoneNumberFormatted: String? = null
)

@Serializable
data class IsWaitingForEmailEnvelope(@SerialName("response") val response: IsWaitingForEmailResponse? = null)

@Serializable
data class IsWaitingForEmailResponse(
    @SerialName("awaiting_email_confirmation") val awaitingEmailConfirmation: Boolean = false,
    @SerialName("seconds_to_wait") val secondsToWait: Int = 0
)

@Serializable
data class GetUserCountryEnvelope(@SerialName("response") val response: GetUserCountryResponse? = null)

@Serializable
data class GetUserCountryResponse(@SerialName("country") val country: String? = null)

// --- ITwoFactorService/QueryTime/v1 (TimeAligner) ---

@Serializable
data class QueryTimeEnvelope(@SerialName("response") val response: QueryTimeResponse? = null)

@Serializable
data class QueryTimeResponse(@SerialName("server_time") val serverTime: Long = 0)

// --- IAuthenticationService/GenerateAccessTokenForApp/v1 ---
// (cheap "just mint a new access token from the still-valid refresh token" path - matches
// SessionData.RefreshAccessToken() on desktop)

@Serializable
data class GenerateAccessTokenEnvelope(@SerialName("response") val response: GenerateAccessTokenResponse? = null)

@Serializable
data class GenerateAccessTokenResponse(
    @SerialName("access_token") val accessToken: String? = null,
    @SerialName("refresh_token") val refreshToken: String? = null
)

// --- mobileconf/getlist & ajaxop ---

@Serializable
data class SendConfirmationResponse(@SerialName("success") val success: Boolean = false)
