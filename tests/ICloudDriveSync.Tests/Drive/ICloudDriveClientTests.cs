using System.Net;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Drive;

public class ICloudDriveClientTests
{
    private const string DriveWsUrl = "https://drivews.icloud.com";
    private static readonly WebServices Services = new(DriveWsUrl, "https://docws.icloud.com/drive/ws");

    private static ICloudDriveClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), Services);

    private const string FolderItemsResponse = """
    [
      {
        "drivewsid": "FOLDER::com.apple.CloudDocs::root",
        "etag": "etag-root",
        "name": "",
        "type": "FOLDER",
        "fileCount": 2,
        "items": [
          {
            "drivewsid": "FILE::com.apple.CloudDocs::doc-aaa",
            "docwsid": "doc-aaa",
            "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root",
            "etag": "etag-1",
            "extension": "pdf",
            "name": "CV Hudson",
            "type": "FILE",
            "size": 123456,
            "dateChanged": "2026-08-09T20:00:00Z",
            "dateModified": "2026-08-09T20:00:00Z"
          },
          {
            "drivewsid": "FOLDER::com.apple.CloudDocs::folder-bbb",
            "docwsid": "folder-bbb",
            "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root",
            "etag": "etag-2",
            "name": "Documentos",
            "type": "FOLDER",
            "fileCount": 3,
            "dateChanged": "2026-08-10T01:02:03Z",
            "dateModified": "2026-08-10T01:02:03Z"
          }
        ]
      }
    ]
    """;

    [Fact]
    public async Task GetChildrenPostsRetrieveItemDetailsWithFolderId()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/retrieveItemDetailsInFolders", req.RequestUri!.AbsolutePath);
            Assert.Equal(DriveWsUrl, req.RequestUri.GetLeftPart(UriPartial.Authority));
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("FOLDER::com.apple.CloudDocs::root", body);
            Assert.Contains("\"partialData\":false", body);
            Assert.StartsWith("[", body.Trim());
            return JsonResponse(FolderItemsResponse);
        });
        var client = CreateClient(handler);

        var items = await client.GetChildrenAsync("FOLDER::com.apple.CloudDocs::root");

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetChildrenParsesFileAndFolderFields()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(FolderItemsResponse));
        var client = CreateClient(handler);

        var items = await client.GetChildrenAsync("root");

        var file = items[0];
        Assert.Equal("FILE::com.apple.CloudDocs::doc-aaa", file.DriveWsId);
        Assert.Equal("CV Hudson", file.Name);
        Assert.Equal("FILE", file.Type);
        Assert.Equal(123456, file.Size);
        Assert.Equal("etag-1", file.Etag);
        Assert.Equal("pdf", file.Extension);
        Assert.Equal(DateTimeOffset.Parse("2026-08-09T20:00:00Z"), file.DateModified);

        var folder = items[1];
        Assert.Equal("FOLDER", folder.Type);
        Assert.Equal(3, folder.FileCount);
    }

    [Fact]
    public async Task GetChildrenReturnsEmptyWhenNoItems()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var client = CreateClient(handler);

        var items = await client.GetChildrenAsync("root");

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetFileCountReadsFromNodeResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(FolderItemsResponse));
        var client = CreateClient(handler);

        var fileCount = await client.GetFileCountAsync("FOLDER::com.apple.CloudDocs::root");

        Assert.Equal(2, fileCount);
    }

    [Fact]
    public async Task GetChildrenThrowsOnHttpError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetChildrenAsync("root"));
    }

    private static HttpResponseMessage JsonResponse(string json) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}
