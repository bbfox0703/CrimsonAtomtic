namespace CrimsonAtomtic.Core;

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

    /// <summary>Where the user's Crimson Desert save files live (game-specific, not app-specific).</summary>
    string GameSaveRoot { get; }

    /// <summary>
    /// Best-effort path to a Crimson Desert game install (the directory
    /// containing the <c>0000/</c>, <c>0020/</c>, … pack groups). Used
    /// to bootstrap localization tables. Returns <c>null</c> when no
    /// install can be detected; consumers should degrade gracefully.
    /// </summary>
    string? GameInstallRoot { get; }
}
