using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SteamAuth;

namespace Sda.Mobile.Core;

public sealed record GuardChallenge(int Type, string Message);

public interface ISteamLoginClient
{
    ulong SteamId { get; }
    Task<GuardChallenge?> BeginAsync(string username, string password, CancellationToken cancellationToken);
    Task SubmitCodeAsync(string code, int type, CancellationToken cancellationToken);
    Task<SessionData> PollAsync(CancellationToken cancellationToken);
}

/// <summary>Mobile HTTPS login flow matching the Android companion; no runtime code generation.</summary>
public sealed class SteamLoginClient : ISteamLoginClient
{
    private const string BaseUrl = "https://api.steampowered.com/IAuthenticationService/";
    private readonly HttpClient _http;
    private string? _clientId;
    private string? _requestId;
    private ulong _steamId;
    private TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public SteamLoginClient(HttpClient http) => _http = http;
    public ulong SteamId => _steamId;

    public async Task<GuardChallenge?> BeginAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Enter a Steam username and password.");
        _clientId = _requestId = null;
        var rsa = await RequestAsync("GetPasswordRSAPublicKey", new() { ["account_name"] = username }, true, cancellationToken);
        using var algorithm = RSA.Create();
        algorithm.ImportParameters(new RSAParameters
        {
            Modulus = Hex(Required(rsa, "publickey_mod")),
            Exponent = Hex(Required(rsa, "publickey_exp"))
        });
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        string encrypted;
        try { encrypted = Convert.ToBase64String(algorithm.Encrypt(passwordBytes, RSAEncryptionPadding.Pkcs1)); }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
        var response = await RequestAsync("BeginAuthSessionViaCredentials", new()
        {
            ["account_name"] = username,
            ["encrypted_password"] = encrypted,
            ["encryption_timestamp"] = Required(rsa, "timestamp"),
            ["remember_login"] = "false", ["platform_type"] = "3", ["persistence"] = "0",
            ["website_id"] = "Mobile", ["device_friendly_name"] = "SDA Mobile (iOS)"
        }, false, cancellationToken);
        _clientId = Required(response, "client_id");
        _requestId = Required(response, "request_id");
        _steamId = ulong.Parse(Required(response, "steamid"), CultureInfo.InvariantCulture);
        var seconds = (double?)response["interval"] ?? 5;
        _pollInterval = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 1, 10) : 5);
        var choices = (response["allowed_confirmations"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
        // Prefer a code over a potentially indefinite approval wait, while exposing the actual type.
        var choice = choices.OrderBy(item => (int?)item["confirmation_type"] switch { 3 => 0, 2 => 1, 4 => 2, 1 => 3, _ => 4 }).FirstOrDefault();
        var type = (int?)choice?["confirmation_type"] ?? 1;
        if (type == 1) return null;
        if (type is not (2 or 3 or 4))
            throw new InvalidOperationException("Steam requested an unsupported sign-in method. Sign in with the official Steam app first.");
        return new GuardChallenge(type, (string?)choice?["associated_message"] ?? "");
    }

    public async Task SubmitCodeAsync(string code, int type, CancellationToken cancellationToken)
    {
        EnsureStarted();
        if (type is not (2 or 3)) throw new InvalidOperationException("This sign-in requires approval in your existing authenticator.");
        await RequestAsync("UpdateAuthSessionWithSteamGuardCode", new()
        {
            ["client_id"] = _clientId!, ["steamid"] = _steamId.ToString(CultureInfo.InvariantCulture),
            ["code"] = code.Trim(), ["code_type"] = type.ToString(CultureInfo.InvariantCulture)
        }, false, cancellationToken);
    }

    public async Task<SessionData> PollAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            while (true)
            {
                var response = await RequestAsync("PollAuthSessionStatus", new()
                {
                    ["client_id"] = _clientId!, ["request_id"] = _requestId!
                }, false, timeout.Token);
                if (!string.IsNullOrEmpty((string?)response["new_client_id"]))
                    _clientId = (string)response["new_client_id"]!;
                var refreshToken = (string?)response["refresh_token"];
                var accessToken = (string?)response["access_token"];
                // Match Android: Steam may finish authentication with a refresh token
                // alone. An access token can be minted when confirmations are opened.
                if (!string.IsNullOrEmpty(refreshToken))
                    return new SessionData
                    {
                        SteamID = _steamId, AccessToken = accessToken, RefreshToken = refreshToken,
                        SessionID = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
                    };
                await Task.Delay(_pollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Steam sign-in timed out. Start sign-in again.");
        }
    }

    private async Task<JObject> RequestAsync(string method, Dictionary<string, string> fields, bool get, CancellationToken cancellationToken)
    {
        var url = BaseUrl + method + "/v1/";
        using var content = new FormUrlEncodedContent(fields);
        if (get) url += "?" + await content.ReadAsStringAsync(cancellationToken);
        using var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, url);
        if (!get) request.Content = content;
        request.Headers.UserAgent.ParseAdd("SDA-Mobile/2.0.5");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Steam sign-in returned HTTP {(int)response.StatusCode}. Try again later.");
        if (response.Headers.TryGetValues("X-EResult", out var values) && int.TryParse(values.FirstOrDefault(), out var result) && result != 1)
            throw new InvalidOperationException($"Steam rejected sign-in (result {result}). Check your credentials or verification code.");
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json["response"] as JObject ?? throw new InvalidDataException("Steam returned an invalid sign-in response.");
    }

    private void EnsureStarted()
    {
        if (_clientId == null || _requestId == null) throw new InvalidOperationException("Start sign-in again.");
    }

    private static string Required(JObject json, string name) =>
        (string?)json[name] is { Length: > 0 } value ? value : throw new InvalidDataException("Steam returned an incomplete sign-in response.");

    private static byte[] Hex(string value) => Convert.FromHexString(value.Length % 2 == 0 ? value : "0" + value);
}
