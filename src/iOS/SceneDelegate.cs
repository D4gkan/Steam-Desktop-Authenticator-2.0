using Foundation;
using UIKit;

namespace Sda.Mobile.iOS;

[Register("SdaSceneDelegate")]
[Preserve(AllMembers = true)]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    // Match the native iOS template: export protocol members from a concrete
    // responder instead of inheriting the generated protocol model class.
    [Export("window")]
    public UIWindow? Window { get; set; }
    private static AppDelegate ApplicationDelegate =>
        (AppDelegate)UIApplication.SharedApplication.Delegate;

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        Console.WriteLine("SDA startup: connecting the window scene.");
        if (scene is UIWindowScene windowScene) Window = ApplicationDelegate.ConnectWindow(windowScene);
    }

    [Export("sceneDidBecomeActive:")]
    public void DidBecomeActive(UIScene scene) => ApplicationDelegate.OnActivated(UIApplication.SharedApplication);

    [Export("sceneWillResignActive:")]
    public void WillResignActive(UIScene scene) => ApplicationDelegate.OnResignActivation(UIApplication.SharedApplication);

    [Export("sceneWillEnterForeground:")]
    public void WillEnterForeground(UIScene scene) => ApplicationDelegate.WillEnterForeground(UIApplication.SharedApplication);

    [Export("sceneDidEnterBackground:")]
    public void DidEnterBackground(UIScene scene) => ApplicationDelegate.DidEnterBackground(UIApplication.SharedApplication);
}
