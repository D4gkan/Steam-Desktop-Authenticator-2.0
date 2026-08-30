using System;

namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>
    /// Abstraction over the operating system's native secure credential storage
    /// (Windows Credential Manager, macOS Keychain, Linux Secret Service).
    ///
    /// IMPORTANT: implementations of this interface are the ONLY place in the app that is
    /// allowed to persist a Steam account password to disk. The password is always handed to
    /// the OS-native secret store; SDA itself never writes it into manifest.json, ui-meta.json,
    /// a .maFile, or any log file. If no native secure store is available on the current
    /// platform, <see cref="IsSupported"/> is false and callers must refuse to save the
    /// password rather than falling back to an insecure location.
    /// </summary>
    public interface ICredentialStore
    {
        /// <summary>True if this store can actually be used on the current machine (the
        /// relevant OS service/binary is present and reachable). Checked lazily/cheaply -
        /// implementations should not throw from this property.</summary>
        bool IsSupported { get; }

        /// <summary>Human-readable name of the backing store, for status/error messages only
        /// (e.g. "Windows Credential Manager", "macOS Keychain", "Secret Service (libsecret)").
        /// Never includes any secret material.</summary>
        string DisplayName { get; }

        /// <summary>Optional, human-readable remediation text to append to the "could not save
        /// password" status/error message when <see cref="IsSupported"/> is false - e.g. what
        /// package to install and/or what service needs to be running. Empty string if there is
        /// nothing actionable to tell the user (e.g. platforms where the store is always
        /// available). Never includes any secret material.</summary>
        string UnavailableHint => string.Empty;

        /// <summary>Saves (or overwrites) the password for the given account key. Throws
        /// <see cref="CredentialStoreException"/> on failure.</summary>
        void SavePassword(string accountKey, string username, string password);

        /// <summary>Returns the stored password for the given account key, or null if none is
        /// stored. Throws <see cref="CredentialStoreException"/> only for unexpected OS-level
        /// failures - a simple "not found" returns null, it does not throw.</summary>
        string? TryGetPassword(string accountKey);

        /// <summary>Removes any stored password for the given account key. Safe to call even if
        /// nothing is currently stored (no-op in that case).</summary>
        void DeletePassword(string accountKey);
    }

    /// <summary>Thrown when a native credential-store operation fails unexpectedly. Never
    /// includes the password/secret value in its message.</summary>
    public class CredentialStoreException : Exception
    {
        public CredentialStoreException(string message) : base(message) { }
        public CredentialStoreException(string message, Exception inner) : base(message, inner) { }
    }
}
