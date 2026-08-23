using SteamAuth;
using SteamDesktopAuthenticator.Services;
using SteamKit2.Authentication;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>
    /// Ported from the original UserFormAuthenticator. Supplies Steam Guard codes during the
    /// SteamKit2 login flow: if we already hold a linked SteamGuardAccount (session refresh),
    /// generate the code locally; otherwise (adding a brand-new authenticator, or before SDA
    /// is the authenticator yet) prompt for an email code.
    /// </summary>
    public class AvaloniaAuthenticator : IAuthenticator
    {
        private readonly SteamGuardAccount? _account;
        private readonly IDialogService _dialogService;
        private int _deviceCodesGenerated = 0;

        public AvaloniaAuthenticator(SteamGuardAccount? account, IDialogService dialogService)
        {
            _account = account;
            _dialogService = dialogService;
        }

        public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);

        // Return type matches IAuthenticator's Task<string> exactly (not Task<string?>) even
        // though we can return null on failure - the interface itself doesn't declare
        // nullability, so this avoids a CS8613 mismatch warning without changing behavior.
        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            if (previousCodeWasIncorrect)
            {
                if (_deviceCodesGenerated > 2)
                {
                    await _dialogService.ShowMessageAsync("There seems to be an issue logging into your account with these two-factor codes. Is SDA still set as your authenticator?");
                }
                await Task.Delay(30000);
            }

            if (_account == null)
            {
                await _dialogService.ShowMessageAsync("This account already has an authenticator linked. You must remove that authenticator to add SDA as your authenticator.");
                return null!;
            }

            var code = await _account.GenerateSteamGuardCodeAsync();
            _deviceCodesGenerated++;
            return code;
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            string message = previousCodeWasIncorrect
                ? $"The code you provided was invalid. Enter the code sent to {email}:"
                : $"Enter the code sent to {email}:";
            return (await _dialogService.PromptTextAsync("Steam Guard", message))!;
        }
    }
}
