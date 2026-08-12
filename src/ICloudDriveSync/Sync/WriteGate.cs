namespace ICloudDriveSync.Sync;

/// <summary>
/// Serializa operações de escrita no iCloud (1 por vez).
/// O CloudDocs responde ZONE_BUSY em writes concorrentes.
/// </summary>
public sealed class WriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();
}