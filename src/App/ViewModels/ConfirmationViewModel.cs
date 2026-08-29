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
            // Steam sent a type this build doesn't recognize yet. Rather than inventing a label,
            // fall back to whatever human-readable name Steam itself included (type_name), and
            // only fall further back to a generic label if even that is missing.
            Confirmation.EMobileConfirmationType.Unknown when !string.IsNullOrWhiteSpace(Confirmation.TypeName) => Confirmation.TypeName,
            Confirmation.EMobileConfirmationType.Unknown => "Unrecognized Confirmation",
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

        /// <summary>True for confirmation types Steam has documented but this account's SDA
        /// build doesn't have a dedicated label/handling for yet (Task 12). Bound by the view to
        /// show a small "unrecognized" note so nothing is silently hidden.</summary>
        public bool IsUnrecognizedType => Confirmation.ConfType == Confirmation.EMobileConfirmationType.Unknown;

        public ConfirmationViewModel(Confirmation confirmation, AccountViewModel owner)
        {
            Confirmation = confirmation;
            Owner = owner;
        }
    }
}
