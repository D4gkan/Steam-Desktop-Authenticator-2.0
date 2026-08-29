using System;
namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>Picks the right native credential store for the OS SDA is currently running on.
    /// A single instance is reused for the process lifetime.</summary>
    public static class CredentialStoreFactory
    {
        private static ICredentialStore? _instance;

        public static ICredentialStore Get()
        {
            if (_instance != null) return _instance;

            if (OperatingSystem.IsWindows())
                _instance = new WindowsCredentialStore();
            else if (OperatingSystem.IsMacOS())
                _instance = new MacKeychainCredentialStore();
            else
                _instance = new LinuxSecretServiceCredentialStore();

            return _instance;
        }
    }
}
