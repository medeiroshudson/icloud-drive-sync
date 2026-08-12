using System.Net;
using System.Text.RegularExpressions;

namespace ICloudDriveSync.Cli;

/// <summary>
/// Lê cookies nos dois formatos suportados pelo LWPCookieJar do pyicloud:
/// 1. Netscape clássico (7 colunas tab-separated) — formato do "Get cookies.txt LOCALLY"
/// 2. LWP (#LWP-Cookies-2.0 + Set-Cookie3: name=value; attr; attr="value") — como o
///    pyicloud salva o cookiejar em disco (formato usado pelo seed APPLE_ICLOUD_COOKIEJAR).
/// </summary>
public static class NetscapeCookieReader
{
    public static List<Cookie> Read(string filePath)
    {
        return Parse(File.ReadAllText(filePath));
    }

    public static List<Cookie> Parse(string content)
    {
        var cookies = new List<Cookie>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || (line.StartsWith('#') && !line.StartsWith("#HttpOnly_")))
            {
                continue;
            }

            if (line.StartsWith("Set-Cookie3:", StringComparison.Ordinal))
            {
                var cookie = ParseLwp(line);
                if (cookie is not null)
                {
                    cookies.Add(cookie);
                }

                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 7)
            {
                continue;
            }

            var domain = fields[0].StartsWith("#HttpOnly_", StringComparison.Ordinal)
                ? fields[0]["#HttpOnly_".Length..]
                : fields[0];

            cookies.Add(new Cookie(fields[5], fields[6], fields[2], domain)
            {
                Secure = fields[3] == "TRUE",
                Expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[4])).UtcDateTime,
            });
        }

        return cookies;
    }

    /// <summary>
    /// Formato LWP: Set-Cookie3: name=value; path="/"; domain=...; domain_dot; secure;
    /// expires="2026-09-08 22:26:49Z"; HttpOnly=None; version=0
    /// Valores escapados: \" vira ", \\ vira \ (espelha o unescape do LWPCookieJar.load).
    /// </summary>
    private static Cookie? ParseLwp(string line)
    {
        var body = line["Set-Cookie3:".Length..].Trim();
        var parts = body.Split(';');

        var eq = parts[0].IndexOf('=');
        if (eq <= 0)
        {
            return null;
        }

        var name = parts[0][..eq].Trim();
        var value = Unescape(StripOuterQuotes(parts[0][(eq + 1)..].Trim()));

        string path = "/";
        string? domain = null;
        var secure = false;
        DateTime? expires = null;

        foreach (var part in parts.Skip(1))
        {
            var attr = part.Trim();
            if (attr.Length == 0)
            {
                continue;
            }

            var attrEq = attr.IndexOf('=');
            var key = attrEq >= 0 ? attr[..attrEq].Trim() : attr;
            var val = attrEq >= 0 ? Unescape(attr[(attrEq + 1)..].Trim().Trim('"')) : null;

            switch (key)
            {
                case "path":
                    path = val ?? path;
                    break;
                case "domain":
                    domain = val;
                    break;
                case "secure":
                    secure = true;
                    break;
                case "expires" when val is not null:
                    if (DateTime.TryParseExact(val, "yyyy-MM-dd HH:mm:ss'Z'",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var dt))
                    {
                        expires = dt;
                    }

                    break;
            }
        }

        if (domain is null)
        {
            return null;
        }

        var cookie = new Cookie(name, value, path, domain)
        {
            Secure = secure,
        };
        if (expires.HasValue)
        {
            cookie.Expires = expires.Value;
        }

        return cookie;
    }

    private static string Unescape(string value) =>
        Regex.Replace(value, @"\\(.)", "$1");

    /// <summary>Remove aspas externas do valor LWP (espelha o LWPCookieJar.load).</summary>
    private static string StripOuterQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}
