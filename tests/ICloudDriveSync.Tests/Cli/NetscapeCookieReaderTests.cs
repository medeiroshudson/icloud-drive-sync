using ICloudDriveSync.Cli;

namespace ICloudDriveSync.Tests.Cli;

public class NetscapeCookieReaderTests
{
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