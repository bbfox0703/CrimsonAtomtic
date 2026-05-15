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
///   <item>PAZ-extract <c>stringinfo.pabgb</c> (same group) →
///         <c>NativeStringInfoCatalog</c> for resolving icon-path
///         hashes harvested from iteminfo;</item>
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
    private const string StringInfoFileName = "stringinfo.pabgb";
    // Sibling .pabgb files in the same `gamedata/binary__/client/bin/`
    // directory, all under group 0008.
    private const string MissionInfoFileName     = "missioninfo.pabgb";
    private const string QuestInfoFileName       = "questinfo.pabgb";
    private const string StageInfoFileName       = "stageinfo.pabgb";
    private const string KnowledgeInfoFileName   = "knowledgeinfo.pabgb";
    private const string QuestGaugeInfoFileName  = "questgaugeinfo.pabgb";
    private const string GimmickInfoFileName     = "gimmickinfo.pabgb";
    private const string CharacterInfoFileName   = "characterinfo.pabgb";
    private const string SubLevelInfoFileName    = "sublevelinfo.pabgb";
    private const string SkillPabgbFileName      = "skill.pabgb";
    private const string SkillPabghFileName      = "skill.pabgh";

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
    /// <c>MissionKey</c> / <c>QuestKey</c> / <c>StageKey</c> /
    /// <c>KnowledgeKey</c> / <c>QuestGaugeKey</c> / <c>SkillKey</c> /
    /// <c>CharacterKey</c> are resolved through dedicated
    /// <c>*.pabgb</c> bridges in the new table-driven path (see
    /// <see cref="ResolveViaKeyTable"/>) — they don't sit at a single
    /// PALOC type byte and are routed by schema TypeName, not by
    /// integer namespace. <c>CharacterKey</c> in particular: the
    /// bridge strips a "cat byte" (hi-byte) the raw byte path can't,
    /// so leaving it on the byte path would surface wrong-namespace
    /// matches for FieldNPC spawn rows.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, byte> TypeNameToTypeByte =
        new(StringComparer.Ordinal)
        {
            ["ItemKey"]                        = ItemNameTypeByte,
            // FactionKey shares PALOC byte 0x30 with character display
            // names but lives outside characterinfo.pabgb — keep it on
            // the raw byte path, no cat-byte strip needed.
            ["FactionKey"]                     = CharacterNameTypeByte,
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
    /// Schema TypeNames that resolve through the dedicated key-table
    /// bridges instead of (or before) a PALOC type byte scan. Routed by
    /// <see cref="ResolveViaKeyTable"/> — each entry corresponds to a
    /// <c>Native*InfoCatalog</c> field below. Resolution preference:
    /// PALOC-localized title (when the bridge ships
    /// <c>LookupDisplayName</c>) → internal name fallback → if both
    /// produce nothing AND the TypeName is also in
    /// <see cref="TypeNameToTypeByte"/>, fall through to the PALOC-byte
    /// path. The PALOC fallback matters specifically for
    /// <c>GimmickInfoKey</c> / <c>LevelGimmickSceneObjectInfoKey</c>:
    /// the new <c>gimmickinfo.pabgb</c> bridge covers most rows but
    /// not the legacy scene-object 0x00 slice, and we want both
    /// resolutions to reach the column.
    /// </summary>
    private static readonly HashSet<string> TableDrivenKeyTypes = new(StringComparer.Ordinal)
    {
        "MissionKey",
        "QuestKey",
        "StageKey",
        "KnowledgeKey",
        "QuestGaugeKey",
        "SkillKey",
        "GimmickInfoKey",
        "LevelGimmickSceneObjectInfoKey",
        "SubLevelKey",
        "CharacterKey",
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
    private NativeStringInfoCatalog? _stringInfo;
    private NativeMissionInfoCatalog? _missionInfo;
    private NativeQuestInfoCatalog? _questInfo;
    private NativeStageInfoCatalog? _stageInfo;
    private NativeKnowledgeInfoCatalog? _knowledgeInfo;
    private NativeQuestGaugeInfoCatalog? _questGaugeInfo;
    private NativeSkillInfoCatalog? _skillInfo;
    private NativeGimmickInfoCatalog? _gimmickInfo;
    private NativeCharacterInfoCatalog? _characterInfo;
    private NativeSubLevelInfoCatalog? _subLevelInfo;
    private string? _gameRoot;
    private string? _secondaryLanguage;

    /// <summary>
    /// Item-icon resolver. Always non-null; <see cref="IconProvider.IsAvailable"/>
    /// tells the UI whether to bother rendering the icon column at all.
    /// Default instance points at a placeholder path that never exists —
    /// real wiring happens via <see cref="ConfigureIconProvider"/> at
    /// app startup once the platform paths are known.
    /// </summary>
    public IconProvider Icons { get; private set; } = new(string.Empty);

    /// <summary>
    /// NPC portrait resolver, lazy + on-demand. Always non-null;
    /// <see cref="PortraitProvider.IsAvailable"/> tells the UI whether
    /// to bother rendering a portrait column. Default instance points
    /// at a placeholder path — real wiring happens via
    /// <see cref="ConfigurePortraitProvider"/> at app startup once the
    /// platform paths AND the characterinfo bridge are bootstrapped.
    /// </summary>
    public PortraitProvider Portraits { get; private set; }

    public LocalizationProvider(IPazExtractor paz)
    {
        ArgumentNullException.ThrowIfNull(paz);
        _paz = paz;
        // Stub portrait provider; replaced in ConfigurePortraitProvider
        // after the platform paths and game install are known. Using a
        // stub keeps Portraits non-null so callers don't need to
        // null-guard the property itself.
        Portraits = new PortraitProvider(string.Empty, paz, this, null);
    }

    /// <summary>
    /// Re-seed the icon provider at <paramref name="rootDirectory"/>.
    /// Called once during bootstrap (with <c>%LOCALAPPDATA%\CrimsonAtomtic\IconCache\</c>)
    /// and again after Tools → Extract Icons so the Bitmap cache is
    /// dropped and the FileCount snapshot refreshes against the
    /// freshly-written .webp files.
    /// </summary>
    public void ConfigureIconProvider(string rootDirectory)
    {
        Icons = new IconProvider(rootDirectory);
    }

    /// <summary>
    /// Re-seed the portrait provider at <paramref name="cacheRootDirectory"/>.
    /// Called once during bootstrap (with
    /// <c>%LOCALAPPDATA%\CrimsonAtomtic\PortraitCache\</c>) and again
    /// if the game install changes (Tools → Set Game Install Folder)
    /// so the new install's PAMT becomes the source for cold-path
    /// extraction. Disk-cached portraits from a previous game-root
    /// stay valid across the swap (filename keys on CharacterKey, not
    /// install path).
    /// </summary>
    public void ConfigurePortraitProvider(string cacheRootDirectory)
    {
        Portraits = new PortraitProvider(cacheRootDirectory, _paz, this, _gameRoot);
    }

    /// <summary>True when the iteminfo bridge AND the English PALOC are loaded.</summary>
    public bool IsLoaded => _itemInfo is not null && _catalogs.ContainsKey(DefaultLanguage);

    /// <summary>
    /// Crimson Desert install root the provider was bootstrapped against,
    /// or <c>null</c> when bootstrap didn't find one. Exposed so the
    /// icon-extraction action can resolve <c>0012/0.pamt</c> without
    /// re-running platform-path discovery.
    /// </summary>
    public string? GameRoot => _gameRoot;

    /// <summary>
    /// PAZ extractor the provider was constructed with. Exposed so
    /// downstream actions (icon extraction, future asset operations)
    /// can reuse the same instance instead of allocating a new one.
    /// </summary>
    public IPazExtractor Paz => _paz;

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

        // ── stringinfo bridge (also group 0008). Resolves
        // StringInfoKey hashes harvested from iteminfo's icon_path /
        // map_icon_path fields. Optional: when missing, the
        // icon-extraction pipeline degrades but the editor keeps
        // working (the existing IconProvider cache path is unaffected).
        TryBootstrapStringInfo(gameRoot);

        // ── Key-resolver bridges (Mission/Quest/Stage/Knowledge live
        // alongside iteminfo in group 0008's
        // gamedata/binary__/client/bin/). Each is independent — failure
        // of one only blanks the corresponding column. Display-name
        // lookups also need the English PALOC to be loaded below, but
        // the bridge-load step can run before that since
        // LookupDisplayName only needs the paloc handle at call time.
        TryBootstrapKeyInfoCatalog(gameRoot, MissionInfoFileName,
            NativeMissionInfoCatalog.LoadFromBytes, ref _missionInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, QuestInfoFileName,
            NativeQuestInfoCatalog.LoadFromBytes, ref _questInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, StageInfoFileName,
            NativeStageInfoCatalog.LoadFromBytes, ref _stageInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, KnowledgeInfoFileName,
            NativeKnowledgeInfoCatalog.LoadFromBytes, ref _knowledgeInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, QuestGaugeInfoFileName,
            NativeQuestGaugeInfoCatalog.LoadFromBytes, ref _questGaugeInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, GimmickInfoFileName,
            NativeGimmickInfoCatalog.LoadFromBytes, ref _gimmickInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, CharacterInfoFileName,
            NativeCharacterInfoCatalog.LoadFromBytes, ref _characterInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, SubLevelInfoFileName,
            NativeSubLevelInfoCatalog.LoadFromBytes, ref _subLevelInfo);
        TryBootstrapSkillInfo(gameRoot);

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

    private void TryBootstrapStringInfo(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        try
        {
            var bytes = _paz.ExtractFile(pamt, ItemInfoDirectory, StringInfoFileName);
            _stringInfo?.Dispose();
            _stringInfo = NativeStringInfoCatalog.LoadFromBytes(bytes);
        }
        catch (CrimsonSaveException)
        {
            // StringInfo missing or malformed — the icon pipeline
            // degrades to "no extraction" but everything else still
            // works.
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Generic loader for the Mission / Quest / Stage / Knowledge / Gauge
    /// catalogs — same group, same directory, same one-file-load shape.
    /// Failures degrade silently: the corresponding TypeName just stops
    /// resolving in <see cref="ResolveViaKeyTable"/> until the next
    /// successful bootstrap.
    /// </summary>
    private void TryBootstrapKeyInfoCatalog<T>(
        string gameRoot,
        string fileName,
        Func<ReadOnlySpan<byte>, T> loader,
        ref T? slot)
        where T : class, IDisposable
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        try
        {
            var bytes = _paz.ExtractFile(pamt, ItemInfoDirectory, fileName);
            slot?.Dispose();
            slot = loader(bytes);
        }
        catch (CrimsonSaveException)
        {
            // File missing or parse failure — degrade gracefully.
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Two-file loader for the skill bridge — needs both
    /// <c>skill.pabgh</c> (index) and <c>skill.pabgb</c> (body). Failure
    /// of either extraction blanks the SkillKey column without affecting
    /// anything else.
    /// </summary>
    private void TryBootstrapSkillInfo(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        try
        {
            var pabgh = _paz.ExtractFile(pamt, ItemInfoDirectory, SkillPabghFileName);
            var pabgb = _paz.ExtractFile(pamt, ItemInfoDirectory, SkillPabgbFileName);
            _skillInfo?.Dispose();
            _skillInfo = NativeSkillInfoCatalog.LoadFromBytes(pabgh, pabgb);
        }
        catch (CrimsonSaveException)
        {
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
    /// <c>StringInfoKey</c> (u32 hash) of an item's primary icon —
    /// the first entry of <c>item_icon_list[0].icon_path</c>. Pair
    /// with <see cref="ResolveStringInfoHash(uint)"/> to get the
    /// underlying texture filename. Returns <c>null</c> when the
    /// iteminfo bridge isn't loaded or the item has no icon.
    /// </summary>
    public uint? GetItemIconPathHash(uint itemKey) =>
        _itemInfo?.LookupIconPathHash(itemKey);

    /// <summary>
    /// Catalog <c>MissionKey</c> (u32) the iteminfo entry's
    /// <c>look_detail_mission_info</c> field points at. Returns
    /// <c>null</c> when the bridge isn't loaded OR the item has no
    /// mission link (the field is 0 — vanilla items).
    /// </summary>
    /// <remarks>
    /// Quest-reward items (the Sealed Abyss Artifact series) point at
    /// the catalog mission key of the challenge that rewards them.
    /// Drives the "Mark Challenge Complete" button's gating predicate
    /// — only enables when an item with this mission link is currently
    /// in the player's inventory.
    /// </remarks>
    public uint? GetItemLookDetailMissionInfo(uint itemKey) =>
        _itemInfo?.LookupLookDetailMissionInfo(itemKey);

    /// <summary>
    /// True when the stringinfo bridge is loaded. Lets the icon-extraction
    /// pipeline gate its action UI cheaply — without this, the only
    /// signal would be a null return from <see cref="ResolveStringInfoHash"/>
    /// for every probe.
    /// </summary>
    public bool HasStringInfo => _stringInfo is not null;

    /// <summary>
    /// Resolve a <c>StringInfoKey</c> hash (u32) to its underlying string
    /// value — most often a texture filename like
    /// <c>cd_icon_arrow_basic.dds</c> referenced from iteminfo's
    /// <c>icon_path</c> field. Returns <c>null</c> when the bridge
    /// isn't loaded or the hash doesn't appear in
    /// <c>stringinfo.pabgb</c>.
    /// </summary>
    public string? ResolveStringInfoHash(uint hash) =>
        _stringInfo?.LookupByHash(hash);

    /// <summary>
    /// Internal ASCII identifier for <paramref name="missionKey"/>
    /// (e.g. <c>Challenge_SealedArtifact_Vehicle_II</c> or
    /// <c>Mission_Intro_Tutorial_I</c>). <c>null</c> when the
    /// missioninfo bridge isn't loaded or the key isn't in the table —
    /// engine-internal negative-encoded keys (<c>0xFFFFxxxx</c>) always
    /// miss because they live outside the catalog namespace.
    /// </summary>
    public string? MissionInfoStringKey(uint missionKey) =>
        _missionInfo?.LookupStringKey(missionKey);

    /// <summary>
    /// Reverse lookup: missioninfo internal name → catalog
    /// <c>MissionKey</c>. Returns <c>null</c> when the bridge isn't
    /// loaded or the name isn't in the table.
    /// </summary>
    /// <remarks>
    /// Driven by the per-row "Mark Challenge Complete" recipe: given a
    /// catalog challenge name (e.g.
    /// <c>Challenge_SealedArtifact_Mastery_Shield_II</c>), the recipe
    /// looks up the corresponding <c>_2</c> follow-up sub-mission key
    /// (<c>Challenge_SealedArtifact_Mastery_Shield_II_2</c>) via this
    /// method to populate the new <c>MissionStateData</c> entry it
    /// creates. The reverse map is built lazily on first call (one
    /// pass over <see cref="NativeMissionInfoCatalog.EntryCount"/>
    /// entries — a few thousand u32→string pairs, ~10 ms one-time)
    /// and cached for subsequent lookups.
    /// </remarks>
    public uint? LookupMissionKeyByInternalName(string internalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(internalName);
        var bridge = _missionInfo;
        if (bridge is null)
        {
            return null;
        }
        var map = _missionNameToKey ??= BuildMissionNameToKeyMap(bridge);
        return map.TryGetValue(internalName, out var k) ? k : null;
    }

    private Dictionary<string, uint>? _missionNameToKey;

    private static Dictionary<string, uint> BuildMissionNameToKeyMap(
        NativeMissionInfoCatalog bridge)
    {
        var n = bridge.EntryCount;
        var map = new Dictionary<string, uint>(n, StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            var entry = bridge.GetEntry(i);
            if (entry is { } e && !string.IsNullOrEmpty(e.Name))
            {
                // First-wins on duplicate names (anchor-scan parser may
                // emit names containing U+FFFD — see the GetEntry
                // caveat in NativeKeyInfoCatalogs).
                map.TryAdd(e.Name, e.Key);
            }
        }
        return map;
    }

    /// <summary>
    /// Walk every iteminfo entry and return the <c>(itemKey, stringKey)</c>
    /// pairs whose <c>stringKey</c> starts with <paramref name="prefix"/>
    /// (ordinal). Empty result when the iteminfo bridge isn't loaded.
    /// O(n) over <see cref="ItemCount"/> — used by the bulk-edit
    /// "drop all Sealed Abyss Artifacts" path to harvest the artifact
    /// itemKey universe in one pass.
    /// </summary>
    public IReadOnlyList<(uint ItemKey, string StringKey)>
        EnumerateItemsByStringKeyPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        var list = new List<(uint, string)>();
        var info = _itemInfo;
        if (info is null)
        {
            return list;
        }
        var count = info.EntryCount;
        for (var i = 0; i < count; i++)
        {
            var entry = info.GetEntry(i);
            if (entry is { } e
                && e.StringKey is { Length: > 0 } sk
                && sk.StartsWith(prefix, StringComparison.Ordinal))
            {
                list.Add((e.ItemKey, sk));
            }
        }
        return list;
    }

    /// <summary>
    /// True when the given save-schema field <c>TypeName</c> has a
    /// name-resolution path — PALOC-backed (item / faction / character /
    /// gimmick), table-driven (mission / quest / stage / knowledge /
    /// gauge / skill), or hardcoded (InventoryKey). Lets callers
    /// cheaply gate UI work without trying a lookup that's bound to
    /// come back empty.
    /// </summary>
    public static bool CanResolveTypeName(string? typeName) =>
        typeName is not null
        && (TypeNameToTypeByte.ContainsKey(typeName)
            || TableDrivenKeyTypes.Contains(typeName)
            || typeName == "InventoryKey");

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
        // Table-driven Key bridges (Mission/Quest/Stage/Knowledge/Gauge/
        // Skill/Gimmick/SubLevel) resolve through their dedicated
        // .pabgb files. Tried first because some of these (e.g.
        // MissionKey) numerically collide with item keys at PALOC 0x70 —
        // routing through the table-driven path avoids leaking the
        // wrong-namespace answer through.
        if (typeName is not null && TableDrivenKeyTypes.Contains(typeName))
        {
            var bridgeResult = FormatPair(ResolveViaKeyTable(typeName, key));
            if (!string.IsNullOrEmpty(bridgeResult))
            {
                return bridgeResult;
            }
            // Bridge didn't cover this value. For GimmickInfoKey /
            // LevelGimmickSceneObjectInfoKey, the legacy PALOC-byte-0x00
            // path may still resolve (scene-object slice). Fall through
            // to the byte-map check below — for TypeNames that aren't
            // in TypeNameToTypeByte (Mission/Quest/Stage/Knowledge/…),
            // the fall-through returns empty, same as before.
        }
        if (typeName is null
            || !TypeNameToTypeByte.TryGetValue(typeName, out var typeByte))
        {
            return string.Empty;
        }
        return FormatPair(ResolveAt(typeByte, key));
    }

    /// <summary>
    /// High-level: resolve a <c>CharacterKey</c> to its best-scoring
    /// NPC portrait DDS path against <paramref name="portraitListBuffer"/>
    /// (the raw NUL-separated buffer from
    /// <see cref="IPazExtractor.ListNpcPortraits"/>). Returns
    /// <c>null</c> when:
    /// <list type="bullet">
    ///   <item>characterinfo.pabgb wasn't loaded (no game install);</item>
    ///   <item>the default-language PALOC wasn't loaded;</item>
    ///   <item>the bridge couldn't match the key to any portrait.</item>
    /// </list>
    /// The English PALOC drives the match because the bridge's
    /// fuzzy scorer needs the English display name to compare against
    /// the English-name-derived portrait filenames Pearl Abyss ships.
    /// </summary>
    public (string Path, int Score)? ResolvePortraitForCharacter(
        uint characterKey, ReadOnlySpan<byte> portraitListBuffer)
    {
        if (_characterInfo is null)
        {
            return null;
        }
        if (!_catalogs.TryGetValue(DefaultLanguage, out var paloc))
        {
            return null;
        }
        return _characterInfo.ResolvePortrait(characterKey, paloc, portraitListBuffer);
    }

    /// <summary>
    /// Resolve a Key value through its dedicated <c>*.pabgb</c> bridge
    /// (Mission / Quest / Stage / Knowledge / Gauge / Skill). Each entry
    /// in <see cref="TableDrivenKeyTypes"/> routes here; the dispatch
    /// picks the right bridge by TypeName.
    ///
    /// <para>Resolution preference, per bridge:</para>
    /// <list type="bullet">
    ///   <item>If the bridge supports the hash-hop chain (Mission /
    ///   Quest / Stage / Knowledge), try <c>LookupDisplayName</c>
    ///   against the loaded PALOC — that's the localized title.</item>
    ///   <item>On miss (or for bridges without a PALOC chain — Gauge
    ///   and Skill), fall back to <c>LookupStringKey</c>, the internal
    ///   ASCII identifier from the <c>.pabgb</c> row.</item>
    ///   <item>If neither lookup hits, the column blanks. Showing
    ///   nothing is better than misattributing a value to the wrong
    ///   namespace (the bug the table-driven path is designed to
    ///   avoid).</item>
    /// </list>
    /// </summary>
    private (string? English, string? Secondary) ResolveViaKeyTable(string typeName, uint key)
    {
        var english = ResolveKeyTableOne(typeName, key, DefaultLanguage);
        var secondary = _secondaryLanguage is null
            ? null
            : ResolveKeyTableOne(typeName, key, _secondaryLanguage);
        return (english, secondary);
    }

    private string? ResolveKeyTableOne(string typeName, uint key, string langCode)
    {
        _catalogs.TryGetValue(langCode, out var paloc);
        return typeName switch
        {
            "MissionKey"    => DisplayOrFallback(_missionInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            "QuestKey"      => DisplayOrFallback(_questInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            "StageKey"      => DisplayOrFallback(_stageInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            "KnowledgeKey"  => DisplayOrFallback(_knowledgeInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            // Gauge + Skill: no PALOC chain. Internal name only, same
            // value across all languages (so secondary-language columns
            // intentionally echo the English one — the alternative is a
            // blank secondary cell next to a populated primary cell,
            // which reads as "missing data").
            "QuestGaugeKey" => _questGaugeInfo?.LookupStringKey(key),
            "SkillKey"      => _skillInfo?.LookupStringKey(key),
            // Gimmick: hash hop at lo32=0x200. Same dispatch shape as
            // Mission/Quest/Stage/Knowledge. If the bridge returns
            // nothing, ResolveByFieldTypeName falls through to the
            // legacy PALOC-byte-0x00 path (the scene-object slice).
            "GimmickInfoKey"                 => DisplayOrFallback(_gimmickInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            "LevelGimmickSceneObjectInfoKey" => DisplayOrFallback(_gimmickInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            // SubLevel: Pattern A only — internal name is the label.
            "SubLevelKey"   => _subLevelInfo?.LookupStringKey(key),
            // Character: lo24 cat-byte strip + PALOC chain at lo32=0x30
            // (NO hash hop unlike Mission/Quest/Stage/Knowledge). Bridge
            // does the strip internally; we pass the raw u32 in.
            "CharacterKey"  => DisplayOrFallback(_characterInfo, key, paloc,
                                                bridge => bridge.LookupDisplayName(key, paloc!),
                                                bridge => bridge.LookupStringKey(key)),
            _               => null,
        };
    }

    /// <summary>
    /// Apply the "display-name preferred, internal-name fallback" rule
    /// generically. <paramref name="paloc"/> may be null (catalog for
    /// the language wasn't loaded); in that case the display-name probe
    /// is skipped and only the internal-name fallback runs.
    /// </summary>
    private static string? DisplayOrFallback<TBridge>(
        TBridge? bridge,
        uint key,
        NativePalocCatalog? paloc,
        Func<TBridge, string?> displayLookup,
        Func<TBridge, string?> internalLookup)
        where TBridge : class
    {
        if (bridge is null)
        {
            return null;
        }
        if (paloc is not null)
        {
            var display = displayLookup(bridge);
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }
        }
        return internalLookup(bridge);
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
        _stringInfo?.Dispose();
        _stringInfo = null;
        _missionInfo?.Dispose();
        _missionInfo = null;
        _questInfo?.Dispose();
        _questInfo = null;
        _stageInfo?.Dispose();
        _stageInfo = null;
        _knowledgeInfo?.Dispose();
        _knowledgeInfo = null;
        _questGaugeInfo?.Dispose();
        _questGaugeInfo = null;
        _skillInfo?.Dispose();
        _skillInfo = null;
        _gimmickInfo?.Dispose();
        _gimmickInfo = null;
        _characterInfo?.Dispose();
        _characterInfo = null;
        _subLevelInfo?.Dispose();
        _subLevelInfo = null;
        foreach (var cat in _catalogs.Values)
        {
            cat.Dispose();
        }
        _catalogs.Clear();
    }
}
