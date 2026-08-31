package com.sda.mobile.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/** Mirrors Core/Manifest.cs's on-disk manifest.json shape exactly - a manifest.json produced
 * by the desktop app (or vice versa) parses correctly here, field-for-field. */
@Serializable
data class Manifest(
    @SerialName("encrypted") val encrypted: Boolean = false,
    @SerialName("first_run") val firstRun: Boolean = true,
    @SerialName("entries") val entries: List<ManifestEntry> = emptyList(),
    @SerialName("periodic_checking") val periodicChecking: Boolean = false,
    @SerialName("periodic_checking_interval") val periodicCheckingInterval: Int = 5,
    @SerialName("periodic_checking_checkall") val checkAllAccounts: Boolean = false,
    @SerialName("auto_confirm_market_transactions") val autoConfirmMarketTransactions: Boolean = false,
    @SerialName("auto_confirm_trades") val autoConfirmTrades: Boolean = false
)

@Serializable
data class ManifestEntry(
    @SerialName("encryption_iv") val iv: String? = null,
    @SerialName("encryption_salt") val salt: String? = null,
    @SerialName("filename") val filename: String = "",
    @SerialName("steamid") val steamId: Long = 0
)
