using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamAuth;
using SteamDesktopAuthenticator.Core;

namespace Sda.Mobile.Core;

public sealed class VaultManifest
{
    [JsonProperty("encrypted")] public bool Encrypted { get; set; } = true;
    [JsonProperty("first_run")] public bool FirstRun { get; set; }
    [JsonProperty("entries")] public List<VaultEntry> Entries { get; set; } = new();
    [JsonExtensionData] public IDictionary<string, JToken>? OtherSettings { get; set; }
}

public sealed class VaultEntry
{
    [JsonProperty("filename")] public string Filename { get; set; } = "";
    [JsonProperty("steamid")] public ulong SteamID { get; set; }
    [JsonProperty("encryption_iv")] public string? IV { get; set; }
    [JsonProperty("encryption_salt")] public string? Salt { get; set; }
}

/// <summary>
/// Uses SDA's existing manifest and encryption formats. A new account-file generation is
/// written before the manifest is atomically replaced, so an interrupted save cannot pair
/// an old salt/IV with new ciphertext. Callers serialize access through the UI operation gate.
/// </summary>
public sealed class AccountVault
{
    private readonly string _directory;
    private readonly string _passkey;
    private VaultManifest _manifest;

    public AccountVault(string directory, string passkey)
    {
        if (string.IsNullOrEmpty(passkey)) throw new ArgumentException("A vault key is required.", nameof(passkey));
        _directory = Path.GetFullPath(directory);
        _passkey = passkey;
        Directory.CreateDirectory(_directory);
        var manifestPath = Path.Combine(_directory, "manifest.json");
        _manifest = File.Exists(manifestPath)
            ? JsonConvert.DeserializeObject<VaultManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The account manifest is damaged.")
            : new VaultManifest();
        if (!_manifest.Encrypted && _manifest.Entries.Count != 0)
            throw new InvalidDataException("The local vault is unexpectedly unencrypted. Import these files into a new vault.");
        var ids = new HashSet<ulong>();
        foreach (var entry in _manifest.Entries)
        {
            GetAccountPath(entry.Filename);
            if (entry.SteamID == 0 || !ids.Add(entry.SteamID))
                throw new InvalidDataException("The account manifest contains invalid or duplicate Steam IDs.");
        }
    }

    public IReadOnlyList<SteamGuardAccount> Load()
    {
        // Never silently discard a damaged or missing entry and overwrite the manifest.
        return _manifest.Entries.Select(entry =>
        {
            var account = AccountTransfer.Read(File.ReadAllText(GetAccountPath(entry.Filename)), entry, _passkey);
            if (account.Session.SteamID != entry.SteamID)
                throw new InvalidDataException("An account does not match its manifest entry.");
            return account;
        }).ToArray();
    }

    public void Save(SteamGuardAccount account)
    {
        AccountTransfer.Validate(account);
        var oldEntry = _manifest.Entries.FirstOrDefault(entry => entry.SteamID == account.Session.SteamID);
        var entry = new VaultEntry
        {
            SteamID = account.Session.SteamID,
            Filename = $"{account.Session.SteamID}-{Guid.NewGuid():N}.maFile",
            Salt = FileEncryptor.GetRandomSalt(),
            IV = FileEncryptor.GetInitializationVector()
        };
        var ciphertext = FileEncryptor.EncryptData(_passkey, entry.Salt, entry.IV, AccountJson.Write(account));
        File.WriteAllText(GetAccountPath(entry.Filename), ciphertext);
        var next = CloneManifest();
        var position = next.Entries.FindIndex(item => item.SteamID == entry.SteamID);
        if (position < 0) next.Entries.Add(entry);
        else next.Entries[position] = entry;
        Commit(next);
        if (oldEntry != null) TryDelete(oldEntry.Filename);
    }

    public void Remove(ulong steamId)
    {
        var entry = _manifest.Entries.FirstOrDefault(item => item.SteamID == steamId);
        if (entry == null) return;
        var next = CloneManifest();
        next.Entries.RemoveAll(item => item.SteamID == steamId);
        Commit(next);
        TryDelete(entry.Filename);
    }

    public static VaultManifest ReadImportManifest(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > AccountTransfer.MaximumImportBytes)
            throw new InvalidDataException("The manifest is too large.");
        var manifest = JsonConvert.DeserializeObject<VaultManifest>(json)
            ?? throw new InvalidDataException("The manifest is empty.");
        if (manifest.Entries == null) throw new InvalidDataException("The manifest has no entries.");
        return manifest;
    }

    private VaultManifest CloneManifest() => JsonConvert.DeserializeObject<VaultManifest>(JsonConvert.SerializeObject(_manifest))!;

    private void Commit(VaultManifest next)
    {
        var temp = Path.Combine(_directory, "manifest.json.tmp");
        File.WriteAllText(temp, JsonConvert.SerializeObject(next, Formatting.Indented));
        File.Move(temp, Path.Combine(_directory, "manifest.json"), true);
        _manifest = next;
    }

    private string GetAccountPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename is "." or ".." ||
            filename.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || Path.GetFileName(filename) != filename ||
            !filename.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The manifest contains an unsafe account filename.");
        return Path.Combine(_directory, filename);
    }

    private void TryDelete(string filename)
    {
        try { File.Delete(GetAccountPath(filename)); }
        catch (IOException) { /* An unreferenced encrypted generation can be cleaned up later. */ }
        catch (UnauthorizedAccessException) { }
    }
}
