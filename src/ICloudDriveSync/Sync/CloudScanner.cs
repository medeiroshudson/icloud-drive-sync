using ICloudDriveSync.Drive;

namespace ICloudDriveSync.Sync;

/// <summary>
/// Varre a árvore do iCloud Drive (recursivo), a lixeira e o sanity check de fileCount.
/// APP_LIBRARY só entra na árvore quando includeAppLibrary=true (default: não sincronizar apps).
/// </summary>
public sealed class CloudScanner(
    ICloudDriveClient drive,
    bool includeAppLibrary = false,
    IgnoreRules? ignoreRules = null)
{
    public const string TrashRootId = "FOLDER::com.apple.CloudDocs::TRASH_ROOT";

    private readonly IgnoreRules _rules = ignoreRules ?? IgnoreRules.Defaults;

    public Task<long?> GetRootFileCountAsync(string rootFolderDriveWsId, CancellationToken ct = default) =>
        drive.GetFileCountAsync(rootFolderDriveWsId, ct);

    public async Task<Dictionary<string, RemoteEntry>> ScanRootAsync(string rootFolderDriveWsId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, RemoteEntry>();
        await WalkAsync(rootFolderDriveWsId, string.Empty, result, ct);
        return result;
    }

    /// <summary>
    /// Lê a lixeira do iCloud (TRASH_ROOT) e devolve as paths originais (restorePath)
    /// dos itens — usadas pelo diff para deletar a cópia local. Itens sem restorePath
    /// são pulados (não sabemos de onde vieram), como faz o icloudds.
    /// </summary>
    public async Task<HashSet<string>> ScanTrashAsync(CancellationToken ct = default)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var items = await drive.GetChildrenAsync(TrashRootId, ct);
        foreach (var node in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(node.RestorePath))
            {
                result.Add(PathRules.Normalize(node.RestorePath));
            }
        }
        return result;
    }

    private async Task WalkAsync(string folderDriveWsId, string prefix, Dictionary<string, RemoteEntry> result, CancellationToken ct)
    {
        var children = await drive.GetChildrenAsync(folderDriveWsId, ct);
        foreach (var node in children)
        {
            ct.ThrowIfCancellationRequested();

            var rel = prefix.Length == 0 ? node.EffectiveName : $"{prefix}/{node.EffectiveName}";
            if (rel.Length == 0 || _rules.IsIgnored(rel))
            {
                continue;
            }

            var dateModified = node.DateModified is { } modified
                ? TimestampRules.RoundSeconds(modified)
                : DateTimeOffset.MinValue;

            if (node.IsFolder || (node.Type == "APP_LIBRARY" && includeAppLibrary))
            {
                result[rel] = new RemoteEntry(dateModified, 0, true, node);
                await WalkAsync(node.DriveWsId, rel, result, ct);
            }
            else if (node.Type == "APP_LIBRARY")
            {
                // App library desabilitada: não sincroniza dados de apps.
                continue;
            }
            else
            {
                result[rel] = new RemoteEntry(dateModified, node.Size, false, node);
            }
        }
    }
}