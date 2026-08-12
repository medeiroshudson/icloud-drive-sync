using System.Net;
using System.Text;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Drive;

public class ICloudDriveClientUploadTests
{
    private static readonly WebServices Services = new("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws");

    private static ICloudDriveClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), Services);

    [Fact]
    public async Task UploadPerformsThreeStepsInOrder()
    {
        var steps = new List<string>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/upload/web"))
            {
                steps.Add("1.upload-web");
                Assert.Contains("token=tok-123", req.RequestUri.Query);
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"filename\":\"nota.txt\"", body);
                Assert.Contains("\"size\":5", body);
                return FakeHttpMessageHandler.JsonResponse("""[{"document_id":"doc-new-1","url":"https://content.example.com/up/1"}]""");
            }
            if (req.RequestUri.AbsoluteUri == "https://content.example.com/up/1")
            {
                steps.Add("2.content");
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.IsType<MultipartFormDataContent>(req.Content);
                return FakeHttpMessageHandler.JsonResponse("""{"singleFile":{"fileChecksum":"abc","wrappingKey":"key","referenceChecksum":"ref","size":5,"receipt":"rcp-1"}}""");
            }
            if (path.EndsWith("/update/documents"))
            {
                steps.Add("3.update-documents");
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"command\":\"add_file\"", body);
                Assert.Contains("\"document_id\":\"doc-new-1\"", body);
                Assert.Contains("\"path_components\":[\"nota.txt\"]", body);
                Assert.Contains("\"signature\":\"abc\"", body);
                Assert.Contains("\"receipt\":\"rcp-1\"", body);
                Assert.Contains("FOLDER::com.apple.CloudDocs::folder-1", body);
                return FakeHttpMessageHandler.JsonResponse("""{"docwsid":"doc-new-1"}""");
            }
            throw new InvalidOperationException("Request inesperado: " + req.RequestUri);
        });
        var client = CreateClient(handler);

        await client.UploadAsync(
            parentFolderDriveWsId: "FOLDER::com.apple.CloudDocs::folder-1",
            fileName: "nota.txt",
            size: 5,
            content: new MemoryStream("olá!!"u8.ToArray()),
            webauthToken: "tok-123");

        Assert.Equal(["1.upload-web", "2.content", "3.update-documents"], steps);
    }

    [Fact]
    public async Task UploadSendsTokenFromWebauthCookie()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/upload/web"))
            {
                Assert.Contains("token=abc", req.RequestUri.Query);
                return FakeHttpMessageHandler.JsonResponse("""[{"document_id":"d","url":"https://content.example.com/x"}]""");
            }
            if (req.RequestUri.AbsoluteUri == "https://content.example.com/x")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"singleFile":{"fileChecksum":"a","wrappingKey":"k","referenceChecksum":"r","size":1}}""");
            }
            return FakeHttpMessageHandler.JsonResponse("""{}""");
        });
        var client = CreateClient(handler);

        await client.UploadAsync("parent", "a.txt", 1, new MemoryStream("x"u8.ToArray()), webauthToken: "abc");

        Assert.Equal(3, handler.SentRequests.Count);
    }

    [Fact]
    public async Task UploadThrowsOnUploadWebError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.UploadAsync("parent", "a.txt", 1, new MemoryStream([1]), webauthToken: null));
    }
}