using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Sda.Mobile.Core;
using Xunit;

namespace Sda.Mobile.Tests;

public sealed class SteamLoginClientTests
{
    [Fact]
    public async Task LoginUsesGetForRsaAndEncryptedPasswordThenHandlesGuardChallenge()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var step = 0;
        using var handler = new StubHandler(async request =>
        {
            step++;
            if (step == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("account_name=test-user", request.RequestUri!.Query);
                return Json(new JObject { ["publickey_mod"] = Convert.ToHexString(parameters.Modulus!), ["publickey_exp"] = Convert.ToHexString(parameters.Exponent!), ["timestamp"] = "123" });
            }
            var form = (await request.Content!.ReadAsStringAsync()).Split('&')
                .Select(part => part.Split('=', 2)).ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));
            Assert.Equal(HttpMethod.Post, request.Method);
            if (step == 2)
            {
                Assert.Equal("synthetic-password", System.Text.Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(form["encrypted_password"]), RSAEncryptionPadding.Pkcs1)));
                Assert.Equal("3", form["platform_type"]);
                return Json(JObject.Parse("{ 'client_id':'123','request_id':'456','steamid':'76561198000000000','interval':1,'allowed_confirmations':[{'confirmation_type':3}] }"));
            }
            if (step == 3)
            {
                Assert.Equal("ABCDE", form["code"]);
                Assert.Equal("3", form["code_type"]);
                return Json(new());
            }
            Assert.Equal("456", form["request_id"]);
            return Json(JObject.Parse("{ 'access_token':'synthetic-access','refresh_token':'synthetic-refresh' }"));
        });
        using var http = new HttpClient(handler);
        var login = new SteamLoginClient(http);
        var challenge = await login.BeginAsync("test-user", "synthetic-password", default);
        Assert.Equal(3, challenge!.Type);
        Assert.Equal(76561198000000000UL, login.SteamId);
        await login.SubmitCodeAsync("ABCDE", challenge.Type, default);
        var session = await login.PollAsync(default);
        Assert.Equal(76561198000000000UL, session.SteamID);
        Assert.Equal("synthetic-access", session.AccessToken);
        Assert.Equal(4, step);
    }

    [Fact]
    public async Task HttpErrorsDoNotExposeResponseBodyOrTokens()
    {
        using var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("synthetic-sensitive-response")
        }));
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new SteamLoginClient(http).BeginAsync("user", "password", default));
        Assert.DoesNotContain("synthetic-sensitive-response", exception.Message);
        Assert.Contains("403", exception.Message);
    }

    [Fact]
    public async Task RefreshTokenAloneCompletesLoginLikeAndroid()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var requests = 0;
        using var handler = new StubHandler(request =>
        {
            requests++;
            return Task.FromResult(request.RequestUri!.AbsolutePath.Contains("GetPasswordRSAPublicKey", StringComparison.Ordinal)
                ? Json(new JObject { ["publickey_mod"] = Convert.ToHexString(parameters.Modulus!), ["publickey_exp"] = Convert.ToHexString(parameters.Exponent!), ["timestamp"] = "123" })
                : request.RequestUri.AbsolutePath.Contains("BeginAuthSession", StringComparison.Ordinal)
                    ? Json(JObject.Parse("{ 'client_id':'123','request_id':'456','steamid':'76561198000000000','allowed_confirmations':[] }"))
                    : Json(JObject.Parse("{ 'refresh_token':'synthetic-refresh-only' }")));
        });
        using var http = new HttpClient(handler);
        var login = new SteamLoginClient(http);
        Assert.Null(await login.BeginAsync("synthetic-user", "synthetic-password", default));
        var session = await login.PollAsync(default);
        Assert.Equal("synthetic-refresh-only", session.RefreshToken);
        Assert.Null(session.AccessToken);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task CancelledRequestStopsLogin()
    {
        using var handler = new StubHandler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SteamLoginClient(http).BeginAsync("user", "password", cancellation.Token));
    }

    private static HttpResponseMessage Json(JObject response) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JObject { ["response"] = response }.ToString())
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _send(request);
        }
    }
}
