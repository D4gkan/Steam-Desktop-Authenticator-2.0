using Avalonia.Controls;
using Avalonia.Interactivity;
using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Core.Security;
using SteamDesktopAuthenticator.Services;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Views
{
    public enum LoginType
    {
        Initial,  // No accounts yet / adding a brand new authenticator
        Refresh,  // Existing linked account needs a fresh session
        Import    // Logging in solely to refresh a session on an imported .maFile
    }

    public partial class LoginWindow : Window
    {
        private readonly LoginType _loginReason;
        private readonly SteamGuardAccount? _account;
        private readonly IDialogService _dialogService;

        /// <summary>Session obtained after a successful credential login. Always populated on
        /// success, regardless of LoginType - same as the original's public "Session" field.</summary>
        public SessionData? Session { get; private set; }

        /// <summary>Only populated for LoginType.Initial after the full authenticator-linking
        /// flow finishes successfully.</summary>
        public SteamGuardAccount? LinkedAccount { get; private set; }

        /// <summary>Task 1 UI: whether "Save password for automatic re-login" was checked at the
        /// moment login succeeded. Callers (MainWindow's Refresh Login flow, the Initial/Import
        /// flows below) use this together with <see cref="EnteredPassword"/> to decide whether to
        /// write the password into the OS-native secure credential store.</summary>
        public bool SavePasswordRequested { get; private set; }

        /// <summary>The password that was entered, only if login succeeded AND the save-password
        /// checkbox was checked - null otherwise. Cleared as soon as a caller has read it once
        /// (see <see cref="ConsumeEnteredPassword"/>) so it does not linger in memory longer than
        /// necessary.</summary>
        public string? EnteredPassword { get; private set; }

        /// <summary>Reads and immediately clears <see cref="EnteredPassword"/>.</summary>
        public string? ConsumeEnteredPassword()
        {
            var p = EnteredPassword;
            EnteredPassword = null;
            return p;
        }

        /// <summary>Design-time/XAML-loader constructor only. Not used at runtime - the app always
        /// constructs this window via the (IDialogService, LoginType, SteamGuardAccount?) overload below.</summary>
        public LoginWindow() : this(null!)
        {
        }

        public LoginWindow(IDialogService dialogService, LoginType loginReason = LoginType.Initial, SteamGuardAccount? account = null, bool initialSavePasswordChecked = false)
        {
            InitializeComponent();
            _dialogService = dialogService;
            _loginReason = loginReason;
            _account = account;

            if (loginReason != LoginType.Initial && account != null)
            {
                UsernameBox.Text = account.AccountName;
                UsernameBox.IsEnabled = false;
            }

            SavePasswordCheckBox.IsChecked = initialSavePasswordChecked;

            ExplanationText.Text = loginReason switch
            {
                LoginType.Refresh => "Your Steam credentials have expired. For trade and market confirmations to work properly, please login again.",
                LoginType.Import => "Please login to your Steam account to import it.",
                _ => "Login to link Steam Desktop Authenticator as your Steam Guard authenticator."
            };
        }

        private void SetBusy(bool busy)
        {
            LoginButton.IsEnabled = !busy;
            LoginButton.Content = busy ? "Logging in…" : "Login";
        }

        private void ShowError(string message)
        {
            StatusText.Text = message;
            StatusText.IsVisible = true;
        }

        private async void OnLoginClick(object? sender, RoutedEventArgs e)
        {
            SetBusy(true);
            StatusText.IsVisible = false;

            string username = UsernameBox.Text ?? "";
            string password = PasswordBox.Text ?? "";

            var steamClient = new SteamClient();
            steamClient.Connect();

            int waited = 0;
            while (!steamClient.IsConnected && waited < 15000)
            {
                await Task.Delay(500);
                waited += 500;
            }
            if (!steamClient.IsConnected)
            {
                ShowError("Could not connect to Steam. Check your internet connection and try again.");
                SetBusy(false);
                return;
            }

            CredentialsAuthSession authSession;
            try
            {
                authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new AvaloniaAuthenticator(_account, _dialogService),
                });
            }
            catch (Exception ex)
            {
                ShowError("Steam login failed: " + ex.Message);
                SetBusy(false);
                return;
            }

            AuthPollResult pollResponse;
            try
            {
                pollResponse = await authSession.PollingWaitForResultAsync();
            }
            catch (Exception ex)
            {
                ShowError("Steam login failed: " + ex.Message);
                SetBusy(false);
                return;
            }

            var sessionData = new SessionData
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = pollResponse.AccessToken,
                RefreshToken = pollResponse.RefreshToken,
            };
            Session = sessionData;

            // Task 1: capture the save-password checkbox state now, while we still have the
            // plaintext password in hand. Nothing is written to the credential store here -
            // that decision (and the matching ui-meta.json flag) belongs to whichever caller
            // owns the account's UiMetaStore entry, so it stays a single source of truth.
            SavePasswordRequested = SavePasswordCheckBox.IsChecked == true;
            EnteredPassword = SavePasswordRequested ? password : null;

            if (_loginReason == LoginType.Import)
            {
                Close(true);
                return;
            }

            if (_loginReason == LoginType.Refresh)
            {
                if (_account != null)
                {
                    _account.FullyEnrolled = true;
                    _account.Session = sessionData;
                    await SaveAfterReloginAsync(_account, isRefreshing: true);
                }
                Close(true);
                return;
            }

            // LoginType.Initial: proceed to link a brand new authenticator.
            bool proceed = await _dialogService.ConfirmAsync(
                "Steam Login",
                "Steam account login succeeded. Continue adding SDA as your authenticator?");
            if (!proceed)
            {
                await _dialogService.ShowMessageAsync("Adding authenticator aborted.");
                SetBusy(false);
                return;
            }

            await RunAuthenticatorLinkFlowAsync(sessionData);
        }

        private async Task RunAuthenticatorLinkFlowAsync(SessionData sessionData)
        {
            var linker = new AuthenticatorLinker(sessionData);
            var linkResponse = AuthenticatorLinker.LinkResult.GeneralFailure;

            while (linkResponse != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            {
                try
                {
                    linkResponse = await linker.AddAuthenticator();
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowMessageAsync("Error adding your authenticator: " + ex.Message);
                    SetBusy(false);
                    return;
                }

                switch (linkResponse)
                {
                    case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
                        var phoneWindow = new PhoneInputWindow();
                        var phoneResult = await phoneWindow.ShowDialog<PhoneInputResult?>(this);
                        if (phoneResult == null || phoneResult.Canceled)
                        {
                            Close(false);
                            return;
                        }
                        linker.PhoneNumber = phoneResult.PhoneNumber;
                        linker.PhoneCountryCode = phoneResult.CountryCode;
                        break;

                    case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                        await _dialogService.ShowMessageAsync("This account already has an authenticator linked. You must remove that authenticator to add SDA as your authenticator.");
                        Close(false);
                        return;

                    case AuthenticatorLinker.LinkResult.FailureAddingPhone:
                        await _dialogService.ShowMessageAsync("Failed to add your phone number. Please try again or use a different phone number.");
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustRemovePhoneNumber:
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                        await _dialogService.ShowMessageAsync("Please check your email, and click the link Steam sent you before continuing.");
                        break;

                    case AuthenticatorLinker.LinkResult.GeneralFailure:
                        await _dialogService.ShowMessageAsync("Error adding your authenticator.");
                        Close(false);
                        return;
                }
            }

            var manifest = Manifest.GetManifest();
            string? passKey = null;
            if (manifest.Entries.Count == 0)
            {
                passKey = await manifest.PromptSetupPassKeyAsync("Please enter an encryption passkey. Leave blank or cancel to not encrypt (VERY INSECURE).");
            }
            else if (manifest.Entries.Count > 0 && manifest.Encrypted)
            {
                passKey = await manifest.PromptForPassKeyAsync();
                if (passKey == null)
                {
                    Close(false);
                    return;
                }
            }

            if (!manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey))
            {
                manifest.RemoveAccount(linker.LinkedAccount);
                await _dialogService.ShowMessageAsync("Unable to save mobile authenticator file. The mobile authenticator has not been linked.");
                Close(false);
                return;
            }

            await _dialogService.ShowMessageAsync("The mobile authenticator has not yet been linked. Before finalizing, please write down your revocation code: " + linker.LinkedAccount.RevocationCode);

            var finalizeResponse = AuthenticatorLinker.FinalizeResult.GeneralFailure;
            while (finalizeResponse != AuthenticatorLinker.FinalizeResult.Success)
            {
                var smsCode = await _dialogService.PromptTextAsync("Steam Login", "Please input the code Steam sent you (this may arrive by SMS or email, depending on your account).");
                if (smsCode == null)
                {
                    manifest.RemoveAccount(linker.LinkedAccount);
                    Close(false);
                    return;
                }

                var revocationConfirm = await _dialogService.PromptTextAsync("Steam Login", "Please enter your revocation code to confirm you've saved it.");
                if (revocationConfirm == null || revocationConfirm.Trim().ToUpperInvariant() != linker.LinkedAccount.RevocationCode)
                {
                    await _dialogService.ShowMessageAsync("Revocation code incorrect; the authenticator has not been linked.");
                    manifest.RemoveAccount(linker.LinkedAccount);
                    Close(false);
                    return;
                }

                finalizeResponse = await linker.FinalizeAddAuthenticator(smsCode);

                switch (finalizeResponse)
                {
                    case AuthenticatorLinker.FinalizeResult.BadSMSCode:
                        continue;

                    case AuthenticatorLinker.FinalizeResult.UnableToGenerateCorrectCodes:
                        await _dialogService.ShowMessageAsync("Unable to generate the proper codes to finalize this authenticator. It should not be linked. In case it was, here is your revocation code one last time: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        Close(false);
                        return;

                    case AuthenticatorLinker.FinalizeResult.GeneralFailure:
                        await _dialogService.ShowMessageAsync("Unable to finalize this authenticator. It should not be linked. In case it was, here is your revocation code one last time: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        Close(false);
                        return;
                }
            }

            manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey);
            await _dialogService.ShowMessageAsync("Mobile authenticator successfully linked. Please write down your revocation code: " + linker.LinkedAccount.RevocationCode);
            LinkedAccount = linker.LinkedAccount;
            Close(true);
        }

        private async Task SaveAfterReloginAsync(SteamGuardAccount account, bool isRefreshing)
        {
            var manifest = Manifest.GetManifest();
            string? passKey = null;
            if (manifest.Entries.Count == 0)
            {
                passKey = await manifest.PromptSetupPassKeyAsync("Please enter an encryption passkey. Leave blank or cancel to not encrypt (VERY INSECURE).");
            }
            else if (manifest.Entries.Count > 0 && manifest.Encrypted)
            {
                passKey = await manifest.PromptForPassKeyAsync();
                if (passKey == null) return;
            }

            manifest.SaveAccount(account, passKey != null, passKey);
            if (isRefreshing)
            {
                await _dialogService.ShowMessageAsync("Your session was refreshed.");
            }
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    }
}
