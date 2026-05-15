namespace CrimsonAtomtic.Core;

/// <summary>
/// Which storefront / launcher a save file belongs to. Pearl Abyss
/// ships the same save layout under different parent directories
/// depending on how the game was installed, so this enum just labels
/// the directory bucket — it doesn't affect file format.
/// </summary>
public enum SavePlatform
{
    /// <summary>Steam install — saves under <c>%LOCALAPPDATA%\Pearl Abyss\CD\save</c>.</summary>
    Steam,
    /// <summary>Epic Games Store install — saves under <c>%LOCALAPPDATA%\Pearl Abyss\CD_Epic\save</c>.</summary>
    Epic,
    /// <summary>Microsoft Store / Game Pass install — saves under <c>%LOCALAPPDATA%\Pearl Abyss\CD_GamePass\save</c> (plain fallback only; the wgs UWP container is out of scope for now).</summary>
    GamePass,
    /// <summary>A save we couldn't classify — typically a file the user Browse-opened from a non-default location.</summary>
    Unknown,
}

/// <summary>
/// One Crimson Desert save root that currently exists on disk.
/// </summary>
/// <param name="Platform">Which launcher this root belongs to.</param>
/// <param name="RootPath">Absolute path to the <c>...\save\</c> directory containing per-user subfolders.</param>
/// <param name="MostRecentSaveMtime">UTC mtime of the most-recently-written <c>save.save</c> below <paramref name="RootPath"/>, or <c>null</c> when the root exists but is empty / unreadable. Drives the "default platform" selection in the UI when multiple platforms are installed.</param>
public sealed record DiscoveredSaveRoot(
    SavePlatform Platform,
    string RootPath,
    DateTime? MostRecentSaveMtime);

/// <summary>
/// Filesystem locations the app reads from or writes to. Platform-specific
/// implementations live outside this project (e.g. a Windows-specific
/// resolver) — Core only declares the contract.
/// </summary>
public interface IPlatformPaths
{
    /// <summary>Per-user persistent state directory, e.g. <c>%LOCALAPPDATA%\CrimsonAtomtic</c> on Windows.</summary>
    string LocalAppDataDirectory { get; }

    /// <summary>Where rolling log files live. Always a subfolder of <see cref="LocalAppDataDirectory"/>.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// All Crimson Desert save roots that exist on disk right now, ordered
    /// most-recently-modified-save first. One entry per <see cref="SavePlatform"/>
    /// (except <see cref="SavePlatform.Unknown"/>, which never appears). Empty
    /// when no install can be detected — callers should fall back to the
    /// platform's user home as the Open dialog's starting point.
    /// </summary>
    /// <remarks>
    /// Does I/O (file stat per <c>save.save</c> below each candidate root)
    /// — call once at startup or per Open dialog, not in a hot path.
    /// </remarks>
    IReadOnlyList<DiscoveredSaveRoot> DiscoverSaveRoots();

    /// <summary>
    /// Classify <paramref name="savePath"/> by checking which of the known
    /// per-platform save trees it sits under. Returns
    /// <see cref="SavePlatform.Unknown"/> for paths that don't match any
    /// known root. Used by <c>SaveBackupService</c> to scope backups per
    /// platform so two users sharing a userId across launchers never collide.
    /// </summary>
    SavePlatform ClassifySavePath(string savePath);

    /// <summary>
    /// Best-effort path to a Crimson Desert game install (the directory
    /// containing the <c>0000/</c>, <c>0020/</c>, … pack groups). Used
    /// to bootstrap localization tables. Returns <c>null</c> when no
    /// install can be detected; consumers should degrade gracefully.
    /// </summary>
    string? GameInstallRoot { get; }
}
