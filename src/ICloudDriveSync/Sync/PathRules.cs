namespace ICloudDriveSync.Sync;

/// <summary>Regras de path (normalização e ignores) — fonte única de verdade.</summary>
public static class PathRules
{
    /// <summary>
    /// Paths que nunca sincronizam (defaults do IgnoreRules: dotfiles, .DS_Store,
    /// .com-apple-bird e app_library do iCloud).
    /// </summary>
    public static bool ShouldIgnore(string path) => IgnoreRules.Defaults.IsIgnored(path);

    /// <summary>Normaliza separadores e remove "./" redundante (paths sempre em "/").</summary>
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}