using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>
    /// Stores passwords in Windows Credential Manager via the native CredWrite/CredRead/
    /// CredDelete Win32 APIs (advapi32.dll). Credentials are written with
    /// CRED_PERSIST_LOCAL_MACHINE so they survive reboots but are only readable by processes
    /// running as the current Windows user - the OS itself enforces this, SDA does not
    /// implement its own encryption on top of it.
    ///
    /// Each account gets its own credential entry, target-named
    /// "SteamDesktopAuthenticator:{accountKey}" so multiple linked Steam accounts do not collide.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsCredentialStore : ICredentialStore
    {
        private const string TargetPrefix = "SteamDesktopAuthenticator:";
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;
        private const int ERROR_NOT_FOUND = 1168;

        public bool IsSupported => OperatingSystem.IsWindows();
        public string DisplayName => "Windows Credential Manager";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWriteW([In] ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDeleteW(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr cred);

        private static string BuildTarget(string accountKey) => TargetPrefix + accountKey;

        public void SavePassword(string accountKey, string username, string password)
        {
            byte[] passwordBytes = System.Text.Encoding.Unicode.GetBytes(password);
            IntPtr blobPtr = Marshal.AllocHGlobal(passwordBytes.Length);
            try
            {
                Marshal.Copy(passwordBytes, 0, blobPtr, passwordBytes.Length);

                var credential = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = BuildTarget(accountKey),
                    Comment = "Steam Desktop Authenticator saved login",
                    CredentialBlobSize = passwordBytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = username,
                };

                if (!CredWriteW(ref credential, 0))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new CredentialStoreException($"CredWrite failed (Win32 error {err}).");
                }
            }
            finally
            {
                // Zero the plaintext copy before freeing, and always clear the managed byte[]
                // reference too - best-effort, .NET does not guarantee immediate GC of it.
                ZeroMemory(blobPtr, passwordBytes.Length);
                Marshal.FreeHGlobal(blobPtr);
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        public string? TryGetPassword(string accountKey)
        {
            if (!CredReadW(BuildTarget(accountKey), CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_NOT_FOUND) return null;
                throw new CredentialStoreException($"CredRead failed (Win32 error {err}).");
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;

                byte[] bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, credential.CredentialBlobSize);
                string password = System.Text.Encoding.Unicode.GetString(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                return password;
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public void DeletePassword(string accountKey)
        {
            if (!CredDeleteW(BuildTarget(accountKey), CRED_TYPE_GENERIC, 0))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_NOT_FOUND) return; // already gone - not an error
                throw new CredentialStoreException($"CredDelete failed (Win32 error {err}).");
            }
        }

        private static void ZeroMemory(IntPtr ptr, int length)
        {
            for (int i = 0; i < length; i++)
            {
                Marshal.WriteByte(ptr, i, 0);
            }
        }
    }
}
