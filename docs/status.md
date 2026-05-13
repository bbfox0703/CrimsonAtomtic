# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-05-13 (post-localization polish session —
> PALOC lookup-encoding bug fixed, element-list now carries
> ItemKey + Item Name columns with a live filter, nested locator
> children resolve too, DataGrid columns use fixed widths +
> horizontal scroll, window restore on multi-monitor fixed.
> Faction-name resolution scoped + deferred to next session).

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
    `NULL_ARG`, `INVALID_PATH`.
  - **iteminfo bridge C ABI** (`crimson_iteminfo_*`): `load_from_file`
    / `load_from_bytes` / `free` / `entry_count` /
    `lookup_string_key(u32)` (two-call) / `get_entry(idx, *out_key, …)`
    (two-call). Handle owns a `HashMap<u32, String>` built from the
    existing `item_info` parser — only `(key, string_key)` retained;
    the other 100+ fields dropped. ~200 KB resident for 1.06's
    ~6,400 items.
  - 67 tests pass (52 base + 15 c_abi tests across save, paloc, paz, iteminfo).
- **Latest main**: `446ad1f`. PRs landed across recent sessions:
  #14, #15, #16, #17, #18 (path-addressed scalar mutation),
  #19 (PALOC C ABI), #20 (one-shot PAZ extraction),
  #21 (iteminfo bridge).
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
  src/CrimsonAtomtic.Tests        xUnit v3 — 51 tests:
                                  16 NativeSaveLoaderTests + 25
                                  ScalarFieldEditingTests (as before) +
                                  3 PalocCatalogTests + 4
                                  PazExtractorTests +
                                  3 ItemInfoCatalogTests
                                  (live-install get_entry / lookup
                                  round-trip, garbage bytes → BODY_PARSE,
                                  post-Dispose throws). Live tests
                                  skip cleanly when the game install
                                  / save isn't present.
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
- **Vendor**: `vendor/crimson-rs` at `3eff882`. Refresh via
  `.\vendor\update_vendors.ps1`.

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

The localization story is now visibly complete: item names show
beside u32 IDs both in the field view (`_itemKey` rows) and the
element list (`itemList[14]` and `EquipSlotElementSaveData → item`
nested case), the fields filter and the element-list filter both
match against item-name / ItemKey, and the user can pick a secondary
language at runtime. DataGrids carry fixed column widths + a
horizontal scrollbar so adding columns doesn't squeeze the rest.
Window restore-from-maximize on multi-monitor setups now lands back
on the originating monitor.

Suggested next steps, in order of expected user value:

1. **Faction (and other key namespaces) name resolution.** The user
   asked about `FactionSaveData._ownerFactionKey = 1000063` — can it
   resolve to a faction display name? Currently no; only `ItemKey`
   does. Same plumbing should generalise to `FactionKey`,
   `SkillKey`, `CharacterKey`, `GimmickInfoKey`, … but each lives at
   a different PALOC **type byte** (item names are at `0x70`; the
   others are unknown). Plan:
   - **Discover** the type byte for factions by scanning the loaded
     English PALOC for entries whose `sid >> 32` matches a known
     faction key (e.g. 1000063) and noting `sid & 0xFF`. A throwaway
     C# helper or a one-shot run in `crimson-rs/scripts/` works.
   - **Generalise** `LocalizationProvider._itemNamesByLang` from
     `Dictionary<lang, Dictionary<uint, string>>` to
     `Dictionary<lang, Dictionary<(byte typeByte, uint key), string>>`
     (or one map per type byte).
   - **Add** `ResolveName(uint id, byte typeByte, lang)` +
     per-namespace helpers (`ResolveFactionName` etc.).
   - **Wire** `FieldRowViewModel` + `ElementRowViewModel` — switch
     on `row.TypeName` (`ItemKey` → 0x70, `FactionKey` → ??, …).
   ~1 hour once the type bytes are known.

2. **Length-changing edits (PR B)**. List add / remove / reorder,
   inline-byte resize. Needs an `ObjectBlock` re-serializer (mirror
   of `decoder.rs`) and a body re-emit path that re-computes TOC
   offsets. Hard but unlocks adding inventory items (not just
   editing existing ones).

3. **Multi-level path tests on the C# side**. The current
   `SetScalarField_NestedPath_RoundTripsThroughWriteToFile` test
   exercises one descent step. Worth a two-step regression
   (`inventoryList[N].itemList[M].count`-shaped) — the Rust c_abi
   test already covers any one-step-reachable scalar, so the gap is
   only on the C# side.

4. **Asset / icon pipeline** — one-time mine icons from
   `D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`, run through
   a thumbnail pipeline (32 / 64 / 128), ship as a starter
   `IconCache/` in the AOT bundle.

5. **Save backup management** — auto-backup on every load + on every
   write, configurable retention. (Valuable now that the UI can
   overwrite a save in place at any nav depth.)

6. **Mod awareness** — read `CDMods/cdumm.db` (SQLite) and
   `mods/_enabled/` to surface mod-added items without crashing on
   unknown keys.

7. **Cross-platform save paths** — Wine/Proton prefix resolution on
   Linux/macOS. Currently `IPlatformPaths` is Windows-only.

8. **Avalonia.Diagnostics 12.x** — add back behind a `Debug`
   condition once published.

9. **Re-evaluate the DataGrid AOT warning suppression** — the
   `<NoWarn>IL2104;IL3053</NoWarn>` in
   `CrimsonAtomtic.Ui.csproj` is a workaround for Avalonia
   DataGrid 12.0.0 internals. Drop it once Avalonia ships a
   trim-safe DataGrid (12.1+?) so future internal-reflection
   regressions in our own code can't hide under it.

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
- **Item-name resolution today only knows the `0x70` type byte.**
  Faction / skill / character / gimmick keys each presumably live
  at a different type byte that we haven't identified yet. "Pick up
  here" #1 covers the generalisation.
- **`ElementRowViewModel` walks one locator level.** When the
  element directly carries an `ItemKey`-typed field
  (e.g. `ItemSaveData._itemKey`), we use it. When it doesn't
  (e.g. `EquipSlotElementSaveData._item` → locator into
  `ItemSaveData`), we descend one level into inline locator
  children. Deeper paths aren't covered — if a future schema needs
  it, add recursion in `FindItemKeyInChildren`.
- **PALOC discovery probes a fixed code list.** The 14 codes (eng,
  kor, jpn, zho-tw, zho-cn, ger, fra, spa, por, rus, tur, tha, ind,
  ara) cover every language Crimson Desert 1.06 ships. If a future
  patch adds one, add it to `KnownLanguageCodes` in
  `LocalizationProvider`; otherwise the picker won't surface it.
  Discovery itself runs synchronously at app launch (probes
  `0019..0050`); first-launch cost is ~1 s on SSD because each
  successful probe also caches the catalog. Subsequent launches
  re-probe — settings only persists the user's preferred secondary,
  not the discovery result.
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
- **Window position/size restore is snapshot-based.** Avalonia 12
  on Windows can land a restored-from-maximized window straddling
  two monitors on multi-display setups. `MainWindow` snapshots
  `Position` (via `PositionChanged` — it's not an AvaloniaProperty)
  and `Width` / `Height` (via `OnPropertyChanged` for the standard
  AvaloniaProperties) while in `WindowState.Normal`, then re-applies
  them on the Maximized → Normal transition. Don't rely on the OS
  to round-trip the window rect correctly.
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
