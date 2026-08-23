using Avalonia;
using System;
using System.Threading;

namespace SteamDesktopAuthenticator
{
    /// <summary>
    /// Ported from the original Program.cs: single-instance enforcement via a named Mutex,
    /// and parsing of command-line options (silent start, encryption key passed at launch).
    /// See CommandLineOptions.cs for the parity-ported option parser.
    /// </summary>
    internal static class Program
    {
        public const string MutexName = "SteamDesktopAuthenticator-{B1D0F4B6-90B2-4A44-9C60-2E44B5B7A111}";
        private static Mutex? _mutex;

        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");

            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running. Original SDA silently exits in this case
                // (it does not support multiple simultaneous instances because they'd race on maFiles).
                Console.Error.WriteLine("Steam Desktop Authenticator is already running.");
                return;
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                LogCrash(ex, "Main() try/catch");
                throw;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        private static void LogCrash(Exception? ex, string source)
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
                var text = $"[{DateTime.Now:O}] Unhandled exception from {source}:\n{ex}\n\n";
                System.IO.File.AppendAllText(path, text);
            }
            catch
            {
                // If we can't even write the crash log, there's nothing more we can do here.
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
