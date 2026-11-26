using Foundation;
using UIKit;

namespace Sda.Mobile.iOS;

[Register("SdaTextImportController")]
[Preserve(AllMembers = true)]
public sealed class TextImportController : UIViewController
{
    private readonly PromptRequest _request;
    private readonly TaskCompletionSource<string?> _completion;
    private UITextView? _editor;
    private UILabel? _error;
    private bool _finished;

    public TextImportController(PromptRequest request, TaskCompletionSource<string?> completion)
    {
        _request = request;
        _completion = completion;
        ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        ModalInPresentation = true;
        OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View!;
        view.BackgroundColor = UIColor.FromRGB(9, 9, 15);
        view.TintColor = UIColor.FromRGB(174, 154, 221);
        var title = new UILabel
        {
            Text = _request.Title, TextColor = UIColor.FromRGB(232, 230, 239),
            Font = UIFont.BoldSystemFontOfSize(24)!, Lines = 0
        };
        var message = new UILabel
        {
            Text = _request.Message, TextColor = UIColor.FromRGB(153, 149, 167),
            Font = UIFont.SystemFontOfSize(13)!, Lines = 0
        };
        _editor = new UITextView
        {
            Editable = true, Selectable = true,
            TextColor = UIColor.FromRGB(218, 213, 229), BackgroundColor = UIColor.FromRGB(16, 16, 21),
            Font = UIFont.FromName("Menlo-Regular", 13) ?? UIFont.SystemFontOfSize(13)!,
            AutocapitalizationType = UITextAutocapitalizationType.None,
            AutocorrectionType = UITextAutocorrectionType.No, SpellCheckingType = UITextSpellCheckingType.No,
            PasteConfiguration = new UIPasteConfiguration(new[] { "public.utf8-plain-text", "public.text" }),
            TextContainerInset = new UIEdgeInsets(12, 12, 12, 12), AccessibilityLabel = "Account JSON or SDA QR payload"
        };
        _editor.Layer.CornerRadius = 12;
        _editor.Layer.BorderWidth = 1;
        _editor.Layer.BorderColor = UIColor.FromRGB(44, 40, 55).CGColor;

        // User-authorized native paste avoids UIPasteboard's programmatic-read prompt.
        // UITextView preserves multiline JSON and provides the system editing menu.
        using var pasteConfiguration = new UIPasteControlConfiguration
        {
            BaseBackgroundColor = UIColor.FromRGB(104, 85, 189),
            BaseForegroundColor = UIColor.White
        };
        if (!OperatingSystem.IsMacCatalyst()) pasteConfiguration.DisplayMode = UIPasteControlDisplayMode.IconAndLabel;
        var paste = new UIPasteControl(pasteConfiguration) { Target = _editor };
        _error = new UILabel { TextColor = UIColor.FromRGB(211, 172, 191), Font = UIFont.SystemFontOfSize(12)!, Lines = 0 };
        var cancel = new UIButton(UIButtonType.System);
        cancel.SetTitle("Cancel", UIControlState.Normal);
        cancel.SetTitleColor(UIColor.FromRGB(214, 210, 227), UIControlState.Normal);
        cancel.BackgroundColor = UIColor.FromRGB(24, 23, 31);
        cancel.Layer.CornerRadius = 12;
        cancel.TouchUpInside += (_, _) => Cancel();
        var next = new UIButton(UIButtonType.System);
        next.SetTitle(_request.AcceptText, UIControlState.Normal);
        next.SetTitleColor(UIColor.White, UIControlState.Normal);
        next.BackgroundColor = UIColor.FromRGB(104, 85, 189);
        next.Layer.CornerRadius = 12;
        next.TouchUpInside += (_, _) => Submit();
        var actions = new UIStackView(new UIView[] { cancel, next })
        {
            Axis = UILayoutConstraintAxis.Horizontal, Distribution = UIStackViewDistribution.FillEqually, Spacing = 12
        };
        foreach (var child in new UIView[] { title, message, paste, _editor, _error, actions })
        {
            child.TranslatesAutoresizingMaskIntoConstraints = false;
            view.AddSubview(child);
        }
        var safe = view.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            title.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 20), title.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 20), title.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -20),
            message.TopAnchor.ConstraintEqualTo(title.BottomAnchor, 12), message.LeadingAnchor.ConstraintEqualTo(title.LeadingAnchor), message.TrailingAnchor.ConstraintEqualTo(title.TrailingAnchor),
            paste.TopAnchor.ConstraintEqualTo(message.BottomAnchor, 16), paste.LeadingAnchor.ConstraintEqualTo(title.LeadingAnchor), paste.HeightAnchor.ConstraintEqualTo(44),
            _editor.TopAnchor.ConstraintEqualTo(paste.BottomAnchor, 16), _editor.LeadingAnchor.ConstraintEqualTo(title.LeadingAnchor), _editor.TrailingAnchor.ConstraintEqualTo(title.TrailingAnchor),
            _error.TopAnchor.ConstraintEqualTo(_editor.BottomAnchor, 8), _error.LeadingAnchor.ConstraintEqualTo(title.LeadingAnchor), _error.TrailingAnchor.ConstraintEqualTo(title.TrailingAnchor),
            actions.TopAnchor.ConstraintEqualTo(_error.BottomAnchor, 12), actions.LeadingAnchor.ConstraintEqualTo(title.LeadingAnchor), actions.TrailingAnchor.ConstraintEqualTo(title.TrailingAnchor), actions.HeightAnchor.ConstraintEqualTo(48),
            actions.BottomAnchor.ConstraintEqualTo(view.KeyboardLayoutGuide.TopAnchor, -16)
        });
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        _editor?.BecomeFirstResponder();
    }

    private void Submit()
    {
        var value = _editor?.Text;
        if (string.IsNullOrWhiteSpace(value)) { _error!.Text = "Tap Paste above or enter your account data first."; return; }
        if (System.Text.Encoding.UTF8.GetByteCount(value) > Core.AccountTransfer.MaximumImportBytes)
        {
            _error!.Text = "The account data is too large. Import a single account at a time.";
            return;
        }
        Finish(value);
    }

    public void Cancel() => Finish(null);

#if SDA_IOS_SIMULATOR
    internal bool SubmitForFunctionalCheck(string value)
    {
        if (!IsViewLoaded || View?.Window == null || IsBeingPresented || _editor == null) return false;
        _editor.Text = value;
        var window = View.Window;
        using var renderer = new UIGraphicsImageRenderer(window.Bounds.Size);
        using var picture = renderer.CreateImage(_ => window.DrawViewHierarchy(window.Bounds, true));
        using var png = picture.AsPNG();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        Directory.CreateDirectory(documents);
        using var destination = NSUrl.FromFilename(Path.Combine(documents, "native-paste-editor.png"));
        if (png == null || !png.Save(destination, false))
            throw new InvalidOperationException("The synthetic native editor screenshot could not be saved.");
        Submit();
        return true;
    }
#endif

    private async void Finish(string? value)
    {
        if (_finished) return;
        _finished = true;
        View?.EndEditing(true);
        if (_editor != null) _editor.Text = "";
        await DismissViewControllerAsync(true);
        _completion.TrySetResult(value);
    }
}
