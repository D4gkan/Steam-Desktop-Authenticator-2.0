using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Sda.Mobile;
using Sda.Mobile.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Sda.Mobile.Tests.MobileTestAppBuilder))]

namespace Sda.Mobile.Tests;

public static class MobileTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MobileTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MobileTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class MobileImportTests
{
    [AvaloniaFact]
    public async Task StartupShowsLogoThenRequestsPasscodeWithoutAnUnlockButton()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        var unlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.IsUnlocked) && model.IsUnlocked) unlocked.TrySetResult(); };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 393, Height = 852, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(view.FindControl<Image>("StartupLogo")!.Source);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Unlock"));
        await unlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, view.FindControl<Image>("StartupLogo")!.Opacity);
        Assert.Equal(2, platform.PinRequests.Count);
        Assert.All(platform.PinRequests, request => Assert.False(request.CanCancel));
        window.Close();
    }

    [AvaloniaFact]
    public async Task CodeCopyRemainsAvailableDuringAConfirmationRequest()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        await ImportTwoAsync(model, platform);
        platform.ConfirmationApi.PendingFetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = model.RefreshConfirmationsAsync();
        Assert.True(model.IsBusy);
        model.Section = MobileSection.Accounts;
        model.CopyCode(model.Accounts[0]);
        Assert.Equal(model.Accounts[0].Code, platform.LastCopiedCode);
        model.Lock();
        await refresh;
        Assert.False(model.IsUnlocked);
    }

    [AvaloniaFact]
    public async Task BadgeUsesSessionCopiesWithoutWritingVaultOrBlockingAccountControls()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        await ImportTwoAsync(model, platform);
        Assert.Equal(2, model.PendingConfirmationCount);
        var manifest = File.ReadAllBytes(Path.Combine(platform.AccountDirectory, "manifest.json"));
        var access = model.Accounts[0].Account.Session.AccessToken;
        platform.ConfirmationApi.MutateSession = true;
        model.SetActive(false);
        model.SetActive(true);
        Assert.Equal(access, model.Accounts[0].Account.Session.AccessToken);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(platform.AccountDirectory, "manifest.json")));
        platform.ConfirmationApi.PendingFetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        model.SetActive(false);
        model.SetActive(true);
        Assert.True(model.CanInteract);
        model.CopyCode(model.Accounts[0]);
        Assert.Equal(model.Accounts[0].Code, platform.LastCopiedCode);
        model.Lock();
        platform.ConfirmationApi.PendingFetch.TrySetResult(Array.Empty<SteamAuth.Confirmation>());
        await Task.Yield();
        Assert.Equal(0, model.PendingConfirmationCount);
        Assert.Empty(model.Accounts);
    }

    [AvaloniaFact]
    public async Task AccountNameCopiesCodeAndOptionsKeepTheirAccountWithoutNavigation()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        await ImportTwoAsync(model, platform);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 393, Height = 852, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var owner = model.Accounts[0];
        var name = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Tag, owner) && button.Content is TextBlock text && text.Text == owner.Name);
        name.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(owner.Code, platform.LastCopiedCode);
        Assert.Equal(MobileSection.Accounts, model.Section);
        var options = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Tag, owner) && button.Flyout is MenuFlyout);
        options.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var menu = (MenuFlyout)options.Flyout!;
        Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.Same(owner, item.Tag));
        Assert.Equal(MobileSection.Accounts, model.Section);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ConfirmationsMergeAccountsAndRespondUsingTheItemOwner()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        await ImportTwoAsync(model, platform);
        model.SelectedAccount = null;
        await model.OpenConfirmationsAsync();
        Assert.Equal(2, model.Confirmations.Count);
        Assert.Empty(model.ConfirmationIssues);
        var second = model.Confirmations[1];
        model.SelectedAccount = model.Accounts[0];
        await model.RespondAsync(second, true);
        Assert.Equal(second.Owner.Account.Session.SteamID, platform.ConfirmationApi.LastAnsweredSteamId);
        Assert.Single(model.Confirmations);
        platform.ConfirmationApi.RejectSteamId = second.Owner.Account.Session.SteamID;
        await model.RefreshConfirmationsAsync();
        Assert.Single(model.Confirmations);
        var issue = Assert.Single(model.ConfirmationIssues);
        Assert.Equal(second.AccountName, issue.AccountName);
        Assert.True(issue.CanSignIn);
        Assert.False(model.ShowNotice);
        model.Section = MobileSection.Settings;
        Assert.False(model.ShowNotice);
    }

    [AvaloniaFact]
    public async Task ShortBackgroundVisitKeepsUnlockAndFifteenMinutesClearsAllAccountViews()
    {
        using var platform = new TestPlatform();
        var clock = new SessionClock();
        using var model = new MainViewModel(platform, platform.PromptAsync, clock);
        await model.UnlockAsync();
        await ImportTwoAsync(model, platform);
        await model.RefreshConfirmationsAsync();
        var prompts = platform.PinPrompts;
        model.SetActive(false);
        clock.Advance(TimeSpan.FromMinutes(14.9));
        model.SetActive(true);
        Assert.True(model.IsUnlocked);
        Assert.Equal(prompts, platform.PinPrompts);
        model.SetActive(false);
        clock.Advance(TimeSpan.FromMinutes(15));
        model.SetActive(true);
        Assert.False(model.IsUnlocked);
        Assert.Empty(model.Accounts);
        Assert.Empty(model.Confirmations);
        await model.UnlockAsync();
        Assert.True(model.IsUnlocked);
        Assert.Equal(prompts + 1, platform.PinPrompts);
        model.Lock();
        Assert.False(model.IsUnlocked);
    }

    [AvaloniaFact]
    public async Task FullPagePinRetriesAndSubmitsOnTheSixthDigit()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        model.IsUnlocked = true;
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 393, Height = 852, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var task = view.PromptAsync(new PromptRequest("Enter passcode", "", true, IsNumeric: true,
            ValidatePinAsync: pin => Task.FromResult<string?>(pin == "123456" ? null : "Incorrect passcode.")));
        var keys = view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("key")).ToArray();
        foreach (var digit in "111111") keys.Single(button => Equals(button.Content, digit.ToString())).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(task.IsCompleted);
        foreach (var digit in "123456") keys.Single(button => Equals(button.Content, digit.ToString())).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("123456", await task.WaitAsync(TimeSpan.FromSeconds(5)));
        window.Close();
    }

    private static async Task ImportTwoAsync(MainViewModel model, TestPlatform platform)
    {
        var first = AccountVaultTests.SyntheticAccount();
        await model.ImportFilesAsync(new[] { ("first.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(first))) });
        first.AccountName = "second-synthetic-account";
        first.Session.SteamID++;
        platform.Login.SteamId++;
        await model.ImportFilesAsync(new[] { ("second.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(first))) });
        Assert.False(model.IsError, model.Status);
    }

    [AvaloniaFact]
    public async Task DesktopJsonReachesRequiredPasswordAndPersistsThroughEveryImportRoute()
    {
        foreach (var route in new[] { "file", "paste", "legacy-qr", "compressed-qr" })
        {
            using var platform = new TestPlatform();
            using var model = new MainViewModel(platform, platform.PromptAsync);
            await model.UnlockAsync();
            var json = AccountVaultTests.DesktopFixture();
            if (route == "file") await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(json)) });
            else if (route == "paste") { platform.Clipboard = json; await model.PasteQrAsync(); }
            else
            {
                platform.QrPayload = route == "legacy-qr" ? json : AccountTransfer.CreateQrPayload(AccountTransfer.Read(json));
                await model.ScanAsync();
            }
            Assert.False(model.IsError, route + ": " + model.Status);
            Assert.Equal(1, platform.Login.PasswordSubmissions);
            Assert.Equal("Test account", platform.Login.LastUsername);
            Assert.Equal(5, Assert.Single(model.Accounts).Code.Length);
            model.Lock();
            await model.UnlockAsync();
            Assert.False(model.IsError, route + ": " + model.Status);
            Assert.Equal(1, Assert.Single(model.Accounts).Account.Status);
        }
    }

    [AvaloniaFact]
    public async Task AccountIsNotVisibleOrPersistedUntilSteamLoginFinishes()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        platform.Login.PendingSession = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var import = model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AccountVaultTests.SyntheticAccount()))) });
        await platform.Login.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Accounts);
        Assert.False(File.Exists(Path.Combine(platform.AccountDirectory, "manifest.json")));
        platform.Login.PendingSession.SetResult(new SteamAuth.SessionData { SteamID = platform.Login.SteamId, AccessToken = "synthetic-complete-access" });
        await import;
        Assert.False(model.IsError, model.Status);
        Assert.Single(model.Accounts);
        Assert.True(File.Exists(Path.Combine(platform.AccountDirectory, "manifest.json")));
    }

    [AvaloniaFact]
    public async Task ImportRequiresSteamPasswordAndUsesTheImportedUsernameAndGuardSecret()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        var account = AccountVaultTests.SyntheticAccount();
        await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(account))) });
        Assert.False(model.IsError, model.Status);
        Assert.Equal(1, platform.Login.PasswordSubmissions);
        Assert.Equal(account.AccountName, platform.Login.LastUsername);
        Assert.Equal(5, platform.Login.SubmittedGuardCode?.Length);
        Assert.Equal("synthetic-new-access", Assert.Single(model.Accounts).Account.Session.AccessToken);
    }

    [AvaloniaFact]
    public async Task FailedOrCancelledSteamLoginDoesNotCommitTheImport()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        var files = new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AccountVaultTests.SyntheticAccount()))) };
        platform.Answers.Enqueue(null);
        await model.ImportFilesAsync(files);
        Assert.Empty(model.Accounts);
        Assert.Equal(0, platform.Login.PasswordSubmissions);
        Assert.False(File.Exists(Path.Combine(platform.AccountDirectory, "manifest.json")));
        platform.Login.Reject = true;
        await model.ImportFilesAsync(files);
        Assert.True(model.IsError);
        Assert.Empty(model.Accounts);
        Assert.False(File.Exists(Path.Combine(platform.AccountDirectory, "manifest.json")));
    }

    [AvaloniaFact]
    public async Task ImportRejectsASteamLoginForADifferentSteamId()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        platform.Login.SteamId++;
        await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AccountVaultTests.SyntheticAccount()))) });
        Assert.True(model.IsError);
        Assert.Contains("different account", model.Status);
        Assert.Empty(model.Accounts);
        Assert.Null(platform.Login.SubmittedGuardCode);
    }

    [AvaloniaFact]
    public async Task MissingLegacySteamIdIsFilledByRequiredSteamLogin()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        var account = AccountVaultTests.SyntheticAccount();
        account.Session = null!;
        await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(JsonConvert.SerializeObject(account))).ToArray()) });
        Assert.False(model.IsError, model.Status);
        Assert.Equal(platform.Login.SteamId, Assert.Single(model.Accounts).Account.Session.SteamID);
    }

    [AvaloniaFact]
    public async Task QrImportSignsInAndCancelledScanClearsThePreviousFileError()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes("not JSON")) });
        Assert.True(model.IsError);
        await model.ScanAsync();
        Assert.False(model.IsError);
        Assert.Contains("QR scan cancelled", model.Status);
        platform.QrPayload = AccountTransfer.CreateQrPayload(AccountVaultTests.SyntheticAccount());
        await model.ScanAsync();
        Assert.False(model.IsError, model.Status);
        Assert.Equal(1, platform.Login.PasswordSubmissions);
        Assert.Single(model.Accounts);
    }

    [AvaloniaFact]
    public async Task UpgradingAnExistingVaultPreservesAccountsAndRemovesTheUnwrappedKey()
    {
        using var platform = new TestPlatform();
        var legacyKey = platform.GetVaultKey(false);
        new AccountVault(platform.AccountDirectory, legacyKey).Save(AccountVaultTests.SyntheticAccount());
        var manifestBefore = File.ReadAllBytes(Path.Combine(platform.AccountDirectory, "manifest.json"));
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.InitializeAsync();
        Assert.True(model.IsUnlocked, model.Status);
        Assert.Single(model.Accounts);
        Assert.Null(platform.ReadSecurityValue("vault-key-v1"));
        Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(platform.AccountDirectory, "manifest.json")));
        model.Lock();
        await model.UnlockAsync();
        Assert.Equal(AccountVaultTests.SyntheticAccount().SharedSecret, Assert.Single(model.Accounts).Account.SharedSecret);
    }

    [AvaloniaFact]
    public async Task FailedPasscodeStorageKeepsTheLegacyVaultRecoverable()
    {
        using var platform = new TestPlatform();
        var legacyKey = platform.GetVaultKey(false);
        new AccountVault(platform.AccountDirectory, legacyKey).Save(AccountVaultTests.SyntheticAccount());
        platform.FailPasscodeWrite = true;
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.InitializeAsync();
        Assert.False(model.IsUnlocked);
        Assert.True(model.IsError);
        Assert.Null(platform.ReadSecurityValue(VaultPasscode.StateName));
        Assert.Equal(legacyKey, platform.ReadSecurityValue("vault-key-v1"));
        Assert.Single(new AccountVault(platform.AccountDirectory, legacyKey).Load());
        platform.FailPasscodeWrite = false;
        await model.UnlockAsync();
        Assert.True(model.IsUnlocked, model.Status);
        Assert.Single(model.Accounts);
    }

    [AvaloniaFact]
    public async Task FileImportDisplaysACodeAndSurvivesLockAndUnlock()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.InitializeAsync();
        Assert.True(model.IsUnlocked, model.Status);
        var account = AccountVaultTests.SyntheticAccount();
        var view = new MainView { DataContext = model, Width = 393, Height = 852 };
        var window = new Window { Content = view, Width = 393, Height = 852 };
        window.Show();
        await model.ImportAsync(() => Task.FromResult<IReadOnlyList<ImportFile>>(new[]
        {
            new ImportFile("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(account)))
        }));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.False(model.IsError, model.Status);
        Assert.Equal(5, Assert.Single(model.Accounts).Code.Length);
        Assert.Contains("Imported and signed in to 1", model.Status);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == account.AccountName);
        Assert.Null(platform.ReadSecurityValue("vault-key-v1"));
        model.Lock();
        Assert.Empty(model.Accounts);
        platform.Answers.Enqueue("000000");
        await model.UnlockAsync();
        Assert.False(model.IsUnlocked);
        Assert.Empty(model.Accounts);
        await model.UnlockAsync();
        Assert.True(model.IsUnlocked, model.Status);
        Assert.Equal(account.SharedSecret, Assert.Single(model.Accounts).Account.SharedSecret);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PastedClipboardDataImportsAndInvalidJsonShowsAnError()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        platform.Clipboard = JsonConvert.SerializeObject(AccountVaultTests.SyntheticAccount());
        model.Section = MobileSection.Settings;
        await model.PasteQrAsync();
        Assert.False(model.IsError, model.Status);
        Assert.Equal(MobileSection.Accounts, model.Section);
        Assert.Single(model.Accounts);
        platform.Clipboard = "{invalid synthetic json";
        await model.PasteQrAsync();
        Assert.True(model.IsError);
        Assert.DoesNotContain("invalid synthetic json", model.Status);
        Assert.Single(model.Accounts);
    }

    [AvaloniaFact]
    public async Task EncryptedImportUsesTheSourceManifestAndPassword()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        await model.UnlockAsync();
        var sourceDirectory = Path.Combine(platform.AccountDirectory, "source");
        new AccountVault(sourceDirectory, "synthetic source password").Save(AccountVaultTests.SyntheticAccount());
        var files = Directory.GetFiles(sourceDirectory).Select(path => (Path.GetFileName(path), File.ReadAllBytes(path))).ToArray();
        platform.Answers.Enqueue("synthetic source password");
        await model.ImportFilesAsync(files);
        Assert.False(model.IsError, model.Status);
        Assert.Single(model.Accounts);
        model.Lock();
        await model.UnlockAsync();
        Assert.Equal(5, Assert.Single(model.Accounts).Code.Length);
    }

    [AvaloniaFact]
    public async Task CancelledSetupAndCancelledPickerDoNotChangeAccounts()
    {
        using var platform = new TestPlatform();
        using var model = new MainViewModel(platform, platform.PromptAsync);
        platform.Answers.Enqueue(null);
        await model.UnlockAsync();
        Assert.False(model.IsUnlocked);
        Assert.Null(platform.ReadSecurityValue(VaultPasscode.StateName));
        await model.UnlockAsync();
        await model.ImportAsync(() => Task.FromResult<IReadOnlyList<ImportFile>>(Array.Empty<ImportFile>()));
        Assert.False(model.IsBusy);
        Assert.False(model.IsError);
        Assert.Empty(model.Accounts);
        model.Lock();
        await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AccountVaultTests.SyntheticAccount()))) });
        Assert.True(model.IsError);
        Assert.Empty(model.Accounts);
    }

    private sealed class TestPlatform : IMobilePlatform, IDisposable
    {
        private readonly VaultPasscodeTests.MemoryStore _store = new();
        public string AccountDirectory { get; } = Path.Combine(Path.GetTempPath(), "sda-import-tests", Guid.NewGuid().ToString("N"));
        public Queue<string?> Answers { get; } = new();
        public string? Clipboard { get; set; }
        public string? QrPayload { get; set; }
        public bool FailPasscodeWrite { get; set; }
        public FakeLogin Login { get; } = new();
        public FakeConfirmations ConfirmationApi { get; } = new();
        public int PinPrompts { get; private set; }
        public List<PromptRequest> PinRequests { get; } = new();
        public string? LastCopiedCode { get; private set; }
        public string? ReadSecurityValue(string name) => _store.ReadSecurityValue(name);
        public void WriteSecurityValue(string name, string value)
        {
            if (FailPasscodeWrite && name == VaultPasscode.StateName) throw new IOException("Synthetic storage failure.");
            _store.WriteSecurityValue(name, value);
        }
        public void RemoveSecurityValue(string name) => _store.RemoveSecurityValue(name);
        public string GetVaultKey(bool vaultExists)
        {
            if (vaultExists && ReadSecurityValue("vault-key-v1") == null) throw new InvalidOperationException("Missing synthetic key.");
            var key = ReadSecurityValue("vault-key-v1") ?? "synthetic temporary vault key";
            WriteSecurityValue("vault-key-v1", key);
            return key;
        }
        public Task<string?> PromptAsync(PromptRequest request)
        {
            if (request.IsNumeric) { PinPrompts++; PinRequests.Add(request); }
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : request.IsMultiline ? Clipboard : "123456");
        }
        public ISteamLoginClient CreateLoginClient(HttpClient http) => Login;
        public ISteamConfirmationClient CreateConfirmationClient(HttpClient http) => ConfirmationApi;
        public Task<string?> ScanQrAsync() => Task.FromResult(QrPayload);
        public void CopyCode(string code) => LastCopiedCode = code;
        public void Dispose() { if (Directory.Exists(AccountDirectory)) Directory.Delete(AccountDirectory, true); }
    }

    private sealed class SessionClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }

    private sealed class FakeConfirmations : ISteamConfirmationClient
    {
        private readonly HashSet<ulong> _answered = new();
        public bool MutateSession { get; set; }
        public TaskCompletionSource<IReadOnlyList<SteamAuth.Confirmation>>? PendingFetch { get; set; }
        public ulong? RejectSteamId { get; set; }
        public ulong? LastAnsweredSteamId { get; private set; }
        public Task<long> GetTimeAsync(CancellationToken token) => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public Task<bool> RefreshSessionAsync(SteamAuth.SessionData session, long time, CancellationToken token)
        { if (MutateSession) session.AccessToken = "synthetic-badge-access"; return Task.FromResult(MutateSession); }
        public Task<IReadOnlyList<SteamAuth.Confirmation>> FetchAsync(SteamAuth.SteamGuardAccount account, long time, CancellationToken token)
        {
            if (account.Session.SteamID == RejectSteamId) throw new SteamSignInRequiredException("Sign in again.");
            if (PendingFetch != null) return PendingFetch.Task.WaitAsync(token);
            return Task.FromResult<IReadOnlyList<SteamAuth.Confirmation>>(_answered.Contains(account.Session.SteamID) ? Array.Empty<SteamAuth.Confirmation>() : new[] { new SteamAuth.Confirmation
            { ID = 1, Key = ulong.MaxValue, Headline = "Synthetic trade", Summary = new(), ConfType = SteamAuth.Confirmation.EMobileConfirmationType.Trade } });
        }
        public Task AnswerAsync(SteamAuth.SteamGuardAccount account, SteamAuth.Confirmation confirmation, bool allow, long time, CancellationToken token)
        { LastAnsweredSteamId = account.Session.SteamID; _answered.Add(account.Session.SteamID); return Task.CompletedTask; }
    }

    private sealed class FakeLogin : ISteamLoginClient
    {
        public ulong SteamId { get; set; } = 76561198000000000;
        public bool Reject { get; set; }
        public int PasswordSubmissions { get; private set; }
        public string? LastUsername { get; private set; }
        public string? SubmittedGuardCode { get; private set; }
        public TaskCompletionSource<SteamAuth.SessionData>? PendingSession { get; set; }
        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GuardChallenge?> BeginAsync(string username, string password, CancellationToken cancellationToken)
        {
            PasswordSubmissions++;
            LastUsername = username;
            if (Reject) throw new InvalidOperationException("Synthetic Steam sign-in rejected.");
            return Task.FromResult<GuardChallenge?>(new GuardChallenge(3, ""));
        }
        public Task SubmitCodeAsync(string code, int type, CancellationToken cancellationToken)
        {
            SubmittedGuardCode = code;
            return Task.CompletedTask;
        }
        public Task<SteamAuth.SessionData> PollAsync(CancellationToken cancellationToken)
        {
            PollStarted.TrySetResult();
            return PendingSession?.Task ?? Task.FromResult(new SteamAuth.SessionData { SteamID = SteamId, AccessToken = "synthetic-new-access", RefreshToken = "synthetic-new-refresh" });
        }
    }
}
