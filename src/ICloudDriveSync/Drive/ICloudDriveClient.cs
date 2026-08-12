using System.Net.Http.Json;
using System.Text.Json;
using ICloudDriveSync.Auth;

namespace ICloudDriveSync.Drive;

/// <summary>Cliente do protocolo CloudDocs (DriveWS/docws) do iCloud Drive.</summary>
public sealed class ICloudDriveClient
{
    private readonly HttpClient _http;
    private readonly WebServices _services;
    private readonly string _clientId;

    public ICloudDriveClient(HttpClient http, WebServices services, string? clientId = null)
    {
        _http = http;
        _services = services;
        _clientId = clientId ?? "icloud-drive-sync";

        // O gateway responde 421 ("Invalid or missing Origin header") sem estes headers.
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.icloud.com");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.icloud.com/");
    }

    public async Task<IReadOnlyList<DriveNode>> GetChildrenAsync(string folderDriveWsId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_services.DriveWsUrl}/retrieveItemDetailsInFolders")
        {
            Content = JsonContent.Create(new object[]
            {
                new { drivewsid = folderDriveWsId, partialData = false },
            }),
        };

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // O iCloud responde com um array; o [0] é o próprio nó pedido, com os filhos em "items".
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return [];
        }

        var node = root[0];
        if (!node.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.Deserialize<List<DriveNode>>() ?? [];
    }

    /// <summary>fileCount do nó pedido (sanity check rápido — espelha o refresh do icloudds).</summary>
    public async Task<long?> GetFileCountAsync(string driveWsId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_services.DriveWsUrl}/retrieveItemDetailsInFolders")
        {
            Content = JsonContent.Create(new object[]
            {
                new { drivewsid = driveWsId, partialData = false },
            }),
        };

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return null;
        }

        return root[0].TryGetProperty("fileCount", out var fileCount) && fileCount.ValueKind == JsonValueKind.Number
            ? fileCount.GetInt64()
            : null;
    }

    /// <summary>Baixa o conteúdo de um arquivo do iCloud Drive (streaming, sem buffer em memória).</summary>
    public async Task<Stream> DownloadAsync(DriveNode node, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(node.DocWsId))
        {
            throw new InvalidDataException("Nó sem docwsid não pode ser baixado.");
        }

        var tokenRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{_services.DocWsUrl}/ws/com.apple.CloudDocs/download/by_id?document_id={Uri.EscapeDataString(node.DocWsId)}&clientId={_clientId}");
        using var tokenResponse = await _http.SendAsync(tokenRequest, ct);
        tokenResponse.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string? url = null;
        if (root.TryGetProperty("data_token", out var dataToken) && dataToken.TryGetProperty("url", out var dataUrl) && dataUrl.ValueKind == JsonValueKind.String)
        {
            url = dataUrl.GetString();
        }
        else if (root.TryGetProperty("package_token", out var packageToken) && packageToken.TryGetProperty("url", out var packageUrl) && packageUrl.ValueKind == JsonValueKind.String)
        {
            url = packageUrl.GetString();
        }

        if (url is null)
        {
            throw new InvalidDataException("Resposta do download sem data_token/package_token.");
        }

        var contentRequest = new HttpRequestMessage(HttpMethod.Get, url);
        var contentResponse = await _http.SendAsync(contentRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        contentResponse.EnsureSuccessStatusCode();
        return await contentResponse.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>
    /// Envia um arquivo novo para o iCloud Drive (3 passos: upload/web → content_url → update/documents).
    /// </summary>
    public async Task UploadAsync(
        string parentFolderDriveWsId,
        string fileName,
        long size,
        Stream content,
        string? webauthToken = null,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var tokenQuery = string.IsNullOrEmpty(webauthToken) ? "" : $"&token={Uri.EscapeDataString(webauthToken)}";

        // 1. Solicita a URL de conteúdo.
        var uploadWebUrl = $"{_services.DocWsUrl}/ws/com.apple.CloudDocs/upload/web?clientId={_clientId}{tokenQuery}";
        using (var uploadWebReq = new HttpRequestMessage(HttpMethod.Post, uploadWebUrl)
               {
                   Content = JsonContent.Create(new
                   {
                       filename = fileName,
                       type = "FILE",
                       content_type = contentType ?? "application/octet-stream",
                       size = size,
                   }),
               })
        {
            using var uploadWebResp = await _http.SendAsync(uploadWebReq, ct);
            uploadWebResp.EnsureSuccessStatusCode();

            using var uploadWebDoc = JsonDocument.Parse(await uploadWebResp.Content.ReadAsStringAsync(ct));
            var items = uploadWebDoc.RootElement;
            var documentId = items[0].GetProperty("document_id").GetString()!;
            var contentUrl = items[0].GetProperty("url").GetString()!;

            // 2. Envia o binário (multipart) para a content_url.
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(content), fileName, fileName);
            using var contentResp = await _http.PostAsync(contentUrl, form, ct);
            contentResp.EnsureSuccessStatusCode();

            using var sfDoc = JsonDocument.Parse(await contentResp.Content.ReadAsStringAsync(ct));
            var sf = sfDoc.RootElement.GetProperty("singleFile");
            var signature = sf.GetProperty("fileChecksum").GetString()!;
            var wrappingKey = sf.GetProperty("wrappingKey").GetString()!;
            var referenceChecksum = sf.GetProperty("referenceChecksum").GetString()!;
            var sfSize = sf.GetProperty("size").GetInt64();
            string? receipt = sf.TryGetProperty("receipt", out var recv) && recv.ValueKind == JsonValueKind.String
                ? recv.GetString() : null;

            // 3. Registra o documento na pasta de destino.
            var mtime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var data = new Dictionary<string, object?>
            {
                ["data"] = BuildFileData(signature, wrappingKey, referenceChecksum, sfSize, receipt),
                ["command"] = "add_file",
                ["document_id"] = documentId,
                ["path"] = new
                {
                    starting_point = "MAIN_DATABASE",
                    root = parentFolderDriveWsId,
                    path_components = new[] { fileName },
                },
                ["allow_conflict"] = true,
                ["file_flags"] = new[] { "IS_WIDGET_COMPATIBLE" },
                ["mtime"] = mtime,
                ["btime"] = mtime,
            };

            using var updateReq = new HttpRequestMessage(HttpMethod.Post,
                $"{_services.DocWsUrl}/ws/com.apple.CloudDocs/update/documents?clientId={_clientId}")
            {
                Content = new StringContent(JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "text/plain"),
            };
            using var updateResp = await _http.SendAsync(updateReq, ct);
            updateResp.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Cria uma pasta no iCloud Drive.</summary>
    public async Task CreateFolderAsync(string destinationDriveWsId, string name, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_services.DriveWsUrl}/createFolders")
        {
            Content = JsonContent.Create(new
            {
                folders = new[]
                {
                    new { destinationDriveWsId, name },
                },
            }),
        };
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Dictionary<string, object?> BuildFileData(
        string signature, string wrappingKey, string referenceChecksum, long size, string? receipt)
    {
        var data = new Dictionary<string, object?>
        {
            ["signature"] = signature,
            ["wrapping_key"] = wrappingKey,
            ["reference_signature"] = referenceChecksum,
            ["size"] = size,
        };
        if (receipt is not null)
        {
            data["receipt"] = receipt;
        }
        return data;
    }
}