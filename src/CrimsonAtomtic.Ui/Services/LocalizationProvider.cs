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
                    // Probe by attempting the (cheap) PAMT lookup. The
                    // first call with a zero-sized buffer returns
                    // either BUFFER_TOO_SMALL (file exists) or
                    // NOT_FOUND. We swallow the file's bytes when
                    // they're returned — discovery doesn't need them
                    // yet, the lazy load below will pay the cost.
                    var bytes = _paz.ExtractFile(pamt, PalocDirectory, fileName);
                    _languageSources[code] = (group, fileName);
                    // We extracted the bytes; might as well cache the
                    // catalog so the lazy load below doesn't re-extract.
                    // Disposing the temp byte array is cheap.
                    if (!_catalogs.ContainsKey(code))
                    {
                        _catalogs[code] = NativePalocCatalog.LoadFromBytes(bytes);
                    }
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
            _catalogs[langCode] = NativePalocCatalog.LoadFromBytes(bytes);
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
    /// Pipe a save's <c>u32</c> item ID through iteminfo + PALOC to
    /// yield the localized display name in the given language. Returns
    /// <c>null</c> when any link in the chain breaks (iteminfo not
    /// loaded, item key unknown, string key not in the chosen
    /// language's PALOC).
    /// </summary>
    public string? LookupItemName(uint itemId, string langCode)
    {
        if (_itemInfo is null)
        {
            return null;
        }
        var stringKey = _itemInfo.LookupStringKey(itemId);
        if (string.IsNullOrEmpty(stringKey))
        {
            return null;
        }
        return _catalogs.TryGetValue(langCode, out var cat) ? cat.Lookup(stringKey) : null;
    }

    /// <summary>
    /// Convenience: look up the same item ID in English and (if set)
    /// the user's secondary language. Either or both may be
    /// <c>null</c>; the UI shows whichever non-null parts it gets.
    /// </summary>
    public (string? English, string? Secondary) ResolveItemName(uint itemId)
    {
        var english = LookupItemName(itemId, DefaultLanguage);
        var secondary = _secondaryLanguage is null
            ? null
            : LookupItemName(itemId, _secondaryLanguage);
        return (english, secondary);
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
