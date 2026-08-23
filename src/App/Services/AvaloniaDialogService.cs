using Avalonia.Controls;
using SteamDesktopAuthenticator.Views;
using System;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Services
{
    public class AvaloniaDialogService : IDialogService
    {
        private readonly Func<Window> _ownerProvider;

        public AvaloniaDialogService(Func<Window> ownerProvider)
        {
            _ownerProvider = ownerProvider;
        }

        private Window Owner => _ownerProvider();

        public async Task<string?> PromptForPasskeyAsync(string message)
        {
            var dlg = PromptDialog.Create(PromptMode.PasswordInput, "Encryption Passkey", message);
            return await dlg.ShowDialog<string?>(Owner);
        }

        public async Task<string?> PromptForNewPasskeyAsync(string message)
        {
            var dlg = PromptDialog.Create(PromptMode.PasswordInput, "Set Passkey", message);
            return await dlg.ShowDialog<string?>(Owner);
        }

        public async Task ShowMessageAsync(string message)
        {
            var dlg = PromptDialog.Create(PromptMode.Message, "Steam Desktop Authenticator", message);
            await dlg.ShowDialog<bool>(Owner);
        }

        public Task ShowWarningAsync(string message) => ShowMessageAsync(message);

        public async Task<string?> PromptTextAsync(string title, string label, string? initialValue = null, bool isPassword = false)
        {
            var dlg = PromptDialog.Create(isPassword ? PromptMode.PasswordInput : PromptMode.TextInput, title, label, initialValue);
            return await dlg.ShowDialog<string?>(Owner);
        }

        public async Task<bool> ConfirmAsync(string title, string message)
        {
            var dlg = PromptDialog.Create(PromptMode.Confirm, title, message);
            var result = await dlg.ShowDialog<bool?>(Owner);
            return result == true;
        }

        public async Task<bool?> ConfirmThreeWayAsync(string title, string message, string yesLabel = "Yes", string noLabel = "No", string cancelLabel = "Cancel")
        {
            var dlg = PromptDialog.Create(PromptMode.ThreeChoice, title, message, null, yesLabel, noLabel, cancelLabel);
            return await dlg.ShowDialog<bool?>(Owner);
        }

        public void CopyToClipboard(string text)
        {
            var clipboard = Owner.Clipboard;
            clipboard?.SetTextAsync(text);
        }
    }
}
