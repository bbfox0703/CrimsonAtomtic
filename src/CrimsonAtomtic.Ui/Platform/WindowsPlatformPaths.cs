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
}
