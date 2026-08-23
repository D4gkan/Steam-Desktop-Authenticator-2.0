using System;

namespace SteamDesktopAuthenticator.Core
{
    public class ManifestParseException : Exception
    {
        public ManifestParseException() : base("Failed to parse manifest.json") { }
    }
}
