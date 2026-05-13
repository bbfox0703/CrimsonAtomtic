using System.Globalization;
using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Application-level service that owns the loaded iteminfo bridge +
/// per-language PALOC catalogs and exposes a single
/// <see cref="ResolveItemName"/> entry point.
///
/// Bootstrap (called once from <see cref="App.OnFrameworkInitializationCompleted"/>):
/// <list type="number">
///   <item>locate a Crimson Desert install via <c>IPlatformPaths.GameInstallRoot</c>;</item>
///   <item>PAZ-extract <c>iteminfo.pabgb</c> from group <c>0008</c> →
///         <c>NativeItemInfoCatalog</c>;</item>
///   <item>discover every available localization language by probing
///         <c>localizationstring_&lt;code&gt;.paloc</c> across the
///         well-known group range <c>0019..0050</c>;</item>
///   <item>eagerly load the English (<c>eng</c>) PALOC; lazy-load any
///         additional language on demand.</item>
/// </list>
///
/// Degrades cleanly when no install is found (probe gracefully fails;
/// the editor continues to function without resolved names).
/// </summary>
public sealed class LocalizationProvider : IDisposable
{
    /// <summary>The primary language. Always loaded when available.</summary>
    public const string DefaultLanguage = "eng";

    private const string PalocDirectory = "gamedata/stringtable/binary__";
    private const string ItemInfoDirectory = "gamedata/binary__/client/bin";
    private const string ItemInfoFileName = "iteminfo.pabgb";

    /// <summary>
    /// Known PALOC language codes the game ships. Order matches the
    /// crimson-rs README + community references. Discovery probes each
    /// of these against every group in <see cref="PalocGroupRange"/>;
    /// codes that resolve are surfaced via <see cref="AvailableLanguages"/>.
    /// </summary>
    private static readonly string[] KnownLanguageCodes =
    [
        "eng",     // English
        "kor",     // Korean
        "jpn",     // Japanese
        "zho-tw",  // Chinese (Traditional)
        "zho-cn",  // Chinese (Simplified)
        "ger",     // German
        "fra",     // French
        "spa",     // Spanish
        "por",     // Portuguese
        "rus",     // Russian
        "tur",     // Turkish
        "tha",     // Thai
        "ind",     // Indonesian
        "ara",     // Arabic
    ];

    /// <summary>
    /// Inclusive group range to probe for PALOC files. 0019 hosts the
    /// Korean PALOC, 0020 hosts English; later groups host the other
    /// languages. Higher bound is empirical.
    /// </summary>
    private static readonly (int Lo, int Hi) PalocGroupRange = (19, 50);

    private readonly IPazExtractor _paz;

    /// <summary>For each discovered language code, the (group, filename)
    /// pair that holds it. Populated by Bootstrap.</summary>
    private readonly Dictionary<string, (string Group, string FileName)> _languageSources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded catalogs, keyed by language code.</summary>
    private readonly Dictionary<string, NativePalocCatalog> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pre-built <c>itemKey → name</c> map per loaded language. Built
    /// once when a language's PALOC catalog loads by walking every
    /// entry and keeping the type-0x70 records. PALOC's <c>string_key</c>
    /// is a decimal-formatted u64 where bits 63..32 are the item key
    /// and bits 7..0 are a type byte (0x70 == item name); the middle
    /// 24 bits aren't predictable, so the only reliable lookup path
    /// is to scan once and key the resulting dict by the upper 32 bits.
    /// </summary>
    private readonly Dictionary<string, Dictionary<uint, string>> _itemNamesByLang =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Type byte for the item-name flavour of a PALOC entry.</summary>
    private const byte ItemNameTypeByte = 0x70;

    private NativeItemInfoCatalog? _itemInfo;
    private string? _gameRoot;
    private string? _secondaryLanguage;

    public LocalizationProvider(IPazExtractor paz)
    {
        ArgumentNullException.ThrowIfNull(paz);
        _paz = paz;
    }

    /// <summary>True when the iteminfo bridge AND the English PALOC are loaded.</summary>
    public bool IsLoaded => _itemInfo is not null && _catalogs.ContainsKey(DefaultLanguage);

    /// <summary>Number of entries in the English PALOC, or 0 when not loaded.</summary>
    public int EntryCount =>
        _catalogs.TryGetValue(DefaultLanguage, out var cat) ? cat.EntryCount : 0;

    /// <summary>Number of items in the iteminfo bridge, or 0 when not loaded.</summary>
    public int ItemCount => _itemInfo?.EntryCount ?? 0;

    /// <summary>
    /// Language codes discovered in the game install (e.g. <c>"eng"</c>,
    /// <c>"zho-tw"</c>, …). Always includes <see cref="DefaultLanguage"/>
    /// when bootstrap succeeded.
    /// </summary>
    public IReadOnlyCollection<string> AvailableLanguages => _languageSources.Keys;

    /// <summary>
    /// User-selected secondary language code. <c>null</c> when only
    /// English should be displayed. Setting this triggers a lazy
    /// load (and disposes the previously-active secondary catalog if
    /// no other reference holds it — but currently we keep all loaded
    /// catalogs in memory for snappy switching).
    /// </summary>
    public string? SecondaryLanguage
    {
        get => _secondaryLanguage;
        set
        {
            // Normalise: empty / "eng" / same-as-default means "no secondary".
            var normalised = string.IsNullOrWhiteSpace(value)
                             || value.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase)
                ? null
                : value;
            if (normalised is not null && !_languageSources.ContainsKey(normalised))
            {
                // Reject unknown codes silently — keep the previous value.
                return;
            }
            _secondaryLanguage = normalised;
            if (normalised is not null)
            {
                TryLoadCatalog(normalised);
            }
        }
    }

    /// <summary>
    /// Bootstrap from a Crimson Desert install root. Returns
    /// <c>true</c> when at least the iteminfo bridge + English PALOC
    /// loaded successfully; <c>false</c> means the editor will run
    /// without name resolution.
    /// </summary>
    public bool TryBootstrapFromGameRoot(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot))
        {
            return false;
        }
        _gameRoot = gameRoot;

        // ── iteminfo bridge (group 0008). Required for any item-name
        // resolution to work. Failure here means we still load PALOC
        // (so the Browse Localization dialog works) but ResolveItemName
        // returns null everywhere.
        TryBootstrapItemInfo(gameRoot);

        // ── Discover available PALOC languages by probing the well-known
        // group range. PAMT parses are fast (a few ms each); the probe
        // exits early on NOT_FOUND so the total cost stays under a
        // second on SSD.
        DiscoverLanguages(gameRoot);

        // ── Eagerly load English (the primary). Other languages load
        // lazily on demand via SecondaryLanguage = "...".
        TryLoadCatalog(DefaultLanguage);

        return IsLoaded;
    }

    private void TryBootstrapItemInfo(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        try
        {
            var bytes = _paz.ExtractFile(pamt, ItemInfoDirectory, ItemInfoFileName);
            _itemInfo?.Dispose();
            _itemInfo = NativeItemInfoCatalog.LoadFromBytes(bytes);
        }
        catch (CrimsonSaveException)
        {
            // ItemInfo missing or malformed — degrade gracefully.
        }
        catch (IOException)
        {
        }
    }

    private void DiscoverLanguages(string gameRoot)
    {
        _languageSources.Clear();
        for (var n = PalocGroupRange.Lo; n <= PalocGroupRange.Hi; n++)
        {
            var group = $"{n:D4}";
            var pamt = Path.Combine(gameRoot, group, "0.pamt");
            if (!File.Exists(pamt))
            {
                continue;
            }
            foreach (var code in KnownLanguageCodes)
            {
                if (_languageSources.ContainsKey(code))
                {
                    continue; // first hit wins
                }
                var fileName = $"localizationstring_{code}.paloc";
                try
                {
                    // Probe by attempting the extract — `crimson_paz_extract_file`
                    // returns NOT_FOUND fast when the file isn't in this PAMT.
                    // On success we *discard* the bytes here: caching all 14
                    // language catalogs eagerly would consume ~350 MB. The
                    // catalogs are re-extracted lazily on first request
                    // through TryLoadCatalog below.
                    _ = _paz.ExtractFile(pamt, PalocDirectory, fileName);
                    _languageSources[code] = (group, fileName);
                }
                catch (CrimsonSaveException ex) when (ex.ErrorCode == -16) // NOT_FOUND
                {
                    // file not in this group — keep probing
                }
                catch (CrimsonSaveException)
                {
                    // Other extraction failures: skip this candidate.
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private bool TryLoadCatalog(string langCode)
    {
        if (_catalogs.ContainsKey(langCode))
        {
            return true;
        }
        if (_gameRoot is null || !_languageSources.TryGetValue(langCode, out var src))
        {
            return false;
        }
        try
        {
            var pamt = Path.Combine(_gameRoot, src.Group, "0.pamt");
            var bytes = _paz.ExtractFile(pamt, PalocDirectory, src.FileName);
            var cat = NativePalocCatalog.LoadFromBytes(bytes);
            _catalogs[langCode] = cat;
            // Pre-walk the catalog to build the u32 → name map for type
            // 0x70 entries. This is a one-time per-language cost that
            // turns every subsequent ResolveItemName into an O(1)
            // dictionary lookup.
            _itemNamesByLang[langCode] = BuildItemNameMap(cat);
            return true;
        }
        catch (CrimsonSaveException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walk every entry in <paramref name="cat"/>, keep the ones whose
    /// decoded <c>string_key</c> is a u64 with type-byte 0x70, and
    /// build a <c>itemKey → value</c> map. Mirrors the Python
    /// <c>export_for_ce.py</c> "extract paloc 0x70" loop: duplicate
    /// item keys are resolved by last-wins. ~180k entries on the
    /// English table; the walk costs ~1-2 s per language on SSD.
    /// </summary>
    private static Dictionary<uint, string> BuildItemNameMap(NativePalocCatalog cat)
    {
        var map = new Dictionary<uint, string>(capacity: Math.Max(1, cat.EntryCount / 16));
        for (var i = 0; i < cat.EntryCount; i++)
        {
            var entry = cat.GetEntry(i);
            if (entry is null)
            {
                continue;
            }
            if (!ulong.TryParse(entry.Value.Key, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var sid))
            {
                // Non-numeric keys exist for non-item entries (e.g. UI
                // strings); skip silently.
                continue;
            }
            if ((sid & 0xFFul) != ItemNameTypeByte)
            {
                continue;
            }
            var itemKey = (uint)(sid >> 32);
            map[itemKey] = entry.Value.Value;
        }
        return map;
    }

    /// <summary>
    /// Resolve <paramref name="key"/> against the English (default)
    /// catalog. Returns <c>null</c> when no PALOC is loaded or when the
    /// key is absent. Kept for backward compat with the Browse
    /// Localization dialog.
    /// </summary>
    public string? Lookup(string? key) =>
        string.IsNullOrEmpty(key) ? null
        : _catalogs.TryGetValue(DefaultLanguage, out var cat) ? cat.Lookup(key)
        : null;

    /// <summary>Browse-localization helper.</summary>
    public (string Key, string Value)? GetEntry(int index) =>
        _catalogs.TryGetValue(DefaultLanguage, out var cat) ? cat.GetEntry(index) : null;

    /// <summary>
    /// Resolve a save's <c>u32</c> item ID to its localized display
    /// name in the given language. Looks up against the pre-built
    /// type-0x70 map for that language. Returns <c>null</c> when no
    /// PALOC entry exists for that item; the caller can fall back to
    /// iteminfo's <c>string_key</c> via <see cref="ItemInfoStringKey"/>.
    /// </summary>
    public string? LookupItemName(uint itemId, string langCode) =>
        _itemNamesByLang.TryGetValue(langCode, out var map)
        && map.TryGetValue(itemId, out var name)
            ? name
            : null;

    /// <summary>
    /// iteminfo's internal identifier for the item (e.g.
    /// <c>"Pyeonjeon_Arrow"</c>). Useful as a fallback display when
    /// no PALOC entry exists.
    /// </summary>
    public string? ItemInfoStringKey(uint itemId) => _itemInfo?.LookupStringKey(itemId);

    /// <summary>
    /// Convenience: look up the same item ID in English and (if set)
    /// the user's secondary language. English falls back to the
    /// iteminfo internal name when no PALOC entry exists so users
    /// always see *something* for known items. The secondary language
    /// returns <c>null</c> on miss — duplicating the iteminfo string
    /// in both columns would just be noise.
    /// </summary>
    public (string? English, string? Secondary) ResolveItemName(uint itemId)
    {
        var english = LookupItemName(itemId, DefaultLanguage)
                      ?? ItemInfoStringKey(itemId);
        var secondary = _secondaryLanguage is null
            ? null
            : LookupItemName(itemId, _secondaryLanguage);
        return (english, secondary);
    }

    /// <summary>
    /// Same as <see cref="ResolveItemName"/> but pre-formatted as a
    /// single display string. Returns the empty string when neither
    /// language resolves. Shape:
    /// <list type="bullet">
    ///   <item>English only: <c>"Gold"</c></item>
    ///   <item>English + secondary: <c>"Gold / 黃金"</c></item>
    ///   <item>Secondary only (rare): <c>"黃金"</c></item>
    ///   <item>Neither: empty string.</item>
    /// </list>
    /// Both the per-field DataGrid wrapper and the per-element
    /// DataGrid wrapper route through this so the column formatting
    /// stays consistent.
    /// </summary>
    public string ResolveItemNameFormatted(uint itemId)
    {
        var (english, secondary) = ResolveItemName(itemId);
        var hasEn = !string.IsNullOrEmpty(english);
        var hasSec = !string.IsNullOrEmpty(secondary);
        if (!hasEn && !hasSec) return string.Empty;
        if (!hasSec) return english!;
        if (!hasEn) return secondary!;
        return $"{english} / {secondary}";
    }

    public void Dispose()
    {
        _itemInfo?.Dispose();
        _itemInfo = null;
        foreach (var cat in _catalogs.Values)
        {
            cat.Dispose();
        }
        _catalogs.Clear();
    }
}
