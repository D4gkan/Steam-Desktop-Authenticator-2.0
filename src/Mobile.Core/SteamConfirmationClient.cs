using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamAuth;

namespace Sda.Mobile.Core;

public sealed class SteamSignInRequiredException(string message) : InvalidOperationException(message);

public interface ISteamConfirmationClient
{
    Task<long> GetTimeAsync(CancellationToken token);
    Task<bool> RefreshSessionAsync(SessionData session, long time, CancellationToken token);
    Task<IReadOnlyList<Confirmation>> FetchAsync(SteamGuardAccount account, long time, CancellationToken token);
    Task AnswerAsync(SteamGuardAccount account, Confirmation confirmation, bool allow, long time, CancellationToken token);
}

// Uses the same endpoints/cookies as the Android client, without reflection-based DTOs.
public sealed class SteamConfirmationClient(HttpClient http) : ISteamConfirmationClient
{
    private const string Community = "https://steamcommunity.com/mobileconf/";

    public static bool IsExpired(string? token, long time)
    {
        if (string.IsNullOrEmpty(token)) return true;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            using var json = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            return !long.TryParse(Text(json.RootElement, "exp"), out var expiry) || expiry <= time + 30;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidDataException) { return true; }
    }

    public async Task<long> GetTimeAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.steampowered.com/ITwoFactorService/QueryTime/v1/")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["steamid"] = "0" }) };
        using var json = await SendAsync(request, "Steam time", token);
        if (!json.RootElement.TryGetProperty("response", out var response) ||
            !long.TryParse(Text(response, "server_time"), out var time) || time <= 0)
            throw new InvalidDataException("Steam returned an incomplete clock response (C01).");
        return time;
    }

    public async Task<bool> RefreshSessionAsync(SessionData session, long time, CancellationToken token)
    {
        if (!IsExpired(session.AccessToken, time)) return false;
        if (IsExpired(session.RefreshToken, time))
            throw new SteamSignInRequiredException("Steam sign-in expired. Sign in again for this account.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = session.RefreshToken, ["steamid"] = session.SteamID.ToString(CultureInfo.InvariantCulture), ["renewal_type"] = "0"
            })
        };
        using var json = await SendAsync(request, "Steam session refresh", token);
        if (!json.RootElement.TryGetProperty("response", out var response))
            throw new InvalidDataException("Steam did not return a session. Sign in again (C02).");
        var access = Text(response, "access_token");
        if (IsExpired(access, time)) throw new SteamSignInRequiredException("Steam could not refresh this session. Sign in again (C02).");
        session.AccessToken = access;
        var refresh = Text(response, "refresh_token");
        if (!string.IsNullOrEmpty(refresh)) session.RefreshToken = refresh;
        return true;
    }

    public async Task<IReadOnlyList<Confirmation>> FetchAsync(SteamGuardAccount account, long time, CancellationToken token)
    {
        using var request = CommunityRequest(account, "getlist?" + Query(account, time, "conf"));
        using var json = await SendAsync(request, "Steam confirmations", token);
        CheckSuccess(json.RootElement);
        var result = new List<Confirmation>();
        if (!json.RootElement.TryGetProperty("conf", out var list) || list.ValueKind == JsonValueKind.Null) return result;
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Steam returned an unreadable confirmation list (C03).");
        foreach (var entry in list.EnumerateArray())
        {
            var rawType = Text(entry, "type");
            var type = ParseType(rawType);
            var summary = new List<string>();
            if (entry.TryGetProperty("summary", out var lines) && lines.ValueKind == JsonValueKind.Array)
                foreach (var line in lines.EnumerateArray()) if (line.ValueKind == JsonValueKind.String) summary.Add(line.GetString()!);
            result.Add(new Confirmation
            {
                ID = Unsigned(entry, "id", true), Key = Unsigned(entry, "nonce", true), Creator = Unsigned(entry, "creator_id", false),
                Headline = Text(entry, "headline"), Summary = summary, Accept = Text(entry, "accept"), Cancel = Text(entry, "cancel"),
                Icon = Text(entry, "icon"), ConfType = type, TypeName = Text(entry, "type_name") ?? (type == Confirmation.EMobileConfirmationType.Unknown ? rawType : null)
            });
        }
        return result;
    }

    public async Task AnswerAsync(SteamGuardAccount account, Confirmation confirmation, bool allow, long time, CancellationToken token)
    {
        // Match Android's operation/tag pairing. Never infer IDs/nonces from display text.
        var operation = allow ? "allow" : "cancel";
        var query = "ajaxop?op=" + operation + "&" + Query(account, time, operation) +
            "&cid=" + confirmation.ID.ToString(CultureInfo.InvariantCulture) + "&ck=" + confirmation.Key.ToString(CultureInfo.InvariantCulture);
        using var request = CommunityRequest(account, query);
        using var json = await SendAsync(request, "Steam confirmation response", token);
        CheckSuccess(json.RootElement);
    }

    private static HttpRequestMessage CommunityRequest(SteamGuardAccount account, string suffix)
    {
        var session = account.Session;
        session.SessionID ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Get, Community + suffix);
        request.Headers.TryAddWithoutValidation("Cookie", "steamLoginSecure=" + session.SteamID.ToString(CultureInfo.InvariantCulture) + "%7C%7C" +
            Uri.EscapeDataString(session.AccessToken ?? "") + "; sessionid=" + Uri.EscapeDataString(session.SessionID) + "; mobileClient=android; mobileClientVersion=777777 3.6.4");
        return request;
    }

    private static string Query(SteamGuardAccount account, long time, string tag)
    {
        if (string.IsNullOrEmpty(account.DeviceID) || string.IsNullOrEmpty(account.IdentitySecret))
            throw new InvalidOperationException("This account needs its full maFile to use confirmations.");
        var secret = Convert.FromBase64String(account.IdentitySecret);
        var data = new byte[8 + tag.Length];
        BinaryPrimitives.WriteInt64BigEndian(data, time);
        Encoding.UTF8.GetBytes(tag).CopyTo(data, 8);
        string hash;
        try { hash = Convert.ToBase64String(HMACSHA1.HashData(secret, data)); }
        finally { CryptographicOperations.ZeroMemory(secret); }
        return "p=" + Uri.EscapeDataString(account.DeviceID) + "&a=" + account.Session.SteamID.ToString(CultureInfo.InvariantCulture) +
            "&k=" + Uri.EscapeDataString(hash) + "&t=" + time.ToString(CultureInfo.InvariantCulture) + "&m=react&tag=" + tag;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, string stage, CancellationToken token)
    {
        request.Headers.UserAgent.ParseAdd(SteamWeb.MOBILE_APP_USER_AGENT);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SteamSignInRequiredException("Steam requires sign-in again for this account.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{stage} returned HTTP {(int)response.StatusCode}. Try again later.");
        if (response.Headers.TryGetValues("X-EResult", out var values) && int.TryParse(values.FirstOrDefault(), out var result) && result != 1)
            throw new InvalidOperationException($"Steam rejected this request (result {result}). Sign in again or retry later.");
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            // Bound responses, including HTML/error pages, before parsing.
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, token)) != 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024) throw new InvalidDataException($"{stage} response is too large (C04).");
                buffer.Write(chunk, 0, count);
            }
            var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        throw new InvalidDataException($"{stage} returned an unreadable response. Try signing in again (C05).");
    }

    private static void CheckSuccess(JsonElement json)
    {
        if (True(json, "needauth")) throw new SteamSignInRequiredException("Steam sign-in expired. Sign in again for this account.");
        var message = Text(json, "message") ?? "";
        if (message.Contains("needs authentication", StringComparison.OrdinalIgnoreCase) || message.Contains("invalid access token", StringComparison.OrdinalIgnoreCase) || message.Contains("access_token", StringComparison.OrdinalIgnoreCase))
            throw new SteamSignInRequiredException("Steam sign-in expired. Sign in again for this account.");
        if (!True(json, "success")) throw new InvalidOperationException("Steam could not complete the confirmation request. Refresh or sign in again.");
    }

    private static bool True(JsonElement json, string name) => Text(json, name) is "true" or "1" or "True";
    private static string? Text(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => throw new InvalidDataException("Steam returned an unsupported response field (C06).")
        };
    }

    private static ulong Unsigned(JsonElement json, string name, bool required)
    {
        var value = Text(json, name);
        if (!required && value == null) return 0;
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new InvalidDataException("Steam returned an unreadable confirmation identifier (C07).");
    }

    private static Confirmation.EMobileConfirmationType ParseType(string? value) => value?.ToLowerInvariant() switch
    {
        "0" or "invalid" => Confirmation.EMobileConfirmationType.Invalid,
        "1" or "test" => Confirmation.EMobileConfirmationType.Test,
        "2" or "trade" => Confirmation.EMobileConfirmationType.Trade,
        "3" or "marketlisting" or "market_listing" or "market_sell_transaction" => Confirmation.EMobileConfirmationType.MarketListing,
        "4" or "featureoptout" or "feature_opt_out" => Confirmation.EMobileConfirmationType.FeatureOptOut,
        "5" or "phonenumberchange" or "phone_number_change" => Confirmation.EMobileConfirmationType.PhoneNumberChange,
        "6" or "accountrecovery" or "account_recovery" => Confirmation.EMobileConfirmationType.AccountRecovery,
        _ => Confirmation.EMobileConfirmationType.Unknown
    };
}
