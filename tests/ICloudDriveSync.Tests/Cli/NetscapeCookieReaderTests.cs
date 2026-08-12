using ICloudDriveSync.Cli;

namespace ICloudDriveSync.Tests.Cli;

public class NetscapeCookieReaderTests
{
    [Fact]
    public void ParsesLwpCookieJarFormat()
    {
        var lwp = """
        #LWP-Cookies-2.0
        Set-Cookie3: x-apple-group=false; path="/"; domain=.icloud.com; domain_dot; secure; discard; version=0
        Set-Cookie3: X-APPLE-WEBAUTH-USER="v=1:s=1:d=8401341634"; path="/"; domain=.icloud.com; domain_dot; secure; expires="2026-09-08 22:26:49Z"; version=0
        Set-Cookie3: X-APPLE-WEBAUTH-PCS-Photos="\"S2V5QXBwbDoBAAAAAwMAeXMFjXOjnphjZQbqPle+0rfVJseMfvk0t4zBCAPXtjqH5w==\""; path="/"; domain=.icloud.com; path_spec; domain_dot; secure; expires="2026-09-10 23:29:10Z"; HttpOnly=None; version=0
        """;

        var cookies = NetscapeCookieReader.Parse(lwp);

        Assert.Equal(3, cookies.Count);
        var group = cookies[0];
        Assert.Equal("x-apple-group", group.Name);
        Assert.Equal("false", group.Value);
        Assert.True(group.Secure);
        Assert.Equal(".icloud.com", group.Domain);
        var user = cookies[1];
        Assert.Equal("v=1:s=1:d=8401341634", user.Value);
        Assert.Equal(new DateTime(2026, 9, 8, 22, 26, 49, DateTimeKind.Utc), user.Expires);
        var photos = cookies[2];
        Assert.Equal("\"S2V5QXBwbDoBAAAAAwMAeXMFjXOjnphjZQbqPle+0rfVJseMfvk0t4zBCAPXtjqH5w==\"", photos.Value);
    }

    [Fact]
    public void ParsesNetscapeCookieJar()
    {
        var jar = """
        # Netscape HTTP Cookie File
        .icloud.com	TRUE	/	FALSE	1893456000	X-APPLE-WEBAUTH-VALIDATE	t%3Dtok123
        #HttpOnly_.icloud.com	TRUE	/	TRUE	1893456000	X-APPLE-DS-WEB-SESSION-TOKEN	sess-token
        """;

        var cookies = NetscapeCookieReader.Parse(jar);

        Assert.Equal(2, cookies.Count);
        Assert.Equal("X-APPLE-WEBAUTH-VALIDATE", cookies[0].Name);
        Assert.Equal("t%3Dtok123", cookies[0].Value);
        Assert.Equal(".icloud.com", cookies[0].Domain);
        Assert.Equal("sess-token", cookies[1].Value);
    }

    [Fact]
    public void SkipsCommentsAndMalformedLines()
    {
        var jar = """
        # comment
        #HttpOnly_.icloud.com	TRUE	/	TRUE	1893456000	X-APPLE-DS-WEB-SESSION-TOKEN	ok
        linha invalida sem tabs
        """;

        var cookies = NetscapeCookieReader.Parse(jar);

        Assert.Single(cookies);
    }

    [Fact]
    public void ReadsFromFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, ".icloud.com\tTRUE\t/\tFALSE\t1893456000\tA\tb");
            var cookies = NetscapeCookieReader.Read(path);
            Assert.Single(cookies);
        }
        finally
        {
            File.Delete(path);
        }
    }
}