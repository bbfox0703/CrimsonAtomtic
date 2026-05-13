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
    /// Known PALOC language codes the game ships. Sourced authoritatively
    /// from <c>list_all_paloc.py</c> against the 1.06 install — every
    /// entry below was verified to extract from the corresponding
    /// <c>localizationstring_&lt;code&gt;.paloc</c>. Order is by group
    /// number so the probe's first-pass discovery hits in the order the
    /// game stores them. Discovery probes each of these against every
    /// group in <see cref="PalocGroupRange"/>; codes that resolve are
    /// surfaced via <see cref="AvailableLanguages"/>.
    /// </summary>
    private static readonly string[] KnownLanguageCodes =
    [
        "kor",      // group 0019 — Korean
        "eng",      // group 0020 — English (the default)
        "jpn",      // group 0021 — Japanese
        "rus",      // group 0022 — Russian
        "tur",      // group 0023 — Turkish
        "spa-es",   // group 0024 — Spanish (Spain)
        "spa-mx",   // group 0025 — Spanish (Mexico, Latin America)
        "fre",      // group 0026 — French (note: "fre", NOT "fra")
        "ger",      // group 0027 — German
        "ita",      // group 0028 — Italian
        "pol",      // group 0029 — Polish
        "por-br",   // group 0030 — Portuguese (Brazil)
        "zho-tw",   // group 0031 — Chinese (Traditional)
        "zho-cn",   // group 0032 — Chinese (Simplified)
    ];

    /// <summary>
    /// Inclusive group range to probe for PALOC files. As of 1.06 the
    /// highest-numbered language is at group 0032 (zho-cn); we probe up
    /// to 0050 to leave headroom for future patches without missing a
    /// newly-added language.
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
    /// Pre-built <c>(typeByte, key) → name</c> map per loaded language.
    /// Built once when a language's PALOC catalog loads by walking every
    /// entry and keeping the records whose type byte sits in
    /// <see cref="NameTypeBytes"/>. PALOC's <c>string_key</c> is a
    /// decimal-formatted u64 where bits 63..32 are the namespace key and
    /// bits 7..0 are a type byte (0x70 == item, 0x30 == character /
    /// faction). The middle 24 bits aren't predictable, so the only
    /// reliable lookup path is to scan once and key the resulting dict
    /// by (typeByte, upper32).
    /// </summary>
    private readonly Dictionary<string, Dictionary<(byte TypeByte, uint Key), string>> _namesByLang =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Type byte for the item-name flavour of a PALOC entry.</summary>
    private const byte ItemNameTypeByte = 0x70;

    /// <summary>
    /// Type byte shared by character and faction names. Empirically
    /// confirmed against 1.06: <c>CharacterKey 704 → "Carl"</c>,
    /// <c>CharacterKey 51306 → "Greymane"</c>,
    /// <c>FactionKey 1000063 → "Dusksong"</c> all sit at this byte.
    /// The numeric ranges don't collide (factions are 1,000,000+,
    /// characters are 0..999,999), so one map for both works.
    /// </summary>
    private const byte CharacterNameTypeByte = 0x30;

    /// <summary>
    /// Type byte for in-world interactable / scenery names — the home
    /// of <c>GimmickInfoKey</c>. Confirmed against 1.06: every
    /// harvested GimmickInfo key value (1002143 → "Grindstone", 1004966
    /// → "Anvil", 1007815 → "Skybridge Gate", 1003226 → "Abyss Nexus",
    /// …) resolves cleanly here, and high-numbered keys only have
    /// 0x00 entries (no namespace collision).
    /// </summary>
    private const byte GimmickNameTypeByte = 0x00;

    /// <summary>Type bytes captured by <see cref="BuildNameMap"/>.</summary>
    private static readonly HashSet<byte> NameTypeBytes =
    [
        ItemNameTypeByte,
        CharacterNameTypeByte,
        GimmickNameTypeByte,
    ];

    /// <summary>
    /// Maps a save-schema <c>TypeName</c> (the string the Rust decoder
    /// emits on each field, e.g. "ItemKey") to the PALOC type byte that
    /// holds the localized name for that namespace. Add a row here to
    /// extend coverage — no other code changes required.
    ///
    /// <para>
    /// Deliberately omitted:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>MissionKey</c> — the values do hit 0x70 cleanly, but
    ///   those are <i>item</i> entries: the game stores missions and
    ///   their tracking items in the same numeric ID space, so
    ///   `MissionKey 1003440` resolves to "Hearty Braised Meat and
    ///   Fish" (the dish the mission rewards), not a mission title.
    ///   Verified against the user's <c>out/output*.txt</c> dumps —
    ///   PALOC has no per-key mission-name namespace; mission text
    ///   only appears at 0xC1 with embedded <c>staticInfo:Mission:</c>
    ///   templates that need a separate resolver. Better to show the
    ///   raw key than mislead with an item name.</item>
    ///   <item><c>KnowledgeKey</c> / <c>QuestKey</c> — straddle
    ///   multiple type bytes; large values just coincidentally hit
    ///   the item / character tables (see "gotchas" in docs/status.md).</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, byte> TypeNameToTypeByte =
        new(StringComparer.Ordinal)
        {
            ["ItemKey"]                        = ItemNameTypeByte,
            ["FactionKey"]                     = CharacterNameTypeByte,
            ["CharacterKey"]                   = CharacterNameTypeByte,
            ["GimmickInfoKey"]                 = GimmickNameTypeByte,
            // Scene-object gimmicks (discovered interactables in the
            // open world) live at the same 0x00 byte as GimmickInfo.
            // Confirmed against 1.06: every harvested
            // LevelGimmickSceneObjectInfoKey (1000003 → "Circus Pillar",
            // 1000043 → "Skybridge Gate", 1000109 → "Chair",
            // 1000121 → "Oak Barrel", …) cleanly resolves here.
            ["LevelGimmickSceneObjectInfoKey"] = GimmickNameTypeByte,
        };

    /// <summary>
    /// Hardcoded labels for the 18 <c>InventoryKey</c> containers the
    /// game ships. InventoryKey doesn't have a PALOC namespace — the
    /// small u16 values (1, 2, …, 20) collide with every other table
    /// that uses small integers — so the only honest resolution is a
    /// manually-maintained table.
    ///
    /// First-pass guesses (named by container content) were corrected
    /// by the user based on in-game knowledge: several "X items"
    /// containers are actually named after the camp upgrade / chest
    /// that holds them (Kuku Pot, Enhanced Kuku Cooler, Gatherables
    /// Chest, etc.). Run <c>Probe_InventoryKeyContainers</c> in the
    /// test project against a new save / patch to surface new keys;
    /// the *labels* themselves need an in-game check to get right.
    /// </summary>
    private static readonly Dictionary<uint, string> InventoryContainerLabels =
        new()
        {
            [1]  = "Camp & Contributions",
            [2]  = "Backpack",
            [5]  = "Quest Artifacts",
            [8]  = "Private Storage",
            [9]  = "Camp Trading Goods",
            [10] = "Valuables",
            [13] = "Kuku Pot",
            // 14 — observed to hold "Ordinary Gloves" in slot0; user
            // confirms it is NOT an equipment container (their gear
            // lives elsewhere). Leave un-labelled until identified.
            [16] = "Enhanced Kuku Cooler",
            [19] = "Gatherables Chest",
            [20] = "Collectibles",
        };

    private NativeItemInfoCatalog? _itemInfo;
    private string? _gameRoot;
    private string? _secondaryLanguage;

    /// <summary>
    /// Item-icon resolver. Always non-null; <see cref="IconProvider.IsAvailable"/>
    /// tells the UI whether to bother rendering the icon column at all.
    /// </summary>
    public IconProvider Icons { get; private set; } = new(configuredPath: null, exeDirectory: null);

    public LocalizationProvider(IPazExtractor paz)
    {
        ArgumentNullException.ThrowIfNull(paz);
        _paz = paz;
    }

    /// <summary>
    /// Re-seed the icon provider with a fresh configured path. Called
    /// once during bootstrap and again whenever the user edits the
    /// path through the Tools menu. The previously-loaded Bitmap
    /// cache is dropped — different folder, potentially different
    /// icons keyed by the same ItemKey.
    /// </summary>
    public void ConfigureIconProvider(string? configuredPath, string? exeDirectory)
    {
        Icons = new IconProvider(configuredPath, exeDirectory);
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
            // Pre-walk the catalog to build the (typeByte, key) → name
            // map for the type bytes we care about. One-time per-language
            // cost; turns every subsequent name resolution into an O(1)
            // dictionary lookup.
            _namesByLang[langCode] = BuildNameMap(cat);
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
    /// decoded <c>string_key</c> is a u64 with a type byte in
    /// <see cref="NameTypeBytes"/>, and build a <c>(typeByte, key) → value</c>
    /// map. Duplicate (typeByte, key) pairs resolve by last-wins. ~180k
    /// entries on the English table; the walk costs ~1-2 s per language
    /// on SSD.
    /// </summary>
    private static Dictionary<(byte, uint), string> BuildNameMap(NativePalocCatalog cat)
    {
        // Each captured type byte contributes ~6k entries in 1.06's
        // English table; capacity hint stays generous to avoid rehash
        // during the walk.
        var map = new Dictionary<(byte, uint), string>(
            capacity: Math.Max(1, cat.EntryCount / 8));
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
            var typeByte = (byte)(sid & 0xFFul);
            if (!NameTypeBytes.Contains(typeByte))
            {
                continue;
            }
            var upper = (uint)(sid >> 32);
            map[(typeByte, upper)] = entry.Value.Value;
        }
        return map;
    }

    /// <summary>
    /// Resolve <paramref name="key"/> against a specific loaded catalog,
    /// defaulting to <see cref="DefaultLanguage"/> when
    /// <paramref name="langCode"/> is null. Returns <c>null</c> when the
    /// requested catalog isn't loaded or the key is absent.
    /// </summary>
    public string? Lookup(string? key, string? langCode = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        var code = langCode ?? DefaultLanguage;
        return _catalogs.TryGetValue(code, out var cat) ? cat.Lookup(key) : null;
    }

    /// <summary>
    /// Browse-localization helper — returns the <paramref name="index"/>th
    /// entry of a specific catalog (English by default). Lets the
    /// Browse Localization dialog enumerate the secondary catalog
    /// without poking at the raw native handle.
    /// </summary>
    public (string Key, string Value)? GetEntry(int index, string? langCode = null)
    {
        var code = langCode ?? DefaultLanguage;
        return _catalogs.TryGetValue(code, out var cat) ? cat.GetEntry(index) : null;
    }

    /// <summary>
    /// Number of entries in a given catalog (English by default).
    /// 0 when the catalog isn't loaded.
    /// </summary>
    public int EntryCountFor(string? langCode = null) =>
        _catalogs.TryGetValue(langCode ?? DefaultLanguage, out var cat) ? cat.EntryCount : 0;

    /// <summary>
    /// Low-level lookup: resolve <paramref name="key"/> at a specific
    /// PALOC <paramref name="typeByte"/> in the given language. Returns
    /// <c>null</c> when no entry exists. Callers usually want
    /// <see cref="ResolveByFieldTypeName"/> instead.
    /// </summary>
    public string? LookupName(byte typeByte, uint key, string langCode) =>
        _namesByLang.TryGetValue(langCode, out var map)
        && map.TryGetValue((typeByte, key), out var name)
            ? name
            : null;

    /// <summary>
    /// Item-name lookup. Kept for callers that already know they're
    /// dealing with an item ID. Forwards to <see cref="LookupName"/>
    /// at type byte 0x70.
    /// </summary>
    public string? LookupItemName(uint itemId, string langCode) =>
        LookupName(ItemNameTypeByte, itemId, langCode);

    /// <summary>
    /// iteminfo's internal identifier for the item (e.g.
    /// <c>"Pyeonjeon_Arrow"</c>). Useful as a fallback display when
    /// no PALOC entry exists.
    /// </summary>
    public string? ItemInfoStringKey(uint itemId) => _itemInfo?.LookupStringKey(itemId);

    /// <summary>
    /// Enumerate one entry of the loaded iteminfo bridge by index.
    /// Returns <c>null</c> when the bridge isn't loaded or the index
    /// is out of range. Used by the Item Picker dialog to walk every
    /// known item without re-extracting iteminfo.pabgb.
    /// </summary>
    public (uint ItemKey, string StringKey)? GetItem(int index) =>
        _itemInfo?.GetEntry(index);

    /// <summary>
    /// Game-defined max_stack_count for an item. Returns <c>null</c>
    /// when the iteminfo bridge isn't loaded or the key isn't known.
    /// Drives the "Set to max stack" UX in the edit panel.
    /// </summary>
    public ulong? GetItemMaxStackCount(uint itemKey) =>
        _itemInfo?.LookupMaxStackCount(itemKey);

    /// <summary>
    /// True when the given save-schema field <c>TypeName</c> has a
    /// name-resolution path — either PALOC-backed (item / faction /
    /// character / gimmick) or hardcoded (InventoryKey). Lets callers
    /// cheaply gate UI work without trying a lookup that's bound to
    /// come back empty.
    /// </summary>
    public static bool CanResolveTypeName(string? typeName) =>
        typeName is not null
        && (TypeNameToTypeByte.ContainsKey(typeName) || typeName == "InventoryKey");

    /// <summary>
    /// Convenience: look up the same key in English and (if set) the
    /// user's secondary language. English-side ItemKey lookups fall
    /// back to the iteminfo internal name so users always see
    /// *something* for known items. The secondary language returns
    /// <c>null</c> on miss — duplicating the iteminfo string in both
    /// columns would just be noise. For non-item type bytes (faction,
    /// character) there's no equivalent fallback.
    /// </summary>
    public (string? English, string? Secondary) ResolveItemName(uint itemId) =>
        ResolveAt(ItemNameTypeByte, itemId);

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
    public string ResolveItemNameFormatted(uint itemId) =>
        FormatPair(ResolveAt(ItemNameTypeByte, itemId));

    /// <summary>
    /// Resolve a key whose schema TypeName indicates a known name
    /// namespace (<c>ItemKey</c> / <c>FactionKey</c> / <c>CharacterKey</c>
    /// / gimmick / <c>InventoryKey</c>, see <see cref="TypeNameToTypeByte"/>
    /// and <see cref="InventoryContainerLabels"/>). Returns the empty
    /// string when <paramref name="typeName"/> isn't a resolvable
    /// namespace, or when the key has no entry. The VM-side wrappers
    /// route every name column through this single entry point.
    /// </summary>
    public string ResolveByFieldTypeName(string? typeName, uint key)
    {
        if (typeName == "InventoryKey")
        {
            // InventoryKey lives outside PALOC entirely — labels are
            // a hardcoded table sourced from inspecting live saves.
            return InventoryContainerLabels.GetValueOrDefault(key, string.Empty);
        }
        if (typeName is null
            || !TypeNameToTypeByte.TryGetValue(typeName, out var typeByte))
        {
            return string.Empty;
        }
        return FormatPair(ResolveAt(typeByte, key));
    }

    /// <summary>
    /// Shared lookup core: returns (english, secondary) for one
    /// (typeByte, key) pair. The iteminfo fallback only kicks in for
    /// the item-name byte — there's no equivalent table for characters
    /// or factions.
    /// </summary>
    private (string? English, string? Secondary) ResolveAt(byte typeByte, uint key)
    {
        var english = LookupName(typeByte, key, DefaultLanguage);
        if (string.IsNullOrEmpty(english) && typeByte == ItemNameTypeByte)
        {
            english = ItemInfoStringKey(key);
        }
        var secondary = _secondaryLanguage is null
            ? null
            : LookupName(typeByte, key, _secondaryLanguage);
        return (english, secondary);
    }

    private static string FormatPair((string? English, string? Secondary) pair)
    {
        var hasEn = !string.IsNullOrEmpty(pair.English);
        var hasSec = !string.IsNullOrEmpty(pair.Secondary);
        if (!hasEn && !hasSec) return string.Empty;
        if (!hasSec) return pair.English!;
        if (!hasEn) return pair.Secondary!;
        return $"{pair.English} / {pair.Secondary}";
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
