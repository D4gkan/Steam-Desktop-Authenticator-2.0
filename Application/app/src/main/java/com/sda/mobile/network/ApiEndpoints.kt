package com.sda.mobile.network

/** Same endpoints the desktop app (via SteamKit2 + hand-rolled ITwoFactorService/IPhoneService
 * calls) talks to - see src/SteamAuth/APIEndpoints.cs and AuthenticatorLinker.cs. */
object ApiEndpoints {
    const val STEAM_API_BASE = "https://api.steampowered.com"
    const val COMMUNITY_BASE = "https://steamcommunity.com"

    const val QUERY_TIME = "$STEAM_API_BASE/ITwoFactorService/QueryTime/v0001"

    const val GET_PASSWORD_RSA_KEY = "$STEAM_API_BASE/IAuthenticationService/GetPasswordRSAPublicKey/v1"
    const val BEGIN_AUTH_SESSION_VIA_CREDENTIALS = "$STEAM_API_BASE/IAuthenticationService/BeginAuthSessionViaCredentials/v1"
    const val UPDATE_AUTH_SESSION_WITH_STEAM_GUARD_CODE = "$STEAM_API_BASE/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1"
    const val POLL_AUTH_SESSION_STATUS = "$STEAM_API_BASE/IAuthenticationService/PollAuthSessionStatus/v1"
    const val GENERATE_ACCESS_TOKEN_FOR_APP = "$STEAM_API_BASE/IAuthenticationService/GenerateAccessTokenForApp/v1"

    const val ADD_AUTHENTICATOR = "$STEAM_API_BASE/ITwoFactorService/AddAuthenticator/v1"
    const val FINALIZE_ADD_AUTHENTICATOR = "$STEAM_API_BASE/ITwoFactorService/FinalizeAddAuthenticator/v1"
    const val REMOVE_AUTHENTICATOR = "$STEAM_API_BASE/ITwoFactorService/RemoveAuthenticator/v1"

    const val GET_USER_COUNTRY = "$STEAM_API_BASE/IUserAccountService/GetUserCountry/v1"
    const val ACCOUNT_PHONE_STATUS = "$STEAM_API_BASE/IPhoneService/AccountPhoneStatus/v1"
    const val SET_ACCOUNT_PHONE_NUMBER = "$STEAM_API_BASE/IPhoneService/SetAccountPhoneNumber/v1"
    const val VERIFY_ACCOUNT_PHONE_WITH_CODE = "$STEAM_API_BASE/IPhoneService/VerifyAccountPhoneWithCode/v1"
    const val IS_WAITING_FOR_EMAIL_CONFIRMATION = "$STEAM_API_BASE/IPhoneService/IsAccountWaitingForEmailConfirmation/v1"
    const val SEND_PHONE_VERIFICATION_CODE = "$STEAM_API_BASE/IPhoneService/SendPhoneVerificationCode/v1"

    const val MOBILECONF_GETLIST = "$COMMUNITY_BASE/mobileconf/getlist"
    const val MOBILECONF_AJAXOP = "$COMMUNITY_BASE/mobileconf/ajaxop"
    const val MOBILECONF_MULTIAJAXOP = "$COMMUNITY_BASE/mobileconf/multiajaxop"
}
