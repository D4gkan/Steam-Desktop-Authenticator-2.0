using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>Per-account UI-only metadata: none of this is read by original SDA, and original
    /// SDA ignores this file entirely, so switching between this build and stock SDA on the same
    /// maFiles folder is safe in both directions.</summary>
    public class AccountMeta
    {
        [JsonProperty("steamid")]
        public ulong SteamID { get; set; }

        [JsonProperty("display_name")]
        public string? DisplayName { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Whether this account's Steam password is saved in the OS-native secure
        /// credential store for automatic re-login when the session expires. The password
        /// itself is never stored here - this is only a non-sensitive on/off flag; the actual
        /// secret lives in Windows Credential Manager / macOS Keychain / the Linux Secret
        /// Service, keyed by this account's Steam ID. Defaults to false (opt-in, per Task 1).</summary>
        [JsonProperty("save_login_enabled")]
        public bool SaveLoginEnabled { get; set; } = false;
    }

    public class UiMetaStore
    {
        [JsonProperty("accounts")]
        public List<AccountMeta> Accounts { get; set; } = new();

        public static string GetPath() => Manifest.GetExecutableDir() + "/maFiles/ui-meta.json";

        public static UiMetaStore Load()
        {
            string path = GetPath();
            if (!File.Exists(path)) return new UiMetaStore();
            try
            {
                var contents = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<UiMetaStore>(contents) ?? new UiMetaStore();
            }
            catch
            {
                return new UiMetaStore();
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(GetPath());
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(GetPath(), JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch
            {
                // Non-critical: UI metadata loss just means names/order reset, no auth data at risk.
            }
        }

        public AccountMeta GetOrCreate(ulong steamId)
        {
            var existing = Accounts.FirstOrDefault(a => a.SteamID == steamId);
            if (existing != null) return existing;

            var created = new AccountMeta
            {
                SteamID = steamId,
                Order = Accounts.Count == 0 ? 0 : Accounts.Max(a => a.Order) + 1,
                Enabled = true
            };
            Accounts.Add(created);
            return created;
        }

        public void Remove(ulong steamId)
        {
            Accounts.RemoveAll(a => a.SteamID == steamId);
        }
    }
}
