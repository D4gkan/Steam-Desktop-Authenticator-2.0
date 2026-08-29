using System;
using System.IO;

namespace SteamDesktopAuthenticator.Core
{
    /// <summary>
    /// Minimal structured logger for authentication and confirmation events (Task 15).
    ///
    /// Hard rule: callers must never pass a password, session token, cookie, API key, or other
    /// credential into these methods - not even truncated/masked. Log calls in this codebase
    /// only ever include non-sensitive identifiers (Steam ID, confirmation type, exception
    /// messages from SteamAuth, which do not include secret material).
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();

        private static string LogPath => Path.Combine(AppContext.BaseDirectory, "logs", "sda.log");

        public static void Info(string category, string message) => Write("INFO", category, message);
        public static void Warn(string category, string message) => Write("WARN", category, message);
        public static void Error(string category, string message) => Write("ERROR", category, message);

        /// <summary>Redacts everything but the account's Steam ID for use in log lines, so log
        /// output can identify *which* account an event belongs to without ever needing to
        /// include the account name, email, or any credential.</summary>
        public static string AccountRef(ulong steamId) => $"steamid={steamId}";

        private static void Write(string level, string category, string message)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string line = $"[{DateTime.UtcNow:O}] [{level}] [{category}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
                // Logging must never crash the app or interrupt the operation being logged.
            }
        }
    }
}
