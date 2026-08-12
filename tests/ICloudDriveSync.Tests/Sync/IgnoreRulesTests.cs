using ICloudDriveSync.Sync;

namespace ICloudDriveSync.Tests.Sync;

public class IgnoreRulesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("icloud-ignore-").FullName;

    [Fact]
    public void DefaultsIgnoreDotSegments()
    {
        Assert.True(IgnoreRules.Defaults.IsIgnored(".git/config"));
        Assert.True(IgnoreRules.Defaults.IsIgnored("Workspace/.metadata/.log"));
        Assert.True(IgnoreRules.Defaults.IsIgnored(".DS_Store"));
        Assert.True(IgnoreRules.Defaults.IsIgnored(".com-apple-bird-abc"));
    }

    [Fact]
    public void DefaultsIgnoreAppLibraryPath()
    {
        Assert.True(IgnoreRules.Defaults.IsIgnored("app_library/foo"));
        Assert.True(IgnoreRules.Defaults.IsIgnored("app_library"));
    }

    [Fact]
    public void DefaultsDoNotIgnoreRegularPaths()
    {
        Assert.False(IgnoreRules.Defaults.IsIgnored("notas.txt"));
        Assert.False(IgnoreRules.Defaults.IsIgnored("Workspace/arquivo.sql"));
        Assert.False(IgnoreRules.Defaults.IsIgnored("Documentos/cartao.pdf"));
    }

    [Fact]
    public void LoadParsesGitignoreStyleFile()
    {
        var file = Path.Combine(_root, ".icloud-ignore");
        File.WriteAllText(file, """
            # comentário
            *.tmp
            build/
            !keep.tmp
            pasta/segredo.txt
            """);
        var rules = IgnoreRules.Load(file);

        Assert.True(rules.IsIgnored("a.tmp"));
        Assert.True(rules.IsIgnored("x/build/y"));
        Assert.True(rules.IsIgnored("pasta/segredo.txt"));
        Assert.False(rules.IsIgnored("outra/pasta/segredo.txt"), "padrão com / deve ancorar na raiz");
        Assert.False(rules.IsIgnored("keep.tmp"), "negação deve vencer o padrão anterior");
        Assert.False(rules.IsIgnored("outra/keep.tmp"));
        Assert.False(rules.IsIgnored("notas.md"));
    }

    [Fact]
    public void NegationCanReincludeDotfile()
    {
        var file = Path.Combine(_root, ".icloud-ignore");
        File.WriteAllText(file, "!.env\n");
        var rules = IgnoreRules.Load(file);

        Assert.False(rules.IsIgnored(".env"), "negação re-inclui dotfile específico");
        Assert.True(rules.IsIgnored(".git/config"), "demais dotfiles continuam ignorados");
    }

    [Fact]
    public void MissingFileLoadsDefaultsOnly()
    {
        var rules = IgnoreRules.Load(Path.Combine(_root, "nao-existe"));

        Assert.True(rules.IsIgnored(".git/x"));
        Assert.False(rules.IsIgnored("notas.txt"));
    }

    [Fact]
    public void NormalizesBackslashesBeforeMatch()
    {
        var file = Path.Combine(_root, ".icloud-ignore");
        File.WriteAllText(file, "pasta/segredo.txt\n");
        var rules = IgnoreRules.Load(file);

        Assert.True(rules.IsIgnored("pasta\\segredo.txt"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}