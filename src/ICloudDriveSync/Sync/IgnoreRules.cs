using System.Text;
using System.Text.RegularExpressions;

namespace ICloudDriveSync.Sync;

/// <summary>
/// Regras de ignore no estilo .gitignore, aplicadas em conjunto com os defaults
/// (dotfiles, app_library). Última regra que casa vence — permite re-incluir
/// com "!" (ex.: "!.env").
/// </summary>
public sealed class IgnoreRules
{
    private readonly List<Rule> _rules;

    private IgnoreRules(IEnumerable<Rule> rules) => _rules = rules.ToList();

    /// <summary>Defaults: segmentos que começam com "." (dotfiles) e app_library do iCloud.</summary>
    public static IgnoreRules Defaults { get; } = new([
        new Rule(new Regex("(^|/)\\.", RegexOptions.Compiled), Negated: false),
        new Rule(new Regex("(^|/)app_library($|/)", RegexOptions.Compiled), Negated: false),
    ]);

    /// <summary>Carrega os defaults mais as regras de um arquivo estilo .gitignore (se existir).</summary>
    public static IgnoreRules Load(string? ignoreFile) =>
        File.Exists(ignoreFile) ? FromLines(File.ReadAllLines(ignoreFile)) : Defaults;

    /// <summary>Defaults + linhas de um arquivo de ignore (útil em testes).</summary>
    public static IgnoreRules FromLines(IEnumerable<string> lines)
    {
        var rules = new List<Rule>(Defaults._rules);
        foreach (var line in lines)
        {
            var rule = ParseLine(line);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }
        return new IgnoreRules(rules);
    }

    public bool IsIgnored(string path)
    {
        var normalized = PathRules.Normalize(path);
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.Regex.IsMatch(normalized))
            {
                ignored = !rule.Negated;
            }
        }
        return ignored;
    }

    private static Rule? ParseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return null;
        }

        var negated = trimmed.StartsWith('!');
        var pattern = negated ? trimmed[1..] : trimmed;
        if (pattern.Length == 0)
        {
            return null;
        }

        pattern = pattern.TrimEnd('/');

        var regex = GlobToRegex(pattern);
        return new Rule(new Regex(regex, RegexOptions.Compiled), negated);
    }

    /// <summary>
    /// Converte um padrão estilo .gitignore em regex:
    /// "*" → qualquer coisa exceto "/", "**" → qualquer coisa, "?" → um char.
    /// Padrão com "/" ancora na raiz; sem "/" casa em qualquer nível.
    /// </summary>
    private static string GlobToRegex(string pattern)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        var anchored = pattern.Contains('/');
        return anchored
            ? $"^{sb}($|/)"
            : $"(^|.*/){sb}($|/)";
    }

    private sealed record Rule(Regex Regex, bool Negated);
}