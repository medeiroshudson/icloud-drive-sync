using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;

namespace ICloudDriveSync.Tests.Sync;

public class DiffEngineTests
{
    private static readonly DateTimeOffset Base = DateTimeOffset.Parse("2026-08-10T12:00:00Z");

    private static readonly DriveNode FileNode = new(
        DriveWsId: "FILE::com.apple.CloudDocs::doc-1", DocWsId: "doc-1",
        ParentDriveWsId: "FOLDER::com.apple.CloudDocs::root", Etag: "etag-1",
        Extension: "txt", Name: "arquivo.txt", Type: "FILE", Size: 100,
        DateChanged: null, DateModified: Base, FileCount: null);

    private static readonly DriveNode FolderNode = new(
        DriveWsId: "FOLDER::com.apple.CloudDocs::folder-1", DocWsId: "folder-1",
        ParentDriveWsId: "FOLDER::com.apple.CloudDocs::root", Etag: "etag-2",
        Extension: null, Name: "pasta", Type: "FOLDER", Size: 0,
        DateChanged: null, DateModified: Base, FileCount: 1);

    private static LocalEntry Local(string path, DateTimeOffset modified, long size = 100, bool dir = false) =>
        new(modified, size, dir);

    private static RemoteEntry Remote(string path, DateTimeOffset modified, long size = 100, bool dir = false) =>
        new(modified, size, dir, dir ? FolderNode : FileNode);

    private readonly DiffEngine _engine = new();

    [Fact]
    public void NewLocalFileProducesUpload()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["novo.txt"] = Local("novo.txt", Base) },
            new Dictionary<string, RemoteEntry>());

        Assert.Equal([new UploadAction("novo.txt")], actions);
    }

    [Fact]
    public void NewRemoteFileProducesDownload()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry>(),
            new Dictionary<string, RemoteEntry> { ["remoto.txt"] = Remote("remoto.txt", Base) });

        Assert.Equal([new DownloadAction("remoto.txt")], actions);
    }

    [Fact]
    public void NewLocalFolderProducesMkDirCloud()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["pasta"] = Local("pasta", Base, dir: true) },
            new Dictionary<string, RemoteEntry>());

        Assert.Equal([new MkDirCloudAction("pasta")], actions);
    }

    [Fact]
    public void NewRemoteFolderProducesMkDirLocal()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry>(),
            new Dictionary<string, RemoteEntry> { ["pasta"] = Remote("pasta", Base, dir: true) });

        Assert.Equal([new MkDirLocalAction("pasta")], actions);
    }

    [Fact]
    public void LocalNewerFileProducesUpload()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["a.txt"] = Local("a.txt", Base.AddMinutes(5)) },
            new Dictionary<string, RemoteEntry> { ["a.txt"] = Remote("a.txt", Base) });

        Assert.Equal([new UploadAction("a.txt")], actions);
    }

    [Fact]
    public void RemoteNewerFileProducesDownload()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["a.txt"] = Local("a.txt", Base) },
            new Dictionary<string, RemoteEntry> { ["a.txt"] = Remote("a.txt", Base.AddMinutes(5)) });

        Assert.Equal([new DownloadAction("a.txt")], actions);
    }

    [Fact]
    public void EqualFilesProduceNoAction()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["a.txt"] = Local("a.txt", Base) },
            new Dictionary<string, RemoteEntry> { ["a.txt"] = Remote("a.txt", Base) });

        Assert.Empty(actions);
    }

    [Fact]
    public void SameMtimeDifferentSizeProducesNoAction()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["a.txt"] = Local("a.txt", Base, size: 10) },
            new Dictionary<string, RemoteEntry> { ["a.txt"] = Remote("a.txt", Base, size: 200) });

        Assert.Empty(actions);
    }

    [Fact]
    public void LocalNewerZeroByteFileDoesNotUpload()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["vazio.txt"] = Local("vazio.txt", Base.AddMinutes(5), size: 0) },
            new Dictionary<string, RemoteEntry> { ["vazio.txt"] = Remote("vazio.txt", Base, size: 0) });

        Assert.Empty(actions);
    }

    [Fact]
    public void IgnoresDsStoreComAppleBirdAndAppLibrary()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry>
            {
                ["pasta/.DS_Store"] = Local("pasta/.DS_Store", Base),
                [".com-apple-bird-abc"] = Local(".com-apple-bird-abc", Base),
            },
            new Dictionary<string, RemoteEntry>
            {
                ["app_library/pasta"] = Remote("app_library/pasta", Base),
            });

        Assert.Empty(actions);
    }

    [Fact]
    public void TrashPathsProduceDeleteLocal()
    {
        var actions = _engine.Diff(
            new Dictionary<string, LocalEntry> { ["velho.txt"] = Local("velho.txt", Base) },
            new Dictionary<string, RemoteEntry>(),
            pathsInTrash: new HashSet<string> { "velho.txt" });

        Assert.Equal([new DeleteLocalAction("velho.txt")], actions);
    }

    [Fact]
    public void RoundSecondsRoundsHalfSecondUp()
    {
        var dt = DateTimeOffset.Parse("2026-08-10T12:00:00.700Z");

        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:01Z"), TimestampRules.RoundSeconds(dt));
    }

    [Fact]
    public void RoundSecondsTruncatesBelowHalfSecond()
    {
        var dt = DateTimeOffset.Parse("2026-08-10T12:00:00.400Z");

        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:00Z"), TimestampRules.RoundSeconds(dt));
    }

    [Fact]
    public void AppliesIgnoreRulesFromFile()
    {
        var local = new Dictionary<string, LocalEntry>
        {
            ["backup.bak"] = Local("backup.bak", Base),
            ["nota.txt"] = Local("nota.txt", Base),
        };
        var remote = new Dictionary<string, RemoteEntry>
        {
            ["relatorio.bak"] = Remote("relatorio.bak", Base),
            ["relatorio.pdf"] = Remote("relatorio.pdf", Base),
        };

        var actions = _engine.Diff(local, remote, ignoreRules: IgnoreRules.FromLines(["*.bak"]));

        Assert.DoesNotContain(actions, a => a is UploadAction u && u.Path.EndsWith(".bak"));
        Assert.DoesNotContain(actions, a => a is DownloadAction d && d.Path.EndsWith(".bak"));
        Assert.Contains(actions, a => a is UploadAction u && u.Path == "nota.txt");
        Assert.Contains(actions, a => a is DownloadAction d && d.Path == "relatorio.pdf");
    }

    [Fact]
    public void AppliesIgnoreRegexesToNewAndExistingPaths()
    {
        var local = new Dictionary<string, LocalEntry>
        {
            ["backup.tmp"] = Local("backup.tmp", Base),
            ["nota.txt"] = Local("nota.txt", Base),
        };
        var remote = new Dictionary<string, RemoteEntry>
        {
            ["relatorio.tmp"] = Remote("relatorio.tmp", Base),
            ["relatorio.pdf"] = Remote("relatorio.pdf", Base),
        };

        var actions = _engine.Diff(local, remote, ignoreRegexes: ["\\.tmp$"]);

        Assert.DoesNotContain(actions, a => a is UploadAction u && u.Path.EndsWith(".tmp"));
        Assert.DoesNotContain(actions, a => a is DownloadAction d && d.Path.EndsWith(".tmp"));
        Assert.Contains(actions, a => a is UploadAction u && u.Path == "nota.txt");
        Assert.Contains(actions, a => a is DownloadAction d && d.Path == "relatorio.pdf");
    }
}
