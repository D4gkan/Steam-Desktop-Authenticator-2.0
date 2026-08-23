using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>
    /// Ported from the original Steam_Desktop_Authenticator.Manifest. JSON property names,
    /// file paths ("maFiles/manifest.json", "maFiles/{steamid}.maFile"), and every save/load/
    /// encrypt/decrypt decision are preserved exactly so files created by the original SDA
    /// (or older versions of it) continue to load correctly, and vice versa.
    /// </summary>
    public class Manifest
    {
        [JsonProperty("encrypted")]
        public bool Encrypted { get; set; }

        [JsonProperty("first_run")]
        public bool FirstRun { get; set; } = true;

        [JsonProperty("entries")]
        public List<ManifestEntry> Entries { get; set; } = new List<ManifestEntry>();

        [JsonProperty("periodic_checking")]
        public bool PeriodicChecking { get; set; } = false;

        [JsonProperty("periodic_checking_interval")]
        public int PeriodicCheckingInterval { get; set; } = 5;

        [JsonProperty("periodic_checking_checkall")]
        public bool CheckAllAccounts { get; set; } = false;

        [JsonProperty("auto_confirm_market_transactions")]
        public bool AutoConfirmMarketTransactions { get; set; } = false;

        [JsonProperty("auto_confirm_trades")]
        public bool AutoConfirmTrades { get; set; } = false;

        private static Manifest? _manifest;

        /// <summary>
        /// Injected by App startup. Same role as the WinForms dialogs the original Manifest
        /// used to create directly - see IPasskeyPrompter.
        /// </summary>
        public static IPasskeyPrompter? Prompter { get; set; }

        public static string GetExecutableDir()
        {
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static Manifest GetManifest(bool forceLoad = false)
        {
            if (_manifest != null && !forceLoad)
            {
                return _manifest;
            }

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string manifestFile = maDir + "manifest.json";

            if (!Directory.Exists(maDir))
            {
                _manifest = GenerateNewManifest(false);
                return _manifest!;
            }

            if (!File.Exists(manifestFile))
            {
                throw new ManifestParseException();
            }

            try
            {
                string manifestContents = File.ReadAllText(manifestFile);
                _manifest = JsonConvert.DeserializeObject<Manifest>(manifestContents);
                if (_manifest == null) throw new ManifestParseException();

                if (_manifest.Encrypted && _manifest.Entries.Count == 0)
                {
                    _manifest.Encrypted = false;
                    _manifest.Save();
                }

                _manifest.RecomputeExistingEntries();

                return _manifest;
            }
            catch (Exception ex) when (!(ex is ManifestParseException))
            {
                throw new ManifestParseException();
            }
        }

        public static Manifest? GenerateNewManifest(bool scanDir = false)
        {
            Manifest newManifest = new Manifest
            {
                Encrypted = false,
                PeriodicCheckingInterval = 5,
                PeriodicChecking = false,
                AutoConfirmMarketTransactions = false,
                AutoConfirmTrades = false,
                Entries = new List<ManifestEntry>(),
                FirstRun = true
            };

            if (scanDir)
            {
                string maDir = Manifest.GetExecutableDir() + "/maFiles/";
                if (Directory.Exists(maDir))
                {
                    DirectoryInfo dir = new DirectoryInfo(maDir);
                    var files = dir.GetFiles();

                    foreach (var file in files)
                    {
                        if (file.Extension != ".maFile") continue;

                        string contents = File.ReadAllText(file.FullName);
                        try
                        {
                            SteamGuardAccount? account = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);
                            if (account == null) throw new MaFileEncryptedException();
                            ManifestEntry newEntry = new ManifestEntry()
                            {
                                Filename = file.Name,
                                SteamID = account.Session.SteamID
                            };
                            newManifest.Entries.Add(newEntry);
                        }
                        catch (Exception)
                        {
                            throw new MaFileEncryptedException();
                        }
                    }

                    if (newManifest.Entries.Count > 0)
                    {
                        newManifest.Save();
                        // Fire-and-forget is intentional here to preserve the original's synchronous
                        // call shape at the one call site (App startup first-run scan); callers that
                        // need to await should call PromptSetupPassKeyAsync directly instead.
                        newManifest.PromptSetupPassKeyAsync(
                            "This version of SDA has encryption. Please enter a passkey below, or hit cancel to remain unencrypted")
                            .GetAwaiter().GetResult();
                    }
                }
            }

            if (newManifest.Save())
            {
                return newManifest;
            }

            return null;
        }

        public class IncorrectPassKeyException : Exception { }
        public class ManifestNotEncryptedException : Exception { }

        public async System.Threading.Tasks.Task<string?> PromptForPassKeyAsync()
        {
            if (!this.Encrypted)
            {
                throw new ManifestNotEncryptedException();
            }
            if (Prompter == null)
            {
                throw new InvalidOperationException("No IPasskeyPrompter has been configured (Manifest.Prompter).");
            }

            bool passKeyValid = false;
            string? passKey = null;
            while (!passKeyValid)
            {
                passKey = await Prompter.PromptForPasskeyAsync("Please enter your encryption passkey.");
                if (passKey != null)
                {
                    passKeyValid = this.VerifyPasskey(passKey);
                    if (!passKeyValid)
                    {
                        await Prompter.ShowMessageAsync("That passkey is invalid.");
                    }
                }
                else
                {
                    return null;
                }
            }
            return passKey;
        }

        public async System.Threading.Tasks.Task<string?> PromptSetupPassKeyAsync(string initialPrompt = "Enter passkey, or hit cancel to remain unencrypted.")
        {
            if (Prompter == null)
            {
                throw new InvalidOperationException("No IPasskeyPrompter has been configured (Manifest.Prompter).");
            }

            string? newPassKey = await Prompter.PromptForNewPasskeyAsync(initialPrompt);
            if (string.IsNullOrEmpty(newPassKey))
            {
                await Prompter.ShowWarningAsync("WARNING: You chose to not encrypt your files. Doing so imposes a security risk for yourself. If an attacker were to gain access to your computer, they could completely lock you out of your account and steal all your items.");
                return null;
            }

            string? confirmPassKey = await Prompter.PromptForNewPasskeyAsync("Confirm new passkey.");
            if (confirmPassKey == null)
            {
                await Prompter.ShowWarningAsync("WARNING: You chose to not encrypt your files. Doing so imposes a security risk for yourself. If an attacker were to gain access to your computer, they could completely lock you out of your account and steal all your items.");
                return null;
            }

            if (newPassKey != confirmPassKey)
            {
                await Prompter.ShowMessageAsync("Passkeys do not match.");
                return null;
            }

            if (!this.ChangeEncryptionKey(null, newPassKey))
            {
                await Prompter.ShowMessageAsync("Unable to set passkey.");
                return null;
            }
            else
            {
                await Prompter.ShowMessageAsync("Passkey successfully set.");
            }

            return newPassKey;
        }

        public SteamGuardAccount[] GetAllAccounts(string? passKey = null, int limit = -1)
        {
            if (passKey == null && this.Encrypted) return Array.Empty<SteamGuardAccount>();
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";

            List<SteamGuardAccount> accounts = new List<SteamGuardAccount>();
            foreach (var entry in this.Entries)
            {
                string path = maDir + entry.Filename;
                if (!File.Exists(path)) continue;

                string fileText = File.ReadAllText(path);
                if (this.Encrypted)
                {
                    if (entry.IV == null || entry.Salt == null) continue;
                    string? decrypted = FileEncryptor.DecryptData(passKey!, entry.Salt, entry.IV, fileText);
                    if (decrypted == null) continue;
                    fileText = decrypted;
                }

                SteamGuardAccount? account;
                try
                {
                    account = JsonConvert.DeserializeObject<SteamGuardAccount>(fileText);
                }
                catch
                {
                    continue;
                }
                if (account == null) continue;

                accounts.Add(account);

                if (limit != -1 && accounts.Count >= limit)
                    break;
            }

            return accounts.ToArray();
        }

        public bool ChangeEncryptionKey(string? oldKey, string? newKey)
        {
            if (this.Encrypted)
            {
                if (!this.VerifyPasskey(oldKey))
                {
                    return false;
                }
            }
            bool toEncrypt = newKey != null;

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            for (int i = 0; i < this.Entries.Count; i++)
            {
                ManifestEntry entry = this.Entries[i];
                string filename = maDir + entry.Filename;
                if (!File.Exists(filename)) continue;

                string fileContents = File.ReadAllText(filename);
                if (this.Encrypted)
                {
                    string? decrypted = FileEncryptor.DecryptData(oldKey!, entry.Salt!, entry.IV!, fileContents);
                    if (decrypted == null) return false;
                    fileContents = decrypted;
                }

                string? newSalt = null;
                string? newIV = null;
                string toWriteFileContents = fileContents;

                if (toEncrypt)
                {
                    newSalt = FileEncryptor.GetRandomSalt();
                    newIV = FileEncryptor.GetInitializationVector();
                    toWriteFileContents = FileEncryptor.EncryptData(newKey!, newSalt, newIV, fileContents);
                }

                File.WriteAllText(filename, toWriteFileContents);
                entry.IV = newIV;
                entry.Salt = newSalt;
            }

            this.Encrypted = toEncrypt;

            this.Save();
            return true;
        }

        public bool VerifyPasskey(string? passkey)
        {
            if (!this.Encrypted || this.Entries.Count == 0) return true;
            if (passkey == null) return false;

            var accounts = this.GetAllAccounts(passkey, 1);
            return accounts.Length == 1;
        }

        public bool RemoveAccount(SteamGuardAccount account, bool deleteMaFile = true)
        {
            ManifestEntry? entry = this.Entries.FirstOrDefault(e => e.SteamID == account.Session.SteamID);
            if (entry == null) return true;

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = maDir + entry.Filename;
            this.Entries.Remove(entry);

            if (this.Entries.Count == 0)
            {
                this.Encrypted = false;
            }

            if (this.Save() && deleteMaFile)
            {
                try
                {
                    File.Delete(filename);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public bool SaveAccount(SteamGuardAccount account, bool encrypt, string? passKey = null)
        {
            if (encrypt && string.IsNullOrEmpty(passKey)) return false;
            if (!encrypt && this.Encrypted) return false;

            string? salt = null;
            string? iV = null;
            string jsonAccount = JsonConvert.SerializeObject(account);

            if (encrypt)
            {
                salt = FileEncryptor.GetRandomSalt();
                iV = FileEncryptor.GetInitializationVector();
                string encrypted = FileEncryptor.EncryptData(passKey!, salt, iV, jsonAccount);
                jsonAccount = encrypted;
            }

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = account.Session.SteamID.ToString() + ".maFile";

            ManifestEntry newEntry = new ManifestEntry()
            {
                SteamID = account.Session.SteamID,
                IV = iV,
                Salt = salt,
                Filename = filename
            };

            bool foundExistingEntry = false;
            for (int i = 0; i < this.Entries.Count; i++)
            {
                if (this.Entries[i].SteamID == account.Session.SteamID)
                {
                    this.Entries[i] = newEntry;
                    foundExistingEntry = true;
                    break;
                }
            }

            if (!foundExistingEntry)
            {
                this.Entries.Add(newEntry);
            }

            bool wasEncrypted = this.Encrypted;
            this.Encrypted = encrypt || this.Encrypted;

            if (!this.Save())
            {
                this.Encrypted = wasEncrypted;
                return false;
            }

            try
            {
                if (!Directory.Exists(maDir)) Directory.CreateDirectory(maDir);
                File.WriteAllText(maDir + filename, jsonAccount);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Save()
        {
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = maDir + "manifest.json";
            if (!Directory.Exists(maDir))
            {
                try
                {
                    Directory.CreateDirectory(maDir);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            try
            {
                string contents = JsonConvert.SerializeObject(this);
                File.WriteAllText(filename, contents);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void RecomputeExistingEntries()
        {
            List<ManifestEntry> newEntries = new List<ManifestEntry>();
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";

            foreach (var entry in this.Entries)
            {
                string filename = maDir + entry.Filename;
                if (File.Exists(filename))
                {
                    newEntries.Add(entry);
                }
            }

            this.Entries = newEntries;

            if (this.Entries.Count == 0)
            {
                this.Encrypted = false;
            }
        }

        public void MoveEntry(int from, int to)
        {
            if (from < 0 || to < 0 || from > Entries.Count || to > Entries.Count - 1) return;
            ManifestEntry sel = Entries[from];
            Entries.RemoveAt(from);
            Entries.Insert(to, sel);
            Save();
        }

        public class ManifestEntry
        {
            [JsonProperty("encryption_iv")]
            public string? IV { get; set; }

            [JsonProperty("encryption_salt")]
            public string? Salt { get; set; }

            [JsonProperty("filename")]
            public string Filename { get; set; } = "";

            [JsonProperty("steamid")]
            public ulong SteamID { get; set; }
        }
    }
}
