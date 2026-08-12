namespace ICloudDriveSync.Sync;

/// <summary>
/// Ciclo de sincronização: sanity check rápido (fileCount) + varredura da lixeira →
/// refresh completo quando necessário → diff → aplicação. Espelha o refresh periódico
/// do icloudds (check 60s / refresh 600s) e a deleção local de itens na lixeira.
/// </summary>
public sealed class SyncLoop(
    CloudScanner cloud,
    LocalScanner local,
    ActionApplier applier,
    TimeSpan refreshPeriod,
    IgnoreRules? ignoreRules = null,
    Func<DateTimeOffset>? now = null)
{
    private long? _lastFileCount;
    private DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;
    private HashSet<string> _lastTrash = new(StringComparer.Ordinal);

    public async Task RunOnceAsync(string rootFolderDriveWsId, CancellationToken ct = default)
    {
        var currentNow = (now ?? (() => DateTimeOffset.UtcNow))();
        var fileCount = await cloud.GetRootFileCountAsync(rootFolderDriveWsId, ct) ?? -1;

        // Lixeira é lida em todo ciclo (1 request): item deletado no iCloud
        // (via outro dispositivo) reflete no local sem esperar o refresh completo.
        var trash = await cloud.ScanTrashAsync(ct);
        var trashChanged = !_lastTrash.SetEquals(trash);
        _lastTrash = trash;

        var needsRefresh = !_lastFileCount.HasValue
            || fileCount != _lastFileCount.Value
            || trashChanged
            || currentNow - _lastRefreshAt >= refreshPeriod;
        _lastFileCount = fileCount;

        if (!needsRefresh)
        {
            return;
        }

        var remote = await cloud.ScanRootAsync(rootFolderDriveWsId, ct);
        var tree = local.Scan();
        Console.WriteLine(
            $"[{currentNow:HH:mm:ss}] ciclo: remoto={remote.Count} itens, local={tree.Count}, trash={trash.Count} — diff em andamento");
        var actions = new DiffEngine().Diff(tree, remote, pathsInTrash: trash, ignoreRules: ignoreRules);
        Console.WriteLine($"[{currentNow:HH:mm:ss}] plano: {actions.Count} ações");
        await applier.ApplyAsync(actions, remote, rootFolderDriveWsId, ct);

        _lastRefreshAt = currentNow;
    }
}