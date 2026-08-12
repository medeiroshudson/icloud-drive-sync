using System.Net;
using ICloudDriveSync.Auth;
using ICloudDriveSync.Cli;
using ICloudDriveSync.Drive;
using ICloudDriveSync.Sync;

namespace ICloudDriveSync;

public static class Program
{
    private const string RootFolderId = "FOLDER::com.apple.CloudDocs::root";

    public static async Task<int> Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            Console.Error.WriteLine(
                "Uso: icloud-drive-sync -d <diretório> [--cookie-directory <dir>] [--account <email>] " +
                "[--icloud-refresh-period 600] [--icloud-check-period 60] [--ignore-regexes 'a|b'] " +
                "[--ignore-file <path>] [--include-app-library] [--dry-run]");
            return 1;
        }

        if (!Directory.Exists(options.Directory))
        {
            Console.Error.WriteLine($"Erro: diretório não existe: {options.Directory}");
            return 1;
        }

        var session = LoadSession(options);
        if (session is null)
        {
            return 1;
        }

        using var http = BuildHttpClient(options);
        var auth = new ICloudAuthClient(http);
        var result = await auth.AuthenticateAsync(session);
        if (result is not AuthSuccess success)
        {
            var reason = ((AuthRequired)result).Reason;
            Console.Error.WriteLine($"[ALERTA] Sessão do iCloud expirada: {reason}");
            Console.Error.WriteLine(
                "Atualize a sessão (cookies/session_token do browser) e reinicie. " +
                "Nenhuma autenticação automática será tentada (evita bloqueio da conta).");
            return 2;
        }

        var drive = new ICloudDriveClient(http, success.Services, session.ClientId);
        var coalescer = new LocalChangeCoalescer();
        var rules = IgnoreRules.Load(options.IgnoreFile);
        var applier = new ActionApplier(
            drive,
            options.Directory,
            coalescer: coalescer,
            webauthToken: ExtractWebauthToken(options),
            dryRun: options.DryRun);
        var loop = new SyncLoop(
            new CloudScanner(drive, includeAppLibrary: options.IncludeAppLibrary, ignoreRules: rules),
            new LocalScanner(options.Directory, rules),
            applier,
            TimeSpan.FromSeconds(options.RefreshPeriodSeconds),
            ignoreRules: rules);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine(
            $"Sincronizando {options.Directory} ↔ iCloud Drive " +
            $"(check {options.CheckPeriodSeconds}s, refresh {options.RefreshPeriodSeconds}s, dry-run={options.DryRun})");

        while (!cts.IsCancellationRequested)
        {
            try
            {
                await loop.RunOnceAsync(RootFolderId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Erro no ciclo: {ex}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.CheckPeriodSeconds), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("Encerrado.");
        return 0;
    }

    private static ICloudSession? LoadSession(CliOptions options)
    {
        if (options.CookieDirectory is null || options.Account is null)
        {
            Console.Error.WriteLine("Erro: --cookie-directory e --account são obrigatórios (sessão injetada, sem senha).");
            return null;
        }

        var sessionFile = Path.Combine(options.CookieDirectory, $"{CliOptions.SessionBaseName(options.Account!)}.session");
        if (!File.Exists(sessionFile))
        {
            Console.Error.WriteLine($"Erro: arquivo de sessão não encontrado: {sessionFile}");
            return null;
        }

        try
        {
            return ICloudSession.Parse(File.ReadAllText(sessionFile));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: sessão inválida: {ex.Message}");
            return null;
        }
    }

    private static HttpClient BuildHttpClient(CliOptions options)
    {
        var handler = new HttpClientHandler();
        if (options.CookieDirectory is not null && options.Account is not null)
        {
            var cookieJar = Path.Combine(options.CookieDirectory, $"{CliOptions.SessionBaseName(options.Account!)}.cookiejar");
            if (File.Exists(cookieJar))
            {
                try
                {
                    foreach (var cookie in NetscapeCookieReader.Read(cookieJar))
                    {
                        handler.CookieContainer.Add(cookie);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Aviso: não foi possível carregar cookies ({ex.Message}); continuando com a sessão.");
                }
            }
        }

        return new HttpClient(handler);
    }

    private static string? ExtractWebauthToken(CliOptions options)
    {
        if (options.CookieDirectory is null || options.Account is null)
        {
            return null;
        }

        var cookieJar = Path.Combine(options.CookieDirectory, $"{CliOptions.SessionBaseName(options.Account!)}.cookiejar");
        if (!File.Exists(cookieJar))
        {
            return null;
        }

        try
        {
            var token = NetscapeCookieReader.Read(cookieJar)
                .FirstOrDefault(c => c.Name == "X-APPLE-WEBAUTH-VALIDATE")
                ?.Value;
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }
}