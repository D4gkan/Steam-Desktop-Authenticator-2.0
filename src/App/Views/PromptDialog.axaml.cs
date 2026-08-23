using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace SteamDesktopAuthenticator.Views
{
    public enum PromptMode
    {
        Message,     // OK only
        Confirm,     // OK / Cancel, no input
        TextInput,   // OK / Cancel + plain textbox
        PasswordInput, // OK / Cancel + masked textbox
        ThreeChoice  // Yes / No / Cancel, no input - closes with bool? (true/false/null)
    }

    public partial class PromptDialog : Window
    {
        private PromptMode _mode = PromptMode.Message;

        public PromptDialog()
        {
            InitializeComponent();
        }

        public static PromptDialog Create(PromptMode mode, string title, string message, string? initialValue = null,
            string yesLabel = "Yes", string noLabel = "No", string cancelLabel = "Cancel")
        {
            var dlg = new PromptDialog();
            dlg._mode = mode;
            dlg.TitleText.Text = title;
            dlg.MessageText.Text = message;

            switch (mode)
            {
                case PromptMode.Message:
                    dlg.CancelButton.IsVisible = false;
                    break;
                case PromptMode.Confirm:
                    dlg.OkButton.Content = "Confirm";
                    break;
                case PromptMode.TextInput:
                    dlg.InputBox.IsVisible = true;
                    dlg.InputBox.Text = initialValue ?? "";
                    dlg.InputBox.PasswordChar = default;
                    break;
                case PromptMode.PasswordInput:
                    dlg.InputBox.IsVisible = true;
                    dlg.InputBox.PasswordChar = '•';
                    dlg.InputBox.Text = initialValue ?? "";
                    break;
                case PromptMode.ThreeChoice:
                    dlg.OkButton.Content = yesLabel;
                    dlg.NoButton.Content = noLabel;
                    dlg.NoButton.IsVisible = true;
                    dlg.CancelButton.Content = cancelLabel;

                    // Yes/No/Cancel labels here are full sentences (e.g. "Yes - Remove Steam Guard
                    // Completely"), which don't fit three-across in the normal 420px-wide horizontal
                    // button row - stack them full-width instead and widen the dialog to fit.
                    dlg.ButtonsPanel.Orientation = global::Avalonia.Layout.Orientation.Vertical;
                    dlg.ButtonsPanel.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                    dlg.OkButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                    dlg.OkButton.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                    dlg.NoButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                    dlg.NoButton.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                    dlg.CancelButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                    dlg.CancelButton.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;

                    // Re-order to Yes / No / Cancel, top to bottom, matching the original WinForms layout.
                    dlg.ButtonsPanel.Children.Clear();
                    dlg.ButtonsPanel.Children.Add(dlg.OkButton);
                    dlg.ButtonsPanel.Children.Add(dlg.NoButton);
                    dlg.ButtonsPanel.Children.Add(dlg.CancelButton);

                    dlg.Width = 480;
                    break;
            }

            dlg.InputBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) dlg.OnOkClick(dlg, new RoutedEventArgs());
            };

            return dlg;
        }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            if (_mode == PromptMode.TextInput || _mode == PromptMode.PasswordInput)
            {
                Close(InputBox.Text);
            }
            else
            {
                Close(true);
            }
        }

        private void OnNoClick(object? sender, RoutedEventArgs e)
        {
            // Only reachable in ThreeChoice mode - the "No" button is hidden otherwise.
            Close(false);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            if (_mode == PromptMode.TextInput || _mode == PromptMode.PasswordInput)
            {
                Close(null);
            }
            else if (_mode == PromptMode.ThreeChoice)
            {
                Close(null); // Cancel = null, distinct from No = false
            }
            else
            {
                Close(false);
            }
        }
    }
}
