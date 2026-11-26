using Foundation;
using UIKit;

namespace Sda.Mobile.iOS;

[Register("SdaPasscodeController")]
[Preserve(AllMembers = true)]
public sealed class PasscodeController : UIViewController
{
    private readonly PromptRequest _request;
    private readonly TaskCompletionSource<string?> _completion;
    private readonly List<UILabel> _dots = new();
    private UITextField? _input;
    private UIStackView? _slots;
    private UILabel? _error;
    private UIButton? _legacy;
    private int _length;
    private bool _finished;
    private bool _submitting;

    public PasscodeController(PromptRequest request, TaskCompletionSource<string?> completion)
    {
        _request = request; _completion = completion; _length = request.PinLength;
        ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        ModalTransitionStyle = request.CanCancel ? UIModalTransitionStyle.CoverVertical : UIModalTransitionStyle.CrossDissolve;
        ModalInPresentation = true;
        OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View!;
        view.BackgroundColor = UIColor.FromRGB(9, 9, 15);
        view.TintColor = UIColor.FromRGB(174, 154, 221);
        var cancel = new UIButton(UIButtonType.System);
        cancel.SetTitle("Cancel", UIControlState.Normal);
        cancel.Hidden = !_request.CanCancel;
        cancel.TouchUpInside += (_, _) => Cancel();
        var title = new UILabel { Text = _request.Title, Font = UIFont.SystemFontOfSize(24, UIFontWeight.Medium)!,
            TextColor = UIColor.FromRGB(232, 230, 239), TextAlignment = UITextAlignment.Center };
        _slots = new UIStackView { Axis = UILayoutConstraintAxis.Horizontal, Distribution = UIStackViewDistribution.FillEqually, Spacing = 10 };
        _error = new UILabel { Lines = 0, TextAlignment = UITextAlignment.Center, TextColor = UIColor.FromRGB(198, 160, 176), Font = UIFont.SystemFontOfSize(13)!, Hidden = true };
        _legacy = new UIButton(UIButtonType.System) { Hidden = !_request.AllowLegacyPinLength };
        _legacy.SetTitle("Use 8 digits", UIControlState.Normal);
        _legacy.TouchUpInside += (_, _) =>
        {
            if (_submitting) return;
            _length = _length == 6 ? 8 : 6;
            _input!.Text = "";
            _legacy.SetTitle(_length == 6 ? "Use 8 digits" : "Use 6 digits", UIControlState.Normal);
            BuildSlots();
        };
        var stack = new UIStackView(new UIView[] { title, _slots, _error, _legacy }) { Axis = UILayoutConstraintAxis.Vertical, Spacing = 28 };
        var body = new UIView();
        _input = new UITextField
        {
            SecureTextEntry = true, KeyboardType = UIKeyboardType.NumberPad,
            AutocorrectionType = UITextAutocorrectionType.No, SpellCheckingType = UITextSpellCheckingType.No,
            TextColor = UIColor.Clear, TintColor = UIColor.Clear, BackgroundColor = UIColor.Clear,
            AccessibilityLabel = "Passcode", Font = UIFont.SystemFontOfSize(1)
        };
        _input.EditingChanged += async (_, _) => await ReadInputAsync();
        foreach (var child in new UIView[] { cancel, body, _input }) { child.TranslatesAutoresizingMaskIntoConstraints = false; view.AddSubview(child); }
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        body.AddSubview(stack);
        var safe = view.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            cancel.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 8), cancel.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 24), cancel.HeightAnchor.ConstraintEqualTo(44),
            body.TopAnchor.ConstraintEqualTo(cancel.BottomAnchor, 8), body.BottomAnchor.ConstraintEqualTo(view.KeyboardLayoutGuide.TopAnchor, -20),
            body.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 24), body.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -24),
            stack.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor), stack.TrailingAnchor.ConstraintEqualTo(body.TrailingAnchor), stack.CenterYAnchor.ConstraintEqualTo(body.CenterYAnchor, -24),
            _slots.HeightAnchor.ConstraintEqualTo(52),
            _input.WidthAnchor.ConstraintEqualTo(1), _input.HeightAnchor.ConstraintEqualTo(1), _input.TopAnchor.ConstraintEqualTo(safe.TopAnchor), _input.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor)
        });
        BuildSlots();
    }

    private void BuildSlots()
    {
        if (_slots == null) return;
        foreach (var child in _slots.ArrangedSubviews) { _slots.RemoveArrangedSubview(child); child.RemoveFromSuperview(); child.Dispose(); }
        _dots.Clear();
        for (var index = 0; index < _length; index++)
        {
            var dot = new UILabel { TextAlignment = UITextAlignment.Center, Font = UIFont.SystemFontOfSize(20)!, TextColor = UIColor.FromRGB(211, 201, 231) };
            dot.Layer.CornerRadius = 10; dot.Layer.BorderWidth = 1; dot.Layer.BorderColor = UIColor.FromRGB(44, 40, 55).CGColor;
            _dots.Add(dot); _slots.AddArrangedSubview(dot);
        }
        UpdateDots();
    }

    private void UpdateDots()
    { for (var index = 0; index < _dots.Count; index++) _dots[index].Text = index < (_input?.Text?.Length ?? 0) ? "●" : ""; }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        _input?.BecomeFirstResponder();
        Console.WriteLine("SDA startup: passcode presented.");
#if SDA_IOS_SIMULATOR
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "startup-ready.txt"), "passcode-presented:" + Environment.ProcessId);
#endif
    }

    private async Task ReadInputAsync()
    {
        if (_finished || _submitting || _input == null) return;
        var pin = new string((_input.Text ?? "").Where(character => character is >= '0' and <= '9').Take(_length).ToArray());
        _input.Text = pin;
        UpdateDots();
        if (pin.Length != _length) return;
        _submitting = true;
        try
        {
            var error = _request.ValidatePinAsync == null ? null : await _request.ValidatePinAsync(pin);
            if (_finished) return;
            if (error == null) Finish(pin);
            else ShowError(error);
        }
        catch (OperationCanceledException) { Cancel(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { ShowError(error.Message); }
        catch (Exception) { ShowError("Could not unlock. Try again."); }
        finally { _submitting = false; }
    }

    private void ShowError(string message)
    {
        if (_finished) return;
        _error!.Text = message; _error.Hidden = false;
        _input!.Text = ""; UpdateDots(); _input.BecomeFirstResponder();
    }

    public void Cancel() => Finish(null);
    private async void Finish(string? pin)
    {
        if (_finished) return;
        _finished = true;
        View?.EndEditing(true);
        if (_input != null) _input.Text = "";
        foreach (var dot in _dots) dot.Text = "";
        await DismissViewControllerAsync(true);
        _completion.TrySetResult(pin);
    }

#if SDA_IOS_SIMULATOR
    internal bool SubmitForFunctionalCheck(string pin)
    {
        if (!IsViewLoaded || View?.Window == null || IsBeingPresented || _input == null) return false;
        _input.Text = pin[..3]; UpdateDots();
        _error!.Hidden = true;
        using var renderer = new UIGraphicsImageRenderer(View.Window.Bounds.Size);
        using var picture = renderer.CreateImage(_ => View.Window.DrawViewHierarchy(View.Window.Bounds, true));
        using var png = picture.AsPNG();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        Directory.CreateDirectory(documents);
        using var destination = NSUrl.FromFilename(Path.Combine(documents, "native-passcode.png"));
        if (png == null || !png.Save(destination, false)) throw new InvalidOperationException("Passcode screenshot failed.");
        _input.Text = pin;
        _ = ReadInputAsync();
        return true;
    }
#endif
}
