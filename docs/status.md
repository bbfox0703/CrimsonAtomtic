# Status / session handoff

> **Read this first on a new session.** Lean by design — it carries the
> current state, the next task, the active backlog, and the gotchas worth not
> relearning. The full append-only session-by-session history and the
> long-form investigations live in
> **[status-archive.md](status-archive.md)** — look there only when you need
> the deep history behind a decision.
>
> Last updated: **2026-09-18** — the editor is aligned to game **2.03**
> **committed on `dev`, not yet pushed, tagged or released** (v2.02.01 is still
> the published Latest). 2.03 (paver `2/3/0/0x03045138`) makes two format
> changes, both absorbed inside the vendored crimson-rs (`main` `234b289`;
> the 2.03 work is PR #97): iteminfo's `inventory_info_list` widened
> `[u16; 9]` → `[u16; 10]` (every item +2 B), and every `.paloc` file is
> now wrapped in a 0x200-byte header + one LZ4 block. Neither reached the
> C ABI surface — both PALOC loaders unwrap internally, and
> `CrimsonItemInfoSummary` still reads slot 0 — so the editor side was the
> manual `VerMinor` 2 → 3 bump, the paver pins, and one mission title 2.03
> changed *back* (`MissionKey 1000157` "Unfamiliar Land" → "Unfamiliar
> Lands"). **401 C# tests green, 0 skipped**; the untouched suite failed
> exactly those five. AOT publish emits zero IL/trim warnings, and the
> local single-file exe (28,966,912 B, 4-file bundle, no `crimson_rs.dll`)
> stamps `2.3.1.27` and **launches** as `CrimsonAtomtic v2.03.01.27` with
> exactly one visible window — no version-mismatch dialog on the 2.03
> install. The SDK did not move (10.0.401 / runtime 10.0.12), so the
> ILCompiler pin stays at 10.0.12.
>
> **Next task: ship it** — push `dev`, PR to `main`, then the
> 「啟動 release CI」 runbook for `v2.03.01`. crimson-rs `main` already
> carries 2.03, so CI's fresh clone ships the right parser.
>
> **A vendor refresh is not a build — it bit again, in a new shape.**
> `vendor/crimson-rs` had been refreshed to `234b289` (13:29 today), but its
> `target/release/crimson_rs.dll` / `.lib` and the Python `.pyd` were all
> **2026-08-28** builds of 2.00-era source. Tests or the app run from that
> state would have loaded a parser that cannot unwrap a 2.03 PALOC (no names
> anywhere) and mis-decodes 2.03 iteminfo. `build_rust.ps1` +
> `setup_python_env.ps1` fixed it. Also odd: this clone's reflog shows no
> move between `fa1e8da` (2026-08-28) and today, so the 2.01 / 2.02 rebuilds
> recorded below were made somewhere else (no worktree remains to check).
> Compare artifact **mtimes** against the vendor HEAD before trusting a run.
>
> **The C# suite never loads a 2.03-written save.** `NativeSaveLoaderTests`
> takes the first of slot0/1/2 — all 2.01-era on this machine; the only
> 2.03-written saves are slot107 and slot102. Both were checked through the
> editor's own `NativeSaveLoader` with a throwaway probe: HMAC ok, every
> field decoded, 0 undecoded bytes, and a write → reload that decodes
> identically — matching upstream's Rust-side result. Making that permanent
> (load the newest save) would close the gap.
>
> **Below this line is the 2.02 history, kept for context.**
>
> The editor was aligned to game **2.02** on 2026-09-11 and **v2.02.01 was
> tagged, built and PUBLISHED** (2026-09-11T14:42:44Z, marked Latest). 2.02
> (paver `2/2/0/0xc8925c58`) is content-only for everything crimson-rs
> parses — `iteminfo` and `skill` byte-identical to 2.01, 17 of 269 gamedata
> files changed as pure content, no save-body drift — so the editor side was
> the manual `VerMinor` 1 → 2 bump, the paver pin refresh, and wiring the
> seven additive quest-key lookups the vendored crimson-rs (`main`
> `1753d71`) now exports. **401 C# tests green, 0 skipped**; the untouched
> suite failed exactly the four paver pins. AOT publish emits zero IL/trim
> warnings, and the local single-file exe (28,965,888 B, 4-file bundle, no
> `crimson_rs.dll`) stamps `2.2.1.27` and **launches** with the window title
> `CrimsonAtomtic v2.02.01.27`.
>
> **Release v2.02.01 (2026-09-11).** PR #38 (merge `f5a645a`) → annotated
> tag `v2.02.01` (`--cleanup=verbatim`, both headings verified before the
> push) → CI run `34608782045` (5 m 13 s; runner SDK 10.0.401, no NU1109;
> crimson-rs `main` `1753d71`) → **DRAFT** "CrimsonAtomtic v2.02.01". Its
> body was replaced with the bilingual player-facing notes only, matching
> earlier releases. Verified without downloading anything: the zip asset
> (`CrimsonAtomtic-v2.02.01-win-x64.zip`, 20,647,100 B) carries GitHub
> digest `sha256:2bfa5407…`, identical to the hash CI printed when it
> packaged it, and CI's bundle check reported 4 files with no
> `crimson_rs.dll`. **Published 2026-09-11T14:42:44Z on the user's word,
> marked Latest** (superseding v2.01.01). Not run before publishing: the
> binary checks on the release zip itself (download, FileVersion, launch —
> the launch check was done on a local AOT build of the same source) and a
> load-a-real-save check. The whole sequence is now the 「啟動 release CI」
> runbook in [release-process.md](release-process.md). With it shipped, the
> next task was the backlog below (the Dye editor's mask-group RE first),
> until 2.03 landed.
>
> **Nothing built until the SDK pin moved.** The .NET SDK had updated to
> 10.0.401 (runtime 10.0.12) since the last session, so restore failed with
> NU1109 before a single test could run — the ILCompiler central pin went
> 10.0.11 → 10.0.12 (see gotchas). CI's `setup-dotnet` asks for `10.0.x`,
> so the release build needed the same pin — the runner got 10.0.401 and
> restored cleanly.
>
> **The quest-key lookups have no UI consumer yet.** crimson-rs reconciled
> its curated main-/side-quest tables against the live 2.02 PALOC and keyed
> every row by `MissionKey` / `QuestKey` (additive C ABI; the title lookups
> are unchanged, and upstream's advice is "C# editor: prefer the key
> lookups"). They are wired through `NativeMainQuestChapter` /
> `NativeSideQuestFaction` and pinned by five new tests, but nothing in the
> app calls either class, so under `TrimMode=full` the AOT link never sees
> them — the tests, through the cdylib, are what exercise them.
>
> **The Dye editor stays disabled**: `partprefabdyeslotinfo` is
> byte-identical in 2.02 (body and header), so the 2.01 measurement stands.
> The menu now reads "unsupported since 2.01" instead of "2.01 unsupported",
> which read wrong on a 2.02 install.
>
> **Below this line is the 2.01 history, kept for context.**
>
> The editor was aligned to game **2.01** on 2026-09-04 and **v2.01.01 was
> tagged, built and published** the same day (2026-09-04T10:21:31Z; Latest
> until v2.02.01 — this doc called it an unpublished draft until 2026-09-11). The annotated
> tag points at `9e239c6` (PR #36 merge); CI cloned crimson-rs `main`
> (`5dbeefb`, PR #93) fresh and built the single-file AOT exe. Verified from
> the release's own assets: sha256 matches, the zip holds exactly the four
> expected files with **no `crimson_rs.dll`** (the Rust core is linked into
> the exe), and the exe stamps `2.1.1.26` / product `2.1.1.26+9e239c6`. The
> release notes were trimmed to the bilingual highlights only, matching
> v1.18.01 / v2.00.01 / v2.00.02 — CI's auto `## What's changed` + footer
> are dropped at publish time by convention. The standing loose end is
> unchanged: crimson-rs still has no pushed version tags (see the
> crimson-rs bullet).
>
> **The bug this release fixes had a green test suite.** Every individual
> bridge had a live-install test, but nothing drove `LocalizationProvider` —
> the production entry point — end to end, so "every extraction returns
> NOT_FOUND because the archive layout moved" was invisible to CI. Closed by
> `LocalizationProviderTests.Bootstrap_LiveInstall_ResolvesThroughWhicheverArchiveLayoutShips`,
> which calls `TryBootstrapFromGameRoot` against the real install and asserts
> iteminfo, the English PALOC, the language probe and a sibling table bridge
> all resolved. Added after the tag — it changes no shipped code.
>
> **2.01 is a rename patch — and it broke the app outright.** Every gamedata
> file moved (`gamedata/binary__/client/bin/<t>.pabgb`/`.pabgh` →
> `gamedata/binarystaticinfo__/bin/<t>.staticinfobody`/`.staticinfoheader`)
> and each language's single `localizationstring_<lang>.paloc` blob became
> one file per namespace under `stringtable/binary__/<lang>/`. **Not one byte
> inside any file changed**, so no parser moved: Rust's only edit is
> `PARSER_TARGET_GAMEDATA_MINOR` 0 → 1, and `CompatibleMinors = {1}` is the
> target-only convention (2.00 data still parses byte-perfectly), not a real
> incompatibility. But `LocalizationProvider` hardcoded the old directory and
> all 40-odd filenames, so on a 2.01 install every bridge returned NOT_FOUND
> and **no name resolved anywhere**. Both the app and the tests now go
> through a new `GameDataLayout` (RustInterop): tables are named by *stem*
> and the directory + extensions come from a newest-first probe of the
> group-0008 manifest, so a kept pre-2.01 install still works. It mirrors
> crimson-rs's `src/binary/gamedata_layout.rs`, which is `#[cfg(test)]` there
> and therefore unreachable over the C ABI. Localization needed more than a
> rename: `crimson_paloc_load_from_bytes` takes one blob, so a language is
> held as its parts behind the new `MultiPalocCatalog`, and the
> `*_lookup_display_name` bridges — each taking a single PALOC *native
> handle* — are offered one part at a time (namespaces don't overlap, so at
> most one answers). **395 C# tests green, 0 skipped** (45 were failing
> against the live 2.01 install before this).
>
> **The Dye editor is greyed out.** 2.01 widened `partprefabdyeslotinfo`'s
> per-slot `mask` 3 → **12** bytes *and re-encoded it* — in 5,572 of 6,555
> comparable slot pairs the old three bytes appear nowhere as a contiguous
> window inside the new twelve, so no sub-slice is the pre-2.01 field. The
> twelve read as four groups of three and **which group a slot uses is not
> yet RE'd**. Measured through the release dll over all 1,626 prefabs / 6,585
> slots, the legacy 3-byte getter the editor calls reads all-zero on **2,196
> slots (33.3%)** whose full field is non-zero — a third of the dye UI would
> render blank and would write edits from that wrong reading. Foundation over
> workaround: the menu item is disabled (reason in its tooltip, and
> `MainWindowViewModel.IsDyeEditorAvailable` carries the measurement) until
> the mask groups are decoded upstream and the editor moves to the new
> `crimson_part_prefab_dye_slot_info_lookup_slot_{,extra_layer_}mask_full`
> sized-buffer bridges. Nothing else about dyeing regressed — the table still
> loads all 1,626 rows on 2.01.
>
> One soft pin moved, C# side only: `MissionKey 1000157`'s English title was
> reworded **"Unfamiliar Lands" → "Unfamiliar Land"** — a game-side text
> edit, not a parse drift (the 25-character `MissionKey 1000083` title
> resolves unchanged through the identical path).
>
> **Also fixed this session (unrelated to 2.01):**
> `vendor/update_vendors.ps1` could rewrite *this* repository. A leftover
> `vendor/crimson-rs/` folder with no `.git` of its own passed the
> `Test-Path` existence check, so every `git -C vendor/crimson-rs …` walked
> up to the parent project — rewriting origin to `crimson-rs.git`, refetching
> its refs and `reset --hard`-ing `main` onto crimson-rs's `main`. The script
> now requires the target to hold its own `.git` and asserts
> `rev-parse --show-toplevel` really resolves to the vendor path before any
> mutating command. Local game saves also moved out of `vendor/` (which is
> for dependency clones only) to `data/_saves/`.
>
> **Below this line is the 2.00 history, kept for context.**
>
> **v2.00.02 is a socket-editor correctness release** — no game-version
> change, so only `VerPatch` moved (1 → 2). Socket editing was producing
> saves the *engine* rejects: every socket on an edited item read back
> in-game as not-yet-opened. Three save-format invariants were being broken,
> all measured against 22,019 socket-bearing blocks in four game-written
> saves; an adversarial audit of the fix then caught a fourth defect the fix
> itself introduced on the deferred Apply-Set path. Details in the session
> changelog below, and the two rules the upstream reference doc had wrong are
> corrected in crimson-rs (PR #92, merge `fa1e8da`).
>
> **The version model changed, and that matters more than either drift.**
> `meta/0.paver`'s `minor` **resets across a major bump** — 1.18 → 2.00 is
> `18` → `0` — so the minor alone no longer identifies a schema. crimson-rs
> added `PARSER_TARGET_GAMEDATA_MAJOR = 2` beside
> `PARSER_TARGET_GAMEDATA_MINOR = 0` and a new
> `crimson_parser_target_gamedata_major()` bridge (purely additive). That
> closed a **real hole on the editor side**: `IsCompatibleWithParser` was a
> minor-only lookup, and with `CompatibleMinors = {0}` it would have accepted
> a hypothetical `1.00.xx` install into a mis-decode. It now gates on the
> `(major, minor)` pair. The mismatch dialog had the same bug in its readout
> (hard-coded `"1."` prefix → would have shown the 2.00 target as `1.00.xx`).
>
> **2.00 ships two iteminfo drifts**: `SubItem` gained a payload-free
> `type_id == 18` (the same renumbering 1.12 and 1.13 did), and a new
> always-zero `u32` (`unk_pre_max_endurance_a`) sits ahead of the 1.12-era
> `unk_pre_max_endurance`. Iteminfo 6,573 → **6,810** items (+237, none
> removed); skill grew to **2,046** with zero drift; no save-body drift.
> crimson-rs 2.00 is vendored from `main` (commit `0f2363b`, PR #91, merge
> `8e942d7`). The C# side needed the manual `VerMajor` 1→2 / `VerMinor` 18→0
> bump, the new major `LibraryImport` + compatibility gate, the dialog prefix
> fix, and two pin refreshes — **382 C# tests green, 0 skipped**; AOT publish
> emits **zero** IL/trim warnings and stamps `2.0.1.17` (rendered
> `v2.00.01.17`).

## Current state

- **Editor aligned to game 2.03 — committed on `dev`, not pushed** (2026-09-18).
  `VerMinor` 2 → **3** (`VerPatch` stays 1), so builds stamp
  `2.3.1.<build>` and the UI renders `v2.03.01.<build>`. 2.03's two format
  changes (a tenth iteminfo `inventory_info_list` slot; an LZ4 container
  around every PALOC file) are absorbed in Rust with the C ABI surface
  unchanged; full breakdown in [game-versions.md](game-versions.md). The C#
  cost was the version bump, the paver pins, the reverted `MissionKey
  1000157` title, and doc refreshes (Dye figures re-measured on 2.03, the
  quest wrappers' retitle note, the PALOC-container note in
  `GameDataLayout`). **401 C# tests green, 0 skipped.**
- **Editor v2.02.01 — tagged, built and PUBLISHED** (2026-09-11T14:42:44Z,
  marked Latest; still the current release). Tag → `f5a645a` (PR #38 merge), CI run `34608782045`, zip
  20,647,100 B (`sha256:2bfa5407…`), body = the bilingual notes only. `VerMinor` 1 → **2** (`VerPatch` stays 1), so the
  build stamps `2.2.1.<build>` and the UI renders `v2.02.01.<build>`. 2.02 is
  content-only for everything crimson-rs parses (full breakdown in
  [game-versions.md](game-versions.md)); the C# cost was the version bump,
  the paver pin refresh, seven additive quest-key lookups, version-neutral
  Dye-menu text, and an SDK-driven ILCompiler pin bump. **401 C# tests green,
  0 skipped**; AOT publish zero IL/trim warnings.
- **Editor v2.01.01 — tagged, built and published** (2026-09-04T10:21:31Z;
  superseded by v2.02.01), aligned to live game **2.01**. Tag `v2.01.01` → `9e239c6`
  (PR #36 merge); the zip's sha256 matched, it held the four expected files
  with no `crimson_rs.dll`, and the exe stamped `2.1.1.26`. This doc
  described it as an unpublished draft until 2026-09-11 — `gh release view`
  says `draft: false`.
- **Editor v2.00.02 — tagged, built and published** (2026-08-28T07:33:26Z;
  superseded by v2.01.01), aligned to live game **2.00**. A socket-editor correctness
  release on top of v2.00.01: `VerMajor` / `VerMinor` unchanged (the game is
  still 2.00), `VerPatch` **1 → 2**. Merged to `main` (PR #32, merge
  `2a30d4c`), tagged `v2.00.02` → CI run `33151190235` built the single-file
  AOT exe and created the draft
  (`CrimsonAtomtic-v2.00.02-win-x64.zip`, **20,724,824 B**, sha256
  `14809084…`), whose notes were trimmed to the bilingual `## Highlights` /
  `## 重點` sections; published and marked Latest. **Release-artifact checks
  the test suite cannot cover**: the downloaded zip's sha256 matches its
  `.sha256`; the packaged exe is 29,145,088 B with FileVersion `2.0.2.25` and
  a ProductVersion embedding `2a30d4c`; the bundle carries 4 files with **no**
  `crimson_rs.dll` (folded into the exe via staticlib); and the AOT binary
  **actually launches** — window title `CrimsonAtomtic v2.00.02.25`, the only
  end-to-end proof that the `2.0.2.25` → `v2.00.02.25` rendering is right in a
  shipped build. **394 C# tests green, 0 skipped**; AOT publish emits zero
  IL/trim warnings.
- **Editor v2.00.01 — superseded** (published 2026-08-26T08:06:44Z),
  the alignment to live game **2.00**. `VerMajor` 1 → **2**, `VerMinor` 18 → **0**,
  `VerPatch` reset to **1** per the convention (both game-tracking components
  are **manual** build-identity bumps; `ParserTargetMajor` /
  `ParserTargetMinor` are **ABI-sourced**). The build stamps `2.0.1.17`, which
  the UI renders `v2.00.01.17` — the `:D2` padding in
  `MainWindowViewModel.GetAppVersion` is what keeps a game "2.00" from
  displaying as "2.0". Verified locally (**382 C# tests green, 0 skipped**,
  against the live 2.00 install; AOT publish clean with zero IL/trim
  warnings), merged to `main` (PR #30, merge `bb78dd8`), then tagged
  `v2.00.01` → CI built the single-file AOT exe (run `32945531894`) and
  created the draft (`CrimsonAtomtic-v2.00.01-win-x64.zip`, **20,713,436 B**,
  sha256 `555c63f1…`), whose notes were trimmed to the bilingual
  `## Highlights` / `## 重點` sections; published and marked Latest.
  **Release-artifact checks that the test suite cannot cover**: the
  downloaded zip's sha256 matches its `.sha256`; the packaged exe is
  29,124,608 B with FileVersion `2.0.1.17` and a ProductVersion embedding
  `bb78dd8`; and the AOT binary **actually launches** — it came up with the
  window title `CrimsonAtomtic v2.00.01.17`, which is the only end-to-end
  proof that the `2.0.1.17` → `v2.00.01.17` rendering is right in a shipped
  build. See [release-process.md](release-process.md).
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
- **2.00 has TWO iteminfo drifts; the rest is content-only.** (1) `SubItem`
  gained a payload-free `type_id == 18` — the same renumbering 1.12 (16) and
  1.13 (17) did; every site that read 17 in 1.18 reads 18 here, and no item
  still reads 14..=17. (2) A new always-zero `u32`
  (`unk_pre_max_endurance_a`) sits directly ahead of the 1.12-era
  `unk_pre_max_endurance`, so the block before `respawn_time_seconds` now
  carries two `u32`s. iteminfo went 6,573 → **6,810** items (+237, none
  removed), 6,190,316 → 6,446,719 B. `skill.pabgb` grew to **2,046** entries
  with **zero** drift (probe 2,046/2,046, format still `WithField58`) — even a
  major bump left it alone. gamedata: 30 tables / 96,997 keys, 13 moved, 17
  key-identical; the extracted-bin roster went 268 → **269**
  (`levelgimmicksceneobjectinfo_misc.base.pabgm` is new while
  `levelgimmicksceneobjectinfo.pabgb` shrank 3,410,666 → 21,376 B as its
  payload moved into it — nothing parses either).
- **The minor now RESETS on a major bump — this is the load-bearing change.**
  1.18 → 2.00 is minor `18` → `0`, so the minor alone stopped identifying a
  schema. crimson-rs added `PARSER_TARGET_GAMEDATA_MAJOR = 2` +
  `crimson_parser_target_gamedata_major()`; the editor added the matching
  `LibraryImport`, a `GameDataVersion.ParserTargetMajor`, and changed
  `IsCompatibleWithParser` from a minor-only `Array.IndexOf` to
  `Major == ParserTargetMajor && Minor ∈ CompatibleMinors`. Without that, a
  hypothetical `1.00.xx` install (minor 0, the very value in
  `CompatibleMinors`) would have been waved through into a mis-decode;
  `NativePaverReaderTests` now pins exactly that shape. The mismatch dialog
  carried the same latent bug — a hard-coded `"1."` prefix that would have
  shown the 2.00 target as `1.00.xx`.
- **Save read/write is version-agnostic.** Each save embeds its own schema, so
  1.05–2.00 saves round-trip in their own format (no version conversion). 2.00
  brought **no save-body drift** (format still v2 / flags `0x0080`; the live
  2.00 save decodes 1,103 blocks / 3,315 fields with `undecoded_bytes`
  0/5,229,306). Verified this session: the live C# loader suite round-trips
  clean (all 382 C# tests ran with 0 skipped; iteminfo catalog parses the real
  2.00 data, now 6,810 items), and the refreshed Python toolchain round-trips
  the live 2.00 `iteminfo.pabgb` **byte-identical** (6,446,719 B, 6,810 items,
  SHA256 `51f87fb4…`).
- **The C ABI surface only GREW — nothing existing moved.**
  `crimson_parser_target_gamedata_major()` is purely additive;
  `CrimsonItemInfoSummary` is untouched and the 80-byte `Marshal.SizeOf` pin
  still holds, as it did across the *structural* 1.16 and 1.18 drifts. Both
  2.00 iteminfo drifts are absorbed entirely inside Rust. This is the payoff
  of the foundation-first rule: the C# interop cost of a two-drift **major**
  game bump was one new `LibraryImport`.
- **Name/icon resolution targets the *installed* game.**
  `GameDataVersion.ParserTargetMajor`, `ParserTargetMinor` and
  `CompatibleMinors` are all read from the crimson-rs C ABI
  (`crimson_parser_target_gamedata_major()` → 2;
  `crimson_parser_target_gamedata_minor()` → 3;
  `crimson_parser_compatible_gamedata_minors()` → {3}) — not hand-coded.
  2.03's tenth `inventory_info_list` slot makes the warning shown to a 2.02
  install **substantive** (2.02 iteminfo really does mis-decode), like
  1.18 → 2.00 and unlike the target-only convention 2.01 and 2.02 got. Full
  per-version breakdown in [game-versions.md](game-versions.md).
- **crimson-rs 2.03 is on `main`, and vendored.** The 2.03 support is PR #97
  (merge `332c47b`: `e932797` — `PARSER_TARGET_GAMEDATA_MINOR` 2 → 3, the
  10-slot `inventory_info_list`, `binary::paloc::unwrap_container` in every
  PALOC reader, and the curated quest tables' 2.03 retitles), and `main` has
  since moved to `234b289` (PR #98, fork-guard scripts only). The vendor copy
  sits at `234b289`; `build_rust.ps1` rebuilt the c_abi dll + staticlib from
  it and `setup_python_env.ps1` the Python module. CI clones `main` fresh at
  tag time, so a `v2.03.01` tag ships the 2.03 parser as things stand. (The
  2.02 support was PR #95, merge `3296b5c`.)
  **Still no version tags**, now for 1.18 through 2.03 (re-checked
  2026-09-18: `git ls-remote --tags origin` returns 0 refs): the `v1.0.10.x`–
  `v1.0.17.x` tags exist **only in the local clone** at
  `D:\Github\crimson-rs` — `git ls-remote --tags` against the fork returns
  **nothing**, so none were ever pushed. "Parity with 1.13–1.17" therefore
  means parity with local-only tags; decide whether to push the whole set,
  keep them local, or stop cutting them.
- **Health:** full suite green this session (**401** C# tests, 0 skipped, 0
  failures) against the live 2.03 install, with the native lib rebuilt from
  the vendored 2.03 crimson-rs so the ABI reports target major 2 / minor 3.
  The untouched suite (401) failed exactly five: the four
  `NativePaverReaderTests` pins and the `MissionKey 1000157` title. At 2.02
  the untouched suite (396) failed exactly the four paver pins; the five
  tests added then are the quest-key checks. At 2.01 the count was
  **395** (396 once the `LocalizationProvider` bootstrap test landed), 45 of
  which had been failing against the live 2.01 install before the
  `GameDataLayout` rewire, all on the renamed archive paths.
  The 2.00 count went
  381 → 382 because the 2.00 alignment added one test
  (`TryReadFromBytes_SameMinorUnderOtherMajor_FlagsIncompatible`). Two pin
  groups were red pre-fix: the `NativePaverReaderTests` version pins
  (expected — they name the live paver), and — **unlike 1.17/1.18, where no
  C# pin moved** — one live-data assertion,
  `KeyInfoCatalogsTests.NicheBridges_LiveInstall_LoadAllAndResolveKnownKeys`,
  which looked up `globalgameevent` key `0x424a` (RoyalSupply). 2.00 deleted
  that row and added four per-faction ones (`0x4308`–`0x430b`), so the lookup
  returned `null` where the test expected paloc `0`. Re-shaped one-for-four,
  matching the Rust `KNOWN_BODY` change, and strengthened to assert the group
  key too. The `runtime.win-x64.Microsoft.DotNet.ILCompiler` central pin did
  **not** move this time (still **10.0.11**, SDK still 10.0.400).

## Feature ledger

The shipped editor surface (generic block/field editor, inventory, sockets,
dye, sealed-abyss, abyss-gates, mount-unlock, knowledge, vendor-buyback,
mercenary-rename, browsers, 32 key-resolver bridges, …) is listed in the
[README](../README.md#editor-features-current). Deep design notes per feature
are in [status-archive.md](status-archive.md).

## Open work / backlog

- **🔴 Dye editor is disabled since 2.01 — needs the mask-group RE.** (Still
  true on 2.03, which added 19 prefabs but kept the 12-byte mask shape.
  Re-measured on 2.03 through the rebuilt dll: 1,645 prefabs / 6,634 slots,
  exactly one group non-zero on 4,304, two on 1,272, three on 73, none on
  985, never four; the legacy getter reads blank on **2,212 (33.3%)**. The
  figures below are the original 2.01 measurement, unchanged on 2.02.) 2.01
  widened `partprefabdyeslotinfo`'s per-slot `mask` from 3 bytes to 12 and
  **re-encoded the contents**: there is no "original three", so no sub-slice
  of the new field is the pre-2.01 one. The twelve read as four groups of
  three — exactly one group non-zero on 4,276 of 6,585 live slots, two on
  1,254, three on 72, none on 983, never four — and **which group a given
  slot uses is not yet reverse-engineered**. Until it is, the legacy
  `..._lookup_slot_mask` / `..._lookup_slot_extra_layer_mask` bridges the
  editor calls are a *partial* read: measured through the release dll over
  all 1,626 prefabs / 6,585 slots, **2,196 slots (33.3%) come back all-zero
  while the full field is non-zero**. The menu item is greyed out
  (`MainWindowViewModel.IsDyeEditorAvailable`, reason in its tooltip) rather
  than shipping an editor that renders a third of the UI blank and writes
  edits from that reading. **To re-enable:** land the group-selection RE in
  crimson-rs, move the C# bridge to the sized-buffer
  `crimson_part_prefab_dye_slot_info_lookup_slot_{,extra_layer_}mask_full`
  entry points (two-call: size with `buf_len = 0`, then fill), then flip the
  flag. Upstream detail: `vendor/crimson-rs/docs/dye-editor-scope.md`,
  "Cross-version drift (2.01)".

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
- **Arabic is never offered as a name language.** The game ships 15 PALOC
  languages (already at 2.02, per upstream), but
  `LocalizationProvider.KnownLanguageCodes` — sourced from the 1.06 install —
  and `tools/analyze/dump_catalogs.py`'s `LANG_TO_GROUP` list 14. The
  missing one is `ara` in group 0033: 39 files, and all of them parse through
  the same loader on 2.03. Adding it is feature work (a right-to-left script
  in the grids, plus a language-menu entry), not an alignment fix.
- **No C# test loads a save the current game wrote.**
  `NativeSaveLoaderTests.FindLiveSave` takes the first of slot0/1/2, which on
  this machine are 2.01-era saves; the 2.03-written ones are slot107 and
  slot102. A save-body drift would reach the editor before the C# suite
  noticed (crimson-rs's own tests do cover it). At 2.03 the two were checked
  by a throwaway probe; picking the newest save by mtime would make that
  permanent.

## Gotchas — don't relearn these

Condensed; the exhaustive list (PALOC keying internals, per-TypeName
name-resolution coverage, nested-edit path mechanics, fill-stack regimes,
window-restore quirks, etc.) is in
[status-archive.md → "Important context / gotchas"](status-archive.md).

- **Foundation-first.** When parsing produces wrong data, fix the parser or
  schema — never add a workaround in a consumer. (CLAUDE.md rule 12.)
- **ABSENT is a value. `_validSocketCount` proves it.** The save's presence
  mask is not just an "is this field written" optimisation — the game uses
  absence *semantically*. `_validSocketCount` is **absent** on an item whose
  sockets have never been opened, and the explicit value `0` does not occur
  once across the 5,556 socket-capable items in four live saves — 5,278 have
  it absent, and the present values are 1 ×27, 2 ×88, 3 ×73, 5 ×90. So "open the first socket" is a
  presence **promotion** (`SetScalarFieldPresent`), not an in-place scalar
  write — the in-place setter rejects an absent field with `NOT_SCALAR (-12)`
  because it has no byte range. Before touching any scalar, check whether the
  game ever writes its zero; if it doesn't, absent is the zero and the two
  mutation surfaces are not interchangeable.
- **A "sentinel" that is really just a data value.** Gem `_currentEndurance`
  reads 65535 on most gems and 100 on the `AbyssGear_*_Special` family, and
  the old note in
  [socket-editor-scope.md](../vendor/crimson-rs/docs/socket-editor-scope.md)
  framed 65535 as a *no-durability sentinel*. It isn't: `_currentEndurance`
  is simply `iteminfo.max_endurance` for that gem key, and 65535 is what that
  field happens to hold for durability-less gems. The 1:1 mapping holds on
  every gem in every live save, with worn gems sitting *below* it (99 / 95),
  never above. Treating it as a sentinel is what let the editor write 65535
  into a gem capped at 100 — a value the engine rejects. When two values look
  like "normal" and "sentinel", check whether one table already explains both.
- **Inside a deferred batch, you cannot promote a field and then write
  it in place.** `toggle_one_scalar_presence_in_place` says so in its own
  comment — *"start/end are stale but the encoder ignores them; they'll be
  refreshed by the re-decode"* — and in a `RunDeferred` batch that re-decode
  only happens at commit. So a follow-up `set_scalar_field_path` on the field
  computes `expected = end - start = 0`, and a 1-byte write fails
  `LENGTH_MISMATCH`. Immediate mode re-decodes after every call and never
  shows it, which is exactly why this reached a green test suite: single Fill
  was covered, Apply-Set (the only `RunDeferred` caller) was not. The way out
  is the presence surface, which writes from `init_bytes` and never reads
  start/end — but `present(1)` on an already-present field is a documented
  no-op, so it takes a `present(0)` + `present(1, value)` pair. **Generalise:
  a deferred batch changes which mutation primitives are valid, not just how
  fast they are. Any code path that only ever runs immediate is untested
  against the deferred contract.**
- **`(BlockIndex, BagIndex, ItemIndex)` does NOT identify an item.** One
  top-level block hosts several item lists, so the equipped list and the
  quick-use reserve list both address their first item as bag 0 / item 0 —
  105 colliding socket rows on slot101 alone. Any item key must include both
  descent **field** indices; `SocketRow.ItemIdentity` is the 5-tuple that
  does. The collision is silent and cross-item: it let an Apply-Set write
  gems into an item the user never selected, and let one item's socket-count
  update mark a *different* item as already-opened.
- **The paver `minor` RESETS on a major bump — never gate on it alone.**
  Game 2.00 took the major 1 → 2 and the minor 18 → **0**. Everything written
  before that (docs, code comments, the C# compatibility check) treated the
  minor as *the* schema key because the major had been 1 on every shipped
  patch since 1.03. That made `IsCompatibleWithParser` silently unsound the
  moment 2.00 landed: with `CompatibleMinors = {0}`, a hypothetical `1.00.xx`
  install matches on minor and would have been let through into a mis-decode.
  The gate is now `(major, minor)`, both ABI-sourced. Generalise the lesson:
  a component that has been constant for 15 patches is an *assumption*, not an
  invariant — the two places that hard-coded a literal `1` (the compat check
  and the mismatch dialog's readout) were both written as if it were the
  latter. The old `TryReadFromInstall_LiveInstall_PinsCurrent` test asserted
  `Major == 1` for the same reason, and broke.
- **A field's POSITION inside an all-zero run is not decidable from bytes.**
  2.00's new `unk_pre_max_endurance_a: u32` lands in a constant-zero region,
  so placing it before `unk_pre_max_endurance`, between the two, or after
  `respawn_time_seconds` all produce **identical bytes and all round-trip
  byte-perfectly**. Only the *value distributions* of the neighbours pick the
  winner: `unk_pre_max_endurance` must stay `0x01000000` on exactly the 59
  `Trade_*_PackedInVehicle` items, and `respawn_time_seconds` must stay
  0 / −1 / 604800. The wrong placements reproduce 1.16's `-4294967296`
  nonsense signature. This is the same lesson as the round-trip gotcha below,
  sharpened: inside a constant run, a round-trip is not just insufficient
  evidence, it is *no* evidence.
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
  (1.14) → 10.0.11 (1.18, SDK 10.0.400) → **10.0.12** (2.02, SDK 10.0.401 —
  it failed even the untouched pre-edit baseline test run). Expect this every
  time the SDK moves; it is not related to the game patch.
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
  **✅ Cleared 2026-08-26** (it was LIVE from 2026-08-18: a 1.17-era `.pyd`
  dated 2026-08-09 parsing 1.18 data). `setup_python_env.ps1` was re-run at
  the 2.00 alignment; the in-tree `crimson_rs.pyd` is now **1,272,832 B dated
  2026-08-26 15:48**, and `vendor/crimson-rs/target-py/` now exists — so the
  1.18 `CARGO_TARGET_DIR` fix below has finally been exercised on this
  machine. Verified by round-tripping the live 2.00 `iteminfo.pabgb` through
  the module: 6,446,719 B / 6,810 items, byte-identical. Expect the trap to
  come back at the next vendor refresh; it recurs because documenting it does
  not run the script. **It did, at 2.02**: after the vendor refresh the
  in-tree `.pyd` was still the 2026-09-04 (2.01-era) build. Re-running
  `setup_python_env.ps1` made it **1,272,320 B dated 2026-09-11 21:39**, and
  the live 2.02 `iteminfo` then round-tripped byte-identical (6,450,232 B,
  6,813 items, SHA256 `e646e4a0…`). **And at 2.03, one step worse**: the
  vendor had been refreshed with no rebuild at all, so the `.pyd` *and* the
  c_abi dll/lib were all 2026-08-28 builds. Re-running both scripts made the
  `.pyd` **1,286,656 B dated 2026-09-18 13:33**, and the live 2.03
  `iteminfo` then round-tripped byte-identical (6,465,724 B, 6,816 items,
  SHA256 `87a1bbcd…`).
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
  suspecting the ABI. The two builds differ by roughly 150 KB, and that gap —
  not any absolute number — is the tell: **don't memorise the byte counts,
  they move with every patch.** At 2.03 the c_abi build is **1,129,984 B** and
  the PyO3 one **1,286,656 B**, a ~153 KB gap (1,136,640 / 1,272,320 at 2.02;
  1,114,112 / 1,272,832 at 2.00; 1,097,216 / 1,274,368 at 1.18).
  Windows Explorer and `Get-Item .Length/1KB` divide by 1024, so they show the
  2.03 pair as **1,103.5 KB** and **1,256.5 KB** — don't be thrown when those
  disagree with decimal-KB (÷1000) figures.
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

- **2026-09-18 — aligned to game 2.03 (one iteminfo drift + a PALOC container); local, not yet shipped**:
  2.03 (paver `2/3/0/0x03045138`) makes two format changes, both absorbed in
  crimson-rs `main` (PR #97, `e932797`; vendored at `234b289`): iteminfo's
  `inventory_info_list` widened `[u16; 9]` → `[u16; 10]` (6,816 items, +3,
  every carried-over item +2 B), and all 585 `.paloc` files (15 languages ×
  39 namespaces) are wrapped in a 0x200-byte header + one LZ4 block, which
  both C-ABI PALOC loaders now unwrap. Neither touched the C ABI surface, so
  the editor's PALOC path needed no code change — the live
  `LocalizationProvider` bootstrap test passing on 2.03 is the end-to-end
  proof. The vendor had been refreshed but not rebuilt (dll, lib and `.pyd`
  were 2026-08-28 builds); after `build_rust.ps1` + `setup_python_env.ps1`
  the untouched suite failed exactly five of 401: the four
  `NativePaverReaderTests` pins and `MissionKey 1000157`, which 2.03 retitled
  back to "Unfamiliar Lands". Editor changes: `VerMinor` 2 → 3; paver pins
  moved to 2.03 (previous-patch guard 2.02 — now a *substantive*
  incompatibility — same-minor-other-major guard `1.03.xx`, future guard
  2.04; the happy path also pins the first live build with a zero top
  nibble, `0x03045138`, through `DisplayString`'s `x8` padding); the
  mission-title pin; the Dye-editor rationale re-measured on 2.03 (1,645
  prefabs / 6,634 slots, 2,212 blank = 33.3%); the quest wrappers' docs note
  2.03's nine retitles; `GameDataLayout` and the Python layout mirror note
  the PALOC container. Verified outside the suite: the Python module
  round-trips the live 2.03 iteminfo byte-identical, 585/585 PALOC files
  parse, and the two 2.03-written saves (slot107, slot102) load through the
  editor's `NativeSaveLoader` with 0 undecoded bytes and re-save
  decode-identical. **401 tests green, 0 skipped**; AOT publish zero IL/trim
  warnings, 4-file bundle with no `crimson_rs.dll`, exe 28,966,912 B
  stamping `2.3.1.27` that launches as `CrimsonAtomtic v2.03.01.27` with no
  mismatch dialog. Found in passing, not fixed: Arabic is never offered as a
  language, and no C# test loads a save the current game wrote (both in the
  backlog).

- **2026-09-11 — aligned to game 2.02 (content-only); v2.02.01 released**:
  2.02 (paver `2/2/0/0xc8925c58`) changed the layout of nothing crimson-rs
  parses: `iteminfo` and `skill` byte-identical to 2.01, 17 of 269 gamedata
  files changed as pure content (a file-by-file SHA256 compare of the kept
  `gamedata-bin/2.01` vs `2.02` confirms upstream's count),
  `partprefabdyeslotinfo` untouched, no save-body drift. Vendored crimson-rs
  `main` at `1753d71` (PR #95: `PARSER_TARGET_GAMEDATA_MINOR` 1 → 2, plus the
  curated quest tables reconciled against the live 2.02 PALOC and keyed by
  `MissionKey` / `QuestKey` behind seven additive C ABI entry points). Before
  anything built, the SDK had moved to 10.0.401 and NU1109 blocked restore —
  ILCompiler central pin 10.0.11 → **10.0.12**. The untouched suite then
  failed exactly the four `NativePaverReaderTests` pins (of 396). Editor
  changes: `VerMinor` 1 → 2; paver pins moved to 2.02 (previous-patch guard
  2.01, same-minor-other-major guard `1.02.xx`, future guard 2.03); the seven
  key lookups wired through `NativeMainQuestChapter` /
  `NativeSideQuestFaction` with a strict `QuestRollupKey` (an unknown kind
  code throws instead of becoming an enum value no `switch` expects) and five
  new tests that walk every row checking the key and title surfaces agree —
  including "In Ashes", two different missions the title lookup cannot tell
  apart; stale wrapper docs fixed (22 → 23 factions, the "Encirlement" caveat
  resolved upstream, 64 of the 84 side-quest rows are missions); the Dye menu
  reads "unsupported since 2.01" instead of "2.01 unsupported". **401 tests
  green, 0 skipped**; AOT publish zero IL/trim warnings, 4-file bundle with
  no `crimson_rs.dll`, exe 28,965,888 B stamping `2.2.1.27` that launches as
  `CrimsonAtomtic v2.02.01.27`. The key lookups have no UI consumer, so the
  AOT link trims them away; the tests (through the cdylib) are what exercise
  them. The Python module was stale again (a 2.01-era `.pyd`) and was
  rebuilt; it round-trips the live 2.02 `iteminfo` byte-identical. Also
  corrected here: v2.01.01 was published 2026-09-04, not left as a draft.
  Shipped the same day: PR #38 (merge `f5a645a`) → tag `v2.02.01` → CI run
  `34608782045` → draft, with the bilingual player-facing notes swapped in
  for CI's assembled body → published 2026-09-11T14:42:44Z, marked Latest.
  The sequence is now written up as the
  「啟動 release CI」 runbook in [release-process.md](release-process.md),
  with a pointer in the root CLAUDE.md so the phrase alone is enough.

- **2026-09-04 — aligned to game 2.01 (rename patch); Dye editor greyed out; vendor script no longer eats this repo**:
  2.01 moved every gamedata file without changing a byte inside it —
  `gamedata/binary__/client/bin/<t>.pabgb`/`.pabgh` →
  `gamedata/binarystaticinfo__/bin/<t>.staticinfobody`/`.staticinfoheader`,
  and each language's single `localizationstring_<lang>.paloc` became one
  file per namespace under `stringtable/binary__/<lang>/`. Rust needed only
  `PARSER_TARGET_GAMEDATA_MINOR` 0 → 1 (vendored from `main`, PR #93, merge
  `5dbeefb`), but the **app was hard-broken**: `LocalizationProvider`
  hardcoded the old directory and all 40-odd filenames, so every bridge
  returned NOT_FOUND on a 2.01 install and no name resolved anywhere — 45 of
  395 tests failing. Fixed at the foundation with a new
  `GameDataLayout` (RustInterop): tables are named by *stem*, and the
  directory + extensions come from a newest-first probe of the group-0008
  manifest, so a kept pre-2.01 install still resolves. Mirrors crimson-rs's
  `src/binary/gamedata_layout.rs`, which is `#[cfg(test)]` upstream and so
  unreachable over the C ABI. Localization needed more than a rename:
  `crimson_paloc_load_from_bytes` takes one blob, so a language is now held
  as its parts behind the new `MultiPalocCatalog`, and the
  `*_lookup_display_name` bridges — each taking a single PALOC *native
  handle* — are offered one part at a time (namespaces don't overlap, so at
  most one answers). Tests share the same resolution through a new
  `LiveInstall` helper. **395 tests green, 0 skipped**; one soft pin moved
  (`MissionKey 1000157` reworded "Unfamiliar Lands" → "Unfamiliar Land", a
  game-side text edit — the 25-character `1000083` title resolves unchanged
  through the identical path). **The Dye editor is deliberately disabled**:
  2.01 widened `partprefabdyeslotinfo`'s per-slot mask 3 → 12 bytes *and
  re-encoded it* (in 5,572 of 6,555 comparable slot pairs the old three bytes
  appear nowhere as a contiguous window inside the new twelve), the twelve
  read as four groups of three, and which group a slot uses is not yet RE'd —
  the legacy getter the editor calls reads all-zero on 2,196 of 6,585 live
  slots (33.3%) whose full field is non-zero. Separately and unrelated to
  2.01, `vendor/update_vendors.ps1` was found able to **rewrite this
  repository**: a leftover `vendor/crimson-rs/` folder with no `.git` passed
  its `Test-Path` check, so every `git -C vendor/crimson-rs …` walked up to
  the parent and rewrote origin / refetched / `reset --hard` `main` onto
  crimson-rs's `main`. It now demands the target hold its own `.git` and
  asserts `rev-parse --show-toplevel` resolves to the vendor path before any
  mutating command; local game saves moved out of `vendor/` to `data/_saves/`.
  **Not tagged or built** — `v2.01.01` is the next concrete task.

- **2026-08-28 — socket editor was writing saves the engine rejects (fixed; shipped as v2.00.02)**:
  Reported symptom — edit an item's sockets in the editor, insert gems, and
  in-game every socket on that item reads back as **未開封 / not yet opened**;
  the save loads but the engine discards the item's socket state. Root-caused
  against ground truth decoded from 22,019 socket-bearing `ItemSaveData`
  blocks across four game-written saves (slot0 / 101 / 104 / 107), which
  yielded three hard invariants the editor was breaking. **(1)**
  `_validSocketCount` is *absent*, never an explicit 0, on a never-socketed
  item — the editor opened sockets with an in-place `SetScalarField`, which
  an absent field rejects with `NOT_SCALAR (-12)`, and the failure was
  swallowed into `row.LastError`; the gem landed in a socket the engine
  considers sealed. Now promoted via `SetScalarFieldPresent`, run *before*
  the gem write so a failure can't strand an orphan gem. **(2)** A socket's
  `_currentEndurance` is the gem's own `iteminfo.max_endurance` (100 for the
  `AbyssGear_*_Special` durability family, 65535 for the rest), not the
  blanket 65535 the editor wrote — which put durability gems above their own
  cap. Now read through the existing
  `LocalizationProvider.LookupItemInfoSummary`, so **no interop change was
  needed**. **(3)** Items were keyed on `(BlockIndex, BagIndex, ItemIndex)`,
  which collides across item lists inside one block (equipped vs quick-use
  reserve, 105 colliding rows on slot101); that silently let Apply-Set write
  into an unselected item and let one item's count update mark another as
  already-opened. Replaced by `SocketRow.ItemIdentity`, a 5-tuple including
  both descent field indices (0 collisions). Six regression tests in
  `SocketEditorTests.cs` pin all three plus the ground-truth invariants
  themselves, so a future schema drift trips them first.
  **An adversarial audit of the fix (36 agents, 32 claims, 19 refuted) then
  surfaced a defect the fix itself introduced**: Apply-Set runs inside
  `RunDeferred`, where a presence promotion leaves the field's byte range at
  `start == end == 0` until commit, so the in-place raise for the second slot
  failed `LENGTH_MISMATCH` and every gem after the first was silently dropped
  — reproducing the sealed-socket state on the one path the tests didn't
  cover. `TryEnsureSocketOpened` now raises a stale-range field through a
  `present(0)` + `present(1, value)` pair. Five more confirmed findings were
  fixed alongside: `ApplyGemPick` returns a bool so Apply-Set stops counting
  aborted slots as changed (it used to journal "5 slot(s) changed" for a run
  that wrote nothing, and overwrite the per-slot error text with a success
  line); `ResolveGemEndurance` now returns `null` rather than 65535 when the
  iteminfo bridge failed to load, and the write is refused — collapsing
  "catalog unloaded" into "unknown key" silently reinstated the blanket
  sentinel on *every* gem; the same-gem short-circuit no longer skips the
  socket repair that `StateLabel` advertises; the count write clamps upward
  so a stale mirror can't lower it; writes are gated on
  `GetMutationVersion` (the Tools dialogs are non-modal over a live main
  window sharing one loader, and a length-changing edit there shifts every
  later element index — a stale row would have dropped a gem into an
  innocent item); and error footers name the item instead of `ItemIndex`,
  which is pinned to 0 for every locator-addressed equipped item.
  **392 C# tests green, 0 skipped**. Also fixed `scripts/setup_python_env.ps1`, which pointed
  maturin's `CARGO_TARGET_DIR` at `vendor/crimson-rs/target-py` — outside the
  crate's `/target` gitignore, so every Python-env build left the vendor
  clone dirty and `update_vendors.ps1` refused to refresh it (the vendor was
  stranded two commits behind at 1.18's `e4261be`). Now `target/py`, inside
  the ignored tree. Two reported UI defects fixed in the same pass: the
  Apply-Set target dropdown ignored the grid filter (702 entries however
  narrow the filter), and `ApplyGemSet` collected its slots from the
  *filtered* `Sockets` view — so with a filter active it silently applied to
  fewer slots than the set, or none. The dropdown now republishes in lockstep
  with the filter and drops a selection the filter hides; the apply reads
  `_allSockets`. The dialog's header and tooltips still described the old
  "reset endurance to 65535" behaviour and were rewritten in all three
  languages. **Socket editor now defaults to a wearable-items-only
  view.** The save format hands a full 5-entry `_socketSaveDataList` to
  items that are not equipment at all, so the raw list was two-thirds
  props — on slot101, 702 items / 3,510 slots collapses to **237 items /
  1,185 slots**, dropping Gold Bar ×59, ores, food, arrows and beetles
  while keeping every weapon / armour / helmet / gloves / boots / ring /
  earring / necklace / cloak / lantern / broom / mask. The
  discriminator is gamedata's `equip_type_info != 0`, deliberately
  **not** `use_socket` / `socket_valid_count` — gamedata forbids sockets
  on rings, yet force-modding them works in-game, so a
  socket-capability test would hide items the user legitimately edits.
  It is a checkbox, not a rule: the point is signal-to-noise, and
  nothing it hides becomes unreachable.

- **2026-08-26 — game 2.00 alignment (first MAJOR bump; local, unreleased)**:
  Crimson Desert went 1.18 → **2.00**, the first major-version bump since the
  project started. The headline is not either iteminfo drift but the
  **version-model change**: `meta/0.paver`'s minor *resets* across a major
  (18 → **0**), so the minor alone stopped identifying a schema. crimson-rs
  had already landed 2.00 support on `dev` and it was merged to `main` during
  this session (commit `0f2363b`, PR #91, merge `8e942d7`), adding
  `PARSER_TARGET_GAMEDATA_MAJOR = 2` and a purely additive
  `crimson_parser_target_gamedata_major()` bridge; `update_vendors.ps1`
  (tracks `main`) pulled it to `8e942d7` and `build_rust.ps1` rebuilt the
  c_abi cdylib. **The editor side had a real bug waiting**:
  `GameDataVersion.IsCompatibleWithParser` was a minor-only
  `Array.IndexOf(CompatibleMinors, Minor)`, so with `CompatibleMinors = {0}`
  a hypothetical `1.00.xx` install would have been reported compatible with
  the 2.00 parser and let into a mis-decode. It now gates on
  `Major == ParserTargetMajor` first, with a dedicated regression test for
  that exact shape (same minor, wrong major). `GameVersionMismatchDialog`
  carried the same latent defect in its readout — a hard-coded `"1."` prefix
  that would have rendered the 2.00 target as `1.00.xx`. Both majors are now
  ABI-sourced, so the next major bump needs no code edit. Data side: iteminfo
  6,573 → **6,810** items (+237, none removed; `SubItem` tag 17 → 18, plus a
  new always-zero `u32` ahead of `unk_pre_max_endurance`), skill 2,027 →
  **2,046** with zero drift, **no** save-body drift, gamedata 30 tables /
  96,997 keys. Version bumped `VerMajor` 1 → 2 / `VerMinor` 18 → 0 /
  `VerPatch` → 1; the AOT exe stamps `2.0.1.17` and the UI renders
  `v2.00.01.17`. **382 C# tests green, 0 skipped**; AOT publish zero IL/trim
  warnings (27.8 MB exe); the Python toolchain was refreshed
  (`setup_python_env.ps1`) and round-trips the live 2.00 `iteminfo.pabgb`
  byte-identical (6,446,719 B). Two C# pin groups moved: the
  `NativePaverReaderTests` version pins, and — the first C# count/value pin to
  move since 1.16 — the `globalgameevent` RoyalSupply assertion, because 2.00
  deleted key `0x424a` and split it into four per-faction rows
  `0x4308`–`0x430b` (re-shaped one-for-four, matching the Rust `KNOWN_BODY`
  change, and strengthened to assert the group key). New gotchas recorded: the
  minor-resets-on-major trap, and that a field's **position inside an all-zero
  run is not decidable from bytes** — 2.00's new `u32` round-trips
  byte-perfectly in three different placements, and only the neighbours' value
  distributions pick the right one. The stale-`.pyd` trap, flagged LIVE on
  2026-08-18, is **cleared**. Shipped the same day: commit `7179ee2` → PR #30
  (merge `bb78dd8`) → annotated tag `v2.00.01` (`--cleanup=verbatim`, both
  `## Highlights` and `## 重點` verified present before pushing) → CI run
  `32945531894` → notes trimmed to the bilingual sections → **published
  2026-08-26T08:06:44Z**, superseding v1.18.01.
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
