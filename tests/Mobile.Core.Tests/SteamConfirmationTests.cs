using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sda.Mobile.Core;
using SteamAuth;
using Xunit;

namespace Sda.Mobile.Tests;

public sealed class SteamConfirmationTests
{
    internal static string Jwt(long expiry) => "synthetic." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":" + expiry + "}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".not-a-real-signature";

    [Fact]
    public async Task RefreshOnlySessionThenFetchUsesNewAccessCookieAndFullUnsignedNonce()
    {
        var account = AccountVaultTests.SyntheticAccount();
        account.Session.AccessToken = null!;
        account.Session.RefreshToken = Jwt(9999999999);
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.Contains("GenerateAccessTokenForApp"))
            {
                var fields = await request.Content!.ReadAsStringAsync();
                Assert.Contains("renewal_type=0", fields);
                Assert.Contains(account.Session.SteamID.ToString(), fields);
                return Reply("{\"response\":{\"access_token\":\"" + Jwt(8888888888) + "\"}}");
            }
            var cookie = request.Headers.GetValues("Cookie").Single();
            Assert.Contains("steamLoginSecure=" + account.Session.SteamID + "%7C%7C" + Jwt(8888888888), cookie);
            Assert.Contains("sessionid=synthetic-session", cookie);
            var query = Fields(request.RequestUri.Query.TrimStart('?'));
            Assert.Equal("conf", query["tag"]);
            Assert.Equal("1700000000", query["t"]);
            Assert.Equal(account.DeviceID, query["p"]);
            var data = Convert.FromHexString("000000006553F100636F6E66");
            Assert.Equal(Convert.ToBase64String(HMACSHA1.HashData(Convert.FromBase64String(account.IdentitySecret), data)), query["k"]);
            return Reply("{\"success\":true,\"conf\":[{\"id\":\"18446744073709551614\",\"nonce\":\"18446744073709551615\",\"creator_id\":42,\"type\":\"new_steam_type\",\"type_name\":\"New type\",\"headline\":\"Synthetic trade\",\"summary\":[\"One item\"]}]}");
        }));
        var client = new SteamConfirmationClient(http);
        Assert.True(await client.RefreshSessionAsync(account.Session, 1700000000, CancellationToken.None));
        var confirmation = Assert.Single(await client.FetchAsync(account, 1700000000, CancellationToken.None));
        Assert.Equal(ulong.MaxValue, confirmation.Key);
        Assert.Equal(ulong.MaxValue - 1, confirmation.ID);
        Assert.Equal(Confirmation.EMobileConfirmationType.Unknown, confirmation.ConfType);
        Assert.Equal("New type", confirmation.TypeName);
        Assert.Equal(2, requests.Count);
        Assert.False(await client.RefreshSessionAsync(account.Session, 1700000000, CancellationToken.None));
        Assert.Equal(2, requests.Count);
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "cancel")]
    public async Task AnswerSignsTheCorrectOperationAndPreservesUnsignedIdentifiers(bool allow, string expected)
    {
        var account = AccountVaultTests.SyntheticAccount();
        using var http = new HttpClient(new Handler(request =>
        {
            var query = Fields(request.RequestUri!.Query.TrimStart('?'));
            Assert.Equal(expected, query["op"]);
            Assert.Equal(expected, query["tag"]);
            Assert.Equal(ulong.MaxValue.ToString(), query["ck"]);
            Assert.Equal("11", query["cid"]);
            Assert.Contains(account.Session.SteamID.ToString(), request.Headers.GetValues("Cookie").Single());
            return Task.FromResult(Reply("{\"success\":true}"));
        }));
        await new SteamConfirmationClient(http).AnswerAsync(account, new Confirmation { ID = 11, Key = ulong.MaxValue }, allow, 1700000000, CancellationToken.None);
    }

    [Theory]
    [InlineData("<html>synthetic-private-response</html>")]
    [InlineData("{invalid synthetic-private-response")]
    public async Task UnreadableResponsesShowAStageWithoutLeakingResponseBodies(string body)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply(body))));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new SteamConfirmationClient(http).FetchAsync(AccountVaultTests.SyntheticAccount(), 1700000000, CancellationToken.None));
        Assert.Contains("Steam confirmations", error.Message);
        Assert.Contains("C05", error.Message);
        Assert.DoesNotContain("synthetic-private-response", error.Message);
    }

    [Fact]
    public async Task NeedAuthIsReportedEvenWhenSuccessIsTrue()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply("{\"success\":true,\"needauth\":true}"))));
        await Assert.ThrowsAsync<SteamSignInRequiredException>(() => new SteamConfirmationClient(http).FetchAsync(AccountVaultTests.SyntheticAccount(), 1700000000, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticationMessageIsMappedWithoutDisplayingItsContents()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply("{\"success\":false,\"message\":\"invalid access_token synthetic-private-marker\"}"))));
        var error = await Assert.ThrowsAsync<SteamSignInRequiredException>(() => new SteamConfirmationClient(http).FetchAsync(AccountVaultTests.SyntheticAccount(), 1700000000, CancellationToken.None));
        Assert.DoesNotContain("synthetic-private-marker", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("a.not-base64.c")]
    public void InvalidTokenDataRequestsRefreshInsteadOfThrowingJsonErrors(string? value)
        => Assert.True(SteamConfirmationClient.IsExpired(value, 1700000000));

    [Fact]
    public async Task SteamClockUsesExplicitParsingForStringOrNumericTime()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply("{\"response\":{\"server_time\":\"1700000000\"}}"))));
        Assert.Equal(1700000000, await new SteamConfirmationClient(http).GetTimeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredRefreshTokenDoesNotSendAnAuthenticationRequest()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not send expired credentials.")));
        await Assert.ThrowsAsync<SteamSignInRequiredException>(() => new SteamConfirmationClient(http).RefreshSessionAsync(new SessionData { RefreshToken = Jwt(1600000000) }, 1700000000, CancellationToken.None));
    }

    private static Dictionary<string, string> Fields(string text) => text.Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));
    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return respond(request); }
    }
}
