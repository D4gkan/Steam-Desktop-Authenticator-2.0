using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>
    /// In the original SDA, Manifest.cs directly instantiated WinForms dialogs (InputForm,
    /// MessageBox) to prompt for a passkey. That tightly coupled "business logic" to one
    /// specific UI toolkit, which is exactly the kind of coupling that made a UI redesign
    /// risky. This interface extracts that interaction so any UI layer (here, Avalonia) can
    /// supply its own dialogs while Manifest keeps the exact original decision logic.
    /// </summary>
    public interface IPasskeyPrompter
    {
        /// <summary>Prompt for an existing passkey to unlock an encrypted manifest. Returns null if cancelled.</summary>
        Task<string?> PromptForPasskeyAsync(string message);

        /// <summary>Prompt for a new passkey (setup or change). Returns null if cancelled or left blank.</summary>
        Task<string?> PromptForNewPasskeyAsync(string message);

        Task ShowMessageAsync(string message);

        Task ShowWarningAsync(string message);
    }
}
