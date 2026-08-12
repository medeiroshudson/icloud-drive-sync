using System.Net;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Drive;

public class ICloudDriveClientDownloadTests
{
    private const string DocWsUrl = "https://docws.icloud.com/drive/ws";
    private static readonly WebServices Services = new("https://drivews.icloud.com", DocWsUrl);

    private static readonly DriveNode FileNode = new(
        DriveWsId: "FILE::com.apple.CloudDocs::doc-1",
        DocWsId: "doc-1",
        ParentDriveWsId: "FOLDER::com.apple.CloudDocs::root",
        Etag: "etag-1",
        Extension: "txt",
        Name: "nota.txt",
        Type: "FILE",
        Size: 11,
        DateChanged: null,
        DateModified: null,
        FileCount: null);

    private static ICloudDriveClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), Services);

    [Fact]
    public async Task DownloadRequestsDocumentThenReturnsContentStream()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/download/by_id"))
            {
                Assert.Equal(HttpMethod.Get, req.Method);
                Assert.Equal("doc-1", req.RequestUri.Query.TrimStart('?').Split('&').Select(p => p.Split('='))
                    .First(kv => kv[0] == "document_id")[1]);
                return FakeHttpMessageHandler.JsonResponse("""{"data_token":{"url":"https://cdn.example.com/data"},"package_token":{"url":"https://cdn.example.com/pkg"}}""");
            }
            Assert.Equal("https://cdn.example.com/data", req.RequestUri.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("conteudo"u8.ToArray()),
            };
        });
        var client = CreateClient(handler);

        using var stream = await client.DownloadAsync(FileNode);

        using var reader = new StreamReader(stream);
        Assert.Equal("conteudo", await reader.ReadToEndAsync());
        Assert.Equal(2, handler.SentRequests.Count);
    }

    [Fact]
    public async Task DownloadUsesPackageTokenWhenNoDataToken()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/download/by_id"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"package_token":{"url":"https://cdn.example.com/pkg"}}""");
            }
            Assert.Equal("https://cdn.example.com/pkg", req.RequestUri.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("pacote"u8.ToArray()),
            };
        });
        var client = CreateClient(handler);

        using var stream = await client.DownloadAsync(FileNode);

        using var reader = new StreamReader(stream);
        Assert.Equal("pacote", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task DownloadThrowsWhenNoTokenReturned()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(FileNode));

        Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadThrowsOnTokenRequestHttpError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(FileNode));
    }
}
