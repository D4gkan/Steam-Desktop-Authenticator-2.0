using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sda.Mobile.Core;
using SteamAuth;
using SteamDesktopAuthenticator.Core;
using Xunit;

namespace Sda.Mobile.Tests;

public sealed class AccountVaultTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sda-vault-tests", Guid.NewGuid().ToString("N"));
    private const string TestKey = "synthetic vault test key - not an account credential";

    internal static SteamGuardAccount SyntheticAccount() => new()
    {
        AccountName = "synthetic-test-account", SharedSecret = Convert.ToBase64String(Enumerable.Range(0, 20).Select(i => (byte)i).ToArray()),
        IdentitySecret = Convert.ToBase64String(Enumerable.Range(20, 20).Select(i => (byte)i).ToArray()),
        DeviceID = "android:00000000-0000-0000-0000-000000000000", RevocationCode = "SYNTHETIC", FullyEnrolled = true,
        Session = new SessionData { SteamID = 76561198000000000, AccessToken = "synthetic-access", RefreshToken = "synthetic-refresh", SessionID = "synthetic-session" }
    };

    internal static string DesktopFixture()
    {
        using var stream = typeof(AccountVaultTests).Assembly.GetManifestResourceStream("Sda.Tests.DesktopAccount.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void ExternallyAuthoredDesktopJsonPreservesEveryFieldAcrossVaultAndBackup()
    {
        var text = DesktopFixture();
        var expected = JsonConvert.DeserializeObject<SteamGuardAccount>(text)!;
        var account = AccountTransfer.Read(text);
        Assert.True(JToken.DeepEquals(JObject.FromObject(expected), JObject.FromObject(account)));
        var vault = new AccountVault(_directory, TestKey);
        vault.Save(account);
        Assert.True(JToken.DeepEquals(JObject.FromObject(expected), JObject.FromObject(vault.Load().Single())));
        using var backup = new MemoryStream(AccountTransfer.CreateBackup(new[] { account }, TestKey));
        Assert.True(JToken.DeepEquals(JObject.FromObject(expected), JObject.FromObject(AccountTransfer.ReadBackup(backup, TestKey).Single())));
        var qr = AccountTransfer.ReadQr(AccountTransfer.CreateQrPayload(account));
        Assert.Equal(expected.SharedSecret, qr.SharedSecret);
        Assert.Equal(expected.Secret1, qr.Secret1);
        Assert.Equal(expected.Session.SteamID, qr.Session.SteamID);
        Assert.Null(qr.Session.AccessToken);
        Assert.Null(qr.Session.RefreshToken);
    }

    [Fact]
    public void AccountMappingAcceptsLegacyCasingAndNumericStringsWithoutUsingDeviceCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            var json = JObject.Parse(DesktopFixture());
            json["session"] = json["Session"];
            json.Remove("Session");
            json["session"]!["SteamID"] = "76561198000000000";
            json["server_time"] = "1700000000";
            json["status"] = "1";
            var account = AccountTransfer.Read(json.ToString());
            Assert.Equal(76561198000000000UL, account.Session.SteamID);
            Assert.Equal(1700000000L, account.ServerTime);
            Assert.Equal(1, account.Status);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void MalformedAndWrongTypeErrorsIdentifyTheStageWithoutExposingInputValues()
    {
        const string privateMarker = "synthetic-private-marker";
        var malformed = Assert.Throws<InvalidDataException>(() => AccountTransfer.Read("{\"shared_secret\":\"" + privateMarker));
        Assert.Contains("I01", malformed.Message);
        Assert.DoesNotContain(privateMarker, malformed.Message);
        var json = JObject.Parse(DesktopFixture());
        json["server_time"] = privateMarker;
        var wrongType = Assert.Throws<InvalidDataException>(() => AccountTransfer.Read(json.ToString()));
        Assert.Contains("server_time", wrongType.Message);
        Assert.Contains("I03", wrongType.Message);
        Assert.DoesNotContain(privateMarker, wrongType.Message);
        json = JObject.Parse(DesktopFixture());
        json["shared_secret"] = privateMarker;
        var secret = Assert.Throws<InvalidDataException>(() => AccountTransfer.Read(json.ToString()));
        Assert.Contains("Steam Guard secret", secret.Message);
        Assert.DoesNotContain(privateMarker, secret.Message);
    }

    [Fact]
    public void AcceleratedEncryptionMatchesLegacyPbkdf2ByteForByte()
    {
        const string password = "synthetic compatibility pässword";
        const string plaintext = "synthetic account plaintext";
        var salt = Enumerable.Range(1, 8).Select(value => (byte)value).ToArray();
        var iv = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        using var legacy = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 50_000, System.Security.Cryptography.HashAlgorithmName.SHA1);
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = legacy.GetBytes(32);
        aes.IV = iv;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        using var encryptor = aes.CreateEncryptor();
        var expected = Convert.ToBase64String(encryptor.TransformFinalBlock(bytes, 0, bytes.Length));
        Assert.Equal(expected, FileEncryptor.EncryptData(password, Convert.ToBase64String(salt), Convert.ToBase64String(iv), plaintext));
        Assert.Equal(plaintext, FileEncryptor.DecryptData(password, Convert.ToBase64String(salt), Convert.ToBase64String(iv), expected));
    }

    [Fact]
    public void VaultUsesDesktopManifestAndEncryptionFormats()
    {
        var account = SyntheticAccount();
        var vault = new AccountVault(_directory, TestKey);
        vault.Save(account);
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(_directory, "manifest.json")));
        Assert.True((bool)manifest["encrypted"]!);
        var entry = manifest["entries"]![0]!;
        var ciphertext = File.ReadAllText(Path.Combine(_directory, (string)entry["filename"]!));
        Assert.DoesNotContain(account.SharedSecret, ciphertext);
        var plaintext = FileEncryptor.DecryptData(TestKey, (string)entry["encryption_salt"]!, (string)entry["encryption_iv"]!, ciphertext);
        var desktopAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(plaintext!)!;
        Assert.Equal(account.Session.SteamID, desktopAccount.Session.SteamID);
        Assert.Equal(account.GenerateSteamGuardCodeForTime(1234567890), desktopAccount.GenerateSteamGuardCodeForTime(1234567890));
        Assert.Equal(account.IdentitySecret, new AccountVault(_directory, TestKey).Load().Single().IdentitySecret);
    }

    [Fact]
    public void FailedManifestCommitPreservesPreviousGeneration()
    {
        var vault = new AccountVault(_directory, TestKey);
        var account = SyntheticAccount();
        vault.Save(account);
        Directory.CreateDirectory(Path.Combine(_directory, "manifest.json.tmp"));
        account.AccountName = "replacement";
        Assert.ThrowsAny<Exception>(() => vault.Save(account));
        Assert.Equal("synthetic-test-account", new AccountVault(_directory, TestKey).Load().Single().AccountName);
    }

    [Fact]
    public void WrongKeyDoesNotOverwriteVault()
    {
        var vault = new AccountVault(_directory, TestKey);
        vault.Save(SyntheticAccount());
        var before = File.ReadAllText(Path.Combine(_directory, "manifest.json"));
        Assert.ThrowsAny<Exception>(() => new AccountVault(_directory, "wrong key").Load());
        Assert.Equal(before, File.ReadAllText(Path.Combine(_directory, "manifest.json")));
    }

    [Theory]
    [InlineData("../outside.maFile")]
    [InlineData("..\\outside.maFile")]
    [InlineData("C:outside.maFile")]
    [InlineData("/outside.maFile")]
    public void ManifestCannotReadOutsideVault(string filename)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), JsonConvert.SerializeObject(new VaultManifest
        {
            Entries = new() { new() { Filename = filename, SteamID = 1 } }
        }));
        Assert.Throws<InvalidDataException>(() => new AccountVault(_directory, TestKey));
    }

    [Fact]
    public void BackupRoundTripsWithOriginalEncryptionAlgorithm()
    {
        var account = SyntheticAccount();
        var bytes = AccountTransfer.CreateBackup(new[] { account }, "synthetic backup password");
        using var stream = new MemoryStream(bytes);
        var imported = AccountTransfer.ReadBackup(stream, "synthetic backup password").Single();
        Assert.Equal(account.SharedSecret, imported.SharedSecret);
        Assert.Equal(account.Session.RefreshToken, imported.Session.RefreshToken);
        stream.Position = 0;
        Assert.ThrowsAny<Exception>(() => AccountTransfer.ReadBackup(stream, "incorrect"));
    }

    [Fact]
    public void QrTransferPreservesSecretsAndSteamIdButOmitsSessionCredentials()
    {
        var account = SyntheticAccount();
        var payload = AccountTransfer.CreateQrPayload(account);
        Assert.StartsWith("sda-mafile:v1:", payload);
        var imported = AccountTransfer.Read(payload);
        Assert.Equal(account.SharedSecret, imported.SharedSecret);
        Assert.Equal(account.IdentitySecret, imported.IdentitySecret);
        Assert.Equal(account.Session.SteamID, imported.Session.SteamID);
        Assert.Null(imported.Session.AccessToken);
        Assert.Null(imported.Session.RefreshToken);
        Assert.Null(imported.Session.SessionID);
    }

    [Fact]
    public void LegacyJsonQrIsAccepted()
    {
        var account = SyntheticAccount();
        Assert.Equal(account.SharedSecret, AccountTransfer.Read("\uFEFF " + JsonConvert.SerializeObject(account)).SharedSecret);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void FileImportRecognizesDesktopTextEncodingBoms(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var account = SyntheticAccount();
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(JsonConvert.SerializeObject(account))).ToArray();
        Assert.Equal(account.SharedSecret, AccountTransfer.Read(AccountTransfer.ReadFileText(bytes)).SharedSecret);
    }

    [Fact]
    public void QrWithBomInsideCompressedTextIsNormalizedAndUnknownQrIsNotCalledEncrypted()
    {
        var account = SyntheticAccount();
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
            gzip.Write(Encoding.UTF8.GetBytes(" \uFEFF " + JsonConvert.SerializeObject(account)));
        var payload = AccountTransfer.QrPrefix + Convert.ToBase64String(compressed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(account.SharedSecret, AccountTransfer.ReadQr(payload).SharedSecret);
        var error = Assert.Throws<InvalidDataException>(() => AccountTransfer.ReadQr("https://example.invalid/not-an-account"));
        Assert.DoesNotContain("encrypted", error.Message);
        Assert.Contains("SDA account transfer", error.Message);
    }

    [Fact]
    public void InvalidSecretsAndMissingSteamIdAreRejected()
    {
        var account = SyntheticAccount();
        account.SharedSecret = Convert.ToBase64String(new byte[5]);
        Assert.Throws<InvalidDataException>(() => AccountTransfer.Read(JsonConvert.SerializeObject(account)));
        account = SyntheticAccount();
        account.Session.SteamID = 0;
        Assert.Throws<InvalidDataException>(() => AccountTransfer.Read(JsonConvert.SerializeObject(account)));
    }

    [Fact]
    public void DecompressionLimitRejectsQrBomb()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
            gzip.Write(new byte[AccountTransfer.MaximumImportBytes + 1]);
        var payload = AccountTransfer.QrPrefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<InvalidDataException>(() => AccountTransfer.Read(payload));
    }

    [Fact]
    public void RemovingAnAccountDoesNotDeleteAnotherAccount()
    {
        var vault = new AccountVault(_directory, TestKey);
        var first = SyntheticAccount();
        var second = SyntheticAccount();
        second.Session.SteamID++;
        vault.Save(first);
        vault.Save(second);
        vault.Remove(first.Session.SteamID);
        Assert.Equal(second.Session.SteamID, new AccountVault(_directory, TestKey).Load().Single().Session.SteamID);
    }

    [Fact]
    public void BackupLimitsTotalDecompressedSizeBeforeLoadingAccounts()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var megabyte = new byte[AccountTransfer.MaximumImportBytes];
            for (var index = 0; index < 33; index++)
            {
                using var file = archive.CreateEntry(index + ".maFile", CompressionLevel.SmallestSize).Open();
                file.Write(megabyte);
            }
        }
        output.Position = 0;
        Assert.Throws<InvalidDataException>(() => AccountTransfer.ReadBackup(output, "synthetic password"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
