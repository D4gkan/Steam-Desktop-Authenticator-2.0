using System;
using System.Collections.Generic;

namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>
    /// Compatibility helper for saved-password lookups. Some earlier SDA builds stored a password
    /// under the SteamID or the account name depending on the release, and some users may have a
    /// password saved under the older key even after upgrading. We therefore treat both variants as
    /// valid for reads and ensure writes/cleans keep both in sync so previously-saved credentials
    /// continue to work after a restart.
    /// </summary>
    public static class CredentialStoreCompat
    {
        public static void SavePassword(ICredentialStore store, ulong steamId, string? accountName, string password)
        {
            foreach (var key in GetCandidateKeys(steamId, accountName))
            {
                store.SavePassword(key, accountName ?? string.Empty, password);
            }
        }

        public static string? TryGetPassword(ICredentialStore store, ulong steamId, string? accountName)
        {
            foreach (var key in GetCandidateKeys(steamId, accountName))
            {
                var password = store.TryGetPassword(key);
                if (!string.IsNullOrEmpty(password))
                {
                    return password;
                }
            }

            return null;
        }

        public static void DeletePassword(ICredentialStore store, ulong steamId, string? accountName)
        {
            foreach (var key in GetCandidateKeys(steamId, accountName))
            {
                store.DeletePassword(key);
            }
        }

        private static IEnumerable<string> GetCandidateKeys(ulong steamId, string? accountName)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (steamId != 0)
            {
                var steamKey = steamId.ToString();
                if (seen.Add(steamKey))
                {
                    keys.Add(steamKey);
                }
            }

            if (!string.IsNullOrWhiteSpace(accountName))
            {
                var trimmed = accountName.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                {
                    keys.Add(trimmed);
                }
            }

            return keys;
        }
    }
}
