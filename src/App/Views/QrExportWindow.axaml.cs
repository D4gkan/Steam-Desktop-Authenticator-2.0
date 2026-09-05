using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;
using SteamAuth;

namespace SteamDesktopAuthenticator.Views
{
    public partial class QrExportWindow : Window
    {
        /// <summary>Design-time/XAML-loader constructor only.</summary>
        public QrExportWindow()
        {
            InitializeComponent();
        }

        public QrExportWindow(SteamGuardAccount account) : this()
        {
            AccountNameText.Text = account.AccountName ?? string.Empty;
            GenerateQrCode(account);
        }

        private void GenerateQrCode(SteamGuardAccount account)
        {
            try
            {
                // Keep the shared .maFile field names, then compact the payload for reliable
                // decoding by phone cameras. The Android importer also accepts legacy JSON.
                string plaintext = CreateQrAccountJson(account);
                string payload = EncodeQrPayload(plaintext);
                using QRCodeData qrData = QRCodeGenerator.GenerateQrCode(
                    payload,
                    QRCodeGenerator.ECCLevel.L);
                using var qrCode = new PngByteQRCode(qrData);
                byte[] png = qrCode.GetGraphic(10);
                using var stream = new MemoryStream(png);
                QrCodeImage.Source = new Bitmap(stream);
            }
            catch (Exception)
            {
                QrCodeImage.IsVisible = false;
                ErrorText.Text = "This account's data is too large to fit in a single QR code. Use file-based export instead.";
                ErrorText.IsVisible = true;
            }
        }

        private static string CreateQrAccountJson(SteamGuardAccount account)
        {
            JObject qrAccount = JObject.FromObject(account);
            if (qrAccount["Session"] is JObject session)
            {
                // Session JWTs make the QR needlessly dense and are not required to generate
                // Steam Guard codes. Preserve SteamID, then let the phone request login only
                // when an authenticated feature such as confirmations is used.
                session.Remove("AccessToken");
                session.Remove("RefreshToken");
                session.Remove("SessionID");
            }

            return qrAccount.ToString(Formatting.None);
        }

        private static string EncodeQrPayload(string plaintext)
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
                gzip.Write(bytes, 0, bytes.Length);
            }

            string base64Url = Convert.ToBase64String(compressed.ToArray())
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return "sda-mafile:v1:" + base64Url;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }
}
