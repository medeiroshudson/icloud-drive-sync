using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Sync;

public class CloudScannerTests
{
    private const string RootId = "FOLDER::com.apple.CloudDocs::root";

    private static ICloudDriveClient DriveClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), new (DriveWsUrl: "https://drivews.icloud.com", DocWsUrl: "https://docws.icloud.com/drive/ws"));

    [Fact]
    public async Task ScanBuildsTreeRecursively()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains(RootId))
            {
                return FakeHttpMessageHandler.JsonResponse("""
                [
                  {
                    "drivewsid": "FOLDER::com.apple.CloudDocs::root",
                    "type": "FOLDER",
                    "fileCount": 2,
                    "items": [
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-aaa", "docwsid": "doc-aaa",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e1",
                        "name": "relatorio.pdf", "type": "FILE", "size": 100,
                        "dateModified": "2026-08-10T12:00:00Z"
                      },
                      {
                        "drivewsid": "FOLDER::com.apple.CloudDocs::folder-bbb", "docwsid": "folder-bbb",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e2",
                        "name": "Documentos", "type": "FOLDER", "fileCount": 1,
                        "dateModified": "2026-08-10T12:00:00Z"
                      }
                    ]
                  }
                ]
                """);
            }
            if (body.Contains("folder-bbb"))
            {
                return FakeHttpMessageHandler.JsonResponse("""
                [
                  {
                    "drivewsid": "FOLDER::com.apple.CloudDocs::folder-bbb",
                    "type": "FOLDER",
                    "fileCount": 1,
                    "items": [
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-inner", "docwsid": "doc-inner",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::folder-bbb", "etag": "e3",
                        "name": "inner.txt", "type": "FILE", "size": 7,
                        "dateModified": "2026-08-10T13:00:00Z"
                      }
                    ]
                  }
                ]
                """);
            }
            throw new InvalidOperationException("Requisição inesperada: " + body);
        });
        var scanner = new CloudScanner(DriveClient(handler));

        var tree = await scanner.ScanRootAsync(RootId);

        Assert.True(tree.ContainsKey("relatorio.pdf"));
        Assert.True(tree.ContainsKey("Documentos"));
        Assert.True(tree.ContainsKey("Documentos/inner.txt"));
        Assert.True(tree["Documentos"].IsDirectory);
        Assert.Equal(7, tree["Documentos/inner.txt"].Size);
    }

    [Fact]
    public async Task GetRootFileCountReadsFromIcloud()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse(
            """[{"drivewsid":"FOLDER::com.apple.CloudDocs::root","type":"FOLDER","fileCount":5,"items":[]}]"""));
        var scanner = new CloudScanner(DriveClient(handler));

        var count = await scanner.GetRootFileCountAsync(RootId);

        Assert.Equal(5, count);
    }
}