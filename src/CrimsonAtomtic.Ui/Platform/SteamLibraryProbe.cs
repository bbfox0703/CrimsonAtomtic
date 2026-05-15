using System.Text.RegularExpressions;

namespace CrimsonAtomtic.Ui.Platform;

/// <summary>
/// Locate a Crimson Desert install across all Steam libraries on this
/// machine by parsing <c>libraryfolders.vdf</c>. Steam tracks every
/// library folder the user has added there (the default Steam install
/// dir plus any extras the user mounted on other drives), so a VDF
/// walk finds installs we'd otherwise miss with hardcoded path guesses.
/// </summary>
/// <remarks>
/// <para>
/// VDF format is Valve's hand-rolled tagged-tree text format:
/// <code>
/// "libraryfolders"
/// {
///     "0"
///     {
///         "path"      "C:\\Program Files (x86)\\Steam"
///         "label"     ""
///         ...
///     }
///     "1"
///     {
///         "path"      "D:\\SteamLibrary"
///         ...
///     }
/// }
/// </code>
/// We only need the <c>path</c> values — a single regex over the file
/// covers it, since VDF doesn't nest a second <c>path</c> key inside the
/// per-library subblocks.
/// </para>
/// <para>
/// AOT-safe: no reflection, no source generators required. Regex is
/// constructed once at first use.
/// </para>
/// </remarks>
public static partial class SteamLibraryProbe
{
    /// <summary>
    /// Well-known locations of the Steam client root on Windows. The
    /// real registry value <c>HKLM\SOFTWARE\WOW6432Node\Valve\Steam!InstallPath</c>
    /// is more authoritative but adds a Microsoft.Win32.Registry
    /// dependency just for one lookup — the user can route around an
    /// unusual Steam install via the Tools menu manual override.
    /// </summary>
    private static readonly string[] SteamRootCandidates =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
    ];

    /// <summary>
    /// Subdirectory under each Steam library that holds installed games.
    /// </summary>
    private const string SteamAppsCommon = @"steamapps\common";

    /// <summary>
    /// Game folder name as Steam writes it under <c>steamapps\common</c>.
    /// </summary>
    private const string CrimsonDesertFolderName = "Crimson Desert";

    /// <summary>
    /// Witness file used to validate that a directory really IS a
    /// Crimson Desert install — same probe signal the legacy hardcoded
    /// candidate list used (the PAMT we extract English PALOC from).
    /// </summary>
    private const string InstallWitnessRelative = @"0020\0.pamt";

    /// <summary>
    /// Walk every detected Steam library and return the path to a
    /// Crimson Desert install, or <c>null</c> when none match. Steam
    /// installs land at <c>&lt;library&gt;\steamapps\common\Crimson Desert\</c>;
    /// the install is considered valid when it contains
    /// <c>0020\0.pamt</c>.
    /// </summary>
    public static string? FindCrimsonDesertInstall()
    {
        foreach (var libraryRoot in EnumerateLibraryRoots())
        {
            var candidate = Path.Combine(libraryRoot, SteamAppsCommon, CrimsonDesertFolderName);
            if (LooksLikeCrimsonDesertInstall(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Yield every Steam library root the local <c>libraryfolders.vdf</c>
    /// knows about (default Steam folder + any user-added libraries).
    /// Empty when no Steam install is detected in either standard
    /// location. Returned paths are NOT validated to exist — callers
    /// drill further into <c>steamapps\common\&lt;game&gt;</c> and
    /// check the game's witness file themselves.
    /// </summary>
    public static IEnumerable<string> EnumerateLibraryRoots()
    {
        foreach (var steamRoot in SteamRootCandidates)
        {
            var vdf = Path.Combine(steamRoot, "config", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }
            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var path in ExtractLibraryPaths(text))
            {
                yield return path;
            }
            // Found a VDF — stop probing further Steam roots.
            yield break;
        }
    }

    /// <summary>
    /// Extract every <c>"path" "..."</c> value from a
    /// <c>libraryfolders.vdf</c> text body. VDF escapes are minimal —
    /// <c>\\</c> for backslash and <c>\"</c> for quote — and the latter
    /// never appears in a real library path, so a single unescape pass
    /// of <c>\\</c> is sufficient.
    /// </summary>
    public static IEnumerable<string> ExtractLibraryPaths(string vdfText)
    {
        foreach (Match m in PathValueRegex().Matches(vdfText))
        {
            var raw = m.Groups[1].Value;
            yield return raw.Replace(@"\\", @"\");
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is a valid Crimson Desert
    /// install (contains the witness file we use to extract English
    /// PALOC). Defends against partial / cancelled installs that left
    /// a <c>Crimson Desert</c> folder behind but no game data.
    /// </summary>
    public static bool LooksLikeCrimsonDesertInstall(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }
        try
        {
            return File.Exists(Path.Combine(candidate, InstallWitnessRelative));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PathValueRegex();
}
