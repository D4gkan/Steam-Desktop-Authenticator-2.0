using System.Globalization;
using System.Text;
using System.Text.Json;
using SteamAuth;

namespace Sda.Mobile.Core;

// Explicit account mapping avoids reflection-generated constructors/setters on iOS.
// Keep these names identical to SteamGuardAccount's desktop JsonProperty names.
internal static class AccountJson
{
    public static SteamGuardAccount Read(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The account JSON is incomplete or malformed (I01). Import the original maFile without editing its contents.");
        }
        using (document)
        {
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The account JSON must contain an object (I02).");
            var session = Property(json, "Session");
            if (session.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
                throw InvalidField("Session");
            var account = new SteamGuardAccount
            {
                SharedSecret = Text(json, "shared_secret"), SerialNumber = Text(json, "serial_number"),
                RevocationCode = Text(json, "revocation_code"), URI = Text(json, "uri"),
                ServerTime = Number(json, "server_time"), AccountName = Text(json, "account_name"),
                TokenGID = Text(json, "token_gid"), IdentitySecret = Text(json, "identity_secret"),
                Secret1 = Text(json, "secret_1"), Status = Integer(json, "status"),
                DeviceID = Text(json, "device_id"), PhoneNumberHint = Text(json, "phone_number_hint"),
                ConfirmType = Integer(json, "confirm_type"), FullyEnrolled = Boolean(json, "fully_enrolled"),
                Session = new SessionData
                {
                    SteamID = SteamId(session, "SteamID"), AccessToken = Text(session, "AccessToken"),
                    RefreshToken = Text(session, "RefreshToken"), SessionID = Text(session, "SessionID")
                }
            };
            if (account.Session.SteamID == 0)
            {
                account.Session.SteamID = SteamId(json, "steamid");
                if (account.Session.SteamID == 0) account.Session.SteamID = SteamId(json, "steam_id");
                if (account.Session.SteamID == 0 && Uri.TryCreate(account.URI, UriKind.Absolute, out var uri))
                {
                    var id = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                        .FirstOrDefault(part => part.Length == 2 && part[0] == "steamid")?[1];
                    if (ulong.TryParse(id, out var steamId)) account.Session.SteamID = steamId;
                }
            }
            return account;
        }
    }

    public static string Write(SteamGuardAccount account, bool includeSessionCredentials = true)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("shared_secret", account.SharedSecret);
            writer.WriteString("serial_number", account.SerialNumber);
            writer.WriteString("revocation_code", account.RevocationCode);
            writer.WriteString("uri", account.URI);
            writer.WriteNumber("server_time", account.ServerTime);
            writer.WriteString("account_name", account.AccountName);
            writer.WriteString("token_gid", account.TokenGID);
            writer.WriteString("identity_secret", account.IdentitySecret);
            writer.WriteString("secret_1", account.Secret1);
            writer.WriteNumber("status", account.Status);
            writer.WriteString("device_id", account.DeviceID);
            writer.WriteString("phone_number_hint", account.PhoneNumberHint);
            writer.WriteNumber("confirm_type", account.ConfirmType);
            writer.WriteBoolean("fully_enrolled", account.FullyEnrolled);
            if (account.Session == null) writer.WriteNull("Session");
            else
            {
                writer.WriteStartObject("Session");
                writer.WriteNumber("SteamID", account.Session.SteamID);
                if (includeSessionCredentials)
                {
                    writer.WriteString("AccessToken", account.Session.AccessToken);
                    writer.WriteString("RefreshToken", account.Session.RefreshToken);
                    writer.WriteString("SessionID", account.Session.SessionID);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static JsonElement Property(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object) return default;
        if (json.TryGetProperty(name, out var value)) return value;
        foreach (var property in json.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string? Text(JsonElement json, string name)
    {
        var value = Property(json, name);
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => throw InvalidField(name)
        };
    }

    private static long Number(JsonElement json, string name)
    {
        var value = Text(json, name);
        if (value == null) return 0;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number : throw InvalidField(name);
    }

    private static int Integer(JsonElement json, string name)
    {
        var number = Number(json, name);
        return number is >= int.MinValue and <= int.MaxValue ? (int)number : throw InvalidField(name);
    }

    private static ulong SteamId(JsonElement json, string name)
    {
        var value = Text(json, name);
        if (value == null) return 0;
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number : throw InvalidField(name);
    }

    private static bool Boolean(JsonElement json, string name)
    {
        var value = Text(json, name);
        if (value == null) return false;
        if (bool.TryParse(value, out var boolean)) return boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number != 0;
        throw InvalidField(name);
    }

    // Only fixed schema names enter this message; never include input values or parser errors.
    private static InvalidDataException InvalidField(string name) =>
        new($"The account field '{name}' has an unsupported value type (I03). Re-export the account from SDA Desktop.");
}
