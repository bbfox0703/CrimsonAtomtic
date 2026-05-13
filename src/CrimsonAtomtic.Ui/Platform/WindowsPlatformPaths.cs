using CrimsonAtomtic.Core;

namespace CrimsonAtomtic.Ui.Platform;

/// <summary>
/// Windows implementation of <see cref="IPlatformPaths"/>.
/// </summary>
/// <remarks>
/// Resolved paths:
/// <list type="bullet">
///   <item><c>LocalAppDataDirectory</c> → <c>%LOCALAPPDATA%\CrimsonAtomtic</c>.</item>
///   <item><c>LogDirectory</c> → <c>%LOCALAPPDATA%\CrimsonAtomtic\Logs</c>.</item>
///   <item><c>GameSaveRoot</c> → <c>%LOCALAPPDATA%\Pearl Abyss\CD\save</c>.</item>
/// </list>
/// On non-Windows hosts the same call returns the platform's equivalent
/// of <c>SpecialFolder.LocalApplicationData</c> (e.g. <c>~/.local/share</c>
/// on Linux), which is correct for the app's own data but won't point
/// at a real Crimson Desert install — Linux/macOS need Wine/Proton
/// prefix detection in a future implementation. See the roadmap in
/// <c>docs/status.md</c>.
/// </remarks>
public sealed class WindowsPlatformPaths : IPlatformPaths
{
    public string LocalAppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrimsonAtomtic");

    public string LogDirectory => Path.Combine(LocalAppDataDirectory, "Logs");

    public string GameSaveRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pearl Abyss",
            "CD",
            "save");

    /// <summary>
    /// Probe a handful of well-known Steam library roots for a Crimson
    /// Desert install. Not exhaustive — a future iteration should parse
    /// <c>libraryfolders.vdf</c> for the user's actual library list.
    /// Returns the first directory that contains a <c>0020/0.pamt</c>
    /// (the PAMT we extract the English PALOC from).
    /// </summary>
    public string? GameInstallRoot
    {
        get
        {
            string[] candidates =
            [
                @"D:\SteamLibrary\steamapps\common\Crimson Desert",
                @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
                @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
                @"E:\SteamLibrary\steamapps\common\Crimson Desert",
                @"F:\SteamLibrary\steamapps\common\Crimson Desert",
            ];
            foreach (var candidate in candidates)
            {
                var probe = Path.Combine(candidate, "0020", "0.pamt");
                if (File.Exists(probe))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
