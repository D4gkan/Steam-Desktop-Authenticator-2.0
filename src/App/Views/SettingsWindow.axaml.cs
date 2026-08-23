using Avalonia.Controls;
using Avalonia.Interactivity;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Services;

namespace SteamDesktopAuthenticator.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly Manifest _manifest;
        private readonly IDialogService _dialogService;
        private bool _fullyLoaded;

        /// <summary>Raised after Save, so MainWindowViewModel can restart the polling timer
        /// with the new interval/enabled state.</summary>
        public bool SettingsChanged { get; private set; }

        /// <summary>Design-time/XAML-loader constructor only. Not used at runtime - the app always
        /// constructs this window via the (IDialogService) overload below.</summary>
        public SettingsWindow() : this(null!)
        {
        }

        public SettingsWindow(IDialogService dialogService)
        {
            InitializeComponent();
            _dialogService = dialogService;
            _manifest = Manifest.GetManifest(true);

            PeriodicCheckingBox.IsChecked = _manifest.PeriodicChecking;
            IntervalBox.Value = _manifest.PeriodicCheckingInterval;
            CheckAllBox.IsChecked = _manifest.CheckAllAccounts;
            ConfirmMarketBox.IsChecked = _manifest.AutoConfirmMarketTransactions;
            ConfirmTradesBox.IsChecked = _manifest.AutoConfirmTrades;

            SetControlsEnabledState(_manifest.PeriodicChecking);
            _fullyLoaded = true;
        }

        private void SetControlsEnabledState(bool enabled)
        {
            IntervalBox.IsEnabled = CheckAllBox.IsEnabled = ConfirmMarketBox.IsEnabled = ConfirmTradesBox.IsEnabled = enabled;
        }

        private async void ShowWarningIfNeeded(CheckBox affectedBox)
        {
            if (!_fullyLoaded) return;
            if (affectedBox.IsChecked != true) return;

            bool proceed = await _dialogService.ConfirmAsync(
                "Warning!",
                "Warning: enabling this will severely reduce the security of your items! Use of this option is at your own risk. Would you like to continue?");
            if (!proceed)
            {
                affectedBox.IsChecked = false;
            }
        }

        private void OnPeriodicCheckingChanged(object? sender, RoutedEventArgs e)
        {
            SetControlsEnabledState(PeriodicCheckingBox.IsChecked == true);
        }

        private void OnConfirmMarketChecked(object? sender, RoutedEventArgs e) => ShowWarningIfNeeded(ConfirmMarketBox);
        private void OnConfirmTradesChecked(object? sender, RoutedEventArgs e) => ShowWarningIfNeeded(ConfirmTradesBox);

        private void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            _manifest.PeriodicChecking = PeriodicCheckingBox.IsChecked == true;
            _manifest.PeriodicCheckingInterval = (int)(IntervalBox.Value ?? 5);
            _manifest.CheckAllAccounts = CheckAllBox.IsChecked == true;
            _manifest.AutoConfirmMarketTransactions = ConfirmMarketBox.IsChecked == true;
            _manifest.AutoConfirmTrades = ConfirmTradesBox.IsChecked == true;
            _manifest.Save();
            SettingsChanged = true;
            Close(true);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    }
}
