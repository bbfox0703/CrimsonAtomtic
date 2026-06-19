# Status / session handoff

> **Read this first on a new session.** Lean by design — it carries the
> current state, the next task, the active backlog, and the gotchas worth not
> relearning. The full append-only session-by-session history and the
> long-form investigations live in
> **[status-archive.md](status-archive.md)** — look there only when you need
> the deep history behind a decision.
>
> Last updated: **2026-06-19** — editor aligned to game **1.12** (v1.12.01),
> verified locally. Release is **pending crimson-rs 1.12 landing on `main`**
> (see the release-boundary note in Current state).

## Current state

- **Editor v1.12.01**, aligned to live game **1.12** (`VerMinor` 11 → 12,
  `VerPatch` reset to 1 per the lock-step `VerMinor == ParserTargetMinor`
  convention). Verified locally this session; **NOT yet released** — see the
  release-boundary note below. The release flow (annotated `v*` tag → CI
  single-file AOT exe + bilingual notes → human clicks **Publish**) is
  unchanged; see [release-process.md](release-process.md).
- **Save read/write is version-agnostic.** Each save embeds its own schema, so
  1.05–1.12 saves round-trip in their own format (no version conversion). 1.12
  brought **no save-body drift** (format still v2 / flags `0x0080`). Verified
  this session: the live C# loader suite (slot0/1/2) round-trips clean, and the
  new-format `slot106` / `slot107` parse `hmac_ok` with `undecoded_bytes=0`
  (1107 blocks, 3098/3098 fields decoded) and re-seal decode-stable.
- **Name/icon resolution targets the *installed* game.**
  `GameDataVersion.ParserTargetMinor = 12`, `CompatibleMinors = {12}`. 1.12
  drifted the iteminfo schema (+150 items → 6,483; four byte-perfect layout
  changes) and the `partprefabdyeslotinfo` dye table (−143 rows → 968), so
  1.11-and-earlier installs no longer round-trip against this parser and are
  warned at startup. Full per-field breakdown in
  [game-versions.md](game-versions.md).
- **⚠️ Release-boundary note: crimson-rs 1.12 is NOT yet on `main`.** The 1.12
  parser lives on the source repo `D:\Github\crimson-rs` **`dev`** (commit
  `0694dfb`, "feat(1.12): … iteminfo + dye-slot + save mutation"); it is not
  pushed/merged. For this session the gitignored `vendor/crimson-rs` was synced
  from that local `dev` (fetch + `reset --hard`, no tracked files touched) so
  the build picks up 1.12. **Before tagging a 1.12 release**, land `0694dfb` on
  `bbfox0703/crimson-rs` `main` via PR — CI clones `main`, so a release built
  before that lands would silently ship the 1.11 parser.
- **Health:** 346 tests pass (0 skipped — live-install + catalog tests parse
  the real 1.12 iteminfo). Debug build clean (0/0). AOT publish verified this
  session (single self-contained `CrimsonAtomtic.exe`, `crimson_rs` staticlib
  folded in).

## Feature ledger

The shipped editor surface (generic block/field editor, inventory, sockets,
dye, sealed-abyss, abyss-gates, mount-unlock, knowledge, vendor-buyback,
mercenary-rename, browsers, 32 key-resolver bridges, …) is listed in the
[README](../README.md#editor-features-current). Deep design notes per feature
are in [status-archive.md](status-archive.md).

## Open work / backlog

- **`ParserTargetMinor` / `CompatibleMinors` are still hard-coded C#-side** —
  this was the *fifth* manual bump (8→9→10→11→12). Promote to a
  `crimson_parser_target_gamedata_minor()` + compatible-set C ABI so Rust is
  the single source of truth and the values stop being duplicated. Friction
  recurs every patch; do it next time this is touched.
- **🐞 World Map parchment composite layer-alignment bug** (from the
  2026-05-17 part-14 work) — the `blur_height` and `road_sdf` layers disagree
  on world coverage, so roads land in the wrong places relative to the
  coastline. Iteration was paused; the likely fix is to validate per-layer
  world ranges or fall back to the 785-tile terrain composite. Still open.
- **Feature-parity backlog vs the reference editor**
  (NattKh's `CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`) — features we only do
  via the generic field tree (🔸) or not at all (❌): ItemBuffs (iteminfo
  stats/buffs/enchant/transmog), Stores prices/stock, DropSets loot tables,
  SpawnEdit, Skills params (`skill.pabgb`), FieldEdit
  (`fieldinfo`/`vehicleinfo`), inventory storage expansion, dedicated quest /
  equipment-enchant editing, item-pack share/import/export, full reveal-map.
  Most need a new crimson-rs parser first. Full table in the archive.
- **Name resolution gaps** (deferred until prioritised): `MissionKey` /
  `QuestKey` / `KnowledgeKey` (large) / `SkillKey` / field-NPC / learned-skill
  names aren't resolvable from PALOC today — see the gotchas + archive for why
  each is intentionally left blank rather than mislabelled.

## Gotchas — don't relearn these

Condensed; the exhaustive list (PALOC keying internals, per-TypeName
name-resolution coverage, nested-edit path mechanics, fill-stack regimes,
window-restore quirks, etc.) is in
[status-archive.md → "Important context / gotchas"](status-archive.md).

- **Foundation-first.** When parsing produces wrong data, fix the parser or
  schema — never add a workaround in a consumer. (CLAUDE.md rule 12.)
- **Old saves are the same format** (`version=2 / flags=0x0080`, HMAC ok,
  0 undecoded bytes). Block-count drift across slots is gameplay-driven, not
  format-driven.
- **Scalar-only mutation + length-changing ops.** The C ABI mutates
  fixed-size scalars in place; list clone/insert/remove and inline-bytes
  resize are supported via the dedicated ops (incl. the `marker_run_plus_zeros`
  list variant). Anything that changes block length re-encodes the block.
- **String getters use the two-call pattern** (null buffer → required size →
  allocate → fill). Same shape for class names and JSON blobs.
- **`get_block_json` is hand-rolled JSON** (no serde in the cdylib); C# parses
  with a source-generated `JsonSerializerContext` to stay AOT-safe. Field
  values are pre-formatted in Rust — don't reformat in C#.
- **PALOC names are keyed by `(typeByte, upper32-of-u64)`**, scanned once per
  language into a dictionary. iteminfo `string_key` is the internal id (a
  fallback for the ~71 dev items without a `0x70` entry), NOT a PALOC key.
  Empty name cells can be real data (no localized name), not a bug.
- **InventoryKey labels are hard-coded** (`LocalizationProvider.InventoryContainerLabels`)
  — no PALOC namespace. Re-run `Probe_InventoryKeyContainers` if a patch
  shifts the layout.
- **Saves preserve the original file's last-write timestamp** (Steam Cloud +
  the in-game picker sort by mtime). Save As re-anchors the working doc to the
  new path.
- **AOT publish is fragile about the linker.** `<NoWarn>IL2104;IL3053</NoWarn>`
  in the Ui csproj is load-bearing (Avalonia DataGrid 12 roll-up warnings).
  `scripts/package_aot.ps1` only opts into `IlcUseEnvironmentalTools` when the
  `link.exe` on PATH is the **MSVC** linker — a bare `Get-Command link.exe`
  matches Git-for-Windows' GNU coreutils `link` on the CI runner, which makes
  ILC choke on `/DEF: @link.rsp` (this exact trap failed the first v1.11.01
  build). On a clean CI runner, let ILC auto-discover MSVC via vcvars.
- **crimson-rs is read-only here** and CI **clones it fresh from GitHub
  `main`** (the local `vendor/` is gitignored, not used by CI). Land any
  crimson-rs fix on `bbfox0703/crimson-rs` `main` *before* tagging a release.
  `main` is branch-protected (clippy `-D warnings` + `cargo test`); always go
  via PR; never push to upstream `potter420/crimson-rs`.
- **Run tests with `dotnet run --project src/CrimsonAtomtic.Tests`** — this SDK
  rejects the legacy `dotnet test` VSTest path for MTP.
- **Avalonia 12 quirks**: DataGrid pinned at 12.0.0 (core is ahead);
  `Avalonia.Diagnostics` 12.x not released; MVVM uses field-based
  `[ObservableProperty]` (partial-property syntax didn't generate on
  CommunityToolkit 8.4 / .NET 10).

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

Each step should be green. If anything fails, fix it before touching new code
— drift is harder to chase later.

## Session changelog (newest first)

One line per milestone; full detail in [status-archive.md](status-archive.md).

- **2026-06-19 — game 1.12 alignment (v1.12.01, unreleased)**: third
  consecutive iteminfo schema drift (+150 items → 6,483; four byte-perfect
  layout changes) + `partprefabdyeslotinfo` dye-table drift (−143 rows → 968),
  no save-body change. Synced the gitignored `vendor/crimson-rs` from local
  `crimson-rs` `dev` `0694dfb` (not yet on `main`), rebuilt the native lib,
  bumped `ParserTargetMinor` / `CompatibleMinors` / `VerMinor` to 12, refreshed
  the paver tests. 346 tests green against the real 1.12 install;
  `slot106` / `slot107` verified `hmac_ok` + `undecoded_bytes=0` +
  decode-stable. Release deferred until crimson-rs 1.12 lands on `main`.
- **2026-06-12 — v1.11.01**: aligned editor to game 1.11 (iteminfo `u8`
  drift, no save-body change); rebuilt native lib; bumped NuGet packages +
  fixed the ILCompiler-pin / CI `link.exe` traps; refined zh-TW translations;
  cut the v1.11.01 release.
- **2026-06-09 — v1.10.01**: version-sync convention
  (`VerMinor == ParserTargetMinor`); broad Tools-menu dialog localization pass
  (en/ja/zh-TW, ~710 keys); 4 UX fixes (localized warnings, Browse-Items
  "go to item", restore-no-double-backup).
- **2026-06-05 — game 1.10**: first iteminfo schema drift since the 1.05/1.06
  `ItemSaveData` change (−`money_icon_path`, +`UnitData.unk_post_icon_path`) +
  the `ContentsMiscSaveData` leading-pad save-body fix.
- **2026-05-31 — feature wave**: Mount-Unlock dialog (sigil grant + dragon
  element/knowledge transplant), Faction-node editor, Knowledge editor,
  discoverable Add-Item flow, bulk-fill caps, Sealed-Abyss preview dialog,
  Add-Item localization, mercenary-name read-back FFI.
- **2026-05-29 — game 1.09**: content-only (no schema drift);
  `CompatibleMinors` became an allow-list.
- **2026-05-22→23 (parts 15–17)**: staticlib pivot for single-file AOT
  publish (`crimson_rs.dll` folded into the exe); type-byte discovery harness;
  1.08 baseline.
- **2026-05-14→18 (parts 1–14)**: initial editor build-out — save
  load/decode/mutate/write, generic block/field tree editor, scalar-path
  editing, name-resolver bridges, inventory / sockets / dye editors,
  multi-language localization + PALOC pipeline, icon pipeline, World Map view.
- **earlier**: crimson-rs reverse-engineering + Python toolchain foundation
  (save format, PABGB family, PAZ containers).
