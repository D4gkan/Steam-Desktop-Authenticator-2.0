using System;

namespace SteamDesktopAuthenticator.Core
{
    public class MaFileEncryptedException : Exception
    {
        public MaFileEncryptedException() : base("maFile is encrypted and could not be parsed without a passkey") { }
    }
}
