namespace ICloudDriveSync.Sync;

/// <summary>Regras de path (normalização e ignores) — fonte única de verdade.</summary>
public static class PathRules
{
    /// <summary>Paths que nunca sincronizam (lixo do macOS/iOS e app_library do iCloud).</summary>
    public static bool ShouldIgnore(string path)
    {
        if (path.EndsWith(".DS_Store", StringComparison.Ordinal))
        {
            return true;
        }
        if (path.StartsWith(".com-apple-bird", StringComparison.Ordinal))
        {
            return true;
        }
        if (path.Equals("app_library", StringComparison.Ordinal) || path.StartsWith("app_library/", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    /// <summary>Normaliza separadores e remove "./" redundante (paths sempre em "/").</summary>
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
