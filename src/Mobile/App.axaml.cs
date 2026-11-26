using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Sda.Mobile;

public sealed partial class App : Application
{
    public static IMobilePlatform Platform { get; set; } = null!;
    public static MainViewModel? ViewModel { get; private set; }
    public static event Action<MainView>? ViewCreated;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var view = new MainView();
        ViewModel = new MainViewModel(Platform, request => Platform.PromptAsync(request, view.PromptAsync));
        view.DataContext = ViewModel;
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
            mobile.MainView = view;
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Avalonia.Controls.Window
            {
                Title = "SDA Mobile Preview", Width = 393, Height = 852, MinWidth = 340,
                Content = view, Background = Avalonia.Media.Brush.Parse("#07070C")
            };
        base.OnFrameworkInitializationCompleted();
        ViewCreated?.Invoke(view);
    }
}
