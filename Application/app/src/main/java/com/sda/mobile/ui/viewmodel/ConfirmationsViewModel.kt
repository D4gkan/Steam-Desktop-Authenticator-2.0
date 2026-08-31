package com.sda.mobile.ui.viewmodel

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.sda.mobile.data.ConfirmationRepository
import com.sda.mobile.model.Confirmation
import com.sda.mobile.model.SteamGuardAccount
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ConfirmationRow(val account: SteamGuardAccount, val confirmation: Confirmation)

data class ConfirmationsUiState(
    val loading: Boolean = false,
    val rows: List<ConfirmationRow> = emptyList(),
    val error: String? = null,
    /** ids currently being accepted/denied, so their row can show a spinner. */
    val pendingIds: Set<Long> = emptySet()
)

class ConfirmationsViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = ConfirmationRepository()

    private val _state = MutableStateFlow(ConfirmationsUiState())
    val state: StateFlow<ConfirmationsUiState> = _state.asStateFlow()

    /** Fetches pending confirmations for every fully-enrolled, unlocked account. Only ever
     * runs when the user opens/refreshes this screen - no background polling, per design. */
    fun refresh(accounts: List<SteamGuardAccount>) {
        _state.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                val results = accounts.filter { it.fullyEnrolled && it.session != null }
                    .map { account -> async { runCatching { account to repo.fetch(account) } } }
                    .awaitAll()

                val rows = results.mapNotNull { it.getOrNull() }
                    .flatMap { (account, confs) -> confs.map { ConfirmationRow(account, it) } }

                val firstError = results.firstOrNull { it.isFailure }?.exceptionOrNull()?.message
                _state.update { it.copy(loading = false, rows = rows, error = if (rows.isEmpty()) firstError else null) }
            } catch (e: Exception) {
                _state.update { it.copy(loading = false, error = e.message) }
            }
        }
    }

    fun answer(row: ConfirmationRow, allow: Boolean, onDone: (Boolean) -> Unit) {
        _state.update { it.copy(pendingIds = it.pendingIds + row.confirmation.id) }
        viewModelScope.launch {
            val ok = try {
                if (allow) repo.accept(row.account, row.confirmation) else repo.deny(row.account, row.confirmation)
            } catch (e: Exception) {
                false
            }
            _state.update { s ->
                s.copy(
                    pendingIds = s.pendingIds - row.confirmation.id,
                    rows = if (ok) s.rows.filterNot { it.confirmation.id == row.confirmation.id } else s.rows
                )
            }
            onDone(ok)
        }
    }
}
