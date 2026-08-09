# Status / session handoff

> **Read this first on a new session.** Lean by design — it carries the
> current state, the next task, the active backlog, and the gotchas worth not
> relearning. The full append-only session-by-session history and the
> long-form investigations live in
> **[status-archive.md](status-archive.md)** — look there only when you need
> the deep history behind a decision.
>
> Last updated: **2026-08-09** — editor aligned to game **1.17**, a
> **content-only** patch over 1.16: the structural 1.16 drift was a one-off and
> 1.17 goes back to the 1.14/1.15 pattern. No schema drift in any subsystem —
> iteminfo lost nine items (6,581 → 6,572) with the layout untouched, and
> `skill.pabgb` is byte-identical. crimson-rs 1.17 is vendored from `main`
> (tag `v1.0.17.x`, commit `0767361`, merge `dcf3a42`). The C# side needed only
> the manual `VerMinor` 16→17 lock-step bump plus the `NativePaverReaderTests`
> version-pin refresh — **no** count/value pin moved, and the C ABI surface is
> unchanged. 381 C# tests green, 0 skipped. **Next concrete task: commit on
> `dev`, merge to `main`, tag `v1.17.01`.** (v1.16.01 was published on
> 2026-08-01 and is the current Latest release.)

## Current state

- **Editor aligned to live game 1.17 — not yet committed/tagged.** `VerMinor`
  16 → 17, `VerPatch` stays 1 per the lock-step `VerMinor == ParserTargetMinor`
  convention (`VerMinor` is a **manual** build-identity bump, while
  `ParserTargetMinor` is **ABI-sourced**). Verified locally: 381 C# tests
  green, 0 skipped, against the real 1.17 install. **Next concrete task:
  commit on `dev` → merge to `main` → tag `v1.17.01`**; see
  [release-process.md](release-process.md). The shipped Latest release is still
  v1.16.01 (tagged + **published** 2026-08-01).
- **1.17 is a CONTENT-ONLY patch over 1.16** — the structural 1.16 drift was a
  one-off; 1.17 returns to the 1.14/1.15 shape. iteminfo went 6,581 →
  **6,572** items: nine `Item_Set_*_Tier0_Reminiscence` keys (1004912–1004920)
  removed, none added, **layout untouched**. The proof it isn't a layout drift
  hiding behind value changes: of the 6,572 survivors 6,435 are byte-identical,
  137 changed values, and **zero changed size** — and the −5,652 B file delta
  is exactly the sum of the nine removed items' spans. `skill.pabgb` /
  `.pabgh` are byte-identical to 1.16 (2,013/2,013 entries parse). gamedata
  keys: `gimmickinfo` +1 (13,690), `itemgroupinfo` −1 (1,596), other 28 tables
  key-identical. The only crimson-rs code change was the version pin.
- **Save read/write is version-agnostic.** Each save embeds its own schema, so
  1.05–1.17 saves round-trip in their own format (no version conversion). 1.17
  brought **no save-body drift** (format still v2 / flags `0x0080`; the live
  `slot107` 1.17 save decodes 1,107 blocks / 3,097 fields with
  `undecoded_bytes=0`). Verified this session: the live C# loader suite
  round-trips clean (all 381 C# tests ran with 0 skipped; iteminfo catalog
  parses the real 1.17 data, now 6,572 items).
- **The C ABI surface did NOT change.** `CrimsonItemInfoSummary` is untouched
  and the 80-byte `Marshal.SizeOf` pin still holds — as it also did across the
  *structural* 1.16 drift, where `inventory_info` was re-sourced from
  `inventory_info_list[0]` entirely inside Rust. This is the payoff of the
  foundation-first rule.
- **Name/icon resolution targets the *installed* game.**
  `GameDataVersion.ParserTargetMinor` and `CompatibleMinors` are read from the
  crimson-rs C ABI (`crimson_parser_target_gamedata_minor()` → 17;
  `crimson_parser_compatible_gamedata_minors()` → {17}) — not hand-coded.
  Because 1.17 is content-only, the allow-list warns a 1.16 install purely by
  **convention** again (its layout would still parse) — unlike at 1.16, where
  the structural drift made the warning substantive. Full per-version
  breakdown in [game-versions.md](game-versions.md).
- **crimson-rs 1.17 is on `main`.** The 1.17 support is merged to
  `bbfox0703/crimson-rs` `main` (commit `0767361`, PR #88, merge `dcf3a42`)
  and tagged **`v1.0.17.x`** (vendored at `dcf3a42`). CI clones `main`, so a
  release cut ships the 1.17 parser. Reminder for the next patch: land the
  crimson-rs change on `main` *before* tagging a CrimsonAtomtic release.
- **Health:** full suite green this session (381 C# tests, 0 skipped, 0
  failures after the version-pin refresh — live-install + catalog tests parse
  the real 1.17 iteminfo, 6,572 items; the native lib was rebuilt from the
  vendored 1.17 crimson-rs so the ABI reports target minor 17). Only 3 tests
  were red pre-bump, all in `NativePaverReaderTests`. The
  `runtime.win-x64.Microsoft.DotNet.ILCompiler` central pin stays at 10.0.10
  (SDK 10.0.302, unchanged since 1.14).

## Feature ledger

The shipped editor surface (generic block/field editor, inventory, sockets,
dye, sealed-abyss, abyss-gates, mount-unlock, knowledge, vendor-buyback,
mercenary-rename, browsers, 32 key-resolver bridges, …) is listed in the
[README](../README.md#editor-features-current). Deep design notes per feature
are in [status-archive.md](status-archive.md).

## Open work / backlog

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
- **A byte-identical round-trip does NOT validate field boundaries.** Learned
  the hard way in the 1.16 RE: the first iteminfo model round-tripped
  *perfectly* while `respawn_time_seconds` decoded as `-4294967296` — a 4-byte
  misalignment that serialization happily reproduced. What caught it was the
  **value distribution** (0 / −1 / 604800 = 7 days). When modelling a drift,
  sanity-check decoded values against a plausible domain, not just `in == out`.
- **A field can move rather than vanish.** 1.16's head-side `inventory_info`
  looked deleted; it had actually relocated to the item end and widened to
  `[u16; 9]`, absorbing a neighbouring "constant" tail as slot 8. Before
  concluding a field was removed, look for it re-appearing elsewhere with a
  wider shape.
- **Conditional fields hide behind low-population discriminators.** 1.16's
  removed `DockingChildData::unk_post_summon_tag` is visible only on the 391
  items carrying `docking_child_data`, and the new `UnkPreRespawnData` list is
  non-empty on just 14 items (so it read as a flat 10-byte insert at first).
  A drift that appears fixed-size across most rows may be variable-size on a
  small subset — check the discriminator partitions the table fp=0/fn=0.
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
- **A vendor refresh does NOT rebuild the Python `crimson_rs` module.**
  `maturin develop` installs *editable* (a `.pth` pointing at
  `vendor/crimson-rs/python`), so after `update_vendors.ps1` the in-tree source
  and `crimson_rs.__file__` both look current while the compiled
  `python/crimson_rs/crimson_rs.pyd` is still whatever was last built — it was
  51 days stale (a 1.12-era build) at the 1.17 alignment. The C# editor is
  unaffected (it loads `crimson_rs.dll` from `scripts/build_rust.ps1`), but the
  `tools/` Python toolchain would silently parse new game data with an old
  schema. Re-run `scripts/setup_python_env.ps1` after every vendor refresh, and
  check the `.pyd` mtime — not `crimson_rs.__file__` — to tell whether it's
  current.
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

- **2026-08-09 — game 1.17 alignment (pending commit/tag)**: content-only over
  1.16 — the structural 1.16 patch was a one-off. iteminfo 6,581 → **6,572**
  items (nine `Item_Set_*_Tier0_Reminiscence` keys 1004912–1004920 removed,
  none added) with the **layout untouched**; the size-delta accounting is what
  proves it (6,435 of 6,572 survivors byte-identical, 137 value-only changes,
  **zero** size changes, and the −5,652 B file delta exactly equals the nine
  removed items' spans). `skill.pabgb` byte-identical to 1.16 (2,013/2,013
  parse). No save-body drift (v2 / flags `0x0080`; live `slot107` decodes
  1,107 blocks / 3,097 fields, undecoded 0/5,204,773). gamedata keys 30 tables
  / 96,076: `gimmickinfo` +1 (13,690, new key 1012695), `itemgroupinfo` −1
  (1,596, removed key 18566), other 28 key-identical. crimson-rs commit
  `0767361`, tag `v1.0.17.x`, merged to `main` via PR #88 (vendored at
  `dcf3a42`); its only code change was `PARSER_TARGET_GAMEDATA_MINOR` 16 → 17.
  C# side: manual `VerMinor` 16→17 lock-step bump + `NativePaverReaderTests`
  refresh (happy path pins the 1.17 paver `01 00 11 00 00 00 97 4c 5e d0` /
  build `0xd05e4c97`, previous-minor guard moved to 1.16, future-minor guard to
  1.18, ABI target pin 16→17). **No count/value pin moved** — unlike every
  alignment since 1.13, `Pyeonjeon_Arrow` `item_type` stayed 0 and the catalog
  tests passed untouched, so exactly 3 of 381 tests were red pre-bump and all
  381 are green after. C ABI surface unchanged.
- **2026-08-01 — game 1.16 alignment (v1.16.01, released)**: the
  content-only streak ended — 1.16 is the largest schema drift since 1.13 and
  the **first patch ever to break the skill parser**. iteminfo 6,508 → 6,581
  items with four layout drifts (head-side `inventory_info` removed;
  `DockingChildData::unk_post_summon_tag` removed; a 10 + 28·N
  `UnkPreRespawnData` block inserted before `respawn_time_seconds` with
  `unk_pre_max_endurance` swapped ahead of it; `inventory_info` relocated to
  the item end as `inventory_info_list: [u16; 9]`, absorbing the 1.13-era
  `unk_tail` as slot 8); skill 1,999 → 2,013 entries with
  `PostBuff::unk_pre_damage_type: u8` before `damage_type`. No save-body drift
  (v2 / flags `0x0080`; all 12 live slots `hmac_ok` / `undecoded_bytes=0`).
  crimson-rs commit `e81acc5`, tag `v1.0.16.x`, merged to `main` via PR #87
  (vendored at `92fc0e2`). Because every drift was absorbed in Rust **and the
  C ABI surface stayed unchanged** (`inventory_info` now reads
  `inventory_info_list[0]`, byte-identical to the old field), the C# side
  needed only the manual `VerMinor` 15→16 lock-step bump plus an expectation
  refresh: `NativePaverReaderTests` (happy path pins the 1.16 paver
  `01 00 10 00 00 00 e1 6d 1d 8d` / build `0x8d1d6de1`, previous-minor guard
  moved to 1.15, future-minor guard to 1.17) and `ItemInfoCatalogTests`
  (`Pyeonjeon_Arrow` `item_type` 23 → 0 — a game-side enum remap, the second
  for this key). All 381 C# tests green, 0 skipped, against the live 1.16
  install. Merged to `main` (PR #25) and **tagged `v1.16.01`** → CI AOT build
  green (run `30699496761`), draft release created and trimmed to the
  bilingual `## Highlights` / `## 重點` sections, then **published**
  2026-08-01.
- **2026-07-24 — game 1.15 alignment**: second content-only patch in a row
  (after 1.14 broke the 1.10→1.13 four-drift streak) — 1.15 changed item
  **values** but not the layout; the save body / skill / all 30 gamedata
  bridges parse unchanged, and the 30-table gamedata-key snapshot is
  byte-identical to 1.14 (95,185 keys, zero changes). crimson-rs bumped only
  `PARSER_TARGET_GAMEDATA_MINOR` 14→15 (commit `82d0bae`, tag `v1.0.15.x`,
  merged to `main` via PR #85/#86, vendored at `d2bc6bc`); because the C#
  `ParserTargetMinor` / `CompatibleMinors` are ABI-sourced, the editor
  alignment was just the manual `VerMinor` 14→15 lock-step bump plus the
  version-pin test refresh (`NativePaverReaderTests`: happy-path now pins the
  1.15 paver `01 00 0f 00 00 00 e1 88 84 6a` / build `0x6a8488e1`, the
  previous-minor guard moved to 1.14, and the "future minor" guard to 1.16).
  All 381 C# tests ran with 0 skipped and 0 failures after the refresh (native
  lib rebuilt from vendored 1.15). Committed on `dev`, merged to `main` (PR
  #23), then **released as v1.15.01** (annotated tag `v1.15.01` → CI AOT draft
  → published; bilingual release notes trimmed to the `## Highlights` / `## 重點`
  sections, matching prior releases).
- **2026-07-17 — game 1.14 alignment (v1.14.01)**: first content-only patch
  since the 1.10→1.13 run of four consecutive iteminfo schema drifts — 1.14
  changed item **values** but not the layout, and the save body / skill / all
  30 gamedata bridges parse unchanged (crimson-rs `v1.0.14.x`, vendored at
  `7cfe072`; only `PARSER_TARGET_GAMEDATA_MINOR` bumped 13→14). Because the C#
  `ParserTargetMinor` / `CompatibleMinors` are ABI-sourced (since 1.13), the
  editor alignment was just the manual `VerMinor` 13→14 lock-step bump plus a
  version-pin test refresh (`NativePaverReaderTests`: happy-path now pins the
  1.14 paver `01 00 0e 00 00 00 f8 42 7d 59` / build `0x597d42f8`, the
  previous-minor guard moved to 1.13, and the "future minor" guard to 1.15).
  Live 1.14 `slot107` parses `hmac_ok` / `undecoded_bytes=0`; all 381 C# tests
  ran with 0 skipped and 0 failures after the refresh. Also bumped the
  `runtime.win-x64.Microsoft.DotNet.ILCompiler` central pin 10.0.9 → 10.0.10
  (SDK moved to 10.0.302, whose auto-injected ILCompiler tripped NU1109 on the
  stale pin). Tagged **v1.14.01** (CI draft → human Publish).
- **2026-07-04 — window position memory + drift-free maximize/restore (all
  windows)**: ported UE5CEDumper's window-restore design. New pure, unit-tested
  services `WindowRestoreState` (deferred-commit snapshot state machine),
  `WindowPlacement` (off-screen visibility + centering), `WindowStateStore`
  (`%LOCALAPPDATA%\CrimsonAtomtic\window-state.txt`, AOT-safe key=value). The
  **main window** now (a) restores last-session position/size/maximized on
  restart — validated against the monitors present this session (a rect on a
  now-absent monitor is reset to centered-on-primary), wired via
  `MainWindow.AttachWindowState` in `App.axaml.cs` before the window shows — and
  (b) gained the previously-missing off-screen position guard + **deferred
  (Background) re-apply** + re-seed, so repeated maximize/restore no longer
  drifts or jumps to 0,0 (the old code re-applied synchronously mid-transition —
  the anti-pattern). **20 resizable child dialogs** attach the new
  `ManagedWindowRestore` helper (one line per ctor, no `.axaml` re-rooting) for
  the same drift-free maximize/restore; the 5 fixed-size dialogs are unchanged.
  +33 unit tests; smoke-verified end-to-end (restore-on-restart + save-on-close).
- **2026-07-04 — game 1.13 alignment (v1.13.01)**: fourth consecutive
  iteminfo schema drift (+25 items → 6,508; `SubItem` `type_id` 16→17;
  `prefab_data_list` + `gimmick_visual_prefab_data_list` merged into
  `MergedPrefabVisualData` relocated to item end), plus `partprefabdyeslotinfo`
  +570 rows (968 → 1,538) with a new additive `DyeExtraLayer` 2nd layer — all
  inside the Rust parser; no save-body change (format v2 / flags `0x0080`,
  `slot107` = live 1.13 save, all round-trip). Vendored crimson-rs at
  `7462f0e` / tag `v1.0.13.x`. **Retired the manual `ParserTargetMinor` /
  `CompatibleMinors` bump chain (8→9→10→11→12→13):** wired the C#
  `GameDataVersion` constants to the new crimson-rs C ABI
  (`crimson_parser_target_gamedata_minor()` /
  `crimson_parser_compatible_gamedata_minors()`, commit `a3ab5ee`) so Rust is
  the single source of truth. Bumped editor `VerMinor` 12 → 13 (manual
  build-identity), `VerPatch` reset to 1. Fixed two live-install test drifts:
  `Pyeonjeon_Arrow` `item_type` 0 → 23 (game remap) and the Paz LZ4-icon test
  (the `cd_icon_skill_*` icons are gone in 1.13 → switched to a still-LZ4
  `itemicon_gachaimage_*`). **Released as v1.13.01** (published 2026-07-04) —
  the release bundles this alignment plus the same-day DyeExtraLayer
  2nd-dye-layer UI and the window-position-memory work.
- **2026-06-19 — game 1.12 alignment (v1.12.01)**: third
  consecutive iteminfo schema drift (+150 items → 6,483; four byte-perfect
  layout changes) + `partprefabdyeslotinfo` dye-table drift (−143 rows → 968),
  no save-body change. Synced the gitignored `vendor/crimson-rs` from local
  `crimson-rs` `dev` `0694dfb` (not yet on `main`), rebuilt the native lib,
  bumped `ParserTargetMinor` / `CompatibleMinors` / `VerMinor` to 12, refreshed
  the paver tests. 346 tests green against the real 1.12 install;
  `slot106` / `slot107` verified `hmac_ok` + `undecoded_bytes=0` +
  decode-stable. Later released as v1.12.01 (tag `1.12.01`).
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
