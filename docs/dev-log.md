# Dev Log

Append-only history of what shipped, newest first. **Reading tip:** grep `^## ` for the index, then
read from the top.

> **Looking for current state?** [status.md](status.md) holds where the project stands, the next
> task, the backlog and the gotchas; this file records *what shipped and why*.
> [status-archive.md](status-archive.md) is the frozen history before 2026-06-12.

## How to write an entry

- **When:** in the same PR as the work, before it is committed and merged. A release gets its own
  entry when the runbook records it ([release-process.md](release-process.md)).
- **Heading:** `## YYYY-MM-DD (vX.YY.ZZ) — what changed`, or `(no release)` for work merged
  between releases. Say what changed in plain words, not the commit subject.
- **Body:** bullets, roughly in this order, each only if it has something to say:
  - **What changes:** the behaviour a player or maintainer sees.
  - **How:** the mechanism, with the PR, the crimson-rs PR and vendored commit, and the C ABI added.
  - **Found on the way:** a fact about the game data or the save worth not relearning (and copy it
    to status.md's gotchas when it should steer future work).
  - **Not verified:** what has not been checked in-game or on a release build.
  - **Tests:** the suite count, and whether the AOT publish is clean.
  - **Release:** tag commit, CI run, exe version, zip size and sha256.
- **Never edit an old entry** except to fix a broken link. A later correction is a new entry that
  names the old one.
- **Split:** past about 400 KB, move the oldest entries unchanged to
  `archive/dev-log-<yyyy-mm>-pre-vX.YY.ZZ.md`, and list it here.

-----

## 2026-11-28 (no release) — Code audit P6: game-data caches and the threads that read them

- **What changes:**
  - **Portraits:** concurrent loads wait for the one read of the game's portrait list instead of
    seeing "attempted" before it exists and caching "no portrait" for good; only a definite "this
    character has none" is remembered, so a failed extraction is retried (B3-02).
  - **Cache files** (icons and portraits) are written under a temporary name and renamed, so a
    write cut short is never taken for a cached image; a cached portrait that does not decode is
    deleted and extracted again (B3-03).
  - **Cache versions:** each cache folder records the game build it was filled from
    (`cache-stamp.txt`). After a game update the portrait cache is emptied (it refills on demand)
    and the next icon extraction extracts every icon again; the stamp is written only after a
    clean run (B3-07).
  - **DDS headers** from game files are checked before any size derived from them is allocated
    (sides 1..16384, sizes in checked arithmetic) (B3-06).
  - **Re-bootstrap** releases every table first, so a new install no longer keeps the previous
    one's PALOC, mission map or merged DLC rows; a secondary language is reloaded from the new
    install or dropped when it does not ship it (B3-01). Switching the secondary language releases
    the old one after half a minute, once lookups already running on it are done (B3-10).
  - **Concurrent readers:** the per-language maps background readers use (portraits, Known Dyes)
    are concurrent dictionaries, and a lookup reads the secondary language once (B3-05).
  - **Block reads** decode outside the cache lock; the result is cached under the version taken
    before the read, so an edit landing meanwhile is never served stale (B1-15).
  - **The change journal** posts a write from another thread to the thread it was created on,
    where its UI binding lives (B2-28).
- **Left open:** B1-14 (a PAMT parse per extracted file) needs a crimson-rs API; B1-06 (DLC
  merges are not safe against concurrent lookups) holds while merges run only during a
  bootstrap, which moves off the UI thread in P7.
- **Tests:** 479 green (+3 DDS header cases).

-----

## 2026-11-28 (no release) — Code audit P5: values the game writes, and only those

- **Save scan first.** Twelve live saves (2.06) settle what several fixes depend on: every dye
  element stores slot 0 as an absent `_dyeSlotNo`, `_grimeOpacity` is absent on all of them and
  `_dyeColorA` is a present 255; a store's sold list is absent or non-empty, never present and
  empty; 60 of 12,600 `CharacterKey` values use the high byte (the hashed character rows).
- **What changes:**
  - **Dye:** a row's Apply writes only the channels the user changed, so an absent grime stays
    absent; a new colour on a slot without alpha gets 255, as the game writes it; after Apply the
    row takes the written values as the save's, so changing one back writes again; slot 0 is
    written as an absent `_dyeSlotNo` (B4-04, B4-05, B4-14). The palette button says a theme is
    needed instead of doing nothing, and the dye list keeps the dyed items when it caps at 500.
  - **Vendor Buyback:** removing a store's last entry makes the list absent (B4-15).
  - **Generic editor:** writing a present zero into an absent field and "Make absent" both ask
    first (B2-15); f32 / f64 / vector values refuse NaN, infinity and out-of-range numbers (B2-16).
  - **Sockets:** a custom gem set keeps each gem in its socket's position (an empty cell leaves
    that socket alone), refuses a key that is not in the engine's canonical gem set, and Apply-Set
    refuses a non-gem too (B4-16, B4-17).
  - **Stack fill** stops at max for items iteminfo flags with `ApplyMaxStackCap` (B4-13); "Fill
    to max" is offered on `_stackCount` only; Add Item says when the save has no knowledge list.
  - **World editors:** the Abyss gate Lock / Unlock buttons follow the row's state (B5-04); the
    faction-node ★ shows, since `_isCapital` is a bool (B5-05); Faction Relations no longer
    rewrites one-sided rows the user did not touch (B5-09); Rename refuses an unchanged, empty
    (the game stores "no custom name" as absent) or over-64-byte name (B5-12).
  - **Shared parsing:** six copies of the integer parser are one `SaveFieldValues.TryParseUInt`,
    plus `TryFlag` for bool fields; the world map asks the resolver for both names instead of
    splitting a formatted string on " / " (B5-20). A name that only exists as an internal name
    no longer shows as "X / X" (B3-18).
  - **Interop:** list reads keep the count the fill call reports and refuse a list that grew in
    between, and block JSON and name reads decode the fill's own length (B1-04); the 3-byte dye
    mask wrappers, whose meaning the 12-byte mask replaced, are gone (B1-13); `IItemInfoCatalog`
    has `LookupSummary` / `LookupFlags` (B1-23).
- **Left open:** B2-24 needs crimson-rs to report the type of an absent scalar; B3-08 and B3-12
  need a data check; B5-07 and B5-14 need an in-game check.
- **Tests:** 476 green (+8 non-finite float cases, +1 stack cap).

-----

## 2026-11-28 (no release) — Code audit P4: failures are reported, not swallowed or fatal

- **What changes:**
  - **Nothing a handler misses ends the process.** The dispatcher's unhandled-exception event,
    unobserved tasks and fire-and-forget faults all go to `AppErrors`, which shows an alert; the
    edits stay in memory (B2-19, B4-20, B5-08). The four copy buttons share one handler that
    tolerates a busy clipboard (B3-09).
  - **Lookups say "none" only for "not there".** Catalog lookups return null / 0 for
    `NOT_FOUND` / `OUT_OF_RANGE` and throw for any other code, where they used to turn a panic into
    "unknown character" or an early end of a list (B1-07). The PAZ directory probe folds only "not
    there" into null (B1-08). The paver reads survive a missing or stale DLL as documented (B1-09);
    the compatible-minor list is read-only (B1-10). An item path longer than two steps, or an enum
    value this build does not know, throws instead of becoming a wrong path or a default branch
    (B1-11, B1-12).
  - **iteminfo** falls back to the body-only loader only when that names items; a 2.06 header the
    parser refused no longer turns into a catalog of nameless items (X-02).
  - **Load failures are visible.** LocalizationProvider records every table that failed to load
    ("where: what (CODE)"), the status bar counts them and its tooltip lists them (B3-22). A
    broken DLC costs its languages, an unreadable DLC folder or gamedata manifest no longer stops
    the start-up (B3-11).
  - A field edit marks the document dirty and journals before re-reading the block, and a failed
    re-read shows in the details pane (B2-07).
  - Scans name how many blocks they could not read (sockets, buyback, fill-all) and stop asking
    again for the same block; the Sealed Abyss Mark refuses to stamp a completion time when a
    quest block cannot be read, since it could sort before an existing one (B4-23, B5-15).
  - The basemap loader no longer swallows out-of-memory (B5-24).
- **Tests:** 466 green.

-----

## 2026-11-28 (no release) — Code audit P3: multi-step writes land whole or not at all

- **The rule:** inside a deferred batch crimson-rs keeps the ops before a failure applied (it has no
  savepoint), so a batch body that catches a failure and returns commits a half-made edit. Every
  multi-step edit now throws on failure so `RunDeferred` rolls it back, and `ISaveLoader` says so
  (B1-02). `RunDeferred` reports a failed rollback with the original error as the inner exception
  and whether the batch is still open (B1-17); a rolled-back batch restores `HasStructuralEdit`
  (B1-16); a batch failure no longer reports op 0 when no op was at fault (B1-03).
- **What changes:**
  - **Add Item, Grant and Contributions** clone and patch the new item in one batch, so a failure
    no longer leaves a copy of the template with its `_itemNo` (which the game rejects). Field
    bytes come from each field's own type tag, `_itemNo` is unique across the whole block, and a
    failed "new" mark is a warning, not a failed add (B4-09, B4-12, B4-30).
  - **Socket editor:** opening a socket is accounted for even when the gem write then stops, and
    re-picking the gem already in an unopened socket opens it, in Apply-Set too (B4-07, B4-19). A
    gem change writes key and durability in one call (B4-18). Apply-Set is all or nothing: a
    failing slot rolls the whole set back, the rows are restored to match, and the journal line is
    written only after the commit (B4-08).
  - **Dye editor:** Add Dye, Add Slot and a row's Apply each run as one batch; a missing
    `_dyeSlotNo` is a failure instead of a silent success; a row's failure is not overwritten by
    "Applied"; the editor is marked dirty only after the commit (B4-02, B4-03, B4-06).
  - **Sealed Abyss and knowledge:** a single Mark is one batch. The bulk Mark and the knowledge
    inject run the whole sweep in one batch; when one entry fails, the batch rolls back and the
    entries before it are applied again in a second batch, so each challenge or key lands whole
    and the usual run still costs one re-decode. A knowledge field the template has absent is
    promoted instead of failing (B5-03, B5-06).
  - **Faction Relations** writes both sides of a change in one batch, so a relation is never left
    one-sided; a group key takes the field's own width. **The dragon insert** rolls back when the
    new dragon cannot get its own `_mercenaryNo` (B5-06).
  - **One writer at a time:** `WriteGate` serialises every background edit for its whole flow (an
    edit that calls another does not wait on itself), and the socket, dye and buyback editors
    refuse to write while one runs (B1-05, B4-21, B5-13).
  - crimson-rs accepts the same handle as transplant source and target, so the C# ban is gone (X-05).
- **Found on the way:** the first `WriteGate` was an `async` method that set its `AsyncLocal`
  depth inside itself; that change does not flow back to the caller, so a nested edit (Add Item
  learning the item's knowledge) would have waited on itself forever. Its test caught it.
- **Not verified in the app:** Apply-Set rollback and the bulk Mark retry need a failing write to
  show; no live save produces one.
- **Tests:** 466 green (+1 `WriteGateTests`).

-----

## 2026-11-28 (no release) — Code audit P2: tool windows never write through a stale snapshot

- **What changes:** tool windows address items by element index, and an insert or remove made
  elsewhere shifts those indices, so a write could land on a different item. Two guards now stop
  that.
  - **Another document:** Open, Save As and Restore raise `DocumentReplaced`, and the main window
    closes every window it owns. Before, a tool kept writing by the old indices into the newly
    loaded save, and read through the old file path (B2-03, B5-01).
  - **The same document changed:** `SnapshotGuard` compares the loader's mutation version (which
    now only moves forward) with the one a tool's rows were built at. The dye list rescans
    instead of opening or adding on a stale row; the dye slot editor and Vendor Buyback refuse a
    stale write and resync after their own; Find Items' Go rebuilds the list and finds the item
    again by inventory and item number; the two bulk fills, Contributions and the Sealed Abyss
    dialog re-check the version after their confirm dialog (B4-01, B4-10, B4-11, B5-02).
  - **The main view** rebuilds itself when the version moved without a main-window edit, so its
    rows follow edits made in tool windows (the rest of B4-11).
  - The Sealed Abyss dialog applies only ticked rows the filter shows (B5-10) and rescans after
    every run, a failed one included.
  - The icon and portrait providers stop handing out or caching Bitmaps once disposed, so a picker
    still holding a replaced provider leaks nothing (B3-04).
- **Not verified in the app:** that closing the tool windows on Open feels right; the guards
  themselves have tests.
- **Tests:** 465 green (+2 `SnapshotGuardTests`).

-----

## 2026-11-28 (no release) — Code audit P1: unsaved edits, Save failures, backups and restore

- **What changes:**
  - **One dirty state.** `IsDirty` follows the loader's mutation version (`RefreshDirty`), and the
    main window re-derives it every second, so edits made in tool windows count before the tool
    closes; the close prompt asks the same state instead of the journal (B2-14, B2-06). Add Dye
    also journals its edit.
  - **No edit is dropped without asking.** File → Exit closes the window through its Closing
    prompt instead of shutting the lifetime down (B2-01); Open and Restore ask Save / Discard /
    Cancel first (B2-02).
  - **A failed write no longer crashes the app.** Save and Save As report the failure, with the
    crimson-rs code name, and keep the document dirty; a Save As whose reload fails says the file
    was written (B2-04).
  - **Save checks the file first.** When the file changed on disk since the editor read or wrote
    it (the game saved), Save asks before overwriting (B2-09); when the backup fails, it asks
    before writing without one (B2-08). The write itself is crimson-rs's temp file + rename.
  - **Backups** capture the file about to be overwritten under its own name, so a
    `save.edited.save` is backed up too (B2-05); record the full source path; skip a backup equal
    to the newest; are named in UTC (`…Z`, older local-time folders still read) and never prune
    the backup just written (B2-10).
  - **Restore** goes back to the recorded source folder; an Unknown-platform backup without one is
    refused instead of landing in the first launcher's tree (B2-11). The slot's current files move
    to a pre-restore snapshot (outside the retention cap, listed as "before a restore") before the
    backup's files are copied in by temp + rename, so no stale `lobby.save` survives and a failed
    restore puts the files back (B2-12). The list names the platform and survives an unreadable
    folder (B2-13); only one picker and one restore run at a time (X-07).
  - **Settings** are written by temp + rename; a corrupt `settings.json` is kept as
    `settings.corrupt-<utc>.json` instead of being overwritten by defaults (B2-17).
- **Not verified in the app:** Exit with unsaved edits, the external-change prompt after an
  in-game save, and a restore over a slot with a newer `lobby.save`. The service paths have tests.
- **Tests:** 463 green (+6 backup tests: pre-restore snapshot, own-name backup, duplicate skip,
  never pruning the new backup, restore folder, UTC stamps).

-----

## 2026-11-28 (no release) — Code audit P0: crimson-rs ebf7304, error codes, HMAC re-sign, comment rules

- **What changes:** the 2026-11-28 code audit (168 findings in six blocks) is verified and tracked in
  [code-audit-2026-11-28-control.md](code-audit-2026-11-28-control.md); this is its first fix batch.
  - crimson-rs is vendored at `ebf7304` (96 commits, crimson-rs's own audit). No export was removed
    or renamed and no signature changed: all 298 C# entry points match (checked mechanically).
  - The mutation version now only moves forward: every in-batch mutation bumps it, commit bumps it
    again, and an abort that rolled changes back moves it forward. That alone fixes B1-01, the
    details cache serving rolled-back data after an abort. Four deferred tests pinned the old
    contract (abort restores the version, commit bumps exactly once); they now pin the new one, and
    the abort test also reads inside the batch, which is the case B1-01 described.
  - `CrimsonErrorCode` is the one public list of status codes and names. The eight per-file name
    tables delegate to it, and it has the three new codes: −26 `LIST_PARTIALLY_DECODED`,
    −27 `ELEMENT_CLASS_MISMATCH`, −28 `HMAC_UNVERIFIED` (B1-18).
  - crimson-rs now refuses to write a save whose HMAC failed on load. Save and Save As ask first
    (the file may be damaged or edited by another tool), then call the new
    `crimson_save_set_allow_hmac_rewrap`; headless hosts never re-sign (X-01).
  - The five dye-slot status and journal strings the slot editor used had no entry in any language
    (B4-24); they exist in all three now.
  - Comment rules from UE5CEDumper, adapted: [code-comments.md](code-comments.md), gated by
    `CommentRulesTests`. Line references, audit / PR / commit ids and stacked `<summary>` blocks
    must be zero; history phrases are a per-file ratchet (43 lines in 22 files at the start, target
    zero). P0 cleared 10 stacked doc blocks (each moved onto its member, or dropped when the member
    is gone) and 25 PR / commit ids.
- **Found on the way:** status.md's gotcha "you cannot promote a field and then write it in place
  inside a batch" is no longer true with this crimson-rs (the length is checked against the schema
  size); it now states the current deferred contract. The audit agents' leftovers are done: the
  test baseline (453 green at `fad6d47`), the full symbol comparison, the string-key comparison
  (852 keys, identical in all three dictionaries) and three report errata.
- **Tests:** baseline 453; with the new crimson-rs 4 deferred tests failed on the old contract and
  pass on the new one; `CommentRulesTests` adds 4.

-----

## 2026-11-28 (no release) — Every CLAUDE.md is capped at 40 KB

- **What changes:** root CLAUDE.md's Rule 0 now caps every `CLAUDE.md` at 40,960 bytes, the limit
  UE5CEDumper uses: the file is loaded on every turn, so over the line text is trimmed or moved, not
  added. New docs get their row in docs/CLAUDE.md; root keeps only pointers every session needs.
- **How:** `ClaudeMdSizeTests` finds every `CLAUDE.md` outside `vendor/`, `out/`, `bin/`, `obj/` and
  `.git/` and fails when one is over the cap. At the time: root 6.3 KB, docs/ 2.8 KB, tools/ 4.6 KB.
- **Found on the way:** root CLAUDE.md still said the live install is 2.05; it now says 2.06.

-----

## 2026-11-28 (no release) — status.md's "Current state" rewritten for 2.06

- **What changes:** the section still described game 2.03 as "committed on `dev`, not pushed"
  and v2.02.01 as the current release. It now states the 2.06 / v2.06.02 state; the old text
  is kept below unchanged.

<details><summary>The retired section, as of 2026-09-18</summary>

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
- **crimson-rs 2.03 and the absence-marker fix are on `main`, and vendored.** The 2.03 support is PR #97
  (merge `332c47b`: `e932797` — `PARSER_TARGET_GAMEDATA_MINOR` 2 → 3, the
  10-slot `inventory_info_list`, `binary::paloc::unwrap_container` in every
  PALOC reader, and the curated quest tables' 2.03 retitles), and `main` has
  since moved to `234b289` (PR #98, fork-guard scripts only) and `b1b687b`
  (PR #99, absence markers + element templates — the dragon fix). The vendor
  copy sits at `b1b687b`; `build_rust.ps1` rebuilt the c_abi dll + staticlib
  from it and `setup_python_env.ps1` the Python module. CI clones `main`
  fresh at tag time, so a `v2.03.01` tag ships both. (The 2.02 support was
  PR #95, merge `3296b5c`.)
  **Still no version tags**, now for 1.18 through 2.03 (re-checked
  2026-09-18: `git ls-remote --tags origin` returns 0 refs): the `v1.0.10.x`–
  `v1.0.17.x` tags exist **only in the local clone** at
  `D:\Github\crimson-rs` — `git ls-remote --tags` against the fork returns
  **nothing**, so none were ever pushed. "Parity with 1.13–1.17" therefore
  means parity with local-only tags; decide whether to push the whole set,
  keep them local, or stop cutting them.
- **Health:** full suite green this session (**402** C# tests, 0 skipped, 0
  failures — 401 for the 2.03 alignment, plus the newest-save rewrite test)
  against the live 2.03 install, with the native lib rebuilt from the
  vendored crimson-rs `b1b687b`. The count stayed 402 through the dragon fix
  (tests renamed and re-pinned, none added).
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

</details>

-----

## 2026-11-28 (no release) — The dev log takes over status.md's history

- **What changes:** this file is new. status.md's per-version narratives (2.00 to 2.06) and its
  session changelog moved here unchanged, below; status.md keeps the current state, the backlog
  and the gotchas.
- **How:** a script cut the blocks out of status.md, removed the blockquote markers, gave each
  version its own heading, and checked that every moved line is in this file.
- CLAUDE.md, docs/CLAUDE.md, [dev-workflow.md](dev-workflow.md),
  [release-process.md](release-process.md) and status-archive.md now point new history here.

-----

## 2026-11-27 (no release) — Cross-PC working notes recorded in the repo

- **What changes:** [dev-workflow.md](dev-workflow.md) gained the rules that lived only in one PC's
  agent memory: push to `origin` only; how to ask the crimson-rs session for a new C ABI; throwaway
  `Zz*` probes; test-save backups, restores and rebuilds; reading in-game screenshots; agent
  working notes. CLAUDE.md gained the push-target and image-size lines. PR #63, merge `a336412`.

-----

## 2026-11-27 (v2.06.02) — Dye editor back, Known Dye Colours, faction editors, Abyss gate locations

- **What changes:** the dye editor is re-enabled, with slot add / remove and a free colour picker;
  Tools → Known Dye Colours unlocks a dye theme's palette; Edit Faction Relations, Edit
  Contributions and a readable Edit Faction Nodes; the Abyss gate editor shows each gate's region
  and whether it is on the ground or in the sky; Add Item also learns the item's knowledge, so the
  item is not shown as "Unknown item". PRs #57 to #60; Avalonia 12.2 and a NuGet refresh in #60.
- **Found on the way:** the dyehouse palette is gated per colour by six knowledge keys per theme,
  and a dyer offers only their own theme; the journal's "dye recipes" page counts the 50 dye
  items, a separate collection. Details in the entry below and in status.md.
- **Not verified in-game:** dye slot add / remove, Add Item learning knowledge, the Abyss gate
  columns, relation-group edits.
- **Release:** version 2.6.1 → 2.6.2 in PR #61; tag → `b9034bb` (PR #61 merge); CI run
  `46005852913` (3 m 44 s); crimson-rs `0a551a6`; exe `2.6.2.36`; zip 22,155,661 B, sha256
  `f850854b…08ffd`, equal to the asset's GitHub digest and the run log. The downloaded zip was
  checked before publishing: the `.sha256` matches, it holds the four expected files with no
  `crimson_rs.dll`, the exe's ProductVersion ends in `b9034bb`, and it launches as
  `CrimsonAtomtic v2.06.02.36`. Published 2026-11-27T13:22:50Z, marked Latest; recorded in PR #62.

-----

## 2026-11-27 (v2.06.01; the features shipped in v2.06.02) — Game 2.06: names move into the row headers; dye, faction and Abyss gate editors

**Tools → Edit Contributions (new, 2026-11-27, on `dev`).** The faction
contribution currencies (`Contribution_Hernandian` 1001754, `_Graymane`,
`_Demenissian`, `_Pailunese`, `_Delesyian`, `_Tashkalpan` on 2.06) are
plain `ItemSaveData` stacks in `_inventorylist` container 1 ("Camp &
Contributions"). iteminfo gives them no tag, but they and nothing else
carry `category_info` **60001** (`item_type` 32 is shared with copper and
camp resources), so `ContributionService` reads the catalog from iteminfo.
The dialog sets stacks the save holds and adds absent ones by cloning an
existing contribution stack (same mask profile) with a fresh `_slotNo`,
an `_itemNo` unique across the whole inventory block, and the engine's
`_transferredItemKey`. 0 on an existing stack is refused (whether the game
accepts a 0-count currency row is unknown). `ContributionServiceTests`
adds the absent Hernandian stack to a copy of the newest save and reads
it back. **413 C# tests green**; AOT publish has zero IL/trim warnings.
Not checked in-game yet (slot103 below carries 10,000 Hernandian).

**Tools → Edit Faction Relations (new, 2026-11-27, on `dev`).**
`FactionRelationService` lists every faction with its relation to the
player's (the element led by `CharacterKey 1`) and writes changes both
ways: retype a row, Neutral = a row with `_relationTypeRaw` absent (the
game's shape), add rows to a faction with none (`SetObjectListPresent`
or a clone, `_dueDate` `0xFFFFFFFF`), and Group default = remove the rows
(a faction's last row makes the list absent again). Relation-group names
now resolve (`FactionRelationGroupKey`, new C# bridge over the existing
`crimson_factionrelationgroup_*`). `FactionRelationServiceTests` runs all
four shapes on a copy of the newest save. The dialog also shows each
faction's **member count** (characterinfo's faction field, via crimson-rs
PR #117 `crimson_characterinfo_faction_member_count`, vendored at
`9827361`) and sorts by it, plus the localized faction-group name
(`crimson_faction_info_lookup_display_name`: Celeste_Hernand → "Hernand",
Graymane → "Greymanes"); pinned by `MemberCountsAndGroupNames_Live`
(66 / 172 / 2 / 0). With #117 characterinfo reads its rows from the 2.06
header, so `entry_count` is 7,315 (359 hashed-key rows); the suite stayed
green. **416 C# tests green**; AOT zero warnings. **In-game, 2026-11-27:**
rows **added** to a faction that had none work and override the relation
group — player ⇄ Faction_Bandit (no rows, Monster_Common) set to
BloodAlly through `FactionRelationService`, and bandits no longer attack.
The user opened the dialog: its "Name" column is the factiongroup, i.e. a
region ("Hernand" for Faction_Bandit too), which does not identify a
faction. Fixed: factions ship no name, but their members do, so
`LocalizationProvider.FactionMemberNames` (via
`crimson_characterinfo_lookup_faction_key`) gives the most common member
names — Faction_Bandit "Bandit / 盜賊 ×164", Celeste_Hernand "Hernandian
Guard / 埃爾南德警衛隊 ×49" — shown first as "Typical members"; the
factiongroup column is now "Region". `ResolveFactionLabel` reuses it.

**Edit Faction Nodes — readable.** The user found the 1,262 `Node_Her_*`
internal names unreadable. Now: a Place column (crimson-rs PR #118,
vendored at `ec7cd83`: each node names a knowledge row whose title is
the place — `crimson_factionnode_lookup_display_name`, 1,120 of 1,262
nodes named, e.g. Node_Her_GlenbrightFarm → "Glenbright Farm / …"),
State / Region / Conqueror filter combos, conquerors by member name.
crimson-rs found **no** owning-faction or region field, so Region is a
guess from the internal-name prefix (Her → Hernand, Dem, Del, Crim, Kwe,
…) and the column says so.

**DLC rows merged (2026-11-27).** crimson-rs PR #119 (vendored at
`7c72e20`) lets the factionnode / knowledgeinfo / faction / characterinfo
handles merge a DLC copy (`*_merge_dlc_from_bytes`, appended, base rows
win on shared keys). `LocalizationProvider.TryMergeDlcTables` merges every
installed DLC at bootstrap, so the 142 dlc_01 nodes get place names
(Node_Ocean_IliyaIsland → "Iliya Island"), DLC factions resolve
(1000099 Faction_Satyros, 9 members) and member counts include DLC
characters (Greymanes 172 → 187). Side effect: Browse Characters and the
knowledge list now also list DLC rows on a DLC install. Contribution
currencies are confirmed complete at 6 (dlc_01 adds none). **423 C# tests
green**; AOT zero warnings; dialogs not opened by hand after these
changes. Dye-mask RE: queued as its own crimson-rs session (the user
starts it); it reports straight back here.

**Follow-ups from the user's review (2026-11-27, on `dev`).** Relation
editor: search also matches the Current column, plus a Current-relation
filter combo (typing 血 used to hit 赤血沙漠 in Region instead of
BloodAlly rows); the Relation group column is now an editable combo —
a faction's `_relationGroupKey` (u16) is in the save
(`FactionGroupChange`, pinned by `Apply_ChangesARelationGroup_AndReadsBack`);
**not yet tested in-game**. Node editor: region labels localized from
the game's faction-group names (`FactionGroupNames`: Hernand / 埃爾南德,
Great Ocean / 大洋, …); Kweiden and Calphade are not groups and have no
standalone PALOC string, so they stay English. The group-to-group matrix
is game data, not save data: crimson-rs decoded it (list1 = War, list2 =
BloodAlly; PR #121, vendored at `aa5aa53`) and
`LocalizationProvider.GroupRelation` wraps
`crimson_factionrelationgroup_lookup_relation`. The player's Graymane is
War with HostileCombat / Monster_Common and BloodAlly with FriendlyCombat
/ NPC_Common. The editor now shows "Group default → War" in Current (and
as a filter choice), plus a "Group default" column that follows the
group combo, so a regroup previews its effect. **425 C# tests green**;
AOT zero warnings.

**Faction relations — `1` = War confirmed in-game (2026-11-27).**
`FactionSaveData._factionElementSaveDataList[i]._factionRelationDataList[j]`
= `{_targetFactionKey, _relationTypeRaw u8, _dueDate u32}`. The exe's
name table right after the "FactionRelationGroupInfo … must be the same
Relation" check reads **0 Neutral, 1 War, 2 Agreement, 3 BloodAlly,
4 ByGroup**; the save fits it (every pair is symmetric; `_dueDate` is
always `0xFFFFFFFF`; the only `1` is the Sandfang ⇄ Savage Fangs bandit
pair; the player is `3` with Faction_Roberts / Serkis / GoldleafGuildhouse
and others, `2` with Faction_Alfonso, absent with Marchell / Elimoor /
Byron / SilvermoonTroll). Hostility toward most enemies comes from the
faction's relation group, not from these rows. The save holds no 0–100
faction "reputation" value. **In-game tests:** (1) player ⇄
Faction_Roberts (1000001) `3` → `1`: no reaction at their stronghold —
but only **2** characterinfo rows belong to that faction, so it proved
nothing. The 10,000 Hernandian contribution added by Edit Contributions
**showed in-game** (2026-11-27). (2) Now in slot103: slot102 with player
⇄ **Faction_Celeste_Hernand (1000131)** `3` → `1` both ways, plus the
10,000 Hernandian; all 47 Hernand soldier rows (the in-game "Hernandian
Guard") belong to it — **the guards attacked on entering Hernand: `1` is
War.** The other values rest on the exe table plus the save's pattern,
not yet on an in-game test. Practical upshot: what decides whether a
place turns hostile is the faction its rank-and-file NPCs belong to (66
characters for Celeste_Hernand), not the named family faction (2 for
Roberts) — so a relation editor must show each faction's member count.
A character's faction lives only in characterinfo (the save records
it for the player alone): a u32 at 2.06 body-row offset 159, always a
faction key or 0 — found by a scratch scan; asked crimson-rs to parse and
expose it (plus faction group keys and their PALOC display names).
The previous slot103 is in
`data/_saves/slot103-before-faction-test-20261127/`. NPC friendliness
(`FriendlySaveData._friendlyDataList[i]._levelData`) is `_exp` 0–100;
`_level` is 0 on all 67 NPCs, including the 19 at 100.

**FactionKey names were wrong (fixed 2026-11-27).**
`FactionKey` resolved through PALOC type byte `0x30`, which is
`character.paloc`'s; factions and characters share the 1,000,000+ key
range, so the player's faction 1000000 read as `CharacterKey 1000000`
"Hernandian Guard" and six factions as "Dusksong". Faction rows carry no
localized name (`faction.paloc` holds factiongroup / operation strings,
not faction-keyed ones), so the fix is a name-only `faction` table bridge:
crimson-rs `src/faction_info` + `crimson_faction_info_*` (PR #116,
merged to `main` as `ebd95c4` and vendored), C#
`NativeFactionInfoCatalog`, and `FactionKey` moved to the table-driven
path with the `faction.dlc_01` fallback (11 DLC-only rows). The PALOC
`0x30` name slice had no other consumer and was dropped. All 146 distinct
FactionKeys in slot0 / slot2 now resolve to `Faction_*` rows (60 were
blank, the rest character names). Pinned by
`LocalizationProviderTests.Bootstrap_LiveInstall_ResolvesFactionKeysThroughTheFactionTable`
and `NewestSave_FactionKeysResolveToFactionRows`.

The 2.06 alignment itself: 2.06
moved every static-info row's name from the table body into its header, so
**v2.05.02 shows item, mission, character, knowledge and gimmick names
blank on 2.06**. crimson-rs PR #115 (vendored at `2983e1e`) reads the new
header and adds `*_load_from_bytes_with_header` for the nine body-only
bridges; `LocalizationProvider` now extracts each table's header with its
body and calls them (iteminfo falls back to the body-only loader on a
pre-2.06 header). The body-only overloads of the six replaced bridges and
of quest / stage (whose `_with_pabgh` path still works) are gone.
`VerMinor` 5 → 6, `VerPatch` 1 (next release **v2.06.01**). The untouched
suite failed 16 of 408; now **408 C# tests green**. Unresolved distinct keys
per TypeName on slot0 / slot102 (2.06-written) equal 2.05.01's, ItemKey
752/752 resolved; AOT publish has zero IL/trim warnings and launches as
`v2.06.01.36`. Not checked: anything in-game. Details in
[game-versions.md](game-versions.md).

-----

## 2026-11-21 (v2.05.02) — Game 2.05.01: a new iteminfo byte without a minor bump

On **2026-11-21** the editor was aligned to game **2.05.01**
(paver `2/5/1/0x4ef4be86`, `dlc_01` revision 9, Steam build 26776744,
live since 2026-11-19), and **v2.05.02 is tagged, built and PUBLISHED**
(2026-11-21T03:19:28Z, marked Latest): tag → `7adecad` (PR #53 merge), CI
run `44859982100` on Rust 1.100.0, crimson-rs `9f4a2ba`, exe `2.5.2.36`,
zip 20,703,991 B, sha256 `f2f5d879…efeb0a8b` (equal to the asset's GitHub
digest and the run log). The downloaded zip was checked before
publishing: its `.sha256` matches, it holds the four expected files with
no `crimson_rs.dll`, the exe's ProductVersion ends in `7adecad`, and it
launches as `CrimsonAtomtic v2.05.02.36`.

The 2.05.01 alignment itself: like
2.04.08, 2.05.01 changed the iteminfo layout without moving the minor (a
`u8` on every row, a trailing `u8` on every notice-paper page), so
**v2.05.01 resolves no item names on a 2.05.01 install**. crimson-rs PR #113
(vendored at `9f4a2ba`) parses it and raises the patch floor 0 → 1, so a
2.05.00 install now reads incompatible. C ABI unchanged; the C# side is
`VerPatch` 1 → 2 (next release **v2.05.02**) and the paver pins (the
untouched suite failed exactly three of 408). **408 C# tests green**;
unresolved distinct keys per TypeName on slot102 (2.05.01) equal slot1's
(2.05.00), ItemKey 753/753 resolved; AOT publish has zero IL/trim warnings
and launches as `v2.05.02.36`. Not checked: anything in-game. Details in
[game-versions.md](game-versions.md).

-----

## 2026-11-13 (v2.05.01) — Game 2.05

On **2026-11-13** the editor was aligned to game **2.05**
(paver `2/5/0/0x6b551ea7`, `dlc_01` revision 8), and **v2.05.01 is tagged,
built and PUBLISHED** (2026-11-13T12:57:17Z, marked Latest): tag →
`7373f9b` (PR #49 merge), CI run `43428676035` on Rust 1.100.0, crimson-rs
`983dcc5` (docs-only past the tested `d4036b0`), exe `2.5.1.35`, zip
20,704,174 B, sha256 `30a5d567…fc1bb3b` (equal to the asset's GitHub
digest). **It was cut twice.** The first tag (on PR #48's merge `48dad90`)
failed in CI before any release existed: Rust 1.100, out on 2026-11-09 and
picked up by the runner's `stable`, parks threads with `WaitOnAddress` /
`WakeByAddress*`, so the AOT link needed `Synchronization.lib` (LNK2019);
the local 1.99 never did. PR #49 links it next to `Userenv.lib`, the tag was
deleted and re-cut. Not run before publishing: the binary checks on the
release zip itself; the local AOT publish of the same source launched clean.
(Next task at the time: none queued.)

The 2.05 alignment itself:
crimson-rs `d4036b0` (PR #105) absorbs 2.05's one format change — both dye
tables named their rows again, and had read 0 rows on 2.04/2.04.08 — and
moves the gate to minor 5 with the patch floor reset to 0, so the C# side
needed only `VerMinor` 4 → 5 (`VerPatch` 1) and the paver pins. The .NET
SDK on this machine had been swapped for 11.0.100; after the user
reinstalled SDK 10.0.404 the ILCompiler pin went 10.0.14 → 10.0.15.
**408 C# tests green**; three 2.05-written saves decode with 0 undecoded
bytes; the pickers list 8,379 items (7,068 base + 1,311 DLC-only); AOT
publish has zero IL/trim warnings and launches as `v2.05.01.33`. Not
checked: anything in-game. Details in [game-versions.md](game-versions.md).

-----

## 2026-11-07 (v2.04.02) — Game 2.04.08

On **2026-11-07** the editor was aligned to game **2.04.08**
(paver `2/4/8/0xe4d53a97`, `dlc_01` revision 7), and **v2.04.02 is tagged,
built and PUBLISHED** (2026-11-07T09:12:24Z, marked Latest): tag →
`3659591` (PR #46 merge), CI run `42358078301`, crimson-rs `7d02f8c`, exe
`2.4.2.32`, zip 20,695,563 B, sha256 `30dbc0f1…dc3cca` (equal to the
asset's GitHub digest). Not run before publishing: the binary checks on the
release zip itself (download, FileVersion, launch); the local AOT publish of
the same source launched clean.

What v2.04.02 carries:
2.04.08 added two strings to every iteminfo row without moving the minor,
so every name went blank on v2.04.01; crimson-rs PR #102 (merge `08dbc2d`)
parses it and adds `crimson_parser_min_gamedata_patch()` = 8, which
`GameDataVersion.IsCompatibleWithParser` now applies on the target minor
(a 2.04.03 install reads incompatible). `VerPatch` 1 → 2, so the next
release is v2.04.02. Also on this cut: **the item pickers offer DLC items**
(8,379 = 7,069 base + 1,310 DLC-only; a "DLC" column names the source;
the gem picker gains 19), DLC icons come from the DLC's 0012 group with a
fall-back to the base one, and a missing or broken DLC no longer blocks a
language (tested with junction-built installs). Re-verified on crimson-rs
`54fc208` (PR #103: a DLC-less 2.04 stamp reads its `build`, pinned by
`TryReadFromBytes_DlcLessStamp_ReadsBuildAndIsCompatible`): **408 C# tests
green**, plus the opt-in icon extraction test; AOT publish has zero IL/trim
warnings and launches as `v2.04.02.31`. The Browse Items window was checked
in the app: 8,379 items, "Diving" finds 28 with `dlc_01` and Chinese names.
Their icon cells stay blank until Tools → extract icons is re-run, because
the existing cache predates DLC support. Not checked in-game.

**Fixed upstream: the skill parser could try a ~15.7 GB allocation.** The
first full-suite run on `54fc208` aborted with `memory allocation of
15770583040 bytes failed`. The skill reader's tail probe reads list counts
at misaligned offsets, and `buff_data.rs` (`cnt96`, `cnt112`) and
`post_buff.rs` (`read_list_u32`) passed them straight to
`Vec::with_capacity`; 15,770,583,040 = 4 × `0xEB000000`. Whether it fired
depended on `TailSizeCache` hash order and free memory — the
"`--test-threads=4`" artifact crimson-rs documented — and the app, which
loads skill at startup, was exposed too. crimson-rs PR #104 (merge
`7d02f8c`) rejects a count that cannot fit in the remaining bytes. Vendored
and rebuilt: **408 C# tests green on three consecutive default-parallel
runs** (162–171 s), live skill round-trips byte-identical through Python
(2,159 entries), and AOT publish has zero IL/trim warnings and launches as
`v2.04.02.32`. crimson-rs's own notes still recommend `--test-threads=4`;
that advice is now stale.

-----

## 2026-11-01 (v2.04.01) — Game 2.04 and the first DLC

On **2026-11-01** the editor was aligned to game **2.04**
(paver `2/4/3/0x3ebe7ee2`), and **v2.04.01 is tagged, built and PUBLISHED**
(2026-11-01T05:25:13Z, marked Latest): tag → `f0207a1` (PR #44 merge), CI run
`41275985676`, crimson-rs `8e84704`, exe `2.4.1.29`, zip 20,690,606 B, sha256
`c9b7f79f…1af6` (equal to the asset's GitHub digest). Not run before
publishing: the binary checks on the release zip itself (download,
FileVersion, launch); the local AOT publish of the same source launched
clean. The user confirmed in-game
that the test save loads and the dragon can be summoned: slot103 holds slot102
plus the dragon unlock and a Silver Fang sigil, built through the real view
model (the previous slot103 is in
`data/_saves/slot103-before-2.04-test-20261101/`). The sigil was not used
in-game. Rust absorbed the new row
layout and archive index; the editor's real work was the first DLC. A
2.04-written save references rows only `dlc/dlc_01/` defines, and the suite
stayed green while they showed blank, so `LocalizationProvider` now falls
back to each DLC's PALOC, iteminfo, stringinfo and key tables. Pickers stayed
base-only in that release. **405 C# tests green, 0 skipped**; AOT publish had zero
IL/trim warnings and the exe launched as `CrimsonAtomtic v2.04.01.29`. Details
in [game-versions.md](game-versions.md).

-----

## 2026-09-18 (v2.03.01) — Game 2.03 and the dragon fix

As of **2026-09-18** the editor is aligned to game **2.03**
and the Mount-Unlock dragon works again; both shipped in **v2.03.01**,
tagged, built and **PUBLISHED** (2026-09-18T10:03:22Z, marked Latest). 2.03
(paver `2/3/0/0x03045138`) makes two format changes, both absorbed inside
crimson-rs (the 2.03 work is PR #97): iteminfo's `inventory_info_list` widened
`[u16; 9]` → `[u16; 10]` (every item +2 B), and every `.paloc` file is
now wrapped in a 0x200-byte header + one LZ4 block. Neither reached the
C ABI surface — both PALOC loaders unwrap internally, and
`CrimsonItemInfoSummary` still reads slot 0 — so the editor side was the
manual `VerMinor` 2 → 3 bump, the paver pins, and one mission title 2.03
changed *back* (`MissionKey 1000157` "Unfamiliar Land" → "Unfamiliar
Lands"). **401 C# tests green, 0 skipped**; the untouched suite failed
exactly those five. AOT publish emits zero IL/trim warnings, and the
local single-file exe (28,966,912 B, 4-file bundle, no `crimson_rs.dll`)
stamps `2.3.1.27` and **launches** as `CrimsonAtomtic v2.03.01.27` with
exactly one visible window — no version-mismatch dialog on the 2.03
install. The SDK did not move (10.0.401 / runtime 10.0.12), so the
ILCompiler pin stays at 10.0.12.

**v2.03.01 was cut twice.** The first cut (PR #40, merge `ec69029`; CI
run `35327302283`) was published at 09:12:19Z. The Unlock Mounts dialog
and README text fixes then landed (PR #41, merge `7150190`, build number
27 → 28), so that release and its tag were deleted (0 downloads) and
v2.03.01 was re-tagged at `7150190`: CI run `35332014534`, exe
`2.3.1.28`, zip 20,662,273 B, sha256 `06975c46…1384` (equal to the
asset's GitHub digest), crimson-rs `b1b687b`. **The re-cut cost the user
their notes edits**: they had edited the notes on GitHub when publishing,
the replacement body was rebuilt from the local notes file, and they had
to edit it again. Save the live body before undoing a published release
(now in [release-process.md](release-process.md), "Undoing a tag"). The
published notes also park the Dye editor: **no plan to fix it for now**.
Not run before publishing: the binary checks on the release zip itself
(download, FileVersion, launch) — the local AOT publish of `ec69029` was
clean (4 files, no warnings).


**A vendor refresh is not a build — it bit again, in a new shape.**
`vendor/crimson-rs` had been refreshed to `234b289` (13:29 today), but its
`target/release/crimson_rs.dll` / `.lib` and the Python `.pyd` were all
**2026-08-28** builds of 2.00-era source. Tests or the app run from that
state would have loaded a parser that cannot unwrap a 2.03 PALOC (no names
anywhere) and mis-decodes 2.03 iteminfo. `build_rust.ps1` +
`setup_python_env.ps1` fixed it. Also odd: this clone's reflog shows no
move between `fa1e8da` (2026-08-28) and today, so the 2.01 / 2.02 rebuilds
recorded below were made somewhere else (no worktree remains to check).
Compare artifact **mtimes** against the vendor HEAD before trusting a run.

**The C# live-save tests now follow the newest save.** They used to take
the first of slot0/1/2 — all 2.01-era on this machine, while the only
2.03-written saves are slot107 and slot102 — so no C# test exercised the
format the installed game writes. A new test helper, `LiveSaves`, lists
every live save newest-first by last-write time: all 43 live-save tests in
`NativeSaveLoaderTests`, the socket-editor write-path harness and the
type-byte probes now take the newest save;
`Load_EveryLiveSave_ReturnsConsistentSummary` checks every save on the
machine (12 here); and the new
`NewestSave_DecodesCompletelyAndRewritesIdentically` asserts 0 undecoded
bytes at every nesting depth plus a decode-identical write → reload.
**402 tests green, 0 skipped**, about +6 s on a ~180 s suite (A/B on the
same machine).

**Doing that exposed a real bug: Mount-Unlock's dragon failed on every
save written since game 2.00**, because the embedded 1.09-era element no
longer matched `MercenarySaveData`'s field order. **Fixed, and confirmed
in-game on 2.03**: the dragon summons and can be ridden. Chasing it turned
up a decoder bug under every save: the engine writes one `0x01` byte for
each absent dynamic array / object list, and the old walk read those
bytes as part of the next field — the dragon's "packed TStat"
`_currentHp` was one of them; it is a plain u64 (1032). crimson-rs PR #99
(merge `b1b687b`) decodes by that rule (0 fallback and 0 `trailing_pad`
on all 17 saves measured) and adds name-keyed element templates, which
rebuild the dragon under whichever schema the target save carries.
`MountUnlockMechanicsTests` runs the unlock on each schema among the live
saves. Vendored at `b1b687b` and rebuilt (`build_rust.ps1` +
`setup_python_env.ps1`).

-----

## 2026-09-11 (v2.02.01) — Game 2.02

The editor was aligned to game **2.02** on 2026-09-11 and **v2.02.01 was
tagged, built and PUBLISHED** (2026-09-11T14:42:44Z, marked Latest). 2.02
(paver `2/2/0/0xc8925c58`) is content-only for everything crimson-rs
parses — `iteminfo` and `skill` byte-identical to 2.01, 17 of 269 gamedata
files changed as pure content, no save-body drift — so the editor side was
the manual `VerMinor` 1 → 2 bump, the paver pin refresh, and wiring the
seven additive quest-key lookups the vendored crimson-rs (`main`
`1753d71`) now exports. **401 C# tests green, 0 skipped**; the untouched
suite failed exactly the four paver pins. AOT publish emits zero IL/trim
warnings, and the local single-file exe (28,965,888 B, 4-file bundle, no
`crimson_rs.dll`) stamps `2.2.1.27` and **launches** with the window title
`CrimsonAtomtic v2.02.01.27`.

**Release v2.02.01 (2026-09-11).** PR #38 (merge `f5a645a`) → annotated
tag `v2.02.01` (`--cleanup=verbatim`, both headings verified before the
push) → CI run `34608782045` (5 m 13 s; runner SDK 10.0.401, no NU1109;
crimson-rs `main` `1753d71`) → **DRAFT** "CrimsonAtomtic v2.02.01". Its
body was replaced with the bilingual player-facing notes only, matching
earlier releases. Verified without downloading anything: the zip asset
(`CrimsonAtomtic-v2.02.01-win-x64.zip`, 20,647,100 B) carries GitHub
digest `sha256:2bfa5407…`, identical to the hash CI printed when it
packaged it, and CI's bundle check reported 4 files with no
`crimson_rs.dll`. **Published 2026-09-11T14:42:44Z on the user's word,
marked Latest** (superseding v2.01.01). Not run before publishing: the
binary checks on the release zip itself (download, FileVersion, launch —
the launch check was done on a local AOT build of the same source) and a
load-a-real-save check. The whole sequence is now the 「啟動 release CI」
runbook in [release-process.md](release-process.md). With it shipped, the
next task was the backlog below (the Dye editor's mask-group RE first),
until 2.03 landed.

**Nothing built until the SDK pin moved.** The .NET SDK had updated to
10.0.401 (runtime 10.0.12) since the last session, so restore failed with
NU1109 before a single test could run — the ILCompiler central pin went
10.0.11 → 10.0.12 (see gotchas). CI's `setup-dotnet` asks for `10.0.x`,
so the release build needed the same pin — the runner got 10.0.401 and
restored cleanly.

**The quest-key lookups have no UI consumer yet.** crimson-rs reconciled
its curated main-/side-quest tables against the live 2.02 PALOC and keyed
every row by `MissionKey` / `QuestKey` (additive C ABI; the title lookups
are unchanged, and upstream's advice is "C# editor: prefer the key
lookups"). They are wired through `NativeMainQuestChapter` /
`NativeSideQuestFaction` and pinned by five new tests, but nothing in the
app calls either class, so under `TrimMode=full` the AOT link never sees
them — the tests, through the cdylib, are what exercise them.

**The Dye editor stays disabled**: `partprefabdyeslotinfo` is
byte-identical in 2.02 (body and header), so the 2.01 measurement stands.
The menu now reads "unsupported since 2.01" instead of "2.01 unsupported",
which read wrong on a 2.02 install.

-----

## 2026-09-04 (v2.01.01) — Game 2.01, a rename patch

The editor was aligned to game **2.01** on 2026-09-04 and **v2.01.01 was
tagged, built and published** the same day (2026-09-04T10:21:31Z; Latest
until v2.02.01 — this doc called it an unpublished draft until 2026-09-11). The annotated
tag points at `9e239c6` (PR #36 merge); CI cloned crimson-rs `main`
(`5dbeefb`, PR #93) fresh and built the single-file AOT exe. Verified from
the release's own assets: sha256 matches, the zip holds exactly the four
expected files with **no `crimson_rs.dll`** (the Rust core is linked into
the exe), and the exe stamps `2.1.1.26` / product `2.1.1.26+9e239c6`. The
release notes were trimmed to the bilingual highlights only, matching
v1.18.01 / v2.00.01 / v2.00.02 — CI's auto `## What's changed` + footer
are dropped at publish time by convention. The standing loose end is
unchanged: crimson-rs still has no pushed version tags (see the
crimson-rs bullet).

**The bug this release fixes had a green test suite.** Every individual
bridge had a live-install test, but nothing drove `LocalizationProvider` —
the production entry point — end to end, so "every extraction returns
NOT_FOUND because the archive layout moved" was invisible to CI. Closed by
`LocalizationProviderTests.Bootstrap_LiveInstall_ResolvesThroughWhicheverArchiveLayoutShips`,
which calls `TryBootstrapFromGameRoot` against the real install and asserts
iteminfo, the English PALOC, the language probe and a sibling table bridge
all resolved. Added after the tag — it changes no shipped code.

**2.01 is a rename patch — and it broke the app outright.** Every gamedata
file moved (`gamedata/binary__/client/bin/<t>.pabgb`/`.pabgh` →
`gamedata/binarystaticinfo__/bin/<t>.staticinfobody`/`.staticinfoheader`)
and each language's single `localizationstring_<lang>.paloc` blob became
one file per namespace under `stringtable/binary__/<lang>/`. **Not one byte
inside any file changed**, so no parser moved: Rust's only edit is
`PARSER_TARGET_GAMEDATA_MINOR` 0 → 1, and `CompatibleMinors = {1}` is the
target-only convention (2.00 data still parses byte-perfectly), not a real
incompatibility. But `LocalizationProvider` hardcoded the old directory and
all 40-odd filenames, so on a 2.01 install every bridge returned NOT_FOUND
and **no name resolved anywhere**. Both the app and the tests now go
through a new `GameDataLayout` (RustInterop): tables are named by *stem*
and the directory + extensions come from a newest-first probe of the
group-0008 manifest, so a kept pre-2.01 install still works. It mirrors
crimson-rs's `src/binary/gamedata_layout.rs`, which is `#[cfg(test)]` there
and therefore unreachable over the C ABI. Localization needed more than a
rename: `crimson_paloc_load_from_bytes` takes one blob, so a language is
held as its parts behind the new `MultiPalocCatalog`, and the
`*_lookup_display_name` bridges — each taking a single PALOC *native
handle* — are offered one part at a time (namespaces don't overlap, so at
most one answers). **395 C# tests green, 0 skipped** (45 were failing
against the live 2.01 install before this).

**The Dye editor is greyed out.** 2.01 widened `partprefabdyeslotinfo`'s
per-slot `mask` 3 → **12** bytes *and re-encoded it* — in 5,572 of 6,555
comparable slot pairs the old three bytes appear nowhere as a contiguous
window inside the new twelve, so no sub-slice is the pre-2.01 field. The
twelve read as four groups of three and **which group a slot uses is not
yet RE'd**. Measured through the release dll over all 1,626 prefabs / 6,585
slots, the legacy 3-byte getter the editor calls reads all-zero on **2,196
slots (33.3%)** whose full field is non-zero — a third of the dye UI would
render blank and would write edits from that wrong reading. Foundation over
workaround: the menu item is disabled (reason in its tooltip, and
`MainWindowViewModel.IsDyeEditorAvailable` carries the measurement) until
the mask groups are decoded upstream and the editor moves to the new
`crimson_part_prefab_dye_slot_info_lookup_slot_{,extra_layer_}mask_full`
sized-buffer bridges. Nothing else about dyeing regressed — the table still
loads all 1,626 rows on 2.01.

One soft pin moved, C# side only: `MissionKey 1000157`'s English title was
reworded **"Unfamiliar Lands" → "Unfamiliar Land"** — a game-side text
edit, not a parse drift (the 25-character `MissionKey 1000083` title
resolves unchanged through the identical path).

**Also fixed this session (unrelated to 2.01):**
`vendor/update_vendors.ps1` could rewrite *this* repository. A leftover
`vendor/crimson-rs/` folder with no `.git` of its own passed the
`Test-Path` existence check, so every `git -C vendor/crimson-rs …` walked
up to the parent project — rewriting origin to `crimson-rs.git`, refetching
its refs and `reset --hard`-ing `main` onto crimson-rs's `main`. The script
now requires the target to hold its own `.git` and asserts
`rev-parse --show-toplevel` really resolves to the vendor path before any
mutating command. Local game saves also moved out of `vendor/` (which is
for dependency clones only) to `data/_saves/`.

-----

## 2026-08-26 to 08-28 (v2.00.01, v2.00.02) — Game 2.00 and the socket-editor fix

**v2.00.02 is a socket-editor correctness release** — no game-version
change, so only `VerPatch` moved (1 → 2). Socket editing was producing
saves the *engine* rejects: every socket on an edited item read back
in-game as not-yet-opened. Three save-format invariants were being broken,
all measured against 22,019 socket-bearing blocks in four game-written
saves; an adversarial audit of the fix then caught a fourth defect the fix
itself introduced on the deferred Apply-Set path. Details in the session
changelog below, and the two rules the upstream reference doc had wrong are
corrected in crimson-rs (PR #92, merge `fa1e8da`).

**The version model changed, and that matters more than either drift.**
`meta/0.paver`'s `minor` **resets across a major bump** — 1.18 → 2.00 is
`18` → `0` — so the minor alone no longer identifies a schema. crimson-rs
added `PARSER_TARGET_GAMEDATA_MAJOR = 2` beside
`PARSER_TARGET_GAMEDATA_MINOR = 0` and a new
`crimson_parser_target_gamedata_major()` bridge (purely additive). That
closed a **real hole on the editor side**: `IsCompatibleWithParser` was a
minor-only lookup, and with `CompatibleMinors = {0}` it would have accepted
a hypothetical `1.00.xx` install into a mis-decode. It now gates on the
`(major, minor)` pair. The mismatch dialog had the same bug in its readout
(hard-coded `"1."` prefix → would have shown the 2.00 target as `1.00.xx`).

**2.00 ships two iteminfo drifts**: `SubItem` gained a payload-free
`type_id == 18` (the same renumbering 1.12 and 1.13 did), and a new
always-zero `u32` (`unk_pre_max_endurance_a`) sits ahead of the 1.12-era
`unk_pre_max_endurance`. Iteminfo 6,573 → **6,810** items (+237, none
removed); skill grew to **2,046** with zero drift; no save-body drift.
crimson-rs 2.00 is vendored from `main` (commit `0f2363b`, PR #91, merge
`8e942d7`). The C# side needed the manual `VerMajor` 1→2 / `VerMinor` 18→0
bump, the new major `LibraryImport` + compatibility gate, the dialog prefix
fix, and two pin refreshes — **382 C# tests green, 0 skipped**; AOT publish
emits **zero** IL/trim warnings and stamps `2.0.1.17` (rendered
`v2.00.01.17`).

-----

## To 2026-09-18 — the session changelog from status.md

status.md's one-line-per-milestone changelog, moved here unchanged on 2026-11-28. It overlaps the
entries above for 2026-08-26 to 2026-09-18, and its milestones are bullets, not `## ` headings.
Earlier detail: [status-archive.md](status-archive.md).

- **2026-09-18 — v2.03.01 released (2.03 + the dragon fix)**:
  PR #40 (2.03 alignment, newest-save tests, dragon fix; merge `ec69029`)
  and PR #41 (Unlock Mounts dialog + README text; merge `7150190`). First
  cut at `ec69029` published 09:12:19Z, deleted with 0 downloads, re-cut
  at `7150190` (CI run `35332014534`, exe `2.3.1.28`) and published
  10:03:22Z, marked Latest. The re-cut wiped the user's GitHub edits to
  the notes (they redid them); `release-process.md` now says to save the
  live body first. The notes park the Dye editor with no plan to fix it.

- **2026-09-18 — dragon unlock rebuilt by field name; save decoder follows the engine's absence markers (confirmed in-game)**:
  crimson-rs PR #99 (merge `b1b687b`) walks every object by the
  engine's rule — one `0x01` byte per absent dynamic array / object list —
  and keeps the heuristic walk only as a fallback no save needs (17 saves:
  12 live from 1.10 to 2.03 + 5 fixtures; 118k–135k objects and 170k–181k
  markers each; 0 `trailing_pad`; identical re-encode). New C ABI
  `crimson_save_export_element_template` /
  `crimson_save_list_insert_element_template` ("CRET" v1, name-keyed:
  removed fields dropped, new ones absent, empty child for new inline
  objects, `TEMPLATE_MISMATCH` −25 on a kind or size change). Editor:
  `MountCatalog.DragonElementTemplateHex` replaces the 212-byte raw element
  + fixups; `InsertDragonElementAsync` inserts it and sets `_mercenaryNo` /
  `_isMainMercenary` by name; `FillDragonHpAsync` writes a plain u64 2500
  (the "packed TStat" guard is gone, it guarded a misread). Tests:
  `MountUnlockMechanicsTests` runs once per schema of the template's
  classes (3 of the 12 saves here: crimson-rs runs save calls one at a time
  behind a global lock, so all 12 took 66 s against 24 s); the four block-0
  `_characterKey` pins accept `fixed_prefix` (the marker walk reads scalars
  in order); the empty-element size test counts markers. With the new
  native lib the untouched suite failed exactly those four pins. **402 tests
  green, 0 skipped**, about 200 s (from ~186 s).
  In-game check on 2.03: slot103 (slot102 plus the unlock, made by the real
  `UnlockMountAsync` run headless) — the dragon summons and can be ridden.

- **2026-09-18 — live-save tests follow the newest save; the dragon unlock is broken on 2.00+ saves (found, not fixed)**:
  a new `LiveSaves` test helper (newest-first by last-write time) replaces
  four copies of save discovery. `NativeSaveLoaderTests`, the socket-editor
  harness and the type-byte probes now run on the newest save (slot102,
  written by 2.03) instead of a 2.01-era slot0;
  `Load_EveryLiveSave_ReturnsConsistentSummary` covers all 12 local saves
  (collecting problems across them, 4-way parallel);
  `NewestSave_DecodesCompletelyAndRewritesIdentically` adds the deep checks
  the summary can't see — undecoded bytes inside nested objects, and a
  field-by-field decode comparison after an unmodified write → reload (both
  decodes run side by side; one pass over a late-game save is ~9 s, mostly
  `QuestSaveData` and `FieldSaveData`). 402 tests green, 0 skipped, about
  +6 s on the suite. Pointing `MountUnlockMechanicsTests` at the newest save
  failed with `MUTATION_INVALID`, and a sweep of all 12 saves showed the
  dragon insert failing on every save since 2.00: the embedded element
  predates a `MercenarySaveData` schema change (see the 🔴 backlog item).
  That test stays on slot105 with the reason in a comment.

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
  mismatch dialog. Found in passing: Arabic is never offered as a language
  (backlog), and no C# test loaded a save the current game wrote (closed the
  same day — see the entry above).

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
