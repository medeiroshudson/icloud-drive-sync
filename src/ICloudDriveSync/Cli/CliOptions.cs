namespace ICloudDriveSync.Cli;

/// <summary>Opções de linha de comando (compatível com o icloudds).</summary>
public sealed record CliOptions(
    string Directory,
    string? CookieDirectory,
    string? Account,
    int RefreshPeriodSeconds,
    int CheckPeriodSeconds,
    IReadOnlyList<string> IgnoreRegexes,
    bool DryRun)
{
    public static CliOptions Parse(string[] args)
    {
        string? directory = null;
        string? cookieDirectory = null;
        string? account = null;
        var refreshPeriod = 600;
        var checkPeriod = 60;
        var ignoreRegexes = new List<string>();
        var dryRun = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d":
                case "--directory":
                    directory = RequireValue(args, ref i, args[i]);
                    break;
                case "--cookie-directory":
                    cookieDirectory = RequireValue(args, ref i, args[i]);
                    break;
                case "--account":
                    account = RequireValue(args, ref i, args[i]);
                    break;
                case "--icloud-refresh-period":
                    refreshPeriod = int.Parse(RequireValue(args, ref i, args[i]));
                    break;
                case "--icloud-check-period":
                    checkPeriod = int.Parse(RequireValue(args, ref i, args[i]));
                    break;
                case "--ignore-regexes":
                    ignoreRegexes = RequireValue(args, ref i, args[i])
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Argumento desconhecido: {args[i]}");
            }
        }

        if (directory is null)
        {
            throw new ArgumentException("O diretório a sincronizar é obrigatório (-d/--directory).");
        }

        return new CliOptions(directory, cookieDirectory, account, refreshPeriod, checkPeriod, ignoreRegexes, dryRun);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Falta o valor para {flag}.");
        }
        i++;
        return args[i];
    }
}