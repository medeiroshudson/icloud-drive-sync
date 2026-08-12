using System.Net;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;
using ICloudDriveSync.Tests.TestInfra;

namespace ICloudDriveSync.Tests.Sync;

public class ActionApplierTests : IDisposable
{
    private const string RootId = "FOLDER::com.apple.CloudDocs::root";

    private readonly string _localRoot = Directory.CreateTempSubdirectory("icloud-sync-applier-").FullName;

    [Fact]
    public async Task UploadWritesNewLocalFileToIcloud()
    {
        var expected = Path.Combine(_localRoot, "novo.txt");
        File.WriteAllText(expected, "conteudo novo");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/upload/web"))
            {
                return FakeHttpMessageHandler.JsonResponse("""[{"document_id":"d1","url":"https://content.example.com/up"}]""");
            }
            if (req.RequestUri.AbsoluteUri == "https://content.example.com/up")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"singleFile":{"fileChecksum":"c","wrappingKey":"k","referenceChecksum":"r","size":13}}""");
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/update/documents"))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"path_components\":[\"novo.txt\"]", body);
                Assert.Contains(RootId, body);
                return FakeHttpMessageHandler.JsonResponse("""{"docwsid":"d1"}""");
            }
            throw new InvalidOperationException("Requisição inesperada: " + req.RequestUri);
        });
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var applier = new ActionApplier(drive, _localRoot);

        await applier.ApplyAsync([new UploadAction("novo.txt")], new Dictionary<string, RemoteEntry>(), RootId);
    }

    [Fact]
    public async Task DownloadCreatesLocalFileAndSuppressesWatcher()
    {
        var remote = new Dictionary<string, RemoteEntry>
        {
            ["remoto.txt"] = new(
                DateModifiedUtc: DateTimeOffset.Parse("2026-08-10T12:00:00Z"), Size: 9, IsDirectory: false,
                Node: FileNode("doc-r", "remoto.txt")),
        };
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/download/by_id"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"data_token":{"url":"https://cdn.example.com/f"}}""");
            }
            Assert.Equal("https://cdn.example.com/f", req.RequestUri.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("conteudo"u8.ToArray()),
            };
        });
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var coalescer = new LocalChangeCoalescer();
        var applier = new ActionApplier(drive, _localRoot, coalescer: coalescer);

        await applier.ApplyAsync([new DownloadAction("remoto.txt")], remote, RootId);

        Assert.Equal("conteudo", File.ReadAllText(Path.Combine(_localRoot, "remoto.txt")));
        // Write local feito pelo sync não vira evento de watcher (supressão).
        Assert.Empty(coalescer.Drain());
    }

    [Fact]
    public async Task CreatesCloudFolderForNewLocalFolder()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/createFolders"))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("nova-pasta", body);
                Assert.Contains(RootId, body);
                return FakeHttpMessageHandler.JsonResponse("""{"folders":[{"drivewsid":"FOLDER::com.apple.CloudDocs::newf"}]}""");
            }
            throw new InvalidOperationException("Requisição inesperada: " + req.RequestUri);
        });
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var applier = new ActionApplier(drive, _localRoot);

        await applier.ApplyAsync([new MkDirCloudAction("nova-pasta")], new Dictionary<string, RemoteEntry>(), RootId);
    }

    [Fact]
    public async Task DryRunDoesNotWriteOrUpload()
    {
        var remote = new Dictionary<string, RemoteEntry>
        {
            ["remoto.txt"] = new(
                DateModifiedUtc: DateTimeOffset.Parse("2026-08-10T12:00:00Z"), Size: 9, IsDirectory: false,
                Node: FileNode("doc-r", "remoto.txt")),
        };
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("dry-run não deve tocar a rede"));
        var drive = new ICloudDriveClient(new HttpClient(handler), new WebServices("https://drivews.icloud.com", "https://docws.icloud.com/drive/ws"));
        var applier = new ActionApplier(drive, _localRoot, dryRun: true);

        await applier.ApplyAsync(
            [new DownloadAction("remoto.txt"), new UploadAction("local.txt"), new DeleteLocalAction("velho.txt")],
            remote, RootId);

        Assert.False(File.Exists(Path.Combine(_localRoot, "remoto.txt")));
    }

    [Fact]
    public async Task DeleteLocalTrashItemRemovesFolderRecursively()
    {
        var victim = Path.Combine(_localRoot, "pasta-lixo");
        Directory.CreateDirectory(Path.Combine(victim, "sub"));
        File.WriteAllText(Path.Combine(victim, "a.txt"), "x");
        File.WriteAllText(Path.Combine(victim, "sub", "b.txt"), "y");
        var applier = new ActionApplier(new ICloudDriveClient(new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException())), new WebServices("x", "y")), _localRoot);

        await applier.ApplyAsync([new DeleteLocalAction("pasta-lixo")], new Dictionary<string, RemoteEntry>(), RootId);

        Assert.False(Directory.Exists(victim));
    }

    [Fact]
    public async Task DeleteLocalTrashItemRemovesFile()
    {
        var victim = Path.Combine(_localRoot, "velho.txt");
        File.WriteAllText(victim, "x");
        var applier = new ActionApplier(new ICloudDriveClient(new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException())), new WebServices("x", "y")), _localRoot);

        await applier.ApplyAsync([new DeleteLocalAction("velho.txt")], new Dictionary<string, RemoteEntry>(), RootId);

        Assert.False(File.Exists(victim));
    }

    private static DriveNode FileNode(string docWsId, string name) => new(
        DriveWsId: $"FILE::com.apple.CloudDocs::{docWsId}", DocWsId: docWsId,
        ParentDriveWsId: RootId, Etag: "e", Extension: "txt", Name: name, Type: "FILE", Size: 9,
        DateChanged: null, DateModified: DateTimeOffset.Parse("2026-08-10T12:00:00Z"), FileCount: null);

    public void Dispose()
    {
        try { Directory.Delete(_localRoot, recursive: true); } catch { /* best effort */ }
    }
}