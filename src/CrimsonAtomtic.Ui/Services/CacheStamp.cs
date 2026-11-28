namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Which game build an asset cache folder (icons, portraits) was filled
/// from. Its files are named by item or character key only, so after a game
/// update a cached image can be the old one; comparing the stamp with the
/// installed game tells the cache to refill.
/// </summary>
public static class CacheStamp
{
    /// <summary>The stamp file inside a cache folder.</summary>
    public const string FileName = "cache-stamp.txt";

    // Bump when the cached images change format or size, so every cache refills once.
    private const string Format = "webp-1";

    /// <summary>The stamp for <paramref name="gameVersion"/>, or null when it is unknown.</summary>
    public static string? For(string? gameVersion) =>
        string.IsNullOrEmpty(gameVersion) ? null : $"{Format} {gameVersion}";

    /// <summary>
    /// True when <paramref name="cacheDirectory"/> was filled from the game
    /// <paramref name="stamp"/> names. An unknown game version counts as
    /// current, since there is nothing to compare.
    /// </summary>
    public static bool IsCurrent(string cacheDirectory, string? stamp)
    {
        if (stamp is null)
        {
            return true;
        }
        try
        {
            var path = Path.Combine(cacheDirectory, FileName);
            return File.Exists(path) && File.ReadAllText(path).Trim() == stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Record that <paramref name="cacheDirectory"/> now holds <paramref name="stamp"/>'s images.</summary>
    public static void Write(string cacheDirectory, string? stamp)
    {
        if (stamp is null)
        {
            return;
        }
        try
        {
            File.WriteAllText(Path.Combine(cacheDirectory, FileName), stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the stamp the cache refills next time; nothing is lost.
        }
    }

    /// <summary>
    /// Empty <paramref name="cacheDirectory"/> of cached images when they are
    /// from another game build, then stamp it. For caches that refill on
    /// demand (portraits).
    /// </summary>
    public static void ClearIfStale(string cacheDirectory, string? stamp)
    {
        if (IsCurrent(cacheDirectory, stamp))
        {
            return;
        }
        try
        {
            foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*.webp"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        Write(cacheDirectory, stamp);
    }
}
