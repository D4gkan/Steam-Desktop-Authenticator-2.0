using Avalonia.iOS;
using Foundation;

namespace Sda.Mobile.iOS;

[Register("SdaHostViewController")]
[Preserve(AllMembers = true)]
public sealed class HostViewController : DefaultAvaloniaViewController
{
    public Action? Appeared { get; set; }
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        Appeared?.Invoke();
    }
}
