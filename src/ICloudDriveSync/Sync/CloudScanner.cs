using ICloudDriveSync.Drive;

namespace ICloudDriveSync.Sync;

/// <summary>Varre a árvore do iCloud Drive (recursivo) e o sanity check de fileCount.</summary>
public sealed class CloudScanner(ICloudDriveClient drive)
{
    public Task<long?> GetRootFileCountAsync(string rootFolderDriveWsId, CancellationToken ct = default) =>
        drive.GetFileCountAsync(rootFolderDriveWsId, ct);

    public async Task<Dictionary<string, RemoteEntry>> ScanRootAsync(string rootFolderDriveWsId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, RemoteEntry>();
        await WalkAsync(rootFolderDriveWsId, string.Empty, result, ct);
        return result;
    }

    private async Task WalkAsync(string folderDriveWsId, string prefix, Dictionary<string, RemoteEntry> result, CancellationToken ct)
    {
        var children = await drive.GetChildrenAsync(folderDriveWsId, ct);
        foreach (var node in children)
        {
            ct.ThrowIfCancellationRequested();

            var rel = prefix.Length == 0 ? node.Name : $"{prefix}/{node.Name}";
            if (rel.Length == 0 || PathRules.ShouldIgnore(rel))
            {
                continue;
            }

            var dateModified = node.DateModified is { } modified
                ? TimestampRules.RoundSeconds(modified)
                : DateTimeOffset.MinValue;

            if (node.IsFolder)
            {
                result[rel] = new RemoteEntry(dateModified, 0, true, node);
                await WalkAsync(node.DriveWsId, rel, result, ct);
            }
            else
            {
                result[rel] = new RemoteEntry(dateModified, node.Size, false, node);
            }
        }
    }
}