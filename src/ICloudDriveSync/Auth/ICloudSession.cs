using System.Text.Json;

namespace ICloudDriveSync.Auth;

/// <summary>Sessão do iCloud injetada (formato compatível com o arquivo de sessão do pyicloud).</summary>
public sealed record ICloudSession(
    string SessionToken,
    string? SessionId = null,
    string? TrustToken = null,
    string? AccountCountry = null,
    string? ClientId = null)
{
    public static ICloudSession Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("session_token", out var token) || string.IsNullOrEmpty(token.GetString()))
        {
            throw new InvalidDataException("session_token ausente no arquivo de sessão.");
        }

        return new ICloudSession(
            SessionToken: token.GetString()!,
            SessionId: GetString(root, "session_id"),
            TrustToken: GetString(root, "trust_token"),
            AccountCountry: GetString(root, "account_country"),
            ClientId: GetString(root, "client_id"));
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
