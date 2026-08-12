using ICloudDriveSync.Drive;

namespace ICloudDriveSync.Sync;

/// <summary>
/// Executa o plano de ações do DiffEngine. Escritas no iCloud são serializadas
/// (WriteGate — ZONE_BUSY); writes locais suprimem o coalescer (evita loop).
/// </summary>
public sealed class ActionApplier(
    ICloudDriveClient drive,
    string localRoot,
    WriteGate? gate = null,
    LocalChangeCoalescer? coalescer = null,
    string? webauthToken = null,
    bool dryRun = false)
{
    private readonly WriteGate _gate = gate ?? new WriteGate();
    private readonly LocalChangeCoalescer? _coalescer = coalescer;

    public async Task ApplyAsync(
        IReadOnlyList<SyncAction> actions,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        string rootFolderDriveWsId,
        CancellationToken ct = default)
    {
        // Pastas primeiro (uploads dependem da pasta pai existir no iCloud).
        var ordered = actions.OrderBy(a => a is MkDirCloudAction or MkDirLocalAction ? 0 : 1);
        foreach (var action in ordered)
        {
            Console.WriteLine($"[{(dryRun ? "DRY-RUN" : "sync")}] {Describe(action)}");
            if (dryRun)
            {
                continue;
            }
            await ApplyOneAsync(action, remote, rootFolderDriveWsId, ct);
        }
    }

    private static string Describe(SyncAction action) => action switch
    {
        UploadAction u => $"upload   {u.Path}",
        DownloadAction d => $"download {d.Path}",
        MkDirCloudAction m => $"mkdir   {m.Path} (iCloud)",
        MkDirLocalAction m => $"mkdir   {m.Path} (local)",
        DeleteLocalAction d => $"delete  {d.Path} (local)",
        _ => "?",
    };

    private async Task ApplyOneAsync(
        SyncAction action,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        string rootFolderDriveWsId,
        CancellationToken ct)
    {
        switch (action)
        {
            case UploadAction upload:
                await ApplyUploadAsync(upload.Path, remote, rootFolderDriveWsId, ct);
                break;

            case DownloadAction download:
                await ApplyDownloadAsync(download.Path, remote, ct);
                break;

            case MkDirLocalAction mkdirLocal:
                Directory.CreateDirectory(FullPath(mkdirLocal.Path));
                break;

            case MkDirCloudAction mkdirCloud:
                await drive.CreateFolderAsync(ResolveParentId(mkdirCloud.Path, remote, rootFolderDriveWsId), NameOf(mkdirCloud.Path), ct);
                break;

            case DeleteLocalAction delete:
                DeleteLocal(FullPath(delete.Path));
                break;
        }
    }

    /// <summary>Remove arquivo OU pasta recursivamente (itens da lixeira podem ser pastas inteiras).</summary>
    private static void DeleteLocal(string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        else if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private async Task ApplyUploadAsync(
        string path,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        string rootFolderDriveWsId,
        CancellationToken ct)
    {
        var fullPath = FullPath(path);
        var info = new FileInfo(fullPath);
        var parentId = ResolveParentId(path, remote, rootFolderDriveWsId);

        await _gate.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(fullPath);
            await drive.UploadAsync(parentId, Path.GetFileName(fullPath), info.Length, stream, webauthToken, ct: ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyDownloadAsync(
        string path,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        CancellationToken ct)
    {
        if (!remote.TryGetValue(path, out var entry) || entry.Node.DocWsId is null)
        {
            return;
        }

        using var suppression = _coalescer?.Suppress();
        var fullPath = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var stream = await drive.DownloadAsync(entry.Node, ct))
        await using (var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.CopyToAsync(file, ct);
        }

        if (entry.Node.DateModified is { } modified)
        {
            File.SetLastWriteTimeUtc(fullPath, modified.UtcDateTime);
        }
    }

    private string FullPath(string path) => Path.Combine(localRoot, path.Replace('/', Path.DirectorySeparatorChar));

    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string ResolveParentId(
        string path,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        string rootFolderDriveWsId)
    {
        var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
        if (parent.Length == 0)
        {
            return rootFolderDriveWsId;
        }
        // Se a pasta pai ainda não existe na árvore, o MkDirCloudAction da leva cria primeiro
        // (a árvore atualizada só é visível no próximo refresh).
        return remote.TryGetValue(parent, out var entry) ? entry.Node.DriveWsId : rootFolderDriveWsId;
    }
}