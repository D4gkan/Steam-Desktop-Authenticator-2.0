using Avalonia.Controls;
using Avalonia.Interactivity;
using SteamDesktopAuthenticator.ViewModels;

namespace SteamDesktopAuthenticator.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _vm;

        public MainWindow()
        {
            InitializeComponent();
            Opened += async (_, _) =>
            {
                if (_vm != null) await _vm.InitializeAsync();
            };
        }

        public void AttachViewModel(MainWindowViewModel vm)
        {
            _vm = vm;
        }

        private async void OnAddAccountClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var window = new ImportAccountWindow(_vm.DialogService);
            window.AccountAdded += (account, saveLoginRequested, password) => _vm.AddImportedAccount(account, saveLoginRequested, password);
            await window.ShowDialog(this);
        }

        private async void OnSettingsClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var window = new SettingsWindow(_vm.DialogService);
            await window.ShowDialog(this);
            if (window.SettingsChanged)
            {
                _vm.ReloadManifestSettings();
            }
        }

        private async void OnUnlockClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.UnlockAsync();
        }

        private async void OnSetupEncryptionClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.SetupEncryptionCommand.ExecuteAsync(null);
        }

        private async void OnRemoveEncryptionClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RemoveEncryptionCommand.ExecuteAsync(null);
        }

        private async void OnAccountMenuClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            if (sender is not Control control) return;
            if (control.Tag is not AccountViewModel account) return;

            var menu = new ContextMenu();

            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) => await _vm.RenameAccountCommand.ExecuteAsync(account);

            var toggleItem = new MenuItem { Header = account.Enabled ? "Disable" : "Enable" };
            toggleItem.Click += (_, _) => _vm.ToggleEnabledCommand.Execute(account);

            var reloginItem = new MenuItem { Header = "Refresh Login…" };
            reloginItem.Click += async (_, _) =>
            {
                var loginWindow = new LoginWindow(_vm.DialogService, LoginType.Refresh, account.Account, account.Meta.SaveLoginEnabled);
                var ok = await loginWindow.ShowDialog<bool>(this);
                if (ok)
                {
                    // Task 1: reflect whatever the person chose in this login (checked, or
                    // unchecked to remove a previously saved password) back into the secure
                    // credential store and ui-meta.json.
                    _vm.ApplySavedLoginPreference(account, loginWindow.SavePasswordRequested, loginWindow.ConsumeEnteredPassword());
                }
            };

            var forgetPasswordItem = new MenuItem { Header = "Forget Saved Password" };
            forgetPasswordItem.Click += (_, _) => _vm.ForgetSavedPassword(account);

            var deactivateItem = new MenuItem { Header = "Deactivate Authenticator…" };
            deactivateItem.Click += async (_, _) => await _vm.DeactivateAuthenticatorCommand.ExecuteAsync(account);

            var removeItem = new MenuItem { Header = "Remove Account" };
            removeItem.Click += async (_, _) => await _vm.RemoveAccountCommand.ExecuteAsync(account);

            menu.Items.Add(renameItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(reloginItem);
            if (account.Meta.SaveLoginEnabled) menu.Items.Add(forgetPasswordItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(deactivateItem);
            menu.Items.Add(removeItem);

            menu.Open(control);
            await System.Threading.Tasks.Task.CompletedTask;
        }

    }
}
