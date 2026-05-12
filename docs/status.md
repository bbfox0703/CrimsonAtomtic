# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-05-12 (end of session that scaffolded the C# app).

## Where we are

### `vendor/crimson-rs` (Rust core)

- **Save format**: fully decoded against a live 1.06 save (slot0).
  - `Save::parse` (header + crypto + LZ4) — done
  - `Body::parse` (schema + TOC) — done
  - `Body::decode_blocks` (per-object field decoder, all 8 `meta_kind` values) — done
  - **0 undecoded bytes / 5,172,506 total.** Every byte either decoded into a
    typed field or captured as `ObjectBlock.trailing_pad`.
  - Hard test invariants: `total_undecoded == 0` and
    `total_decoded_fields == total_present_fields`. A future game-patch
    drift will fail loudly, not silently.
- **Currently exposed via**: PyO3 bindings (used by `tools/`). **No C ABI yet.**
- **Latest main**: `1aad9d7`. PRs landed this session: #10, #11, #12, #13.
- **Branch model**: dev → PR → main (rebase merge, linear history).
  After merge, local + origin/dev get force-reset to match main.

### `CrimsonAtomtic` (this repo)

- **Foundation** (CLAUDE.md, docs/, .gitignore, vendor/, scripts/) — done.
- **C# / Avalonia scaffolding** — done. Builds clean, 4/4 unit tests pass:
  ```
  src/CrimsonAtomtic.Core         IPlatformPaths, ISingleInstanceGuard
  src/CrimsonAtomtic.SaveModel    SaveSummary + AOT JsonSerializerContext
  src/CrimsonAtomtic.RustInterop  ISaveLoader + PlaceholderSaveLoader
  src/CrimsonAtomtic.Ui           Avalonia 12 / .NET 10, PublishAot=true,
                                  Mutex single-instance, MainWindow with
                                  File menu + DataGrid
  src/CrimsonAtomtic.Tests        xUnit v3
  ```
  Currently the UI shows **canned data** from `PlaceholderSaveLoader`. The
  composition root in `App.axaml.cs` is the swap point for the real loader.
- **Python tools** (working): `tools/extract/extract_save.py`,
  `tools/inspect/inspect_save_body.py`, `tools/inspect/inspect_save_section.py`.
- **Vendor**: `vendor/crimson-rs` cloned at `1aad9d7`. Refresh via
  `.\vendor\update_vendors.ps1`.

## Pick up here (next concrete task)

**Add a C ABI to `vendor/crimson-rs`** so the C# `ISaveLoader` has a real
implementation and `PlaceholderSaveLoader` can be swapped out.

### In `D:\Github\crimson-rs` (source repo)

1. Add a `c_abi` Cargo feature in `Cargo.toml`.
2. Create `src/c_abi/mod.rs` with `extern "C"` exports. Design:
   - **Handle-based**: `crimson_save_load_from_file(path: *const c_char, out_handle: *mut SaveHandle) -> i32`.
   - **Free function**: `crimson_save_free(handle: SaveHandle)`.
   - **Scalar getters** (no allocation): `crimson_save_get_version`, `…_get_hmac_ok`, `…_get_payload_size`, `…_get_uncompressed_size`, `…_get_schema_type_count`, `…_get_toc_entry_count`.
   - **Block info**: `crimson_save_get_block_info(handle, index, *mut BlockInfo) -> i32` returns a flat struct (class_index, data_offset, data_size, fields_present, fields_decoded).
   - **Class name**: `crimson_save_get_block_class_name(handle, index, out_buf: *mut u8, buf_len: usize) -> i32` writes a NUL-terminated UTF-8 string; returns required length if `buf_len` too small.
   - Error codes: `0 = success`, negative integers per category (see `SaveError` enum for parallels).
3. Build with `cargo build --release --features c_abi`. Verify
   `target/release/crimson_rs.dll` exports the symbols
   (`dumpbin /exports crimson_rs.dll` or equivalent).
4. PR to main, merge with rebase, force-push dev.

### In `CrimsonAtomtic` (this repo)

5. Refresh vendor: `.\vendor\update_vendors.ps1`.
6. Update `scripts/build_rust.ps1` to build with `--features c_abi` and
   to copy the resulting `crimson_rs.dll` to a known location.
7. Add `src/CrimsonAtomtic.RustInterop/NativeSaveLoader.cs`:
   - Use `[LibraryImport]` source generators (AOT-safe).
   - Wrap handle lifetime in a `SafeHandle` subclass.
   - Marshal the path as UTF-8 (Avalonia passes UTF-16; convert at the boundary).
8. Update `src/CrimsonAtomtic.Ui/App.axaml.cs` composition root to construct
   `NativeSaveLoader` instead of `PlaceholderSaveLoader`.
9. Update `scripts/package_aot.ps1` so the published bundle includes
   `crimson_rs.dll` next to `CrimsonAtomtic.exe`.
10. Add a smoke-test that loads a real save through the C ABI (gated on
    `%LOCALAPPDATA%\Pearl Abyss\CD\save\…` existing, like the Rust tests).
11. Commit + push CrimsonAtomtic.

### Verification on done

- `dotnet test` still passes (now exercising real Rust under the hood).
- The UI launches, File → Open Save reads a real `.save`, the DataGrid
  shows the same 1,112 block summaries we get from Python tools.
- `dotnet publish -c Release -r win-x64 -p:PublishAot=true` succeeds
  (full AOT trim).

## Roadmap (priority order after C ABI)

1. **Open / display real saves** (the C ABI lets `PlaceholderSaveLoader` retire).
2. **Field-level inspection in the UI** — click a block → see its decoded fields. Mirror of `inspect_save_section.py --pretty` in C#.
3. **Item inventory editing** — typed mutation API on `InventorySaveData.ItemSaveData` (set stack count, change item key, swap slots). Round-trip needs save *write* path which currently doesn't exist in `crimson-rs` either.
4. **Save file writing** — re-encrypt + write back via crimson-rs. The save crypto already has `Save::write_with_nonce`; need body re-serializer for any mutated block.
5. **Asset / icon pipeline** — one-time mine icons from the old reference repo, run through a thumbnail pipeline (32 / 64 / 128), ship as a starter `IconCache/` in the AOT bundle.
6. **Localization** — read game `paloc/*.paloc` via crimson-rs to resolve item / skill / region names in the user's chosen game language (independent of UI shell language).
7. **Save backup management** — auto-backup on every load + on every write, configurable retention.
8. **Mod awareness** — read `CDMods/cdumm.db` (SQLite) and `mods/_enabled/` to surface mod-added items in the save editor without crashing on unknown keys.
9. **Cross-platform save paths** — Wine/Proton prefix resolution on Linux/macOS. Currently `IPlatformPaths` is Windows-only.
10. **Avalonia.Diagnostics 12.x** — add back behind a `Debug` condition once published. (11.3.15 is the latest as of 2026-05.)

## Important context / gotchas (don't relearn these)

- **Data policy** ([docs/data-policy.md](data-policy.md)) — never commit
  derived data. Old reference repo (`D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`)
  is one-time mining only; the only ongoing external dependency is
  `vendor/crimson-rs`.
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
Pop-Location

# 2. Python toolchain + crimson_rs as Python module
.\scripts\setup_python_env.ps1
.\.venv\Scripts\python.exe .\tools\extract\extract_save.py --out .\out\save-extract\
.\.venv\Scripts\python.exe .\tools\inspect\inspect_save_body.py

# 3. C# scaffolding
.\scripts\build_ui.ps1 -Test
```

Each step should be green. If anything fails, fix it before touching
new code — drift is harder to chase later.
