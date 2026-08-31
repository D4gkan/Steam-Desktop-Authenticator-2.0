package com.sda.mobile.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/** Mobile-app-local per-account UI state. Not read by the desktop app - analogous in spirit to
 * its ui-meta.json, but a separate file since the two apps run on separate devices with
 * separate OS-native secure stores. */
@Serializable
data class AccountMeta(
    @SerialName("steamid") val steamId: Long,
    @SerialName("display_name") val displayName: String? = null,
    @SerialName("order") val order: Int = 0,
    @SerialName("enabled") val enabled: Boolean = true,
    /** Whether the password is saved in the Android Keystore-backed credential store for
     * automatic re-login. The password itself never lives here - see data/CredentialStore.kt. */
    @SerialName("save_login_enabled") val saveLoginEnabled: Boolean = false
)

@Serializable
data class UiMetaStore(
    @SerialName("accounts") val accounts: List<AccountMeta> = emptyList()
)
