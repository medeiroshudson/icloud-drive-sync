using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Sync;

public class CloudScannerTests
{
    private const string RootId = "FOLDER::com.apple.CloudDocs::root";
    private const string TrashRootId = "FOLDER::com.apple.CloudDocs::TRASH_ROOT";

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
    public async Task ScanKeepsFileExtensionInPath()
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
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-1", "docwsid": "doc-1",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e1",
                        "name": "relatorio", "extension": "pdf", "type": "FILE", "size": 100,
                        "dateModified": "2026-08-10T12:00:00Z"
                      },
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-2", "docwsid": "doc-2",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e2",
                        "name": "sem_ext", "type": "FILE", "size": 10,
                        "dateModified": "2026-08-10T12:00:00Z"
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

        Assert.True(tree.ContainsKey("relatorio.pdf"), "path remoto deve incluir a extensão (name + extension)");
        Assert.True(tree.ContainsKey("sem_ext"));
        Assert.False(tree.ContainsKey("relatorio"));
    }

    [Fact]
    public async Task ScanSkipsAppLibraryByDefault()
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
                        "drivewsid": "FOLDER::com.apple.Automator", "docwsid": "automator",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e1",
                        "name": "Automator", "type": "APP_LIBRARY", "fileCount": 5,
                        "dateModified": "2026-08-10T12:00:00Z"
                      },
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-2", "docwsid": "doc-2",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e2",
                        "name": "notas.txt", "type": "FILE", "size": 10,
                        "dateModified": "2026-08-10T12:00:00Z"
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

        Assert.True(tree.ContainsKey("notas.txt"));
        Assert.DoesNotContain(tree.Keys, k => k.Contains("Automator"));
    }

    [Fact]
    public async Task ScanIncludesAppLibraryWhenConfigured()
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
                    "fileCount": 1,
                    "items": [
                      {
                        "drivewsid": "FOLDER::com.apple.Automator", "docwsid": "automator",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e1",
                        "name": "Automator", "type": "APP_LIBRARY", "fileCount": 1,
                        "dateModified": "2026-08-10T12:00:00Z"
                      }
                    ]
                  }
                ]
                """);
            }
            if (body.Contains("FOLDER::com.apple.Automator"))
            {
                return FakeHttpMessageHandler.JsonResponse("""
                [
                  {
                    "drivewsid": "FOLDER::com.apple.Automator",
                    "type": "APP_LIBRARY",
                    "fileCount": 1,
                    "items": [
                      {
                        "drivewsid": "FILE::com.apple.Automator::doc-1", "docwsid": "doc-1",
                        "parentDrivewsid": "FOLDER::com.apple.Automator", "etag": "e2",
                        "name": "algum.txt", "type": "FILE", "size": 5,
                        "dateModified": "2026-08-10T12:00:00Z"
                      }
                    ]
                  }
                ]
                """);
            }
            throw new InvalidOperationException("Requisição inesperada: " + body);
        });
        var scanner = new CloudScanner(DriveClient(handler), includeAppLibrary: true);

        var tree = await scanner.ScanRootAsync(RootId);

        Assert.True(tree.ContainsKey("Automator"));
        Assert.True(tree.ContainsKey("Automator/algum.txt"));
    }

    [Fact]
    public async Task ScanSkipsDotfileEntries()
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
                    "fileCount": 3,
                    "items": [
                      {
                        "drivewsid": "FOLDER::com.apple.CloudDocs::folder-dot", "docwsid": "folder-dot",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e1",
                        "name": ".metadata", "type": "FOLDER", "fileCount": 2,
                        "dateModified": "2026-08-10T12:00:00Z"
                      },
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::doc-2", "docwsid": "doc-2",
                        "parentDrivewsid": "FOLDER::com.apple.CloudDocs::root", "etag": "e2",
                        "name": "notas.txt", "type": "FILE", "size": 10,
                        "dateModified": "2026-08-10T12:00:00Z"
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

        Assert.True(tree.ContainsKey("notas.txt"));
        Assert.DoesNotContain(tree.Keys, k => k.StartsWith(".metadata"));
        Assert.DoesNotContain(tree.Keys, k => k.Contains("/."));
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

    [Fact]
    public async Task ScanTrashReturnsRestorePathsOnly()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains(TrashRootId))
            {
                return FakeHttpMessageHandler.JsonResponse("""
                [
                  {
                    "drivewsid": "FOLDER::com.apple.CloudDocs::TRASH_ROOT",
                    "type": "FOLDER",
                    "fileCount": 2,
                    "items": [
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::t1", "docwsid": "t1",
                        "parentDrivewsid": "TRASH_ROOT", "etag": "e1",
                        "name": ".ai-configuration.json", "extension": "bak", "type": "FILE", "size": 555,
                        "restorePath": "Workspace/Viveo/DBeaver/Workspace/.metadata/.config/.ai-configuration.json.bak",
                        "dateModified": "2026-07-13T18:11:27Z"
                      },
                      {
                        "drivewsid": "FILE::com.apple.CloudDocs::t2", "docwsid": "t2",
                        "parentDrivewsid": "TRASH_ROOT", "etag": "e2",
                        "name": "sem-origem", "type": "FILE", "size": 1,
                        "dateModified": "2026-07-13T18:11:27Z"
                      }
                    ]
                  }
                ]
                """);
            }
            throw new InvalidOperationException("Requisição inesperada: " + body);
        });
        var scanner = new CloudScanner(DriveClient(handler));

        var paths = await scanner.ScanTrashAsync();

        Assert.Equal(
            new HashSet<string> { "Workspace/Viveo/DBeaver/Workspace/.metadata/.config/.ai-configuration.json.bak" },
            paths);
    }
}