using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SteamDesktopAuthenticator.Core;
using SteamDesktopAuthenticator.Services;
using SteamDesktopAuthenticator.ViewModels;
using SteamDesktopAuthenticator.Views;

namespace SteamDesktopAuthenticator
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                var dialogService = new AvaloniaDialogService(() => mainWindow);

                // Manifest.cs (Core) calls back into this whenever it needs a passkey prompt -
                // this is the seam that replaces the original's direct WinForms dialog creation.
                Manifest.Prompter = dialogService;

                var vm = new MainWindowViewModel(dialogService);
                mainWindow.DataContext = vm;
                mainWindow.AttachViewModel(vm);

                desktop.MainWindow = mainWindow;
                desktop.Exit += (_, _) => vm.Shutdown();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
