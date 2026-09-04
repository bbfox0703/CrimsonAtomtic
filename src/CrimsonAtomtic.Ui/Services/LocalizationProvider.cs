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

    // Table stems only. Crimson Desert 2.01 renamed the gamedata directory
    // AND every extension (.pabgb/.pabgh -> .staticinfobody/.staticinfoheader)
    // without changing one byte of content, so the concrete directory and
    // filenames come from the layout resolved against the live install at
    // bootstrap (see GameDataLayout) rather than being spelled out here.
    private const string ItemInfoTable = "iteminfo";
    private const string StringInfoTable = "stringinfo";
    // Sibling tables in the same group-0008 gamedata directory.
    private const string MissionInfoTable     = "missioninfo";
    private const string QuestInfoTable       = "questinfo";
    private const string StageInfoTable       = "stageinfo";
    private const string KnowledgeInfoTable   = "knowledgeinfo";
    private const string QuestGaugeInfoTable  = "questgaugeinfo";
    private const string GimmickInfoTable     = "gimmickinfo";
    private const string CharacterInfoTable   = "characterinfo";
    private const string SubLevelInfoTable    = "sublevelinfo";
    private const string SkillTable           = "skill";
    // Three dye gamedata tables, each a body + index pair in the same
    // group 0008 directory. Drive the Dye editor's color-group /
    // material dropdowns + per-prefab slot-count lookup.
    private const string DyeColorGroupTable = "dyecolorgroupinfo";
    private const string DyeTexturePalleteTable = "partprefabdyetexturepalleteinfo";
    private const string DyeSlotInfoTable = "partprefabdyeslotinfo";
    // storeinfo: two-file load (body + index, custom 6-byte shape).
    // Resolves StoreKey -> internal template name; drives the Vendor
    // Buyback dialog.
    private const string StoreInfoTable  = "storeinfo";
    // 14 niche name-only bridges, all in group 0008 next to iteminfo.
    // Order matches the new-session brief table. Four stems drop the
    // "info" suffix (royalsupply, globalgameevent, globalgameeventgroup,
    // reserveslot) - copy literally.
    private const string FactionNodeTable            = "factionnode";
    private const string HouseInfoTable              = "houseinfo";
    private const string RoyalSupplyTable            = "royalsupply";
    private const string CraftToolInfoTable          = "crafttoolinfo";
    private const string CraftToolGroupInfoTable     = "crafttoolgroupinfo";
    private const string TriggerRegionInfoTable      = "triggerregioninfo";
    private const string GamePlayVariableInfoTable   = "gameplayvariableinfo";
    private const string GlobalGameEventTable        = "globalgameevent";
    private const string GlobalGameEventGroupTable   = "globalgameeventgroup";
    private const string GameAdviceInfoTable         = "gameadviceinfo";
    private const string GameAdviceGroupInfoTable    = "gameadvicegroupinfo";
    private const string ReserveSlotTable            = "reserveslot";
    private const string RegionInfoTable             = "regioninfo";
    private const string ItemGroupInfoTable          = "itemgroupinfo";

    /// <summary>
    /// Which archive naming the live install ships. Resolved once per
    /// bootstrap; defaults to the newest so a probe against a missing
    /// install still has a well-defined answer (every caller bails on the
    /// absent PAMT before it matters).
    /// </summary>
    private GameDataLayout _layout = GameDataLayout.Modern;

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

    /// <summary>For each discovered language code, the group plus the
    /// archive directory and filename(s) holding it. One file through 2.00;
    /// 2.01 split each language into one file per namespace inside a
    /// per-language directory. Populated by Bootstrap.</summary>
    private readonly Dictionary<string, (string Group, string Directory, IReadOnlyList<string> Files)> _languageSources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loaded catalogs, keyed by language code. Owns them.</summary>
    private readonly Dictionary<string, IPalocCatalog> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The same catalogs kept as their concrete parts, NOT owned (the
    /// entry in <see cref="_catalogs"/> owns them). The
    /// <c>*_lookup_display_name</c> bridges each take one PALOC <i>native
    /// handle</i>, so a language split across files — every language from
    /// 2.01 on — has to be offered to them one part at a time. See
    /// <see cref="FirstDisplayName"/>.
    /// </summary>
    private readonly Dictionary<string, NativePalocCatalog[]> _palocParts =
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
        // Dye color group: bridge is already loaded for the Dye editor's
        // dropdown; routing it here lights up the resolved-name column
        // for ItemDyeSaveData._dyeColorGroupInfoKey (and any future
        // fields typed the same way).
        "DyeColorGroupInfoKey",
        // StoreKey: storeinfo.pabgb internal template name
        // ("Store_Her_General", "Store_BlackMarket", …). Lights up
        // StoreDataSaveData._storeKey rows in the resolved-name column +
        // labels the Vendor Buyback dialog's per-store header.
        "StoreKey",
        // StringInfoKey scalars: pre-computed Jenkins hashes. The
        // already-loaded stringinfo bridge reverses them in one hop —
        // covers UseItemReserveSlotElementSaveData._specialNameKey,
        // FactionNodeSubInnerEnableElementSaveData._levelNameKey, etc.
        // (Dynamic-array StringInfoKey fields like _usedTagList stay on
        // their original raw-array display path; only scalar fields
        // route through here.)
        "StringInfoKey",
        // 13 niche name-only bridges (impl_name_only_bridge!) — each
        // resolves the save-side key to the row's internal template name
        // ("DefaultHouse_Lv1", "Region_Pywel", "ItemGroup_Category_Equipment", …).
        // No PALOC chain on any of these; secondary-language column
        // intentionally mirrors English (matches QuestGauge / Skill convention).
        "HouseKey",                  // 4 rows
        "RoyalSupplyKey",            // 4 rows
        "CraftToolKey",              // 17 rows
        "CraftToolGroupKey",         // 10 rows
        "TriggerRegionKey",          // 12 rows
        "GamePlayVariableKey",       // 47 rows
        "GlobalGameEventInfoKey",    // 103 rows
        "GlobalGameEventGroupKey",   // 7 rows
        "GameAdviceInfoKey",         // 461 rows; PALOC chain deferred
        "GameAdviceGroupKey",        // 8 rows
        "ReserveSlotKey",            // 27 rows; PALOC chain deferred
        "RegionKey",                 // 1,004 rows
        "ItemGroupKey",              // 1,500 rows
        "FactionNodeKey",            // 1,158 rows — faction-stronghold node names
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
    private NativeDyeColorGroupInfoCatalog? _dyeColorGroupInfo;
    private NativePartPrefabDyeTexturePalleteCatalog? _dyeTexturePalleteInfo;
    private NativePartPrefabDyeSlotInfoCatalog? _dyeSlotInfo;
    private NativeItemPartPrefabCatalog? _itemPartPrefab;
    private NativeStoreInfoCatalog? _storeInfo;
    // 13 niche name-only bridges, group 0008 (impl_name_only_bridge!).
    private NativeFactionNodeInfoCatalog? _factionNodeInfo;
    private NativeHouseInfoCatalog? _houseInfo;
    private NativeRoyalSupplyInfoCatalog? _royalSupplyInfo;
    private NativeCraftToolInfoCatalog? _craftToolInfo;
    private NativeCraftToolGroupInfoCatalog? _craftToolGroupInfo;
    private NativeTriggerRegionInfoCatalog? _triggerRegionInfo;
    private NativeGamePlayVariableInfoCatalog? _gamePlayVariableInfo;
    private NativeGlobalGameEventInfoCatalog? _globalGameEventInfo;
    private NativeGlobalGameEventGroupInfoCatalog? _globalGameEventGroupInfo;
    private NativeGameAdviceInfoCatalog? _gameAdviceInfo;
    private NativeGameAdviceGroupInfoCatalog? _gameAdviceGroupInfo;
    private NativeReserveSlotInfoCatalog? _reserveSlotInfo;
    private NativeRegionInfoCatalog? _regionInfo;
    private NativeItemGroupInfoCatalog? _itemGroupInfo;
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
    /// <returns>
    /// The provider that was displaced (the previous <see cref="Icons"/>),
    /// or <c>null</c> when nothing changed. The caller owns disposing it —
    /// it must NOT be disposed until any UI bound to its cached Bitmaps has
    /// rebuilt against the new provider, or live Image elements would
    /// reference a disposed Bitmap.
    /// </returns>
    public IconProvider? ConfigureIconProvider(string rootDirectory)
    {
        var previous = Icons;
        Icons = new IconProvider(rootDirectory);
        return ReferenceEquals(previous, Icons) ? null : previous;
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
    /// <returns>
    /// The displaced portrait provider (previous <see cref="Portraits"/>),
    /// or <c>null</c> when nothing changed. The caller owns disposing it,
    /// and must defer that until no UI is bound to its cached Bitmaps.
    /// </returns>
    public PortraitProvider? ConfigurePortraitProvider(string cacheRootDirectory)
    {
        var previous = Portraits;
        Portraits = new PortraitProvider(cacheRootDirectory, _paz, this, _gameRoot);
        return ReferenceEquals(previous, Portraits) ? null : previous;
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
    /// Parsed <c>meta/0.paver</c> for the bootstrapped install — the
    /// authoritative game-data version. <c>null</c> when no install
    /// root was detected OR the paver file couldn't be read (very
    /// rare; would indicate an unusual install layout). Use
    /// <see cref="GameDataVersion.IsCompatibleWithParser"/> to decide
    /// whether to warn the user before save / iteminfo operations.
    /// </summary>
    public GameDataVersion? GameDataVersion { get; private set; }

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

        // ── Read meta/0.paver FIRST so the caller (App startup) can
        // surface a warning dialog if the install's game-data version
        // doesn't match what the parser targets. We still proceed with
        // bootstrap regardless — the iteminfo / save-body steps are
        // wrapped in CrimsonSaveException catches so a mismatched parse
        // degrades gracefully rather than crashing the whole app.
        GameDataVersion = NativePaverReader.TryReadFromInstall(gameRoot);

        // ── Resolve which archive naming this install ships BEFORE any
        // extraction. 2.01 renamed the gamedata directory and every file
        // extension without touching the contents, so every bridge below
        // keys off this one probe.
        _layout = GameDataLayout.Resolve(
            _paz, Path.Combine(gameRoot, GameDataLayout.GameDataGroup, "0.pamt"));

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
        TryBootstrapKeyInfoCatalog(gameRoot, MissionInfoTable,
            NativeMissionInfoCatalog.LoadFromBytes, ref _missionInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, QuestInfoTable,
            NativeQuestInfoCatalog.LoadFromBytes, ref _questInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, StageInfoTable,
            NativeStageInfoCatalog.LoadFromBytes, ref _stageInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, KnowledgeInfoTable,
            NativeKnowledgeInfoCatalog.LoadFromBytes, ref _knowledgeInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, QuestGaugeInfoTable,
            NativeQuestGaugeInfoCatalog.LoadFromBytes, ref _questGaugeInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, GimmickInfoTable,
            NativeGimmickInfoCatalog.LoadFromBytes, ref _gimmickInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, CharacterInfoTable,
            NativeCharacterInfoCatalog.LoadFromBytes, ref _characterInfo);
        TryBootstrapKeyInfoCatalog(gameRoot, SubLevelInfoTable,
            NativeSubLevelInfoCatalog.LoadFromBytes, ref _subLevelInfo);
        TryBootstrapSkillInfo(gameRoot);
        TryBootstrapDyeGamedata(gameRoot);
        TryBootstrapStoreInfo(gameRoot);
        TryBootstrapNicheBridges(gameRoot);

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
            var bytes = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(ItemInfoTable));
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
            var bytes = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(StringInfoTable));
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
        string tableStem,
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
            var bytes = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(tableStem));
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
            var pabgh = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Header(SkillTable));
            var pabgb = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(SkillTable));
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

    /// <summary>
    /// Load the three dye gamedata tables. Each is a separate
    /// <c>.pabgb</c> + <c>.pabgh</c> pair in the same group 0008
    /// directory as the other table-driven catalogs. Failures
    /// degrade per-table — losing color-group resolution doesn't
    /// blank material resolution and vice-versa.
    /// </summary>
    private void TryBootstrapDyeGamedata(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        TryLoadDyeBridge(pamt, DyeColorGroupTable,
            NativeDyeColorGroupInfoCatalog.LoadFromBytes, ref _dyeColorGroupInfo);
        TryLoadDyeBridge(pamt, DyeTexturePalleteTable,
            NativePartPrefabDyeTexturePalleteCatalog.LoadFromBytes,
            ref _dyeTexturePalleteInfo);
        TryLoadDyeBridge(pamt, DyeSlotInfoTable,
            NativePartPrefabDyeSlotInfoCatalog.LoadFromBytes, ref _dyeSlotInfo);
        TryLoadItemPartPrefabBridge(pamt);
    }

    /// <summary>
    /// Bootstrap the <c>ItemKey → PartPrefabKey[]</c> join (iteminfo +
    /// stringinfo + partprefabdyeslotinfo). The 4 bytes buffers needed
    /// for the join are re-extracted from the same PAMT — they aren't
    /// kept alive after the individual bridge constructors already
    /// consumed them, so this is a one-time-per-bootstrap re-read.
    /// Failure degrades silently: <see cref="ItemPartPrefab"/> stays
    /// <c>null</c> and the Dye editor's slot-count column shows
    /// "unknown".
    /// </summary>
    private void TryLoadItemPartPrefabBridge(string pamt)
    {
        try
        {
            var iteminfo = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(ItemInfoTable));
            var stringinfo = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(StringInfoTable));
            var pabgb = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(DyeSlotInfoTable));
            var pabgh = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Header(DyeSlotInfoTable));
            _itemPartPrefab?.Dispose();
            _itemPartPrefab = NativeItemPartPrefabCatalog.LoadFromBytes(
                iteminfo, stringinfo, pabgb, pabgh);
        }
        catch (CrimsonSaveException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Generic two-file loader for the dye bridges — extracts the
    /// <c>.pabgb</c> + <c>.pabgh</c> pair and hands them to the
    /// per-bridge factory. Failures degrade silently per-bridge.
    /// </summary>
    private void TryLoadDyeBridge<T>(
        string pamt, string tableStem,
        Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>, T> loader,
        ref T? slot)
        where T : class, IDisposable
    {
        try
        {
            var pabgb = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(tableStem));
            var pabgh = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Header(tableStem));
            slot?.Dispose();
            slot = loader(pabgb, pabgh);
        }
        catch (CrimsonSaveException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Load the <c>storeinfo</c> two-file pair (.pabgb body + .pabgh
    /// index) from group 0008. Drives StoreKey resolution in the
    /// resolved-name column + the Vendor Buyback dialog. Failures
    /// degrade silently — the editor still works, just without
    /// store-name resolution.
    /// </summary>
    private void TryBootstrapStoreInfo(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        try
        {
            var pabgb = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(StoreInfoTable));
            var pabgh = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Header(StoreInfoTable));
            _storeInfo?.Dispose();
            _storeInfo = NativeStoreInfoCatalog.LoadFromBytes(pabgb, pabgh);
        }
        catch (CrimsonSaveException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Load all 13 name-only niche bridges in one pass. Each one is the
    /// same two-file shape (<c>.pabgb</c> body + <c>.pabgh</c> index, both
    /// in group <c>0008</c>'s <c>gamedata/binary__/client/bin/</c>) and
    /// each is independent — failure of one only blanks the corresponding
    /// resolved-name column. We open the group-0008 PAMT once and route
    /// every extraction through <see cref="TryLoadNicheBridge{T}"/>.
    /// </summary>
    private void TryBootstrapNicheBridges(string gameRoot)
    {
        var pamt = Path.Combine(gameRoot, "0008", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        TryLoadNicheBridge(pamt, FactionNodeTable,
            NativeFactionNodeInfoCatalog.LoadFromBytes, ref _factionNodeInfo);
        TryLoadNicheBridge(pamt, HouseInfoTable,
            NativeHouseInfoCatalog.LoadFromBytes, ref _houseInfo);
        TryLoadNicheBridge(pamt, RoyalSupplyTable,
            NativeRoyalSupplyInfoCatalog.LoadFromBytes, ref _royalSupplyInfo);
        TryLoadNicheBridge(pamt, CraftToolInfoTable,
            NativeCraftToolInfoCatalog.LoadFromBytes, ref _craftToolInfo);
        TryLoadNicheBridge(pamt, CraftToolGroupInfoTable,
            NativeCraftToolGroupInfoCatalog.LoadFromBytes, ref _craftToolGroupInfo);
        TryLoadNicheBridge(pamt, TriggerRegionInfoTable,
            NativeTriggerRegionInfoCatalog.LoadFromBytes, ref _triggerRegionInfo);
        TryLoadNicheBridge(pamt, GamePlayVariableInfoTable,
            NativeGamePlayVariableInfoCatalog.LoadFromBytes, ref _gamePlayVariableInfo);
        TryLoadNicheBridge(pamt, GlobalGameEventTable,
            NativeGlobalGameEventInfoCatalog.LoadFromBytes, ref _globalGameEventInfo);
        TryLoadNicheBridge(pamt, GlobalGameEventGroupTable,
            NativeGlobalGameEventGroupInfoCatalog.LoadFromBytes, ref _globalGameEventGroupInfo);
        TryLoadNicheBridge(pamt, GameAdviceInfoTable,
            NativeGameAdviceInfoCatalog.LoadFromBytes, ref _gameAdviceInfo);
        TryLoadNicheBridge(pamt, GameAdviceGroupInfoTable,
            NativeGameAdviceGroupInfoCatalog.LoadFromBytes, ref _gameAdviceGroupInfo);
        TryLoadNicheBridge(pamt, ReserveSlotTable,
            NativeReserveSlotInfoCatalog.LoadFromBytes, ref _reserveSlotInfo);
        TryLoadNicheBridge(pamt, RegionInfoTable,
            NativeRegionInfoCatalog.LoadFromBytes, ref _regionInfo);
        TryLoadNicheBridge(pamt, ItemGroupInfoTable,
            NativeItemGroupInfoCatalog.LoadFromBytes, ref _itemGroupInfo);
    }

    /// <summary>
    /// Generic two-file loader shared by every niche bridge — extracts
    /// the <c>.pabgb</c> + <c>.pabgh</c> pair from the group-0008 PAMT
    /// and hands them to the per-bridge factory. Modelled on
    /// <see cref="TryLoadDyeBridge"/>; failures degrade silently
    /// per-bridge (a missing file just blanks the corresponding column).
    /// </summary>
    private void TryLoadNicheBridge<T>(
        string pamt, string tableStem,
        Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>, T> loader,
        ref T? slot)
        where T : class, IDisposable
    {
        try
        {
            var pabgb = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Body(tableStem));
            var pabgh = _paz.ExtractFile(pamt, _layout.BinDirectory, _layout.Header(tableStem));
            slot?.Dispose();
            slot = loader(pabgb, pabgh);
        }
        catch (CrimsonSaveException) { }
        catch (IOException) { }
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
                // Probe by listing rather than extracting: the resolver
                // reports whichever layout this install ships (one blob per
                // language through 2.00, one file per namespace from 2.01)
                // and returns null when the language isn't in this group.
                // We deliberately do NOT read the bytes here — caching all
                // 14 language catalogs eagerly would consume ~350 MB; they
                // are extracted lazily on first request in TryLoadCatalog.
                if (GameDataLayout.ResolvePalocFiles(_paz, pamt, code) is { } found)
                {
                    _languageSources[code] = (group, found.Directory, found.Files);
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
            var parts = new NativePalocCatalog[src.Files.Count];
            var built = 0;
            try
            {
                for (; built < parts.Length; built++)
                {
                    var bytes = _paz.ExtractFile(pamt, src.Directory, src.Files[built]);
                    parts[built] = NativePalocCatalog.LoadFromBytes(bytes);
                }
            }
            catch
            {
                // Partial load: nothing is registered yet, so the handles
                // opened so far would leak. Close them before rethrowing
                // into the per-language degrade-gracefully catches below.
                for (var i = 0; i < built; i++)
                {
                    parts[i].Dispose();
                }
                throw;
            }
            // A single-file language is its own catalog; a split one is
            // queried part by part. Either way _catalogs owns the result.
            IPalocCatalog cat = parts.Length == 1 ? parts[0] : new MultiPalocCatalog(parts);
            _catalogs[langCode] = cat;
            _palocParts[langCode] = parts;
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
    private static Dictionary<(byte, uint), string> BuildNameMap(IPalocCatalog cat)
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
    /// Number of entries in the loaded <c>characterinfo.pabgb</c>.
    /// <c>0</c> when the bridge isn't loaded.
    /// </summary>
    public int CharacterCount => _characterInfo?.EntryCount ?? 0;

    /// <summary>
    /// Get the <c>(CharacterKey, InternalName)</c> pair at insertion
    /// index <paramref name="index"/>. Returns <c>null</c> when the
    /// bridge isn't loaded or <paramref name="index"/> is out of range.
    /// Drives the Browse Characters dialog. The key is the lo24 row
    /// key; save-side <c>_characterKey</c> values with a cat-byte
    /// still need the strip before they'll match.
    /// </summary>
    public (uint CharacterKey, string InternalName)? GetCharacter(int index) =>
        _characterInfo?.GetEntry(index);

    /// <summary>
    /// Number of entries in the loaded <c>knowledgeinfo.pabgb</c>.
    /// <c>0</c> when the bridge isn't loaded.
    /// </summary>
    public int KnowledgeCount => _knowledgeInfo?.EntryCount ?? 0;

    /// <summary>
    /// Get the <c>(KnowledgeKey, InternalName)</c> pair at insertion
    /// index <paramref name="index"/>. Returns <c>null</c> when the
    /// bridge isn't loaded or <paramref name="index"/> is out of range.
    /// Drives the Abyss-Gate bulk-unlock scan.
    /// </summary>
    public (uint KnowledgeKey, string InternalName)? GetKnowledge(int index) =>
        _knowledgeInfo?.GetEntry(index);

    /// <summary>
    /// Number of entries in the loaded <c>gimmickinfo.pabgb</c>.
    /// <c>0</c> when the bridge isn't loaded.
    /// </summary>
    public int GimmickCount => _gimmickInfo?.EntryCount ?? 0;

    // ── Dye gamedata bridges ────────────────────────────────────────────────
    //
    // Exposed for the Dye editor: color-group dropdown (10 named
    // groups) + material dropdown (11 palette tiers × 2-3 sub-records)
    // + (future) per-prefab slot-count lookup. All three are loaded
    // alongside the other catalogs in TryBootstrapDyeGamedata.

    /// <summary>
    /// True iff all three dye gamedata bridges are loaded. False
    /// degrades the Dye editor to a "raw key" mode (no resolved
    /// names in dropdowns).
    /// </summary>
    public bool HasDyeGamedata =>
        _dyeColorGroupInfo is not null
        && _dyeTexturePalleteInfo is not null
        && _dyeSlotInfo is not null;

    /// <summary>
    /// Direct access to <c>dyecolorgroupinfo</c>. <c>null</c> when the
    /// bridge isn't loaded.
    /// </summary>
    public NativeDyeColorGroupInfoCatalog? DyeColorGroupInfo => _dyeColorGroupInfo;

    /// <summary>
    /// Direct access to <c>storeinfo</c>. <c>null</c> when the bridge
    /// isn't loaded (no game install configured, or storeinfo.pabgb
    /// missing from the install). Consumed by the Vendor Buyback
    /// dialog to enumerate distinct stores + label rows.
    /// </summary>
    public NativeStoreInfoCatalog? StoreInfo => _storeInfo;

    /// <summary>
    /// Direct access to <c>partprefabdyetexturepalleteinfo</c>.
    /// <c>null</c> when the bridge isn't loaded.
    /// </summary>
    public NativePartPrefabDyeTexturePalleteCatalog? DyeTexturePalleteInfo =>
        _dyeTexturePalleteInfo;

    /// <summary>
    /// Direct access to <c>partprefabdyeslotinfo</c>. <c>null</c> when
    /// the bridge isn't loaded. Consumed transitively through
    /// <see cref="LookupDyeSlotCount"/>; the catalog accessor stays
    /// public for diagnostics + per-prefab default-material reads.
    /// </summary>
    public NativePartPrefabDyeSlotInfoCatalog? DyeSlotInfo => _dyeSlotInfo;

    /// <summary>
    /// Direct access to the <c>ItemKey → PartPrefabKey[]</c> join.
    /// <c>null</c> when any of the four backing tables (iteminfo,
    /// stringinfo, partprefabdyeslotinfo .pabgb/.pabgh) failed to load.
    /// </summary>
    public NativeItemPartPrefabCatalog? ItemPartPrefab => _itemPartPrefab;

    /// <summary>
    /// One-shot "how many dye slots does this item have?". Returns
    /// <c>null</c> when either backing catalog is unavailable or the
    /// resolver can't pin a slot count (mesh-variant items without a
    /// partprefab entry — see
    /// <see cref="DyeSlotCountSource.NotResolvedNoPartPrefab"/>).
    /// Drives the Dye editor's per-row slot-count column + the Add Dye
    /// slot-picker.
    /// </summary>
    public int? LookupDyeSlotCount(uint itemKey)
    {
        if (_itemPartPrefab is null || _dyeSlotInfo is null) return null;
        var (count, source) = _itemPartPrefab.ResolveDyeSlotCount(itemKey, _dyeSlotInfo);
        return source == DyeSlotCountSource.Direct ? count : null;
    }

    /// <summary>
    /// Describe a dye slot's gamedata material layers as a short display
    /// hint. Returns the primary-layer default material and, for 1.13's
    /// expanded dyeable gear (cloaks / shields / quivers / skullknight
    /// set), the second (extra) layer's material — e.g.
    /// <c>"leather + cloth"</c>. Shows only the primary when the slot has
    /// no extra layer (<c>"leather"</c>). Returns <c>null</c> when the
    /// item's partprefab join or the slot-info bridge is unavailable, or
    /// when neither layer names a material.
    /// </summary>
    public string? DescribeDyeSlotLayers(uint itemKey, int slotIdx)
    {
        if (_itemPartPrefab is null || _dyeSlotInfo is null || slotIdx < 0) return null;
        try
        {
            if (_itemPartPrefab.LookupFirstPrefabKey(itemKey) is not { } prefabKey)
            {
                return null;
            }
            var primary = _dyeSlotInfo.LookupSlotDefaultMaterial(prefabKey, slotIdx, 0);
            string? extra = null;
            if ((_dyeSlotInfo.LookupSlotExtraLayerCount(prefabKey, slotIdx) ?? 0) >= 1)
            {
                extra = _dyeSlotInfo.LookupSlotExtraLayerMaterial(prefabKey, slotIdx, 0, 0);
            }
            var hasPrimary = !string.IsNullOrEmpty(primary);
            var hasExtra = !string.IsNullOrEmpty(extra);
            if (hasPrimary && hasExtra) return $"{primary} + {extra}";
            if (hasPrimary) return primary;
            if (hasExtra) return extra;
            return null;
        }
        catch (CrimsonSaveException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the <c>(GimmickInfoKey, InternalName)</c> pair at insertion
    /// index <paramref name="index"/>. Returns <c>null</c> when the
    /// bridge isn't loaded or <paramref name="index"/> is out of range.
    /// </summary>
    public (uint GimmickInfoKey, string InternalName)? GetGimmick(int index) =>
        _gimmickInfo?.GetEntry(index);

    /// <summary>
    /// Enumerate every loaded gimmick entry whose internal name
    /// contains <b>any</b> of <paramref name="substrings"/>
    /// (case-insensitive). Returns an empty sequence when the bridge
    /// isn't loaded. Substring match (not prefix) — gimmick internal
    /// names are descriptive (e.g. <c>gimmick_abyssone_bridge_gate_01</c>)
    /// so substring catches more than prefix would.
    /// </summary>
    public IEnumerable<(uint GimmickInfoKey, string InternalName)>
        EnumerateGimmicksByNameContains(params string[] substrings)
    {
        if (_gimmickInfo is null || substrings is null || substrings.Length == 0)
        {
            yield break;
        }
        var count = _gimmickInfo.EntryCount;
        for (var i = 0; i < count; i++)
        {
            var entry = _gimmickInfo.GetEntry(i);
            if (entry is not { } e || string.IsNullOrEmpty(e.Name))
            {
                continue;
            }
            foreach (var s in substrings)
            {
                if (e.Name.Contains(s, StringComparison.OrdinalIgnoreCase))
                {
                    yield return e;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Enumerate every loaded knowledge entry whose internal name
    /// matches at least one of <paramref name="namePrefixes"/>
    /// (case-insensitive ordinal). Returns an empty sequence when
    /// the bridge isn't loaded. Used by the Abyss-Gate bulk-unlock
    /// flow to harvest every <c>AbyssGate_*</c> /
    /// <c>Knowledge_AbyssRuins_HyperSpace_*</c> /
    /// <c>Knowledge_LevelGimmickIcon_AbyssGate*</c> key from
    /// <c>knowledgeinfo.pabgb</c> without vendoring a JSON pack.
    /// </summary>
    public IEnumerable<(uint KnowledgeKey, string InternalName)>
        EnumerateKnowledgeByNamePrefix(params string[] namePrefixes)
    {
        if (_knowledgeInfo is null || namePrefixes is null || namePrefixes.Length == 0)
        {
            yield break;
        }
        var count = _knowledgeInfo.EntryCount;
        for (var i = 0; i < count; i++)
        {
            var entry = _knowledgeInfo.GetEntry(i);
            if (entry is not { } e || string.IsNullOrEmpty(e.Name))
            {
                continue;
            }
            foreach (var prefix in namePrefixes)
            {
                if (e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return e;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Look up the localized display name for a <c>CharacterKey</c>
    /// against the language whose PALOC is loaded under
    /// <paramref name="langCode"/>. Returns <c>null</c> when:
    /// <list type="bullet">
    ///   <item>characterinfo.pabgb wasn't loaded;</item>
    ///   <item>the language's PALOC isn't loaded;</item>
    ///   <item>the key has no PALOC entry at <c>lo32 = 0x30</c>.</item>
    /// </list>
    /// </summary>
    public string? LookupCharacterDisplayName(uint characterKey, string langCode)
    {
        if (_characterInfo is null) return null;
        var bridge = _characterInfo;
        return FirstDisplayName(langCode, paloc => bridge.LookupDisplayName(characterKey, paloc));
    }

    /// <summary>
    /// Internal name (string_key) for a <c>CharacterKey</c> from
    /// <c>characterinfo.pabgb</c> — e.g. <c>"Damian"</c>,
    /// <c>"Riding_Horse_Tiuta_Unique_2050_kliff"</c>,
    /// <c>"NHM_Unique_Shane_OneHandBow_410"</c>. Useful as a fallback
    /// label when the display PALOC misses, and as a category signal
    /// (the Riding_*/Animal_*/NHM_*/NOM_* prefixes correspond to the
    /// in-game mount / vehicle / wild-animal / NPC taxonomy).
    ///
    /// Returns <c>null</c> when the characterinfo bridge isn't loaded
    /// or the key isn't in the catalog.
    /// </summary>
    public string? LookupCharacterInternalName(uint characterKey) =>
        _characterInfo?.LookupStringKey(characterKey);

    /// <summary>
    /// Mount / vehicle / animal template-name prefixes that the editor
    /// treats as "player-controlled" when the strict
    /// <see cref="ItemRecordFlags.IsPlayerOwned"/> flag misses (i.e.
    /// the enclosing mercenary has <c>_ownedCharacterKey</c> absent).
    /// Pearl Abyss uses these consistently across 1.05–1.07; sanity-
    /// check against a fresh save after a new patch.
    /// </summary>
    /// <remarks>
    /// Source: <c>vendor/crimson-rs/docs/dye-editor-scope.md</c>
    /// §"C# editor — IS_PLAYER_OWNED widening recipe".
    /// </remarks>
    private static readonly string[] PlayerMountNamePrefixes =
    {
        "Riding_",   // Tiuta horses, balloons, wagons, …
        "Animal_",   // tamed wild animals (Black Horse, Stefano, …)
        "Vehicle_",  // generic vehicle templates
    };

    /// <summary>
    /// Decide whether an <see cref="ItemRecord"/> from
    /// <see cref="ISaveLoader.ListAllItems"/> should be exposed in the
    /// editor's equipment-related UI tabs (dye / gem socket / item
    /// edit / search).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fast path: the strict
    /// <see cref="ItemRecordFlags.IsPlayerOwned"/> flag covers the
    /// common case (619 / 829 records on the slot103 baseline).
    /// </para>
    /// <para>
    /// Slow path: for mercenary kinds without the flag set, resolve
    /// the owner's template name via <see cref="LookupCharacterInternalName"/>
    /// and admit any whose name starts with
    /// <c>Riding_*</c> / <c>Animal_*</c> / <c>Vehicle_*</c>. This
    /// widens the slot103 acceptance from 619 to 627 records (the
    /// 3 Tiuta_kliff equip items + 5 Stefano equip items). NPC
    /// mercenaries still get rejected because their template names
    /// start with <c>NHM_*</c> / <c>NHW_*</c> / <c>NDM_*</c>.
    /// </para>
    /// <para>
    /// The C ABI does NOT promote
    /// <see cref="ItemRecordFlags.IsPlayerOwned"/> automatically for
    /// these mounts because doing so would couple the all-items hot
    /// path to a characterinfo.pabgb load on every refresh — keeping
    /// the widening on the C# side preserves the enumerator as a
    /// gamedata-free, no-allocation operation.
    /// </para>
    /// </remarks>
    public bool IsPlayerEditableItem(ItemRecord record)
    {
        if (record.IsPlayerOwned)
        {
            return true;
        }
        // Slow path only matters for mercenary kinds — active kinds
        // always carry the strict flag.
        if (record.Container != ContainerKind.MercenaryEquip &&
            record.Container != ContainerKind.MercenaryInventory)
        {
            return false;
        }
        var name = LookupCharacterInternalName(record.OwnerCharacterKey);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        foreach (var prefix in PlayerMountNamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Friendly source label for an <see cref="ItemRecord"/> — drives
    /// the "Bag" / "Equipped" / "Mount: …" column in the item-list
    /// editors (sockets / dye / future search). Single source of
    /// truth so both editors render consistently.
    /// </summary>
    /// <remarks>
    /// Inventory rows resolve through
    /// <see cref="ResolveByFieldTypeName"/> against the record's
    /// <see cref="ItemRecord.InventoryKey"/> (the actual
    /// <c>_inventoryKey</c> value, NOT the list position); mercenary
    /// rows resolve the owner via <see cref="LookupCharacterInternalName"/>.
    /// </remarks>
    public string FormatItemSourceLabel(ItemRecord record)
    {
        switch (record.Container)
        {
            case ContainerKind.Inventory:
                var bagLabel = ResolveByFieldTypeName("InventoryKey", record.InventoryKey);
                return string.IsNullOrEmpty(bagLabel)
                    ? $"InventoryKey {record.InventoryKey}"
                    : bagLabel;
            case ContainerKind.ActiveEquip:
                return "Equipped";
            case ContainerKind.ActiveUseReserve:
                return "Quick-Use Reserve";
            case ContainerKind.MercenaryEquip:
            case ContainerKind.MercenaryInventory:
                var ownerName = LookupCharacterInternalName(record.OwnerCharacterKey)
                                ?? $"CharacterKey {record.OwnerCharacterKey}";
                var suffix = record.Container == ContainerKind.MercenaryEquip
                    ? "Equipped"
                    : "Inventory";
                return record.OwnerIsMainMercenary
                    ? $"{ownerName} (Main, {suffix})"
                    : $"{ownerName} ({suffix})";
            default:
                return $"ContainerKind {(uint)record.Container}";
        }
    }

    /// <summary>
    /// Game-defined max_stack_count for an item. Returns <c>null</c>
    /// when the iteminfo bridge isn't loaded or the key isn't known.
    /// Drives the "Set to max stack" UX in the edit panel.
    /// </summary>
    public ulong? GetItemMaxStackCount(uint itemKey) =>
        _itemInfo?.LookupMaxStackCount(itemKey);

    /// <summary>
    /// One-shot static-metadata snapshot for <paramref name="itemKey"/>.
    /// Returns <c>null</c> when iteminfo isn't loaded yet or the key
    /// isn't in the table. The detail pane in FindItemsWindow drives
    /// off this — pair with <see cref="LookupItemName"/> + the existing
    /// stack / icon / socket helpers to render a complete item card.
    /// </summary>
    public ItemInfoSummary? LookupItemInfoSummary(uint itemKey) =>
        _itemInfo?.LookupSummary(itemKey);

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
        if (!_palocParts.TryGetValue(DefaultLanguage, out var parts))
        {
            return null;
        }
        // Best score across the language's parts. Pre-2.01 that is a
        // single part, so this reduces to the old single call.
        (string Path, int Score)? best = null;
        foreach (var part in parts)
        {
            var hit = _characterInfo.ResolvePortrait(characterKey, part, portraitListBuffer);
            if (hit is { } h && (best is null || h.Score > best.Value.Score))
            {
                best = h;
            }
        }
        return best;
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
        _palocParts.TryGetValue(langCode, out var paloc);
        return typeName switch
        {
            "MissionKey"    => DisplayOrFallback(_missionInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            "QuestKey"      => DisplayOrFallback(_questInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            "StageKey"      => DisplayOrFallback(_stageInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            "KnowledgeKey"  => DisplayOrFallback(_knowledgeInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
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
            "GimmickInfoKey"                 => DisplayOrFallback(_gimmickInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            "LevelGimmickSceneObjectInfoKey" => DisplayOrFallback(_gimmickInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            // SubLevel: Pattern A only — internal name is the label.
            "SubLevelKey"   => _subLevelInfo?.LookupStringKey(key),
            // Character: lo24 cat-byte strip + PALOC chain at lo32=0x30
            // (NO hash hop unlike Mission/Quest/Stage/Knowledge). Bridge
            // does the strip internally; we pass the raw u32 in.
            "CharacterKey"  => DisplayOrFallback(_characterInfo, paloc,
                                                (bridge, part) => bridge.LookupDisplayName(key, part),
                                                bridge => bridge.LookupStringKey(key)),
            // Store: internal name only — no PALOC chain yet for stores.
            // Same convention as QuestGauge / Skill (secondary language
            // intentionally echoes English).
            "StoreKey"      => _storeInfo?.LookupStringKey(key),
            // Dye color group: internal name only. Same across all
            // languages, so the secondary column intentionally mirrors
            // the primary (matches QuestGauge / Skill convention).
            "DyeColorGroupInfoKey" => _dyeColorGroupInfo?.LookupName(key),
            // StringInfoKey scalar: u32 Jenkins hash → reversed via the
            // already-loaded stringinfo bridge. Internal name only —
            // stringinfo doesn't ship localized titles.
            "StringInfoKey" => _stringInfo?.LookupByHash(key),
            // 13 niche bridges — internal name only (no PALOC chain).
            // Same convention as Store / QuestGauge / Skill: secondary
            // language intentionally echoes the English column.
            "FactionNodeKey"          => _factionNodeInfo?.LookupStringKey(key),
            "HouseKey"                => _houseInfo?.LookupStringKey(key),
            "RoyalSupplyKey"          => _royalSupplyInfo?.LookupStringKey(key),
            "CraftToolKey"            => _craftToolInfo?.LookupStringKey(key),
            "CraftToolGroupKey"       => _craftToolGroupInfo?.LookupStringKey(key),
            "TriggerRegionKey"        => _triggerRegionInfo?.LookupStringKey(key),
            "GamePlayVariableKey"     => _gamePlayVariableInfo?.LookupStringKey(key),
            "GlobalGameEventInfoKey"  => _globalGameEventInfo?.LookupStringKey(key),
            "GlobalGameEventGroupKey" => _globalGameEventGroupInfo?.LookupStringKey(key),
            "GameAdviceInfoKey"       => _gameAdviceInfo?.LookupStringKey(key),
            "GameAdviceGroupKey"      => _gameAdviceGroupInfo?.LookupStringKey(key),
            "ReserveSlotKey"          => _reserveSlotInfo?.LookupStringKey(key),
            "RegionKey"               => _regionInfo?.LookupStringKey(key),
            "ItemGroupKey"            => _itemGroupInfo?.LookupStringKey(key),
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
        NativePalocCatalog[]? palocParts,
        Func<TBridge, NativePalocCatalog, string?> displayLookup,
        Func<TBridge, string?> internalLookup)
        where TBridge : class
    {
        if (bridge is null)
        {
            return null;
        }
        if (palocParts is not null)
        {
            foreach (var part in palocParts)
            {
                var display = displayLookup(bridge, part);
                if (!string.IsNullOrEmpty(display))
                {
                    return display;
                }
            }
        }
        return internalLookup(bridge);
    }

    /// <summary>
    /// Run <paramref name="lookup"/> against each PALOC part of
    /// <paramref name="langCode"/> in load order and return the first
    /// non-empty answer.
    ///
    /// <para>
    /// The <c>*_lookup_display_name</c> C ABI bridges take a single PALOC
    /// native handle, so a language the install splits across files (2.01
    /// onwards) cannot be handed over as one object. Namespaces don't
    /// overlap, so at most one part answers; pre-2.01 there is only one
    /// part and this is the old single call.
    /// </para>
    /// </summary>
    private string? FirstDisplayName(string langCode, Func<NativePalocCatalog, string?> lookup)
    {
        if (!_palocParts.TryGetValue(langCode, out var parts))
        {
            return null;
        }
        foreach (var part in parts)
        {
            var hit = lookup(part);
            if (!string.IsNullOrEmpty(hit))
            {
                return hit;
            }
        }
        return null;
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
        _dyeColorGroupInfo?.Dispose();
        _dyeColorGroupInfo = null;
        _dyeTexturePalleteInfo?.Dispose();
        _dyeTexturePalleteInfo = null;
        _dyeSlotInfo?.Dispose();
        _dyeSlotInfo = null;
        _itemPartPrefab?.Dispose();
        _itemPartPrefab = null;
        _storeInfo?.Dispose();
        _storeInfo = null;
        // 13 niche bridges.
        _factionNodeInfo?.Dispose(); _factionNodeInfo = null;
        _houseInfo?.Dispose(); _houseInfo = null;
        _royalSupplyInfo?.Dispose(); _royalSupplyInfo = null;
        _craftToolInfo?.Dispose(); _craftToolInfo = null;
        _craftToolGroupInfo?.Dispose(); _craftToolGroupInfo = null;
        _triggerRegionInfo?.Dispose(); _triggerRegionInfo = null;
        _gamePlayVariableInfo?.Dispose(); _gamePlayVariableInfo = null;
        _globalGameEventInfo?.Dispose(); _globalGameEventInfo = null;
        _globalGameEventGroupInfo?.Dispose(); _globalGameEventGroupInfo = null;
        _gameAdviceInfo?.Dispose(); _gameAdviceInfo = null;
        _gameAdviceGroupInfo?.Dispose(); _gameAdviceGroupInfo = null;
        _reserveSlotInfo?.Dispose(); _reserveSlotInfo = null;
        _regionInfo?.Dispose(); _regionInfo = null;
        _itemGroupInfo?.Dispose(); _itemGroupInfo = null;
        _palocParts.Clear();
        foreach (var cat in _catalogs.Values)
        {
            cat.Dispose();
        }
        _catalogs.Clear();
        // Release the decoded icon / portrait Bitmap caches (native Skia
        // memory). Safe to dispose eagerly here: Dispose runs at app exit
        // when the UI is already torn down, so no Image is still bound to
        // a cached Bitmap.
        Icons.Dispose();
        Portraits.Dispose();
    }
}
