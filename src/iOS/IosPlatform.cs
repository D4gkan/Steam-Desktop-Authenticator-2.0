using System.Security.Cryptography;
using System.Text;
using AVFoundation;
using Foundation;
using Security;
using UIKit;

namespace Sda.Mobile.iOS;

public sealed class IosPlatform : IMobilePlatform
{
    private const string KeychainService = "SDA.Mobile.Vault";
    private readonly TaskCompletionSource _hostReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void MarkHostReady()
    {
        if (_hostReady.TrySetResult()) Console.WriteLine("SDA startup: native host appeared.");
    }
    private QrScannerController? _scanner;
    private UIAlertController? _prompt;
    private TextImportController? _textImport;
    private PasscodeController? _passcodeInput;
    private TaskCompletionSource<string?>? _promptCompletion;
    public string AccountDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDA", "maFiles");

    public string GetVaultKey(bool vaultExists)
    {
        Directory.CreateDirectory(AccountDirectory);
        using var directoryUrl = NSUrl.FromFilename(AccountDirectory);
        if (!directoryUrl.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var backupError))
        {
            backupError?.Dispose();
            throw new InvalidOperationException("Could not exclude the account vault from device backups.");
        }
        // iOS file protection complements encryption, including while the app is suspended.
        NSFileManager.DefaultManager.SetAttributes(new NSFileAttributes
        {
            ProtectionKey = NSFileProtection.Complete
        }, AccountDirectory, out var protectionError);
        protectionError?.Dispose();

        var existing = ReadSecurityValue("vault-key-v1");
        if (existing != null) return existing;
        if (vaultExists)
            throw new InvalidOperationException("The vault key is missing. Keep these files and restore your encrypted backup; do not overwrite the vault.");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        WriteSecurityValue("vault-key-v1", key);
        return key;
    }

    public string? ReadSecurityValue(string name)
    {
        using var query = new SecRecord(SecKind.GenericPassword) { Service = KeychainService, Account = name };
        using var existing = SecKeyChain.QueryAsData(query, false, out var status);
        if (status == SecStatusCode.Success && existing != null) return Encoding.UTF8.GetString(existing.ToArray());
        if (status == SecStatusCode.ItemNotFound) return null;
        throw new InvalidOperationException("The iOS Keychain is unavailable. Unlock your iPhone and retry.");
    }

    public void WriteSecurityValue(string name, string value)
    {
        using var data = NSData.FromString(value, NSStringEncoding.UTF8);
        using var query = new SecRecord(SecKind.GenericPassword) { Service = KeychainService, Account = name };
        using var changes = new SecRecord { ValueData = data, Accessible = SecAccessible.WhenUnlockedThisDeviceOnly };
        var status = SecKeyChain.Update(query, changes);
        if (status == SecStatusCode.Success) return;
        if (status != SecStatusCode.ItemNotFound)
            throw new InvalidOperationException("Could not update vault protection in the iOS Keychain. Your existing vault is unchanged.");
        using var record = new SecRecord(SecKind.GenericPassword)
        {
            Service = KeychainService, Account = name, ValueData = data,
            Accessible = SecAccessible.WhenUnlockedThisDeviceOnly
        };
        if (SecKeyChain.Add(record) != SecStatusCode.Success)
            throw new InvalidOperationException("Could not save vault protection in the iOS Keychain.");
    }

    public void RemoveSecurityValue(string name)
    {
        using var query = new SecRecord(SecKind.GenericPassword) { Service = KeychainService, Account = name };
        var status = SecKeyChain.Remove(query);
        if (status is not (SecStatusCode.Success or SecStatusCode.ItemNotFound))
            throw new InvalidOperationException("Could not finish updating vault protection. Unlock your iPhone and retry.");
    }

    private static UIViewController GetPresenter()
    {
        // This host supports the legacy UIKit lifecycle as well as connected scenes.
        var window = (UIApplication.SharedApplication.Delegate as AppDelegate)?.Window
            ?? UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
                .SelectMany(scene => scene.Windows).FirstOrDefault(item => item.IsKeyWindow);
        var controller = window?.RootViewController
            ?? throw new InvalidOperationException("The native view is not ready. Try again.");
        while (controller.PresentedViewController is { } presented) controller = presented;
        return controller;
    }

    public async Task<string?> PromptAsync(PromptRequest request, Func<PromptRequest, Task<string?>> fallback)
    {
        await _hostReady.Task;
        CancelPrompt();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (request.IsNumeric)
        {
            using var controller = new PasscodeController(request, completion);
            _passcodeInput = controller;
            try { await GetPresenter().PresentViewControllerAsync(controller, true); return await completion.Task; }
            finally { if (ReferenceEquals(_passcodeInput, controller)) _passcodeInput = null; }
        }
        if (request.IsMultiline)
        {
            using var editor = new TextImportController(request, completion);
            _textImport = editor;
            try
            {
                await GetPresenter().PresentViewControllerAsync(editor, true);
                return await completion.Task;
            }
            finally { if (ReferenceEquals(_textImport, editor)) _textImport = null; }
        }
        using var alert = UIAlertController.Create(request.Title, request.Message, UIAlertControllerStyle.Alert);
        alert.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
        alert.View!.TintColor = UIColor.FromRGB(174, 154, 221);
        if (!request.IsConfirmation)
        {
            alert.AddTextField(input =>
            {
                input.SecureTextEntry = request.IsPassword;
                input.KeyboardType = request.IsNumeric ? UIKeyboardType.NumberPad : UIKeyboardType.Default;
                input.AutocorrectionType = UITextAutocorrectionType.No;
                input.AutocapitalizationType = UITextAutocapitalizationType.None;
                input.SpellCheckingType = UITextSpellCheckingType.No;
                input.Placeholder = request.IsNumeric ? "6 or 8 digits" : "Enter here";
            });
        }
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => completion.TrySetResult(null)));
        alert.AddAction(UIAlertAction.Create(request.AcceptText, UIAlertActionStyle.Default,
            _ => completion.TrySetResult(request.IsConfirmation ? "yes" : alert.TextFields?[0].Text ?? "")));
        _prompt = alert;
        _promptCompletion = completion;
        try
        {
            await GetPresenter().PresentViewControllerAsync(alert, true);
            var result = await completion.Task;
            // Finish dismissal before a follow-up prompt or file picker is presented.
            if (alert.PresentingViewController != null) await alert.DismissViewControllerAsync(true);
            return result;
        }
        finally
        {
            if (alert.TextFields is { Length: > 0 }) alert.TextFields[0].Text = "";
            if (ReferenceEquals(_prompt, alert)) { _prompt = null; _promptCompletion = null; }
        }
    }

    public void CancelPrompt()
    {
        _textImport?.Cancel();
        _passcodeInput?.Cancel();
        _prompt?.DismissViewController(false, null);
        _promptCompletion?.TrySetResult(null);
        _prompt = null;
        _promptCompletion = null;
    }

#if SDA_IOS_SIMULATOR
    internal bool SubmitTextForFunctionalCheck(string value) => _textImport?.SubmitForFunctionalCheck(value) == true;
    internal bool SubmitPinForFunctionalCheck(string value) => _passcodeInput?.SubmitForFunctionalCheck(value) == true;
    internal void SaveFunctionalScreenshot(string name)
    {
        var window = (UIApplication.SharedApplication.Delegate as AppDelegate)?.Window ?? throw new InvalidOperationException("No native window.");
        using var renderer = new UIGraphicsImageRenderer(window.Bounds.Size);
        using var picture = renderer.CreateImage(_ => window.DrawViewHierarchy(window.Bounds, true));
        using var png = picture.AsPNG();
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        Directory.CreateDirectory(directory);
        using var destination = NSUrl.FromFilename(Path.Combine(directory, name));
        if (png == null || !png.Save(destination, false)) throw new InvalidOperationException("Synthetic screenshot failed.");
    }
#endif

    public void CopyCode(string code)
    {
        using var text = new NSString(code);
        using var format = new NSString("public.utf8-plain-text");
        using var item = new NSDictionary<NSString, NSObject>(format, text);
        UIPasteboard.General.SetItems(new[] { item }, new UIPasteboardOptions
        {
            LocalOnly = true, ExpirationDate = NSDate.FromTimeIntervalSinceNow(30)
        });
    }

    public async Task<string?> ScanQrAsync()
    {
        if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video))
            throw new InvalidOperationException("Camera access is disabled. Enable it in Settings, or import an account file.");
        // Permission callbacks may arrive before UIKit reactivates the app. Returning
        // immediately here discarded the first scan and left the previous error visible.
        for (var attempt = 0; UIApplication.SharedApplication.ApplicationState != UIApplicationState.Active && attempt < 60; attempt++)
            await Task.Delay(50);
        if (UIApplication.SharedApplication.ApplicationState != UIApplicationState.Active)
            throw new InvalidOperationException("Return to SDA Mobile and tap Scan QR again.");
        var controller = GetPresenter();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scanner = new QrScannerController(completion);
        try
        {
            await controller.PresentViewControllerAsync(_scanner, true);
            return await completion.Task;
        }
        finally
        {
            _scanner?.Dispose();
            _scanner = null;
        }
    }

    public void CancelScan() => _scanner?.Cancel();
}
