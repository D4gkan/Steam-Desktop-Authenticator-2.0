using System.Globalization;
using Newtonsoft.Json.Linq;
using SteamAuth;

namespace Sda.Mobile.Core;

public sealed class SteamLinkingClient
{
    private readonly HttpClient _http;
    public SteamLinkingClient(HttpClient http) => _http = http;

    // A standalone finalization step allows an unfinished, already persisted authenticator
    // to be resumed after iOS suspends or terminates the app.
    public async Task FinalizeAsync(SteamGuardAccount account, string smsCode, CancellationToken cancellationToken)
    {
        var time = await TimeAligner.GetSteamTimeAsync();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["steamid"] = account.Session.SteamID.ToString(CultureInfo.InvariantCulture),
                ["authenticator_code"] = account.GenerateSteamGuardCodeForTime(time),
                ["authenticator_time"] = time.ToString(CultureInfo.InvariantCulture),
                ["activation_code"] = smsCode.Trim(), ["validate_sms_code"] = "1",
                ["access_token"] = account.Session.AccessToken
            });
            using var response = await _http.PostAsync("https://api.steampowered.com/ITwoFactorService/FinalizeAddAuthenticator/v1/", body, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken))["response"] as JObject
                ?? throw new InvalidDataException("Steam returned an invalid linking response.");
            var status = (int?)result["status"];
            if (status == 89) throw new InvalidOperationException("The activation code is incorrect. Resume linking and try again.");
            if ((bool?)result["success"] != true)
                throw new InvalidOperationException("Steam could not finalize this authenticator. Keep its backup and retry Resume linking.");
            if ((bool?)result["want_more"] != true)
            {
                account.FullyEnrolled = true;
                return;
            }
            time = ((long?)result["server_time"] ?? time) + 30;
        }
        throw new InvalidOperationException("Steam could not verify the authenticator clock. Resume linking and try again.");
    }
}
