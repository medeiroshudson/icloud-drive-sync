namespace ICloudDriveSync.Sync;

/// <summary>Varre o diretório local e produz a árvore com paths normalizados ("/").</summary>
public sealed class LocalScanner(string rootPath, IgnoreRules? ignoreRules = null)
{
    private readonly IgnoreRules _rules = ignoreRules ?? IgnoreRules.Defaults;

    public Dictionary<string, LocalEntry> Scan()
    {
        var result = new Dictionary<string, LocalEntry>();
        if (!Directory.Exists(rootPath))
        {
            return result;
        }

        Walk(string.Empty, result);
        return result;
    }

    private void Walk(string relative, Dictionary<string, LocalEntry> result)
    {
        var full = Path.Combine(rootPath, relative);
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var name = Path.GetFileName(entry);
            var rel = relative.Length == 0 ? name : $"{relative}/{name}";
            if (_rules.IsIgnored(rel))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                result[rel] = new LocalEntry(Round(File.GetLastWriteTimeUtc(entry)), 0, true);
                Walk(rel, result);
            }
            else
            {
                var info = new FileInfo(entry);
                result[rel] = new LocalEntry(Round(info.LastWriteTimeUtc), info.Length, false);
            }
        }
    }

    private static DateTimeOffset Round(DateTime utc) =>
        TimestampRules.RoundSeconds(new DateTimeOffset(utc, TimeSpan.Zero));
}