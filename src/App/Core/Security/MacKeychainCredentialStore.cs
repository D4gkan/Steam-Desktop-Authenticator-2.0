using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>
    /// Stores passwords in the macOS login Keychain via the native Security.framework C API
    /// (SecKeychainAddGenericPassword / SecKeychainFindGenericPassword /
    /// SecKeychainItemModifyContent / SecKeychainItemDelete). No new NuGet dependency is
    /// required - these are plain P/Invokes into the OS-provided framework.
    ///
    /// Each account is stored as a "generic password" keychain item with
    /// service = "SteamDesktopAuthenticator", account = the SDA account key (Steam ID), which
    /// is exactly the same shape Keychain Access.app itself would show.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public class MacKeychainCredentialStore : ICredentialStore
    {
        private const string ServiceName = "SteamDesktopAuthenticator";
        private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
        private const int errSecSuccess = 0;
        private const int errSecItemNotFound = -25300;

        public bool IsSupported => OperatingSystem.IsMacOS();
        public string DisplayName => "macOS Keychain";

        [DllImport(SecurityLib)]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            uint passwordLength, byte[] passwordData,
            out IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            out uint passwordLength, out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemModifyContent(
            IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        public void SavePassword(string accountKey, string username, string password)
        {
            byte[] service = Encoding.UTF8.GetBytes(ServiceName);
            byte[] account = Encoding.UTF8.GetBytes(accountKey);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

            try
            {
                int status = SecKeychainFindGenericPassword(
                    IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account,
                    out _, out IntPtr existingDataPtr, out IntPtr existingItemRef);

                if (status == errSecSuccess)
                {
                    // Item already exists - overwrite its secret content in place.
                    SecKeychainItemFreeContent(IntPtr.Zero, existingDataPtr);
                    status = SecKeychainItemModifyContent(existingItemRef, IntPtr.Zero, (uint)passwordBytes.Length, passwordBytes);
                    if (status != errSecSuccess)
                        throw new CredentialStoreException($"SecKeychainItemModifyContent failed (OSStatus {status}).");
                    return;
                }

                if (status != errSecItemNotFound)
                    throw new CredentialStoreException($"SecKeychainFindGenericPassword failed (OSStatus {status}).");

                status = SecKeychainAddGenericPassword(
                    IntPtr.Zero,
                    (uint)service.Length, service,
                    (uint)account.Length, account,
                    (uint)passwordBytes.Length, passwordBytes,
                    out _);

                if (status != errSecSuccess)
                    throw new CredentialStoreException($"SecKeychainAddGenericPassword failed (OSStatus {status}).");
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        public string? TryGetPassword(string accountKey)
        {
            byte[] service = Encoding.UTF8.GetBytes(ServiceName);
            byte[] account = Encoding.UTF8.GetBytes(accountKey);

            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account,
                out uint passwordLength, out IntPtr passwordData, out _);

            if (status == errSecItemNotFound) return null;
            if (status != errSecSuccess)
                throw new CredentialStoreException($"SecKeychainFindGenericPassword failed (OSStatus {status}).");

            try
            {
                byte[] bytes = new byte[passwordLength];
                Marshal.Copy(passwordData, bytes, 0, (int)passwordLength);
                string password = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                return password;
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            }
        }

        public void DeletePassword(string accountKey)
        {
            byte[] service = Encoding.UTF8.GetBytes(ServiceName);
            byte[] account = Encoding.UTF8.GetBytes(accountKey);

            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account,
                out _, out IntPtr dataPtr, out IntPtr itemRef);

            if (status == errSecItemNotFound) return; // already gone - not an error
            if (status != errSecSuccess)
                throw new CredentialStoreException($"SecKeychainFindGenericPassword failed (OSStatus {status}).");

            SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);

            status = SecKeychainItemDelete(itemRef);
            if (status != errSecSuccess && status != errSecItemNotFound)
                throw new CredentialStoreException($"SecKeychainItemDelete failed (OSStatus {status}).");
        }
    }
}
