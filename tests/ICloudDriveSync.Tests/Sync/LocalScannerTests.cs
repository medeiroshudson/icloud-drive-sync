using ICloudDriveSync.Sync;

namespace ICloudDriveSync.Tests.Sync;

public class LocalScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("icloud-sync-test-").FullName;

    [Fact]
    public void ScanBuildsTreeWithFilesAndFolders()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "olá");
        Directory.CreateDirectory(Path.Combine(_root, "pasta"));
        File.WriteAllText(Path.Combine(_root, "pasta", "b.txt"), "x");

        var tree = new LocalScanner(_root).Scan();

        Assert.True(tree["a.txt"] is { IsDirectory: false, Size: 4 });
        Assert.True(tree["pasta"].IsDirectory);
        Assert.True(tree["pasta/b.txt"] is { IsDirectory: false, Size: 1 });
    }

    [Fact]
    public void ScanIgnoresDsStoreAndAppleBird()
    {
        File.WriteAllText(Path.Combine(_root, ".DS_Store"), "");
        File.WriteAllText(Path.Combine(_root, ".com-apple-bird-abc"), "");

        var tree = new LocalScanner(_root).Scan();

        Assert.DoesNotContain(".DS_Store", tree.Keys);
        Assert.DoesNotContain(".com-apple-bird-abc", tree.Keys);
    }

    [Fact]
    public void ScanUsesForwardSlashSeparators()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "c.txt"), "x");

        var tree = new LocalScanner(_root).Scan();

        Assert.True(tree.ContainsKey("sub/c.txt"));
    }

    [Fact]
    public void ScanIgnoresDotfilePaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "config"), "x");
        File.WriteAllText(Path.Combine(_root, ".env"), "x");
        File.WriteAllText(Path.Combine(_root, "notas.md"), "x");

        var tree = new LocalScanner(_root).Scan();

        Assert.DoesNotContain(tree.Keys, k => k.Contains(".git"));
        Assert.DoesNotContain(".env", tree.Keys);
        Assert.True(tree.ContainsKey("notas.md"));
    }

    [Fact]
    public void ScanRoundsTimestampsToSeconds()
    {
        var path = Path.Combine(_root, "a.txt");
        File.WriteAllText(path, "x");
        var original = new DateTimeOffset(2026, 8, 10, 12, 0, 0, 700, TimeSpan.Zero).UtcDateTime;
        File.SetLastWriteTimeUtc(path, original);

        var entry = new LocalScanner(_root).Scan()["a.txt"];

        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:01Z"), entry.ModifiedUtc);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}