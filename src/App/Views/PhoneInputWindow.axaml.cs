using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SteamDesktopAuthenticator.Views
{
    public class PhoneInputResult
    {
        public string? PhoneNumber { get; set; }
        public string? CountryCode { get; set; }
        public bool Canceled { get; set; }
    }

    public partial class PhoneInputWindow : Window
    {
        public PhoneInputWindow()
        {
            InitializeComponent();
        }

        private void OnContinueClick(object? sender, RoutedEventArgs e)
        {
            var phone = PhoneNumberBox.Text?.Trim() ?? "";
            var country = CountryCodeBox.Text?.Trim().ToUpperInvariant() ?? "";

            // Same normalization/validation as the original PhoneInputForm.
            phone = phone.Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", "");
            if (phone.Length == 0 || phone[0] != '+')
            {
                phone = "+" + phone;
            }

            Close(new PhoneInputResult { PhoneNumber = phone, CountryCode = country, Canceled = false });
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(new PhoneInputResult { Canceled = true });
        }
    }
}
