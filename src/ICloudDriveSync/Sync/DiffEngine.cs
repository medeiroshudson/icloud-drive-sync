using System.Text.RegularExpressions;

namespace ICloudDriveSync.Sync;

/// <summary>
/// Função pura (sem I/O): compara as árvores local e remota e produz o plano de ações.
/// Espelha a lógica de decisão do icloudds (event_handler.py).
/// </summary>
public sealed class DiffEngine
{
    public IReadOnlyList<SyncAction> Diff(
        IReadOnlyDictionary<string, LocalEntry> local,
        IReadOnlyDictionary<string, RemoteEntry> remote,
        IReadOnlySet<string>? pathsInTrash = null,
        IReadOnlyList<string>? ignoreRegexes = null,
        IgnoreRules? ignoreRules = null)
    {
        var actions = new List<SyncAction>();
        var ignores = ignoreRegexes is { Count: > 0 }
            ? ignoreRegexes.Select(p => new Regex(p, RegexOptions.Compiled)).ToArray()
            : [];
        var rules = ignoreRules ?? IgnoreRules.Defaults;

        bool IsIgnored(string path) => rules.IsIgnored(path) || ignores.Any(r => r.IsMatch(path));

        // Só no local → sobe (arquivo) ou cria pasta no iCloud.
        // Exceto itens na trash do iCloud: não ressuscita o que foi deliberadamente deletado.
        foreach (var (path, l) in local)
        {
            if (IsIgnored(path) || pathsInTrash?.Contains(path) == true)
            {
                continue;
            }
            if (!remote.ContainsKey(path))
            {
                actions.Add(l.IsDirectory ? new MkDirCloudAction(path) : new UploadAction(path));
            }
        }

        // Só no remoto → desce (arquivo) ou cria pasta local.
        foreach (var (path, r) in remote)
        {
            if (IsIgnored(path))
            {
                continue;
            }
            if (!local.ContainsKey(path))
            {
                actions.Add(r.IsDirectory ? new MkDirLocalAction(path) : new DownloadAction(path));
            }
        }

        // Comuns (apenas arquivos): resolve por modified time.
        foreach (var (path, l) in local)
        {
            if (l.IsDirectory || IsIgnored(path))
            {
                continue;
            }
            if (!remote.TryGetValue(path, out var r) || r.IsDirectory)
            {
                continue;
            }

            var lmt = TimestampRules.RoundSeconds(l.ModifiedUtc);
            var rmt = TimestampRules.RoundSeconds(r.DateModifiedUtc);

            if (lmt == rmt)
            {
                // Mesmo timestamp com tamanho diferente: iCloud retorna inconsistência;
                // o icloudds apenas registra e não age (evita loop).
                continue;
            }

            // Arquivo vazio mais novo não sincroniza (regra do icloudds: evita sobe/desce de vazios).
            if (lmt > rmt && l.Size > 0)
            {
                actions.Add(new UploadAction(path));
            }
            else if (rmt > lmt && r.Size > 0)
            {
                actions.Add(new DownloadAction(path));
            }
        }

        // Itens na lixeira do iCloud → remove a cópia local (espelho da trash).
        if (pathsInTrash is not null)
        {
            foreach (var path in pathsInTrash)
            {
                if (local.ContainsKey(path) && !IsIgnored(path))
                {
                    actions.Add(new DeleteLocalAction(path));
                }
            }
        }

        return actions;
    }
}
