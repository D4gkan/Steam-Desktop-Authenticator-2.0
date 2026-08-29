using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Newtonsoft.Json;
using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SteamDesktopAuthenticator.Views
{
    public partial class ImportAccountWindow : Window
    {
        private readonly IDialogService _dialogService;
        private readonly Manifest _manifest;

        /// <summary>Raised on the UI thread whenever an account is successfully linked/imported,
        /// so MainWindow can add it to the live dashboard without a full reload. The bool/string?
        /// carry the "Save password for automatic re-login" checkbox state from whatever
        /// LoginWindow was shown as part of this flow (Task 1) - both are default/null when no
        /// fresh login happened (e.g. importing a .maFile that already had a valid session).</summary>
        public event Action<SteamGuardAccount, bool, string?>? AccountAdded;

        /// <summary>Design-time/XAML-loader constructor only. Not used at runtime - the app always
        /// constructs this window via the (IDialogService) overload below.</summary>
        public ImportAccountWindow() : this(null!)
        {
        }

        public ImportAccountWindow(IDialogService dialogService)
        {
            InitializeComponent();
            _dialogService = dialogService;
            _manifest = Manifest.GetManifest();
        }

        private void ShowError(string message)
        {
            StatusText.Text = message;
            StatusText.IsVisible = true;
        }

        private async void OnLoginNewClick(object? sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow(_dialogService, LoginType.Initial);
            var ok = await loginWindow.ShowDialog<bool>(this);
            if (ok && loginWindow.LinkedAccount != null)
            {
                AccountAdded?.Invoke(loginWindow.LinkedAccount, loginWindow.SavePasswordRequested, loginWindow.ConsumeEnteredPassword());
                await _dialogService.ShowMessageAsync("Account added to the dashboard.");
            }
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            StatusText.IsVisible = false;

            // Original SDA only supports importing when the *current* manifest is unencrypted -
            // preserved here for parity, including the exact message shown.
            if (_manifest.Encrypted)
            {
                ShowError("You can't import an .maFile because the existing accounts in the app are encrypted. Decrypt them and try again.");
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select one or more .maFiles",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("maFile") { Patterns = new[] { "*.maFile" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;

            string encryptionKey = EncryptionKeyBox.Text ?? "";
            int successCount = 0;
            var failures = new List<string>();

            foreach (var file in files)
            {
                var (ok, error) = await ImportSingleFileAsync(file, encryptionKey);
                if (ok)
                {
                    successCount++;
                }
                else
                {
                    failures.Add(files.Count == 1 ? error! : $"{file.Name}: {error}");
                }
            }

            if (successCount > 0)
            {
                EncryptionKeyBox.Text = "";
            }

            if (failures.Count == 0)
            {
                await _dialogService.ShowMessageAsync(successCount == 1 ? "Account imported." : $"{successCount} accounts imported.");
            }
            else if (successCount == 0)
            {
                ShowError(string.Join("\n", failures));
            }
            else
            {
                await _dialogService.ShowMessageAsync($"{successCount} account(s) imported successfully.");
                ShowError($"{failures.Count} file(s) failed to import:\n" + string.Join("\n", failures));
            }
        }

        /// <summary>Imports one .maFile. Same logic/behavior as the original single-file flow
        /// (including the login prompt for files with a missing/expired session), just returning a
        /// result instead of touching StatusText directly, so OnBrowseClick can run it per file and
        /// summarize the whole batch afterward.</summary>
        private async Task<(bool ok, string? error)> ImportSingleFileAsync(IStorageFile file, string encryptionKey)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                string fileContents = await reader.ReadToEndAsync();

                string fileText;

                if (string.IsNullOrEmpty(encryptionKey))
                {
                    fileText = fileContents;
                }
                else
                {
                    // Encrypted import: look for the source folder's own manifest.json to find
                    // this file's salt/IV, exactly as the original does.
                    var localPath = file.TryGetLocalPath();
                    if (localPath == null)
                    {
                        return (false, "Could not resolve a local file path for the selected file.");
                    }

                    var sourceDir = Path.GetDirectoryName(localPath) ?? "";
                    var sourceManifestPath = Path.Combine(sourceDir, "manifest.json");
                    if (!File.Exists(sourceManifestPath))
                    {
                        return (false, "Could not find the source manifest.json next to this file, so it can't be decrypted.");
                    }

                    var sourceManifestJson = File.ReadAllText(sourceManifestPath);
                    Manifest? sourceManifest;
                    try
                    {
                        sourceManifest = JsonConvert.DeserializeObject<Manifest>(sourceManifestJson);
                    }
                    catch
                    {
                        return (false, "Invalid content inside the source manifest.json. Import failed.");
                    }

                    var entry = sourceManifest?.Entries.FirstOrDefault(en => en.Filename == file.Name);
                    if (entry == null || entry.Salt == null || entry.IV == null)
                    {
                        return (false, "This file isn't listed as encrypted in its source manifest.json. Leave the encryption key blank and try again.");
                    }

                    var decrypted = FileEncryptor.DecryptData(encryptionKey, entry.Salt, entry.IV, fileContents);
                    if (decrypted == null)
                    {
                        return (false, "Decryption failed - check the encryption key.");
                    }
                    fileText = decrypted;
                }

                SteamGuardAccount? account;
                try
                {
                    account = JsonConvert.DeserializeObject<SteamGuardAccount>(fileText);
                }
                catch
                {
                    return (false, "This doesn't look like a valid .maFile.");
                }
                if (account == null)
                {
                    return (false, "This doesn't look like a valid .maFile.");
                }

                bool saveLoginRequested = false;
                string? password = null;

                if (account.Session == null || account.Session.SteamID == 0 || account.Session.IsAccessTokenExpired())
                {
                    var loginWindow = new LoginWindow(_dialogService, LoginType.Import, account);
                    var ok = await loginWindow.ShowDialog<bool>(this);
                    if (!ok || loginWindow.Session == null || loginWindow.Session.SteamID == 0)
                    {
                        return (false, "Login failed. Try importing this account again.");
                    }
                    account.Session = loginWindow.Session;
                    saveLoginRequested = loginWindow.SavePasswordRequested;
                    password = loginWindow.ConsumeEnteredPassword();
                }

                // Preserved from original: imported accounts are saved unencrypted into the
                // (already-verified-unencrypted) current manifest.
                if (!_manifest.SaveAccount(account, false))
                {
                    return (false, "Failed to save the imported account.");
                }

                AccountAdded?.Invoke(account, saveLoginRequested, password);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, "Import failed: " + ex.Message);
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }
}
