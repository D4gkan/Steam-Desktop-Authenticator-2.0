using SteamAuth;
using System;
using System.Linq;

namespace SteamDesktopAuthenticator.ViewModels
{
    public class ConfirmationViewModel : ViewModelBase
    {
        public Confirmation Confirmation { get; }
        public AccountViewModel Owner { get; }

        public string AccountLabel => Owner.DisplayName;
        public string Headline => Confirmation.Headline;
        public string Summary => Confirmation.Summary != null ? string.Join(" ", Confirmation.Summary) : "";
        public string TypeLabel => Confirmation.ConfType switch
        {
            Confirmation.EMobileConfirmationType.Trade => "Trade",
            Confirmation.EMobileConfirmationType.MarketListing => "Market Listing",
            Confirmation.EMobileConfirmationType.PhoneNumberChange => "Phone Number Change",
            Confirmation.EMobileConfirmationType.AccountRecovery => "Account Recovery",
            Confirmation.EMobileConfirmationType.FeatureOptOut => "Feature Opt-Out",
            Confirmation.EMobileConfirmationType.Test => "Test",
            _ => "Confirmation"
        };

        /// <summary>Accent used by the UI to color-code the confirmation type indicator.</summary>
        public string TypeAccentKey => Confirmation.ConfType switch
        {
            Confirmation.EMobileConfirmationType.Trade => "TradeAccentBrush",
            Confirmation.EMobileConfirmationType.MarketListing => "MarketAccentBrush",
            Confirmation.EMobileConfirmationType.AccountRecovery => "DangerAccentBrush",
            Confirmation.EMobileConfirmationType.PhoneNumberChange => "DangerAccentBrush",
            _ => "NeutralAccentBrush"
        };

        public ConfirmationViewModel(Confirmation confirmation, AccountViewModel owner)
        {
            Confirmation = confirmation;
            Owner = owner;
        }
    }
}
