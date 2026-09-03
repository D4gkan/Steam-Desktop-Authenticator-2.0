using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAuth;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Services;

namespace SteamDesktopAuthenticator.ViewModels
{
    public partial class AccountViewModel : ViewModelBase
    {
        public SteamGuardAccount Account { get; }
        public AccountMeta Meta { get; }

        [ObservableProperty]
        private string _code = "-----";

        /// <summary>Steam profile picture, once resolved. Null (the default) means "not
        /// available" - the UI falls back to the existing initial-letter avatar circle in that
        /// case, so a slow/failed fetch never leaves a gap in the layout.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAvatar))]
        private Bitmap? _avatarBitmap;

        public bool HasAvatar => AvatarBitmap != null;

        [ObservableProperty]
        private double _codeProgress = 1.0; // 1.0 -> just refreshed, 0.0 -> about to rotate

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPendingConfirmations))]
        private int _pendingConfirmationCount;

        public bool HasPendingConfirmations => PendingConfirmationCount > 0;

        public string SteamId => Account.Session?.SteamID.ToString() ?? "unknown";

        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(Meta.DisplayName) ? Account.AccountName : Meta.DisplayName!;
            set
            {
                Meta.DisplayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Initial));
            }
        }

        /// <summary>Single-character avatar initial. A separate string property (rather than
        /// indexing DisplayName in XAML) so it binds cleanly to a Text property with compiled bindings.</summary>
        public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();

        public bool Enabled
        {
            get => Meta.Enabled;
            set
            {
                if (Meta.Enabled == value) return;
                Meta.Enabled = value;
                OnPropertyChanged();
            }
        }

        public int Order
        {
            get => Meta.Order;
            set
            {
                Meta.Order = value;
                OnPropertyChanged();
            }
        }

        public AccountViewModel(SteamGuardAccount account, AccountMeta meta)
        {
            Account = account;
            Meta = meta;
            LoadAvatar();
        }

        /// <summary>Kicks off an async, non-blocking avatar fetch for this account's SteamID.
        /// Safe to call even when the SteamID is unknown/zero (no-op) or the network is
        /// unavailable (AvatarBitmap simply stays null and the UI keeps the initial-letter
        /// placeholder). Never throws and never delays construction of this view model.</summary>
        private void LoadAvatar()
        {
            if (!ulong.TryParse(SteamId, out var steamId) || steamId == 0) return;
            AvatarService.FetchAndApply(steamId, bitmap => AvatarBitmap = bitmap);
        }

        /// <summary>Recomputes the current Steam Guard code and how much of its 30s window remains.
        /// Called every second by MainWindowViewModel's refresh timer.</summary>
        public void RefreshCode()
        {
            long steamTime = TimeAligner.GetSteamTime();
            Code = Account.GenerateSteamGuardCodeForTime(steamTime);
            double secondsIntoWindow = steamTime % 30;
            CodeProgress = 1.0 - (secondsIntoWindow / 30.0);
        }
    }
}
