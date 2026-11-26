using Sda.Mobile.Core;

namespace Sda.Mobile;

public interface IMobilePlatform : IVaultSecurityStore
{
    string AccountDirectory { get; }
    string GetVaultKey(bool vaultExists);
    Task<string?> ScanQrAsync();
    void CopyCode(string code);
    ISteamLoginClient CreateLoginClient(HttpClient http) => new SteamLoginClient(http);
    ISteamConfirmationClient CreateConfirmationClient(HttpClient http) => new SteamConfirmationClient(http);
    Task<IReadOnlyList<ImportFile>?> PickImportFilesAsync() => Task.FromResult<IReadOnlyList<ImportFile>?>(null);
    Task<string?> PromptAsync(PromptRequest request, Func<PromptRequest, Task<string?>> fallback) => fallback(request);
    void CancelPrompt() { }
}

public sealed record ImportFile(string Name, byte[] Bytes);
