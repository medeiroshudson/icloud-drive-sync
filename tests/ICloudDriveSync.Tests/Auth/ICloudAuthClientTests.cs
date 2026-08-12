using System.Text;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Auth;

public class ICloudAuthClientTests
{
    private static readonly ICloudSession Session = new(
        SessionToken: "AQA-session-token",
        SessionId: "sid-1",
        TrustToken: "trust-1",
        AccountCountry: "BR",
        ClientId: "auth-1");

    private const string AccountLoginResponse = """
    {
      "dsInfo": { "accountCountry": "BR" },
      "webservices": {
        "drivews": { "url": "https://drivews.icloud.com" },
        "docws": { "url": "https://docws.icloud.com/drive/ws" },
        "ckdatabasews": { "url": "https://ckdatabasews.icloud.com" },
        "account": { "url": "https://www.icloud.com" }
      }
    }
    """;

    [Fact]
    public async Task AuthenticatePostsAccountLoginWithDsWebAuthToken()
    {
        using var http = FakeHttpMessageHandler.CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/setup/ws/1/accountLogin", req.RequestUri!.AbsolutePath);
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"dsWebAuthToken\":\"AQA-session-token\"", body);
            Assert.Contains("\"trustToken\":\"trust-1\"", body);
            return JsonResponse(AccountLoginResponse);
        });
        var client = new ICloudAuthClient(http);

        var result = await client.AuthenticateAsync(Session);

        var success = Assert.IsType<AuthSuccess>(result);
        Assert.Equal("https://drivews.icloud.com", success.Services.DriveWsUrl);
        Assert.Equal("https://docws.icloud.com/drive/ws", success.Services.DocWsUrl);
    }

    [Fact]
    public async Task AuthenticateSendsAppleSessionHeadersFromCookies()
    {
        using var http = FakeHttpMessageHandler.CreateClient(req =>
        {
            Assert.Contains("AQA-session-token", req.Headers.GetValues("X-Apple-Session-Token"));
            Assert.Equal("sid-1", req.Headers.GetValues("X-Apple-ID-Session-Id").Single());
            Assert.Equal("trust-1", req.Headers.GetValues("X-Apple-TwoSV-Trust-Token").Single());
            return JsonResponse(AccountLoginResponse);
        });
        var client = new ICloudAuthClient(http);

        await client.AuthenticateAsync(Session);
    }

    [Fact]
    public async Task AuthenticateReturnsAuthRequiredOnExpiredSession()
    {
        var handler = new FakeHttpMessageHandler(req =>
            new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid token"}""", System.Text.Encoding.UTF8, "application/json"),
            });
        using var http = new HttpClient(handler);
        var client = new ICloudAuthClient(http);

        var result = await client.AuthenticateAsync(Session);

        var required = Assert.IsType<AuthRequired>(result);
        Assert.Contains("expirada", required.Reason, StringComparison.OrdinalIgnoreCase);
        // Nunca tenta signin/SRP:
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("signin"));
    }

    [Fact]
    public async Task AuthenticateReturnsAuthRequiredWhenDriveWebserviceMissing()
    {
        using var http = FakeHttpMessageHandler.CreateClient(_ => JsonResponse(
            """{ "webservices": { "account": { "url": "https://www.icloud.com" } } }"""));
        var client = new ICloudAuthClient(http);

        var result = await client.AuthenticateAsync(Session);

        Assert.IsType<AuthRequired>(result);
    }

    private static HttpResponseMessage JsonResponse(string json) => new()
    {
        StatusCode = System.Net.HttpStatusCode.OK,
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}