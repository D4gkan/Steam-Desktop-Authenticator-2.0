package com.sda.mobile.model

import com.sda.mobile.crypto.SteamGuardCodeGenerator
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * Mirrors SteamAuth.SteamGuardAccount on desktop field-for-field. A .maFile written by this
 * app is a valid .maFile for the desktop app, and vice versa (modulo the encryption wrapper -
 * see data/AccountRepository.kt).
 */
@Serializable
data class SteamGuardAccount(
    @SerialName("shared_secret") val sharedSecret: String? = null,
    @SerialName("serial_number") val serialNumber: String? = null,
    @SerialName("revocation_code") val revocationCode: String? = null,
    @SerialName("uri") val uri: String? = null,
    @SerialName("server_time") val serverTime: Long = 0,
    @SerialName("account_name") val accountName: String? = null,
    @SerialName("token_gid") val tokenGid: String? = null,
    @SerialName("identity_secret") val identitySecret: String? = null,
    @SerialName("secret_1") val secret1: String? = null,
    @SerialName("status") val status: Int = 0,
    @SerialName("device_id") val deviceId: String? = null,
    @SerialName("phone_number_hint") val phoneNumberHint: String? = null,
    @SerialName("confirm_type") val confirmType: Int = 0,
    @SerialName("fully_enrolled") val fullyEnrolled: Boolean = false,
    @SerialName("Session") val session: SessionData? = null
) {
    val steamId64: Long get() = session?.steamId ?: 0L

    fun generateCode(unixTimeSeconds: Long): String =
        SteamGuardCodeGenerator.generateCode(sharedSecret, unixTimeSeconds)
}
