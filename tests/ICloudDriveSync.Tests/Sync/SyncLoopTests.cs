using ICloudDriveSync.Auth;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Sync;

public class SyncLoopTests : IDisposable
{
    private const string RootId = "FOLDER::com.apple.CloudDocs::root";
    private const string DriveWsUrl = "https://drivews.icloud.com";
    private const string DocWsUrl = "https://docws.icloud.com/drive/ws";

    private readonly string _localRoot = Directory.CreateTempSubdirectory("icloud-sync-loop-").FullName;

    /// <summary>Handler padrão: roteia download (token + cdn) e responde retrieveItemDetails com folder.</summary>
    private static FakeHttpMessageHandler DefaultHandler(Func<int>? fileCountProvider = null, string fileName = "remoto.txt", string content = "baixado") =>
        new(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/download/by_id"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"data_token":{"url":"https://cdn.example.com/f"}}""");
            }
            if (req.RequestUri.AbsoluteUri == "https://cdn.example.com/f")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content)),
                };
            }
            return FakeHttpMessageHandler.JsonResponse(FolderWithFile("doc1", fileName, 7, (fileCountProvider ?? (() => 1))()));
        });

    [Fact]
    public async Task FirstRunAlwaysRefreshesAndApplies()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/download/by_id"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"data_token":{"url":"https://cdn.example.com/f"}}""");
            }
            if (req.RequestUri.AbsoluteUri == "https://cdn.example.com/f")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("baixado"u8.ToArray()),
                };
            }
            // retrieveItemDetailsInFolders: root com 1 arquivo remoto.
            return FakeHttpMessageHandler.JsonResponse(FolderWithFile("doc1", "remoto.txt", 7, 1));
        });
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var loop = CreateLoop(drive);

        await loop.RunOnceAsync(RootId);

        Assert.Equal("baixado", File.ReadAllText(Path.Combine(_localRoot, "remoto.txt")));
    }

    [Fact]
    public async Task SkipsRefreshWhenFileCountUnchanged()
    {
        var handler = DefaultHandler();
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices(DriveWsUrl, DocWsUrl));
        var loop = CreateLoop(drive);

        await loop.RunOnceAsync(RootId);
        var requestsAfterFirst = handler.SentRequests.Count;

        await loop.RunOnceAsync(RootId);

        // 2º RunOnce: apenas o sanity check de fileCount (1 request), sem scan completo.
        Assert.Equal(requestsAfterFirst + 1, handler.SentRequests.Count);
    }

    [Fact]
    public async Task RefreshesWhenFileCountChanges()
    {
        var current = 1;
        var handler = DefaultHandler(() => current);
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices(DriveWsUrl, DocWsUrl));
        var loop = CreateLoop(drive);

        await loop.RunOnceAsync(RootId);
        var requestsAfterFirst = handler.SentRequests.Count;
        current = 2;

        await loop.RunOnceAsync(RootId);

        Assert.True(handler.SentRequests.Count > requestsAfterFirst + 1, "mudança de fileCount deve disparar scan completo");
    }

    [Fact]
    public async Task RefreshesWhenPeriodElapsesEvenIfFileCountUnchanged()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = DefaultHandler();
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices(DriveWsUrl, DocWsUrl));
        var loop = CreateLoop(drive, refreshPeriod: TimeSpan.FromMinutes(10), clock: () => now);

        await loop.RunOnceAsync(RootId);
        var requestsAfterFirst = handler.SentRequests.Count;

        now = now.AddMinutes(11);
        await loop.RunOnceAsync(RootId);

        Assert.True(handler.SentRequests.Count > requestsAfterFirst + 1, "período decorrido deve forçar refresh mesmo sem mudança de fileCount");
    }

    [Fact]
    public async Task UploadsNewLocalFileToEmptyIcloud()
    {
        File.WriteAllText(Path.Combine(_localRoot, "local.txt"), "novo");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/download/by_id"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"data_token":{"url":"https://cdn.example.com/f"}}""");
            }
            if (req.RequestUri.AbsoluteUri == "https://cdn.example.com/f")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("novo"u8.ToArray()),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/upload/web"))
            {
                return FakeHttpMessageHandler.JsonResponse("""[{"document_id":"d1","url":"https://content.example.com/up"}]""");
            }
            if (req.RequestUri.AbsoluteUri == "https://content.example.com/up")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"singleFile":{"fileChecksum":"c","wrappingKey":"k","referenceChecksum":"r","size":4}}""");
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/update/documents"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"docwsid":"d1"}""");
            }
            return FakeHttpMessageHandler.JsonResponse(FolderWithFile("dummy", "x.txt", 1, 0));
        });
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var loop = CreateLoop(drive);

        await loop.RunOnceAsync(RootId);

        var updateCalls = handler.SentRequests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/update/documents"));
        Assert.Equal(1, updateCalls);
    }

    private SyncLoop CreateLoop(ICloudDriveClient drive, TimeSpan? refreshPeriod = null, Func<DateTimeOffset>? clock = null) =>
        new(
            cloud: new CloudScanner(drive),
            local: new LocalScanner(_localRoot),
            applier: new ActionApplier(drive, _localRoot),
            refreshPeriod: refreshPeriod ?? TimeSpan.FromMinutes(10),
            now: clock ?? (() => new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));

    private static string FolderWithFile(string docWsId, string name, long size, int fileCount) => $$"""
    [
      {
        "drivewsid": "{{RootId}}",
        "type": "FOLDER",
        "fileCount": {{fileCount}},
        "items": [
          {
            "drivewsid": "FILE::com.apple.CloudDocs::{{docWsId}}",
            "docwsid": "{{docWsId}}",
            "parentDrivewsid": "{{RootId}}",
            "etag": "e",
            "name": "{{name}}",
            "type": "FILE",
            "size": {{size}},
            "dateModified": "2026-08-10T11:00:00Z"
          }
        ]
      }
    ]
    """;

    public void Dispose()
    {
        try { Directory.Delete(_localRoot, recursive: true); } catch { /* best effort */ }
    }
}