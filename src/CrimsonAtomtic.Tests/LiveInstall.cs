using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Shared helpers for the live-install tests: find the game install, and
/// read gamedata tables / localization through whichever archive layout it
/// ships.
///
/// <para>
/// Crimson Desert 2.01 renamed the gamedata directory and every file
/// extension, and split each language's single PALOC blob into one file per
/// namespace, without changing any file's contents. Tests therefore ask for
/// a table by its <i>stem</i> ("skill") rather than spelling out
/// <c>skill.pabgb</c>, and take a language as its parts.
/// </para>
/// </summary>
internal static class LiveInstall
{
    private static readonly string[] RootCandidates =
    [
        @"D:\SteamLibrary\steamapps\common\Crimson Desert",
        @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
        @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
        @"E:\SteamLibrary\steamapps\common\Crimson Desert",
        @"F:\SteamLibrary\steamapps\common\Crimson Desert",
    ];

    private static readonly Dictionary<string, GameDataLayout> LayoutCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Absolute path to <c>&lt;group&gt;/0.pamt</c> in the first install
    /// found, or <c>null</c> when no install is present (CI, fresh clone).
    /// </summary>
    public static string? FindGroupPamt(string group)
    {
        foreach (var root in RootCandidates)
        {
            var p = Path.Combine(root, group, "0.pamt");
            if (File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>
    /// Archive layout the install at <paramref name="gameDataPamt"/> ships.
    /// Resolved once per manifest — a PAMT walk costs a few ms and the
    /// live-install suite asks dozens of times.
    /// </summary>
    public static GameDataLayout Layout(IPazExtractor paz, string gameDataPamt)
    {
        lock (LayoutCache)
        {
            if (!LayoutCache.TryGetValue(gameDataPamt, out var layout))
            {
                layout = GameDataLayout.Resolve(paz, gameDataPamt);
                LayoutCache[gameDataPamt] = layout;
            }
            return layout;
        }
    }

    /// <summary>Extract one table's body — <c>"skill"</c> → <c>skill.pabgb</c>
    /// or <c>skill.staticinfobody</c>.</summary>
    public static byte[] Body(IPazExtractor paz, string gameDataPamt, string tableStem)
    {
        var layout = Layout(paz, gameDataPamt);
        return paz.ExtractFile(gameDataPamt, layout.BinDirectory, layout.Body(tableStem));
    }

    /// <summary>Extract one table's index — <c>"skill"</c> → <c>skill.pabgh</c>
    /// or <c>skill.staticinfoheader</c>.</summary>
    public static byte[] Header(IPazExtractor paz, string gameDataPamt, string tableStem)
    {
        var layout = Layout(paz, gameDataPamt);
        return paz.ExtractFile(gameDataPamt, layout.BinDirectory, layout.Header(tableStem));
    }

    /// <summary>
    /// Load one language from <paramref name="palocPamt"/>, whichever layout
    /// holds it. Returns <c>null</c> when that language isn't in the group.
    /// </summary>
    public static PalocLanguage? LoadPaloc(IPazExtractor paz, string palocPamt, string languageCode)
    {
        if (GameDataLayout.ResolvePalocFiles(paz, palocPamt, languageCode) is not { } found)
        {
            return null;
        }
        var parts = new NativePalocCatalog[found.Files.Count];
        var built = 0;
        try
        {
            for (; built < parts.Length; built++)
            {
                parts[built] = NativePalocCatalog.LoadFromBytes(
                    paz.ExtractFile(palocPamt, found.Directory, found.Files[built]));
            }
        }
        catch
        {
            for (var i = 0; i < built; i++)
            {
                parts[i].Dispose();
            }
            throw;
        }
        return new PalocLanguage(parts);
    }
}

/// <summary>
/// One language's PALOC, as the one-or-many files the install ships.
///
/// <para>
/// The <c>*_lookup_display_name</c> bridges take a single PALOC native
/// handle, so a split language has to be offered part by part — that is what
/// <see cref="Display"/> and <see cref="FirstOrDefault"/> do. Namespaces
/// don't overlap, so at most one part answers.
/// </para>
/// </summary>
internal sealed class PalocLanguage : IDisposable
{
    private readonly NativePalocCatalog[] _parts;
    private readonly IPalocCatalog _combined;

    public PalocLanguage(NativePalocCatalog[] parts)
    {
        _parts = parts;
        _combined = parts.Length == 1 ? parts[0] : new MultiPalocCatalog(parts);
    }

    /// <summary>Number of files backing this language: 1 through 2.00,
    /// one per namespace from 2.01 on.</summary>
    public int PartCount => _parts.Length;

    /// <summary>Whole-language view for key lookups and entry walks.</summary>
    public IPalocCatalog Catalog => _combined;

    /// <summary>Total entries across every part.</summary>
    public int EntryCount => _combined.EntryCount;

    /// <summary>First non-empty string any part yields.</summary>
    public string? Display(Func<NativePalocCatalog, string?> lookup)
    {
        foreach (var part in _parts)
        {
            var hit = lookup(part);
            if (!string.IsNullOrEmpty(hit))
            {
                return hit;
            }
        }
        return null;
    }

    /// <summary>First non-null value any part yields.</summary>
    public T? FirstOrDefault<T>(Func<NativePalocCatalog, T?> lookup)
        where T : struct
    {
        foreach (var part in _parts)
        {
            if (lookup(part) is { } hit)
            {
                return hit;
            }
        }
        return null;
    }

    public void Dispose() => _combined.Dispose();
}
