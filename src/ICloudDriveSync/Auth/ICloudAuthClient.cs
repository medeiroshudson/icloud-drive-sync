using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ICloudDriveSync.Auth;

/// <summary>
/// Autentica no iCloud usando apenas a sessão injetada (session_token + cookies).
/// Nunca autentica com senha/SRP — isso causa lockout na conta.
/// </summary>
public sealed class ICloudAuthClient(HttpClient http, string setupEndpoint = "https://setup.icloud.com/setup/ws/1")
{
    private const string WidgetKey = "d39ba9916b7251055b22c7f910e2ea796ee65e98b2ddecea8f5dde8d9d1a815d";

    public async Task<AuthResult> AuthenticateAsync(ICloudSession session, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{setupEndpoint}/accountLogin");

        request.Headers.TryAddWithoutValidation("Origin", "https://www.icloud.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.icloud.com/");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Apple-Widget-Key", WidgetKey);
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-Client-Id", WidgetKey);
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-Redirect-uri", "https://www.icloud.com");
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-Client-Type", "firstPartyAuth");
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-Response-Mode", "web_message");
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-Response-Type", "code");
        request.Headers.TryAddWithoutValidation("X-Apple-OAuth-State", session.ClientId ?? "auth");
        if (!string.IsNullOrEmpty(session.SessionToken))
            request.Headers.TryAddWithoutValidation("X-Apple-Session-Token", session.SessionToken);
        if (!string.IsNullOrEmpty(session.SessionId))
            request.Headers.TryAddWithoutValidation("X-Apple-ID-Session-Id", session.SessionId);
        if (!string.IsNullOrEmpty(session.TrustToken))
            request.Headers.TryAddWithoutValidation("X-Apple-TwoSV-Trust-Token", session.TrustToken);

        var body = new Dictionary<string, object?>
        {
            ["dsWebAuthToken"] = session.SessionToken,
            ["accountCountryCode"] = session.AccountCountry,
            ["extended_login"] = true,
            ["trustToken"] = session.TrustToken,
        };
        request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            return new AuthRequired("Sessão expirada ou inválida (HTTP " + (int)response.StatusCode + "). Renove os cookies do browser e reinicie o seed.");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("webservices", out var ws)
            && ws.TryGetProperty("drivews", out var drivews)
            && drivews.TryGetProperty("url", out var url)
            && url.ValueKind == JsonValueKind.String
            && ws.TryGetProperty("docws", out var docws)
            && docws.TryGetProperty("url", out var docUrl)
            && docUrl.ValueKind == JsonValueKind.String)
        {
            return new AuthSuccess(new WebServices(url.GetString()!, docUrl.GetString()!));
        }

        return new AuthRequired("Webservice drivews/docws não disponível para esta conta. Ative o iCloud Drive em um dispositivo Apple.");
    }
}
