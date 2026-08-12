using System.Net.Http.Json;
using System.Text.Json;

namespace ICloudDriveSync.Auth;

/// <summary>
/// Autentica no iCloud usando apenas a sessão injetada (cookies + session_token).
/// Nunca autentica com senha/SRP — isso causa lockout na conta.
///
/// Fluxo (espelha o pyicloud com sessão salva):
/// POST /setup/ws/1/validate com body "null" — valida o token existente via cookies
/// e devolve os webservices (drivews/docws). accountLogin só é usado após um signin
/// novo, que nunca fazemos.
/// </summary>
public sealed class ICloudAuthClient(HttpClient http, string setupEndpoint = "https://setup.icloud.com/setup/ws/1")
{
    public async Task<AuthResult> AuthenticateAsync(ICloudSession session, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{setupEndpoint}/validate");

        request.Headers.TryAddWithoutValidation("Origin", "https://www.icloud.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.icloud.com/");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

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
