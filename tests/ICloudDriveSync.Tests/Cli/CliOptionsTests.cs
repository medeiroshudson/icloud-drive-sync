using ICloudDriveSync.Cli;

namespace ICloudDriveSync.Tests.Cli;

public class CliOptionsTests
{
    [Fact]
    public void ParsesFullArgumentSet()
    {
        var options = CliOptions.Parse([
            "-d", "/drive",
            "--cookie-directory", "/cookies",
            "--account", "medeiroshudson@outlook.com",
            "--icloud-refresh-period", "600",
            "--icloud-check-period", "60",
            "--ignore-regexes", "\\.tmp$|\\.DS_Store",
        ]);

        Assert.Equal("/drive", options.Directory);
        Assert.Equal("/cookies", options.CookieDirectory);
        Assert.Equal("medeiroshudson@outlook.com", options.Account);
        Assert.Equal(600, options.RefreshPeriodSeconds);
        Assert.Equal(60, options.CheckPeriodSeconds);
        Assert.Equal(["\\.tmp$", "\\.DS_Store"], options.IgnoreRegexes);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void AppliesDefaults()
    {
        var options = CliOptions.Parse(["-d", "/drive"]);

        Assert.Equal(600, options.RefreshPeriodSeconds);
        Assert.Equal(60, options.CheckPeriodSeconds);
        Assert.Empty(options.IgnoreRegexes);
    }

    [Fact]
    public void ParsesDryRunFlag()
    {
        var options = CliOptions.Parse(["-d", "/drive", "--dry-run"]);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void ThrowsWhenDirectoryMissing()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse([]));
    }

    [Fact]
    public void LongDirectoryFlagIsAccepted()
    {
        var options = CliOptions.Parse(["--directory", "/x", "--dry-run"]);
        Assert.Equal("/x", options.Directory);
    }
}