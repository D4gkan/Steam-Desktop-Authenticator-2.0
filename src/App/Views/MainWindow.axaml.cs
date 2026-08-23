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
            window.AccountAdded += account => _vm.AddImportedAccount(account);
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
                var loginWindow = new LoginWindow(_vm.DialogService, LoginType.Refresh, account.Account);
                await loginWindow.ShowDialog(this);
            };

            var deactivateItem = new MenuItem { Header = "Deactivate Authenticator…" };
            deactivateItem.Click += async (_, _) => await _vm.DeactivateAuthenticatorCommand.ExecuteAsync(account);

            var removeItem = new MenuItem { Header = "Remove Account" };
            removeItem.Click += async (_, _) => await _vm.RemoveAccountCommand.ExecuteAsync(account);

            menu.Items.Add(renameItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(reloginItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(deactivateItem);
            menu.Items.Add(removeItem);

            menu.Open(control);
            await System.Threading.Tasks.Task.CompletedTask;
        }

    }
}
