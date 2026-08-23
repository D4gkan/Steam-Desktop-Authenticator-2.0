using SteamDesktopAuthenticator.Core;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Services
{
    public interface IDialogService : IPasskeyPrompter
    {
        Task<string?> PromptTextAsync(string title, string label, string? initialValue = null, bool isPassword = false);
        Task<bool> ConfirmAsync(string title, string message);

        /// <summary>Three-way choice (e.g. Yes / No / Cancel). Returns true for the "yes" choice,
        /// false for the "no" choice, and null if the dialog was cancelled/dismissed.</summary>
        Task<bool?> ConfirmThreeWayAsync(string title, string message, string yesLabel = "Yes", string noLabel = "No", string cancelLabel = "Cancel");

        void CopyToClipboard(string text);
    }
}
