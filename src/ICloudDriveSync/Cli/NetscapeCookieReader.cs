using System.Net;

namespace ICloudDriveSync.Cli;

/// <summary>
/// Lê cookies no formato Netscape (LWPCookieJar, usado pelo pyicloud).
/// Linhas: domain, includeSubdomains, path, secure, expires, name, value.
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

            var fields = line.Split('\t');
            if (fields.Length < 7)
            {
                continue;
            }

            var domain = fields[0].StartsWith("#HttpOnly_", StringComparison.Ordinal)
                ? fields[0]["#HttpOnly_".Length..]
                : fields[0];

            var cookie = new Cookie(fields[5], fields[6], fields[2], domain)
            {
                Secure = fields[3] == "TRUE",
                Expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[4])).UtcDateTime,
            };
            cookies.Add(cookie);
        }

        return cookies;
    }
}