namespace ICloudDriveSync.Sync;

public enum WatchChange { Created, Deleted, Changed, Renamed }

public readonly record struct WatchEvent(string Path, WatchChange Change, bool IsDirectory = false);

/// <summary>
/// Coalesce de mudanças do filesystem: bursts de eventos (ex.: salvar um arquivo gera
/// Created+Changed+Changed) viram um único path sujo. Suporta supressão durante writes
/// feitos pelo próprio sync (evita loop local→iCloud→local).
/// </summary>
public sealed class LocalChangeCoalescer
{
    private readonly Dictionary<string, WatchChange> _pending = [];
    private readonly object _lock = new();
    private int _suppressionDepth;

    /// <summary>Registra uma mudança local; descartada se houver supressão ativa.</summary>
    public void Add(string path, WatchChange change, bool isDirectory = false)
    {
        lock (_lock)
        {
            if (_suppressionDepth > 0)
            {
                return;
            }
            if (!_pending.TryGetValue(path, out var existing) || change == WatchChange.Deleted)
            {
                // Delete sempre vence ("sumiu"); caso contrário o primeiro evento representa o path.
                _pending[path] = change;
            }
        }
    }

    /// <summary>Suprime eventos enquanto o sync escreve no disco (token descartável, suporta aninhamento).</summary>
    public IDisposable Suppress()
    {
        lock (_lock)
        {
            _suppressionDepth++;
        }
        return new SuppressionHandle(this);
    }

    /// <summary>Retorna e limpa os paths alterados desde o último drain.</summary>
    public IReadOnlyList<WatchEvent> Drain()
    {
        lock (_lock)
        {
            var events = _pending.Select(kv => new WatchEvent(kv.Key, kv.Value)).ToList();
            _pending.Clear();
            return events;
        }
    }

    private sealed class SuppressionHandle(LocalChangeCoalescer owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (owner._lock)
                {
                    owner._suppressionDepth--;
                }
            }
        }
    }
}