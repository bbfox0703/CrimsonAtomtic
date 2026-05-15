using CrimsonAtomtic.Core;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.Platform;

/// <summary>
/// Windows implementation of <see cref="IPlatformPaths"/>.
/// </summary>
/// <remarks>
/// <para>
/// Save discovery probes three known Crimson Desert save roots under
/// <c>%LOCALAPPDATA%\Pearl Abyss\</c>:
/// </para>
/// <list type="bullet">
///   <item><c>CD\save</c> — Steam install.</item>
///   <item><c>CD_Epic\save</c> — Epic Games Store install.</item>
///   <item><c>CD_GamePass\save</c> — Microsoft Store / Game Pass install (plain folder fallback; the real UWP <c>wgs</c> container at <c>%LOCALAPPDATA%\Packages\PearlAbyss.CrimsonDesert*\SystemAppData\wgs\</c> is out of scope for v1).</item>
/// </list>
/// <para>
/// Only roots that exist on disk are returned, ordered most-recently-
/// modified-save first so the UI can pick the user's active platform
/// without prompting.
/// </para>
/// <para>
/// On non-Windows hosts <c>LocalAppDataDirectory</c> still resolves
/// via <see cref="Environment.SpecialFolder.LocalApplicationData"/>,
/// but the save-root probes won't find anything — Linux/macOS need
/// Wine/Proton prefix detection in a future implementation
/// (Steam appid <c>3321460</c> per the roadmap).
/// </para>
/// </remarks>
public sealed class WindowsPlatformPaths : IPlatformPaths
{
    /// <summary>Parent directory under <c>%LOCALAPPDATA%</c> that Pearl Abyss writes to on Windows.</summary>
    private const string PearlAbyssDirName = "Pearl Abyss";

    /// <summary>
    /// Per-platform sub-directory names under <c>%LOCALAPPDATA%\Pearl Abyss\</c>.
    /// Order is informational only — discovery sorts by most-recent-save.
    /// </summary>
    private static readonly (SavePlatform Platform, string FolderName)[] PlatformDirs =
    [
        (SavePlatform.Steam,    "CD"),
        (SavePlatform.Epic,     "CD_Epic"),
        (SavePlatform.GamePass, "CD_GamePass"),
    ];

    public string LocalAppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrimsonAtomtic");

    public string LogDirectory => Path.Combine(LocalAppDataDirectory, "Logs");

    public IReadOnlyList<DiscoveredSaveRoot> DiscoverSaveRoots()
    {
        var pearlAbyss = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PearlAbyssDirName);
        var found = new List<DiscoveredSaveRoot>(PlatformDirs.Length);
        foreach (var (platform, folder) in PlatformDirs)
        {
            var root = Path.Combine(pearlAbyss, folder, "save");
            if (!Directory.Exists(root))
            {
                continue;
            }
            found.Add(new DiscoveredSaveRoot(platform, root, TryFindMostRecentSaveMtime(root)));
        }
        // Most-recently-modified first. Treat null mtimes as oldest so an
        // empty root sits behind any root with real saves.
        found.Sort((a, b) =>
        {
            var aT = a.MostRecentSaveMtime ?? DateTime.MinValue;
            var bT = b.MostRecentSaveMtime ?? DateTime.MinValue;
            return bT.CompareTo(aT);
        });
        return found;
    }

    public SavePlatform ClassifySavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return SavePlatform.Unknown;
        }
        string normalized;
        try
        {
            normalized = Path.GetFullPath(savePath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return SavePlatform.Unknown;
        }
        var pearlAbyss = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PearlAbyssDirName);
        var prefix = pearlAbyss + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return SavePlatform.Unknown;
        }
        // Read the immediate sub-folder name (CD / CD_Epic / CD_GamePass).
        var rest = normalized.AsSpan(prefix.Length);
        var sepIdx = rest.IndexOf(Path.DirectorySeparatorChar);
        var folder = sepIdx < 0 ? rest : rest[..sepIdx];
        foreach (var (platform, name) in PlatformDirs)
        {
            if (folder.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return platform;
            }
        }
        return SavePlatform.Unknown;
    }

    /// <summary>
    /// Resolve the Crimson Desert install directory by trying, in order:
    /// <list type="number">
    ///   <item>A user-stored override in <c>settings.json</c>
    ///         (<see cref="AppSettings.GameInstallRoot"/>) — set via
    ///         Tools → Set Game Install Folder… and validated against
    ///         the <c>0020\0.pamt</c> witness at pick time. Wins over
    ///         every auto-probe so the user can route around unusual
    ///         layouts (Game Pass WindowsApps, asset folders copied
    ///         outside the launcher install, etc.).</item>
    ///   <item>Steam: parse <c>libraryfolders.vdf</c> from the standard
    ///         Steam roots, walk every listed library, return the first
    ///         <c>steamapps\common\Crimson Desert</c> that contains the
    ///         witness.</item>
    ///   <item>Epic Games Store: enumerate
    ///         <c>%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item</c>,
    ///         find the manifest matching "Crimson Desert" by
    ///         <c>DisplayName</c> / <c>AppName</c>, validate its
    ///         <c>InstallLocation</c>.</item>
    /// </list>
    /// Returns <c>null</c> when every probe fails — consumers
    /// (<c>LocalizationProvider</c>, <c>IconExtractionService</c>)
    /// degrade gracefully into a no-localization state.
    /// </summary>
    /// <remarks>
    /// Game Pass install probing is intentionally absent: Pearl Abyss
    /// Game Pass writes its game files to <c>%PROGRAMFILES%\WindowsApps\</c>,
    /// which is ACL-locked even for administrators, so even on a hit
    /// we couldn't read the PAMT. Game Pass users should use the Tools
    /// menu override to point at an asset folder they manually copied
    /// out to a readable location.
    /// </remarks>
    public string? GameInstallRoot
    {
        get
        {
            // 1) User override from settings.json.
            var stored = AppSettingsStore.Load(LocalAppDataDirectory).GameInstallRoot;
            if (!string.IsNullOrWhiteSpace(stored)
                && SteamLibraryProbe.LooksLikeCrimsonDesertInstall(stored))
            {
                return stored;
            }
            // 2) Steam libraries.
            var steam = SteamLibraryProbe.FindCrimsonDesertInstall();
            if (steam is not null)
            {
                return steam;
            }
            // 3) Epic manifests.
            var epic = EpicManifestProbe.FindCrimsonDesertInstall();
            if (epic is not null)
            {
                return epic;
            }
            return null;
        }
    }

    /// <summary>
    /// Walk <paramref name="root"/> for <c>save.save</c> files and return
    /// the most recent <see cref="File.GetLastWriteTimeUtc(string)"/>.
    /// Bounded by the OS file-enumeration speed — sub-second even for
    /// the largest realistic save folders. Swallows IO/permission errors
    /// per-file so one unreadable slot doesn't take down the whole walk.
    /// </summary>
    private static DateTime? TryFindMostRecentSaveMtime(string root)
    {
        try
        {
            DateTime? max = null;
            foreach (var path in Directory.EnumerateFiles(root, "save.save", SearchOption.AllDirectories))
            {
                DateTime mtime;
                try
                {
                    mtime = File.GetLastWriteTimeUtc(path);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (max is null || mtime > max)
                {
                    max = mtime;
                }
            }
            return max;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
