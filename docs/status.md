# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-05-12 (end of session that completed UI polish for
> field inspection: handle caching + filter + drill-down + Windows
> save-folder default).

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
  - `crimson_save_free`
  - 53 tests pass (52 existing + `c_abi::tests::c_abi_smoke` covering
    every entry point including `get_block_json` two-call sizing,
    NUL terminator, required keys, child/elements/class_name presence,
    out-of-range).
- **Latest main**: `df037b8`. PRs landed this session: #14, #15, #16.
- **Branch model**: dev → PR → main (rebase merge, linear history).
  After merge, local + origin/dev get force-reset to match main.

### `CrimsonAtomtic` (this repo)

- **Foundation** (CLAUDE.md, docs/, .gitignore, vendor/, scripts/) — done.
- **C# / Avalonia scaffolding** — done. Builds clean, 9/9 unit tests pass:
  ```
  src/CrimsonAtomtic.Core         IPlatformPaths, ISingleInstanceGuard
  src/CrimsonAtomtic.SaveModel    SaveSummary, BlockSummary,
                                  BlockDetails, DecodedFieldRow +
                                  AOT JsonSerializerContext
  src/CrimsonAtomtic.Ui/Platform  WindowsPlatformPaths
                                  (resolves %LOCALAPPDATA%-based paths)
  src/CrimsonAtomtic.RustInterop  ISaveLoader + NativeSaveLoader
                                  (LibraryImport + SafeHandle wrapper
                                  around crimson_save_* C ABI).
                                  IDisposable; caches a live handle for
                                  the currently-open save.
  src/CrimsonAtomtic.Ui           Avalonia 12 / .NET 10, PublishAot=true,
                                  Mutex single-instance, MainWindow with
                                  File menu + blocks DataGrid + lazy
                                  field-detail pane (GridSplitter) +
                                  fields filter + breadcrumb-based
                                  drill-down into locator children and
                                  list elements. All DataGrids have
                                  resizable columns.
  src/CrimsonAtomtic.Tests        xUnit v3 — 9 tests against
                                  NativeSaveLoader incl. live-save
                                  summary, block-details, cache reuse,
                                  Dispose lifecycle, nested data
                                  reachability (skip cleanly when no
                                  save present)
  ```
  The UI now reads **real saves** via `crimson_rs.dll`. Open Save
  defaults to `%LOCALAPPDATA%\Pearl Abyss\CD\save\<user>\` when there's
  a single user; clicking a row in the blocks DataGrid lazily loads its
  per-field decode via `LoadBlockDetails` →
  `crimson_save_get_block_json` → System.Text.Json (source-generated,
  AOT-safe). Drill-down composes recursively
  (`InventorySaveData › inventorylist[18] › [14]:
  InventoryElementSaveData › itemList[40] › …`).
- **AOT publish verified**: `dotnet publish -c Release -r win-x64
  -p:PublishAot=true` (csproj pins `TrimMode=full`; we don't pass
  `-p:PublishTrimmed=true` because it upgrades Avalonia DataGrid 12.0.0
  trim warnings into ilc-blocking errors) produces a working bundle in
  `dist/win-x64/` (CrimsonAtomtic.exe ~21 MB, crimson_rs.dll 1.2 MB,
  Avalonia native deps).
- **Python tools** (unchanged, working): `tools/extract/extract_save.py`,
  `tools/inspect/inspect_save_body.py`, `tools/inspect/inspect_save_section.py`.
- **Vendor**: `vendor/crimson-rs` at `df037b8`. Refresh via
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

The C ABI is now read-everything. Field inspection is feature-complete
for inspection (handle caching, filter, drill-down composing through
locator children + list elements, resizable DataGrid columns, default
save folder, breadcrumb navigation). The save editor is read-only —
the next big step is making it actually edit.

1. **Save file writing** — re-encrypt + write back. Needs three
   landings:
   - **PR A — Rust + C ABI scalar field mutation**. Narrow scope:
     in-place byte patch over fields whose `kind` is `fixed_prefix` or
     `fixed_suffix`, validated by current byte length. No
     ObjectBlock re-serializer; the body stays as `Vec<u8>` and we
     overwrite the field's recorded `start..end` range. After a
     successful set, re-decode all blocks (O(N), ms-scale on 1112
     blocks) so the next `get_block_json` returns the new value.
     Adds: error codes `NOT_SCALAR`, `LENGTH_MISMATCH`, `WRITE_FAILED`;
     `crimson_save_set_scalar_field(handle, block_idx, field_idx,
     bytes, bytes_len)`; `crimson_save_write_to_file(handle, path)`
     (wraps the existing `Save::write_with_nonce`). Tests: mutate
     `_currentHp`, write to temp file, reparse, verify the new value
     round-trips.
   - **PR B — Length-changing edits**. List add / remove / reorder,
     inline-byte resize. Needs an ObjectBlock re-serializer (mirror
     of `decoder.rs`) and a body re-emit path that re-computes TOC
     offsets. Hard; defer until the editing UX actually demands it.
   - **PR C — C# typed wrappers + "Save" / "Save As…" command in the
     UI**. ISaveLoader grows a writer-side counterpart (perhaps
     `ISaveMutator`). DataGrid cell-level editing for scalar fields.
     Wires PR A's mutation API to typed setters per scalar variant.

2. **Asset / icon pipeline** — one-time mine icons from
   `D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`, run through
   a thumbnail pipeline (32 / 64 / 128), ship as a starter
   `IconCache/` in the AOT bundle.

3. **Localization** — read game `paloc/*.paloc` via crimson-rs to
   resolve item / skill / region names in the user's chosen game
   language (independent of UI shell language).

4. **Save backup management** — auto-backup on every load + on every
   write, configurable retention.

5. **Mod awareness** — read `CDMods/cdumm.db` (SQLite) and
   `mods/_enabled/` to surface mod-added items without crashing on
   unknown keys.

6. **Cross-platform save paths** — Wine/Proton prefix resolution on
   Linux/macOS. Currently `IPlatformPaths` is Windows-only.

7. **Avalonia.Diagnostics 12.x** — add back behind a `Debug`
   condition once published.

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
