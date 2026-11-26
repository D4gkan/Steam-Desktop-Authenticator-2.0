using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Sda.Mobile;
using Sda.Mobile.Core;
using SteamAuth;
using Avalonia.VisualTree;

namespace Sda.Mobile.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.Platform = new PreviewPlatform(args.Contains("--demo"), args.Contains("--require-passcode"));
        var snapshotArgument = Array.IndexOf(args, "--snapshot-directory");
        if (snapshotArgument >= 0 && snapshotArgument + 1 < args.Length)
        {
            var outputDirectory = Path.GetFullPath(args[snapshotArgument + 1]);
            App.ViewCreated += view => view.Loaded += async (_, _) =>
            {
                await Task.Delay(350);
                Directory.CreateDirectory(outputDirectory);
                using (var splash = new RenderTargetBitmap(PixelSize.FromSize(view.Bounds.Size, 1)))
                { splash.Render(view); splash.Save(Path.Combine(outputDirectory, "splash.png")); }
                for (var attempt = 0; attempt < 100 && App.ViewModel?.IsUnlocked != true; attempt++) await Task.Delay(50);
                foreach (var section in Enum.GetValues<MobileSection>())
                {
                    if (App.ViewModel == null) break;
                    App.ViewModel.Section = section;
                    if (section != MobileSection.Accounts) App.ViewModel.SelectedAccount = App.ViewModel.Accounts.FirstOrDefault();
                    if (section == MobileSection.Activity) { await App.ViewModel.RefreshConfirmationsAsync(); App.ViewModel.DismissNotice(); }
                    await Task.Delay(100);
                    using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(view.Bounds.Size, 1));
                    bitmap.Render(view);
                    bitmap.Save(Path.Combine(outputDirectory, section.ToString().ToLowerInvariant() + ".png"));
                    if (section == MobileSection.Accounts)
                    {
                        var button = view.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(item => item.Tag is MobileAccount && item.Flyout is Avalonia.Controls.MenuFlyout);
                        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                        var menu = (Avalonia.Controls.MenuFlyout)button.Flyout!;
                        menu.ShowAt(button);
                        await Task.Delay(150);
                        if (menu.Items.OfType<Avalonia.Controls.MenuItem>().First().GetVisualRoot() is Visual root)
                        {
                            using var menuBitmap = new RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size, 1));
                            menuBitmap.Render(root);
                            menuBitmap.Save(Path.Combine(outputDirectory, "account-menu.png"));
                        }
                        menu.Hide();
                    }
                }
                _ = view.PromptAsync(new PromptRequest("Enter passcode", "", true, IsNumeric: true, CanCancel: false));
                await Task.Delay(150);
                using (var bitmap = new RenderTargetBitmap(PixelSize.FromSize(view.Bounds.Size, 1)))
                { bitmap.Render(view); bitmap.Save(Path.Combine(outputDirectory, "passcode.png")); }
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            };
        }
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }

    private sealed class PreviewPlatform : IMobilePlatform
    {
        // A preview intentionally has no persistent key or real account storage. Use only
        // synthetic accounts here; iPhone builds use the native Keychain implementation.
        public string AccountDirectory { get; } = Path.Combine(Path.GetTempPath(), "sda-mobile-preview", Guid.NewGuid().ToString("N"), "maFiles");
        private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        private readonly Dictionary<string, string> _securityValues = new();
        private readonly bool _demo;
        private bool _automaticInitialUnlock;
        public PreviewPlatform(bool demo, bool requirePasscode)
        {
            _demo = demo;
            _automaticInitialUnlock = demo && !requirePasscode;
            if (!demo) return;
            new Core.VaultPasscode(this).Create("123456", _key);
            var vault = new Core.AccountVault(AccountDirectory, _key);
            for (var index = 0; index < 2; index++)
            {
                vault.Save(new SteamAuth.SteamGuardAccount
                {
                    AccountName = index == 0 ? "Main account" : "Trading account", FullyEnrolled = true,
                    SharedSecret = Convert.ToBase64String(Enumerable.Range(index, 20).Select(value => (byte)value).ToArray()),
                    IdentitySecret = Convert.ToBase64String(new byte[20]), DeviceID = "android:00000000-0000-0000-0000-000000000000",
                    Session = new SteamAuth.SessionData { SteamID = 76561198000000000UL + (ulong)index }
                });
            }
        }
        public string GetVaultKey(bool vaultExists) => _key;
        public string? ReadSecurityValue(string name) => _securityValues.GetValueOrDefault(name);
        public void WriteSecurityValue(string name, string value) => _securityValues[name] = value;
        public void RemoveSecurityValue(string name) => _securityValues.Remove(name);
        public Task<string?> PromptAsync(PromptRequest request, Func<PromptRequest, Task<string?>> fallback)
        {
            if (_automaticInitialUnlock && request.IsNumeric) { _automaticInitialUnlock = false; return Task.FromResult<string?>("123456"); }
            return fallback(request);
        }
        public ISteamConfirmationClient CreateConfirmationClient(HttpClient http) => new PreviewConfirmations();
        public Task<string?> ScanQrAsync() => throw new InvalidOperationException("The camera scanner is available on iPhone. Use file import or Paste QR data in the preview.");
        public void CopyCode(string code)
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                _ = desktop.MainWindow?.Clipboard?.SetTextAsync(code);
        }
    }

    private sealed class PreviewConfirmations : ISteamConfirmationClient
    {
        private readonly HashSet<ulong> _answered = new();
        public Task<long> GetTimeAsync(CancellationToken token) => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public Task<bool> RefreshSessionAsync(SessionData session, long time, CancellationToken token) => Task.FromResult(false);
        public Task<IReadOnlyList<Confirmation>> FetchAsync(SteamGuardAccount account, long time, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Confirmation>>(_answered.Contains(account.Session.SteamID) ? Array.Empty<Confirmation>() : new[] { new Confirmation
            {
                ID = 1, Key = 2, ConfType = Confirmation.EMobileConfirmationType.Trade, TypeName = "Trade", Headline = "Trade offer",
                Summary = new List<string> { "Sample trading partner", "1 item to send · 2 items to receive" }
            } });
        public Task AnswerAsync(SteamGuardAccount account, Confirmation confirmation, bool allow, long time, CancellationToken token)
        { _answered.Add(account.Session.SteamID); return Task.CompletedTask; }
    }
}
