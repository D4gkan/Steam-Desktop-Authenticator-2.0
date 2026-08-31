package com.sda.mobile.data

import com.sda.mobile.model.Confirmation
import com.sda.mobile.model.SteamGuardAccount
import com.sda.mobile.network.SteamApi
import com.sda.mobile.network.TimeAligner

class ConfirmationRepository(private val api: SteamApi = SteamApi()) {

    suspend fun fetch(account: SteamGuardAccount): List<Confirmation> {
        TimeAligner.alignIfNeeded()
        return api.fetchConfirmations(account, TimeAligner.getSteamTimeCached())
    }

    suspend fun accept(account: SteamGuardAccount, confirmation: Confirmation): Boolean {
        TimeAligner.alignIfNeeded()
        return api.answerConfirmation(account, confirmation, allow = true, time = TimeAligner.getSteamTimeCached())
    }

    suspend fun deny(account: SteamGuardAccount, confirmation: Confirmation): Boolean {
        TimeAligner.alignIfNeeded()
        return api.answerConfirmation(account, confirmation, allow = false, time = TimeAligner.getSteamTimeCached())
    }

    suspend fun acceptAll(account: SteamGuardAccount, confirmations: List<Confirmation>): Boolean {
        TimeAligner.alignIfNeeded()
        return api.answerMultipleConfirmations(account, confirmations, allow = true, time = TimeAligner.getSteamTimeCached())
    }
}
