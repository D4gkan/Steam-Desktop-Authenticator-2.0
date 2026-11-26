using System.Diagnostics.CodeAnalysis;
using UIKit;

namespace Sda.Mobile.iOS;

public static class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AppDelegate))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SceneDelegate))]
    public static void Main(string[] args)
    {
        Console.WriteLine("SDA bootstrap: selecting the native application delegate.");
        // Use the registered Objective-C name explicitly. The Type overload resolved
        // a base delegate in the affected build, leaving our native callbacks unused.
#pragma warning disable CS0618 // This overload intentionally selects the exact native class.
        UIApplication.Main(args, null, AppDelegate.NativeClassName);
#pragma warning restore CS0618
    }
}
