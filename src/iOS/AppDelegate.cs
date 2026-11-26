using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace Sda.Mobile.iOS;

[Register(AppDelegate.NativeClassName)]
[Preserve(AllMembers = true)]
public class AppDelegate : UIApplicationDelegate, IAvaloniaAppDelegate
{
    public const string NativeClassName = "SdaMobileAppDelegate";
    private UIView? _privacyCover;
    private AvaloniaView? _view;
    private HostViewController? _hostController;
    public override UIWindow? Window { get; set; }
    public event EventHandler<ActivatedEventArgs>? Activated;
    public event EventHandler<ActivatedEventArgs>? Deactivated;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Console.WriteLine("SDA startup: initializing Avalonia.");
        var platform = new IosPlatform();
        App.Platform = platform;
#if SDA_IOS_SIMULATOR
        if (NSProcessInfo.ProcessInfo.Arguments.Contains("--sda-functional-check"))
            App.Platform = new IosFunctionalCheckPlatform((IosPlatform)App.Platform);
#endif
        // UIKit must construct a concrete, non-generic delegate. Host Avalonia through
        // its public embedding API instead of inheriting its generic native delegate.
        MainView? content = null;
        void CaptureView(MainView view) => content = view;
        App.ViewCreated += CaptureView;
        try
        {
            AppBuilder.Configure<App>().UseiOS(this).WithInterFont().SetupWithoutStarting();
        }
        finally
        {
            App.ViewCreated -= CaptureView;
        }
        Console.WriteLine("SDA startup: creating the native host.");
        var bounds = UIScreen.MainScreen.Bounds;
        _view = new AvaloniaView
        {
            Frame = bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };
        _hostController = new HostViewController { View = _view, Appeared = platform.MarkHostReady };
        _view.InitWithController(_hostController);
        _view.Content = content ?? throw new InvalidOperationException("The main view was not created.");
        return true;
    }

    internal UIWindow ConnectWindow(UIWindowScene scene)
    {
        if (_view == null || _hostController == null) throw new InvalidOperationException("The native host is not initialized.");
        Window = new UIWindow(scene) { RootViewController = _hostController };
        _view.Frame = Window.Bounds;
        Window.MakeKeyAndVisible();
        _view.SetNeedsLayout();
        _view.LayoutIfNeeded();
        Console.WriteLine("SDA startup: native host presented.");
#if SDA_IOS_SIMULATOR
        if (App.Platform is IosFunctionalCheckPlatform check)
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await check.RunAsync());
#endif
        return Window;
    }

    public override void OnResignActivation(UIApplication application)
    {
        // Cover the native window before iOS takes its app-switcher snapshot. This also
        // covers the camera/prompt while permissions, calls, and other system UI appear.
        if (Window != null && _privacyCover == null)
        {
            _privacyCover = new UIView(Window.Bounds)
            {
                BackgroundColor = UIColor.FromRGB(7, 7, 12),
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
            };
            var title = new UILabel(_privacyCover.Bounds)
            {
                Text = "SDA Mobile", TextColor = UIColor.White, TextAlignment = UITextAlignment.Center,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
            };
            _privacyCover.AddSubview(title);
            Window.AddSubview(_privacyCover);
        }
    }

    public override void DidEnterBackground(UIApplication application)
    {
        (App.Platform as IosPlatform)?.CancelScan();
        App.ViewModel?.SetActive(false);
        Deactivated?.Invoke(this, new ActivatedEventArgs(ActivationKind.Background));
    }

    public override void WillEnterForeground(UIApplication application)
        => Activated?.Invoke(this, new ActivatedEventArgs(ActivationKind.Background));

    public override void OnActivated(UIApplication application)
    {
        App.ViewModel?.SetActive(true);
        _privacyCover?.RemoveFromSuperview();
        _privacyCover?.Dispose();
        _privacyCover = null;
    }
}
