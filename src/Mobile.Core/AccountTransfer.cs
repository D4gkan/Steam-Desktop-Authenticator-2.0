using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using SteamAuth;
using SteamDesktopAuthenticator.Core;

namespace Sda.Mobile.Core;

public static class AccountTransfer
{
    public const int MaximumImportBytes = 1024 * 1024;
    public const string QrPrefix = "sda-mafile:v1:";

    public static string ReadFileText(byte[] bytes)
    {
        if (bytes.Length > MaximumImportBytes) throw new InvalidDataException("The account file is too large.");
        using var stream = new MemoryStream(bytes, false);
        // Match the desktop importer: recognize UTF-8, UTF-16, and UTF-32 BOMs.
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        try { return reader.ReadToEnd(); }
        catch (DecoderFallbackException) { throw new InvalidDataException("The file's text encoding is unsupported or damaged. Export it from SDA Desktop again."); }
    }

    public static SteamGuardAccount ReadQr(string payload, bool allowMissingSteamId = false)
    {
        var normalized = Normalize(payload);
        if (!normalized.StartsWith('{') && !normalized.StartsWith(QrPrefix, StringComparison.Ordinal))
            throw new InvalidDataException(normalized.StartsWith("sda-mafile:", StringComparison.Ordinal)
                ? "This SDA QR version is unsupported. Create a new transfer QR in SDA Desktop."
                : "This QR is not an SDA account transfer. Use the account menu’s Display QR Code in SDA Desktop.");
        return Read(normalized, allowMissingSteamId: allowMissingSteamId);
    }

    public static SteamGuardAccount ReadPasted(string payload, bool allowMissingSteamId = false)
    {
        var normalized = Normalize(payload);
        if (!normalized.StartsWith('{') && !normalized.StartsWith(QrPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Paste plaintext account JSON or an SDA transfer payload. For encrypted files, use Import file with the source manifest and encryption passkey.");
        return Read(normalized, allowMissingSteamId: allowMissingSteamId);
    }

    public static SteamGuardAccount Read(string payload, VaultEntry? entry = null, string? passkey = null, bool allowMissingSteamId = false)
    {
        if (Encoding.UTF8.GetByteCount(payload) > MaximumImportBytes)
            throw new InvalidDataException("The account file is too large.");
        payload = Normalize(payload);
        if (payload.StartsWith(QrPrefix, StringComparison.Ordinal))
        {
            var encoded = payload[QrPrefix.Length..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var input = new MemoryStream(Convert.FromBase64String(encoded));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = gzip.Read(buffer)) != 0)
            {
                if (output.Length + read > MaximumImportBytes)
                    throw new InvalidDataException("The QR payload is too large.");
                output.Write(buffer, 0, read);
            }
            payload = Normalize(ReadFileText(output.ToArray()));
            if (!payload.StartsWith('{'))
                throw new InvalidDataException("The SDA QR decoded successfully but does not contain an account JSON document.");
        }
        if (!payload.StartsWith('{'))
        {
            if (entry?.Salt == null || entry.IV == null || string.IsNullOrEmpty(passkey))
                throw new InvalidDataException("For an encrypted maFile, also select its manifest.json and enter its passkey.");
            payload = FileEncryptor.DecryptData(passkey, entry.Salt, entry.IV, payload)
                ?? throw new InvalidDataException("The passkey is incorrect or the file is damaged.");
        }
        var account = AccountJson.Read(payload);
        Validate(account, allowMissingSteamId);
        return account;
    }

    private static string Normalize(string payload) => payload.Trim().TrimStart('\uFEFF').Trim();

    public static void Validate(SteamGuardAccount account, bool allowMissingSteamId = false)
    {
        if (account.Session == null || (!allowMissingSteamId && account.Session.SteamID == 0))
            throw new InvalidDataException("The account is missing its Steam ID.");
        if (string.IsNullOrWhiteSpace(account.AccountName))
            throw new InvalidDataException("The account is missing its name.");
        if (!IsSecret(account.SharedSecret))
            throw new InvalidDataException("The Steam Guard secret must contain 20 bytes.");
        if (!string.IsNullOrEmpty(account.IdentitySecret) && !IsSecret(account.IdentitySecret))
            throw new InvalidDataException("The confirmation secret must contain 20 bytes.");
    }

    private static bool IsSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        Span<byte> bytes = stackalloc byte[20];
        var valid = Convert.TryFromBase64String(value, bytes, out var length) && length == 20;
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        return valid;
    }

    public static string CreateQrPayload(SteamGuardAccount account)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            var bytes = Encoding.UTF8.GetBytes(AccountJson.Write(account, includeSessionCredentials: false));
            gzip.Write(bytes);
        }
        return QrPrefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] CreateBackup(IEnumerable<SteamGuardAccount> accounts, string passkey)
    {
        if (string.IsNullOrEmpty(passkey)) throw new InvalidOperationException("Enter a password for the backup.");
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var manifest = new VaultManifest();
            foreach (var account in accounts)
            {
                Validate(account);
                var entry = new VaultEntry
                {
                    Filename = account.Session.SteamID + ".maFile", SteamID = account.Session.SteamID,
                    Salt = FileEncryptor.GetRandomSalt(), IV = FileEncryptor.GetInitializationVector()
                };
                manifest.Entries.Add(entry);
                using var writer = new StreamWriter(archive.CreateEntry(entry.Filename).Open(), new UTF8Encoding(false));
                writer.Write(FileEncryptor.EncryptData(passkey, entry.Salt, entry.IV, AccountJson.Write(account)));
            }
            using var manifestWriter = new StreamWriter(archive.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
            manifestWriter.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }
        return output.ToArray();
    }

    public static IReadOnlyList<SteamGuardAccount> ReadBackup(Stream stream, string passkey)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > 1001) throw new InvalidDataException("The backup contains too many files.");
        long expandedBytes = 0;
        foreach (var file in archive.Entries)
        {
            if (file.Length > MaximumImportBytes || file.Length > 32 * 1024 * 1024 - expandedBytes)
                throw new InvalidDataException("The expanded backup is too large. Import fewer accounts at a time.");
            expandedBytes += file.Length;
        }
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("The backup has no manifest.json.");
        var manifest = AccountVault.ReadImportManifest(ReadZipText(manifestEntry));
        return manifest.Entries.Select(entry =>
        {
            if (string.IsNullOrEmpty(entry.Filename) || entry.Filename.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
                throw new InvalidDataException("The backup contains an unsafe account filename.");
            var file = archive.GetEntry(entry.Filename) ?? throw new InvalidDataException("An account file is missing from the backup.");
            var account = Read(ReadZipText(file), manifest.Encrypted ? entry : null, passkey);
            if (account.Session.SteamID != entry.SteamID) throw new InvalidDataException("An account does not match its manifest entry.");
            return account;
        }).ToArray();
    }

    private static string ReadZipText(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumImportBytes) throw new InvalidDataException("A file in the backup is too large.");
        using var source = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = source.Read(buffer)) != 0)
        {
            if (output.Length + count > MaximumImportBytes) throw new InvalidDataException("A file in the backup is too large.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
