# Status / session handoff

> **Read this first on a new session.** Lean by design — it carries the
> current state, the next task, the active backlog, and the gotchas worth not
> relearning. The full append-only session-by-session history and the
> long-form investigations live in
> **[status-archive.md](status-archive.md)** — look there only when you need
> the deep history behind a decision.
>
> Last updated: **2026-08-18** — **v1.18.01 is tagged, built and PUBLISHED**
> (2026-08-15T10:58Z), so the 1.18 alignment is fully closed out; the only
> loose end is that crimson-rs still has no `v1.0.18.x` tag. The single change
> since is a **NuGet dependency refresh** (PR #28, merge `3b3aa5e`) with no
> game-data, parser, or C ABI impact: Avalonia 12.0.4 → **12.1.1**, DataGrid
> 12.0.0 → **12.1.2** (DataGrid now *leads* core — the old "DataGrid lags"
> note was inverted), the `Microsoft.Extensions` / `Bcl` / `System.*` family
> 10.0.9 → **10.0.11**, plus Azure / Identity / OpenTelemetry / Tmds to current
> stable. `Microsoft.NET.Test.Sdk` was dragged 18.6.0 → **18.9.0** to re-match
> `Microsoft.CodeCoverage`, which had been bumped alone. 381 C# tests green,
> 0 skipped; restore clean; AOT publish emits **zero** IL/trim warnings.
>
> The editor is aligned to game **1.18**, which ships **exactly one** iteminfo
> layout drift and is otherwise content-only: every `MergedPrefabVisualData`
> element gained a `u32` (constant `0xeac5e173`, the empty-string Jenkins
> sentinel, on all 12,274 elements of all 6,573 items). Skill grew to 2,027
> entries with zero drift; no save-body drift. crimson-rs 1.18 is vendored from
> `main` (commit `87fd09f`, PR #90, merge `e4261be`). The C# side needed the
> manual `VerMinor` 17→18 lock-step bump plus the `NativePaverReaderTests`
> version-pin refresh — **no** count/value pin moved, and the C ABI surface is
> unchanged. Three unrelated build blockers were fixed en route: the ILCompiler
> central pin (SDK moved to 10.0.400), the `dotnet test` VSTest bridge the
> .NET 10 SDK removed, and `maturin develop` clobbering the c_abi cdylib.

## Current state

- **Editor v1.18.01 — tagged, built and PUBLISHED** (2026-08-15), aligned to
  live game **1.18** (`VerMinor` 17 → 18, `VerPatch` stays 1 per the lock-step
  `VerMinor == ParserTargetMinor` convention — `VerMinor` is a **manual**
  build-identity bump, while `ParserTargetMinor` is **ABI-sourced**). Verified
  locally (381 C# tests green, 0 skipped, against the live 1.18 install),
  merged to `main` (PR #27), then tagged `v1.18.01` → CI built the single-file
  AOT exe (run `31880379039`) and created the draft
  (`CrimsonAtomtic-v1.18.01-win-x64.zip`, 20,539,560 B), whose notes were
  trimmed to the bilingual `## Highlights` / `## 重點` sections; published
  2026-08-15T10:58Z. It supersedes v1.17.01 (published 2026-08-09). See
  [release-process.md](release-process.md).
  **Next concrete task: cut the crimson-rs `v1.0.18.x` tag** (see the
  crimson-rs bullet below) — after that there is no open release work, and the
  next milestone is whenever the game ships 1.19.
- **Dependency baseline refreshed 2026-08-18 (PR #28, merge `3b3aa5e`).**
  Avalonia 12.0.4 → **12.1.1**, DataGrid 12.0.0 → **12.1.2**,
  `Microsoft.Extensions`/`Bcl`/`System.*` 10.0.9 → **10.0.11**, Azure.Core
  1.59.0 → 1.61.0, Azure.Monitor.OpenTelemetry.Exporter 1.8.1 → 1.8.3, Msal
  4.84.2 → 4.87.0, IdentityModel.Abstractions 8.19.1 → 8.22.0,
  OpenTelemetry.PersistentStorage.* 1.1.0 → 1.1.1, Tmds.DBus.Protocol 0.94.1 →
  0.94.2, CodeCoverage + **Test.Sdk** both to 18.9.0. The ILCompiler pin stayed
  at **10.0.11** (SDK unchanged at 10.0.400). Restore clean, 381/381 green, AOT
  publish with **zero** IL/trim warnings. Note this bump got **no CI run** —
  `release.yml` triggers on a pushed `v*` tag only, so PR merges never build;
  the local `build.ps1 -Mode Publish` was the AOT verification.
- **1.18 has ONE iteminfo drift; the rest is content-only.** Every
  `MergedPrefabVisualData` element gained a `u32` between `tribe_gender_list`
  and the 3-byte flag tail, reading the same constant `0xeac5e173` on all
  12,274 elements of all 6,573 items — the "empty string" Jenkins sentinel
  from the 1.10 `money_icon_path` removal, so very likely a name hash shipping
  unset; typed as a bare `u32` until a populated value turns up. iteminfo went
  6,572 → **6,573** items (one new key, 1005446
  `Demian_Greyfur_Fabric_Cloak_II`), 6,139,734 → 6,190,316 B. `skill.pabgb`
  grew to **2,027** entries with **zero** drift (probe 2,027/2,027, format
  still `WithField58`). gamedata: 30 tables / 96,197 keys, 9 moved, 21
  key-identical; the extracted-bin roster went 270 → 268 (`zoneinfo` dropped,
  nothing references it).
- **Save read/write is version-agnostic.** Each save embeds its own schema, so
  1.05–1.18 saves round-trip in their own format (no version conversion). 1.18
  brought **no save-body drift** (format still v2 / flags `0x0080`; the live
  `slot107` 1.18 save decodes with `undecoded_bytes=0`). Verified this session:
  the live C# loader suite round-trips clean (all 381 C# tests ran with 0
  skipped; iteminfo catalog parses the real 1.18 data, now 6,573 items), and
  the refreshed Python toolchain round-trips the live 1.18 `iteminfo.pabgb`
  byte-identical (6,573 items, SHA256 `771fecb3…`).
- **The C ABI surface did NOT change.** `CrimsonItemInfoSummary` is untouched
  and the 80-byte `Marshal.SizeOf` pin still holds — as it also did across the
  *structural* 1.16 drift and now across 1.18's `MergedPrefabVisualData` `u32`,
  both absorbed entirely inside Rust. This is the payoff of the
  foundation-first rule: the only crimson-rs `src/c_abi/` edits in the 1.18
  commit are inside `mod tests` (count pins), not the exported surface.
- **Name/icon resolution targets the *installed* game.**
  `GameDataVersion.ParserTargetMinor` and `CompatibleMinors` are read from the
  crimson-rs C ABI (`crimson_parser_target_gamedata_minor()` → 18;
  `crimson_parser_compatible_gamedata_minors()` → {18}) — not hand-coded.
  Because 1.18 carries a real layout drift, the warning shown to a 1.17
  install is **substantive** (its iteminfo genuinely mis-decodes), not the
  target-only convention it was at 1.14/1.15/1.17. Full per-version breakdown
  in [game-versions.md](game-versions.md).
- **crimson-rs 1.18 is on `main` but NOT tagged.** The 1.18 support is merged
  to `bbfox0703/crimson-rs` `main` (commit `87fd09f`, PR #90, merge `e4261be`,
  vendored at `e4261be`); there is **no `v1.0.18.x` tag yet** — worth cutting
  for parity with 1.13–1.17. CI clones `main`, so a release cut already ships
  the 1.18 parser. Reminder for the next patch: land the crimson-rs change on
  `main` *before* tagging a CrimsonAtomtic release. Caveat when you go to cut
  it: the `v1.0.10.x`–`v1.0.17.x` tags exist **only in the local clone** at
  `D:\Github\crimson-rs`; `git ls-remote --tags` against the fork returns
  **nothing**, so none of them were ever pushed. "Parity with 1.13–1.17"
  therefore means parity with local-only tags; decide whether to push the whole
  set or keep them local.
- **Health:** full suite green this session (381 C# tests, 0 skipped, 0
  failures after the version-pin refresh — live-install + catalog tests parse
  the real 1.18 iteminfo, 6,573 items; the native lib was rebuilt from the
  vendored 1.18 crimson-rs so the ABI reports target minor 18). Only the
  `NativePaverReaderTests` version pins were red pre-bump. The
  `runtime.win-x64.Microsoft.DotNet.ILCompiler` central pin moved 10.0.10 →
  **10.0.11** (SDK moved 10.0.302 → **10.0.400**, whose auto-injected
  ILCompiler tripped NU1109 on the stale pin — the same trap as at 1.14 and
  1.11).

## Feature ledger

The shipped editor surface (generic block/field editor, inventory, sockets,
dye, sealed-abyss, abyss-gates, mount-unlock, knowledge, vendor-buyback,
mercenary-rename, browsers, 32 key-resolver bridges, …) is listed in the
[README](../README.md#editor-features-current). Deep design notes per feature
are in [status-archive.md](status-archive.md).

## Open work / backlog

- **✅ World Map parchment composite layer-alignment bug — OBSOLETE, not
  fixed.** This sat here as "still open" long after the code it describes was
  deleted. The `blur_height` / `road_sdf` composite pipeline
  (`WorldMapCompositor`, added dd1e650) was removed on 2026-05-18 by a44c12c
  "refactor(ui-worldmap): pivot to user-picked basemap + canonical-affine
  projection" — one day after the part-14 report that logged the bug. The
  dialog now loads a **user-supplied basemap image**
  (`WorldMapBasemapService`) and projects markers purely against
  `WorldMapAffine.Canonical`, so there are no layers left to misalign. The L8
  (`DDPF_LUMINANCE`) decode path for those two layers still exists in
  `IconImageEncoder`, but nothing composites them any more. Nothing to do —
  delete this line once someone confirms they don't want the composite back.
- **Feature-parity backlog vs the reference editor**
  (NattKh's `CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`) — features we only do
  via the generic field tree (🔸) or not at all (❌): ItemBuffs (iteminfo
  stats/buffs/enchant/transmog), Stores prices/stock, DropSets loot tables,
  SpawnEdit, Skills params (`skill.pabgb`), FieldEdit
  (`fieldinfo`/`vehicleinfo`), inventory storage expansion, dedicated quest /
  equipment-enchant editing, item-pack share/import/export, full reveal-map.
  Most need a new crimson-rs parser first. Full table in the archive.
- **Name resolution gaps — mostly CLOSED; this bullet was badly out of date.**
  `MissionKey`, `QuestKey`, `StageKey`, `KnowledgeKey`, `GimmickInfoKey` and
  `CharacterKey` now go through `LocalizationProvider.ResolveKeyTableOne` →
  `DisplayOrFallback(..., LookupDisplayName(key, paloc))`, i.e. they **do**
  resolve to localized PALOC names today, falling back to the internal string
  key only when PALOC has no entry. What genuinely remains is narrower:
  `SkillKey`, `QuestGaugeKey` and `StoreKey` are **internal-name only** — the
  code comments say outright "no PALOC chain", and the secondary-language
  column intentionally echoes English rather than showing a blank cell next to
  a populated one. Field-NPC and learned-skill names have no dedicated
  resolver at all (NPCs may be partly covered by `CharacterKey`). So the open
  work is a PALOC chain for skills/gauges/stores, not the broad gap this
  bullet used to claim.

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
- **A value *reordering* can masquerade as a pair of drifts in a byte-walk.**
  1.18's diff report flagged three length-changing signatures; only one was
  real. The other two were artifacts of the game reordering the
  `item_group_info_list` u16s, which manufactured compensating ±1 B pairs
  against `look_detail_mission_info` (93×) and `enable_alert_system_to_ui`
  (5×). Before modelling a drift, check whether an offsetting pair on
  neighbouring fields nets to zero — that's a reshuffle, not a layout change.
- **Not every count-pin failure is a schema drift — some pins are the wrong
  shape.** 1.18's `part_prefab_dye_slot_info_lossy_live` check asserted
  `slot_count == 1` on more than a quarter of rows; 65 new rows pushed it to
  24.6% and it went red. All 1,619 rows still parsed, every KNOWN
  name+slot_count still matched, and the histogram was textbook right-skewed —
  content, not drift. The fix was to re-shape the assertion ("`slot_count == 1`
  must be the modal bucket") rather than re-number it. Contrast 1.12, which
  broke the same check to 0 rows — that one *was* a drift.
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
  in the Ui csproj is load-bearing (Avalonia DataGrid 12 roll-up warnings) —
  **still true at DataGrid 12.1.2**, re-verified 2026-08-18 by publishing with
  `-p:NoWarn=`, which brings both warnings straight back. Don't drop it on the
  assumption a newer DataGrid fixed the trim annotations.
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
- **Run tests with `dotnet run --project src/CrimsonAtomtic.Tests`** — the
  .NET 10 SDK removed the `dotnet test` → VSTest bridge that MTP (xunit.v3)
  used to ride on. `build.ps1 -Target Test` and `build_ui.ps1 -Test` were both
  still on the old invocation and had been silently dead since; both now call
  the test project's own runner (fixed at the 1.18 alignment).
- **`Microsoft.NET.Test.Sdk` and `Microsoft.CodeCoverage` ship as a matched
  set.** They are versioned and released together (Test.Sdk depends on the
  same-version CodeCoverage), so bumping one alone leaves a silent mismatch —
  restore still succeeds and the tests still pass, so nothing tells you. The
  2026-08-18 bump moved CodeCoverage 18.6.0 → 18.9.0 and left Test.Sdk behind
  at 18.6.0; both are now 18.9.0. When bumping either, bump both.
- **The ILCompiler central pin tracks the installed SDK's runtime.** A newer
  SDK auto-injects a newer `Microsoft.DotNet.ILCompiler`, which then demands
  `runtime.win-x64.Microsoft.DotNet.ILCompiler >=` that version → **NU1109 at
  restore**, blocking every build. Bumped 10.0.8 → 10.0.9 (1.11) → 10.0.10
  (1.14) → **10.0.11** (1.18, SDK 10.0.400). Expect this every time the SDK
  moves; it is not related to the game patch.
- **A vendor refresh does NOT rebuild the Python `crimson_rs` module.**
  `maturin develop` installs *editable* (a `.pth` pointing at
  `vendor/crimson-rs/python`), so after `update_vendors.ps1` the in-tree source
  and `crimson_rs.__file__` both look current while the compiled
  `python/crimson_rs/crimson_rs.pyd` is still whatever was last built — it was
  51 days stale (a 1.12-era build, 2026-06-19) at the 1.17 alignment, i.e.
  documenting it did not prevent the recurrence. (An earlier revision of this
  line claimed "70 days stale again at 1.18"; that figure was impossible — the
  `.pyd` had been rebuilt six days earlier at 1.17 — and has been dropped.)
  The C# editor is unaffected (it loads `crimson_rs.dll` from
  `scripts/build_rust.ps1`), but the `tools/` Python toolchain would silently
  parse new game data with an old schema. Re-run
  `scripts/setup_python_env.ps1` after every vendor refresh, and check the
  `.pyd` mtime — not `crimson_rs.__file__` — to tell whether it's current.
  **⚠️ As of 2026-08-18 the trap is LIVE again**: the in-tree
  `crimson_rs.pyd` is 1,274,368 B dated **2026-08-09 20:39** — the 1.17-era
  build — and the venv `.pth` carries the same date, so the toolchain parses
  1.18 data with a 1.17 parser. `vendor/crimson-rs/target-py/` does not exist
  either, so the 1.18 `CARGO_TARGET_DIR` fix below has not actually been
  exercised on this machine. Run `scripts/setup_python_env.ps1` before trusting
  anything under `tools/`.
- **…and until 1.18, that Python rebuild CLOBBERED the C# native lib.**
  `maturin develop` builds the same crate with the **default** features (PyO3,
  no `c_abi`), so it shared `vendor/crimson-rs/target/release/crimson_rs.dll`
  with `build_rust.ps1` (`--features c_abi`) and whichever ran last won. The
  C# projects copy that dll via `<Content Include=…>`, so a Python refresh
  left every c_abi P/Invoke throwing `EntryPointNotFoundException` — a failure
  that reads like a broken ABI but is really a build-artifact collision. Fixed
  at 1.18 by scoping `CARGO_TARGET_DIR` to `vendor/crimson-rs/target-py` for
  the maturin call only. If you see `EntryPointNotFoundException : Unable to
  find an entry point named 'crimson_…'`, check the dll's **size** before
  suspecting the ABI. Quoting exact bytes to avoid a unit trap: the c_abi build
  is **1,097,216 B** and the PyO3 one **1,274,368 B**. Windows Explorer and
  `Get-Item .Length/1KB` divide by 1024, so they show these as **1,071 KB** and
  **1,244 KB** — don't be thrown when they disagree with the decimal-KB
  (÷1000) figures of ~1,097 / ~1,275 this note used to quote.
- **Avalonia 12 quirks**: DataGrid is at **12.1.2 and now *leads* core
  (12.1.1)** — it ships on its own cadence, so a version mismatch between the
  two is expected and is **not** something to "fix" by pinning them together
  (this note used to say the opposite, back when DataGrid lagged at 12.0.0);
  `Avalonia.Diagnostics` 12.x still not released (latest is 11.3.20), so it
  stays out; MVVM uses field-based `[ObservableProperty]` (partial-property
  syntax didn't generate on CommunityToolkit 8.4 / .NET 10).

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
# NOTE: pass --body explicitly. Per CLAUDE.md rule 9 every tool prints usage
# and exits 2 when called with no args, so the bare form can never go green —
# it does NOT fall back to the --body default.
.\.venv\Scripts\python.exe .\tools\inspect\inspect_save_body.py --body .\out\save-extract\slot0.bin

# 3. C# end-to-end
.\scripts\build_rust.ps1          # builds vendor/crimson-rs --features c_abi
.\scripts\build_ui.ps1 -Test      # builds C# + runs xUnit tests (incl. live-save smoke)
.\scripts\package_aot.ps1 -SkipRustBuild   # AOT publish to dist/win-x64/
```

Each step should be green. If anything fails, fix it before touching new code
— drift is harder to chase later.

## Session changelog (newest first)

One line per milestone; full detail in [status-archive.md](status-archive.md).

- **2026-08-18 — NuGet dependency refresh + status-doc housekeeping**:
  maintenance only, no game-data / parser / C ABI change. Avalonia core 12.0.4
  → **12.1.1** (Desktop, FreeDesktop, HarfBuzz, Themes.Fluent, Fonts.Inter),
  `Avalonia.Controls.DataGrid` 12.0.0 → **12.1.2**, the `Microsoft.Extensions` /
  `Bcl` / `System.*` family 10.0.9 → **10.0.11**, Azure.Core 1.59.0 → 1.61.0,
  Azure.Monitor.OpenTelemetry.Exporter 1.8.1 → 1.8.3, Msal 4.84.2 → 4.87.0,
  IdentityModel.Abstractions 8.19.1 → 8.22.0, OpenTelemetry.PersistentStorage.*
  1.1.0 → 1.1.1, Tmds.DBus.Protocol 0.94.1 → 0.94.2, Microsoft.CodeCoverage
  18.6.0 → 18.9.0. The ILCompiler pin did **not** move this time (still
  10.0.11; SDK still 10.0.400). Two review catches: `Microsoft.NET.Test.Sdk`
  had been left behind at 18.6.0 while its matched-set partner CodeCoverage
  went to 18.9.0 (both now 18.9.0 — see gotchas), and the **"DataGrid lags
  core" note was inverted** — DataGrid now *leads* (12.1.2 vs 12.1.1), so that
  line was corrected in the gotchas and in
  [architecture.md](architecture.md). Also re-verified that
  `<NoWarn>IL2104;IL3053</NoWarn>` is still load-bearing at DataGrid 12.1.2
  (publishing with `-p:NoWarn=` brings both warnings straight back) rather than
  assuming the newer package had fixed it. Restore clean (no NU warnings),
  **381/381 C# tests green, 0 skipped**, AOT publish emits zero IL/trim
  warnings and stages a 27.8 MB single-file exe. Deps merged to `main` as
  PR #28 (merge `3b3aa5e`) — **no CI run**, because `release.yml` fires on
  pushed `v*` tags only, so PR merges never build and the local
  `build.ps1 -Mode Publish` was the AOT verification. This doc then got a
  correctness pass (a fan-out audit of every checkable claim in it, each
  finding then adversarially re-verified). What it caught: v1.18.01 was still
  written up as a **pending DRAFT** when it had actually been published
  2026-08-15T10:58Z, and the headline "next concrete task" still said "click
  Publish"; the **World Map parchment-composite bug** had been sitting in the
  backlog as "still open" since 2026-05-17 even though the whole
  `WorldMapCompositor` pipeline was deleted a day later by a44c12c (pivot to a
  user-picked basemap), so there are no layers left to misalign; the **name
  resolution gaps** bullet was largely obsolete — Mission/Quest/Stage/
  Knowledge/Gimmick/Character keys all resolve through PALOC today, and only
  `SkillKey` / `QuestGaugeKey` / `StoreKey` are still internal-name-only; the
  `.pyd` staleness note carried an arithmetically impossible "70 days stale at
  1.18" (it had been rebuilt 6 days earlier at 1.17); the c_abi-vs-PyO3 dll
  sizes were quoted in decimal KB, which is off by 1024/1000 from what Explorer
  shows, so they're now given in exact bytes; and the "verify on a fresh
  checkout" recipe called
  `inspect_save_body.py` **with no arguments**, which per rule 9 always exits 2
  — that step could never have gone green as written. Also newly recorded: the
  stale-`.pyd` trap is **live right now** (the in-tree build is the 2026-08-09
  1.17-era one and `target-py/` does not exist), so `tools/` must not be
  trusted until `setup_python_env.ps1` is re-run.
- **2026-08-15 — game 1.18 alignment (v1.18.01, released)**: 1.18 ships
  **exactly one** iteminfo layout drift and is otherwise content-only. Every
  `MergedPrefabVisualData` element gained a `u32` between `tribe_gender_list`
  and the 3-byte flag tail, reading the constant `0xeac5e173` — the
  empty-string Jenkins sentinel from the 1.10 `money_icon_path` removal — on
  **all 12,274 elements of all 6,573 items**, so it is very likely a name hash
  shipping unset; typed as a bare `u32` for now. iteminfo 6,572 → **6,573**
  items (one new key, 1005446 `Demian_Greyfur_Fabric_Cloak_II`), 6,139,734 →
  6,190,316 B, `serialize_iteminfo` byte-identical. `skill.pabgb` grew to
  **2,027** entries with **zero** drift (probe 2,027/2,027, format still
  `WithField58`). No save-body drift (v2 / flags `0x0080`; live `slot107`
  decodes `undecoded_bytes=0`). gamedata 30 tables / 96,197 keys (9 moved, 21
  key-identical); extracted-bin roster 270 → 268 (`zoneinfo` dropped). The RE
  (crimson-rs commit `87fd09f`, PR #90, merge `e4261be`, vendored at
  `e4261be`; **no `v1.0.18.x` tag yet**) used `scripts/diff_117_118.py`, and
  turned up a new trap: two of the three length-changing signatures were
  **walk artifacts of an `item_group_info_list` u16 reorder**, not drifts (see
  gotchas). C# side: manual `VerMinor` 17→18 lock-step bump +
  `NativePaverReaderTests` refresh (happy path pins the 1.18 paver
  `01 00 12 00 00 00 0f 7c 57 28` / build `0x28577c0f`, previous-minor guard
  moved to 1.17, future-minor guard to 1.19, ABI target pin 17→18). **No
  count/value pin moved** on the C# side — the drift is absorbed entirely in
  Rust and the C ABI surface is unchanged (the 1.18 commit's only
  `src/c_abi/` edits are inside `mod tests`) — so all 381 tests are green, 0
  skipped. Two unrelated build blockers fixed en route: the ILCompiler central
  pin 10.0.10 → **10.0.11** (SDK moved to 10.0.400 → NU1109), and both build
  scripts' dead `dotnet test` invocation (the .NET 10 SDK dropped the VSTest
  bridge MTP rode on) switched to the test project's own runner. Also caught
  the stale-`.pyd` vendor-refresh trap **again** (the "70 days" figure recorded
  at the time was wrong — see gotchas); after
  `setup_python_env.ps1` the Python toolchain round-trips the live 1.18
  `iteminfo.pabgb` byte-identical (6,573 items, SHA256 `771fecb3…`). Fixing
  that surfaced a **third** build trap, new this session: `maturin develop`
  built into the same `target/release/` as `build_rust.ps1` but **without**
  `--features c_abi`, clobbering `crimson_rs.dll` and turning every C# P/Invoke
  into `EntryPointNotFoundException`. `setup_python_env.ps1` now scopes
  `CARGO_TARGET_DIR` to `target-py` so the two builds cannot collide. Merged to
  `main` (PR #27, merge `a621fa4`) and **tagged `v1.18.01`** → CI AOT build
  green (run `31880379039`), draft release created and trimmed to the bilingual
  `## Highlights` / `## 重點` sections. The `--cleanup=verbatim` guard from the
  1.17 session worked — the `##` headings survived into the tag this time.
  The draft was **published 2026-08-15T10:58Z**. Still open: crimson-rs wants a
  `v1.0.18.x` tag for parity with 1.13–1.17 (its `main` already carries 1.18,
  so this release does ship the right parser).
- **2026-08-09 — game 1.17 alignment (v1.17.01, released)**:
  content-only over 1.16 — the structural 1.16 patch was a one-off. iteminfo
  6,581 → **6,572**
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
  381 are green after. C ABI surface unchanged. Merged to `main` (PR #26) and
  **tagged `v1.17.01`** → CI AOT build green (run `31316944938`), draft release
  created and trimmed to the bilingual `## Highlights` / `## 重點` sections,
  then **published** 2026-08-09. Two process findings: the
  vendor-refresh trap (stale Python `.pyd` — see gotchas) and that `git tag`
  defaults to `--cleanup=strip`, which silently ate the `##` headings out of
  this tag's message (release-process.md now documents `--cleanup=verbatim`
  plus the pre-push verification command).
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
