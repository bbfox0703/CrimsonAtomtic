# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-05-14.
>
> **🆕 All 9 save-editor key resolvers shipped (vendor dev = `cea8300`).**
> Two upstream sessions delivered the full set of `crimson_<table>_*` C
> ABI bridges this week:
>
> 1. Six initial bridges: `mission_info`, `quest_info`, `stage_info`,
>    `knowledge_info`, `quest_gauge_info`, `skill_info` + the
>    `crimson_calculate_checksum` helper.
> 2. Two follow-up bridges + Issue #1/#2 fixes: `gimmick_info` (hash
>    hop at `lo32=0x200`) and `sub_level_info` (Pattern A only). The
>    `gimmick_info` bridge is co-resident with the legacy PALOC-byte-
>    0x00 path on the editor side — `ResolveByFieldTypeName` consults
>    the table-driven bridge first and falls back to the byte map
>    when the bridge doesn't cover the value.
>
> **Issue #1 (mojibake) — fully resolved.** Upstream tightened the
> anchor-scan parsers (strict `std::str::from_utf8` + `name.contains('_')`
> + first-wins dedup). Probe survey after refresh: **0 of 4,059
> missioninfo / 49,508 stageinfo / 6,046 knowledgeinfo entries** carry
> U+FFFD bytes (was 791/4,939 + 39/48,019 + 0/5,483).
>
> **Issue #2 (coverage gap) — exceeded projection.** Upstream widened
> the hi-byte cap from `==0` to `<0x80`, picking up the
> `Shop_*` (0x41) / `HerStore_*` (0x05) / `Knowledge_Recipe_*` (0x7F)
> categories that had been wrongly rejected. Save-side coverage moved:
>
> | Bridge | Before | After |
> |---|---:|---:|
> | StageKey  | 97.3% | **99.7%** (156 unresolved, all hi=0x00 procedurals) |
> | KnowledgeKey | 87.7% | **100.0%** (beat the predicted ~hi=0x7F slice) |
> | MissionKey | 70.1% | 70.1% (1,299 unresolveds are 0xFF save-internal sentinels — no game-data fix possible, editor's empty-Name handling is correct) |
>
> **IGN match rate (Phase A challenge catalog)**: 76.6% → **90.8%** as
> a knock-on benefit — the ~20 Mastery missing-tier challenges
> (Sword 2/6, Bow 1/4/5, Spear 2, Cannon 3, etc.) that vanished into
> the mojibake fallout came back cleanly. Residual 13 unmatched are
> all Combat/Life challenges that live in a different game-data file
> (not in `missioninfo.pabgb` under any prefix).
>
> Bedrock verified end-to-end: `MissionKey 1000083 → "Where the Wind
> Guides You"`, `KnowledgeKey 1002588 → "Demenissian Ruins"`,
> `StageKey 1100170020 → "Shop_Hernand_General"` (new this refresh).
>
> **Health**: 95/95 C# tests (was 93; +2 in `KeyInfoCatalogsTests` for
> gimmick_info + sub_level_info), AOT publish 24.1 MB exe + 1.4 MB
> Rust DLL (was 1.3 MB), trim-clean. `_proto_probe_keyinfo.py` deleted
> — both issues closed.
>
> **Icon-extraction pipeline is complete (Phases 1–3).** Tools →
> Extract Icons from Game Data… walks every item, resolves each
> `icon_path` hash through stringinfo, PAZ-extracts the DDS from
> `0012/ui/texture/icon/`, decodes BC1/BC3 + resizes + WebP-encodes,
> and writes `<cache>/<ItemKey>.webp`. One run ≈ 3 min, ~6,200 icons,
> ~5 MB on disk. Built on crimson-rs PRs #24 (stringinfo bridge),
> #25 (partial-PAZ unblock), #26 (icon_path_hash getter).
>
> **UI polish landed**: collapsible Save Summary panel (« / » toggle
> in the header), Tools → Font Size submenu (10–20 pt presets,
> persisted + clamped), DataGrid cells + status footer follow the
> font-size pick. Quiet `SetSecondaryLanguage` settings-clobber bug
> fixed at the same time (`new AppSettings { … }` → `existing with { … }`).
>
> **Batch scalar mutation landed** (crimson-rs
> [PR #27](https://github.com/bbfox0703/crimson-rs/pull/27) +
> C# end). One FFI round trip applies N scalar mutations sharing a
> single post-batch re-decode — the "Fill stacks" 168-op path now
> pays O(N + block_count) instead of O(N · block_count) (~1 s in
> place of ~5 s on the 1112-block save). All-or-nothing semantics
> on validation failure; `CrimsonSaveException.FailedOpIndex`
> pinpoints the offending op. Public surface:
> `ISaveLoader.SetScalarFieldsBatch(IReadOnlyList<ScalarBatchOp>)`.
>
> **Next session targets**: pick from the lower-priority deferred list
> below — none of them are blocked. `skill_info` C ABI bridge (#4) and
> the `MissionKey`/`QuestKey` name resolution (#6) are now the most
> user-visible follow-ons — both have concrete recipes in
> [`docs/crimsonforge-coverage-gaps.md`](crimsonforge-coverage-gaps.md).
> PAR-container partial compression for `.pam`/`.pamlod`/`.pac` meshes
> in 0009/0015 stays out of scope until mesh extraction is on the
> roadmap.

## Where we are

### `vendor/crimson-rs` (Rust core)

- **Save format**: fully decoded against live 1.06 saves (slot0–slot105).
  - `Save::parse` (header + crypto + LZ4) — done
  - `Body::parse` (schema + TOC) — done
  - `Body::decode_blocks` (per-object field decoder, all 8 `meta_kind` values) — done
  - **0 undecoded bytes** across all 8 tested slots (new + old, sizes from
    1112 to 1136 blocks). Every byte either decoded into a typed field or
    captured as `ObjectBlock.trailing_pad`.
  - Hard test invariants: `total_undecoded == 0` and
    `total_decoded_fields == total_present_fields`. A future game-patch
    drift will fail loudly, not silently.
- **C ABI**: `--features c_abi` exposes `crimson_save_*` extern "C"
  alongside the existing PyO3 entry point (same cdylib). Surface:
  - `crimson_save_load_from_file` (handle-based, parses + decodes once)
  - Scalar getters: version, flags, hmac_ok, payload_size,
    uncompressed_size, schema_type_count, toc_entry_count, block_count
  - `crimson_save_get_block_info` → flat `CrimsonBlockInfo` per block
  - `crimson_save_get_block_class_name` → variable-length UTF-8 via the
    standard two-call (query size → fill buffer) pattern
  - `crimson_save_get_block_json` → full per-field decode of one block
    as a JSON document (same two-call pattern). Hand-rolled JSON
    formatter, no serde dep. Each field also carries `child`
    (object_locator inline child) and `elements` (object_list);
    nested blocks themselves carry `class_name`, so a C# consumer can
    drill in recursively without further FFI getters. `value` is
    pre-formatted to mirror
    `tools/inspect/inspect_save_section.py --pretty`.
  - `crimson_save_set_scalar_field(handle, block, field, bytes, len)` →
    in-place byte patch over `fixed_prefix` / `fixed_suffix` fields of a
    top-level TOC block. Validated by byte length. Errors: `NOT_SCALAR (-12)`,
    `LENGTH_MISMATCH (-13)`, `OUT_OF_RANGE (-10)`. Still exported for
    backward compat; C# now reaches this surface only through the path
    variant (empty path).
  - `crimson_save_set_scalar_field_path(handle, block, path[], path_len, field, bytes, len)`
    → path-addressed scalar mutation. Each `CrimsonPathStep { field_idx, element_idx }`
    descends one level: through a Locator's inline child (element_idx ignored)
    or through ObjectList element `element_idx`. With `path_len == 0`,
    semantics match the top-level setter. New error `NOT_NAVIGABLE (-15)`
    fires when a mid-path step lands on anything other than a locator-with-child
    or list. Re-decodes all blocks on success so the next `get_block_json`
    reflects the new value.
  - `crimson_save_write_to_file(handle, path)` → re-serializes the
    save with the original nonce (HMAC + ChaCha20 + LZ4 rebuilt
    against the modified body) and writes to disk. Error:
    `WRITE_FAILED (-14)`.
  - `crimson_save_free`
  - **PALOC C ABI** (`crimson_paloc_*`): `load_from_file` /
    `load_from_bytes` / `free` / `entry_count` / `lookup` (two-call) /
    `get_entry` (two-call). Handle owns a copied
    `HashMap<String,String>` + insertion-order `Vec` for stable
    enumeration. New error code `NOT_FOUND = -16`. Parser hardening:
    `LocalizationFile::parse` now sanity-checks the trailing
    `entry_count` against body bytes (each entry ≥ 16 bytes), so the
    raw wrapped `gamedata/*.paloc` files don't crash the loader with
    a 300 GB alloc.
  - **PAZ extraction C ABI** (`crimson_paz_extract_file`): one-shot
    stateless helper. Inputs `(pamt_path, directory, file_name)`,
    output: extracted bytes via the standard two-call pattern.
    Wraps `binary::paz::extract_file` (ChaCha20 + LZ4 + manifest
    lookup). Errors map to `NOT_FOUND`, `IO`, `BODY_PARSE`,
    `NULL_ARG`, `INVALID_PATH`. **Partial-compression entries
    (`raw_compression == 1`) are supported** via
    `binary::paz::decompress_partial`, which tries three
    sub-formats in order: (a) identity when `c == u`; (b) 128-byte
    verbatim header + LZ4 over the rest with the header as a prefix
    dictionary; (c) DDS per-mip layout — up to 11 u32 slots at
    DDS-reserved offset 0x20 giving each mip's on-disk size, with
    `0` meaning "remaining mips are stored raw, sequentially". The
    per-mip variant is a Rust port of NattKh's CrimsonForge
    `_decompress_type1_dds_per_mip_sizes` (see
    `D:\Github\crimsonforge\core\compression_engine.py`). DX10 BC1..BC7
    + R10G10B10A2 / R16F / R32F / R8 + DXT1/3/5 + ATI1/2 + plain
    RGB(A)/LUMINANCE pixel formats are all recognised for mip-size
    math. Out of scope: the PAR-container layout used by `.pam` /
    `.pamlod` / `.pac` meshes in 0009/0015 (per-section LZ4 blocks
    indexed by an 8-slot table at offset 0x10; recipe in
    CrimsonForge `_decompress_type1_par`). Those still return
    `BODY_PARSE`.
  - **iteminfo bridge C ABI** (`crimson_iteminfo_*`): `load_from_file`
    / `load_from_bytes` / `free` / `entry_count` /
    `lookup_string_key(u32)` (two-call) / `get_entry(idx, *out_key, …)`
    (two-call). Handle owns a `HashMap<u32, String>` built from the
    existing `item_info` parser — only `(key, string_key)` retained;
    the other 100+ fields dropped. ~200 KB resident for 1.06's
    ~6,400 items.
  - **stringinfo bridge C ABI** (`crimson_string_info_*`):
    `load_from_file` / `load_from_bytes` / `free` / `entry_count` /
    `lookup_by_hash(u32)` (two-call) / `get_entry(idx, *out_hash, …)`
    (two-call). Handle owns a `HashMap<u32, String>` built from the
    new `string_info` parser. 30,206 entries in 1.06; ~900 KB
    resident. Pairs with the existing `stringinfo.pabgh` index for
    byte-identical Rust-side round-trip; the C ABI consumes only the
    pabgb side because every entry is self-describing
    (`[u32 hash][u32 zero][u8 flag][u32 slen][N bytes utf-8]`).
  - **iteminfo icon_path getter** (`crimson_iteminfo_lookup_icon_path_hash`):
    returns the `StringInfoKey` (u32) of `item_icon_list[0].icon_path`
    for a given `ItemKey`. `NOT_FOUND` when the item has no icon
    entry or the entry's hash is 0 (the explicit "no icon"
    sentinel). One extra ~50 KB `HashMap<u32, u32>` on the existing
    handle — total iteminfo memory still under 1 MB.
  - **Batch scalar mutation C ABI** (`crimson_save_set_scalar_fields_batch`):
    one FFI round trip, one re-decode for N writes. Validate-all →
    patch-all → single `decode_blocks`. All-or-nothing on
    validation failure; optional `out_failed_op_index` writes
    `usize::MAX` on success, failing op index on error.
    `CrimsonScalarBatchOp` repr(C) struct mirrors
    `crimson_save_set_scalar_field_path` args. Per-op rules
    factored into private `resolve_leaf_range` helper, shared
    with the single-op setters.
  - 91 tests pass (60 base + 31 c_abi tests across save, paloc,
    paz, iteminfo, string_info — including 3 new batch tests:
    smoke, atomicity, 200-op equivalence vs N × single-op).
- **Latest main**: `244e0cd`. PRs landed across recent sessions:
  #14, #15, #16, #17, #18 (path-addressed scalar mutation),
  #19 (PALOC C ABI), #20 (one-shot PAZ extraction),
  #21 (iteminfo bridge), #24 (stringinfo bridge — icon pipeline P1),
  #25 (partial-PAZ unblock), #26 (iteminfo icon_path getter — P3),
  #27 (batch scalar mutation).
- **Branch model**: dev → PR → main (rebase merge, linear history).
  After merge, local + origin/dev get force-reset to match main.

### `CrimsonAtomtic` (this repo)

- **Foundation** (CLAUDE.md, docs/, .gitignore, vendor/, scripts/) — done.
- **C# / Avalonia scaffolding** — done. Builds clean, 51/51 unit tests pass:
  ```
  src/CrimsonAtomtic.Core         IPlatformPaths, ISingleInstanceGuard
  src/CrimsonAtomtic.SaveModel    SaveSummary, BlockSummary,
                                  BlockDetails, DecodedFieldRow +
                                  AOT JsonSerializerContext +
                                  ScalarFieldEditing static helper
                                  (parse `"123 <u32>"` ↔ raw + tag;
                                  TryEncode for bool / u8..u64 /
                                  i8..i64 / f32 / f64 to LE bytes).
  src/CrimsonAtomtic.Ui/Platform  WindowsPlatformPaths
                                  (resolves %LOCALAPPDATA%-based paths)
  src/CrimsonAtomtic.RustInterop  Four thin wrapper sets over the C ABI:
                                  - ISaveLoader / NativeSaveLoader —
                                    Load / LoadBlockDetails /
                                    SetScalarField (path-aware) /
                                    WriteToFile. SafeHandle, IDisposable,
                                    cached live handle. PathStep is the
                                    public FFI-layout descent struct.
                                  - IPalocCatalog / NativePalocCatalog —
                                    LoadFromFile or LoadFromBytes,
                                    EntryCount, Lookup(key) → string?,
                                    GetEntry(idx) → (key, value)?.
                                  - IPazExtractor / NativePazExtractor —
                                    stateless ExtractFile(pamt, dir,
                                    name) → byte[].
                                  - IItemInfoCatalog / NativeItemInfoCatalog —
                                    LoadFromBytes (preferred),
                                    EntryCount,
                                    LookupStringKey(uint id) → string?,
                                    GetEntry(idx) → (key, stringKey)?.
                                  - IStringInfoCatalog / NativeStringInfoCatalog —
                                    LoadFromBytes (preferred),
                                    EntryCount,
                                    LookupByHash(uint hash) → string?,
                                    GetEntry(idx) → (hash, value)?.
                                  + SaveLoaderScalarExtensions: typed
                                  SetScalarBool / SetScalarUInt32 /
                                  SetScalarSingle / … wrappers — each
                                  has a no-path and a path-aware
                                  overload that share the same byte
                                  encoding.
  src/CrimsonAtomtic.Ui           Avalonia 12 / .NET 10, PublishAot=true,
                                  Mutex single-instance, MainWindow with
                                  File menu (Open / Save / Save As /
                                  Exit) + Tools menu (Browse
                                  Localization…) + blocks DataGrid +
                                  lazy field-detail pane (GridSplitter)
                                  + fields filter + breadcrumb-based
                                  drill-down + inline scalar-edit
                                  panel under the fields DataGrid +
                                  dirty title indicator + status
                                  footer reporting localization load
                                  state. All DataGrids have resizable
                                  columns. FieldRowViewModel wraps
                                  each DecodedFieldRow; IsEditable
                                  gates on scalar kind + supported
                                  type tag. Each wrapper carries the
                                  descent path to its enclosing block
                                  so deep mutations are addressable.
                                  src/CrimsonAtomtic.Ui/Services/
                                  LocalizationProvider owns the
                                  iteminfo bridge + per-language PALOC
                                  catalogs. Bootstrap discovers every
                                  localizationstring_*.paloc the game
                                  ships (groups 0019..0050, 14 known
                                  codes), eagerly loads English,
                                  lazily loads any secondary language
                                  on SecondaryLanguage = "...".
                                  ResolveItemName(uint id) pipes
                                  id → string_key → text through both
                                  PALOC layers. AppSettings persists
                                  the user's secondary-language pick
                                  to settings.json. Tools menu has a
                                  Secondary Language picker
                                  (auto-populated from
                                  AvailableLanguages) with a
                                  check-mark on the active choice;
                                  Tools → Browse Localization… opens
                                  the separate
                                  LocalizationSearchWindow.
  src/CrimsonAtomtic.Tests        xUnit v3 — 53 tests (live-save
                                  walks count once; parameterised
                                  tests count once per InlineData):
                                  16 NativeSaveLoaderTests + 25
                                  ScalarFieldEditingTests (as before) +
                                  3 PalocCatalogTests + 4
                                  PazExtractorTests +
                                  3 ItemInfoCatalogTests +
                                  1 LocalizationTypeByteDiscoveryTests
                                  (live-install: walks every PALOC
                                  entry, prints a type-byte histogram
                                  and the type byte each known key
                                  sample resolves at — kept around
                                  as the cheapest sanity check after a
                                  game patch). Live tests skip cleanly
                                  when the game install / save isn't
                                  present.
  ```
  The UI reads **real saves** via `crimson_rs.dll` and now writes
  them too. Open Save defaults to `%LOCALAPPDATA%\Pearl Abyss\CD\save\<user>\`
  when there's a single user; clicking a row in the blocks DataGrid
  lazily loads its per-field decode via `LoadBlockDetails` →
  `crimson_save_get_block_json` → System.Text.Json (source-generated,
  AOT-safe). Drill-down composes recursively
  (`InventorySaveData › inventorylist[18] › [14]:
  InventoryElementSaveData › itemList[40] › …`).
  Selecting a scalar field at **any nav depth** reveals an inline
  edit panel below the fields DataGrid; Apply (or Enter) validates
  via `ScalarFieldEditing.TryEncode`, pushes the bytes through the
  path-addressed `SetScalarField(blockIdx, path, fieldIdx, bytes)`,
  re-fetches the top-level block, walks every nav frame down its
  stored path to rebase the entire chain, and stamps fresh display
  values onto each existing `FieldRowViewModel` so the DataGrid keeps
  its scroll / selection state. The window title gains a leading `*`
  while there are uncommitted edits; Save (Ctrl-style menu) writes
  back to the open path, Save As… re-anchors to a fresh path.
- **AOT publish verified**: `dotnet publish -c Release -r win-x64
  -p:PublishAot=true` (csproj pins `TrimMode=full` and now suppresses
  the two roll-up codes `IL2104` + `IL3053` against Avalonia DataGrid
  12.0.0; `microsoft.dotnet.ilcompiler` 10.0.7 started promoting those
  to errors under our repo-wide `TreatWarningsAsErrors=true`, so the
  csproj's `<NoWarn>` scopes the exception to where it belongs).
  Produces a working bundle in `dist/win-x64/`: CrimsonAtomtic.exe
  21.7 MB, crimson_rs.dll 1.3 MB, plus Avalonia native deps and PDBs.
- **Python tools** (unchanged, working): `tools/extract/extract_save.py`,
  `tools/inspect/inspect_save_body.py`, `tools/inspect/inspect_save_section.py`.
- **`tools/analyze/dump_save_fields.py`** (new): flattens a `.save` into
  one-row-per-field JSONL (`save / top_class / class / path / field / type /
  prim / kind / value`). One slot0 = ~273k rows / 64 MB, ~10 s. Feeds
  duckdb/pandas correlation work for unknown-Key RE — e.g. "do any
  `MissionKey` values appear at a different namespace's row?" Object lists
  + locators are recursed; `dynamic_array` is opt-in via
  `--include-array-elements`. See [tools/analyze/README.md](../tools/analyze/README.md).
- **`tools/analyze/dump_catalogs.py`** (new): pulls iteminfo + per-language
  PALOC out of the game install into JSONL — the lookup tables
  `dump_save_fields.py` JOINs against. PALOC rows carry derived `key`
  (upper 32 bits) and `type_byte` (lower 8 bits) columns so
  `WHERE type_byte = 0x70` queries are direct. Sample smoke run on 1.06:
  6,253 items + 179,513 English PALOC entries in ~15 s. **stringinfo is
  NOT yet covered** — needs an upstream PR adding
  `parse_string_info_from_bytes` to `vendor/crimson-rs/src/python.rs`
  (mirror of `parse_iteminfo_from_bytes`). Cross-resolve sanity check
  against slot0 confirmed status.md's RE notes (MissionKey 1003440 →
  iteminfo `Braised_Meat_Fish_XLarge` + PALOC 0x70 "Hearty Braised Meat
  and Fish"; StageKey resolves mostly at PALOC 0x00) and surfaced one
  new finding: **every QuestGaugeKey value in slot0 (311/311, 100%)
  exists as an `ItemKey` in iteminfo** — gauges look like they live in
  a strict subset of the item-key numeric range.
- **Challenge catalog + IGN enrichment** (this session, Phase A).
  `tools/analyze/dump_challenges.py` walks `missioninfo.pabgb` via the
  new `NativeMissionInfoCatalog.GetEntry(idx)` enumeration, filters
  `Challenge_*` / `Mission_MiniGame_*` prefixes, applies title
  normalisation (Roman→Arabic), strips PALOC's
  `{StaticInfo:Knowledge:…#Display}` template wrapper, and cross-refs
  the result against (a) the current save's `MissionStateData._state` /
  `_completedTime` and (b) the IGN page text (parsed via
  `--ign-text`). Slot0 result: **1,720 challenges** in catalog,
  **141 IGN rows parsed**, **108 matched (76.6%)**, **261
  player-completed**, **1,273 in-progress**, **184 not-yet-encountered**.
  IGN data adds user-recognisable subcategories like "Goyen's Advice -
  Sword" and per-row goal+reward — the Browse Challenges UI (Phase B,
  deferred) will display these as extra columns. **One-click complete
  is blocked on PR B (length-changing edits)**: every single completed
  challenge in slot0 (261/261) has both `_state=5` AND `_completedTime`
  present, so flipping state without promoting the absent
  `_completedTime` would likely desync the game's read-side checks.
  The 33 unmatched IGN entries decompose into ~14 "missing tier"
  rows (Issue #1 mojibake victims that will reappear after upstream
  fix), ~17 Combat/Life challenges that live outside `missioninfo.pabgb`
  entirely (potential new bridge target), and 2 title-only collisions.
- **`ElementRowViewModel` lazy-resolved hot properties** (this session,
  Phase C). `ResolvedName` and `NestedMatchHaystack` are now computed
  on first access and cached, not in the constructor. Constructing
  46,541 `ElementRowViewModel` instances for QuestSaveData's
  `_stageStateData` previously did 46k native-bridge FFI calls back-
  to-back at load time, which was the dominant cost when drilling
  into that list. With lazy + virtualised DataGrid rendering, the
  cost amortises over user scroll instead of concentrating at first-
  click. The bookkeeping is one sentinel field per property and three
  carried instance fields (`_localization`, `_keyField`, `_key`); no
  INPC plumbing needed because the resolved values are stable once
  computed (a save edit or language switch already rebuilds the row
  list fresh).
- **Six new key-resolver bridges wired into `LocalizationProvider`**
  (this session). Upstream session shipped C ABI for
  `mission_info` / `quest_info` / `stage_info` / `knowledge_info` /
  `quest_gauge_info` / `skill_info` plus a `crimson_calculate_checksum`
  helper (`crimson_<table>_lookup_string_key` + `lookup_display_name`
  surfaces). C# side: 16 new `[LibraryImport]` declarations in
  [`NativeSaveLoader.cs`](../src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs),
  six small wrapper classes in
  [`NativeKeyInfoCatalogs.cs`](../src/CrimsonAtomtic.RustInterop/NativeKeyInfoCatalogs.cs)
  (Mission/Quest/Stage/Knowledge expose `LookupStringKey` +
  `LookupDisplayName(paloc, lo32)`; QuestGauge/Skill expose
  `LookupStringKey` only — gauges + skills aren't on the PALOC hash-hop
  chain in this model). `CrimsonPalocHandle` got an `internal
  NativeHandle` accessor so the four hash-hop bridges can drive PALOC
  in one FFI call. **Dispatch**: `LocalizationProvider` now runs a
  `TableDrivenKeyTypes` check ahead of the PALOC-type-byte map; the
  six new TypeNames route through `ResolveViaKeyTable` which prefers
  `LookupDisplayName` and falls back to the internal name. Crucially
  this **avoids the wrong-namespace pitfall** where MissionKey 1003440
  would have resolved to "Hearty Braised Meat and Fish" via PALOC
  0x70 — now it resolves cleanly to the actual mission title (or
  nothing) via missioninfo's hash hop. `ElementRowViewModel.IsNameKey`
  whitelist expanded so per-element DataGrid views (mission list,
  knowledge list, etc.) auto-pick the Key field for the Name column.
  7 new `KeyInfoCatalogsTests` (live-install gated) pin the bedrock
  cases — `MissionKey 1000083 → "Where the Wind Guides You"`,
  `MissionKey 1000157 → "Unfamiliar Lands"`, `KnowledgeKey 1002588 →
  "Demenissian Ruins"`. Total: 86 → 93 tests, all passing. AOT
  publish unchanged (24.1 MB).
- **`tools/analyze/extract_keycases.py`** (new): the hand-off shape for
  the separate crimson_rs RE session — for every distinct `(type,
  value)` of a `*Key` schema TypeName in a save, emit a self-contained
  row with `total_occurrences`, up to N example `(path, block_class,
  top_class, siblings)` records, and `resolves = {iteminfo_id, paloc[5
  type bytes]}`. **`siblings` = every other scalar in the same block** —
  that's the context (`_state`, `_completedTime`, `_alertType`, etc.)
  that tells the receiving session what the Key actually labels.
  Compound fields stubbed as `"<object_list, N elements>"`. Smoke run on
  4 Quest-family Key types: 7,108 distinct (type, value) cases / 4 MB.
  Surfaced two leads worth following: (a) `MissionKey
  4294964206/4207/4208 = 0xFFFFF3EE/EF/F0` — sequential, in completed
  MissionStateData blocks, no catalog hit anywhere → likely
  negative-encoded internal mission IDs (`-3090..-3088` if i32),
  candidate for `missioninfo.pabgb` negative-keyed entries; (b)
  KnowledgeKey 3/4/7 hit PALOC `0x93 knowledge_category` ("Endless
  Adventures and Stories", …) while larger KnowledgeKey values don't —
  confirms the small-vs-large KnowledgeKey split status.md previously
  guessed at.
- **Vendor**: `vendor/crimson-rs` at `3eff882`. Refresh via
  `.\vendor\update_vendors.ps1`.

## Build entry point

`build.cmd` (cmd.exe) / `build.ps1` (PowerShell 7+) at the repo root —
mirrors the UE5CEDumper command shape so the user reuses muscle
memory. Delegates to the per-step scripts under `scripts\` so the
old workflow still works.

```
build                 Release, all targets (Rust DLL + dotnet build)
build debug           Debug, all targets
build publish         AOT publish to dist\win-x64\ (same output as the old
                      scripts\package_aot.ps1)
build test            Build + run xUnit suite
build dll             Rust DLL only
build ui              C# UI only
build clean           Wipe dist\ + bin\ + obj\ before building (combinable)
build publish clean   Clean + AOT publish
```

`build.cmd` prefers `pwsh.exe` and warns if only Windows PowerShell 5.1
is on PATH (the script `#requires -Version 7`).

## How `crimson_rs.dll` flows into the C# build

1. `.\scripts\build_rust.ps1` runs `cargo build --release --features c_abi`
   in `vendor/crimson-rs/` → produces
   `vendor/crimson-rs/target/release/crimson_rs.dll`.
2. Both `CrimsonAtomtic.Ui.csproj` and `CrimsonAtomtic.Tests.csproj` have a
   `<Content Include="..\..\vendor\crimson-rs\target\release\crimson_rs.dll"
   CopyToOutputDirectory="PreserveNewest" Link="crimson_rs.dll">` item, so
   `dotnet build`, `dotnet test`, and `dotnet publish` all stage the dll
   next to the exe / test runner.
3. `.\scripts\package_aot.ps1` glues steps 1 + dotnet publish + summary into
   one command. `-SkipRustBuild` for C#-only iteration.

## Pick up here (next concrete task)

The icon-extraction pipeline (item #1 below) and the batch
`SetScalarField` C ABI (item #3) are **both complete** — kept
in this doc as historical context + a reference for the
data-flow, file layout, and per-phase decisions. **The actual
next tasks live in the deferred-items list (#2, #4–#14)**; pick
whichever matches your appetite. `skill_info` bridge (#4) and
`Mission/Quest` name resolution (#6) are now the most
user-visible follow-ons.

### #1 — Icon extraction pipeline (DONE — kept as reference)

The display half ships an external icon directory
(`AppSettings.IconCacheDirectory`) of `<ItemKey>.webp` files,
lazy-loaded via `IconProvider` + `ItemKeyToIconConverter`, surfaced
in Item Picker + the elements DataGrid. **The populator half** —
Tools → Extract Icons from Game Data… — walks every iteminfo entry
and writes the cache in ~3 minutes against a Crimson Desert install.

**End-to-end data flow** (built across Phases 1–3):

```
ItemKey                   from save schema (already have)
   ↓ iteminfo.pabgb     ✓ parsed in crimson-rs
icon_path (StringInfoKey, u32)
   ↓ stringinfo.pabgb  ✓ Phase 1 — NativeStringInfoCatalog +
                           LocalizationProvider.ResolveStringInfoHash
"cd_icon_xxx.dds"           (resolved filename, confirmed shape)
   ↓ PAZ 0012/ui/texture/icon/  ⚠ partial-compression — blocks
                                  Phase 3 until crimson-rs adds it
raw .dds bytes              (BC1/BC3 compressed texture, mip 0 only)
   ↓ DDS decoder          ✓ Phase 2 — IconImageEncoder (hand-rolled
                              BC1 + BC3, no NuGet)
RGBA bitmap (32×32 to 256×256)
   ↓ SkiaSharp resize     ✓ Phase 2 — SKImage.ScalePixels
64×64 RGBA bitmap
   ↓ SKImage.Encode(Webp) ✓ Phase 2 — SkiaSharp 3.119
~few-KB .webp file
   ↓ File.WriteAllBytes
<cache>/<ItemKey>.webp
```

**Phased plan** (each phase shippable on its own; 3-5 sessions total):

1. ~~**Phase 1 — `stringinfo.pabgb` parser + C ABI**~~ ✅ **Landed in
   crimson-rs [#24](https://github.com/bbfox0703/crimson-rs/pull/24)
   and this repo.** What shipped:
   - `string_info/` Rust module: pabgb parser (linear walk, 30,206
     entries in 1.06) + pabgh index for byte-identical round-trip;
     entry shape `[u32 hash][u32 zero][u8 flag][u32 slen][N utf-8]`.
     The reserved zero+flag bytes are always 0 in 1.06 — round-
     tripped against a future patch promoting them.
   - `crimson_string_info_*` C ABI: load_from_file / load_from_bytes
     / free / entry_count / lookup_by_hash / get_entry. Same shape
     as iteminfo / paloc bridges (two-call buffer pattern,
     `NOT_FOUND` for missing hashes).
   - `IStringInfoCatalog` + `NativeStringInfoCatalog` C# wrapper.
   - `LocalizationProvider.TryBootstrapStringInfo` extracts
     `stringinfo.pabgb` from `0008/0.pamt` and loads the catalog.
     New public API: `ResolveStringInfoHash(uint) → string?` and
     `HasStringInfo` for the icon pipeline to gate on.
   - 5 new C# tests + 13 new Rust tests; all 73 / 80 pass.
   - Status footer + name-resolution code paths unchanged — the
     bridge degrades silently when no install is found.

2. ~~**Phase 2 — DDS decode + webp re-encode in C#**~~ ✅ **Landed.**
   What shipped:
   - `IconImageEncoder.EncodeDdsToWebp(ReadOnlySpan<byte>, int targetSize, int quality)`
     in `src/CrimsonAtomtic.Ui/Services/`.
   - **Hand-rolled BC1 (DXT1) + BC3 (DXT5) block decoders** instead
     of adding `Pfim` / `BCnEncoder.Net` — keeps the dependency
     surface minimal (project rule 8) and AOT-safe by construction
     (only 200 LOC of pure C#). BC4/5/7 unsupported — not used by
     any item icon in 1.06 (verified against extracted samples).
   - **SkiaSharp 3.119.4** for the resize + WebP encode step — pulled
     transitively via `Avalonia.Skia` 12.0.3, no new explicit NuGet.
     Pipeline: DDS header parse → 4×4 BC block decode → RGBA8888 →
     `SKBitmap` → `SKImage.ScalePixels(SKSamplingOptions(Linear))`
     → `SKImage.Encode(SKEncodedImageFormat.Webp, quality)`.
   - 6 new C# tests in `IconImageEncoderTests`: live BC3 fixture
     produces valid WebP at varying target sizes, synthetic
     solid-red BC1 round-trips, garbage / truncated / unsupported-
     FourCC inputs throw `InvalidDataException`.
   - Test fixture: real `cd_icon_map_enemy_die_1.dds` (32×32 DXT5)
     extracted from 0012; copied next to the test runner via
     `Content Include` in the Tests csproj.
   - AOT publish verified: bundle size unchanged (23.9 MB exe);
     SkiaSharp + the encoder add zero trim warnings.

3. ~~**Phase 3 — Extraction action UI**~~ ✅ **Landed.** What shipped:
   - `crimson_iteminfo_lookup_icon_path_hash` C ABI getter
     ([crimson-rs #26](https://github.com/bbfox0703/crimson-rs/pull/26))
     exposing `item_icon_list[0].icon_path` per ItemKey.
   - C# wrapper additions: `IItemInfoCatalog.LookupIconPathHash`,
     `NativeItemInfoCatalog.LookupIconPathHash`,
     `LocalizationProvider.GetItemIconPathHash`. Also exposed
     `LocalizationProvider.Paz` and `LocalizationProvider.GameRoot`
     for downstream services to reuse the bootstrap state.
   - `IconExtractionService` in
     `src/CrimsonAtomtic.Ui/Services/`: pure async orchestrator
     taking `LocalizationProvider`, `IPazExtractor`, gameRoot,
     cacheDir, overwriteExisting, optional `IProgress<IconExtractionProgress>`,
     and a `CancellationToken`. Walks `ItemCount` entries, for each
     one: resolve icon hash → resolve stringinfo → lowercase name +
     ".dds" → PAZ-extract from `0012/ui/texture/icon/` → encode
     via `IconImageEncoder` → `File.WriteAllBytesAsync`. Counts
     written / already-cached / no-icon / no-string /
     not-in-archive / failed; keeps the first 10 failure messages
     for the summary. Progress sink is throttled to every 25 items.
     Per-item exceptions are caught and counted — one bad item
     never aborts the run.
   - `IconExtractionProgressDialog` modal: progress bar +
     running-counts text + Cancel-then-Close button. Cooperative
     cancellation via the dialog's `CancellationTokenSource`;
     window-close fires cancel too, then waits for the worker to
     honour it before allowing the close. Disposes the CTS on the
     window's `Closed` event. CA1001 explicitly suppressed
     (Avalonia owns the window lifetime).
   - Tools menu entry "_Extract Icons from Game Data…" in
     `MainWindow.axaml`; click handler in `MainWindow.axaml.cs`
     resolves the target directory (configured > `<exe-dir>/IconCache/`),
     runs the dialog, and on success re-seeds `IconProvider`
     through `MainWindowViewModel.SetIconCacheDirectory` so the
     freshly-written icons appear in already-rendered DataGrids
     without restarting.
   - End-to-end test `IconExtractionServiceTests.RunAsync_LiveInstall_WritesIcons`
     gated on `CRIMSON_RUN_EXTRACTION_TEST=1`. Takes ~3 min against
     the live 1.06 install; writes ~6,200 icons. An optional
     `CRIMSON_EXTRACTION_TARGET=<path>` env var redirects the
     output and skips cleanup — used to populate the user's actual
     cache directory from CLI.
   - Live run summary (1.06 install, default settings):
     6,400 items processed; ~6,200 written; ~200 skipped (no icon
     entry / dev items / stale prefab references); 0 failed.

4. **Phase 4 — Polish + edge cases** (future, low priority now).
   - Mercenary character portraits (`icons_mercenary/` shape — keyed by
     CharacterKey, source likely in `ui/texture/image/portraitimage/`).
   - Skip-already-cached on subsequent runs; "Re-extract all" override.
   - File size optimisation (quality knob? 64×64 vs 128×128?).
   - Surface "icon coverage: N of M items" in the status bar.
   - Update docs/data-policy.md if needed (these icons ARE derived
     data and stay outside git — matches the rule).

**Game data layout (snapshot from Phase 1 + 2 investigation)**:
- `0008/gamedata/binary__/client/bin/iteminfo.pabgb` (~24 MB, 6,400 items).
- `0008/gamedata/binary__/client/bin/stringinfo.pabgb` (~1.8 MB) — ✅
  the resolver, parsed and wired in Phase 1.
- `0008/gamedata/binary__/client/bin/localstringinfo.pabgb` (~2.0 MB) — localized variants (probably distinct from PALOC).
- `0012/ui/texture/icon/` — 7,560 `.dds` files; another 11,000+ DDS
  across other `0012` subdirs. Filename shape e.g.
  `cd_icon_skill_07.dds`, `cd_knowledgeimage_knowledge_recipe_*.dds`.
  Some other directories in 0012 (`questimage/`, `challengeimage/`,
  `portraitimage/`, `playguideimage/`, `worldmapimage_knowledge/`)
  hold related asset categories.
- DDS format confirmed in Phase 2: BC3 (DXT5) is the dominant format
  for item icons; 32×32 base size with mips=1 is typical. BC1 also
  used for some opaque textures. BC4/5/7 absent.
- **Partial-compression status**: 17,975 of 18,565 (97%) DDS files
  under `0012/` have PAZ flag `raw_compression == 1`.
  `binary::paz::decompress_partial` covers three sub-formats:
  (a) identity (`c == u`), (b) header(128)+LZ4-with-prefix-dict,
  (c) DDS per-mip table. (a)+(b) cover every file under
  `0012/ui/texture/icon/`; (c) — ported from CrimsonForge's
  `_decompress_type1_dds_per_mip_sizes` — covers the
  `0012/ui/texture/image/worldmap/` SDF tiles plus most large
  textures elsewhere. The investigation harness that derived these
  rules (5 `probe_partial_*` `#[ignore]`d tests) was deleted after
  landing; git history retains them. **Still unrecognised**: the
  PAR-container layout for `.pam` / `.pamlod` / `.pac` mesh assets
  in 0009/0015 (~93k entries). CrimsonForge's
  `_decompress_type1_par` decodes those via an 8-slot table at
  offset 0x10 — port whenever mesh extraction is on the roadmap.
  Unrecognised entries return `BODY_PARSE` (distinct from PAZ
  corruption) so callers can tell what failed.

**Current icon-display code** (unchanged across all three phases):
- `IconProvider` lazy-loads + Bitmap-caches from a configured root.
- `AppSettings.IconCacheDirectory` is the cache location. Both
  Tools → Set Icon Folder… and Tools → Extract Icons… write through
  `MainWindowViewModel.SetIconCacheDirectory` so changes propagate
  to already-rendered cells without restart.
- `ItemKeyToIconConverter` is the XAML-side binding glue.

### #2-N — Lower-priority deferred items

Open from earlier work, none blocking the icon pipeline:

2. **Remaining InventoryKey labels** (`3`, `4`, `11`, `12`, `14`,
   `15`, `17`, `18`). Empty in slot0 so the
   `Probe_InventoryKeyContainers` test can't infer their purpose.
   Run the probe against a save that has those containers
   populated, then extend `LocalizationProvider.InventoryContainerLabels`.

3. ~~**Batch `SetScalarField` C ABI**~~ ✅ **Landed.** Single FFI
   round trip applies many mutations with one post-batch
   re-decode. Fill-stacks 168-op path: ~5 s → ~1 s.
   - Rust: `crimson_save_set_scalar_fields_batch` + repr(C)
     `CrimsonScalarBatchOp` (block_idx, field_idx, path[],
     bytes); validate-all → patch-all → one `decode_blocks`.
     All-or-nothing on validation failure; optional
     `out_failed_op_index` pinpoints offending op. Shared
     `resolve_leaf_range` private helper keeps the three setter
     surfaces (top-level, path, batch) in lockstep on error codes.
     91/91 Rust tests; live-save batch tests verify N-op
     equivalence is byte-identical to N × single-op.
     (crimson-rs [PR #27](https://github.com/bbfox0703/crimson-rs/pull/27))
   - C#: `ScalarBatchOp` public record struct;
     `ISaveLoader.SetScalarFieldsBatch(IReadOnlyList<ScalarBatchOp>)`;
     `CrimsonSaveException.FailedOpIndex` (nullable int) for
     batch failures. Marshalling: single PathStep arena + single
     byte arena + one ops array — 3 GC pins regardless of N
     (vs the 2N pins a per-op `GCHandle.Alloc` would have used).
     `MainWindowViewModel.BulkFillItemListMaxStackAsync` now
     issues one batch call inside `Task.Run`.

4. **`skill_info` bridge** for SkillKey / KnowledgeKey name
   resolution. crimson-rs already has a `skill_info/` parser used
   internally; needs a C ABI bridge (mirror of `iteminfo` /
   `paloc`) to surface skill names in the editor's resolved-name
   column. Would also let us label
   `SkillLearnElementSaveData._knowledgeKey` values like 40114
   that today show empty.

5. **`FieldGimmickSaveDataKey` / `FieldNPCSaveData._characterKey`
   resolution.** Both probe to no-PALOC-entry today. These look
   like spawn template IDs (structured u32, not localized
   namespace references). Probably need a different data file
   parsed; lower priority since most users don't care about
   anonymous field NPCs.

6. **`MissionKey` / `KnowledgeKey` / `QuestKey` proper names.**
   These straddle multiple PALOC type bytes or live entirely
   outside PALOC (mission text uses `{staticInfo:Mission:...}`
   template references at 0xC1). Needs a template-resolver pass
   to reconstruct full strings. Currently NOT in
   `TypeNameToTypeByte` so they show blank (correct — better
   than showing the wrong name).

7. **Length-changing edits (PR B)**. List add / remove / reorder
   + inline-byte resize. Needs an `ObjectBlock` re-serializer
   (mirror of `decoder.rs`) and body re-emit that recomputes TOC
   offsets. Hard but unlocks adding new inventory items (not just
   editing existing slots). Value-prop dropped now that Item
   Picker + slot replace + Fill stacks cover most edit needs.

8. **Cross-bag item search beyond one nested level.**
   `BuildNestedHaystack` walks one level deep. List-of-lists
   shapes (haven't seen one yet in 1.06) wouldn't be reached.
   Generalise to bounded recursion when needed.

9. **Avalonia.Diagnostics 12.x** — add back behind a `Debug`
   condition once published.

10. **Re-evaluate the DataGrid AOT warning suppression** —
    `<NoWarn>IL2104;IL3053</NoWarn>` in `CrimsonAtomtic.Ui.csproj`
    is a workaround for Avalonia DataGrid 12.0.0 internals. Drop
    when 12.1+ ships a trim-safe DataGrid.

11. **Save backup management** — auto-backup on every load + every
    write, configurable retention. Less critical now that mtime is
    preserved, but still useful.

12. **Mod awareness** — read `CDMods/cdumm.db` (SQLite) and
    `mods/_enabled/` to surface mod-added items without crashing
    on unknown keys.

13. **Cross-platform save paths** — Wine/Proton prefix resolution
    on Linux/macOS. Currently `IPlatformPaths` is Windows-only.

14. **Multi-level path tests on the C# side**. The current
    `SetScalarField_NestedPath_RoundTripsThroughWriteToFile` test
    exercises one descent step. Worth a two-step regression
    (`inventoryList[N].itemList[M].count`-shaped) — the Rust
    c_abi test already covers any one-step-reachable scalar, so
    the gap is only on the C# side.

## Key resolvers we still need — C# consumption expectations (forward-looking note)

This section is for whoever picks up the next crimson-rs session to
investigate / add parsers for the **Key types still showing blank in
the save editor's resolved-name column**. CrimsonAtomtic already has
all the C#-side infrastructure to consume them — see
[`LocalizationProvider`](../src/CrimsonAtomtic.Ui/Services/LocalizationProvider.cs),
which today bootstraps `iteminfo`, `paloc`, and `stringinfo` from a
Crimson Desert install and surfaces typed `Resolve*` methods to the
ResolvedNameColumn in the fields DataGrid. So the only **non-trivial
choice for the upstream PR is the C ABI shape** — this note exists so
the crimson-rs author can shape that ABI to minimize C#-side
ceremony, rather than discovering the impedance mismatch in a
follow-up.

### Known Key gaps (observed against live 1.06 saves)

| Key | Status | Likely data source | crimson-rs scope |
|---|---|---|---|
| `SkillKey` / `KnowledgeKey` | parser exists | `skill_info/` (already in crimson-rs, internal) | **small** — just expose a C ABI bridge mirroring iteminfo / paloc |
| `MissionKey` / `QuestKey` | parser absent | likely `missioninfo.pabgb` / `questinfo.pabgb` under `0008/gamedata/binary__/client/bin/`; names may use `{staticInfo:Mission:KEY}` template references | **medium** — investigate, write parser, design template-expansion ABI |
| `FieldNPCSaveData._characterKey` / `FieldGimmickSaveDataKey` | unknown source | spawn-template IDs (structured u32, not paloc-shaped) — probably a shared spawn/character table | **medium** — investigate, new parser, bridge |
| `SubLevelKey` | unknown source | level / world data, possibly under `0010` / `0011`, or a paloc type byte not yet enumerated | **unknown** — `plcli` probe first |

### Established bridge pattern (iteminfo / paloc / stringinfo)

All three existing bridges follow the same shape:

- `crimson_<name>_load_from_bytes(byte*, len, out IntPtr handle)` — preferred entry. C# owns PAZ extraction via `crimson_paz_extract_file` and feeds bytes in. Keeps gameRoot bootstrap centralized in `LocalizationProvider.TryBootstrap*`.
- `crimson_<name>_load_from_file(string path, out IntPtr handle)` — convenience for direct-on-disk consumers.
- `crimson_<name>_free(IntPtr)`.
- `crimson_<name>_entry_count(handle, out uint)`.
- `crimson_<name>_lookup_by_<key>(handle, <key-args>, byte* buf, nuint bufLen, out nuint required)` — two-call buffer pattern.
- `crimson_<name>_get_entry(handle, uint idx, …)` — same pattern for enumeration.
- Errors map to existing codes (`NOT_FOUND`, `BODY_PARSE`, `IO`, `BUFFER_TOO_SMALL`). Add a new negative number only when no existing category fits.

The C# side wraps each bridge with a ~50-LOC `Native<Name>Catalog : I<Name>Catalog` + a `LocalizationProvider.Resolve<Key>(uint)` method that chains through stringinfo / paloc.

### Per-key ABI recommendations

**SkillKey / KnowledgeKey** ([#4](#2-n--lower-priority-deferred-items))
- Recommended: `crimson_skill_info_lookup_string_key(u32 key) → u32?` returning the stringinfo hash, mirror of `crimson_iteminfo_lookup_string_key`.
- C# becomes: `LocalizationProvider.ResolveSkillKey(uint) → _skillInfo.LookupStringKey(key) → _stringInfo.LookupByHash(hash)`.
- Open question for the crimson-rs author: does the underlying `skill_info` entry distinguish "skill" from "knowledge"? If yes, splitting into `lookup_skill_string_key` / `lookup_knowledge_string_key` is cleaner than one polymorphic getter; C# is happy either way.

**MissionKey / QuestKey** ([#6](#2-n--lower-priority-deferred-items))
- Two ABI shapes worth weighing:
  - **A (Rust expands templates)**: `crimson_mission_info_lookup_display_name(handle, paloc_handle, u32 key, byte* buf, …) → string`. Rust gets a paloc handle alongside its own, does the `{staticInfo:Mission:KEY}` walk internally, returns a fully-resolved localized string. C# stays simple.
  - **B (segmented)**: returns a typed segment list `[literal | paloc_ref | string_ref | ...]` that C# concatenates by chaining existing lookups. More flexible but spreads template syntax knowledge across the FFI.
- **C# side prefers A.** Template expansion is a fact about the data format and belongs in the parser. C# only knows "this is the resolved-name column — render the string".
- If mission and quest live in separate `.pabgb` files, two parsers + two bridges is fine; if they share format, one parser with a discriminator is fine too.

**FieldNPC CharacterKey / FieldGimmickSaveDataKey** ([#5](#2-n--lower-priority-deferred-items))
- These resolve in two hops: spawn-template ID → CharacterKey/GimmickKey → display name. Whether crimson-rs exposes one or two hops depends on the underlying data layout.
- Expected: `crimson_<source>_lookup_character_key(u32 spawnId, out u32 characterKey, out u32 stringInfoHash) → i32`, two outputs in one call. C# then either trusts the stringinfo hash directly or chains through a future `characterinfo` bridge.
- Combine FieldNPC + FieldGimmick under one bridge if they share the same source file.

**SubLevelKey** (new — not previously enumerated)
- No assumption — `plcli`-driven hexpat probe first to confirm the data source. Once located, follow the standard bridge shape.
- Could turn out to live in PALOC under a yet-unknown type byte, in which case no new parser is needed; just extend `LocalizationProvider.TypeNameToTypeByte`.

### C# wiring points (cross-reference for the consumer PR)

When the C ABI lands, the C# integration touches a small fixed set of files:

- [`NativeSaveLoader.cs`](../src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs) `NativeMethods` block — add `[LibraryImport]` declarations for the new entry points.
- New files under [`src/CrimsonAtomtic.RustInterop/`](../src/CrimsonAtomtic.RustInterop/):
  - `I<NewCatalog>Catalog.cs` (interface, mirroring `IItemInfoCatalog`).
  - `Native<NewCatalog>Catalog.cs` (implementation, mirroring `NativeItemInfoCatalog`).
- [`LocalizationProvider.cs`](../src/CrimsonAtomtic.Ui/Services/LocalizationProvider.cs) — `TryBootstrap<NewCatalog>` extraction + a `Resolve<Key>(uint)` method.
- The `TypeNameToTypeByte` / type-routing dispatch — add an entry so the ResolvedNameColumn renders the new resolver for the right field-class names.

No public API breakage on the existing surface; the new code only adds wrappers.

## Important context / gotchas (don't relearn these)

- **Old saves are the same format**. Empirically verified this session:
  slots dated 2026-04-29 through 2026-05-11 all have `version=2 /
  flags=0x0080`, HMAC ok, and decode with 0 undecoded bytes. Block count
  drift (1112 → 1136 across slots) is gameplay-driven, not format-driven.
- **C ABI + PyO3 coexist in one cdylib**. Building with
  `--features c_abi` adds the `crimson_save_*` exports; the
  `PyInit_crimson_rs` symbol stays alive too. Python tooling is
  unaffected.
- **Error codes are stable**. `c_abi::error` defines OK=0 and negative
  codes per category (see `NativeMethods` constants in C#). Add new
  variants; never reuse a number.
- **String getters use the two-call pattern**. First call with `buf=null`
  to get the required size (returns `BUFFER_TOO_SMALL`); second call with
  the allocated buffer writes UTF-8 + NUL. Same shape for fixed-size
  class names and for variable-size JSON blobs.
- **`get_block_json` is hand-rolled JSON**. No serde dep in the cdylib.
  If the shape grows past a few dozen lines of formatter code, switch
  to serde_json under a feature. C# parses with a source-generated
  `JsonSerializerContext` so the deserialization stays AOT-safe.
- **`value` is pre-formatted in Rust**. Mirrors
  `tools/inspect/inspect_save_section.py --pretty`'s `format_field_value`,
  so cross-tool views of a block agree. Don't reformat in C# — let the
  Rust source-of-truth win.
- **`NativeSaveLoader` caches a live `CrimsonSaveHandle`** keyed by
  path. `Load(path)` swaps it (disposing the old); `LoadBlockDetails`
  fast-paths through the cache when the path matches; otherwise a slow
  path opens transiently. `Dispose` (wired to `desktop.Exit`) frees the
  cache cleanly. Subsequent post-dispose calls still work via the slow
  path. Path comparison is `OrdinalIgnoreCase` to match Windows
  conventions.
- **`SetScalarField` / `WriteToFile` operate on the cached handle**,
  never the slow path — they throw `InvalidOperationException` when
  no save is loaded. `SetScalarField` re-decodes all blocks on success
  (~ms-scale on 1112 blocks); subsequent `LoadBlockDetails` sees the
  new value. `WriteToFile` reuses the original header nonce; the
  on-disk output passes the game's HMAC check.
- **Scalar-only mutation today**. The C ABI accepts `fixed_prefix` /
  `fixed_suffix` fields only, length-checked. List add/remove, inline
  byte resize, and anything else that changes block length needs an
  ObjectBlock re-serializer (PR B on the roadmap) — defer until the
  UX actually demands it.
- **Nested editing addresses scalars by path.** Each
  `FieldRowViewModel` carries an `EnclosingPath: IReadOnlyList<PathStep>`
  — the descent from the top-level TOC block to its containing block.
  Top-level rows hold an empty path. The VM passes that path through
  to `ISaveLoader.SetScalarField(blockIdx, path, fieldIdx, bytes)`,
  which marshals it across the FFI to
  `crimson_save_set_scalar_field_path`. After commit, the VM walks the
  freshly-decoded top-level block down each nav frame's stored path
  to rebuild every frame's `BlockDetails` reference, so popping back
  via the breadcrumb shows fresh values.
- **`PathStep` is FFI-layout.** Public struct in
  `CrimsonAtomtic.RustInterop`, `[StructLayout(LayoutKind.Sequential)]`,
  matches the Rust `CrimsonPathStep` byte-for-byte. The C# span
  passes straight to the native function via `fixed (PathStep* …)`
  — no per-element marshalling. Indices are `uint` to keep the layout
  exact; convert from `int` at the call site.
- **`NOT_NAVIGABLE (-15)` ≠ `NOT_SCALAR (-12)`.** The first fires
  when a mid-path step targets a field whose kind isn't
  ObjectLocator(child=Some) / ObjectList. The second only fires on
  the leaf. If you see `NOT_NAVIGABLE` from C# you almost certainly
  built the path against the wrong block in the nav stack.
- **`format_field_value`'s shape is the editing contract**.
  `ScalarFieldEditing.TryParse` splits the pre-formatted value on the
  last space and validates the trailing `<…>` tag — if Rust ever
  changes how `format_scalar` emits scalars (e.g. drops the type
  tag), C# editing breaks silently. The `ScalarFieldEditingTests`
  cover the current shape; bump them when the Rust formatter moves.
- **Save As re-anchors the working document.** `MainWindowViewModel.SaveAs`
  writes via `WriteToFile(newPath)` and then calls `Load(newPath)` so
  the cached handle keys to the new path. Side effect: nav state
  (selected block, breadcrumb, edit panel) resets. Acceptable for
  v1; the standard "Save Copy As…" alternative that keeps the
  session anchored to the original is a follow-up if anyone wants it.
- **Save / Save As preserve the original file's last-write
  timestamp.** Captured into `_loadedFileLastWriteTime` at load
  time, re-applied (best-effort via `File.SetLastWriteTime`) after
  every `WriteToFile`. Reason: Steam Cloud uses mtime to pick the
  newer side of a sync, and the in-game save picker sorts by
  recency — silently bumping mtime would have the game/Cloud
  treat the edited save as "freshest" and override the user's
  intent. After Save As, the destination's restored timestamp is
  read back into `_loadedFileLastWriteTime` so the next plain
  Save preserves that same value rather than drifting forward.
  IO errors during timestamp restore are swallowed; not worth
  failing the whole save flow over a permissions issue.
- **Raw `gamedata/*.paloc` is encrypted/wrapped.** Reading the
  file straight off disk and handing it to PALOC parse will fail
  loudly with `BODY_PARSE` now (it used to alloc-bomb the process).
  The right path is PAZ extraction: feed `<install>/0020/0.pamt` +
  `gamedata/stringtable/binary__` + `localizationstring_eng.paloc`
  into `IPazExtractor.ExtractFile` first, then hand the bytes to
  `NativePalocCatalog.LoadFromBytes`. `LocalizationProvider` wires
  both halves together at app startup.
- **`LocalizationProvider` degrades silently.** When no game install
  is detected (or the PAZ extract fails, or the PALOC parse fails),
  `IsLoaded` stays false and `Lookup` always returns null. The
  status-footer text is the only UI signal that this happened — the
  editor itself keeps working on saves. Don't add hard dependencies
  on `Localization.Lookup` succeeding.
- **PALOC keys are integer-encoded decimal strings, not human ids.**
  Each PALOC entry's `string_key` is a u64 written as base-10:
  bits 63..32 = the item key, bits 7..0 = a "type byte"
  (`0x70` == item name). The middle 24 bits aren't predictable, so
  any name lookup must *scan* PALOC once and build a
  `Dictionary<uint, string>` keyed by the upper 32 bits where
  type byte == 0x70. `LocalizationProvider.BuildItemNameMap`
  implements that walk; it runs ~once per loaded language (English
  eagerly, secondary lazily on first switch). Lookup is then O(1).
  iteminfo's `string_key` (e.g. `"Pyeonjeon_Arrow"`) is the
  internal identifier, NOT a PALOC key — it's used only as a
  fallback for the ~71 dev items the game ships without a 0x70
  entry.
- **Name resolution covers five schema TypeNames today.**
  `LocalizationProvider.TypeNameToTypeByte` maps schema TypeNames
  to PALOC type bytes:
  - `ItemKey` → `0x70` (items, with iteminfo string-key fallback)
  - `FactionKey` → `0x30`
  - `CharacterKey` → `0x30` (same byte as factions — the numeric
    ranges don't collide: factions are 1,000,000+, characters are
    0..999,999; one map for both works)
  - `GimmickInfoKey` → `0x00` (in-world scenery / interactables:
    "Grindstone", "Anvil", "Skybridge Gate", "Painting Fragment")
  - `LevelGimmickSceneObjectInfoKey` → `0x00` (open-world
    discovered scene objects — same byte as GimmickInfo; e.g.
    1000003 → "Circus Pillar", 1000109 → "Chair", 1000121 → "Oak
    Barrel". Used by `DiscoveredLevelGimmickSceneObjectSaveData`)
  Adding a new namespace = one row in that dict + one entry in
  `ElementRowViewModel.IsNameKey`. The PALOC build also captures
  the new type byte via `LocalizationProvider.NameTypeBytes`;
  forget to add it there and lookups return empty.
- **`MissionKey` / `KnowledgeKey` / `QuestKey` are intentionally
  NOT in `TypeNameToTypeByte`.** Each one collides with the item
  table at 0x70 but the values aren't really mission / knowledge /
  quest names. Specifically:
  - `MissionKey` values like 1003440 resolve to "Hearty Braised
    Meat and Fish" — that's the item the mission rewards, not the
    mission title. Verified against the user's
    `D:\Github\crimson-rs\out\output*.txt` dumps: 0x70 is
    unambiguously the item namespace, and missions reuse the same
    numeric ID as their tracking item. Mission text fragments only
    appear at 0xC1 with embedded `staticInfo:Mission:...`
    templates that need a separate template resolver — not
    available today.
  - `KnowledgeKey`: small values (1, 2, 4, 7, 51) sit at 0x93 as
    knowledge *category* names ("Various Combat Skills",
    "Fundamentals of Cooking"), but large-numbered keys don't
    appear at 0x93 and only hit coincidentally at 0x30/0x70.
  - `QuestKey`: large values resolve to the quest's *associated*
    character or item via 0x30 / 0x70, never to a quest title in
    its own namespace.
  Showing the wrong name (an item name labelled as a mission name)
  is worse than showing nothing. Leave them empty until a real
  resolver appears.
- **`SkillKey` exists in the schema but wasn't reachable from the
  test save.** All 15 schema occurrences in this user's save are
  `absent` (no skills equipped / unlocked in slot0). To pin down
  the SkillKey type byte, harvest from a save that actually has
  populated skill data and re-run
  `Scan_DiscoverTypeBytesFromLiveSave`. The histogram suggests 0x99
  / 0x9A ("Skill: ..." entries, ~88 each) as likely candidates but
  neither is confirmed.
- **`FieldNPCSaveData._characterKey` and `SkillLearnElementSaveData._knowledgeKey`
  aren't in PALOC.** Probed: `117_440_514` (`0x07000002`, a field
  NPC instance) and `40_114` (a learned skill) both return no
  PALOC entry at any type byte. Field NPCs are likely identified
  by spawn-template ID (a structured u32, not a localized
  character archetype); learned-skill names probably live in
  `skill.pabgb` (a separate file with its own parser in
  crimson-rs's `skill_info/` module, not yet bridged to the
  editor). Resolving either needs new C ABI surface — defer
  until the user prioritises skill / NPC name display.
- **`FieldGimmickSaveDataKey` is save-internal, not localized.**
  Every harvested sample (881022, 40052, 267612, 62768, …) returns
  no PALOC entry at any type byte — these are intra-save block
  references (similar to `StageKey`, `FactionNodeKey`,
  `FieldNPCSaveDataKey`, `FieldInfoKey`). Anything ending in
  `SaveDataKey` is generally an internal index, not a name
  reference; don't try to resolve them.
- **Empty name cells on Faction / Character / Item rows are real
  data, not a bug.** Some keys (e.g. `FactionElementSaveData
  key=1000131`) don't have a localized name in 1.06 PALOC — same
  way the ~71 dev items lack a 0x70 entry. ItemKey gets the
  iteminfo string-key fallback so items always show *something*;
  faction / character / gimmick namespaces don't have an
  equivalent fallback, so they show blank when PALOC has no entry.
- **FieldRowViewModel.ApplyCommittedValue refreshes
  ResolvedName.** Before this fix, editing `_itemKey` updated
  the Value column but the Name column kept showing the
  previous item's name until the user drilled out and back in.
  The fix stashes `_localization` on the VM at construction so
  the post-mutation refresh path can re-call
  `ResolveByFieldTypeName(row.TypeName, new_value)` from inside
  `ApplyCommittedValue` — every other ResolvedName driver
  (constructor, post-language-switch refresh) was already
  correct, only the per-edit refresh was missing.
- **Quest titles exist in PALOC but use a non-trivial indirection.**
  Manual check found "Where the Wind Guides You" at PALOC key
  `15438629828055531777` — a u64 whose upper-32 bits
  (3,595,124,794) are not the save-side QuestKey value (1000725).
  This means quest title text is keyed by some hash/transform of
  the quest ID, not the ID itself; resolving requires either
  reverse-engineering the transform or walking 0xC1-and-similar
  entries and matching by their embedded `staticInfo:Quest:…`
  template references. Deferred — not worth doing until the user
  prioritises quest-name display.
- **`DiscoveredLevelGimmickSceneObjectSaveData` is a subset of
  scene objects the player has *physically interacted with*, not
  all of them.** So filtering for "Grindstone" / "Anvil" /
  "Abyss Nexus" can come back empty even though the keys resolve
  fine in PALOC — those gimmicks just aren't in *this* save's
  discovered list yet. Cross-check via Tools → Browse
  Localization: if the name resolves there but the filter on the
  discovered-list view is empty, it's data, not code.
- **Item Picker dialog joins iteminfo + PALOC.** Tools → Browse
  Items opens `ItemPickerWindow`, backed by `ItemPickerViewModel`
  which pre-builds one row per iteminfo entry (~6,400 in 1.06):
  numeric ItemKey, iteminfo string id, English name (with
  string-id fallback when PALOC has no 0x70 entry), and secondary-
  language name when set. Filter matches all four columns (numeric
  search "11" works too). Per-row copy buttons (K / S / N / N₂)
  use the same Avalonia 12 clipboard dance as Browse Localization
  — `SetTextAsync` lives on `ClipboardExtensions`, not directly
  on `IClipboard`. The window degrades silently when the
  iteminfo bridge isn't loaded (no install discovered at boot).
- **Set-to-max button uses iteminfo's `max_stack_count`.** The
  Rust iteminfo bridge stores `max_stack_by_key: HashMap<u32, u64>`
  alongside `by_key`; `crimson_iteminfo_lookup_max_stack(handle,
  item_key, *out_max)` exposes it through the C ABI. C# wraps it
  via `IItemInfoCatalog.LookupMaxStackCount(uint) → ulong?` and
  `LocalizationProvider.GetItemMaxStackCount(uint)`. The edit
  panel's "Set to max" button finds a peer `ItemKey` on the
  current BlockFrame's fields, looks up the cap, and pre-fills
  the edit textbox — explicit Apply still required. CanExecute
  gates on (a) integer-typed scalar selected, (b) peer ItemKey
  resolves, (c) iteminfo has a max-stack entry; so the button
  stays visible at all times but only lights up when relevant.
  Driver: the user wanted "fill stack" without exceeding what
  the game considers valid (stuffing 100k wood into a 50-stack
  slot breaks the bag's dynamic slot computation). The RawText
  assignment is deferred one dispatcher tick at Background
  priority — without that, on Avalonia 12 the first click was a
  no-op because the focused TextBox didn't repaint the new
  bound value until a follow-up event prodded it.
- **"Fill stack(s)" target calculation has two regimes.**
  Driven by `MainWindowViewModel.TryComputeTargetStack(current,
  max, out target)`:
  - **max > 100** (currency / contributions / camp resources):
    standard fill-to-max. Skip if `current ≥ max`.
  - **max ≤ 100** (regular items — arrows, herbs, ores —
    where partial stacks can pile up across slots):
    round up to the next multiple of `max`. So `current=120, max=50`
    → target=150 (already had 2 full stacks of 50 + a partial
    stack of 20; rounds the partial up). Skip if current is
    already an integer multiple of max.
  Threshold = 100 sourced from the user's domain note about
  contributions / Camp Funds. Single threshold keeps the logic
  one-liner; if a real edge case appears later, split per-namespace.
- **"Fill stack(s)" button covers two row shapes + a UX split.**
  Per-row button in the elements DataGrid:
  - **Single item**: row IS an ItemSaveData (carries `_itemKey`
    + `_stackCount` on its own fields). Label: "Fill stack".
    **Skips the confirm dialog** — same gesture weight as the
    edit-panel Set-to-max button.
  - **Container**: row has a nested ObjectList of single-item
    rows AND the row's own key field (if any) is `InventoryKey`
    (containers) — NOT `CharacterKey` / `FactionKey` / `ItemKey`
    (named entities). Label: "Fill stacks". **Opens a Yes/No
    confirm** before applying the batch.
  The named-entity exclusion is what keeps `MercenarySaveData`
  rows (each row is a person resolved as "Damiane" / "Oongka"
  who happens to own gear) from getting a "Fill stacks" button —
  a mercenary isn't a container conceptually.
- **Bulk fill runs on a background `Task.Run`.** 168
  `SetScalarField` calls × per-call full-block re-decode = several
  seconds of work. Running it on the UI thread froze the
  window mid-operation; now the loop sits on a worker thread
  while the UI stays responsive (Avalonia 12 dispatcher
  schedules the post-await continuation back to UI thread for
  the `RefreshNavStack` + `RebuildFromTop` calls). `BulkOpStatus`
  shows `Filling N stack(s)…` at start, `Filled N stack(s).`
  at end. Future optimisation: a "batch mutate" C ABI that
  defers the re-decode storm until the batch completes — would
  cut the cost ~100×.
  `ElementRowViewModel.IsSingleFillCandidate` + `IsContainerFillCandidate`
  are the two shape flags; `IsBulkFillCandidate` is their OR.
  `FillButtonLabel` switches the button text between "Fill
  stack" and "Fill stacks" so the singular/plural matches.
- **Item icons are a user-configured external folder.** Pearl
  Abyss owns the artwork, so we deliberately don't bundle the
  6,011-icon set. `AppSettings.IconCacheDirectory` (configurable
  via Tools → Set Icon Folder…) points at a directory of
  `<ItemKey>.webp` files; the reference repo
  (`D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS\icons_local\`)
  is the canonical source the user can re-point to. Probe order:
  configured setting → `<exe-dir>/IconCache/` → nothing.
  `IconProvider` (cached `Bitmap?` per ItemKey, IO + decode on
  first hit, miss cached as null) sits on the
  `LocalizationProvider` and is exposed to XAML via the static
  `ItemKeyToIconConverter.Instance` (Avalonia compiled bindings
  can't construct converters with constructor args). Avalonia
  12's SkiaSharp backend decodes WebP natively — no extra NuGet
  dependency. Item Picker + elements DataGrid both render the
  icon at 40×40; element rows with non-ItemKey keys (CharacterKey
  / InventoryKey / etc.) zero `IconItemKey` so the converter
  short-circuits and the cell stays empty without a miss probe.
- **InventoryKey labels are hardcoded.** InventoryKey doesn't
  have a PALOC namespace (small u16 values collide with every
  other table). `LocalizationProvider.InventoryContainerLabels`
  is the manually-maintained map: `1 = Camp & Contributions`,
  `2 = Backpack`, `5 = Quest Artifacts`, `8 = Trade Packs`,
  `9 = Packaged Resources`, `10 = Valuables`, `13 = Power
  Cores`, `14 = Equipped Gear`, `16 = Cooked Food`, `19 =
  Foraged Ingredients`, `20 = Collectibles`. Sourced via
  `Probe_InventoryKeyContainers` in the test project; re-run
  that probe against a new save / patch and update the map if
  the layout shifts. `CanResolveTypeName` and
  `ResolveByFieldTypeName` are special-cased to route
  InventoryKey through the hardcoded table instead of PALOC.
  `FieldRowViewModel` and `ElementRowViewModel` were widened
  from `tag == "u32"` to `tag is "u32" or "u16"` so the u16
  InventoryKey scalars actually surface as resolvable.
- **Type-byte discovery is automated.** Two xUnit tests in
  `LocalizationTypeByteDiscoveryTests`:
  - `Scan_PrintTypeByteHistogram` — walks English PALOC and prints
    a whole-table type-byte histogram plus per-probe-key hits.
    Use when you know a save-side key value and want to find its
    type byte; edit the `Probes` array to add candidates.
  - `Scan_DiscoverTypeBytesFromLiveSave` — loads the live save,
    walks every block recursively, harvests up to 12 sample u32
    values for each `HarvestTargetTypeNames` entry, and resolves
    each sample against every type byte. Also prints the
    distribution of every *Key TypeName actually present in the
    save (so you can spot namespaces the harvest list missed).
    Use when you don't know any key values for a namespace.
  Both skip cleanly when the game install / save isn't present.
- **PALOC name-map is keyed by `(typeByte, upper32)`.**
  `LocalizationProvider._namesByLang` is per-language. Build cost is
  one walk per first-load of a language (~1-2 s on SSD, ~6k entries
  per captured type byte). Don't add a parallel per-namespace map;
  the single dictionary covers every consumer through
  `ResolveByFieldTypeName`.
- **`ElementRowViewModel` walks one locator level for the row's
  own key, and one list level for its children.** The row's `Key`
  / `Name` columns come from either (a) the element's own
  `ItemKey` / `FactionKey` / `CharacterKey` scalar, or (b) the same
  scalar one level down through an inline locator child
  (e.g. `EquipSlotElementSaveData._item` → `ItemSaveData._itemKey`).
  Separately, `NestedMatchHaystack` walks every `ObjectList` field
  one level deep and concatenates the resolved names of every
  sub-element so the elements filter can find e.g. "Gold" inside a
  bag without the user opening the bag. Deeper paths aren't covered
  — if a future schema needs lists-of-lists, generalise both walks
  to bounded recursion.
- **Element-picker filter has cross-row reach via the nested
  haystack.** Names in the haystack are joined by `\n` (never part
  of a resolved name) and lower-cased. The filter lowers the needle
  once and does an ordinal `Contains` against the haystack, plus
  the existing case-insensitive checks against `ClassName`,
  `KeyText`, and `ResolvedName`. Performance: 18 bags × 168 items
  ≈ 3k dict lookups on enter — invisible.
- **Breadcrumb back-nav remembers where you drilled from.** Each
  `NavFrame` carries a mutable `LastDrilledIndex` (set in
  `DrillIntoField` / `DrillIntoElement` right before pushing the
  child). On pop (`NavigateBack` / `NavigateToDepth`),
  `RestoreDrillSelection` looks up the index, sets
  `SelectedField` / `SelectedElement`, and raises
  `FieldScrollRequested` / `ElementScrollRequested`. Code-behind
  subscribes once at `OnDataContextChanged` and calls
  `DataGrid.ScrollIntoView` via the Avalonia dispatcher at
  `DispatcherPriority.Loaded` — scrolling before the row has
  materialised is a silent no-op on Avalonia 12, so the dispatcher
  hop is load-bearing. Falls back to no-selection (rather than
  throwing) when the saved index is out of range against the
  re-decoded list.
- **PALOC discovery probes a fixed code list.** The 14 codes (kor,
  eng, jpn, rus, tur, spa-es, spa-mx, fre, ger, ita, pol, por-br,
  zho-tw, zho-cn) are sourced authoritatively from `list_all_paloc.py`
  against the live 1.06 install — order matches the group numbers
  the game stores them at (0019..0032). Note the codes are NOT ISO
  639-1: it's `fre` (not `fra`), `por-br` (not `por`), `spa-es` /
  `spa-mx` (split, not just `spa`). If a future patch adds a
  language, re-run `D:\Github\crimson-rs\scripts\list_all_paloc.py
  --game-dir ... --scan-range 0:100` and update
  `KnownLanguageCodes` in `LocalizationProvider`; otherwise the
  picker won't surface it. Discovery itself runs synchronously at
  app launch (probes `0019..0050`); first-launch cost is ~1 s on SSD
  because each successful probe also caches the catalog. Subsequent
  launches re-probe — settings only persists the user's preferred
  secondary, not the discovery result. A stale `secondary_language`
  in `settings.json` (e.g. `fra` from before this fix) is silently
  rejected by the setter and falls back to English-only.
- **`AppSettings` is a deliberately tiny JSON file.** One field
  today (`secondary_language`); source-generated
  `JsonSerializerContext` keeps it AOT-safe. Add fields by appending
  to the record + the context — never reach for an
  `IConfiguration` abstraction here.
- **DataGrid columns are intentionally fixed-width, no
  `Width="*"`.** Total column width exceeds the viewport so the
  built-in horizontal scrollbar appears (Avalonia DataGrid default
  `HorizontalScrollBarVisibility = Auto`). Stretching one column to
  fill made every resize a "shrink everything else first" chore;
  fixed widths + scroll is the spreadsheet affordance the user
  asked for. If you add a column, give it a sensible pixel width
  that fits its typical content (and the user can still drag it).
- **Window position/size restore is snapshot-based with a deferred
  commit.** Avalonia 12 on Windows can land a restored-from-maximized
  window straddling two monitors on multi-display setups.
  `MainWindow` snapshots `Position` (via `PositionChanged` — it's
  not an AvaloniaProperty) and `Width` / `Height` (via
  `OnPropertyChanged` for the standard AvaloniaProperties) while in
  `WindowState.Normal`, then re-applies them on the Maximized →
  Normal transition.
  Crucially the snapshot commit is **deferred** one dispatcher tick
  at `Background` priority: on Win32, the property-change order
  during a maximize is Width/Height first, WindowStateProperty
  second, so reading `WindowState` synchronously inside the
  Width/Height handler still sees `Normal` when the values are
  already the maximized dimensions. Without the deferred commit,
  the snapshot gets poisoned with the maximized rect and the next
  restore lands at near-maximized size. `_snapshotCommitScheduled`
  coalesces multiple Width/Height/Position changes into one
  re-check; `CommitSnapshot` re-reads `WindowState` and abandons
  the commit when it has flipped to non-Normal.
- **`<NoWarn>IL2104;IL3053</NoWarn>` is a load-bearing AOT hack.**
  Without it, `dotnet publish -p:PublishAot=true` fails on the
  unchanged baseline as of ilcompiler 10.0.7 — verified by stashing
  this session's changes and re-running. The two codes are roll-up
  warnings on Avalonia.Controls.DataGrid 12.0.0; suppressing them at
  the csproj level keeps our own code under the original
  TreatWarningsAsErrors safety net.
- **`scripts/package_aot.ps1` no longer pre-deletes dist/<rid>/**.
  Pre-cleaning races with file handles held by a prior `dotnet test`
  (mmap on the freshly-published exe); ilc's first invocation fails
  with exit -1. `dotnet publish` handles its own overwrite.
- **`CrimsonSaveHandle` is a SafeHandle**. CA1419 requires the
  parameterless ctor at the type's visibility — kept public for the
  analyzer even though the marshaller never constructs one (LoadFromFile
  returns IntPtr, wrapped explicitly by `FromOwnedPointer`).
- **Data policy** ([docs/data-policy.md](data-policy.md)) — never commit
  derived data. Old reference repo (`D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`)
  is one-time mining only.
- **Foundation-first** — when parsing produces wrong data, fix the parser
  or schema. Never add workarounds in consumers.
- **crimson-rs branch protection** — `main` is protected; CI gates clippy
  -D warnings + cargo test --lib. Always go via PR. Don't push to upstream
  `potter420/crimson-rs`; origin is `bbfox0703/crimson-rs`.
- **Avalonia 12 quirks**:
  - `Avalonia.Controls.DataGrid` is at 12.0.0; core is at 12.0.3 — pin DataGrid explicitly.
  - `Avalonia.Diagnostics` 12.x not released yet.
  - `Tmds.DBus.Protocol` transitive CVE — handled by `NuGetAuditMode=direct`.
- **MVVM partial-property syntax** didn't pick up the source-generated impl
  in CommunityToolkit.Mvvm 8.4.0 on .NET 10. Using field-based
  `[ObservableProperty] private T _name;` instead. Revisit when the
  toolkit catches up.
- **xUnit v3** — analyzer warnings `CA1707` (underscores in test names)
  and `xUnit1051` (TestContext cancellation) are intentionally suppressed
  in the test csproj only.
- **trailing_pad** — `Vec<u8>`, 1..=16 byte cap. Captures engine bytes that
  the schema doesn't model so they round-trip; bigger residuals still
  surface in `undecoded_ranges`.

## How to verify state on a fresh checkout

```powershell
# 0. Fetch vendor deps
.\vendor\update_vendors.ps1

# 1. Rust side
Push-Location D:\Github\crimson-rs
cargo test --lib
cargo clippy --all-targets --lib -- -D warnings
cargo test --lib --features c_abi
cargo clippy --all-targets --lib --features c_abi -- -D warnings
Pop-Location

# 2. Python toolchain + crimson_rs as Python module
.\scripts\setup_python_env.ps1
.\.venv\Scripts\python.exe .\tools\extract\extract_save.py --out .\out\save-extract\
.\.venv\Scripts\python.exe .\tools\inspect\inspect_save_body.py

# 3. C# end-to-end
.\scripts\build_rust.ps1          # builds vendor/crimson-rs --features c_abi
.\scripts\build_ui.ps1 -Test      # builds C# + runs xUnit tests (incl. live-save smoke)
.\scripts\package_aot.ps1 -SkipRustBuild   # AOT publish to dist/win-x64/
```

Each step should be green. If anything fails, fix it before touching
new code — drift is harder to chase later.
