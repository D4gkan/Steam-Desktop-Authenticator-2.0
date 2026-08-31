package com.sda.mobile.auth

import com.sda.mobile.crypto.DeviceId
import com.sda.mobile.crypto.SteamGuardCodeGenerator
import com.sda.mobile.model.SessionData
import com.sda.mobile.model.SteamGuardAccount
import com.sda.mobile.network.SteamApi
import com.sda.mobile.network.TimeAligner
import kotlinx.coroutines.delay

/**
 * Port of SteamAuth.AuthenticatorLinker on desktop. Drives the ITwoFactorService/AddAuthenticator
 * -> (optional phone linking) -> FinalizeAddAuthenticator state machine.
 */
class AuthenticatorLinker(private val session: SessionData, private val api: SteamApi = SteamApi()) {

    val deviceId: String = DeviceId.generate()

    /** Populated once AddAuthenticator succeeds - this is the data that becomes the .maFile. */
    var linkedAccount: SteamGuardAccount? = null
        private set

    enum class LinkResult { MUST_PROVIDE_PHONE_NUMBER, AWAITING_FINALIZATION, GENERAL_FAILURE, AUTHENTICATOR_PRESENT }
    enum class PhoneLinkResult { MUST_PROVIDE_PHONE_NUMBER, MUST_CONFIRM_EMAIL, MUST_CONFIRM_SMS, PHONE_ADDED, FAILURE_ADDING_PHONE }
    enum class FinalizeResult { SUCCESS, BAD_SMS_CODE, UNABLE_TO_GENERATE_CORRECT_CODES, GENERAL_FAILURE }

    private var phoneLinkStep = PhoneLinkStep.NONE
    private enum class PhoneLinkStep { NONE, CONFIRMATION_EMAIL_SENT, SMS_CODE_SENT }
    var confirmationEmailAddress: String? = null
        private set

    suspend fun addAuthenticator(): LinkResult {
        val response = try {
            api.addAuthenticator(session.accessToken.orEmpty(), session.steamId, deviceId)
        } catch (e: Exception) {
            return LinkResult.GENERAL_FAILURE
        }

        return when (response.status) {
            2 -> LinkResult.MUST_PROVIDE_PHONE_NUMBER
            29 -> LinkResult.AUTHENTICATOR_PRESENT
            1 -> {
                linkedAccount = SteamGuardAccount(
                    sharedSecret = response.sharedSecret,
                    serialNumber = response.serialNumber,
                    revocationCode = response.revocationCode,
                    uri = response.uri,
                    serverTime = response.serverTime,
                    accountName = response.accountName,
                    tokenGid = response.tokenGid,
                    identitySecret = response.identitySecret,
                    secret1 = response.secret1,
                    status = response.status,
                    deviceId = deviceId,
                    phoneNumberHint = response.phoneNumberHint,
                    fullyEnrolled = false,
                    session = session
                )
                LinkResult.AWAITING_FINALIZATION
            }
            else -> LinkResult.GENERAL_FAILURE
        }
    }

    /** Only needed if addAuthenticator() returned MUST_PROVIDE_PHONE_NUMBER. Call repeatedly as
     * the UI collects each piece of info (phone number, then SMS code) - same step machine as
     * the desktop app's AddPhoneNumber(). */
    suspend fun addPhoneNumber(phoneNumber: String?, phoneCountryCode: String?, smsCode: String?): PhoneLinkResult {
        val accessToken = session.accessToken.orEmpty()
        when (phoneLinkStep) {
            PhoneLinkStep.NONE -> {
                val status = api.getAccountPhoneStatus(accessToken)
                if (status.verifiedPhone) return PhoneLinkResult.PHONE_ADDED
                if (phoneNumber.isNullOrEmpty()) return PhoneLinkResult.MUST_PROVIDE_PHONE_NUMBER

                val countryCode = phoneCountryCode ?: api.getUserCountry(accessToken, session.steamId) ?: "US"
                val setResponse = try {
                    api.setAccountPhoneNumber(accessToken, phoneNumber, countryCode)
                } catch (e: Exception) {
                    return PhoneLinkResult.FAILURE_ADDING_PHONE
                }
                if (setResponse.confirmationEmailAddress != null) {
                    confirmationEmailAddress = setResponse.confirmationEmailAddress
                    phoneLinkStep = PhoneLinkStep.CONFIRMATION_EMAIL_SENT
                    return PhoneLinkResult.MUST_CONFIRM_EMAIL
                }
                return PhoneLinkResult.FAILURE_ADDING_PHONE
            }
            PhoneLinkStep.CONFIRMATION_EMAIL_SENT -> {
                val stillWaiting = api.isWaitingForEmailConfirmation(accessToken)
                if (stillWaiting) return PhoneLinkResult.MUST_CONFIRM_EMAIL

                api.sendPhoneVerificationCode(accessToken)
                delay(2000)
                phoneLinkStep = PhoneLinkStep.SMS_CODE_SENT
                return PhoneLinkResult.MUST_CONFIRM_SMS
            }
            PhoneLinkStep.SMS_CODE_SENT -> {
                if (smsCode.isNullOrEmpty()) return PhoneLinkResult.MUST_CONFIRM_SMS
                api.verifyPhoneWithCode(accessToken, smsCode)
                return PhoneLinkResult.PHONE_ADDED
            }
        }
    }

    /** Final step: prove ownership by supplying the SMS code Steam texted after AddAuthenticator
     * succeeded, plus a currently-valid Steam Guard code generated from the new shared_secret
     * (proves the client captured it correctly). Retries a few times like the desktop app does,
     * since there's a small window where the freshly-added authenticator's clock/time-step can
     * be one step off from Steam's. */
    suspend fun finalizeAddAuthenticator(smsCode: String): FinalizeResult {
        val account = linkedAccount ?: return FinalizeResult.GENERAL_FAILURE
        var tries = 0
        while (tries <= 10) {
            TimeAligner.alignIfNeeded()
            val time = TimeAligner.getSteamTimeCached()
            val code = SteamGuardCodeGenerator.generateCode(account.sharedSecret, time)

            val response = try {
                api.finalizeAddAuthenticator(session.accessToken.orEmpty(), session.steamId, code, time, smsCode)
            } catch (e: Exception) {
                return FinalizeResult.GENERAL_FAILURE
            }

            // Mirrors AuthenticatorLinker.FinalizeAddAuthenticator on desktop exactly:
            // status 89 = bad SMS code; status 88 = needs another try (code/time-step
            // mismatch) and only gives up once `tries` has already reached 10; otherwise a
            // non-success response is a general failure, and WantMore means loop again.
            if (response.status == 89) return FinalizeResult.BAD_SMS_CODE

            if (response.status == 88 && tries >= 10) {
                return FinalizeResult.UNABLE_TO_GENERATE_CORRECT_CODES
            }

            if (!response.success) return FinalizeResult.GENERAL_FAILURE

            if (response.wantMore) {
                tries++
                delay(1000)
                continue
            }

            linkedAccount = account.copy(fullyEnrolled = true)
            return FinalizeResult.SUCCESS
        }
        return FinalizeResult.GENERAL_FAILURE
    }
}
