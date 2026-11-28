# Code audit plan — 2026-11-28

> **Status: PLAN ONLY.** This file splits the codebase into independent audit
> blocks. No code was changed to produce it. Findings from the audit itself go
> into per-block reports (see [Deliverables](#deliverables)), and fixes go into
> separate PRs.
>
> Snapshot: `dev` at `9ba7ae4` (PR #64), game 2.06, crimson-rs vendored at
> `0a551a6`. Line counts are physical lines (`.cs` / `.axaml` / `.py` /
> `.ps1` / `.yml`) at that commit and are only a sizing aid.

## How the split was made

The repo has five C# projects, a Python toolchain and build/release scripts
(~57,400 lines of C#/AXAML in `src/`, ~4,700 lines of Python / PowerShell /
YAML). The project boundaries alone don't give balanced, independent blocks:
`CrimsonAtomtic.Ui` holds 124 of the 191 `src/` files, and
`MainWindowViewModel.cs` alone (5,926 lines) mixes the generic editor with a
dozen feature flows. So the split follows **functional responsibility**, and
`MainWindowViewModel.cs` is cut by method range (see the table under each
block).

Six blocks:

| # | Block | Main location | ~Lines | Tests (by file) |
|---|---|---|---|---|
| 1 | Native interop boundary | `src/CrimsonAtomtic.RustInterop/` | 12,700 | 8 files |
| 2 | Save document lifecycle & data safety | `Core/`, `SaveModel/`, `Ui/Platform/`, MainWindow, backup | 9,000 | 5 files |
| 3 | Game-data resolution, localization & assets | `LocalizationProvider`, icons, portraits, pickers, `Resources/Strings/` | 8,400 | 6 files |
| 4 | Item editors | inventory / Add Item / stack fill / sockets / dye / buyback | 8,500 | 4 files |
| 5 | World & progression editors | abyss gates, sealed artifacts, faction, knowledge, mounts, mercenaries, world map | 7,500 | 8 files |
| 6 | Build, release & Python tooling | `build.ps1`, `scripts/`, `.github/`, `vendor/*.ps1`, `tools/`, props | 4,700 + docs | 1 file |

**Out of scope:** `vendor/crimson-rs/` is a read-only mirror (CLAUDE.md rule
8). The Rust side of the C ABI (`D:\Github\crimson-rs\src\c_abi\`, 45 modules)
is audited in that repo, not here. Block 1 audits only the C# consumer side of
the contract.

Dependency direction (an audit of block N only needs the blocks to its left as
read-only context):

```
1 Interop  ──►  2 Save lifecycle  ──►  3 Game data  ──►  4 Item editors
                                                    └──►  5 World editors
6 Build / tooling  (independent; touches all as build inputs)
```

---

## Block 1 — Native interop boundary

**Responsibility:** every call from C# into `crimson_rs.dll` (C ABI):
save load / mutate / write, the 30+ `*Info` catalogs, PALOC, PAZ extraction,
paver version read. Nothing above this layer should know about pointers,
handles or status codes.

**Files**

| File | Lines | Notes |
|---|---|---|
| `NativeSaveLoader.cs` | 3,682 | ~300 `[LibraryImport]` declarations; immediate + deferred mutation |
| `NativeKeyInfoCatalogs.cs` | 3,247 | key → name catalogs (skill, character, knowledge, gimmick, store, …) |
| `NativeDyeCatalogs.cs` | 1,139 | three dye catalogs + part-prefab dye slot lookups |
| `ISaveLoader.cs` | 739 | the contract the UI uses, incl. `RunDeferred` |
| `NativeItemInfoCatalog.cs`, `IItemInfoCatalog.cs`, `ItemInfoSummary.cs` | 811 | iteminfo |
| `NativePalocCatalog.cs`, `MultiPalocCatalog.cs`, `IPalocCatalog.cs` | 420 | localization tables |
| `NativePaverReader.cs` | 310 | game version gate `(major, minor)` + patch floor |
| `NativePazExtractor.cs`, `IPazExtractor.cs`, `PazFileEntry.cs` | 317 | asset archives |
| `NativeMainQuestChapter.cs`, `NativeSideQuestFaction.cs`, `QuestRollupKey.cs` | 698 | quest roll-ups |
| `NativeStringInfoCatalog.cs`, `IStringInfoCatalog.cs` | 257 | |
| `GameDataLayout.cs` | 239 | install / DLC directory layout |
| Records: `ItemRecord`, `InventoryItemRecord`, `CharacterRefRecord`, `PositionedEntityRecord`, `PathStep` | 579 | |
| Batching: `BatchOps.cs`, `ScalarBatchOp.cs`, `SaveLoaderScalarExtensions.cs` | 240 | |

**Tests:** `NativeSaveLoaderTests` (2,274 lines, 54 tests), `KeyInfoCatalogsTests`,
`ItemInfoCatalogTests`, `PalocCatalogTests`, `PazExtractorTests`,
`NativePaverReaderTests`, `StringInfoCatalogTests`, `QuestRollupBridgeTests`;
fixtures `LiveInstall.cs`, `LiveSaves.cs`.

**Audit focus**

- Handle lifetime: every native handle is a `SafeHandle`; no use-after-free
  on dispose / reload; no handle crosses threads unguarded.
- Two-call string / buffer pattern: size query, allocation, fill; behaviour
  when the size changes between the two calls; length overflow.
- Status-code mapping: every negative code becomes a typed exception
  (`CrimsonSaveException`), none is silently ignored.
- Deferred batch contract (`RunDeferred`): commit on normal return, abort on
  throw, and which primitives are invalid inside a batch (status.md gotcha
  "promote then write in place"). Nested batches.
- AOT safety: source-generated marshalling only; `AllowUnsafeBlocks` scope;
  string encoding (UTF-8 vs UTF-16) consistent with the Rust side.
- Version gate: `NativePaverReader` sources `(major, minor)` and the patch
  floor from the ABI, never from literals.
- Size of `NativeSaveLoader.cs` / `NativeKeyInfoCatalogs.cs`: whether they
  split cleanly by sub-surface (structure only; no refactor during audit).

---

## Block 2 — Save document lifecycle & data safety

**Responsibility:** finding, opening, editing generically, saving and
restoring a save file without losing the user's data; process bootstrap and
the platform layer.

**Files**

- `src/CrimsonAtomtic.Core/` — `IPlatformPaths`, `ISingleInstanceGuard` (90)
- `src/CrimsonAtomtic.SaveModel/` — `ScalarFieldEditing`, `SaveSummary` (538)
- `src/CrimsonAtomtic.Ui/Platform/` — `WindowsPlatformPaths`,
  `SteamLibraryProbe`, `EpicManifestProbe` (526)
- `Program.cs` (mutex guard), `App.axaml(.cs)` (bootstrap / DI wiring)
- `Services/`: `SaveBackupService` (505), `ChangeJournal`, `AppSettings`,
  `SaveFieldValues`, `TaskExtensions`, `WindowStateStore`,
  `WindowRestoreState`, `ManagedWindowRestore`, `WindowPlacement`
- `ViewModels/`: `FieldRowViewModel`, `ElementRowViewModel`,
  `RestoreFromBackupViewModel`
- `Views/`: `MainWindow.axaml` (720) + `MainWindow.axaml.cs` (1,997),
  `ChangeSummaryDialog`, `ConfirmDialog`, `DisclaimerDialog`,
  `GameVersionMismatchDialog`, `RestoreFromBackupWindow`
- `MainWindowViewModel.cs`, generic-editor ranges (approx. start lines):

| Lines | Methods |
|---|---|
| 18–520 | ctor, `MarkDirtyFromExternalEdit`, `NavigateTo*` |
| 710–920 | `ResolveDefaultOpenSaveRoot`, backup root, platform persistence |
| 1033–1290 | summary panel, font size, app version |
| 1378–1836 | `LoadSave`, timestamp preservation, block load, drill-down nav, `CommitFieldEditAsync`, `MakeFieldAbsent` |
| 3234–3324, 3489–3603 | `WalkPath`, `RefreshNavStack`, `RemoveElementAsync` |
| 5354–5926 | `SaveAsync`, `SaveAsAsync`, structural-edit confirm, backup-before-write, `RestoreFromBackupAsync`, nav frames, filters |

**Tests:** `ScalarFieldEditingTests`, `SaveBackupServiceTests`,
`InstallProbeTests`, `WindowStateTests`, `WindowRestoreStateTests`.

**Audit focus**

- Write path atomicity: temp file + replace vs in-place write; behaviour
  on disk-full / locked file / game running; backup taken *before* every
  write path (Save, Save As, every tool window's write).
- Original mtime preservation (Steam Cloud sort) and Save As re-anchoring.
- Dirty tracking: every mutation path marks dirty; close-on-dirty prompt;
  `ChangeJournal` completeness.
- Absent vs zero semantics in the generic field editor (status.md gotcha
  `_validSocketCount`).
- Backup retention / restore correctness per `SavePlatform`; userId
  collisions across launchers.
- CLAUDE.md rule 2 (mutex), rule 3 (async I/O on the UI thread?), rule 4
  (OS calls behind `Core` interfaces), rule 6 (logs).
- `MainWindow.axaml.cs` code-behind size: logic that belongs in a ViewModel.

---

## Block 3 — Game-data resolution, localization & assets

**Responsibility:** turning keys into names, icons and portraits from the
installed game (base + DLC fallback), UI language switching, and the shared
search / picker windows.

**Files**

- `Services/LocalizationProvider.cs` (2,569) — 32 key-resolver bridges,
  PALOC chains, dye slot lookups, DLC fallback
- `Services/UiLanguageService.cs`, `UiText.cs`
- `Resources/Strings/en.axaml`, `ja.axaml`, `zh-TW.axaml` (~1,000 each)
- `Services/IconExtractionService.cs`, `IconImageEncoder.cs` (DDS decode),
  `IconProvider.cs`, `ItemKeyToIconConverter.cs`, `PortraitProvider.cs`
- `ViewModels` + `Views`: `LocalizationSearch`, `CharacterPicker`,
  `CharacterRefsBrowser`, `ItemPicker`, `IconExtractionProgressDialog`
- `MainWindowViewModel.cs` 522–710 (install root, icon cache, secondary /
  UI language) and 1943–2006 (`LookupUiResourceString`)

**Tests:** `LocalizationProviderTests`, `LocalizationTypeByteDiscoveryTests`,
`UiLanguageServiceTests`, `IconExtractionServiceTests`,
`IconImageEncoderTests`, `ItemPickerRowTests`.

**Audit focus**

- Resolver coverage: unresolved distinct keys per TypeName over the newest
  save (status.md gotcha "a green suite says nothing about keys it never
  asks for").
- Base vs DLC fallback order; behaviour with no game install.
- Thread safety of lazy caches (the dye part-prefab table builds off the UI
  thread on first use); memory held by per-language dictionaries.
- Cache directories under `%LOCALAPPDATA%\CrimsonAtomtic\`: invalidation on a
  game patch, corrupt / partial files, concurrent extraction.
- DDS decoder bounds checks on untrusted game files.
- The three string dictionaries stay key-for-key identical. Their line
  counts differ (997 / 1,000 / 999), which says nothing about the keys; the
  check is the key sets. (Checked 2026-11-28: 852 keys each, identical, no
  duplicates.)

---

## Block 4 — Item editors

**Responsibility:** every flow that adds, removes or changes items in the
save's inventories.

**Files**

- Sockets: `SocketEditorViewModel` (1,737), `SocketEditorWindow`,
  `Services/GemSets.cs`, `CustomGemSetsEditor{ViewModel,Window}`
- Dye: `DyeEditor`, `DyeSlotEditor` (867), `DyePalettePicker`, `DyeSlotPicker`
  (ViewModel + Window each), `Services/KnownDyeService.cs`, `KnownDyes{ViewModel,Window}`
- `VendorBuyback{ViewModel,Window}`, `FindItems{ViewModel,Window}`
- `MainWindowViewModel.cs`: 922–1033 (`RecomputeAddItemTarget`), 1290–1378
  (fill selected stack), 3324–3489 (`BulkFillItemListMaxStackAsync`),
  3603–4289 (`AddItemToCurrentListAsync`, `LearnItemKnowledgeAsync`),
  4386–4929 (item-index helpers, stack-fill candidates,
  `FillAllStacksAcrossInventoriesAsync`)

**Tests:** `SocketEditorTests` (927 lines), `DyeSlotNumberTests`,
`KnownDyeServiceTests`, `StackFillCapTests`.

**Audit focus**

- Item identity: every item key uses the 5-tuple, never
  `(Block, Bag, Item)` alone (status.md gotcha, `SocketRow.ItemIdentity`).
- Deferred batches: `RunDeferred` callers here are `SocketEditorViewModel`
  and `DyeSlotEditorViewModel` (two sites). Socket Apply-Set has a test;
  check whether the dye-slot batches do.
- Value domains: stack caps, `_currentEndurance` vs `iteminfo.max_endurance`,
  dye slot numbers (sparse, 0..11), `_grimeOpacity`.
- Add Item: knowledge learned when missing; DLC items added to a save
  without the DLC (untested in-game, status.md backlog).
- No dedicated test file found for `VendorBuyback`, `FindItems` or
  `AddItemToCurrentListAsync`.

---

## Block 5 — World & progression editors

**Responsibility:** flows that change world / quest / faction state rather
than items.

**Files**

- `AbyssGates{ViewModel,Window}`, `Services/GameRegions.cs`
- `SealedArtifactChallenge{ViewModel,Window}` + `MainWindowViewModel.cs`
  1836–1943 (`MergeTags`) and 2006–3234 (mark-complete, Pattern B v1 writes,
  bulk scan, challenge context, `ScanFactionNodes`)
- Faction: `Services/FactionRelationService.cs`, `ContributionService.cs`,
  `FactionNodeEditor`, `FactionRelationEditor`, `ContributionEditor`
  (ViewModel + Window each), `MainWindowViewModel.Contributions.cs`,
  `MainWindowViewModel.FactionRelations.cs`
- `KnowledgeEditor{ViewModel,Window}` + `MainWindowViewModel.cs` 5059–5354
  (`ResolveKnowledgeList`, `GetLearnedKnowledgeKeys`)
- Mounts: `Services/MountCatalog.cs`, `MountUnlock{ViewModel,Window}`,
  `MainWindowViewModel.cs` 4289–4386 (`InsertDragonElementAsync`)
- `RenameMercenary{ViewModel,Window}`
- `MainWindowViewModel.cs` 4929–5059 (`UnlockAllAbyssGatesAsync`)
- World map: `WorldMap{ViewModel,Window}`, `Services/WorldMapAffine.cs`,
  `WorldMapBasemapService.cs`

**Tests:** `AbyssGateRegionTests`, `FactionNodeEditorTests`,
`FactionRelationServiceTests`, `ContributionServiceTests`,
`KnowledgeEditorTests`, `MountCatalogTests`, `MountUnlockMechanicsTests`,
`WorldMapAffineTests`.

**Audit focus**

- Sealed Abyss: only the pre-claim shape (Pattern B v1) is written; Pattern A
  is gone from every path (status.md gotcha).
- `RunDeferred` callers here: sealed-artifact bulk (≈2397) and knowledge
  (≈5211). Neither has an obvious dedicated test; check.
- Captured-element templates (dragon / mercenary) are mapped by field name
  across schemas, not by index (status.md gotcha "no save-body drift").
- Faction relation-group edits and the Abyss gate columns are not yet checked
  in-game (status.md).
- No dedicated test file found for `SealedArtifactChallengeViewModel`,
  `RenameMercenaryViewModel` or `WorldMapViewModel`.

---

## Block 6 — Build, release & Python tooling

**Responsibility:** turning source into a signed-off release, and the
offline analysis toolchain.

**Files**

- `build.ps1`, `build.cmd`, `build_number.txt`, `global.json`,
  `Directory.Build.props`, `Directory.Packages.props`, `CrimsonAtomtic.slnx`
- `scripts/`: `build_rust.ps1`, `build_ui.ps1`, `package_aot.ps1`,
  `setup_python_env.ps1`
- `.github/workflows/release.yml` (227)
- `vendor/update_vendors.ps1`, `vendor/README.md`
- `tools/` (Python): `common/`, `extract/`, `inspect/` (incl.
  `sa-investigations/`), `analyze/`, `diff/`, `pyproject.toml`
- `.gitignore` vs CLAUDE.md rule 10 (no derived data committed)
- Docs accuracy: `docs/architecture.md`, `README.md`, `scripts/README.md`
- `src/CrimsonAtomtic.Tests/ClaudeMdSizeTests.cs`, `CrimsonAtomtic.Tests.csproj`

**Tests:** `ClaudeMdSizeTests` only. No pytest files were found under
`tools/`.

**Audit focus**

- Release reproducibility: CI clones crimson-rs `main` at tag time; the
  stable-Rust drift (status.md gotcha); ILCompiler pin vs SDK.
- `package_aot.ps1` linker detection; `<NoWarn>IL2104;IL3053</NoWarn>`
  still needed.
- Two cargo target dirs (`target` vs `target-py`) so PyO3 and C ABI builds
  don't clobber each other; `.pyd` freshness after a vendor refresh.
- `update_vendors.ps1` does `reset --hard`: guard against local edits.
- Python rule 9: every `.py` prints usage and exits 2 with no args, no side
  effects at import.
- Supply chain: pinned action versions in `release.yml`, package pins in
  `Directory.Packages.props`.

---

## Cross-cutting checklist (apply in every block)

From root [CLAUDE.md](../CLAUDE.md):

- [ ] Rule 3 — all I/O / IPC / dialogs async; no `.Result` / `.Wait()` on
  the UI thread.
- [ ] Rule 4 — OS-specific calls only behind a `Core` interface.
- [ ] Rule 5 — no reflection-based APIs (AOT).
- [ ] Rule 7 — magic strings in one well-commented file per project.
- [ ] Rule 12 — no consumer-side workaround for a parser / schema problem.
- [ ] Exceptions: no empty `catch` that hides a failed write.
- [ ] UI strings in all three `Resources/Strings/*.axaml`, not inline.

## Pre-scan observations (to verify, not findings yet)

Noticed while scanning; each needs confirmation in its block's audit.

1. **Rule 6 (logs) looks unimplemented** — Block 2. `IPlatformPaths.LogDirectory`
   is defined, but no consumer of it and no file logger (`ILogger`, Serilog,
   custom writer) was found; `ChangeJournal` is in-memory.
2. **Architecture doc drift** — Block 6. `architecture.md` says `Core` holds
   settings persistence and backup retention (they are in
   `Ui/Services/AppSettings.cs` / `SaveBackupService.cs`), quotes
   "190/190" tests (status.md: 452), and says ViewModels know nothing about
   FFI, while 13 ViewModels `using CrimsonAtomtic.RustInterop`. The
   `SaveModel` project is 2 files; the "edit model + undo/redo" it describes
   is not there.
3. **Rule 4** — Block 3. `UiLanguageService` P/Invokes
   `kernel32!GetUserDefaultUILanguage` directly from `Ui/Services`, not
   behind a `Core` interface.
4. **Rule 7** — all blocks. No centralised magic-string class was found by
   name in any project (e.g. the mutex name in `Program.cs`, `"Logs"` in
   `WindowsPlatformPaths`, save class names in ViewModels).
5. **Size hot spots** — `MainWindowViewModel.cs` 5,926 lines (spans blocks
   2/3/4/5), `NativeSaveLoader.cs` 3,682, `NativeKeyInfoCatalogs.cs` 3,247,
   `LocalizationProvider.cs` 2,569, `MainWindow.axaml.cs` 1,997,
   `SocketEditorViewModel.cs` 1,737.
6. **CI covers release only** — Block 6. `.github/workflows/` has just
   `release.yml` (tag push / manual); no workflow runs the tests on a PR.

## Execution plan

1. **Block 1 first.** Blocks 2–5 rely on the `ISaveLoader` contract;
   knowing its guarantees (esp. `RunDeferred`) first makes their audits
   shorter.
2. **Blocks 2 and 6 in parallel** — independent of each other.
3. **Blocks 3, 4, 5 in parallel** after block 2 (they share
   `MainWindowViewModel` only by disjoint method ranges above).
4. Each block is read-only: no code edits during the audit. Baseline before
   starting: `dotnet run --project src/CrimsonAtomtic.Tests` green on the
   snapshot commit, and record the count.

## Deliverables

- One report per block: `docs/code-audit-2026-11-28-block<N>-<slug>.md`, each
  finding with file:line, severity (bug / risk / rule violation / cleanup),
  evidence, and a suggested fix — but not the fix.
- A short summary appended here once all six are done: counts per severity
  and the proposed fix-PR order.
