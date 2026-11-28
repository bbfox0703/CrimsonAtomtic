namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// One learnable dye colour: a <c>Knowledge_&lt;Theme&gt;_DyeColor_&lt;n&gt;</c>
/// entry, the theme knowledge that goes with it and the dye item's
/// collection entry.
/// </summary>
/// <param name="ThemeId">Theme code as the names spell it: <c>Her</c>, <c>Dem_II</c>; <c>Grey</c> only when the palette mapping is unavailable.</param>
/// <param name="ThemeKey">The theme's own knowledge (named after its dyer, "Theoric"); null when there is none.</param>
/// <param name="ThemeName">The theme as the game shows it ("Hernandian Dye / 埃爾南德染劑"), else the dyer's name, else the theme id.</param>
/// <param name="Shade">0 = the theme's grey ramp (palette 0–8), 1..5 = bright, rich, plain, dark, deep (20 colours each); 1..10 for an unmapped Grey.</param>
/// <param name="Key">The colour's knowledge key.</param>
/// <param name="ColorName">"Bright Red / 亮紅色".</param>
/// <param name="InternalName"><c>Knowledge_Her_DyeColor_1</c>.</param>
/// <param name="DyeItemKey"><c>Knowledge_Collection_Prop_Dyewater_NNNN</c> whose name is this colour + " Dye"; null when none matches.</param>
/// <param name="ColorGroupKey">The <c>dyecolorgroupinfo</c> theme whose palette this knowledge gates; null without the mapping.</param>
/// <param name="Swatches">The palette colours this knowledge unlocks, in palette order; empty without the mapping.</param>
public sealed record KnownDyeEntry(
    string ThemeId,
    uint? ThemeKey,
    string ThemeName,
    int Shade,
    uint Key,
    string ColorName,
    string InternalName,
    uint? DyeItemKey,
    uint? ColorGroupKey = null,
    IReadOnlyList<DyeSwatch>? Swatches = null);

/// <summary>One palette colour.</summary>
public readonly record struct DyeSwatch(byte R, byte G, byte B);

/// <summary>
/// Which dye colours a save knows. The game gates every palette colour of
/// every theme (<c>dyecolorgroupinfo</c>) on a <c>CheckKnowledge</c>
/// condition (crimson-rs <c>dye_color_group_info</c>, 2.06 22-byte records),
/// so a known colour is a <c>KnowledgeSaveData._list</c> entry, not an item
/// flag. On 2.06 knowledgeinfo holds ten themes (Her, Dem_I..III, Del, Kwe,
/// Cal, Bar, Tom, Por) × 5 shades, Grey × 10 and 50 dye items. The exact
/// position → knowledge mapping comes from
/// <see cref="CrimsonAtomtic.RustInterop.NativeDyeColorGroupInfoCatalog.PaletteKnowledgeAt"/>
///: each theme's 109 colours use six keys, its own grey
/// (positions 0–8, Grey_1 for Her … Grey_10 for Bar) and its five shades
/// (20 positions each). The 50 dye items gate nothing in the palette; they
/// are the journal's "dye recipes" collection.
/// </summary>
public static class KnownDyeService
{
    private const string Prefix = "Knowledge_";
    private const string DyeColor = "_DyeColor";
    private const string DyeItemPrefix = "Knowledge_Collection_Prop_Dyewater_";

    /// <summary>
    /// Every learnable dye colour in knowledgeinfo, ordered by theme then
    /// shade. Empty when knowledgeinfo is not loaded.
    /// </summary>
    public static List<KnownDyeEntry> Scan(LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        var themes = new Dictionary<string, (uint Key, string Name)>(StringComparer.Ordinal);
        var shades = new List<(string Theme, int Shade, uint Key, string Name, string Internal)>();
        var dyeItems = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < localization.KnowledgeCount; i++)
        {
            if (localization.GetKnowledge(i) is not { } k)
            {
                continue;
            }
            var display = localization.ResolveByFieldTypeName("KnowledgeKey", k.KnowledgeKey);
            if (string.IsNullOrEmpty(display))
            {
                display = k.InternalName;
            }
            if (k.InternalName.StartsWith(DyeItemPrefix, StringComparison.Ordinal))
            {
                dyeItems.TryAdd(English(display), k.KnowledgeKey);
                continue;
            }
            if (Parse(k.InternalName) is not { } parsed)
            {
                continue;
            }
            if (parsed.Shade == 0)
            {
                themes.TryAdd(parsed.Theme, (k.KnowledgeKey, display));
            }
            else
            {
                shades.Add((parsed.Theme, parsed.Shade, k.KnowledgeKey, display, k.InternalName));
            }
        }

        if (FromPalette(localization, shades, themes, dyeItems) is { Count: > 0 } mapped)
        {
            return mapped;
        }

        var entries = new List<KnownDyeEntry>(shades.Count);
        foreach (var s in shades)
        {
            var hasTheme = themes.TryGetValue(s.Theme, out var theme);
            entries.Add(new KnownDyeEntry(
                s.Theme,
                hasTheme ? theme.Key : null,
                hasTheme ? theme.Name : s.Theme,
                s.Shade,
                s.Key,
                s.Name,
                s.Internal,
                dyeItems.TryGetValue(English(s.Name) + " Dye", out var item) ? item : null));
        }
        entries.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.ThemeId, b.ThemeId);
            return c != 0 ? c : a.Shade.CompareTo(b.Shade);
        });
        return entries;
    }

    /// <summary>
    /// Entries from the palette itself: one per knowledge a theme's palette
    /// uses, carrying the colours it unlocks. Empty when the table or the
    /// 2.06 mapping is unavailable (the caller then falls back to the names).
    /// </summary>
    private static List<KnownDyeEntry> FromPalette(
        LocalizationProvider localization,
        List<(string Theme, int Shade, uint Key, string Name, string Internal)> shades,
        Dictionary<string, (uint Key, string Name)> themes,
        Dictionary<string, uint> dyeItems)
    {
        var entries = new List<KnownDyeEntry>();
        if (localization.DyeColorGroupInfo is not { } groups)
        {
            return entries;
        }
        var byKey = new Dictionary<uint, (string Theme, int Shade, string Name, string Internal)>();
        foreach (var s in shades)
        {
            byKey.TryAdd(s.Key, (s.Theme, s.Shade, s.Name, s.Internal));
        }
        for (var i = 0; i < groups.EntryCount; i++)
        {
            if (groups.GetEntry(i) is not { } group)
            {
                continue;
            }
            // Keys in palette order with the colours each one unlocks.
            var order = new List<uint>();
            var swatches = new Dictionary<uint, List<DyeSwatch>>();
            var size = groups.PaletteSize(group.Key) ?? 0;
            for (var pos = 0; pos < size; pos++)
            {
                if (groups.PaletteKnowledgeAt(group.Key, pos) is not { } key
                    || groups.PaletteAt(group.Key, pos) is not { } c)
                {
                    continue;
                }
                if (!swatches.TryGetValue(key, out var list))
                {
                    swatches[key] = list = [];
                    order.Add(key);
                }
                list.Add(new DyeSwatch(c.R, c.G, c.B));
            }
            if (order.Count == 0)
            {
                continue;
            }
            // The theme is what its non-grey keys name; its grey joins it.
            var themeId = order.Select(k => byKey.TryGetValue(k, out var v) ? v.Theme : null)
                               .FirstOrDefault(t => t is not null && t != "Grey") ?? group.Name;
            var hasHead = themes.TryGetValue(themeId, out var head);
            var (english, secondary) = localization.ResolveDyeThemeName(group.Key);
            var themeName = english is null ? (hasHead ? head.Name : themeId)
                : secondary is null || secondary == english ? english
                : $"{english} / {secondary}";
            foreach (var key in order)
            {
                byKey.TryGetValue(key, out var k);
                var isGrey = k.Theme is null or "Grey";
                var name = k.Name ?? key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                entries.Add(new KnownDyeEntry(
                    themeId,
                    hasHead ? head.Key : null,
                    themeName,
                    isGrey ? 0 : k.Shade,
                    key,
                    name,
                    k.Internal ?? string.Empty,
                    dyeItems.TryGetValue(English(name) + " Dye", out var item) ? item : null,
                    group.Key,
                    swatches[key]));
            }
        }
        entries.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.ThemeId, b.ThemeId);
            return c != 0 ? c : a.Shade.CompareTo(b.Shade);
        });
        return entries;
    }

    /// <summary>
    /// The keys to learn for <paramref name="entries"/>: each colour, its
    /// theme (the in-game dye list is by theme) and, when asked, its dye
    /// item's collection entry. Keys already in <paramref name="learned"/>
    /// are left out; the result is sorted and distinct.
    /// </summary>
    public static List<uint> KeysToLearn(
        IEnumerable<KnownDyeEntry> entries, IReadOnlySet<uint> learned, bool includeDyeItems)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(learned);
        var keys = new SortedSet<uint>();
        foreach (var e in entries)
        {
            keys.Add(e.Key);
            if (e.ThemeKey is { } theme) keys.Add(theme);
            if (includeDyeItems && e.DyeItemKey is { } item) keys.Add(item);
        }
        keys.ExceptWith(learned);
        return [.. keys];
    }

    /// <summary>
    /// <c>Knowledge_Her_DyeColor</c> → (Her, 0); <c>Knowledge_Dem_DyeColor_II_3</c>
    /// → (Dem_II, 3); <c>Knowledge_Grey_DyeColor_10</c> → (Grey, 10). Null for
    /// any other name.
    /// </summary>
    public static (string Theme, int Shade)? Parse(string internalName)
    {
        ArgumentNullException.ThrowIfNull(internalName);
        if (!internalName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }
        var at = internalName.IndexOf(DyeColor, Prefix.Length, StringComparison.Ordinal);
        if (at <= Prefix.Length)
        {
            return null;
        }
        var code = internalName[Prefix.Length..at];
        if (code.Contains('_', StringComparison.Ordinal))
        {
            return null; // e.g. Knowledge_Node_… — theme codes are one token
        }
        var rest = internalName[(at + DyeColor.Length)..];
        if (rest.Length == 0)
        {
            return (code, 0);
        }
        if (rest[0] != '_')
        {
            return null;
        }
        var parts = rest[1..].Split('_');
        string? roman = null;
        var shade = 0;
        foreach (var part in parts)
        {
            if (part is "I" or "II" or "III" or "IV" or "V" && roman is null && shade == 0)
            {
                roman = part;
            }
            else if (int.TryParse(part, System.Globalization.NumberStyles.None,
                         System.Globalization.CultureInfo.InvariantCulture, out var n) && shade == 0 && n > 0)
            {
                shade = n;
            }
            else
            {
                return null;
            }
        }
        return (roman is null ? code : $"{code}_{roman}", shade);
    }

    private static string English(string display)
    {
        var slash = display.IndexOf(" / ", StringComparison.Ordinal);
        return slash < 0 ? display : display[..slash];
    }
}
