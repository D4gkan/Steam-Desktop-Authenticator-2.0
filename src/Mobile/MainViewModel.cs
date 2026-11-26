using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using QRCoder;
using Sda.Mobile.Core;
using SteamAuth;

namespace Sda.Mobile;

public sealed record PromptRequest(string Title, string Message, bool IsPassword = false, bool IsConfirmation = false,
    bool IsNumeric = false, bool IsMultiline = false, string AcceptText = "Continue", int PinLength = 6, bool AllowLegacyPinLength = false,
    Func<string, Task<string?>>? ValidatePinAsync = null, bool CanCancel = true);
public enum MobileSection { Accounts, Activity, Settings }

public partial class MobileAccount : ObservableObject
{
    public SteamGuardAccount Account { get; }
    public string Name => Account.AccountName;
    public string SteamId => Account.Session.SteamID.ToString();
    public string Initial => Name.Length == 0 ? "S" : Name[..1].ToUpperInvariant();
    public bool IsPending => !Account.FullyEnrolled;
    [ObservableProperty] private string _code = "-----";
    [ObservableProperty] private int _secondsRemaining = 30;
    public MobileAccount(SteamGuardAccount account) => Account = account;
}

public sealed record MobileConfirmation(Confirmation Confirmation, MobileAccount Owner)
{
    public string AccountName => Owner.Name;
    public string Title => Confirmation.Headline ?? Confirmation.TypeName ?? "Steam confirmation";
    public string Description => string.Join("\n", Confirmation.Summary ?? new List<string>());
    public string Kind => Confirmation.ConfType == Confirmation.EMobileConfirmationType.Unknown ? "Unknown confirmation type" : Confirmation.ConfType.ToString();
}

public sealed record ConfirmationIssue(MobileAccount Owner, string Message, bool CanSignIn)
{
    public string AccountName => Owner.Name;
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IMobilePlatform _platform;
    private readonly Func<PromptRequest, Task<string?>> _prompt;
    private readonly VaultPasscode _passcode;
    private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ISteamConfirmationClient _confirmations;
    private readonly TimeProvider _clock;
    private long? _backgroundAt;
    private bool _unlockOnResume;
    private System.Threading.Timer? _backgroundLock;
    private CancellationTokenSource? _badgeRefresh;
    private int _badgeGeneration;
    public static readonly TimeSpan AutoLockDelay = TimeSpan.FromMinutes(15);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource _operation = new();
    private AccountVault? _vault;
    private bool _initialized;
    private bool _active = true;
    private long _clockOffset;

    public event Action? Locking;

    public ObservableCollection<MobileAccount> Accounts { get; } = new();
    public ObservableCollection<MobileConfirmation> Confirmations { get; } = new();
    public ObservableCollection<ConfirmationIssue> ConfirmationIssues { get; } = new();
    [ObservableProperty] private MobileAccount? _selectedAccount;
    [ObservableProperty] private string _status = "Your accounts stay encrypted on this device.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _showNotice;
    [ObservableProperty] private string _busyMessage = "Working…";
    [ObservableProperty] private int _secondsRemaining = 30;
    [ObservableProperty] private int _pendingConfirmationCount;
    [ObservableProperty] private double _codeProgress = 100;
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private MobileSection _section;
    public int AccountCount => Accounts.Count;
    public bool ShowAccounts => Section == MobileSection.Accounts;
    public bool ShowActivity => Section == MobileSection.Activity;
    public bool ShowSettings => Section == MobileSection.Settings;
    public string PageTitle => Section switch { MobileSection.Activity => "Confirmations", MobileSection.Settings => "Settings", _ => "SDA Mobile" };
    public string PageSubtitle => Section switch { MobileSection.Activity => "Review each trade before you approve.", MobileSection.Settings => "Security, transfers, and account access.", _ => "Your accounts. Ready when you need them." };
    public bool HasSelectedAccount => SelectedAccount != null;
    public bool HasQr => QrImage != null;
    public bool HasNoAccounts => Accounts.Count == 0;
    public bool HasNoConfirmations => Confirmations.Count == 0 && ConfirmationIssues.Count == 0;
    public bool CanInteract => IsUnlocked && !IsBusy;
    public bool HasPendingBadge => PendingConfirmationCount > 0;
    public string PendingBadgeText => PendingConfirmationCount > 99 ? "99+" : PendingConfirmationCount.ToString();

    public MainViewModel(IMobilePlatform platform, Func<PromptRequest, Task<string?>> prompt, TimeProvider? clock = null)
    {
        _platform = platform;
        _prompt = prompt;
        _clock = clock ?? TimeProvider.System;
        _passcode = new VaultPasscode(platform, _clock);
        _confirmations = platform.CreateConfirmationClient(_http);
        _timer.Tick += (_, _) => UpdateCodes();
        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(AccountCount));
        };
        Confirmations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoConfirmations));
        ConfirmationIssues.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoConfirmations));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        if (value) ShowNotice = true;
    }
    partial void OnStatusChanged(string value) => ShowNotice = true;
    partial void OnIsUnlockedChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnPendingConfirmationCountChanged(int value)
    { OnPropertyChanged(nameof(HasPendingBadge)); OnPropertyChanged(nameof(PendingBadgeText)); }
    partial void OnQrImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasQr));
    partial void OnSectionChanged(MobileSection value)
    {
        ShowNotice = false;
        IsError = false;
        OnPropertyChanged(nameof(ShowAccounts));
        OnPropertyChanged(nameof(ShowActivity));
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }
    partial void OnSelectedAccountChanged(MobileAccount? value)
    {
        CloseQr();
        OnPropertyChanged(nameof(HasSelectedAccount));
    }

    public async Task InitializeAsync()
    {
        if (_initialized || IsUnlocked) return;
        _initialized = true;
        await UnlockAsync();
    }

    public Task UnlockAsync() => IsUnlocked ? Task.CompletedTask : RunAsync(async token =>
    {
        BusyMessage = "Unlocking vault…";
        string key;
        if (!_passcode.IsConfigured)
        {
            var pin = await _prompt(new("Create passcode", "", true, IsNumeric: true, CanCancel: false));
            if (pin == null) return;
            VaultPasscode.Validate(pin);
            if (pin.Length != 6) throw new InvalidOperationException("Choose a 6-digit passcode.");
            var confirmation = await _prompt(new("Confirm passcode", "", true, IsNumeric: true,
                ValidatePinAsync: value => Task.FromResult<string?>(value == pin ? null : "Passcodes do not match."), CanCancel: false));
            if (confirmation == null) return;
            if (pin != confirmation) throw new InvalidOperationException("The passcodes do not match. Your vault has not been changed.");
            key = _platform.GetVaultKey(File.Exists(Path.Combine(_platform.AccountDirectory, "manifest.json")));
            // Verify the old key before migrating. Only the wrapper changes; maFiles do not.
            await Task.Run(() => new AccountVault(_platform.AccountDirectory, key).Load(), token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => _passcode.Create(pin, key), token);
        }
        else
        {
            string? verifiedKey = null;
            var pin = await _prompt(ExistingPasscodeRequest("Enter passcode") with
            {
                CanCancel = false,
                ValidatePinAsync = async value =>
                {
                    try { verifiedKey = await Task.Run(() => _passcode.Unlock(value), token); return null; }
                    catch (InvalidOperationException error) { return error.Message; }
                }
            });
            if (pin == null) return;
            key = verifiedKey ?? await Task.Run(() => _passcode.Unlock(pin), token);
        }
        token.ThrowIfCancellationRequested();
        _platform.RemoveSecurityValue("vault-key-v1");
        var vault = new AccountVault(_platform.AccountDirectory, key);
        var accounts = await Task.Run(vault.Load, token);
        token.ThrowIfCancellationRequested();
        _vault = vault;
        Accounts.Clear();
        foreach (var account in accounts) Accounts.Add(new MobileAccount(account));
        IsUnlocked = true;
        _timer.Start();
        UpdateCodes();
        Status = Accounts.Count == 0 ? "Import an account file or scan its desktop QR code to begin." : "Unlocked. Tap an account for confirmations and backups.";
        ShowNotice = false;
        // Offline codes are immediately available. Time sync must never block vault unlock.
        _ = AlignClockAsync();
        RequestBadgeRefresh();
    });

    public Task ChangePasscodeAsync() => RunAsync(async token =>
    {
        RequireUnlocked();
        var current = await _prompt(ExistingPasscodeRequest("Current passcode") with
        {
            ValidatePinAsync = async value =>
            {
                try { await Task.Run(() => _passcode.Unlock(value), token); return null; }
                catch (InvalidOperationException error) { return error.Message; }
            }
        });
        if (current == null) return;
        var next = await _prompt(new("New passcode", "", true, IsNumeric: true));
        if (next == null) return;
        VaultPasscode.Validate(next);
        if (next.Length != 6) throw new InvalidOperationException("Choose a 6-digit passcode.");
        var confirmation = await _prompt(new("Confirm passcode", "", true, IsNumeric: true,
            ValidatePinAsync: value => Task.FromResult<string?>(value == next ? null : "Passcodes do not match.")));
        if (confirmation == null) return;
        if (next != confirmation) throw new InvalidOperationException("The passcodes do not match. Your current passcode is unchanged.");
        BusyMessage = "Updating vault protection…";
        await Task.Run(() => _passcode.Change(current, next), token);
        Status = "Vault passcode updated.";
    });

    private PromptRequest ExistingPasscodeRequest(string title)
    {
        var digits = _passcode.DigitCount;
        return new(title, "", true, IsNumeric: true, PinLength: digits ?? 6, AllowLegacyPinLength: digits == null);
    }

    public void DismissNotice() { ShowNotice = false; IsError = false; }

    public async Task OpenConfirmationsAsync(MobileAccount? account = null)
    {
        Section = MobileSection.Activity;
        if (Accounts.Count > 0) await RefreshConfirmationsAsync();
    }

    private async Task AlignClockAsync()
    {
        try
        {
            _clockOffset = await _confirmations.GetTimeAsync(CancellationToken.None) - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        catch (Exception) { /* Continue using the device clock while offline. */ }
        if (_active && IsUnlocked) UpdateCodes();
    }

    public void Lock()
    {
        _backgroundLock?.Dispose();
        _backgroundLock = null;
        _backgroundAt = null;
        CancelBadgeRefresh();
        PendingConfirmationCount = 0;
        _operation.Cancel();
        _platform.CancelPrompt();
        Locking?.Invoke();
        _timer.Stop();
        CloseQr();
        SelectedAccount = null;
        Accounts.Clear();
        Confirmations.Clear();
        ConfirmationIssues.Clear();
        _vault = null;
        IsUnlocked = false;
        Status = "Locked. Unlock to view your accounts.";
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active)
        {
            CancelBadgeRefresh();
            _timer.Stop();
            CloseQr();
            _backgroundAt = _clock.GetTimestamp();
            _backgroundLock?.Dispose();
            _backgroundLock = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(() =>
            {
                if (!_active && HasBackgroundExpired()) Lock();
            }), null, AutoLockDelay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _backgroundLock?.Dispose();
            _backgroundLock = null;
            if (HasBackgroundExpired()) Lock();
            _backgroundAt = null;
            if (IsUnlocked) { _timer.Start(); UpdateCodes(); RequestBadgeRefresh(); }
            else if (!IsBusy) Dispatcher.UIThread.Post(async () => await UnlockAsync());
            else _unlockOnResume = true;
        }
    }

    private bool HasBackgroundExpired() => _backgroundAt is { } timestamp && _clock.GetElapsedTime(timestamp) >= AutoLockDelay;

    private void CancelBadgeRefresh()
    {
        _badgeGeneration++;
        _badgeRefresh?.Cancel();
        _badgeRefresh?.Dispose();
        _badgeRefresh = null;
    }

    private void RequestBadgeRefresh()
    {
        CancelBadgeRefresh();
        if (!IsUnlocked || !_active || Accounts.Count == 0) return;
        var source = new CancellationTokenSource();
        _badgeRefresh = source;
        _ = UpdateBadgeAsync(source.Token, _badgeGeneration);
    }

    private async Task UpdateBadgeAsync(CancellationToken token, int generation)
    {
        // Badge requests use disposable session copies. They never mutate/persist vault
        // data or block offline codes and account controls behind the UI operation gate.
        var vault = _vault;
        var copies = Accounts.Select(owner => new SteamGuardAccount
        {
            IdentitySecret = owner.Account.IdentitySecret, DeviceID = owner.Account.DeviceID,
            Session = new SessionData { SteamID = owner.Account.Session.SteamID, AccessToken = owner.Account.Session.AccessToken,
                RefreshToken = owner.Account.Session.RefreshToken, SessionID = owner.Account.Session.SessionID }
        }).ToArray();
        var count = 0;
        try
        {
            foreach (var account in copies)
            {
                token.ThrowIfCancellationRequested();
                var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset;
                await _confirmations.RefreshSessionAsync(account.Session, time, token);
                count += (await _confirmations.FetchAsync(account, time, token)).Count;
            }
            if (!token.IsCancellationRequested && generation == _badgeGeneration && IsUnlocked && _active && ReferenceEquals(_vault, vault))
                PendingConfirmationCount = count;
        }
        catch (Exception) { /* Keep the last known count; details appear on manual refresh. */ }
    }

    public Task ScanAsync() => RunAsync(async token =>
    {
        RequireUnlocked();
        BusyMessage = "Scanning account QR…";
        Status = "Point the camera at the account transfer QR in SDA Desktop.";
        var payload = await _platform.ScanQrAsync();
        token.ThrowIfCancellationRequested();
        if (payload == null) { Status = "QR scan cancelled. No account was imported."; return; }
        Status = "QR recognized. Reading the account before Steam sign-in…";
        BusyMessage = "Reading account data…";
        await ImportAccountsAsync(new[] { await Task.Run(() => AccountTransfer.ReadQr(payload, true), token) }, token);
    });

    public Task PasteQrAsync() => RunAsync(async token =>
    {
        RequireUnlocked();
        Status = "Paste the account data, then continue to Steam sign-in.";
        var payload = await _prompt(new("Paste account data", "Paste a plaintext maFile JSON document or SDA QR payload. Steam sign-in is required before it is saved.", IsMultiline: true, AcceptText: "Continue"));
        if (payload == null) { Status = "Paste import cancelled. No account was imported."; return; }
        if (string.IsNullOrWhiteSpace(payload)) throw new InvalidOperationException("No account data was pasted. Copy your maFile contents and try again.");
        BusyMessage = "Reading account data…";
        Status = "Reading pasted account data…";
        await ImportAccountsAsync(new[] { await Task.Run(() => AccountTransfer.ReadPasted(payload, true), token) }, token);
    });

    public Task ImportFilesAsync(IReadOnlyList<(string Name, byte[] Bytes)> files) => RunAsync(async token =>
        await ImportFilesCoreAsync(files, token));

    public Task ImportAsync(Func<Task<IReadOnlyList<ImportFile>>> fallback) => RunAsync(async token =>
    {
        RequireUnlocked();
        BusyMessage = "Choose account files…";
        var selected = await _platform.PickImportFilesAsync() ?? await fallback();
        token.ThrowIfCancellationRequested();
        if (selected.Count == 0) { Status = "Import cancelled. No accounts were changed."; return; }
        await ImportFilesCoreAsync(selected.Select(file => (file.Name, file.Bytes)).ToArray(), token);
    });

    private async Task ImportFilesCoreAsync(IReadOnlyList<(string Name, byte[] Bytes)> files, CancellationToken token)
    {
        RequireUnlocked();
        BusyMessage = "Reading account files…";
        Status = "Reading account files…";
        var imported = new List<SteamGuardAccount>();
        var manifestFile = files.FirstOrDefault(file => file.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        var manifest = manifestFile.Bytes == null ? null : AccountVault.ReadImportManifest(AccountTransfer.ReadFileText(manifestFile.Bytes));
        string? password = null;
        foreach (var file in files.Where(file => !file.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                password ??= await _prompt(new("Backup password", "Enter the password used to encrypt this backup.", true));
                if (password == null) return;
                using var stream = new MemoryStream(file.Bytes);
                imported.AddRange(await Task.Run(() => AccountTransfer.ReadBackup(stream, password), token));
                continue;
            }
            var payload = AccountTransfer.ReadFileText(file.Bytes);
            var entry = manifest?.Entries.FirstOrDefault(item => item.Filename.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
            if (manifest?.Encrypted == true)
            {
                password ??= await _prompt(new("Import passkey", "Enter the original desktop or Android encryption passkey.", true));
                if (password == null) return;
            }
            imported.Add(await Task.Run(() => AccountTransfer.Read(payload, entry, password, true), token));
        }
        if (imported.Count == 0) throw new InvalidDataException("Select at least one maFile or an SDA backup ZIP.");
        await ImportAccountsAsync(imported, token);
    }

    private async Task ImportAccountsAsync(IEnumerable<SteamGuardAccount> accounts, CancellationToken token)
    {
        var authenticated = new List<SteamGuardAccount>();
        foreach (var account in accounts)
        {
            token.ThrowIfCancellationRequested();
            BusyMessage = "Steam sign-in required to finish importing…";
            Status = $"Account read successfully. Sign in as {account.AccountName} to complete the import.";
            var expectedSteamId = account.Session.SteamID;
            var session = await LoginAsync(account.AccountName, token, account);
            if (session == null)
            {
                Status = "Steam sign-in cancelled. No imported accounts were saved.";
                return;
            }
            token.ThrowIfCancellationRequested();
            if (expectedSteamId != 0 && session.SteamID != expectedSteamId)
                throw new InvalidOperationException("Steam sign-in belongs to a different account. The imported account was not saved.");
            account.Session = session;
            var existing = Accounts.FirstOrDefault(item => item.SteamId == account.Session.SteamID.ToString());
            if (existing != null && await _prompt(new("Replace account?", $"Replace the saved account {existing.Name} with the imported copy?", IsConfirmation: true)) == null)
                continue;
            authenticated.Add(account);
        }
        var count = 0;
        foreach (var account in authenticated)
        {
            token.ThrowIfCancellationRequested();
            BusyMessage = "Encrypting and saving account…";
            await SaveAccountAsync(account, token);
            count++;
        }
        Section = MobileSection.Accounts;
        Status = count == 0 ? "No accounts imported. Your existing accounts are unchanged."
            : $"Imported and signed in to {count} account(s). Steam Guard codes are ready below.";
        RequestBadgeRefresh();
    }

    public void CopyCode(MobileAccount account)
    {
        if (!IsUnlocked || !Accounts.Contains(account)) return;
        _platform.CopyCode(account.Code);
        Status = "Steam Guard code copied for 30 seconds.";
    }

    public Task SignInAsync() => RunAsync(async token =>
    {
        RequireUnlocked();
        var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
        var session = await LoginAsync(account.Name, token, account.Account);
        if (session == null) return;
        token.ThrowIfCancellationRequested();
        if (session.SteamID != account.Account.Session.SteamID)
            throw new InvalidOperationException("That sign-in belongs to a different Steam account.");
        account.Account.Session = session;
        await SaveAccountAsync(account.Account, token);
        Status = "Signed in. You can now refresh confirmations.";
    });

    private async Task<SessionData?> LoginAsync(string? suggestedName, CancellationToken token, SteamGuardAccount? importedAccount = null)
    {
        var username = suggestedName ?? await _prompt(new("Steam sign-in", "Enter your Steam account username."));
        if (username == null) return null;
        var password = await _prompt(new("Steam password", $"Sign in as {username}. Your password is used for this sign-in and is not saved.", true));
        if (password == null) return null;
        var login = _platform.CreateLoginClient(_http);
        BusyMessage = "Signing in to Steam…";
        Status = "Signing in to Steam…";
        var challenge = await login.BeginAsync(username, password, token);
        password = null;
        if (importedAccount?.Session.SteamID is > 0 && importedAccount.Session.SteamID != login.SteamId)
            throw new InvalidOperationException("Steam sign-in belongs to a different account. The imported account was not saved.");
        if (challenge is { Type: 2 or 3 })
        {
            var saved = challenge.Type == 3 ? importedAccount ?? Accounts.FirstOrDefault(item =>
                item.Account.FullyEnrolled && item.Account.Session.SteamID == login.SteamId)?.Account : null;
            var code = saved?.GenerateSteamGuardCodeForTime(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset)
                ?? await _prompt(new("Steam Guard", challenge.Type == 2
                    ? $"Enter the email code. {challenge.Message}" : "Enter the code from your existing authenticator."));
            if (code == null) return null;
            await login.SubmitCodeAsync(code, challenge.Type, token);
        }
        else if (challenge is { Type: 4 })
        {
            if (await _prompt(new("Approve Steam sign-in", "Approve this sign-in in your existing authenticator, then tap Continue.", IsConfirmation: true)) == null)
                return null;
        }
        Status = "Waiting for Steam sign-in…";
        return await login.PollAsync(token);
    }

    public Task RefreshConfirmationsAsync() => RunAsync(async token =>
    {
        CancelBadgeRefresh();
        Confirmations.Clear();
        ConfirmationIssues.Clear();
        RequireUnlocked();
        foreach (var account in Accounts.ToArray())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                BusyMessage = "Refreshing Steam sessions…";
                await EnsureSessionAsync(account.Account, token);
                BusyMessage = "Loading confirmations…";
                IReadOnlyList<Confirmation> confirmations;
                try { confirmations = await _confirmations.FetchAsync(account.Account, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset, token); }
                catch (SteamSignInRequiredException)
                {
                    await RefreshRejectedSessionAsync(account.Account, token);
                    confirmations = await _confirmations.FetchAsync(account.Account, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset, token);
                }
                token.ThrowIfCancellationRequested();
                foreach (var confirmation in confirmations) Confirmations.Add(new(confirmation, account));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                var message = error switch
                {
                    InvalidOperationException or InvalidDataException => error.Message,
                    HttpRequestException => "Could not reach Steam. Pull down to retry.",
                    _ => "Could not load this account’s confirmations. Pull down to retry."
                };
                ConfirmationIssues.Add(new(account, message, error is SteamSignInRequiredException));
            }
        }
        Status = Confirmations.Count == 0 ? "No pending confirmations." : $"{Confirmations.Count} pending confirmation(s).";
        PendingConfirmationCount = Confirmations.Count;
        ShowNotice = false;
    });

    public Task RespondAsync(MobileConfirmation item, bool accept) => RunAsync(async token =>
    {
        CancelBadgeRefresh();
        BusyMessage = "Checking Steam session…";
        var account = item.Owner;
        if (!Accounts.Contains(account)) throw new InvalidOperationException("Refresh confirmations before responding.");
        if (await _prompt(new(accept ? "Approve confirmation?" : "Reject confirmation?", item.AccountName + "\n" + item.Title + "\n" + item.Description, IsConfirmation: true)) == null) return;
        await EnsureSessionAsync(account.Account, token);
        token.ThrowIfCancellationRequested();
        BusyMessage = accept ? "Approving confirmation…" : "Rejecting confirmation…";
        try { await _confirmations.AnswerAsync(account.Account, item.Confirmation, accept, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset, token); }
        catch (SteamSignInRequiredException)
        {
            await RefreshRejectedSessionAsync(account.Account, token);
            await _confirmations.AnswerAsync(account.Account, item.Confirmation, accept, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset, token);
        }
        token.ThrowIfCancellationRequested();
        Confirmations.Remove(item);
        PendingConfirmationCount = Math.Max(0, PendingConfirmationCount - 1);
        Status = accept ? "Confirmation approved." : "Confirmation rejected.";
        RequestBadgeRefresh();
    });

    private async Task EnsureSessionAsync(SteamGuardAccount account, CancellationToken token)
    {
        if (!await _confirmations.RefreshSessionAsync(account.Session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset, token)) return;
        token.ThrowIfCancellationRequested();
        await SaveAccountAsync(account, token);
    }

    private async Task RefreshRejectedSessionAsync(SteamGuardAccount account, CancellationToken token)
    {
        var previous = account.Session.AccessToken;
        account.Session.AccessToken = null!;
        try { await EnsureSessionAsync(account, token); }
        catch { account.Session.AccessToken = previous; throw; }
    }

    public Task LinkAsync() => RunAsync(async token =>
    {
        if (await _prompt(new("Link a new authenticator?", "This changes Steam Guard on your Steam account. Use a disposable account for testing. Import an existing maFile instead if this account already has an authenticator.", IsConfirmation: true)) == null) return;
        var username = await _prompt(new("Steam username", "Enter the account to link."));
        if (string.IsNullOrWhiteSpace(username)) return;
        var session = await LoginAsync(username, token);
        if (session == null) return;
        var vault = _vault ?? throw new OperationCanceledException();
        if (Accounts.Any(item => item.Account.Session.SteamID == session.SteamID))
            throw new InvalidOperationException("This account is already saved. Select it to refresh sign-in or resume linking.");
        var linker = new AuthenticatorLinker(session);
        var phoneReady = false;
        for (var step = 0; step < 12 && !phoneReady; step++)
        {
            token.ThrowIfCancellationRequested();
            var result = await linker.AddPhoneNumber();
            token.ThrowIfCancellationRequested();
            switch (result)
            {
                case AuthenticatorLinker.PhoneLinkResult.PhoneAdded:
                    phoneReady = true;
                    break;
                case AuthenticatorLinker.PhoneLinkResult.MustProvidePhoneNumber:
                    linker.PhoneNumber = await _prompt(new("Phone number", "Enter your full phone number including the country calling code, for example +40…"));
                    if (string.IsNullOrWhiteSpace(linker.PhoneNumber)) return;
                    break;
                case AuthenticatorLinker.PhoneLinkResult.MustConfirmEmail:
                    if (await _prompt(new("Verify your phone number", "Open the phone verification email from Steam and follow its link, then return here and tap Continue.", IsConfirmation: true)) == null) return;
                    break;
                case AuthenticatorLinker.PhoneLinkResult.MustConfirmSMS:
                    linker.PhoneSMSCode = await _prompt(new("Phone verification SMS", "Enter the code Steam sent to your phone."));
                    if (string.IsNullOrWhiteSpace(linker.PhoneSMSCode)) return;
                    break;
                default:
                    throw new InvalidOperationException("Steam could not verify the phone number. Check it in Steam and try again.");
            }
        }
        if (!phoneReady) throw new InvalidOperationException("Phone verification did not finish. Check it in Steam and try again.");
        token.ThrowIfCancellationRequested();
        var linkResult = await linker.AddAuthenticator();
        if (linkResult == AuthenticatorLinker.LinkResult.AuthenticatorPresent)
            throw new InvalidOperationException("This account already has an authenticator. Import its existing maFile instead.");
        if (linkResult != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            throw new InvalidOperationException("Steam could not add an authenticator. Check the account's verified phone number and retry.");
        var pending = linker.LinkedAccount;
        pending.AccountName = username;
        // Save BEFORE observing cancellation: the server may have created secrets while the
        // app was going into the background. Never throw those secrets away.
        vault.Save(pending);
        token.ThrowIfCancellationRequested();
        await SaveAccountAsync(pending, token);
        await FinishLinkAsync(pending, token);
    });

    public Task ResumeLinkAsync() => RunAsync(async token =>
    {
        var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
        await EnsureSessionAsync(account.Account, token);
        await FinishLinkAsync(account.Account, token);
    });

    private async Task FinishLinkAsync(SteamGuardAccount account, CancellationToken token)
    {
        if (await _prompt(new("Save your revocation code", $"Revocation code: {account.RevocationCode}\n\nWrite this down somewhere safe and offline. It is required to recover your account if the authenticator is lost. The pending account is already saved on this device.", IsConfirmation: true)) == null) return;
        var sms = await _prompt(new("Activate authenticator", "Enter the activation code Steam sent by SMS. If you cancel, select this account and tap Resume authenticator linking later."));
        if (string.IsNullOrWhiteSpace(sms)) return;
        var vault = _vault ?? throw new OperationCanceledException();
        await new SteamLinkingClient(_http).FinalizeAsync(account, sms, token);
        // Persist successful finalization even if iOS backgrounds the app at this boundary.
        vault.Save(account);
        token.ThrowIfCancellationRequested();
        await SaveAccountAsync(account, token);
        Status = "Authenticator linked. Export an encrypted backup and keep your revocation code safe.";
    }

    public Task ShowQrAsync() => RunAsync(async token =>
    {
        var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
        if (await _prompt(new("Show account QR?", "This QR contains unencrypted authenticator secrets. Show it only to a device you control.", IsConfirmation: true)) == null) return;
        token.ThrowIfCancellationRequested();
        using var data = QRCodeGenerator.GenerateQrCode(AccountTransfer.CreateQrPayload(account.Account), QRCodeGenerator.ECCLevel.L);
        using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(8));
        CloseQr();
        QrImage = new Bitmap(stream);
    });

    public void CloseQr()
    {
        var previous = QrImage;
        QrImage = null;
        previous?.Dispose();
    }

    public Task ExportAsync(Func<byte[], Task> save) => RunAsync(async token =>
    {
        var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
        var password = await _prompt(new("Protect backup", "Choose an encryption password for this account backup. Keep it somewhere safe.", true));
        if (string.IsNullOrEmpty(password)) return;
        var again = await _prompt(new("Confirm backup password", "Enter the backup password again.", true));
        if (again != password) throw new InvalidOperationException("The passwords do not match.");
        token.ThrowIfCancellationRequested();
        BusyMessage = "Encrypting backup…";
        var backup = await Task.Run(() => AccountTransfer.CreateBackup(new[] { account.Account }, password), token);
        await save(backup);
        Status = "Backup export finished.";
    });

    public Task RemoveAsync() => RunAsync(async token =>
    {
        var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
        if (await _prompt(new("Remove local account?", $"Remove {account.Name} from this device? This does not deactivate Steam Guard. Keep a backup first.", IsConfirmation: true)) == null) return;
        token.ThrowIfCancellationRequested();
        var vault = _vault!;
        await Task.Run(() => vault.Remove(account.Account.Session.SteamID), token);
        token.ThrowIfCancellationRequested();
        Accounts.Remove(account);
        Confirmations.Clear();
        ConfirmationIssues.Clear();
        RequestBadgeRefresh();
        SelectedAccount = null;
        Status = "Account removed from this device.";
    });

    private async Task SaveAccountAsync(SteamGuardAccount account, CancellationToken token)
    {
        RequireUnlocked();
        var vault = _vault!;
        await Task.Run(() => vault.Save(account), token);
        token.ThrowIfCancellationRequested();
        if (!IsUnlocked || !ReferenceEquals(vault, _vault)) throw new OperationCanceledException();
        var existing = Accounts.FirstOrDefault(item => item.Account.Session.SteamID == account.Session.SteamID);
        var replacement = new MobileAccount(account);
        if (existing == null) Accounts.Add(replacement);
        else if (!ReferenceEquals(existing.Account, account))
        {
            var selected = SelectedAccount == existing;
            Accounts[Accounts.IndexOf(existing)] = replacement;
            if (selected) SelectedAccount = replacement;
        }
        UpdateCodes();
    }

    private void RequireUnlocked()
    {
        if (!IsUnlocked || _vault == null)
            throw new InvalidOperationException("Unlock your vault before importing or changing accounts.");
    }

    private void UpdateCodes()
    {
        if (!IsUnlocked || !_active) return;
        // No network call in the timer: this keeps codes working when the phone is offline.
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _clockOffset;
        SecondsRemaining = 30 - (int)(time % 30);
        CodeProgress = SecondsRemaining / 30.0 * 100;
        foreach (var account in Accounts)
        {
            account.Code = account.Account.GenerateSteamGuardCodeForTime(time);
            account.SecondsRemaining = SecondsRemaining;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy || !_active) return;
        IsBusy = true;
        IsError = false;
        BusyMessage = "Working…";
        _operation.Dispose();
        _operation = new CancellationTokenSource();
        try { await operation(_operation.Token); }
        catch (OperationCanceledException) { Status = "Operation cancelled."; }
        catch (Exception exception)
        {
            IsError = true;
            // Never display raw HTTP URLs, response bodies, or JSON parser messages: they
            // can include passwords, authenticator material, or session tokens.
            Status = exception switch
            {
                JsonException => $"Could not decode data during: {BusyMessage} (D01). Your existing accounts have not been removed.",
                FormatException => $"An encoded value could not be read during: {BusyMessage} (D02).",
                CryptographicException => $"A cryptographic step failed during: {BusyMessage} (D03). For encrypted imports, check the source passkey.",
                HttpRequestException or WebException => "Could not reach Steam. Check your connection and try again.",
                InvalidDataException or InvalidOperationException or TimeoutException => exception.Message,
                IOException or UnauthorizedAccessException => "Could not read or save account data. Keep your backup and try again.",
                _ => "The operation failed. For confirmations, try signing in again and refreshing."
            };
        }
        finally
        {
            IsBusy = false;
            if (!_active && HasBackgroundExpired()) Lock();
            if (_active && !IsUnlocked && _unlockOnResume)
            {
                _unlockOnResume = false;
                Dispatcher.UIThread.Post(async () => await UnlockAsync());
            }
        }
    }

    public void Dispose()
    {
        Lock();
        _operation.Dispose();
        _http.Dispose();
    }
}
