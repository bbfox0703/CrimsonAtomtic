namespace CrimsonAtomtic.Tests;

/// <summary>
/// Shared helper for the live-save tests: every <c>save.save</c> under
/// <c>%LOCALAPPDATA%\Pearl Abyss\CD\save\&lt;user&gt;\&lt;slot&gt;\</c>,
/// <b>most recently written first</b>.
///
/// <para>
/// Newest-first is the point: a fixed slot number can hold a save several
/// patches old, and then a save-body drift in a new patch reaches the editor
/// before any C# test sees it. Ordering by last-write time makes the tests
/// follow whatever the installed game wrote most recently.
/// </para>
///
/// <para>
/// The list is taken once per test run, so every test in a run sees the same
/// saves even if the game writes a new one mid-run. Tests only ever read
/// these files; writes go to temp paths or scratch copies.
/// </para>
/// </summary>
internal static class LiveSaves
{
    private static readonly Lazy<string[]> Saves = new(Discover);

    /// <summary>
    /// Every live <c>save.save</c>, newest first. Empty when the machine has
    /// no save root (CI, fresh clone) — callers skip.
    /// </summary>
    public static IReadOnlyList<string> All() => Saves.Value;

    /// <summary>
    /// The most recently written save — the format the installed game writes
    /// today — or <c>null</c> when there is none.
    /// </summary>
    public static string? Newest() => Saves.Value.Length > 0 ? Saves.Value[0] : null;

    /// <summary>
    /// Label for failure messages: the slot folder and the day the save was
    /// last written, e.g. <c>slot102 (2026-09-18)</c> — the date is what
    /// tells a save from an old patch apart from a current one.
    /// </summary>
    public static string Describe(string path) =>
        $"{Path.GetFileName(Path.GetDirectoryName(path))} ({File.GetLastWriteTime(path):yyyy-MM-dd})";

    private static string[] Discover()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local))
        {
            return [];
        }
        var root = Path.Combine(local, "Pearl Abyss", "CD", "save");
        if (!Directory.Exists(root))
        {
            return [];
        }
        return Directory.EnumerateDirectories(root)
            .SelectMany(user => Directory.EnumerateDirectories(user))
            .Select(slot => Path.Combine(slot, "save.save"))
            .Where(path => File.Exists(path))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
