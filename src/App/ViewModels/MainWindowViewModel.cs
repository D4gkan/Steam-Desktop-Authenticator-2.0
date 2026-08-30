using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Core.Security;
using SteamDesktopAuthenticator.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<AccountViewModel> Accounts { get; } = new();
        public ObservableCollection<ConfirmationViewModel> Confirmations { get; } = new();

        /// <summary>What the ListBox actually binds to: Accounts filtered by SearchText and kept
        /// in Order sequence. Reordering (via the up/down buttons on each card, see MainWindow
        /// code-behind) only operates while SearchText is empty, so DisplayedAccounts order
        /// always matches Accounts order at the moment a reorder can happen.</summary>
        public ObservableCollection<AccountViewModel> DisplayedAccounts { get; } = new();

        private void RebuildDisplayedAccounts()
        {
            var ordered = FilteredAccounts.ToList();
            DisplayedAccounts.Clear();
            foreach (var a in ordered) DisplayedAccounts.Add(a);
        }

        [ObservableProperty]
        private AccountViewModel? _selectedAccount;

        [ObservableProperty]
        private string _searchText = "";

        [ObservableProperty]
        private bool _isUnlocked;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isManifestEncrypted;

        [ObservableProperty]
        private int _confirmationCount;

        public bool HasConfirmations => ConfirmationCount > 0;

        public IEnumerable<AccountViewModel> FilteredAccounts =>
            string.IsNullOrWhiteSpace(SearchText)
                ? Accounts.OrderBy(a => a.Order)
                : Accounts.Where(a => a.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                                       || a.Account.AccountName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(a => a.Order);

        private Manifest? _manifest;
        private UiMetaStore? _uiMeta;
        private string? _passKey;
        private readonly ConfirmationPollingService _pollingService;
        private readonly SessionRecoveryService _recoveryService;
        private System.Threading.Timer? _codeRefreshTimer;

        /// <summary>Injected by App startup - lets the ViewModel ask the UI layer to show dialogs
        /// without taking a direct dependency on any Avalonia view classes.</summary>
        public IDialogService DialogService { get; }

        public MainWindowViewModel(IDialogService dialogService)
        {
            DialogService = dialogService;
            _recoveryService = new SessionRecoveryService();
            _pollingService = new ConfirmationPollingService(() => Accounts, _recoveryService, PersistAccount);
            _pollingService.ConfirmationsUpdated += OnConfirmationsUpdated;
            _pollingService.AccountPollFailed += OnAccountPollFailed;
        }

        /// <summary>Saves an account's current session/tokens back to its .maFile, using
        /// whichever manifest/passkey is currently active. Shared by the confirmation polling
        /// service and the confirm/reject flow so a successful automatic re-login (Task 2) is
        /// always persisted the same way "Refresh Login…" already persists a manual one.</summary>
        private bool PersistAccount(SteamGuardAccount account)
        {
            if (_manifest == null) return false;
            return _manifest.SaveAccount(account, _manifest.Encrypted, _passKey);
        }

        partial void OnSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();
        }

        public async Task InitializeAsync()
        {
            try
            {
                _manifest = Manifest.GetManifest();
            }
            catch (ManifestParseException)
            {
                await DialogService.ShowMessageAsync("Could not parse maFiles/manifest.json. The file may be corrupt.");
                return;
            }

            if (_manifest == null)
            {
                StatusMessage = "No accounts configured yet.";
                IsManifestEncrypted = false;
                IsUnlocked = true;
                return;
            }

            IsManifestEncrypted = _manifest.Encrypted;

            if (_manifest.Encrypted)
            {
                _passKey = await _manifest.PromptForPassKeyAsync();
                if (_passKey == null)
                {
                    StatusMessage = "Locked. Enter your passkey to view accounts.";
                    IsUnlocked = false;
                    return;
                }
            }

            _uiMeta = UiMetaStore.Load();
            LoadAccountsFromManifest();
            IsUnlocked = true;

            StartCodeRefreshTimer();
            StartConfirmationPolling();
        }

        public async Task UnlockAsync()
        {
            if (_manifest == null) return;
            _passKey = await _manifest.PromptForPassKeyAsync();
            if (_passKey == null) return;

            IsManifestEncrypted = _manifest.Encrypted;
            _uiMeta = UiMetaStore.Load();
            LoadAccountsFromManifest();
            IsUnlocked = true;
            StartCodeRefreshTimer();
            StartConfirmationPolling();
        }

        private void LoadAccountsFromManifest()
        {
            if (_manifest == null || _uiMeta == null) return;
            Accounts.Clear();

            var steamAccounts = _manifest.Encrypted
                ? _manifest.GetAllAccounts(_passKey)
                : _manifest.GetAllAccounts();

            foreach (var acc in steamAccounts)
            {
                var meta = _uiMeta.GetOrCreate(acc.Session.SteamID);
                var vm = new AccountViewModel(acc, meta);
                vm.RefreshCode();
                Accounts.Add(vm);
            }

            OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();
        }

        private void StartCodeRefreshTimer()
        {
            _codeRefreshTimer?.Dispose();
            _codeRefreshTimer = new System.Threading.Timer(_ =>
            {
                foreach (var acc in Accounts) acc.RefreshCode();
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private void StartConfirmationPolling()
        {
            // NOTE: the dashboard spec requires the confirmations panel to always be live and
            // auto-updating, so unlike the original (where periodic_checking gated whether any
            // background checking happened at all), polling always runs here. The legacy
            // periodic_checking flag/checkbox is preserved in Settings and in manifest.json for
            // compatibility, but only PeriodicCheckingInterval is actually consulted below.
            int intervalMinutes = _manifest?.PeriodicCheckingInterval ?? 1;
            if (intervalMinutes < 1) intervalMinutes = 1;
            _pollingService.Start(TimeSpan.FromMinutes(intervalMinutes));
        }

        /// <summary>Called by MainWindow after the Settings dialog is closed with changes, so the
        /// new periodic_checking / interval values take effect immediately.</summary>
        public void ReloadManifestSettings()
        {
            _manifest = Manifest.GetManifest(true);
            IsManifestEncrypted = _manifest?.Encrypted ?? false;
            StartConfirmationPolling();
        }

        private void OnConfirmationsUpdated(List<(AccountViewModel Owner, Confirmation Confirmation)> results)
        {
            // Called from a ThreadPool timer thread - the view's dispatcher wrapper (see MainWindow)
            // marshals back to the UI thread before this fires, OR callers must dispatch themselves.
            Confirmations.Clear();
            foreach (var (owner, conf) in results.OrderBy(r => r.Owner.DisplayName))
            {
                Confirmations.Add(new ConfirmationViewModel(conf, owner));
            }
            SyncConfirmationCount();
        }

        private void SyncConfirmationCount()
        {
            ConfirmationCount = Confirmations.Count;
            OnPropertyChanged(nameof(HasConfirmations));
        }

        private void OnAccountPollFailed(AccountViewModel account, Exception ex)
        {
            // A WGTokenExpiredException-style failure usually means the session needs a fresh
            // login. We surface this passively (icon/status) rather than popping a dialog per
            // poll cycle, matching the original's non-intrusive tray-only status.
            StatusMessage = $"{account.DisplayName}: session may need to be refreshed ({ex.Message})";
        }

        [RelayCommand]
        private async Task RefreshConfirmationsAsync()
        {
            IsBusy = true;
            try
            {
                await _pollingService.PollOnceAsync();
                StatusMessage = $"Refreshed at {DateTime.Now:T}.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ConfirmAllAsync()
        {
            await RunGroupedByAccountAsync(Confirmations.ToList(), accept: true);
        }

        [RelayCommand]
        private async Task RejectAllAsync()
        {
            await RunGroupedByAccountAsync(Confirmations.ToList(), accept: false);
        }

        [RelayCommand]
        private async Task ConfirmOneAsync(ConfirmationViewModel? item)
        {
            if (item == null) return;
            await RunGroupedByAccountAsync(new List<ConfirmationViewModel> { item }, accept: true);
        }

        [RelayCommand]
        private async Task RejectOneAsync(ConfirmationViewModel? item)
        {
            if (item == null) return;
            await RunGroupedByAccountAsync(new List<ConfirmationViewModel> { item }, accept: false);
        }

        private async Task RunGroupedByAccountAsync(List<ConfirmationViewModel> items, bool accept)
        {
            IsBusy = true;
            try
            {
                var byAccount = items.GroupBy(i => i.Owner);
                foreach (var group in byAccount)
                {
                    var confs = group.Select(g => g.Confirmation).ToArray();
                    try
                    {
                        bool ok;
                        try
                        {
                            ok = accept
                                ? await group.Key.Account.AcceptMultipleConfirmations(confs)
                                : await group.Key.Account.DenyMultipleConfirmations(confs);
                        }
                        catch (Exception actionEx) when (group.Key.Account.Session.IsAccessTokenExpired())
                        {
                            // Session died between the last poll and this click - try to recover
                            // it (Task 2) and retry the confirm/reject once before giving up.
                            var outcome = await _recoveryService.EnsureValidSessionAsync(
                                group.Key.Account, group.Key.Meta.SaveLoginEnabled, () => PersistAccount(group.Key.Account));

                            if (outcome != SessionRecoveryService.RecoveryOutcome.Recovered)
                            {
                                throw actionEx;
                            }

                            ok = accept
                                ? await group.Key.Account.AcceptMultipleConfirmations(confs)
                                : await group.Key.Account.DenyMultipleConfirmations(confs);
                        }

                        if (ok)
                        {
                            foreach (var item in group.ToList())
                            {
                                Confirmations.Remove(item);
                            }
                            group.Key.PendingConfirmationCount = Math.Max(0, group.Key.PendingConfirmationCount - confs.Length);
                            SyncConfirmationCount();
                        }
                        else
                        {
                            StatusMessage = $"{group.Key.DisplayName}: failed to {(accept ? "confirm" : "reject")} one or more items.";
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"{group.Key.DisplayName}: {ex.Message}";
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void CopyCode(AccountViewModel? account)
        {
            if (account == null) return;
            DialogService.CopyToClipboard(account.Code);
        }

        [RelayCommand]
        private async Task RenameAccountAsync(AccountViewModel? account)
        {
            if (account == null) return;
            var newName = await DialogService.PromptTextAsync("Rename account", "Display name", account.DisplayName);
            if (newName != null && newName.Trim().Length > 0)
            {
                account.DisplayName = newName.Trim();
                _uiMeta?.Save();
                OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();
            }
        }

        [RelayCommand]
        private void ToggleEnabled(AccountViewModel? account)
        {
            if (account == null) return;
            account.Enabled = !account.Enabled;
            _uiMeta?.Save();
        }

        [RelayCommand]
        private async Task RemoveAccountAsync(AccountViewModel? account)
        {
            if (account == null || _manifest == null) return;
            bool confirmed = await DialogService.ConfirmAsync(
                $"Remove {account.DisplayName}?",
                "This deletes the local .maFile. If you have not backed up the revocation code, you may permanently lose the ability to remove Steam Guard from this account through normal means. This cannot be undone here.");
            if (!confirmed) return;

            if (_manifest.RemoveAccount(account.Account))
            {
                DeleteSavedPasswordSafely(account.Account.Session.SteamID);
                _uiMeta?.Remove(account.Account.Session.SteamID);
                _uiMeta?.Save();
                Accounts.Remove(account);
                OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();
            }
            else
            {
                await DialogService.ShowMessageAsync("Failed to remove the account.");
            }
        }

        /// <summary>Called by the view after a drag-and-drop reorder. Persists the new order
        /// into ui-meta.json (manifest.json's own Entries order is untouched, preserving parity).</summary>
        public void PersistReorder(IEnumerable<AccountViewModel> orderedAccounts)
        {
            int i = 0;
            foreach (var acc in orderedAccounts)
            {
                acc.Order = i++;
            }
            _uiMeta?.Save();
            OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();
        }

        [RelayCommand]
        private void MoveAccountUp(AccountViewModel? account)
        {
            if (account == null || !string.IsNullOrEmpty(SearchText)) return;
            var list = DisplayedAccounts.ToList();
            int index = list.IndexOf(account);
            if (index <= 0) return;

            (list[index - 1], list[index]) = (list[index], list[index - 1]);
            PersistReorder(list);
        }

        [RelayCommand]
        private void MoveAccountDown(AccountViewModel? account)
        {
            if (account == null || !string.IsNullOrEmpty(SearchText)) return;
            var list = DisplayedAccounts.ToList();
            int index = list.IndexOf(account);
            if (index < 0 || index >= list.Count - 1) return;

            (list[index], list[index + 1]) = (list[index + 1], list[index]);
            PersistReorder(list);
        }

        /// <summary>Ported from the original WinForms SDA's menuDeactivateAuthenticator_Click, with
        /// the same safety checks and messages: verify the session is still valid (refreshing the
        /// access token if needed), ask whether to remove Steam Guard completely or fall back to
        /// email codes, make the person re-enter a currently-generated code as a sanity check that
        /// this copy of the authenticator still works, then call Steam's RemoveAuthenticator and
        /// delete the local maFile on success.</summary>
        [RelayCommand]
        private async Task DeactivateAuthenticatorAsync(AccountViewModel? account)
        {
            if (account == null || _manifest == null) return;

            if (account.Account.Session.IsRefreshTokenExpired())
            {
                await DialogService.ShowMessageAsync("Your session has expired. Use \"Refresh Login…\" on this account first, then try again.");
                return;
            }

            if (account.Account.Session.IsAccessTokenExpired())
            {
                try
                {
                    await account.Account.Session.RefreshAccessToken();
                }
                catch (Exception ex)
                {
                    await DialogService.ShowMessageAsync("Could not refresh the session: " + ex.Message);
                    return;
                }
            }

            bool? choice = await DialogService.ConfirmThreeWayAsync(
                $"Deactivate Authenticator: {account.DisplayName}",
                "Would you like to remove Steam Guard Completely?",
                yesLabel: "Yes - Remove Steam Guard Completely",
                noLabel: "No - Switch back to Email authentication",
                cancelLabel: "Cancel");

            // scheme: 2 = remove completely, 1 = switch back to email codes, 0 = cancelled
            int scheme = choice == true ? 2 : choice == false ? 1 : 0;
            if (scheme == 0)
            {
                StatusMessage = "Steam Guard was not removed. No action was taken.";
                return;
            }

            // Sanity check: make this account prove it can still generate a valid code before we
            // let Steam remove it, exactly like the original SDA's confirmation-code step.
            string confCode = account.Account.GenerateSteamGuardCode();
            string? enteredCode = await DialogService.PromptTextAsync(
                $"Deactivate Authenticator: {account.DisplayName}",
                $"Removing Steam Guard from {account.DisplayName}. Enter this confirmation code: {confCode}");

            if (enteredCode == null) return; // dismissed - no changes made

            if (enteredCode.Trim().ToUpperInvariant() != confCode)
            {
                await DialogService.ShowMessageAsync("Confirmation codes do not match. Steam Guard not removed.");
                return;
            }

            IsBusy = true;
            bool success;
            try
            {
                success = await account.Account.DeactivateAuthenticator(scheme);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                await DialogService.ShowMessageAsync("Steam Guard failed to deactivate: " + ex.Message);
                return;
            }
            IsBusy = false;

            if (!success)
            {
                await DialogService.ShowMessageAsync("Steam Guard failed to deactivate.");
                return;
            }

            // scheme 2 = removed completely, scheme 1 = switched back to email codes (never SMS -
            // Steam Guard's fallback from the mobile authenticator is always email, not SMS).
            string outcome = scheme == 2 ? "removed completely" : "switched back to email authentication";
            await DialogService.ShowMessageAsync(
                $"Steam Guard {outcome}. The maFile will be deleted after you close this message. If you need a backup, make one now.");

            if (_manifest.RemoveAccount(account.Account))
            {
                DeleteSavedPasswordSafely(account.Account.Session.SteamID);
                _uiMeta?.Remove(account.Account.Session.SteamID);
                _uiMeta?.Save();
                Accounts.Remove(account);
                IsManifestEncrypted = _manifest.Encrypted; // RemoveAccount() turns encryption off once the last entry is gone
                OnPropertyChanged(nameof(FilteredAccounts));
                RebuildDisplayedAccounts();
            }
            else
            {
                await DialogService.ShowMessageAsync("Steam Guard was deactivated, but the local maFile could not be removed automatically. Please delete it manually.");
            }
        }

        /// <summary>Top-bar "Setup Encryption" action. Prompts for a new passkey and, via
        /// Manifest.PromptSetupPassKeyAsync -> ChangeEncryptionKey, re-encrypts every existing
        /// .maFile under maFiles/ in place so they (and the manifest's per-entry salt/IV) are
        /// locked with it - not just newly-added accounts.</summary>
        [RelayCommand]
        private async Task SetupEncryptionAsync()
        {
            if (_manifest == null) return;

            if (_manifest.Encrypted)
            {
                await DialogService.ShowMessageAsync("Your maFiles are already encrypted. To change the passkey, remove and re-add accounts, or edit maFiles/manifest.json directly.");
                return;
            }

            if (_manifest.Entries.Count == 0)
            {
                await DialogService.ShowMessageAsync("Add an account before setting up encryption.");
                return;
            }

            string? newPassKey = await _manifest.PromptSetupPassKeyAsync(
                "Choose an encryption passkey. This will lock every maFile in maFiles/ so they can't be read without it.");

            if (newPassKey == null) return; // cancelled, or user chose to stay unencrypted (already warned)

            _passKey = newPassKey;
            IsManifestEncrypted = true;
            await DialogService.ShowMessageAsync("Encryption has been set up. Your maFiles are now locked.");
        }

        /// <summary>Top-bar "Remove Encryption" action - the inverse of Setup Encryption. Confirms
        /// with the person (since this writes every maFile back out as plain text), re-verifies the
        /// current passkey, then decrypts every maFile in place via Manifest.ChangeEncryptionKey.</summary>
        [RelayCommand]
        private async Task RemoveEncryptionAsync()
        {
            if (_manifest == null) return;

            if (!_manifest.Encrypted)
            {
                await DialogService.ShowMessageAsync("Your maFiles are not encrypted.");
                return;
            }

            bool confirmed = await DialogService.ConfirmAsync(
                "Remove Encryption",
                "This decrypts every maFile in maFiles/ and stores them as plain text. Anyone with access to this computer would then be able to read them. Continue?");
            if (!confirmed) return;

            // Always re-prompt for the passkey here, even though we may already be unlocked -
            // removing encryption is sensitive and hard to undo, so it shouldn't happen just
            // because the app happens to be sitting unlocked at the moment. PromptForPassKeyAsync
            // loops until a correct passkey is entered or the person cancels.
            string? currentPassKey = await _manifest.PromptForPassKeyAsync();
            if (currentPassKey == null) return;

            if (!_manifest.ChangeEncryptionKey(currentPassKey, null))
            {
                await DialogService.ShowMessageAsync("Incorrect passkey. Encryption was not removed.");
                return;
            }

            _passKey = null;
            IsManifestEncrypted = false;
            await DialogService.ShowMessageAsync("Encryption removed. Your maFiles are now stored as plain text.");
        }

        /// <summary>Adds a freshly-imported/logged-in account (from the Import/Login flow) to the
        /// live dashboard without requiring a full manifest reload. saveLoginRequested/password
        /// come straight from LoginWindow.SavePasswordRequested/ConsumeEnteredPassword() - see
        /// ApplySavedLoginPreference for what happens with them (Task 1).</summary>
        public void AddImportedAccount(SteamGuardAccount account, bool saveLoginRequested = false, string? password = null)
        {
            if (_uiMeta == null) _uiMeta = UiMetaStore.Load();
            var meta = _uiMeta.GetOrCreate(account.Session.SteamID);
            var vm = new AccountViewModel(account, meta);
            vm.RefreshCode();
            Accounts.Add(vm);
            OnPropertyChanged(nameof(FilteredAccounts));
            RebuildDisplayedAccounts();

            ApplySavedLoginPreference(vm, saveLoginRequested, password);
        }

        /// <summary>Single place where the "Save password for automatic re-login" checkbox
        /// (Task 1) is actually acted on: writes/clears the password in the OS-native secure
        /// credential store and keeps AccountMeta.SaveLoginEnabled (the non-sensitive on/off
        /// flag persisted in ui-meta.json) in sync with it. Called after every login flow that
        /// shows LoginWindow - Refresh Login, Import, and the Initial/new-authenticator flow.</summary>
        public void ApplySavedLoginPreference(AccountViewModel account, bool enabled, string? password)
        {
            ulong steamId = account.Account.Session.SteamID;
            string accountKey = steamId.ToString();
            var store = CredentialStoreFactory.Get();

            try
            {
                if (enabled && !string.IsNullOrEmpty(password))
                {
                    if (!store.IsSupported)
                    {
                        string hint = string.IsNullOrEmpty(store.UnavailableHint) ? "" : $" {store.UnavailableHint}";
                        StatusMessage = $"Could not save password: no secure credential store ({store.DisplayName}) is available on this system.{hint}";
                        Logger.Warn("Auth", $"{Logger.AccountRef(steamId)} save-password requested but {store.DisplayName} is unavailable - not saved.");
                        account.Meta.SaveLoginEnabled = false;
                    }
                    else
                    {
                        store.SavePassword(accountKey, account.Account.AccountName, password);
                        account.Meta.SaveLoginEnabled = true;
                        _recoveryService.ResetFailureState(steamId);
                        Logger.Info("Auth", $"{Logger.AccountRef(steamId)} password saved to {store.DisplayName} for automatic re-login.");
                    }
                }
                else if (account.Meta.SaveLoginEnabled)
                {
                    // Checkbox was unchecked (or this login didn't touch the preference at all
                    // but a password was previously saved and is now being explicitly turned
                    // off) - Task 1: "If disabled: Do not persist the password."
                    if (store.IsSupported) store.DeletePassword(accountKey);
                    account.Meta.SaveLoginEnabled = false;
                    Logger.Info("Auth", $"{Logger.AccountRef(steamId)} saved password removed.");
                }
            }
            catch (CredentialStoreException ex)
            {
                StatusMessage = $"Could not update the saved password: {ex.Message}";
                Logger.Error("Auth", $"{Logger.AccountRef(steamId)} credential store operation failed: {ex.Message}");
            }
            finally
            {
                password = null; // do not keep the plaintext around any longer than this call
                _uiMeta?.Save();
            }
        }

        /// <summary>Explicit "forget saved password" action, used by the account context menu -
        /// same effect as unchecking the box and logging in again, without requiring a login.</summary>
        public void ForgetSavedPassword(AccountViewModel? account)
        {
            if (account == null || !account.Meta.SaveLoginEnabled) return;
            ApplySavedLoginPreference(account, enabled: false, password: null);
        }

        /// <summary>Best-effort credential cleanup when an account is removed/deactivated, so a
        /// saved password never outlives the account it belongs to.</summary>
        private static void DeleteSavedPasswordSafely(ulong steamId)
        {
            try
            {
                var store = CredentialStoreFactory.Get();
                if (store.IsSupported) store.DeletePassword(steamId.ToString());
            }
            catch (CredentialStoreException ex)
            {
                Logger.Warn("Auth", $"{Logger.AccountRef(steamId)} failed to remove saved password during account removal: {ex.Message}");
            }
        }

        public void Shutdown()
        {
            _codeRefreshTimer?.Dispose();
            _pollingService.Dispose();
        }
    }
}
