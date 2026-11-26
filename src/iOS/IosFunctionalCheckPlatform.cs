#if SDA_IOS_SIMULATOR
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sda.Mobile.Core;
using SteamAuth;

namespace Sda.Mobile.iOS;

// Compiled only into simulator builds. Uses synthetic accounts in a separate vault,
// while exercising the real Keychain, iOS cryptography, and mobile view model.
internal sealed class IosFunctionalCheckPlatform : IMobilePlatform
{
    private readonly IosPlatform _native;
    private readonly string _prefix = "self-test-" + Guid.NewGuid().ToString("N") + "-";
    private readonly HashSet<string> _names = new();
    private readonly SimulatedSteamApiHandler _server = new();
    private readonly HttpClient _simulatedSteam;
    private readonly Queue<string?> _answers = new();
    private string? _clipboard;
    private string? _scanPayload;
    public string AccountDirectory { get; } = Path.Combine(Path.GetTempPath(), "sda-functional-check", Guid.NewGuid().ToString("N"));

    public IosFunctionalCheckPlatform(IosPlatform native) { _native = native; _simulatedSteam = new(_server); }
    public string? ReadSecurityValue(string name) => _native.ReadSecurityValue(_prefix + name);
    public void WriteSecurityValue(string name, string value) { _names.Add(name); _native.WriteSecurityValue(_prefix + name, value); }
    public void RemoveSecurityValue(string name) => _native.RemoveSecurityValue(_prefix + name);
    public string GetVaultKey(bool vaultExists)
    {
        var key = ReadSecurityValue("vault-key-v1");
        if (key != null) return key;
        if (vaultExists) throw new InvalidOperationException("Self-test legacy key missing.");
        key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        WriteSecurityValue("vault-key-v1", key);
        return key;
    }
    public Task<string?> ScanQrAsync() => Task.FromResult(_scanPayload);
    public void CopyCode(string code) { }
    public ISteamLoginClient CreateLoginClient(HttpClient http) => new SteamLoginClient(_simulatedSteam);
    public ISteamConfirmationClient CreateConfirmationClient(HttpClient http) => new SteamConfirmationClient(_simulatedSteam);
    public Task<string?> PromptAsync(PromptRequest request, Func<PromptRequest, Task<string?>> fallback)
        => Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : request.IsMultiline ? _clipboard : "123456");

    public async Task RunAsync()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var window = (UIKit.UIApplication.SharedApplication.Delegate as AppDelegate)?.Window;
            Require(window?.WindowScene?.Delegate is SceneDelegate && window.RootViewController is HostViewController,
                "scene-backed native host");
            var model = App.ViewModel ?? throw new InvalidOperationException("View model missing.");
            await model.InitializeAsync();
            while (model.IsBusy && watch.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
            Require(model.IsUnlocked, "initial passcode setup");
            var pinPage = _native.PromptAsync(new PromptRequest("Enter passcode", "", true, IsNumeric: true,
                ValidatePinAsync: pin => Task.FromResult<string?>(pin == "123456" ? null : "Incorrect passcode."), CanCancel: false),
                _ => throw new InvalidOperationException("Native passcode page was not used."));
            await Task.Delay(750);
            var pinReady = false;
            for (var attempt = 0; attempt < 100 && !pinReady; attempt++)
            { await Task.Delay(50); pinReady = _native.SubmitPinForFunctionalCheck("000000"); }
            Require(pinReady, "native passcode page presentation");
            await Task.Delay(150);
            Require(!pinPage.IsCompleted && _native.SubmitPinForFunctionalCheck("123456"), "native incorrect passcode retry");
            Require(await pinPage.WaitAsync(TimeSpan.FromSeconds(10)) == "123456", "native six-digit automatic submit");
            // Start with externally authored JSON, rather than serializing the same
            // in-memory model that the importer will read. All values are synthetic.
            using var fixtureStream = typeof(IosFunctionalCheckPlatform).Assembly.GetManifestResourceStream("Sda.Tests.DesktopAccount.json")
                ?? throw new InvalidOperationException("Synthetic import fixture missing.");
            using var fixtureReader = new StreamReader(fixtureStream);
            var syntheticText = await fixtureReader.ReadToEndAsync();
            var account = AccountTransfer.Read(syntheticText);
            var nativeEditor = _native.PromptAsync(new PromptRequest("Paste account data", "Synthetic native input verification.", IsMultiline: true),
                _ => throw new InvalidOperationException("The native editor was not used."));
            await Task.Delay(750);
            var submitted = false;
            for (var attempt = 0; attempt < 100 && !submitted; attempt++)
            {
                await Task.Delay(50);
                submitted = _native.SubmitTextForFunctionalCheck(syntheticText);
            }
            Require(submitted && await nativeEditor.WaitAsync(TimeSpan.FromSeconds(10)) == syntheticText, "native multiline editor and dismissal");
            await model.ImportFilesAsync(new[] { ("synthetic.maFile", Encoding.UTF8.GetBytes(syntheticText)) });
            Require(!model.IsError && model.Accounts.Count == 1 && model.Accounts[0].Code.Length == 5, "file import and code generation");
            Require(ReadSecurityValue("vault-key-v1") == null, "legacy key cleanup");
            model.Lock();
            _answers.Enqueue("000000");
            await model.UnlockAsync();
            Require(!model.IsUnlocked && model.Accounts.Count == 0 && model.IsError, "incorrect passcode rejection");
            await model.UnlockAsync();
            Require(model.IsUnlocked && model.Accounts.Count == 1, "persistent passcode unlock");
            account.AccountName = "Second test account";
            account.Session.SteamID++;
            // Legacy desktop QR sends plaintext JSON. Exercise that independently of
            // this app's QR encoder, then check the compressed transfer as well.
            _scanPayload = syntheticText.Replace("Test account", "Second test account").Replace("76561198000000000", "76561198000000001");
            await model.ScanAsync();
            Require(!model.IsError && model.Accounts.Count == 2, "legacy desktop JSON QR and Steam sign-in");
            _answers.Enqueue("123456");
            _answers.Enqueue("yes");
            _scanPayload = AccountTransfer.CreateQrPayload(account);
            await model.ScanAsync();
            Require(!model.IsError && model.Accounts.Count == 2, "QR import with Steam sign-in");
            _clipboard = syntheticText;
            await model.PasteQrAsync();
            Require(!model.IsError && model.Accounts.Count == 2, "pasted import with Steam sign-in");
            await model.OpenConfirmationsAsync();
            Require(!model.IsError && model.ConfirmationIssues.Count == 0 && model.Confirmations.Count == 2, "combined confirmations and refresh-only sessions");
            await Task.Delay(200);
            _native.SaveFunctionalScreenshot("native-confirmations.png");
            var secondConfirmation = model.Confirmations.Single(item => item.AccountName.StartsWith("Second", StringComparison.Ordinal));
            model.SelectedAccount = model.Accounts[0];
            await model.RespondAsync(secondConfirmation, true);
            Require(!model.IsError && model.Confirmations.Count == 1 && _server.LastAnsweredSteamId == secondConfirmation.Owner.Account.Session.SteamID,
                "confirmation response uses item owner and unsigned nonce");
            model.SetActive(false);
            model.SetActive(true);
            Require(model.IsUnlocked && model.Accounts.Count == 2, "short background return keeps unlock");
            _clipboard = "{invalid synthetic data";
            await model.PasteQrAsync();
            Require(model.IsError && model.Accounts.Count == 2, "invalid import feedback");
            _answers.Enqueue("123456");
            _answers.Enqueue("654321");
            _answers.Enqueue("654321");
            await model.ChangePasscodeAsync();
            Require(!model.IsError, "passcode change");
            model.Lock();
            _answers.Enqueue("654321");
            await model.UnlockAsync();
            Require(model.IsUnlocked && model.Accounts.Count == 2 && model.Accounts.All(item => item.Code.Length == 5), "changed passcode and saved accounts");
            model.Section = MobileSection.Accounts;
            model.DismissNotice();
            Console.WriteLine($"SDA functional check: passed in {watch.Elapsed.TotalSeconds:F1}s.");
        }
        catch (Exception error)
        {
            // Log the failing test stage/type, never account material or native error bodies.
            Console.WriteLine($"SDA functional check: FAILED ({error.GetType().Name}).");
            Console.WriteLine(error.StackTrace);
            _native.CancelPrompt();
        }
        finally
        {
            foreach (var name in _names) _native.RemoveSecurityValue(_prefix + name);
            if (Directory.Exists(AccountDirectory)) Directory.Delete(AccountDirectory, true);
            _simulatedSteam.Dispose();
        }
    }

    private static void Require(bool condition, string stage)
    {
        if (condition) return;
        Console.WriteLine($"SDA functional check: failed stage: {stage}.");
        throw new InvalidOperationException("Functional verification failed.");
    }

    private sealed class SimulatedSteamApiHandler : HttpMessageHandler
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private ulong _steamId;
        private readonly HashSet<ulong> _answered = new();
        public ulong LastAnsweredSteamId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = request.RequestUri!.AbsolutePath.Split('/')[2];
            if (method == "QueryTime") return Reply(new JObject { ["server_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            if (request.RequestUri.Host == "steamcommunity.com")
            {
                var cookie = request.Headers.GetValues("Cookie").Single();
                var id = cookie.Split('=')[1].Split('%')[0];
                if (id is not ("76561198000000000" or "76561198000000001")) throw new InvalidOperationException("Unexpected synthetic cookie owner.");
                if (method == "getlist" && _answered.Contains(ulong.Parse(id))) return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"success\":true,\"conf\":[]}") };
                if (method == "getlist") return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"success\":true,\"conf\":[{\"id\":\"11\",\"nonce\":\"18446744073709551615\",\"creator_id\":42,\"type\":2,\"headline\":\"Synthetic trade\",\"summary\":[\"One item\"]}]}") };
                if (method == "ajaxop")
                {
                    var query = request.RequestUri.Query;
                    if (!query.Contains("ck=18446744073709551615") || !query.Contains("tag=allow")) throw new InvalidOperationException("Incorrect synthetic confirmation response.");
                    LastAnsweredSteamId = ulong.Parse(id);
                    _answered.Add(LastAnsweredSteamId);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") };
                }
                throw new InvalidOperationException("Unexpected synthetic community request.");
            }
            if (method == "GetPasswordRSAPublicKey")
            {
                var key = _rsa.ExportParameters(false);
                return Reply(new JObject { ["publickey_mod"] = Convert.ToHexString(key.Modulus!), ["publickey_exp"] = Convert.ToHexString(key.Exponent!), ["timestamp"] = "123" });
            }
            var fields = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(part => part.Split('=', 2)).ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));
            switch (method)
            {
                case "GenerateAccessTokenForApp":
                    return Reply(new JObject { ["access_token"] = Jwt(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600) });
                case "BeginAuthSessionViaCredentials":
                    var plaintext = _rsa.Decrypt(Convert.FromBase64String(fields["encrypted_password"]), RSAEncryptionPadding.Pkcs1);
                    try
                    {
                        if (Encoding.UTF8.GetString(plaintext) != "123456") throw new InvalidOperationException("Synthetic RSA password exchange failed.");
                    }
                    finally { CryptographicOperations.ZeroMemory(plaintext); }
                    _steamId = fields["account_name"].StartsWith("Second", StringComparison.Ordinal) ? 76561198000000001UL : 76561198000000000UL;
                    return Reply(new JObject
                    {
                        ["client_id"] = "123", ["request_id"] = "synthetic-request", ["steamid"] = _steamId.ToString(),
                        ["allowed_confirmations"] = new JArray(new JObject { ["confirmation_type"] = 3 })
                    });
                case "UpdateAuthSessionWithSteamGuardCode":
                    if (fields["code"].Length != 5 || fields["code_type"] != "3")
                        throw new InvalidOperationException("Synthetic Steam Guard verification failed.");
                    return Reply(new JObject());
                case "PollAuthSessionStatus":
                    // Exercise the actual HTTP login client with Android's refresh-only
                    // success shape, while keeping all credentials and requests synthetic.
                    return Reply(new JObject { ["refresh_token"] = Jwt(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400) });
                default:
                    throw new InvalidOperationException("Unexpected synthetic Steam request.");
            }
        }

        private static string Jwt(long expiry) => "synthetic." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":" + expiry + "}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".not-a-real-signature";

        private static HttpResponseMessage Reply(JObject response) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JObject { ["response"] = response }.ToString(Formatting.None))
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing) _rsa.Dispose();
            base.Dispose(disposing);
        }
    }
}
#endif
