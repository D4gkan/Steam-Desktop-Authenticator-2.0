using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamDesktopAuthenticator.Core.Security
{
    /// <summary>
    /// Stores passwords in the Linux Secret Service (GNOME Keyring / KWallet / any other
    /// org.freedesktop.secrets provider) via the "secret-tool" command line utility that ships
    /// with libsecret-tools. This avoids hand-rolling the Secret Service D-Bus protocol (session
    /// negotiation, algorithm handshake, collection/item management) inside SDA, at the cost of
    /// depending on secret-tool being installed - which is the same approach used by several
    /// other cross-platform desktop apps (e.g. git-credential-libsecret, various browsers'
    /// helper scripts).
    ///
    /// If secret-tool is not on PATH (headless server, minimal distro, Secret Service not
    /// running), <see cref="IsSupported"/> is false and the caller must not offer to save the
    /// password anywhere else - there is no plaintext fallback.
    ///
    /// The password is always passed via stdin, never as a command-line argument, so it never
    /// appears in the process list (ps/proc) of other users on the system.
    /// </summary>
    public class LinuxSecretServiceCredentialStore : ICredentialStore
    {
        private const string ServiceAttrValue = "SteamDesktopAuthenticator";
        private bool? _supportedCache;

        public bool IsSupported
        {
            get
            {
                if (_supportedCache.HasValue) return _supportedCache.Value;
                _supportedCache = OperatingSystem.IsLinux() && ProbeSecretTool();
                return _supportedCache.Value;
            }
        }

        public string DisplayName => "Secret Service (libsecret / secret-tool)";

        public string UnavailableHint =>
            "Install libsecret-tools (Debian/Ubuntu: sudo apt install libsecret-tools; " +
            "Fedora: sudo dnf install libsecret gnome-keyring; " +
            "Arch: sudo pacman -S libsecret gnome-keyring) and make sure a keyring service " +
            "(e.g. gnome-keyring) is running, then restart the app.";

        private static bool ProbeSecretTool()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                });
                if (proc == null) return false;
                proc.WaitForExit(3000);
                return true;
            }
            catch
            {
                // secret-tool missing from PATH, or could not be spawned for any other reason.
                return false;
            }
        }

        private static string[] AttrArgs(string accountKey) =>
            new[] { "service", ServiceAttrValue, "account", accountKey };

        public void SavePassword(string accountKey, string username, string password)
        {
            var args = new System.Collections.Generic.List<string> { "store", "--label", $"Steam Desktop Authenticator ({username})" };
            args.AddRange(AttrArgs(accountKey));

            var (exitCode, _, stderr) = RunSecretTool(args, stdin: password);
            if (exitCode != 0)
                throw new CredentialStoreException($"secret-tool store failed (exit {exitCode}): {stderr}");
        }

        public string? TryGetPassword(string accountKey)
        {
            var args = new System.Collections.Generic.List<string> { "lookup" };
            args.AddRange(AttrArgs(accountKey));

            var (exitCode, stdout, _) = RunSecretTool(args, stdin: null);
            if (exitCode != 0) return null; // not found (or Secret Service locked/unavailable)
            return stdout.Length == 0 ? null : stdout;
        }

        public void DeletePassword(string accountKey)
        {
            var args = new System.Collections.Generic.List<string> { "clear" };
            args.AddRange(AttrArgs(accountKey));

            var (exitCode, _, stderr) = RunSecretTool(args, stdin: null);
            // secret-tool clear returns non-zero if nothing matched - that's fine, not an error
            // for our purposes (Task 1 requires DeletePassword to be a safe no-op in that case).
            _ = exitCode;
            _ = stderr;
        }

        private static (int ExitCode, string Stdout, string Stderr) RunSecretTool(
            System.Collections.Generic.List<string> args, string? stdin)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "secret-tool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi) ?? throw new CredentialStoreException("Failed to start secret-tool.");

            if (stdin != null)
            {
                proc.StandardInput.Write(stdin);
            }
            proc.StandardInput.Close();

            string stdout = proc.StandardOutput.ReadToEnd().TrimEnd('\n', '\r');
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);

            return (proc.ExitCode, stdout, stderr);
        }
    }
}
