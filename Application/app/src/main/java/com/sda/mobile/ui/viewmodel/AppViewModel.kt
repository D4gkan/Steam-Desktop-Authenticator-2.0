package com.sda.mobile.ui.viewmodel

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.sda.mobile.data.AccountRepository
import com.sda.mobile.data.CredentialStore
import com.sda.mobile.data.UiMetaRepository
import com.sda.mobile.model.AccountMeta
import com.sda.mobile.model.Manifest
import com.sda.mobile.model.SteamGuardAccount
import com.sda.mobile.network.SteamApi
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Owns the account list, the manifest, and the in-memory (never persisted) encryption passkey
 * for the lifetime of the app process - equivalent role to Manifest.GetManifest()'s singleton
 * on desktop, but scoped to this ViewModel instead of a static so it's testable and doesn't
 * leak across process restarts by accident.
 */
class AppViewModel(application: Application) : AndroidViewModel(application) {
    private val accountRepository = AccountRepository(application)
    private val uiMetaRepository = UiMetaRepository(application)
    private val steamApi = SteamApi()
    val credentialStore = CredentialStore(application)

    data class AccountEntry(val account: SteamGuardAccount, val meta: AccountMeta)

    data class UiState(
        val loading: Boolean = true,
        val manifest: Manifest = Manifest(),
        /** True once a valid passkey has been supplied for an encrypted manifest, or the
         * manifest isn't encrypted at all. Accounts are only populated once true. */
        val unlocked: Boolean = false,
        val accounts: List<AccountEntry> = emptyList(),
        val error: String? = null,
        val expiredSessionAccountId: Long? = null,
        val expiredSessionMessage: String? = null
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    /** Held only in process memory - never written to disk. Cleared on unlockScreen()/on the
     * ViewModel being cleared (app backgrounded long enough to be killed, or process death). */
    private var passKey: String? = null

    init {
        viewModelScope.launch { reload() }
    }

    private suspend fun reload() {
        val manifest = accountRepository.loadManifest()
        if (!manifest.encrypted) {
            val accounts = loadAccountsWithMeta(manifest, null)
            _state.value = UiState(loading = false, manifest = manifest, unlocked = true, accounts = accounts)
        } else if (passKey != null) {
            val accounts = loadAccountsWithMeta(manifest, passKey)
            _state.value = UiState(loading = false, manifest = manifest, unlocked = true, accounts = accounts)
        } else {
            _state.value = UiState(loading = false, manifest = manifest, unlocked = false, accounts = emptyList())
        }
    }

    private suspend fun loadAccountsWithMeta(manifest: Manifest, passKey: String?): List<AccountEntry> {
        val accounts = accountRepository.getAllAccounts(manifest, passKey)
        return accounts.map { account -> AccountEntry(account, uiMetaRepository.get(account.steamId64)) }
            .sortedBy { it.meta.order }
    }

    fun unlock(candidatePasskey: String, onResult: (success: Boolean) -> Unit) {
        viewModelScope.launch {
            val ok = accountRepository.verifyPasskey(_state.value.manifest, candidatePasskey)
            if (ok) {
                passKey = candidatePasskey
                reload()
            }
            onResult(ok)
        }
    }

    /** Locks the app screen again (e.g. user backgrounded it) without forgetting where the
     * manifest lives - just forces re-entry of the passkey before accounts are shown again. */
    fun lock() {
        passKey = null
        viewModelScope.launch { reload() }
    }

    fun currentPasskey(): String? = passKey

    fun refreshCodes() {
        // Codes are derived (not stored) - callers just need recomposition, which the
        // per-second ticker in AccountListScreen already drives. Exposed for symmetry /
        // pull-to-refresh gestures that also want to re-check for e.g. removed accounts.
        viewModelScope.launch { reload() }
    }

    fun importPlaintextMaFile(fileText: String, onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch {
            val result = accountRepository.importPlaintextMaFile(_state.value.manifest, fileText, passKey)
            if (result.isSuccess) {
                reload()
                onResult(Result.success(Unit))
            } else {
                onResult(result.map { })
            }
        }
    }

    fun addLinkedAccount(account: SteamGuardAccount, onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch {
            val encrypt = _state.value.manifest.encrypted
            val updated = accountRepository.saveAccount(_state.value.manifest, account, encrypt, passKey)
            if (updated == null) {
                onResult(Result.failure(IllegalStateException("Could not save the new account (wrong passkey?).")))
            } else {
                reload()
                onResult(Result.success(Unit))
            }
        }
    }

    fun removeAccount(account: SteamGuardAccount) {
        viewModelScope.launch {
            accountRepository.removeAccount(_state.value.manifest, account)
            credentialStore.clearPassword(account.steamId64)
            uiMetaRepository.remove(account.steamId64)
            reload()
        }
    }

    /** Enable/disable encryption for the whole store. Pass newPasskey = null to decrypt. */
    fun setEncryption(oldPasskey: String?, newPasskey: String?, onResult: (Boolean) -> Unit) {
        viewModelScope.launch {
            val updated = accountRepository.changeEncryptionKey(_state.value.manifest, oldPasskey, newPasskey)
            if (updated != null) {
                passKey = newPasskey
                reload()
            }
            onResult(updated != null)
        }
    }

    fun updateMeta(meta: AccountMeta) {
        viewModelScope.launch {
            uiMetaRepository.upsert(meta)
            reload()
        }
    }

    fun exportAccountPlaintext(account: SteamGuardAccount): String =
        accountRepository.exportAccountPlaintext(account)

    fun clearSessionExpiredPrompt() {
        _state.update { it.copy(expiredSessionAccountId = null, expiredSessionMessage = null) }
    }

    fun savePasswordForAccount(steamId64: Long, password: String) {
        credentialStore.savePassword(steamId64, password)
    }

    fun getSavedPassword(steamId64: Long): String? = credentialStore.getPassword(steamId64)

    /** Opportunistically mints fresh access tokens (via each account's refresh_token) for any
     * account whose access token has expired, before a caller (e.g. the confirmations screen)
     * makes cookie/bearer-authenticated calls with it. Without this, an account's access token
     * silently expires (it's only good for a limited time) and every confirmation call after
     * that fails with "session expired", even though the still-valid refresh_token could have
     * minted a new one with no user interaction at all - matches the cheap path of
     * SessionRecoveryService.EnsureValidSessionAsync() on desktop. Returns the (possibly
     * updated) entries in the same order; accounts with no session, or whose refresh token is
     * also dead, are left untouched, so the caller's later hit still surfaces a clear "log in
     * again" error to the user for those. */
    suspend fun ensureFreshSessions(accounts: List<AccountEntry>): List<AccountEntry> {
        val dueForRefresh = accounts.filter { entry ->
            val session = entry.account.session
            session != null && session.refreshToken != null &&
                session.isAccessTokenExpired() && !session.isRefreshTokenExpired()
        }
        if (dueForRefresh.isEmpty()) {
            val expiredFallback = accounts.filter { entry ->
                val session = entry.account.session
                session != null && (session.isAccessTokenExpired() || session.isRefreshTokenExpired())
            }
            if (expiredFallback.isNotEmpty()) {
                val candidate = expiredFallback.first()
                _state.update {
                    it.copy(
                        expiredSessionAccountId = candidate.account.steamId64,
                        expiredSessionMessage = "Session expired. Please log in again."
                    )
                }
            }
            return accounts
        }

        val refreshedById = dueForRefresh
            .map { entry ->
                viewModelScope.async {
                    val session = entry.account.session!!
                    runCatching { steamApi.refreshAccessToken(session.refreshToken!!, entry.account.steamId64) }
                        .getOrNull()
                        ?.let { entry.account.steamId64 to it }
                }
            }
            .awaitAll()
            .filterNotNull()
            .toMap()
        if (refreshedById.isEmpty()) {
            val candidate = dueForRefresh.first()
            _state.update {
                it.copy(
                    expiredSessionAccountId = candidate.account.steamId64,
                    expiredSessionMessage = "Session expired. Please log in again."
                )
            }
            return accounts
        }

        var manifest = _state.value.manifest
        val updatedEntries = accounts.map { entry ->
            val fresh = refreshedById[entry.account.steamId64] ?: return@map entry
            val newSession = entry.account.session!!.copy(
                accessToken = fresh.accessToken ?: entry.account.session.accessToken,
                refreshToken = fresh.refreshToken ?: entry.account.session.refreshToken
            )
            val updatedAccount = entry.account.copy(session = newSession)
            accountRepository.saveAccount(manifest, updatedAccount, encrypt = manifest.encrypted, passKey = passKey)
                ?.let { manifest = it }
            entry.copy(account = updatedAccount)
        }
        _state.update { it.copy(manifest = manifest, accounts = updatedEntries) }
        return updatedEntries
    }

    fun refreshSession(steamId64: Long, newSession: com.sda.mobile.model.SessionData, onComplete: (() -> Unit)? = null) {
        viewModelScope.launch {
            val entry = _state.value.accounts.firstOrNull { it.account.steamId64 == steamId64 } ?: run {
                onComplete?.invoke()
                return@launch
            }
            val updated = entry.account.copy(session = newSession, fullyEnrolled = true)
            val result = accountRepository.saveAccount(_state.value.manifest, updated, encrypt = _state.value.manifest.encrypted, passKey = passKey)
            if (result != null) {
                reload()
            }
            onComplete?.invoke()
        }
    }
}
