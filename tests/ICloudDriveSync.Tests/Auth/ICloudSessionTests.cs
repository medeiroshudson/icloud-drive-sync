using ICloudDriveSync.Auth;

namespace ICloudDriveSync.Tests.Auth;

public class ICloudSessionTests
{
    [Fact]
    public void ParsesPyicloudSessionJson()
    {
        var json = """
        {
          "client_id": "auth-9f8e7d6c-5b4a-3c2d-1e0f-a1b2c3d4e5f6",
          "session_token": "AQAQ1234567890abcdef",
          "session_id": "abc123def456",
          "trust_token": "trust-token-xyz",
          "account_country": "BR"
        }
        """;

        var session = ICloudSession.Parse(json);

        Assert.Equal("AQAQ1234567890abcdef", session.SessionToken);
        Assert.Equal("abc123def456", session.SessionId);
        Assert.Equal("trust-token-xyz", session.TrustToken);
        Assert.Equal("BR", session.AccountCountry);
        Assert.Equal("auth-9f8e7d6c-5b4a-3c2d-1e0f-a1b2c3d4e5f6", session.ClientId);
    }

    [Fact]
    public void ParseThrowsWhenSessionTokenMissing()
    {
        var json = """{ "client_id": "auth-1" }""";

        var ex = Assert.Throws<InvalidDataException>(() => ICloudSession.Parse(json));

        Assert.Contains("session_token", ex.Message);
    }

    [Fact]
    public void ParseHandlesMissingOptionalFields()
    {
        var json = """{ "session_token": "tok" }""";

        var session = ICloudSession.Parse(json);

        Assert.Equal("tok", session.SessionToken);
        Assert.Null(session.TrustToken);
        Assert.Null(session.AccountCountry);
    }
}
