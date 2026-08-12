namespace ICloudDriveSync.Sync;

/// <summary>
/// Ciclo de sincronização: sanity check rápido (fileCount) → refresh completo →
/// diff → aplicação. Espelha o refresh periódico do icloudds (check 60s / refresh 600s).
/// </summary>
public sealed class SyncLoop(
    CloudScanner cloud,
    LocalScanner local,
    ActionApplier applier,
    TimeSpan refreshPeriod,
    Func<DateTimeOffset>? now = null)
{
    private long? _lastFileCount;
    private DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;

    public async Task RunOnceAsync(string rootFolderDriveWsId, CancellationToken ct = default)
    {
        var currentNow = (now ?? (() => DateTimeOffset.UtcNow))();
        var fileCount = await cloud.GetRootFileCountAsync(rootFolderDriveWsId, ct) ?? -1;

        var needsRefresh = !_lastFileCount.HasValue
            || fileCount != _lastFileCount.Value
            || currentNow - _lastRefreshAt >= refreshPeriod;
        _lastFileCount = fileCount;

        if (!needsRefresh)
        {
            return;
        }

        var remote = await cloud.ScanRootAsync(rootFolderDriveWsId, ct);
        var tree = local.Scan();
        var actions = new DiffEngine().Diff(tree, remote);
        await applier.ApplyAsync(actions, remote, rootFolderDriveWsId, ct);

        _lastRefreshAt = currentNow;
    }
}