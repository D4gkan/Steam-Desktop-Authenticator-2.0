using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.TextInput;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sda.Mobile.Core;

namespace Sda.Mobile;

public partial class MainView : UserControl
{
    private TaskCompletionSource<string?>? _pendingPrompt;
    private bool _confirmation;
    private PromptRequest? _pinRequest;
    private string _pinValue = "";
    private int _pinLength;
    private bool _pinSubmitting;
    private bool _startupShown;
    private MainViewModel Model => (MainViewModel)DataContext!;

    public MainView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.UIThread.Post(async () => await StartAsync());
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel model) model.Locking += CancelPrompt;
        };
    }

    private async Task StartAsync()
    {
        if (_startupShown) return;
        _startupShown = true;
        if (Model.IsUnlocked) return;
        Console.WriteLine("SDA startup: logo shown.");
        await Task.Delay(650);
        StartupLogo.Opacity = 0;
        await Task.Delay(250);
        Console.WriteLine("SDA startup: logo faded.");
        await Model.InitializeAsync();
    }

    public Task<string?> PromptAsync(PromptRequest request)
    {
        CancelPrompt();
        _pendingPrompt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (request.IsNumeric)
        {
            _pinRequest = request;
            _pinLength = request.PinLength;
            PinTitle.Text = request.Title;
            PinCancel.IsVisible = request.CanCancel;
            PinError.Text = "";
            LegacyPinToggle.IsVisible = request.AllowLegacyPinLength;
            LegacyPinToggle.Content = "Use 8 digits";
            BuildPinSlots();
            PinOverlay.IsVisible = true;
            Dispatcher.UIThread.Post(() => PinOverlay.Focus());
            return _pendingPrompt.Task;
        }
        _confirmation = request.IsConfirmation;
        PromptTitle.Text = request.Title;
        PromptMessage.Text = request.Message;
        PromptInput.Text = "";
        PromptInput.PasswordChar = request.IsPassword ? '●' : '\0';
        PromptInput.IsVisible = !request.IsConfirmation;
        PromptInput.AcceptsReturn = request.IsMultiline;
        PromptInput.TextWrapping = request.IsMultiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
        PromptInput.MinHeight = request.IsMultiline ? 140 : 48;
        PromptInput.MaxHeight = request.IsMultiline ? 220 : 48;
        TextInputOptions.SetContentType(PromptInput, request.IsNumeric ? TextInputContentType.Digits : TextInputContentType.Normal);
        PromptContinue.Content = request.AcceptText;
        PromptOverlay.IsVisible = true;
        if (!request.IsConfirmation) Dispatcher.UIThread.Post(() => PromptInput.Focus());
        return _pendingPrompt.Task;
    }

    private void FinishPrompt(string? answer)
    {
        var completion = _pendingPrompt;
        _pendingPrompt = null;
        PromptInput.Text = "";
        PromptOverlay.IsVisible = false;
        PinOverlay.IsVisible = false;
        _pinRequest = null;
        _pinValue = "";
        _pinSubmitting = false;
        PinSlots.Children.Clear();
        completion?.TrySetResult(answer);
    }

    private void CancelPrompt() => FinishPrompt(null);
    private void OnPromptContinue(object? sender, RoutedEventArgs e) => FinishPrompt(_confirmation ? "yes" : PromptInput.Text ?? "");
    private void OnPromptCancel(object? sender, RoutedEventArgs e)
    { if (_pinRequest?.CanCancel != false) CancelPrompt(); }
    private void OnDismissNotice(object? sender, RoutedEventArgs e) => Model.DismissNotice();
    private void BuildPinSlots()
    {
        PinSlots.Columns = _pinLength;
        PinSlots.Children.Clear();
        for (var index = 0; index < _pinLength; index++)
            PinSlots.Children.Add(new Border
            {
                Margin = new Avalonia.Thickness(4, 0), CornerRadius = new Avalonia.CornerRadius(10),
                BorderThickness = new Avalonia.Thickness(1), BorderBrush = Brush.Parse("#2C2837"),
                Child = new TextBlock { Text = index < _pinValue.Length ? "●" : "", FontSize = 20,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            });
    }
    private async Task AppendPinAsync(string digit)
    {
        if (_pinRequest == null || _pinSubmitting || _pinValue.Length >= _pinLength) return;
        _pinValue += digit;
        PinError.Text = "";
        BuildPinSlots();
        if (_pinValue.Length != _pinLength) return;
        var request = _pinRequest;
        var pin = _pinValue;
        _pinSubmitting = true;
        try
        {
            var error = request.ValidatePinAsync == null ? null : await request.ValidatePinAsync(pin);
            if (!ReferenceEquals(_pinRequest, request)) return;
            if (error == null) FinishPrompt(pin);
            else { PinError.Text = error; _pinValue = ""; BuildPinSlots(); }
        }
        catch (OperationCanceledException) { FinishPrompt(null); }
        catch (Exception) { PinError.Text = "Could not unlock. Try again."; _pinValue = ""; BuildPinSlots(); }
        finally { _pinSubmitting = false; }
    }
    private async void OnPinDigit(object? sender, RoutedEventArgs e)
    { if (sender is Button { Content: string digit }) await AppendPinAsync(digit); }
    private void OnPinDelete(object? sender, RoutedEventArgs e)
    {
        if (_pinSubmitting || _pinValue.Length == 0) return;
        _pinValue = _pinValue[..^1];
        BuildPinSlots();
    }
    private void OnLegacyPinToggle(object? sender, RoutedEventArgs e)
    {
        if (_pinSubmitting) return;
        _pinLength = _pinLength == 6 ? 8 : 6;
        _pinValue = "";
        LegacyPinToggle.Content = _pinLength == 6 ? "Use 8 digits" : "Use 6 digits";
        BuildPinSlots();
    }
    private async void OnPinKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.D0 and <= Key.D9) { e.Handled = true; await AppendPinAsync(((int)e.Key - (int)Key.D0).ToString()); }
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9) { e.Handled = true; await AppendPinAsync(((int)e.Key - (int)Key.NumPad0).ToString()); }
        else if (e.Key == Key.Back) { e.Handled = true; OnPinDelete(sender, new RoutedEventArgs()); }
    }
    private async void OnUnlock(object? sender, RoutedEventArgs e) => await Model.UnlockAsync();
    private async void OnChangePasscode(object? sender, RoutedEventArgs e) => await Model.ChangePasscodeAsync();
    private async void OnLock(object? sender, RoutedEventArgs e) { Model.Lock(); await Model.UnlockAsync(); }
    private async void OnScan(object? sender, RoutedEventArgs e) => await Model.ScanAsync();
    private async void OnPaste(object? sender, RoutedEventArgs e) => await Model.PasteQrAsync();
    private async void OnLink(object? sender, RoutedEventArgs e) => await Model.LinkAsync();
    private async void OnResumeLink(object? sender, RoutedEventArgs e) => await Model.ResumeLinkAsync();
    private async void OnSelectAccount(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileAccount account })
        {
            await Model.OpenConfirmationsAsync(account);
        }
    }
    private void OnManageAccount(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileAccount account, Flyout: MenuFlyout flyout })
        {
            foreach (var item in flyout.Items.OfType<MenuItem>())
            { item.Tag = account; item.DataContext = account; }
        }
    }
    private bool SelectMenuAccount(object? sender)
    {
        if (!Model.CanInteract || sender is not MenuItem { Tag: MobileAccount account } || !Model.Accounts.Contains(account)) return false;
        Model.SelectedAccount = account;
        return true;
    }
    private void OnMenuExport(object? sender, RoutedEventArgs e) { if (SelectMenuAccount(sender)) OnExport(sender, e); }
    private async void OnMenuQr(object? sender, RoutedEventArgs e) { if (SelectMenuAccount(sender)) await Model.ShowQrAsync(); }
    private async void OnMenuSignIn(object? sender, RoutedEventArgs e) { if (SelectMenuAccount(sender)) await Model.SignInAsync(); }
    private async void OnMenuRemove(object? sender, RoutedEventArgs e) { if (SelectMenuAccount(sender)) await Model.RemoveAsync(); }
    private async void OnMenuResume(object? sender, RoutedEventArgs e) { if (SelectMenuAccount(sender)) await Model.ResumeLinkAsync(); }
    private void OnAccountsTab(object? sender, RoutedEventArgs e) => Model.Section = MobileSection.Accounts;
    private async void OnActivityTab(object? sender, RoutedEventArgs e) => await Model.OpenConfirmationsAsync();
    private void OnSettingsTab(object? sender, RoutedEventArgs e) => Model.Section = Model.ShowSettings ? MobileSection.Accounts : MobileSection.Settings;
    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileAccount account }) Model.CopyCode(account);
    }
    private async void OnSignIn(object? sender, RoutedEventArgs e) => await Model.SignInAsync();
    private async void OnRefresh(object? sender, RoutedEventArgs e) => await Model.RefreshConfirmationsAsync();
    private async void OnPullRefresh(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try { if (Model.ShowActivity) await Model.RefreshConfirmationsAsync(); }
        finally { deferral.Complete(); }
    }
    private async void OnConfirmationSignIn(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConfirmationIssue issue }) return;
        Model.SelectedAccount = issue.Owner;
        await Model.SignInAsync();
        if (!Model.IsError) await Model.RefreshConfirmationsAsync();
    }
    private async void OnShowQr(object? sender, RoutedEventArgs e) => await Model.ShowQrAsync();
    private void OnCloseQr(object? sender, RoutedEventArgs e) => Model.CloseQr();
    private async void OnRemove(object? sender, RoutedEventArgs e) => await Model.RemoveAsync();
    private async void OnApprove(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileConfirmation item }) await Model.RespondAsync(item, true);
    }
    private async void OnReject(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MobileConfirmation item }) await Model.RespondAsync(item, false);
    }

    private async void OnImport(object? sender, RoutedEventArgs e) => await Model.ImportAsync(PickImportFilesAsync);

    private async Task<IReadOnlyList<ImportFile>> PickImportFilesAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanOpen != true) throw new InvalidOperationException("File import is unavailable. Use Paste account data instead.");
        var selected = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import SDA accounts", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Account files and backups")
            {
                // Avalonia prioritizes extensions over Apple UTIs. Use public.data on
                // iOS so an undeclared .maFile extension remains selectable in Files.
                Patterns = OperatingSystem.IsIOS() ? null : new[] { "*.maFile", "*.json", "*.zip" },
                AppleUniformTypeIdentifiers = new[] { "public.data" }
            } }
        });
        var files = new List<ImportFile>();
        try
        {
            var total = 0L;
            foreach (var file in selected)
            {
                await using (var input = await file.OpenReadAsync())
                {
                    using var output = new MemoryStream();
                    var limit = file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 32 * 1024 * 1024 : AccountTransfer.MaximumImportBytes;
                    var buffer = new byte[8192];
                    int count;
                    while ((count = await input.ReadAsync(buffer)) != 0)
                    {
                        total += count;
                        if (output.Length + count > limit || total > 32 * 1024 * 1024)
                            throw new InvalidDataException("The selected files are too large. Import fewer accounts at a time.");
                        output.Write(buffer, 0, count);
                    }
                    files.Add(new(file.Name, output.ToArray()));
                }
            }
            return files;
        }
        finally { foreach (var file in selected) file.Dispose(); }
    }

    private async void OnExport(object? sender, RoutedEventArgs e) => await Model.ExportAsync(async bytes =>
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) throw new InvalidOperationException("File export is unavailable on this device.");
        using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save encrypted account backup", SuggestedFileName = "SDA-account-backup.zip", DefaultExtension = "zip",
            FileTypeChoices = new[] { new FilePickerFileType("Encrypted SDA backup") { Patterns = new[] { "*.zip" }, AppleUniformTypeIdentifiers = new[] { "public.zip-archive" } } }
        });
        if (file == null) throw new OperationCanceledException();
        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(bytes);
    });
}
