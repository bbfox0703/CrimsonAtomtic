# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-05-17 part 6 (positioned-entity enumerator binding — crimson-rs cd02b28 lights up world-map plotting; UX deferred).
>
> ## ✅ This session — what shipped (2026-05-17 part 6)
>
> One commit consuming the new `crimson_save_list_field_positions` C
> ABI from vendor `cd02b28`. Vendor refreshed `fe566a4` → `cd02b28`
> (1 upstream commit). Foundational binding only — no UX surface
> shipped this iteration.
>
> | Area | Scope |
> |---|---|
> | **Positioned-entity enumerator binding** | New `crimson_save_list_field_positions` upstream ABI yields a single-FFI walk of every save-side positioned entity (active playable char + present-`_spawnPosition` mercenaries + present-`_transform` field gimmicks). Slot103 baseline: **3,317 records** = 1 ActiveChar + 76 Mercenary + 3,240 Gimmick. New 56-byte `repr(C)` `PositionedEntityRecord` struct (layout-pin test) + `PositionKind` enum (ActiveChar / Mercenary / Gimmick) + `PositionEntityFlags` constants (`IsMainMercenary` / `IsPlayerOwned` / `FromOriginTransform`). `ISaveLoader.ListFieldPositions(out version)` exposes the two-call buffer-dance wrapper. Records carry `PosX/Y/Z` in the global coordinate frame (no conversion needed) + `Yaw` + `FieldInfoKey` for region filtering + owner identity (`CharacterKey` / `GimmickInfoKey` / `GimmickSaveDataKey` / `MercenaryNo`). Live-save integration test asserts kind enum coverage, finite floats, ACTIVE_CHAR-implies-IS_PLAYER_OWNED + GIMMICK-clears-IS_PLAYER_OWNED invariants, non-zero positions exist, and the documented basemap affine (`0.432044·X + 5937.50` / `-0.433071·Z + 1864.08`) produces finite pixels on a sample record. |
>
> Tests: **269 → 271** (+2: layout pin + live cross-kind smoke). Debug build clean.
>
> ### Open follow-ons noted during this session
>
> - **World-map UX layer** (deferred): build a Tools → World Map dialog
>   that takes the binding to its consumer. Two intermediate cuts:
>   (a) read-only DataGrid showing `(Kind, OwnerName, PosX, PosZ,
>   FieldInfoKey)` rows with filter UI — useful for inspection, no
>   basemap image needed; (b) full visual basemap with plotted markers
>   + region filter + tooltips — needs a basemap image asset (game-
>   extracted DDS, not in repo; vendor scripts can extract it but
>   asset-license / repo-shipping is a separate question). Pinned
>   basemap affine constants live in `PositionedEntityRecord` docstring
>   for either path.
>
> ### Open follow-ons resolved this session
>
> - ~~Visual verification of palette picker~~ (from part 4) — **confirmed working** end-to-end by the user (Tools → Edit Item Dyes → Edit → Pick renders the 109-cell grid, highlights the current color, cell-click updates the row swatch + dismisses the modal).
>
> ### Open follow-ons carried over (no change)
>
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 5)
>
> One revert commit. The "+ Add Dye to undyed item" UX shipped in part 2
> was rolled back the same day after user testing surfaced a soundness
> problem: `crimson_save_set_object_list_present(makePresent=true)`
> materializes a single dye element with a default `_dyeSlotNo` (0),
> but each item's valid `_dyeSlotNo` set varies per `PartPrefabKey` —
> some items accept `0..3`, others only `0..1`, etc. Writing a dye to
> an unsupported slot is unsafe; doing this right needs a per-item
> slot picker driven by `partprefabdyeslotinfo`'s slot-count lookup
> (already loaded; UX work deferred).
>
> | Area | Scope |
> |---|---|
> | **"+ Add Dye" UX rollback** | DyeEditor scan reverts to surfacing only items with `HasDyeData` flag set (no longer includes un-dyed equipped items). `DyeEditorItemRow.CanAddDye` / `CanEditDye` properties + ctor param removed. `AddDyeRequested` event + `RequestAddDye` method removed from the master VM. `DyeEditorWindow.axaml` Action column reverts to a single Edit button (no more Panel with conditional Edit/Add). `MainWindow.OnEditItemDyesClick` drops the AddDyeRequested handler (~40 lines + confirm dialog + NOT_FOUND alert). en/ja/zh-TW resource strings reverted (DyeEditorHeader text + 7 Add-related strings dropped). |
> | **What's kept** | (a) The `SetObjectListPresent` C ABI binding on `NativeSaveLoader` + `ISaveLoader` + `NOT_OBJECT_LIST = -23` constant — useful primitive, no UX surface, contract-pinned by the roundtrip test. (b) The `ListAllItems` walker refactor of Dye + Sockets editors — independent fix that resolves the missing-mercenary-gear bug. (c) The palette picker UX from part 4 — separate feature, works fine. |
>
> Tests: **269/269 pass** (no test changes — the rolled-back code had no tests of its own). Debug build clean.
>
> ### Open follow-ons noted during this session
>
> - **Safe "+ Add Dye"** (deferred): re-attempt with proper slot-picker UX. Steps: (1) lookup item's `PartPrefabKey` via the existing `ItemKey → PartPrefabKey` bridge; (2) query `partprefabdyeslotinfo.LookupSlotCount(partPrefabKey)` for the valid slot count; (3) UI step asking the user to pick a slot 0..N-1; (4) after `SetObjectListPresent(makePresent=true)` materializes element 0, patch its `_dyeSlotNo` via `SetScalarField` to the picked value. The vendor ABI for steps 1-2 is already loaded — just needs UX + integration.
>
> ### Open follow-ons carried over (no change)
>
> - Visual verification of palette picker (from part 4).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 4)
>
> One commit consuming the part-3 palette ABIs end-to-end into the
> DyeSlot editor UX. No vendor change in this part.
>
> | Area | Scope |
> |---|---|
> | **DyeSlot palette picker UI** | The per-row R/G/B/A NumericUpDown columns in `DyeSlotEditorWindow` are replaced with a single visual "Color" column showing a colored swatch + "Pick…" button. Clicking Pick opens a new modal `DyePalettePickerWindow` that renders the row's color group as a 10-column UniformGrid of 109 clickable color cells (9 grayscale + 10×10 chromatic per vendor's documented layout). Cell-click → confirms + closes (no separate OK button) → row's R/G/B properties update + swatch repaints; user still hits the row's Apply to persist. The picker uses the part-3 `PaletteSize` / `PaletteAt` ABIs to enumerate cells and `PositionForRgb` to highlight the currently-applied cell with a black/white ring. Off-grid CE-modified RGBs (no matching palette position) trigger an orange-tinted banner: "The currently-applied color is not on this theme's palette (probably set by an external tool). Picking any cell will replace it with an on-grid color." Bilingual strings (en / ja / zh-TW) for the new picker window + the "Color" column header + "Pick…" button + off-grid banner. The old NumericUpDown columns are gone — power users who want raw byte access can still use the underlying scalar editor in the block-tree view. Alpha is intentionally hidden in the picker (every palette position uses 0xFF per vendor docs); `ApplyPickedColor` defensively normalizes A to 0xFF if it was 0. |
>
> Tests: **269/269 pass** (no new tests — pure UI plumbing over the part-3 ABIs which already have a roundtrip test). Debug build clean. **UI not yet visually verified end-to-end** — the AXAML compiles, the VM types check, but I haven't launched the app to see the grid render. Next session pick-up should open Tools → Edit Item Dyes → click Edit on any dyed row → click Pick to confirm: (a) the grid renders 109 cells, (b) the current color shows with a ring, (c) clicking a cell closes the modal and updates the swatch, (d) Apply persists.
>
> ### Open follow-ons noted during this session
>
> - **Visual verification of palette picker** — see caveat above. If the UniformGrid or Border ring doesn't render as expected, the Cell template in `DyePalettePickerWindow.axaml` is the place to iterate.
> - **Disable Pick button when no color group selected** — current behavior silently no-ops if `SelectedColorGroup` is null (e.g. brand-new dye element). Should disable the button via `CanExecute` on `RequestPickColorCommand` for cleaner UX.
> - **Palette position tooltip on cells** — currently shows "Position N · #rrggbb" which is RE-engineer-flavored. Could swap to per-tier names if/when a `(tier, column) → human-readable` mapping is RE'd.
>
> ### Open follow-ons carried over (no change)
>
> - **Pattern B v2 for multi-objective SA challenges** (from part 1).
> - **OCT forum post URL placeholder** (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 3)
>
> One commit consuming a single upstream finding that re-frames the
> entire dye-editing UX. Vendor `crimson-rs` refreshed `4ee430b` →
> `d8c15cd` (1 upstream commit).
>
> | Area | Scope |
> |---|---|
> | **Dye color group palette accessors** | Upstream investigation (vendor `d8c15cd`) pinned a critical model correction: the save's `_dyeColorR/G/B/A` scalars are **NOT freeform** — they index into a 109-position palette per Color_Group theme (9 grayscale + 10×10 chromatic). All 11 RGBs observed in slot103 (6 Hernand + 5 Pororin) hit exact gradient positions; **zero off-grid values**. The PyQt5 reference editor's freeform R/G/B sliders are technically valid bytes but reach colors the engine can't display. Critical byte-order finding: the on-disk palette is BGRA but the save uses logical RGBA — the parser now swaps automatically so palette values compare directly. 3 new C ABI exports bound on `NativeDyeColorGroupInfoCatalog`: `PaletteSize(themeKey)` → 109 in 1.07, `PaletteAt(themeKey, idx)` → logical `(R, G, B, A)` ready to write into the save, `PositionForRgb(themeKey, r, g, b)` → reverse lookup (NOT_FOUND for off-grid). New live-install roundtrip test pins forward + reverse symmetry on the first theme. **DyeSlot editor UX rewrite deferred** — vendor docs spec a visual 11-row palette picker grid replacing the freeform sliders; tracked as a follow-on so the ABI binding ships independently of the bigger UX change. |
>
> Tests: **268 → 269** (+1 palette roundtrip). Debug build clean.
>
> ### Open follow-ons noted during this session
>
> - **DyeSlot editor palette picker UX**: replace `DyeSlotEditorWindow`'s
>   freeform R/G/B/A inputs with a visual palette grid (11 rows × ~10 cols
>   per theme). Click a cell → write the cell's RGB back via existing
>   `SetScalarField`. Reverse lookup highlights the currently-applied
>   dye's cell. The "dye consumable item" is bookkeeping only — visual
>   = palette position, so the v2 editor ships **without** needing a
>   `(consumable_ItemKey, theme) → position` mapping. ASCII mockup +
>   recommended layout in
>   [`vendor/crimson-rs/docs/dye-editor-scope.md`](../vendor/crimson-rs/docs/dye-editor-scope.md)
>   §"Recommended C# editor UX". Multi-hour rewrite; not started.
>
> ### Open follow-ons carried over (no change)
>
> - **Pattern B v2 for multi-objective SA challenges** (from part 1).
> - **OCT forum post URL placeholder** (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 2)
>
> Three commits landed today on top of part 1, all pushed to `dev`. Final
> tip: `f85c4e5`. Vendor `crimson-rs` refreshed `21f1883` → `4ee430b`
> (8 upstream commits, +5604 LOC) — consumed below.
>
> | Area | Scope |
> |---|---|
> | **`list_all_items` + `set_object_list_present` + quest rollup bindings** (`ab01db2`) | Consumed 7 new C ABI exports from the vendor refresh. New 64-byte `repr(C)` `ItemRecord` blittable struct (layout-pin test) + `ContainerKind` enum + `ItemRecordFlags` constants. `ISaveLoader.ListAllItems(out version)` exposes the single-FFI cross-container walk (`ActiveEquip` + `ActiveUseReserve` + `Inventory` + `MercenaryEquip` + `MercenaryInventory`, 829 items vs the inventory-only 545 on slot103). `LocalizationProvider.IsPlayerEditableItem` widens the strict `IS_PLAYER_OWNED` flag to admit the 8 player-controlled mounts whose `_ownedCharacterKey` is absent (`Riding_*` / `Animal_*` / `Vehicle_*` prefix per upstream recipe); new `FormatItemSourceLabel` is the single source of truth for the inventory / sockets / dye editors' source-column label. **`ISaveLoader.SetObjectListPresent`** + `NOT_OBJECT_LIST = -23` constant — closes the ObjectList-toggle path used by "+ Add Dye" (below). Live-save roundtrip test handles both ≥ 2-dyed and solo-dyed save edge cases (solo case correctly returns `NOT_FOUND`). **Two static facade catalogs** — `NativeMainQuestChapter` (~170 rows Prologue + 12 chapters + Epilogue) + `NativeSideQuestFaction` (84 quests / 22 factions, both directions); no handle / no file load — pure static lookups. 13 new tests (3 ItemRecord + 1 SetObjectListPresent + 9 rollup bridges + roundtrip). |
> | **Dye + Sockets walk `ListAllItems` + "+ Add Dye" UX** (`70fae58`) | Both editors used to walk every top-level block, filter by class (`InventorySaveData` / `EquipmentSaveData`), then drill into nested item lists by field name (~150 LOC of dual `CollectFromInventory` / `CollectFromEquipment` walkers across the two editors). Rewritten to a single `ListAllItems` FFI call → filter by `HasDyeData` / `HasSocketData` flag + `IsPlayerEditableItem` widening → descend each record's 2-step path to find the inner `ItemSaveData`. **Real bug fix**: the prior walkers ignored `MercenaryClanSaveData` entirely; 245 `MercenaryEquip` + 20 `MercenaryInventory` items (mounts' equipped gear + inactive Damine/Oongka equip) were silently invisible to both editors. Both surfaces now show those items labeled e.g. "Riding_Horse_Black_31378 (Equipped)". **"+ Add Dye" UX**: Dye editor's scan now surfaces un-dyed equipped items (`ActiveEquip` / `ActiveUseReserve` / `MercenaryEquip` — inventory bags excluded to keep list manageable) as "+ Add" rows alongside the existing "Edit" rows. Click "+ Add" → confirm dialog → `SetObjectListPresent(makePresent=true)` materializes a default-empty dye element → master VM refresh. `NOT_FOUND` (no template sibling in the save) surfaces a localized alert explaining the user must dye one item in-game first. Bilingual strings added (en / ja / zh-TW). Dropped unused `_blocks` ctor param + 7 orphan constants in each editor; cleaned up obsolete v1-scope docstring. |
> | **Mark-Challenge-Complete skip-reason tooltip** (`f85c4e5`) | Pre-refactor: the per-row "Mark Challenge Complete" button used a single `IsCurrentChallengeMarkable` gate bound to `IsVisible` — on an ineligible `MissionStateData` row the button silently vanished, leaving the user with no signal whether they were on the wrong kind of row or on the right row with a missing precondition. Split into `IsCurrentNavOnMissionStateRow` (visibility — class + path-of-length-1 only) + `IsCurrentChallengeMarkable` (enablement — full eligibility, unchanged) + new `CurrentChallengeMarkTooltip` that composes the localized tooltip text: eligible → `MarkChallengeCompleteTip` resource (unchanged), ineligible → `{MarkChallengeSkipReasonPrefix} {skipReason}` reusing the existing bulk-sweep `TryBuildChallengeContextFromCatalogRow(..., out string? skipReason)` plumbing. The user now sees exactly which gate failed (e.g. "adjacent twin _state != 5 (artifact never picked up)" or "FAR tracker (key 0x...) not found in _missionStateList"). Three navigation-bump call sites consolidated through a new `NotifyMarkChallengeStateChanged` helper. |
>
> Tests across the session: **257 → 268** (+11 net: 3 layout/cross-container + 1 SetObjectListPresent roundtrip + 9 quest rollup; existing tests unaffected). Debug build clean at every commit. Three commits on `dev`; FF merge into `main` pending.
>
> ### Open follow-ons noted during this session
>
> - **Pattern B v2 for multi-objective SA challenges** (carried over from part 1, unchanged): still needs RE diff of slot106 → slot107 → post-claim. Diagnostic scripts retained in `tools/inspect/sa-investigations/`.
> - **OCT forum post URL placeholder** (carried over from part 1, unchanged): `docs/oct/features-highlights.md` placeholder `*(GitHub URL — fill in before posting)*` still needs filling.
>
> ### Open follow-ons resolved this session
>
> - ~~Add dye to undyed item~~ — **shipped** (`70fae58`). `+ Add Dye` button materializes a default-empty dye element via the new `SetObjectListPresent` toggle; `NOT_FOUND` UX prompts the user to dye one item in-game first when no template sibling exists. Deferred-note comment at the previous `DyeEditorViewModel.cs:22-25` removed in the refactor.
> - ~~CharacterKey picker tooltip~~ — **shipped** (`f85c4e5`). (Originally misnamed in the part-1 follow-on list — the task was the Mark-Challenge-Complete button tooltip, not the CharacterKey picker. Now uses the existing `skipReason` plumbing to explain why the button is grayed out.)
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17)
>
> Five related commits landed today, all pushed to `dev` and FF-merged
> into `main`. Final tip: `9906c00`.
>
> | Area | Scope |
> |---|---|
> | **13 niche `crimson-rs` bridges** (`69f1a01`) | Consumed vendor commit `115e4d8`. New `[LibraryImport]` + `Native<X>Catalog` wrappers for: HouseKey / RoyalSupplyKey / CraftToolKey / CraftToolGroupKey / TriggerRegionInfoKey / GamePlayVariableKey / GlobalGameEventInfoKey / GlobalGameEventGroupKey / GameAdviceInfoKey / GameAdviceGroupKey / ReserveSlotKey / RegionKey / ItemGroupKey. Single `TryBootstrapNicheBridges` + generic `TryLoadNicheBridge<T>` helper opens the group-0008 PAMT once + extracts all 13 pairs. 78 new exports total; 1 new live-install smoke test (`NicheBridges_LiveInstall_LoadAllAndResolveKnownKeys`). |
> | **Dye Item-column secondary-language + 3× deferred-redecode wrappers** (`b140b60`) | Fix: Dye master dialog's Item column was binding `ItemNameEnglish` only (Secondary language never showed). Fix mirrors Sockets editor pattern — pre-format `"English / Secondary"` into a new `ItemName` property, AXAML binds to that, filter still substring-matches each language separately. Plus: `loader.RunDeferred(...)` wrapped around Sockets Apply-Set, Dye slot multi-Apply, and Unlock-All-Abyss-Gates per-key inject — each collapses N body re-decodes to 1, same contract + partial-success semantics as the SA bulk sweep. |
> | **Four UX feedback items** (`f64f6c2`) | (1) Rename Mercenary: dropped the manually-implemented `CanApply` gate (wasn't wired to `NotifyCanExecuteChanged`); TextBox uses `UpdateSourceTrigger=PropertyChanged`; window widened 900→1280 with column resizes. (2) Dye Editor: filtered blocks by class BEFORE `LoadBlockDetails` (was loading every block in the save → 10-20× speedup before async even helps) + new `RefreshAsync` runs on thread pool + ctor sets `IsLoading=true` so the dialog paints "Loading dyed items…" immediately. (3) Vendor Buyback: new per-row **Jump…** button → closes the dialog + navigates the main editor's block tree to the buyback item via new `NavigateToVendorBuybackItemAsync`. (4) Bulk SA sweep: dropped both held-artifact gates (outer + inner `HoldsAnySealedAbyssArtifact`) per user directive; added `out string? skipReason` to `TryBuildChallengeContextFromCatalogRow` + `BulkSaPreview.SkipDetails` list surfaced per-key in the confirm dialog so users see exactly which challenges were skipped and why. Investigation against slot102/105/106/107 revealed multi-objective Living_*/Cooking-style challenges store sub-steps with **negative-encoded** keys (`0xFFFFxxxx`) which the missioninfo anchor-scan parser filters out → Pattern B v1 X_2 lookup fails → correctly skipped. Pattern B v1 is verified only on linear single-step SA challenges; multi-objective shape needs a future Pattern B v2. |
> | **F32x3 / F32x4 / U32x4 typed setters** (`3aecd06`) | Consumed vendor commits `94c7a96` (typed display variants) + `23a9e0d` (typed setters) + `c9343f8` (dynamic_array inline JSON). Chose byte-packing in C# over wiring the 6 new typed setter `[LibraryImport]` entries — functionally equivalent (both produce the same 12 / 16 LE byte payload via the existing generic `SetScalarField`), less ABI surface. `ScalarFieldEditing.SupportedTypeTags` += `{f32x3, f32x4, u32x4}`; `TryEncode` adds bracketed / bare comma-separated parsers; `TryInferTypeTagFromSchema` accepts `float3` / `float4` / `Quaternion` / `uint4` / `SceneObjectUuid` size-gated. 15 new test cases; 200/200 → 215/215. dynamic_array inline display is automatic on DLL rebuild. |
> | **Character Refs Browser + CharacterKey picker + first-launch disclaimer** (`59b6d88`) | Consumed vendor commit `88ef779` (`crimson_save_list_character_refs`). New `CharacterRefRecord` (16-byte `repr(C)`) + `ISaveLoader.ListCharacterRefs(out version)` wrapper mirroring the inventory enumerator two-call dance. **Tools → Browse Character References** dialog lists every save-side CharacterKey reference (top-level + nested) with portrait + resolved name + Jump-to-block button → new `MainWindowViewModel.NavigateToTopLevelBlockAsync`. **Modal pick mode** added to existing `CharacterPickerWindow` — new **"Pick character…"** button in the edit panel visible when `SelectedField.TypeName == "CharacterKey"`; modal returns selected key which fills the textbox for Apply. Both dialogs carry bilingual warning banner about reference linkage. **First-launch disclaimer**: `AppSettings.DisclaimerAcceptedVersion` (version-gated) + `DisclaimerDialog` shown on `MainWindow.Opened`; Accept persists version 1 + continues, Decline calls `desktop.Shutdown()`. Bilingual body (中文 + English). 215/215 → 216/216. |
> | **Documentation + scratch reorg** (`9906c00`) | Promoted 8 of 10 `tools/inspect/_proto_*.py` SA investigation scripts to `tools/inspect/sa-investigations/` with stable names + curated README.md (dropped two one-off probes). New `docs/oct/` folder: `phpBB-markdown-syntax.md` (opencheattables.com markdown dialect reference) + `features-highlights.md` (forum-post draft using that dialect). README / docs index updates to match. |
>
> Tests progression across the session: **190/190 → 191/191 → 200/200 → 215/215 → 216/216**. Debug build clean at every commit. All work on `dev` then FF-merged into `main`; both branches in sync at `9906c00`.
>
> ### Open follow-ons noted during this session
>
> - **Pattern B v2 for multi-objective SA challenges**: needs RE diff of
>   slot106 (progress 2/3) → slot107 (progress 3/3, reward unclaimed)
>   → post-claim to determine the engine-natural completion shape for
>   challenges with negative-keyed sub-steps. Either a `crimson-rs`
>   parser change to surface those rows OR a C# byte-scan fallback that
>   recovers the key directly from `missioninfo.pabgb` bytes. Diagnostic
>   scripts retained in `tools/inspect/sa-investigations/`.
> - **CharacterKey picker tooltip**: when `TryReadCurrentChallengeContext`
>   returns false for a per-row "Mark Challenge Complete" button on
>   `MissionStateData` rows, the button just grays out silently. Could
>   surface the skip reason as a tooltip (the diagnostic plumbing is
>   already in place for the bulk sweep — just needs a tooltip binding).
> - **Add dye to undyed item — READY TO CONSUME** (2026-05-17 vendor refresh, `crimson-rs` at `21f1883`):
>   the upstream `crimson_save_set_object_list_present` C ABI has
>   landed. Contract: `present_flag=1` flips mask bit + auto-materializes
>   `count=1` with a default-empty element (element class borrowed from
>   any sibling block of the same parent class with the field present);
>   caller then drives RGBA / material / color-group via existing
>   `set_scalar_field_present` against element 0. `present_flag=0` is
>   byte-identical to never-present. New error code `NOT_OBJECT_LIST = -23`
>   for meta_kind ∉ {6,7} rejection; `NOT_FOUND` if no template sibling
>   exists in the save (UX needs to handle: prompt user to dye one item
>   in-game first). Pinned upstream by
>   `c_abi_object_list_present_roundtrip_dye_data_list_slot104`. Consume
>   plan: rebuild `crimson_rs.dll` (`.\scripts\build_rust.ps1`) → add
>   `[LibraryImport]` on `NativeMethods` + `NOT_OBJECT_LIST` constant →
>   wrap on `ISaveLoader`/`NativeSaveLoader` mirroring `SetScalarFieldPresent`
>   path shape → add "+ Add Dye" button to `DyeEditorViewModel` for rows
>   with absent `_itemDyeDataList` → remove the deferred-note comment
>   at `src/CrimsonAtomtic.Ui/ViewModels/DyeEditorViewModel.cs:22-25` →
>   one integration test mirroring the Rust round-trip. Full vendor-side
>   contract docs at
>   [`vendor/crimson-rs/docs/dye-editor-scope.md`](../vendor/crimson-rs/docs/dye-editor-scope.md) §v2.
> - **OCT forum post**: `docs/oct/features-highlights.md` has a
>   placeholder `*(GitHub URL — fill in before posting)*` to fix
>   before pasting into the forum.
>
> ---
>
> ## ✅ This session — what shipped (2026-05-16 part 17)
>
> Consumed `vendor/crimson-rs`'s new **deferred-redecode batch** ABI
> (commit `a161064`; full contract at
> [`vendor/crimson-rs/docs/save-deferred-redecode.md`](../vendor/crimson-rs/docs/save-deferred-redecode.md)).
> The bulk "Complete All Held Sealed Abyss Artifact Challenges"
> sweep now wraps its 141-challenge apply loop in
> `begin_/end_deferred_redecode`, collapsing 423 per-call body
> re-decodes (3 length-changing FFI calls × 141 challenges) into
> ONE encode + parse + decode pass at commit.
>
> | Area | Scope |
> |---|---|
> | **C# batch API** | 4 new `[LibraryImport]` entries on `NativeMethods` (`BeginDeferredRedecode` / `EndDeferredRedecode` / `AbortDeferredRedecode` / `IsDeferredRedecodeOpen`) + 2 new error constants (`BATCH_IN_PROGRESS = -21`, `BATCH_NOT_OPEN = -22`). `NativeSaveLoader` exposes 5 wrappers; `ISaveLoader` mirrors them with full docstrings including the partial-success contract. The recommended call site is `loader.RunDeferred(Action body)` — auto-aborts on exception (rolls handle back to pre-begin state), surfaces commit-time `MUTATION_INVALID` as `CrimsonSaveException`. |
> | **Bulk SA sweep wiring** | `BulkCompleteHeldSealedArtifactChallengesAsync`'s apply loop now runs inside `loader.RunDeferred`. Per-op failures are captured into local `loopError` / `loopErrorKey` and the `foreach` falls through normally — that path preserves the pre-batch "partial-success keeps already-applied work" UX (letting the exception escape `RunDeferred` would Abort and lose work, which is wrong for this flow). A commit-time `MUTATION_INVALID` is caught at the outer level and surfaced as `applied=0`. |
> | **7 new tests** in `NativeSaveLoaderTests` | `DeferredRedecode_AbortRestoresPreBeginState`, `DeferredRedecode_EndBumpsVersionExactlyOnce`, `DeferredRedecode_NestedBeginReturnsBatchInProgress`, `DeferredRedecode_EndOrAbortWithNoBatch_ReturnsBatchNotOpen`, `DeferredRedecode_WriteToFileRejectedMidBatch`, `RunDeferred_AutoCommitsOnNormalReturn`, `RunDeferred_AutoAbortsOnException`. All pin the ABI contract against the live save. |
>
> **Measured speedup**, 100 clone+remove cycles (= 200 length-changing ops) against the slot102 save's largest object_list, Debug build:
>
> | Mode | Time | Per-op |
> |---|---:|---:|
> | Normal (per-op decode_blocks) | **197,025 ms** | 1970 ms |
> | Deferred batch (1 decode at end) | **994 ms** | 10 ms |
> | **Speedup** | — | **198×** |
>
> For the actual 141-challenge SA sweep the loop now runs in well under a second instead of the previous ~10 s. Release-mode numbers will improve both sides, but the ratio holds.
>
> Tests: **190/190 pass** (was 183; +7 deferred-redecode contract tests). Debug build clean.
>
> Future opportunity: the same wrapper could wrap any multi-mutation Tools-menu flow (Sockets-Apply-Set bulk, Dye slot editor's multi-Apply, Unlock-All-Abyss-Gates). Each would land a similar order-of-magnitude win.
>
> ## ✅ This session — what shipped (2026-05-16 part 16)
>
> StoreKey bridge consumed + a new Vendor Buyback dialog built on
> top of it. Powered by `vendor/crimson-rs` commit `3af28de` (storeinfo
> bridge) which lights up 292 store templates in 1.07.
>
> | Area | Scope |
> |---|---|
> | **StoreKey resolver bridge — `storeinfo.pabgb` / `.pabgh`** | 6 new `[LibraryImport]` entries on `NativeMethods` (two-file load + free + entry_count + lookup_string_key + get_entry). New `NativeStoreInfoCatalog` (mirrors `NativeSkillInfoCatalog`'s two-file pattern) + `CrimsonStoreInfoHandle` SafeHandle. `LocalizationProvider`: `_storeInfo` field + `StoreInfo` accessor + `TryBootstrapStoreInfo` helper called from `TryBootstrapFromGameRoot`. `"StoreKey"` added to `TableDrivenKeyTypes` + dispatch branch in `ResolveKeyTableOne` — lights up `StoreDataSaveData._storeKey` in the resolved-name column. Internal name only — no PALOC chain (the secondary-language column echoes the primary; same convention as QuestGauge / Skill). |
> | **Tools → Vendor Buyback dialog (v1: view + remove)** | New dialog surfaces every item the player has sold to vendors that still sits in a per-store buyback queue. Walks the singleton `StoreSaveData._storeDataList` → per-store `StoreDataSaveData._storeSoldItemDataList`, emits one row per sold `ItemSaveData`. Resolved columns: Store (via the new bridge) + StoreKey + Item (English / secondary via iteminfo + PALOC) + ItemKey + Stack + Endurance + SoldAt ticks. Two-pass filter (same shape as Sockets v2): store-name match expands to every row in that store; item-name / item-key narrows per-row. Per-row **Remove** action calls `ListRemoveElement`; after success, every sibling row in the same store whose `BuybackElementIdx` sat above the removed one is shifted down by 1 (list-element-shift invariant). |
>
> End-to-end verified against slot102 (17 rows across 5 stores — `Store_Camp_Accessory` 4-item set + `Store_Camp_Grocery` 4-item set + 3 other stores) and slot105 (10 rows across 4 stores).
>
> **Out of scope for v1**: "Move back to inventory" (clone `ItemSaveData` into a target bag's `_itemList` + remove from buyback) — needs target-bag picking + per-bag empty-slot detection. Planned follow-up. Edit stack / endurance / sockets — the generic block editor handles those; the dedicated dialog stays focused on the per-row buyback decision.
>
> Tests: **183/183 pass** (no new tests — wiring of an existing-pattern bridge + a view-only dialog with one mutation surface that already has coverage via `ListRemoveElement` tests). Debug build clean. Rebuilt `crimson_rs.dll` to expose the new storeinfo exports.
>
> ## ✅ This session — what shipped (2026-05-16 part 15)
>
> Two improvements to the editor's resolver + bulk-op infrastructure
> that don't need crimson-rs changes:
>
> | Area | Scope |
> |---|---|
> | **Tier 1 key-resolver wiring — `DyeColorGroupInfoKey` + `StringInfoKey`** | The dye-color-group bridge (`_dyeColorGroupInfo`) is already populated for the Dye editor's dropdown but wasn't routed for `ItemDyeSaveData._dyeColorGroupInfoKey`. Added `"DyeColorGroupInfoKey"` to `TableDrivenKeyTypes` + a switch branch in `ResolveKeyTableOne` that delegates to `LookupName`. Same for `"StringInfoKey"` scalar fields: pre-computed Jenkins hashes the existing stringinfo bridge reverses in one hop — covers `UseItemReserveSlotElementSaveData._specialNameKey`, `FactionNodeSubInnerEnableElementSaveData._levelNameKey`, etc. Dynamic-array StringInfoKey fields like `MissionStateData._usedTagList` stay on the raw-array display path (collections, not single names). |
> | **Bulk SA sweep — live progress + scalar batching** | Before this turn the bulk "Complete All Held Sealed Abyss Artifact Challenges" sweep took ~8–10 s on a 141-artifact set with a frozen-looking status footer. Added (a) live progress via `IProgress<(Done, Total, CurrentKey)>` — `Progress<T>` captures the UI `SynchronizationContext` at construction, so `Report(...)` from inside `Task.Run` posts back to the UI thread automatically; status footer now animates "Applying Pattern B v1: N / 141 — challenge 0xXXXXXXXX" as each apply lands; (b) batched the trailing scalar setters in `ApplyPatternBv1Writes` via `SetScalarFieldsBatch` — up to 4 scalar mutations per challenge now ship in one FFI roundtrip instead of up to 4. Modest wall-clock impact (re-decode cost dominates), but cleaner code + cuts validation overhead. **The big speedup landed in part 17 via the deferred-redecode batch.** |
>
> Tier-1 gap inventory diagnosis surfaced 45 distinct typed-ID TypeNames in slot105's save; 13 are already resolved, 27 fall into 4 tiers by tractability (per the inline analysis): **Tier 1** = pure C# wiring (DyeColorGroup + StringInfoKey above + already-loaded bridges); **Tier 2** = high-value `crimson-rs` bridges (Faction* set / StoreKey / MercenaryKey / FieldInfoKey — most landed upstream in the same vendor refresh); **Tier 3** = niche bridges (GameAdvice / RoyalSupply / etc.); **Tier 4** = per-save instance IDs (not gamedata-lookupable).
>
> Tests: **183/183 pass** (no new tests — pure wiring + UI plumbing). Debug build clean.
>
> ## ✅ This session — what shipped (2026-05-16 part 14)
>
> Four UI bug fixes / UX improvements landed back-to-back on the
> Sockets editor, Dye editor, and Rename Mercenary dialog. All
> driven by user feedback against the live 1.07 save.
>
> | Area | Scope |
> |---|---|
> | **Rename Mercenary — generic class glyph fallback when no NPC portrait** | Diagnosis: 95+ rows in the user's MercenaryClanSaveData showed blank Portrait cells. Probe revealed `0012/0.pamt` ships only 3 named-NPC portraits in 1.07 (`demian` / `kliff` / `oongka`); every other "mercenary" CharKey is actually a mount / wagon / animal with template names like `Riding_Horse_Tiuta_Unique_2050_kliff`, `Riding_Balloon_Summoner_3`, `Animal_Stefano_Wild_31364`. Pearl Abyss doesn't ship per-character portraits for them. Fix: bucket each row by characterinfo internal-name prefix into a `MercenaryCategory` (Npc / Mount / Wagon / Balloon / Animal / Pet / Unknown). Render a centered Unicode glyph (👤 / 🐎 / 🛒 / 🎈 / 🦌 / 🐾 / ❔) in the cell when no real portrait loads. New `LocalizationProvider.LookupCharacterInternalName(uint)` helper delegates to `_characterInfo.LookupStringKey`. Cell template becomes a `<Panel>` with both Image (when `HasPortrait`) and TextBlock (glyph). |
> | **Sockets editor — visible Item column + live filter (en + secondary)** | Two bugs in one commit. (a) The shipped AXAML had `Width="2*"` on both Item and Current-gem columns competing against ~970 px of fixed-width siblings in a 980 px window — the proportional columns ended up with ~5 px each, so the Item column never showed. Fixed widths (Item=260, Current-gem=220, both `MinWidth=120`) + bumped default window width to 1320×600. (b) Added a Filter TextBox above the Apply-Set toolbar, bound to `SocketEditorViewModel.SearchText`. `SocketRow` now stores `ItemNameEnglish` + `ItemNameSecondary` separately (matching Find Items / Sockets v2 pattern); filter does case-insensitive substring against bag label / item name (en + secondary) / item key / current gem name / current gem key. |
> | **Sockets + Dye editors — include equipped gear (`EquipmentSaveData`)** | Both editors walked only `InventorySaveData` blocks; equipped items under `EquipmentSaveData._list[i]._item` (object_locator → inline `ItemSaveData` child) never showed up. In slot105 that meant **18 equipped items × 5 sockets = 90 missing socket rows**, plus any dyed equipped armour silently absent. **No `crimson_rs` change needed** — equipped items use the same `ItemSaveData` schema; the only difference is the descent path: `[(_list, slotIdx), (_item, 0), …]` instead of `[(_inventorylist, bagIdx), (_itemList, itemIdx), …]`. Both are 2-step descents the existing path-addressed ABI already handles (object-list + locator). Sockets: new `CollectFromEquipment` walk runs alongside `CollectFromInventory`; the four existing field-index getters on `SocketRow` get reinterpreted as "first descent step + second descent step" pairs so all the path-construction code stays unchanged. Dye: bigger refactor — the master VM used `ListInventoryItems` FFI (inventory-only) and the slot VM hardcoded an `_inventorylist → _itemList` drill; both rewritten to direct block walking, `DyeEditorItemRow` drops the `InventoryItemRecord` field and carries the same first/second-step indices, `DyeSlotEditorViewModel.LoadSlots` uses a generic `TryDescend` helper. End-to-end verified: SocketEditor now surfaces 2,840 rows (2,750 inventory + 90 equipped). |
> | **Sockets editor — two-pass filter so item-name match shows empty slots** | Diagnosis: the user's "電" filter showed only 1 row for Kuku Rishi's Boots (slot 2 holding Greater Shockward). The other 4 slots existed but were filtered out — the "Item" column / item-name filter and the "Current gem" column / gem-name filter were OR'd together per-row, so empty slots and non-electric gems on the same item got hidden. Fix: split `SocketRow.MatchesFilter` into `MatchesItemFilter` (bag / item name / item key) and `MatchesSocketFilter` (gem name / gem key). `ApplyFilter` does a two-pass walk: pass 1 collects every item identity `(BlockIndex, BagIndex, ItemIndex)` whose parent fields match; pass 2 emits every row whose item is in the matched set OR whose own gem matches. Verified: "Kuku Rishi" → all 5 slots (3 filled + 2 with Fill...), "Equipped" → all 90 equipped slots, narrow gem-name filter unchanged. |
>
> Tests: **183/183 pass** throughout (no new tests — UI fixes are pure presentation-layer plumbing over existing primitives, all of which have their own coverage). Debug build clean. No `vendor/crimson-rs` change in any of the four.
>
> ## ✅ This session — what shipped (2026-05-16 part 13)
>
> Both Tools → Abyss Gates entry points reported empty / wrong data on
> the user's 1.07 save (and, as cross-version probes against slots
> 102 / 103 / 104 confirmed, on every prior 1.05–1.06 save too). The
> shipped v1 of each made the wrong shape assumption; both rewritten
> against ground-truth shapes.
>
> | Area | Scope |
> |---|---|
> | **Per-gate dialog — walks nested `FieldSaveData._fieldGimmickSaveDataList`** | v1 looked for top-level `FieldGimmickSaveData` blocks (always 0 across all probed saves) and reported "no abyss gates in this save." v2 walks every top-level `FieldSaveData` root, drills into `_fieldGimmickSaveDataList` via the existing `DecodedFieldRow.Elements` path, filters elements by `_gimmickInfoKey` against the abyss/hyperspace allowlist from `gimmickinfo.pabgb`, and exposes the toggle via the path-addressed `SetScalarField` (path = `[(_fieldGimmickSaveDataList, elemIdx)]`). The 4,200-ish nested elements per save take ~1 s to scan. Header / not-available strings updated. |
> | **Bulk inject — rewritten for `object_list<KnowledgeElementSaveData>`** | v1 assumed `KnowledgeSaveData._list` was `dynamic_array<u32>` and called `DynamicArrayGetU32Elements` / `DynamicArraySetU32Elements`. The probes confirmed `_list` is always an object_list of full `KnowledgeElementSaveData` records (~1,740 per save) with `{ _key:u32, _level:u8, _learnedFieldTime:u64, _isNewMark:bool }`. The shipped read silently failed → "0 already present" regardless of progression; the shipped write would have stuffed u32 bytes into an object_list field (potential save corruption). v2 reads existing `_key`s out of the elements to build the "already have" set, then for each missing key clones element 0 + patches `_key` / `_learnedFieldTime=0` / `_isNewMark=0` via `ListCloneElement` + `SetScalarField` — same primitives as the bulk Sealed Abyss Artifact sweep. Aborts on first failure (list may now be inconsistent) and surfaces the failing key in the status footer. |
> | **2 new shape regressions in `NativeSaveLoaderTests`** | `KnowledgeSaveData_List_IsObjectListWithKeyedElements` and `FieldGimmickSaveData_NestedUnderFieldSaveDataNotTopLevel`. Pure read assertions on the live save — they pin the shapes the rewrites depend on so a future schema drift (1.08?) surfaces here as a typed test failure instead of as silent UX breakage. |
>
> Defensive guards added to both flows: per-gate dialog reports "no FieldSaveData root yet (very early-game)" when the walk finds 0 roots; bulk inject refuses to run when `_list.Kind != "object_list"` (calls out the schema drift instead of attempting the wrong write).
>
> Tests: **183/183 pass** (was 181; +2 schema regressions covering both Abyss Gates flows). Debug build clean. No `vendor/crimson-rs` changes needed.
>
> ## ✅ This session — what shipped (2026-05-16 part 12)
>
> Bulk variant of the per-row "Mark Challenge Complete" button.
> Enabled by the latest `vendor/crimson-rs` refresh: the new
> `iteminfo_lookup_artifact_for_mission` cements the 1:1 mapping
> invariant (141 challenges / 141 artifacts in 1.07, all named
> `Challenge_SealedArtifact_*`) so iterating from the held-artifact
> side cleanly identifies every gateable challenge.
>
> | Area | Scope |
> |---|---|
> | **`artifact_for_mission` binding + Pattern B v1 helper extraction** | Native binding for the reverse lookup (`NativeMethods.ItemInfoLookupArtifactForMission` + `IItemInfoCatalog.LookupArtifactForMission`). Per-row apply logic refactored: `TryReadCurrentChallengeContext` becomes a thin nav-stack wrapper around the new addressable-by-coords `TryBuildChallengeContextFromCatalogRow`; the FFI write sequence extracted into `ApplyPatternBv1Writes(ctx, newCt, appendElementIdx, farKeyFieldIdx)`. Per-row button is byte-identical in behaviour. |
> | **Tools → Complete All Held Sealed Abyss Artifact Challenges** | New `[RelayCommand]` `BulkCompleteHeldSealedArtifactChallengesAsync`. Pre-flight: walk every InventorySaveData → collect held SA artifact ItemKeys → map to mission keys via `iteminfo.look_detail_mission_info` → find matching catalog rows in every `QuestSaveData._missionStateList` → run each through `TryBuildChallengeContextFromCatalogRow`. Confirm dialog shows the breakdown (held / eligible / skipped-already-done / skipped-FAR-not-ready / skipped-no-mission / skipped-other). Apply loop iterates `ApplyPatternBv1Writes` per challenge, bumping `newCt` per call so timestamps sort distinctly + tracking per-block running append index so successive X_2 inserts land at the right slot. Partial-failure mid-sweep marks the save dirty with applied count + reports the failing challenge key. |
>
> Per-challenge cost is 5–6 length-changing FFI calls (each triggers a full body re-decode on the Rust side); for a player holding all 141 artifacts the whole sweep is on the order of 7–10 seconds. Sequential — no batch ABI for `list_clone_element` yet. Acceptable for a one-shot Tools menu action; backup-before-write still applies via the existing `BackupBeforeWriteSilent` on the next File → Save.
>
> Tests: **181/181 pass** (no new unit tests — bulk sweep is plumbing over the now-extracted Pattern B v1 primitives that have their own per-row coverage). Debug build clean. Rebuilt `crimson_rs.dll` to expose the new `lookup_artifact_for_mission` export.
>
> ## ✅ This session — what shipped (2026-05-16 part 11)
>
> Three commits consuming the latest `vendor/crimson-rs` iteminfo
> additions and rebuilding the Sockets editor end-to-end:
>
> | Area | Scope |
> |---|---|
> | **iteminfo socket + canonical-gem bindings** | 4 new C ABI exports bound: `lookup_socket_caps`, `socket_allows_gem`, `canonical_gem_count`, `canonical_gem_at`. Exposed on both `IItemInfoCatalog` and `NativeItemInfoCatalog`. The canonical gem set is the sorted-ascending union of every weapon's allowed-gem list — authoritative replacement for the prefix-heuristic gem filter (kept in place for the picker, which still works against the existing `Item_Stat_AbyssGear_*` / `Item_Skill_AbyssGear_*` prefix scope). |
> | **Sockets editor v2 (Fill / Change / Clear)** | Surfaces every slot in `_socketSaveDataList` (empty + filled), not just filled. Per-row routes by state: empty → Fill (batch-promote `_currentEndurance` + `_itemKey`), filled → Change (in-place overwrite both fields, **resets endurance to 0xFFFF** so greater gems start fresh) and Clear (batch-demote to absent). `_validSocketCount` auto-bumps when filling a slot past the current cap; no gamedata-cap enforcement (you can fill any slot the underlying list pre-allocates, ~5 typical, per user request). Status column surfaces per-row errors so failures stay visible. |
> | **Built-in + user-custom gem sets + Apply-Set toolbar** | Three hardcoded built-in sets (the user-provided keysets), three user-definable sets persisted to `AppSettings.CustomGemSets`. Sockets editor toolbar: pick item → pick set → Apply. Apply rule per the user contract: a set with N gems overwrites slots `0..N-1` only (slots `[N..max]` left alone); auto-bumps `_validSocketCount` per slot as needed. Dropdown labels resolve each gem key through PALOC at runtime so users see "Built-in Set 1 — Sapphire / Ruby / …" instead of just numbers. Custom-set editor launched from the toolbar "Edit custom sets…" button — 3 rows × (Label + 5 ItemKey TextBoxes), writes settings.json on Save. |
>
> Endurance handling note: there's no `crimson_iteminfo_lookup_max_endurance` C ABI today, so the editor uses `0xFFFF` (u16 max) as the "fresh gem" sentinel for Fill + Change. Safe for both gem kinds — durability-bearing greater gems get full durability, no-durability gems have the field ignored by the engine. If upstream adds a per-gem max getter later, the editor can swap to that without touching anything else.
>
> Tests: **181/181 pass** (was 180; +1 SocketCaps_AndCanonicalGemSet_LiveInstall covering the 4 new APIs against the live install). Debug build clean. Rebuilt `crimson_rs.dll` to expose the new exports.
>
> ## ✅ This session — what shipped (2026-05-16 part 10)
>
> Two paired UX features the user asked for after picking up #11
> "Save backup management" and deciding the more valuable half was
> a confirm-on-close + change-log:
>
> | Area | Scope |
> |---|---|
> | **`ChangeJournal` service** | New `Services/ChangeJournal.cs` — singleton append-only log. Each entry: `(Timestamp, Category, Summary, Details?)`. Lifecycle mirrors `IsDirty`: cleared on every successful `Save` + `Load` + `SaveAs`. Exposed via `MainWindowViewModel.Journal`. Granularity = **per named operation** (sockets, dye slot, abyss gate toggle, etc.) — not per scalar field. Bigger Level B (raw before/after capture) deferred until the user actually demands it. |
> | **Close-on-dirty confirm** | `MainWindow.Closing` event handler reads `vm.Journal.HasUnsavedChanges` and, if true, cancels the default close + opens `ChangeSummaryDialog` showing all pending entries. 3 buttons: Save (commits + closes), Discard & exit (closes without saving), Cancel (stays open). Save failure mid-flow re-prompts via an alert + leaves the app open. |
> | **Tools → Review Pending Changes…** | Same `ChangeSummaryDialog` (different header text) on demand — user can browse the journal without closing. Discard here means "reload save without writing" (which clears the journal via the normal Load path). |
> | **All mutation entry points instrumented** | 10 sites log per-operation summaries: main edit panel `CommitFieldEdit` + `MakeFieldAbsent`, `MarkChallengeComplete`, `BulkFillItemListMaxStack` (single + container variants), `FillAllStacksAcrossInventories`, `RemoveElement`, `AddItemToCurrentList`, `UnlockAllAbyssGates`, plus dialog VMs: `RenameMercenaryViewModel.ApplyRename`, `SocketEditorViewModel.ApplyGemPick`, `AbyssGatesViewModel.Apply`, `DyeSlotEditorViewModel` (one entry per per-slot Apply, listing which scalars flipped). Each dialog VM gained a `ChangeJournal` constructor parameter; MainWindow code-behind threads `vm.Journal` through at construction. |
>
> Tests: **180/180 pass** (no new unit tests — the journal is pure C# state-tracking; manual smoke-test path is "edit → File → exit → modal shows N entries → choose"). Debug build clean.
>
> Closes deferred item #11 (Save backup management — the "auto-backup on every load" half) by superseding rather than implementing: the user doesn't want load-time backups; the close-on-dirty confirm gives them the actual safety net they wanted ("warn me I'm about to lose edits"). Backup retention (the other #11 half — making `MaxVersionsPerSlot` user-configurable) stays deferred.
>
> ## ✅ This session — what shipped (2026-05-16 part 9)
>
> Roadmap pick **#5 Dye editor** consumed in two clean commits.
> Pure C# work — `vendor/crimson-rs` shipped all three dye gamedata
> bridges (`dye_color_group_info` + `part_prefab_dye_texture_pallete_info`
> + `part_prefab_dye_slot_info`) plus the supporting parsers in the
> 2026-05-16 vendor refresh.
>
> | Area | Scope |
> |---|---|
> | **3 dye gamedata bridge bindings + LocalizationProvider integration** | New `NativeDyeCatalogs.cs` exposes `NativeDyeColorGroupInfoCatalog` (10 named groups), `NativePartPrefabDyeTexturePalleteCatalog` (11 palette tiers × 2-3 sub-records each), `NativePartPrefabDyeSlotInfoCatalog` (1,105 per-prefab slot definitions). `LocalizationProvider` adds `TryBootstrapDyeGamedata` (reusable `TryLoadDyeBridge<T>` helper) + `HasDyeGamedata` + 3 direct accessors. Dye slot info bridge is bound for future use; v1 editor doesn't consume it yet (still blocked on `_itemKey → _partPrefabKey` cross-reference upstream). |
> | **Tools → Edit Item Dyes…** | Master dialog (`DyeEditorWindow` + `DyeEditorViewModel`) walks every `InventorySaveData` block via `list_inventory_items`, finds items whose `_itemDyeDataList` is present + non-empty, lists them one row per dyed item with icon / bag / item name / slot count / Edit button. Per-row Edit opens the child slot editor (`DyeSlotEditorWindow` + `DyeSlotEditorViewModel`) which lists every slot in that item's dye list with NumericUpDown editors for R/G/B/A (0–255) + grime (−128..127) + ComboBox dropdowns for Material (palette tier) + Color group. Per-slot Apply writes only the modified-from-original scalars; promotes absent fields to present via `SetScalarFieldPresent` when needed. Edits propagate dirty back through child → master → MainWindow on close. |
>
> v1 scope is **edit-existing-dye only** — adding dye to a previously-undyed item is deferred until the upstream `set_object_list_present` ABI lands (per `vendor/crimson-rs/docs/dye-editor-scope.md`). The 3 schema corrections from the upstream survey are honoured: `_dyeSlotNo` is signed `int8`, `_texturePalleteKey` is fixed `u16`, `_disableSymbol` is the 9th field (PyQt5 RE missed it).
>
> Tests: **180/180 pass** (was 177; +3 covering the three dye bridges' live-install load + lookup + bounds + miss paths). Debug build clean. Rebuilt `crimson_rs.dll` to expose the new exports.
>
> ## ✅ This session — what shipped (2026-05-15 part 8)
>
> Three focused commits — one UX polish on the previous-session
> dialog, two halves of roadmap pick #6 (Abyss Gates).
>
> | Area | Scope |
> |---|---|
> | **Find Items — per-row Go button** | The Find Items dialog's action column gained a "Go" button alongside the K/N copy buttons. Clicking it rebuilds the main window's nav stack down through `InventorySaveData → ElementsFrame(_inventorylist) → BlockFrame(container) → ElementsFrame(_itemList) → BlockFrame(item)` so the user lands directly on the item-detail view with a clean Back trail. `MainWindowViewModel.NavigateToInventoryItemAsync` drives the load + drill in one async; a new `_suppressBlockSelectionLoad` flag prevents the default OnSelectedBlockChanged worker from racing against the deeper stack. Find Items stays open after the jump so the user can inspect several items in sequence. |
> | **Tools → Unlock All Abyss Gates (Map Discovery)** | Roadmap pick #6 (bulk half). Harvests every `AbyssGate_*` / `Knowledge_AbyssRuins_HyperSpace*` / `Knowledge_LevelGimmickIcon_AbyssGate*` / `Knowledge_LevelGimmickIcon_HyperSpace*` key from `knowledgeinfo.pabgb` live (no JSON pack vendored), unions with the user's current `KnowledgeSaveData._list`, writes back via the existing `DynamicArraySetU32Elements`. Touches only the discovery-flag layer (gates show on map); confirm dialog calls out the separation from the gate-state layer below. Pre-flight scan reports "harvested N, M already present, N−M to add" so the user sees real numbers. |
> | **Tools → Edit Abyss Gates (Lock/Unlock per gate)** | The gate-state layer. New dialog (`AbyssGatesWindow` + `AbyssGatesViewModel`) walks every top-level `FieldGimmickSaveData` block, cross-references each `_gimmickInfoKey` against the abyss/hyperspace allowlist harvested from `gimmickinfo.pabgb`, and surfaces a per-row Lock/Unlock toggle that flips `_initStateNameHash` between `0x866c7489` (Default Untouched / locked) and `0xe300acfe` (Activated Crossed / unlocked). Idle (`0x150b14d0`) and Unknown rows stay read-only — flipping scenery hashes might destabilise non-gate gimmicks. Per upstream's `vendor/crimson-rs/docs/abyss-gate-map.md` survey, those three hashes cover the entire 356-gate sample. Rows grouped by `_ownerLevelName` (e.g. "AbyssBridge_0001_Phase00_00"). **v1 limitation**: walks top-level blocks only; nested `FieldGimmickSaveData` elements inside container ObjectLists aren't surfaced yet. |
>
> Infrastructure added in support: `NativeKnowledgeInfoCatalog.GetEntry` + `NativeGimmickInfoCatalog.GetEntry` (C# wrappers around already-shipped Rust `*_get_entry` C ABI); `LocalizationProvider.KnowledgeCount` + `GimmickCount` + `EnumerateKnowledgeByNamePrefix` + `EnumerateGimmicksByNameContains` enumeration helpers; `FindFirstBlockByClassName` private static helper on `MainWindowViewModel` for the singleton-block lookup pattern that's now used by two bulk-op flows.
>
> Tests: **177/177 still pass** (no new unit tests — the Abyss Gate flows are UI plumbing over the existing scalar/dynamic-array primitives that have their own coverage). Debug build clean.
>
> ## ✅ This session — what shipped (2026-05-15 part 7)
>
> Consumed the latest `vendor/crimson-rs` refresh's two new C ABI
> exports (`crimson_save_get_mutation_version` +
> `crimson_save_list_inventory_items`) end-to-end across three
> focused commits.
>
> | Area | Scope |
> |---|---|
> | **`mutation_version` + `list_inventory_items` bindings** | New `InventoryItemRecord` blittable struct (48-byte `repr(C)` mirror of the Rust shape). `ISaveLoader` gains `ulong GetMutationVersion()` + `IReadOnlyList<InventoryItemRecord> ListInventoryItems(out ulong version)`. The version-pairing pattern is documented per `vendor/crimson-rs/docs/save-mutation-version.md`. |
> | **`_detailsCache` refactored to use `mutation_version`** | Per-block details cache changes from `Dictionary<int, BlockDetails>` to `Dictionary<int, (ulong Version, BlockDetails Details)>`. `LoadBlockDetails` now validates cached entries against the live mutation version on every read — refetches on mismatch. `RequireLoaded` loses its `invalidateDetailsCache` flag; 4 inline `_detailsCache.Clear()` patterns in mutation entry points collapse to a single `var cached = RequireLoaded(nameof(...));` line each. Net diff −82 lines despite added behavior. Defense-in-depth: adding a future mutation entry point no longer requires remembering to flag the cache flush — the version check is ground truth. |
> | **Tools → Find Items… cross-bag dialog** | New menu item (gated on `HasSave`) opens `FindItemsWindow` bound to `FindItemsViewModel`. Drives off the new flat-list FFI: every `_inventoryList[N]._itemList[M]` slot in one snapshot, joined with PALOC-resolved item name + InventoryKey container label. Columns: Icon / Bag / Slot / ItemKey / Name (eng) / (Name secondary) / Stack / Flags (L/N) / ItemNo / Copy (K/N). Search filters by ItemKey / item name / bag label / ItemNo. Footer shows snapshot version + Refresh button (re-lists in one click after the user edits elsewhere). |
>
> Tests: **177/177 pass** (was 172; +5 covering `GetMutationVersion` starts-at-0 / no-save-throws / bumps-on-mutation, `ListInventoryItems` returns consistent non-empty records, and `BlockDetailsCache_VersionBumps_InvalidatesAutomatically` regression for the cache refactor). Debug build clean. Rebuilt `crimson_rs.dll` to expose the new exports (vendor source unchanged — `cargo build --features c_abi --release` only).
>
> ## ✅ This session — what shipped (2026-05-15 part 6)
>
> Two small UX changes plus a TODO roadmap sweep for the new bridges
> that landed in the latest `vendor/crimson-rs` refresh.
>
> | Area | Scope |
> |---|---|
> | **Rename Mercenary now shows resolved character names** | The dialog's caveat "Current saved names are NOT shown" was technically true for the user-set custom name (`_mercenaryName` InlineBytes — still needs a read-side FFI) but misleading: the **character/template name** resolved from `_characterKey` was already available via `LocalizationProvider.ResolveByFieldTypeName("CharacterKey", k)` — the same source the main window's mercenaryDataList Name column already used. Dialog now has a **Name** column between CharKey and Type displaying e.g. "Damiane / 德米安". `BuildRow` takes `LocalizationProvider`; `MercenaryRow.ResolvedCharacterName` is populated at construction. Header text updated to explain the column source vs. the still-pending user-custom-name read FFI. |
> | **Drop All Sealed Abyss Artifacts menu — removed** | Pattern B v1's per-row "Mark Challenge Complete" button is the correct fix for SA stuck-state; the artifact-clearing bulk drop was the wrong shape (operates at item layer; the engine's stuck-claim gate is at the quest layer). Deletes `Services/ChallengeBulkOpService.cs` + `Views/ChallengeBulkOpProgressDialog.axaml(.cs)` + `DropAllSealedAbyssArtifactsAsync` + `ScanArtifactBulkOpPreview` + `ArtifactBulkOpPreview` + `ArtifactBulkOpRequested` wiring. `LocalizationProvider.EnumerateItemsByStringKeyPrefix` retained — Pattern B v1's held-artifact gate still uses it. |
> | **Roadmap sweep for new crimson-rs bridges** | `vendor/crimson-rs` refresh brought a **CharacterKey dedicated bridge** (`crimson_characterinfo_load_from_*`, `_lookup_string_key`, `_lookup_display_name`, `_get_entry`, `_resolve_portrait`) and an **NPC portrait pipeline** (`crimson_paz_list_npc_portraits`). C# does not consume either yet. Added as picks #7–9 in the porting roadmap below; also closes deferred item #5's `FieldNPCSaveData._characterKey` half (the bridge does the cat-byte lo24 strip we don't currently do, plus internal-name fallback). |
>
> 170/170 C# tests pass. AOT build clean. Diff +88 / −793 (big delete is the SA bulk-op feature).
>
> ## ✅ This session — what shipped (2026-05-15 part 5)
>
> Pick #4 of the porting roadmap — **Sockets editor v1 (swap-only)**.
> The highest-value save-editor feature still missing, now shipped in
> its safest scope.
>
> | Area | Scope |
> |---|---|
> | **Tools → Edit Item Sockets…** | New menu item; opens [`SocketEditorWindow`](../src/CrimsonAtomtic.Ui/Views/SocketEditorWindow.axaml) bound to [`SocketEditorViewModel`](../src/CrimsonAtomtic.Ui/ViewModels/SocketEditorViewModel.cs). Walks every `InventorySaveData._inventorylist[*]._itemList[*]._socketSaveDataList[*]`, surfaces one row per **filled** socket (mask present + `_itemKey > 0`). Empty sockets are not shown — per the predecessor's hard caveat, embedding gems into empty sockets requires the in-game Witch NPC and forcing fills can crash. |
> | **DataGrid columns** | Bag (InventoryKey-resolved label) / Item name (PALOC-resolved) / ItemKey / Slot # / Current gem name / Current gem key / Change Gem… button / Applied gem name (post-edit). |
> | **Change Gem… flow** | Clicking the per-row button raises `SocketEditorViewModel.ChangeGemRequested`; MainWindow code-behind opens a **gem-filtered** [`ItemPickerWindow`](../src/CrimsonAtomtic.Ui/Views/ItemPickerWindow.axaml). Picker is filtered via the new `ItemPickerViewModel(localization, allowedStringKeyPrefixes)` overload to `Item_Stat_AbyssGear_*` + `Item_Skill_AbyssGear_*`. Action button relabelled to "Pick" via the new `ItemPickerViewModel.ActionButtonLabel` / `ActionButtonTooltip` init-only properties. |
> | **Apply** | On pick, `SocketEditorViewModel.ApplyGemPick(row, gemKey)` writes the 4-byte gem `_itemKey` in-place via `ISaveLoader.SetScalarField` with a 3-step path: `_inventorylist[bag] → _itemList[item] → _socketSaveDataList[socket] → _itemKey`. No length-changing edits. Mirrors the predecessor's "swap-only" practice — fill_socket_slots / clear_socket_slots / socket-count unlock are explicitly out of v1 scope per the safe-edit contract. |
>
> Gem identification: TWO string-key prefixes (`Item_Stat_AbyssGear_*` for stat-mod gems, `Item_Skill_AbyssGear_*` for skill-bestowing gems). Internally Pearl Abyss called these "AbyssGear" — only localised to "gem" in display. 100% of the predecessor save editor's curated 189-entry gem list falls under one of these two prefixes in the 1.06.01 baseline. No vendored JSON; the picker enumerates live from `iteminfo.pabgb` via the existing iteminfo bridge.
>
> Out of v1 scope (documented in the [`SocketEditorViewModel`](../src/CrimsonAtomtic.Ui/ViewModels/SocketEditorViewModel.cs) docstring):
> - **Fill empty socket** — length-changing splice; the predecessor exposes the Python function but the UI route is "swap only" because empty sockets need the in-game Witch NPC.
> - **Clear filled socket** — length-changing splice with sibling-offset cascading.
> - **Socket-count unlock** — triple coupled write (`_maxSocketCount` + `_validSocketCount` + `_endurance` high byte). The predecessor explicitly warns "0 → positive on a zero-record list may crash".
>
> Tests: **170/170 pass** (no new tests for v1 — the FFI surface is `SetScalarField` which is already covered by `SetScalarField_NestedPath_RoundTripsThroughWriteToFile`; v1's net code is UI plumbing over existing primitives). AOT build clean.
>
> ## ✅ This session — what shipped (2026-05-15 part 4)
>
> Pick #3 of the porting roadmap — **Rename Mercenary** (the "Pet rename"
> feature from the predecessor save editor; pets live as mercenary
> entries in this game's save model). Required a new Rust FFI entry
> point and the C# side around it.
>
> Equipment-set duplicator dropped from this iteration — see the
> survey notes below; the predecessor's "duplicator" is actually a
> stack-count exploit rather than a literal duplication, and our
> single-loadout `EquipmentSaveData` shape doesn't map cleanly to
> "duplicate set A to set B". Revisit if/when users actually ask.
>
> | Area | Scope |
> |---|---|
> | **`crimson_save_set_inline_bytes_field` (crimson-rs [PR #37](https://github.com/bbfox0703/crimson-rs/pull/37), merged at `d82e780`)** | New C ABI entry point for editing `meta_kind=1` (InlineBytes) fields, which the existing `set_scalar_field_present` rejects (it only handles fixed-size scalars at `meta_kind` 0/2). Mirrors that function's structure: `navigate_mut_to_parent` → validate kind == 1 → flip mask presence bit → overwrite `FieldValue::InlineBytes { count, bytes }` → run the standard `apply_length_changing_mutation` re-emit pipeline. Adds error code `NOT_INLINE_BYTES = -20`. Validates `new_bytes.len() % meta_size == 0` and rejects with `LENGTH_MISMATCH` otherwise. 3 new tests cover round-trip, scalar rejection, and NULL-arg handling. Live-save round-trip is byte-identical when rewriting bytes back to the original. |
> | **C# wrapper** | `ISaveLoader.SetInlineBytesField(int blockIdx, ReadOnlySpan<PathStep> path, int fieldIndex, ReadOnlySpan<byte> newBytes)` + `NativeSaveLoader` impl + `[LibraryImport]` for `crimson_save_set_inline_bytes_field`. New error name `NOT_INLINE_BYTES`. |
> | **Tools → Rename Mercenary…** | New dialog window (`RenameMercenaryWindow` + `RenameMercenaryViewModel`) bound to a DataGrid over `MercenaryClanSaveData._mercenaryDataList`. Columns: Index / MercNo / CharKey / Type (Animal vs Mercenary based on equip count) / Equip / New name (editable TextBox) / Apply / Applied. Apply UTF-8-encodes the textbox value and calls `SetInlineBytesField`. Dialog refuses to open when the save has no `MercenaryClanSaveData` block (alerts with guidance instead). |
> | **v1 caveat — current names not shown** | The FFI has a setter but no symmetric getter for `inline_bytes`, so the dialog can't display each mercenary's existing name. Users identify rows by `(MercNo, CharacterKey, EquipCount)` and the inferred type tag. Header text in the dialog calls this out. A follow-on `crimson_save_get_inline_bytes_field` is the natural next iteration. |
>
> Tests: **170/170 pass** on C#; **154/154 pass** on crimson-rs (was 151, +3 from the new FFI). AOT build clean.
>
> ### Process notes
>
> - crimson-rs dev had drifted from main again (5 patch-id-equivalent commits with different SHAs after main got rebased). Fixed in-flight by rebasing dev, force-pushing, then merging the PR. After merge, local + origin/dev force-reset to match main per policy.
> - One Rust round-trip required (PR → CI → merge → vendor refresh). Followed the documented "edit source at D:\Github\crimson-rs first, then `vendor/update_vendors.ps1`" pattern.
>
> ## ✅ This session — what shipped (2026-05-15 part 3)
>
> Game-install auto-detection generalised + user override. Follows
> directly from the save-discovery work earlier in the session.
>
> | Area | Scope |
> |---|---|
> | **Steam libraryfolders.vdf parser** ([SteamLibraryProbe.cs](../src/CrimsonAtomtic.Ui/Platform/SteamLibraryProbe.cs)) | Replaces the previous 5-path hardcoded probe with a real walk of every Steam library the user has mounted. Reads `<Steam>\config\libraryfolders.vdf` from the two standard Steam install locations, extracts every `"path"` value with a single regex, unescapes `\\` → `\`, and walks each library's `steamapps\common\Crimson Desert` looking for the `0020\0.pamt` witness file. AOT-safe via `[GeneratedRegex]`. |
> | **Epic manifest probe** ([EpicManifestProbe.cs](../src/CrimsonAtomtic.Ui/Platform/EpicManifestProbe.cs)) | Enumerates `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item`, deserialises each via a source-generated `JsonSerializerContext`, matches `DisplayName` containing "Crimson Desert" or `AppName` containing "CrimsonDesert" (case-insensitive), then validates the manifest's `InstallLocation` against the same witness file. Returns null silently when Epic isn't installed. |
> | **Game Pass install probe — DEFERRED** | `%PROGRAMFILES%\WindowsApps\` is ACL-locked even for admins. Even if we detected a Game Pass install, we couldn't read the PAMT/PALOC out of it. Game Pass users use the manual-override path below to point at an asset folder they manually extracted. |
> | **`game_install_root` settings field** ([AppSettings.cs](../src/CrimsonAtomtic.Ui/Services/AppSettings.cs)) | Optional user override. Wins over every auto-probe. Validated against the witness file before being persisted, so an accidental pick can't poison the setting. |
> | **`WindowsPlatformPaths.GameInstallRoot` rewrite** | Probe chain: stored override → Steam VDF → Epic manifest → null. Silent degrade unchanged — `LocalizationProvider.TryBootstrapFromGameRoot(null)` is still the no-install path. |
> | **Tools → Set Game Install Folder…** | Avalonia folder picker anchored at the currently-resolved game root when one exists. On valid pick: persists to `game_install_root` + immediately re-bootstraps `LocalizationProvider` against the new path (catalogs reload, status footer refreshes, open save's resolved-name columns repaint). On invalid pick: alert dialog with guidance, settings untouched. Covers Game Pass + unusual Steam library layouts + asset folders copied out for any reason. |
>
> Tests: **170/170 pass** (was 157; +13 covering VDF parser happy path, empty / no-libraries VDF, `\\` unescape, witness-file validation positive / negative / partial, Epic manifest matching by DisplayName / case-insensitive / AppName fallback / unrelated game / empty entry).
>
> ### Why no auto-prompt on probe failure
>
> Discussed and rejected — silent degrade matches existing behaviour
> (the LocalizationProvider already degrades when no install is found).
> Tools menu is the discoverable escape hatch; status footer surfaces the
> "no install" state via existing `LocalizationStatus` / `IconStatus`.
> Adding a startup prompt would mean tracking a "user has dismissed"
> bit and risk being annoying on every fresh launch.
>
> ## ✅ This session — what shipped (2026-05-15 part 2)
>
> Pick #1 of the porting roadmap — **Auto-find saves on launch** — plus the
> mandatory backup/restore migration that the multi-launcher schema forced.
>
> | Area | Scope |
> |---|---|
> | **`IPlatformPaths` multi-platform API** | `GameSaveRoot` (single string) replaced by `DiscoverSaveRoots()` (returns `IReadOnlyList<DiscoveredSaveRoot>` of `{Platform, RootPath, MostRecentSaveMtime}`, most-recent-first) + `ClassifySavePath(path) → SavePlatform`. New `SavePlatform` enum in Core: `Steam` / `Epic` / `GamePass` / `Unknown`. |
> | **Windows probe** | `WindowsPlatformPaths.DiscoverSaveRoots()` walks the three known Pearl-Abyss save trees under `%LOCALAPPDATA%\Pearl Abyss\`: `CD\save` (Steam), `CD_Epic\save` (Epic), `CD_GamePass\save` (Game Pass plain fallback). For each existing root, scans every `save.save` once at discovery time to derive `MostRecentSaveMtime`. The real UWP wgs container at `%LOCALAPPDATA%\Packages\PearlAbyss.CrimsonDesert*\SystemAppData\wgs\` is out of scope for v1 (sync-locked + GUID-named binary blobs, deferred). |
> | **`DefaultOpenSaveStartingPath` rewrite** | Selection rule: (1) honour `AppSettings.PreferredPlatform` when it still exists on disk, (2) fall back to the most-recently-modified platform, (3) static Steam path when no platform is detected. After picking the platform root, the existing single-user auto-drill applies. |
> | **`preferred_platform` settings field** | New string field in `AppSettings` (`"Steam"` / `"Epic"` / `"GamePass"` / null). Persisted automatically every time the user opens a save successfully — `LoadSave` calls `paths.ClassifySavePath(path)` and writes the result to settings. Browse-opening a save from an unusual location yields `Unknown` and the field is NOT updated, so the sticky preference is never anchored on a one-off. |
> | **`SaveBackupService` platform-scoped layout** | New path schema: `Backups\<platform>\<userId>\<slot>\<timestamp>\` (was: `Backups\<userId>\<slot>\<timestamp>\`). `BackupEntry` gained a `Platform` field; `BackupBeforeWrite` derives platform via `_paths.ClassifySavePath` at write time. `PruneBackups` takes `SavePlatform` and only prunes within its own platform's tree. |
> | **Legacy backup migration** | `ListBackups` walks BOTH layouts: directories under `BackupRoot` whose name matches a `SavePlatform` enum value are descended as the new layout; everything else (legacy un-platformed `<userId>\` directly under `BackupRoot`) is surfaced tagged `SavePlatform.Steam` — the only launcher supported before this change. Legacy entries are never re-prune-targeted; they age out on their original cycle and don't accumulate. |
> | **Restore picks the right platform root** | `MainWindowViewModel.RestoreFromBackupAsync` now calls `ResolveSaveRootForBackup(entry)` which probes `DiscoverSaveRoots()` for a root matching the entry's `Platform`. When the launcher that wrote the original save is no longer installed (user switched platforms), the dialog surfaces an explicit error instead of silently writing to the wrong tree. Legacy `Unknown`-platform entries fall back to the first available save root. |
>
> Tests: **157/157 pass** (was 151; +6 covering platform-scoped backup placement, Epic-tagged backups, Unknown-platform fallback, mixed-layout `ListBackups`, legacy-layout migration, multi-platform discovery ordering by mtime). Build clean.
>
> ### Next picks (still on the roadmap)
>
> #2 Item Pack import, #3 Pet rename + Equipment-set duplicator, #4 Sockets editor, #5 Dye editor, #6 Unlock All Abyss Gates. See the "Pick up here" section below for the full table.
>
> Last updated (prior milestone): 2026-05-15 (Sealed Abyss Artifact bulk drop + Pattern B v1 single-challenge mark complete).
>
> ## ✅ This session — what shipped (2026-05-15)
>
> Two related Sealed Abyss Artifact (SA) tooling changes, after a multi-iteration debug
> that taught us a lot about the engine's completion bookkeeping. Pinned at
> CrimsonAtomtic `b7eed65` + crimson-rs `7a800e4`.
>
> | Area | Scope |
> |---|---|
> | **Tools → Drop All Sealed Abyss Artifacts From Inventory…** | Single confirm → walks every `InventorySaveData`, drops every item whose iteminfo `string_key` starts with `Sealed_Abyss_Artifact` (12 known item key variants in slot102). Single FFI batch via `crimson_save_list_remove_elements_batch`. Sub-second on a 1100-block save. Clean and useful by itself for clearing stuck artifact items. |
> | **Per-row "✓ Mark Challenge Complete (Pattern B v1)" button** | Appears in the field-detail panel when the current frame is a catalog `MissionStateData` row whose corresponding "FAR tracker" exists AND the user currently holds at least one Sealed Abyss Artifact item in inventory. Writes the engine-natural pre-claim completion state. **Verified to work for slot102 Shield II + Spear I + Hooves II + Slash III** (4 challenges across 3 series). Category restriction (Challenge_* / Mission_MiniGame_* prefixes only) has been removed — the user takes responsibility for matching non-standard categories to the FAR-tracker shape. The held-artifact gate replaces it as the safety net. |
> | **Bulk challenge-flipping feature removed** | Earlier sessions tried bulk catalog flips of `_state=5 + _completedTime` (Pattern A v1/v2/v3); all three corrupted the in-game UI by hiding previously-visible challenge cards. Removed entirely — single-row Pattern B v1 is the only safe path until we understand more. |
> | **Per-row challenge-completion warning guard removed** | The pre-existing prompt on bare `_state ← 5` / `_completedTime` edits is gone — those are now expert-mode raw edits. The Mark Challenge Complete button carries its own (long) warning, and the bulk flow that needed the guard is gone. |
>
> Three new upstream crimson-rs C ABI entry points landed and shipped to vendor:
>
> | crimson-rs entry point | What it does |
> |---|---|
> | `crimson_save_dynamic_array_set_u32_elements` | Wholesale-replace a `dynamic_array<u32>` field's contents; auto-updates the variant header's count slot (4 known variants supported). |
> | `crimson_save_dynamic_array_get_u32_elements` | Two-call buffer-pattern reader for `dynamic_array<u32>` contents (paired with the setter). |
> | `crimson_iteminfo_lookup_look_detail_mission_info` | Per-item lookup: `ItemKey → MissionKey` from the iteminfo `look_detail_mission_info` field. Quest-reward items (specifically the SA series) point at the catalog mission key of the challenge that rewards them. |
>
> ### Engine-natural completion shape — fully decoded
>
> Sealed Abyss Artifact challenges have a **catalog row + adjacent visibility twin + FAR tracker + X_2 follow-up sub-mission** four-piece structure inside `QuestSaveData._missionStateList`. Verified via slot102 → engine-natural slot103 (Hooves II completion) field diff:
>
> | Piece | Shape | When written by engine |
> |---|---|---|
> | **Catalog row** (positive key, e.g. 1000898 Hooves II) at idx N | minimal — `state=2, uiState=1, newAlarm=1, _usedTagList=[base]` | Save creation |
> | **Adjacent visibility twin** (negative key, e.g. 4294966810) at idx N+1 | `state=2, _usedTagList=[base]` initially. On artifact pickup → `state=5 + _completedTime + _usedTagList=[base, visible]` | Artifact pickup |
> | **FAR tracker** (negative key = `adjacent_twin._key - 1`) at idx 3600-3900 range | Only created on artifact pickup. `state=2, _branchedTime=present, _usedTagList=[base]` initially. On engine-natural completion → `state=5 + _completedTime + _usedTagList=[base, visible]` | Artifact pickup → engine-natural completion |
> | **X_2 follow-up** ("Use the sealed Abyss artifact", e.g. key 1003337 = `Vehicle_II_2`) appended at end of list | `state=2, _branchedTime=present, _usedTagList=[base]` | Engine-natural completion |
> | **alertHistorySaveData entry** in `ContentsMiscSaveData._alertHistorySaveDataList` | Pattern A1 (Shield III): `alertType=3, _missionKey=catalog_key`. Pattern A2 (Hooves II): `alertType=30, _missionKey=adjacent_twin_key` | Reward CLAIM (using the artifact item) |
> | **Catalog row + adjacent twin → completed state** | catalog `state=5 + _completedTime + tags=[base, visible, completed]`; twin `_usedTagList` adds `visible` if not already there | Reward CLAIM |
>
> **Pattern B v1** (the recipe we ship) writes only what the engine writes pre-claim: FAR tracker flip + X_2 sub-mission insert. Catalog + alertHistory get filled in naturally when the user reloads + claims the reward in-game.
>
> ### Universal magic StringInfoKey hashes for `_usedTagList` (verified across all 12 solved SA challenges in slot102)
>
> ```
> base_tag        = 2267378118  (every MissionStateData starts with this)
> visible_tag     = 3938836851  (added when discovered)
> completed_tag   = 4104166156  (added on catalog when solved — engine writes this, NOT us)
> ```
>
> ### Why Pattern A v1/v2/v3 all failed (record for the next session)
>
> Earlier patterns tried to write the post-claim catalog state directly. Tested on slot102 → slot103 (manual single flip) and slot104 (batch flip) and slot106 (full v3 with alertHistory + tags): **every save regressed previously-visible challenge cards into the "unknown / locked" UI state**. Likely the engine cross-references the catalog flip against runtime state we can't observe directly, and refuses to display when it sees an inconsistency. Pattern B v1 sidesteps the problem by writing only the engine's own pre-claim shape.
>
> ### Where this leaves the SA bulk-completion idea
>
> Bulk completion of all SA challenges in one menu click is **off the table** for now. The only safe per-challenge recipe (Pattern B v1) requires the user to have picked up the matching artifact item in-game first — which is the exact gating signal the engine itself uses. There's no way to bypass that without re-creating engine state we can't reliably reproduce (the FAR tracker key is engine-assigned). Per-row clicks are the path forward; bulk would just batch the same per-row recipe over every eligible row.
>
> ## ✅ Beer Add-to-bag works end-to-end
>
> The per-item field-population recipe from the previous session
> shipped; the user confirmed the game loads + accepts the cloned beer
> and it appears in their backpack. The recipe is the existing
> `b393b03..b0a015a` patch series. No further investigation needed on
> the basic Add-to-bag flow.
>
> ## ✅ RESOLVED — Sealed Abyss Artifact "claim reward" trigger (via Pattern B v1)
>
> **The recipe in one line:** don't touch the artifact item, don't
> touch the related quest's catalog row, don't touch the adjacent
> negative-key visibility-twin quest row — only click the per-row
> "✓ Mark Challenge Complete (Pattern B v1)" button, which flips the
> FAR tracker and inserts the X_2 follow-up sub-mission. On reload +
> in-game claim, the engine fills in catalog + visibility twin +
> alertHistory + the completed tag naturally.
>
> | 4-piece structure | Touch? |
> |---|---|
> | Artifact ITEM (`_inventorylist[*]._itemList[*]`, the SA artifact) | **NO** |
> | Catalog row (positive key, e.g. 1000898 Hooves II) | **NO** |
> | Adjacent visibility twin (negative key, e.g. 4294966810) | **NO** |
> | FAR tracker (negative key = adjacent_twin._key − 1) | **YES** (button) |
> | X_2 follow-up sub-mission (positive key, appended at list end) | **YES** (button) |
>
> The original `_chargedUseableCount: 0 → 15` edit on the artifact
> item was the wrong layer — the engine's "ready to claim" gate is
> keyed on `QuestSaveData._missionStateList` progression bookkeeping,
> not on the artifact item itself. So "byte-perfect encoder but still
> not claimable" was always correct on the encoder; the recipe was
> aimed at the wrong layer. Verified end-to-end on slot102 against
> Shield II, Spear I, Hooves II, Slash III. Historical deep-dive
> kept below at [Sealed Abyss Artifact investigation
> (HISTORICAL)](#sealed-abyss-artifact-investigation-historical--superseded-by-2026-05-15).
>
> ## ✅ This session — what shipped (2026-05-14 part 2)
>
> | Area | Scope |
> |---|---|
> | **Nav perf** | `NativeSaveLoader` gained a `Dictionary<int, BlockDetails>` cache. Same-block re-clicks are O(1) (was 1–2 s for QuestSaveData/4341 missions). Cache invalidated on every body mutation, on `Load(path)` swap, on `Dispose`; preserved across `WriteToFile`. Plus `OnSelectedBlockChanged` offloaded to `Task.Run` with a race guard — first-click no longer freezes the window. 4 new live-save cache tests. |
> | **Absent ↔ present UI** | Edit panel now lights up for absent scalar rows (gated on `MetaKind in {0,2}` + `ScalarFieldEditing.TryInferTypeTagFromSchema`). Apply routes through `SetScalarFieldPresent(makePresent: true, …)` for absent-source rows, normal `SetScalarField` otherwise. New **"Make absent"** button flips a present scalar to absent via `SetScalarFieldPresent(makePresent: false, …)`. `FieldRowViewModel.Present` is now `ObservableProperty` so the Present column refreshes live after the toggle. Type hint shows `"u32 (absent — Apply makes present)"` when applicable. 28 new InferTypeTag tests. |
> | **Challenge completion guard** | `CommitFieldEdit` is async (`CommitFieldEditAsync`). When editing a `MissionStateData` field to mark completion (`_state ← 5` or `_completedTime` promoting to present), a confirm dialog gates the FFI write. First call per save path shows combined Sealed Abyss Artifact + achievement-impact warning; subsequent calls on the same save show only the artifact reminder. Tracking field: `_challengeWarningAcknowledgedForPath`. Loading a different save naturally re-arms the first-time warning. |
> | **Icon cache fixed location** | Removed `AppSettings.IconCacheDirectory` + Tools menu's "Set Icon Folder…" + the folder-picker handler. Cache now lives at `%LOCALAPPDATA%\CrimsonAtomtic\IconCache\` unconditionally (mirrors `SaveBackupService.BackupsSubdirectory` pattern). `IconProvider` takes a single root path, creates the dir on construction, exposes `ResolveRoot(localAppData)` static helper. `MainWindowViewModel.RefreshIconCache()` replaces `SetIconCacheDirectory(path)`. |
> | **Backup retention 3 → 6** | `SaveBackupService.MaxVersionsPerSlot = 6` (was 3). Reason: Restore from backup itself triggers another pre-write snapshot, so 5 normal edits + 1 restore already consume 6 slots. Test renamed `FourthVersion → OverRetention`, drives the loop off `MaxVersionsPerSlot + 1` so future cap tweaks don't desync. |
> | **Add-to-bag container blocklist — added then removed** | Mid-session: blocked `+ Bag` into Quest Artifacts (InventoryKey 5) and Kuku Pot (13) with an alert dialog. **User reverted the policy**: protection off, user accepts consequences. The `AlertRequested` plumbing + `ConfirmDialog.ShowAlertAsync` infrastructure stays (used elsewhere). |
>
> Health: **151 / 151 C# tests pass** (was 119; +4 cache tests + 28 InferTypeTag tests). AOT publish clean at 24.3 MB exe + 1.4 MB `crimson_rs.dll`. No new Rust changes this session; `vendor/crimson-rs` still pinned at `ab815e6`.
>
> ## 🆕 Verified facts to remember
>
> - **Schema version drift sinks naive diffs.** Two saves can have
>   *different* `ItemSaveData` schemas if they come from different
>   game versions. Today's red herring: slot104 (2026-04-29, chapter II)
>   is a 1.05-era save with a **23-field** ItemSaveData; slot103
>   (2026-05-14, chapter VIII) is 1.06 with **25 fields**. The two
>   extra 1.06 fields are `_maxChargeUseableCount` (uint32) and
>   `_coolTimePerCharge` (TickCount64). **Always check `type_count`
>   and `schema_end` from `parse_save_body_from_bytes` before reading
>   a cross-save diff as logical state difference.** slot102 / slot103
>   are the right minimal pair (both 1.06, 9 minutes apart).
> - **`_transferredItemKey` formula is universal** for Quest Artifacts:
>   `((itemKey & 0xFFFF) << 16) | 0x0101`. 543/546 items in slot103
>   inv[4] match; the 3 outliers all sit in inv[1] (Camp &
>   Contributions) — different encoding for legacy non-stackable
>   currency-style items. Earlier in this session I miscomputed
>   `1002011 & 0xFFFF` as `0xF49B` (decimal-to-hex error); the real
>   answer is `0x4A1B`, the formula matches perfectly. Don't relearn.
> - **`_useItemSaveList` is the equip / quick-use bar, NOT a Sealed
>   Abyss Artifact shortcut.** The 8→9 element change between slot102
>   and slot103 references item `1002130` (`Kuku_Pot_BackPack`), not
>   `1002011` (`Sealed_Abyss_Artifact_0083`). The artifact has no
>   external pointer anywhere in the body — it lives only at
>   `_inventorylist[4]._itemList[37]._itemKey`. Don't chase a shortcut
>   theory.
> - **Our encoder is byte-perfect, modulo expected absolute offsets.**
>   `SetScalarFieldPresent(false) + SetScalarField` on the artifact
>   produces output that's structurally identical to the engine's
>   natural completion. The 6 byte diffs we see in slot102-edited vs
>   slot103 are all `payload_offset` u32/u64 values that match the
>   global +512 byte shift between the two saves (artifact lives at
>   a different absolute body position). PR B.6's `payload_offset`
>   canonicalization is correct. Verified with a ctypes-driven test —
>   see `out/diff-slot102-103/` if you want the raw bytes.
>
> ## ✅ PR B — Length-changing edits — fully shipped (Rust + C#)
>
> Vendor pin: `vendor/crimson-rs` at `ab815e6`. Eight upstream PRs and
> two C# feature commits delivered an end-to-end pipeline that can
> grow / shrink / re-shape decoded `ObjectBlock`s and re-emit the body:
>
> | Phase | Upstream PR | Commit | Scope |
> |---|---|---|---|
> | B.1 | [#29](https://github.com/bbfox0703/crimson-rs/pull/29) | `43242f0` | `ObjectBlock` re-serializer + body re-emit + round-trip golden tests |
> | B.2 | [#30](https://github.com/bbfox0703/crimson-rs/pull/30) | `1ba3051` | `list_remove_element` + `list_clone_element` + `set_scalar_field_present` C ABI |
> | B.3 | [#31](https://github.com/bbfox0703/crimson-rs/pull/31) | `f6dac92` | `make_empty_element_bytes` + `list_insert_element` (schema-aware) |
> | B.6 | [#32](https://github.com/bbfox0703/crimson-rs/pull/32) | `ab815e6` | `payload_offset` canonicalization — the encoder rewrites every locator wrapper's `payload_offset` u32 to wrapper_end_body_offset, since the engine writes stale values that go dangling after a clone-insert |
>
> | Phase | CrimsonAtomtic commit | Scope |
> |---|---|---|
> | B.4 | `5eb94cc` | C# interop layer (5 new `ISaveLoader` methods, 9 new tests); "Remove" button on elements DataGrid; "+ Bag" on Item Picker (cross-window event pipe) |
> | B.5 | `dc52904` | `SaveBackupService` + RestoreFromBackup dialog. Auto-snapshot before every Save / Save As to `%LOCALAPPDATA%\CrimsonAtomtic\Backups\<userId>\<slot>\<timestamp>\` (save.save + lobby.save together). 3-version rolling retention. File → Restore from Backup… picker (15 new unit tests on the service) |
> | B.4.fixup | `b393b03..b0a015a` (5 commits) | Per-field Add-to-bag refinements; see [historical Add-to-bag investigation](#historical-add-to-bag-investigation--resolved) below |
>
> ## Health
>
> - **119 / 119 C# tests pass** (was 95; +9 for the new interop entry
>   points in B.4.1, +15 for `SaveBackupServiceTests` in B.5).
> - **69 lib / 146 c_abi Rust tests pass.** Clippy clean both modes.
> - AOT publish clean: 24.3 MB exe + 1.4 MB `crimson_rs.dll`.
>
> ## Round-trip invariants — updated (don't relearn these)
>
> The encoder canonicalizes `payload_offset` in every locator wrapper
> (B.6). This **intentionally breaks byte-identity** against the raw
> engine bytes, because the engine writes a few items' `payload_offset`
> with non-canonical values (decoder always fell back to wrapper_end —
> see `body/decoder.rs`'s comment near
> `decode_inline_object_locator`).
>
> Updated invariants:
> - `test_save_body_block_roundtrip_per_block` now only asserts
>   `encoded.len() == raw.len()`. Per-block byte identity isn't a
>   property we need.
> - `test_save_body_full_roundtrip` now asserts **idempotence**:
>   `Body::write(canonical, parse(canonical)) == canonical`. The first
>   write may differ from the raw save (canonicalization); subsequent
>   writes are stable.
> - C# `ListCloneElement_ThenRemove_RoundTripsThroughLoad` still passes
>   because both the pre- and post-mutation files go through the same
>   canonicalizing encoder.
>
> ## Previous milestones (kept for reference, no action needed)
>
> All 9 save-editor key resolver bridges (mission/quest/stage/knowledge
> /quest_gauge/skill + checksum, plus gimmick + sub_level) shipped in
> earlier sessions (`cea8300` and before). Coverage: StageKey 99.7%,
> KnowledgeKey 100%, MissionKey 70.1% (residual 1,299 are 0xFF
> save-internal sentinels). Issues #1 (mojibake) and #2 (coverage
> gap) closed upstream. IGN challenge match rate 76.6% → 90.8%.
>
> Icon-extraction pipeline complete (Phases 1–3); Tools → Extract
> Icons from Game Data… works against a live 1.06 install.
>
> Batch scalar mutation (`SetScalarFieldsBatch`) lands per-FFI-call
> performance for "Fill stacks" UX. UI polish — collapsible save
> summary, font-size submenu, AppSettings preservation across edits.

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

## Pick up here (historical snapshot — see top of file for current state)

> ⚠ **This section is the 2026-05-15 handoff snapshot.** Read the
> `2026-05-17` entry at the top of this file for the **current**
> open follow-ons. Both this preamble and the roadmap table below
> have been retroactively struck-through where features shipped after
> the snapshot was taken; everything checked off as ✅ has matching
> commits in the dev/main history.

Pattern B v1 was originally verified end-to-end on slot102 against
four challenges across three series: **Shield II, Spear I, Hooves II,
and Slash III**. The recipe writes the pre-claim FAR-tracker shape;
the in-game UI shows the challenge as "completed, reward not claimed"
after reload, the user can pick up the artifact reward, and the
catalog row + alertHistory fill in naturally. **Category restriction
removed** — any MissionStateData row with the right FAR-tracker shape
is eligible (user takes responsibility for the FAR-shape match).
~~Inventory safety gate added — the button only enables when at least
one Sealed Abyss Artifact item is currently in the user's inventory…~~
**[2026-05-17: held-inventory gate REMOVED]** — eligibility is now
purely save-side data shape (catalog + adjacent twin state=5 + FAR
tracker present). Bulk variant shipped with per-key skip-reason
diagnostics (see top entry). Multi-objective Living_*/Cooking shapes
remain out-of-scope; await a future Pattern B v2.

~~**Next session — pick by appetite from the porting roadmap below.**~~
**[2026-05-17: roadmap exhausted]** — items #1 / #3 / #4 / #5 / #6 /
#7 / #8 / #9 shipped; #2 (Item Pack import) deferred by user choice.
The original 5-pick survey against the predecessor save editor
(`D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`, mined once for
ideas per CLAUDE.md rule 11) is fully consumed. For the current
open-task list see the top of this file.

### Porting roadmap — 5 picks (EASY → MEDIUM) (historical, all 9 items resolved)

| # | Feature | Difficulty | Notes |
|---|---|---|---|
| 1 | ~~**Auto-find saves on launch**~~ | ~~EASY~~ | ✅ **Shipped 2026-05-15 part 2.** Steam / Epic / Game Pass plain-folder probe + most-recent-mtime preference + `preferred_platform` settings persistence + platform-scoped backup tree with legacy migration. Game Pass wgs UWP container deferred. Linux Proton prefix detection (appid `3321460`) deferred. |
| 2 | ~~**Item Pack import**~~ | DEFERRED | Decided not to ship for now — user can already use the Item Picker + Add-to-bag for individual items, and curating safe pack JSONs against the 1.06 schema is more upfront work than the convenience gain warrants. Revisit if/when there's demand for batch-imported gear loadouts. |
| 3 | ~~**Pet rename + Equipment-set duplicator**~~ | ~~EASY~~ | ✅ **Pet rename shipped 2026-05-15 part 4** (as Rename Mercenary; the predecessor's "Pet rename" is mercenary rename in this game's save model). Required a new `set_inline_bytes_field` Rust FFI because `_mercenaryName` is `meta_kind=1` which the existing scalar setters reject. Equipment-set duplicator dropped — the predecessor's "duplicator" is a stack-count exploit, not a true duplication, and our `EquipmentSaveData` shape has no multi-loadout slot to duplicate into. |
| 4 | ~~**Sockets editor (fill / clear / swap gems, up to 5 sockets/item)**~~ | ~~MEDIUM~~ | ✅ **v1 (swap-only) shipped 2026-05-15 part 5.** Tools → Edit Item Sockets… surfaces every filled socket across inventory; per-row Change Gem… opens a gem-filtered Item Picker (`Item_Stat_AbyssGear_*` / `Item_Skill_AbyssGear_*`) → writes new gem `_itemKey` via `SetScalarField`. Fill/clear/unlock deferred per predecessor's safe-edit contract (empty-socket fill needs in-game Witch NPC; socket-count unlock has triple-coupled-write risk). |
| 5 | ~~**Dye editor (RGB / material / grime)**~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-05-16 part 9.** Master `Tools → Edit Item Dyes…` lists every dyed item; per-row Edit opens a child slot editor (R/G/B/A NumericUpDowns + grime + material/color-group dropdowns + per-slot Apply). v1 scope = edit-existing-dye only; add-dye-to-undyed-item deferred until upstream `set_object_list_present` ABI ships. All three `dye*.pabgb` gamedata bridges from the 2026-05-16 vendor refresh bound + integrated into `LocalizationProvider` (replaces the PyQt5 reference editor's `dye_slot_counts.json`). |
| 6 | ~~**Unlock All Abyss Gates (Knowledge bulk-append)**~~ | ~~EASY~~ | ✅ **Shipped 2026-05-15 part 8** as TWO Tools menu items: (a) **Unlock All Abyss Gates (Map Discovery)** — bulk knowledge inject covering the discovery-flag layer (`KnowledgeSaveData._list`), keyset harvested live from `knowledgeinfo.pabgb` by prefix match (no JSON pack vendored); (b) **Edit Abyss Gates (Lock/Unlock per gate)** — per-gate dialog for the gate-state layer (`FieldGimmickSaveData._initStateNameHash`, three known constants from `vendor/crimson-rs/docs/abyss-gate-map.md`). v1 limitation on the per-gate dialog: walks top-level FieldGimmickSaveData only, nested blocks deferred. |
| 7 | ~~**Wire CharacterKey through the new `character_info` C ABI bridge**~~ | ~~EASY~~ | ✅ **Shipped** (rolled in with the Tier 1 / Tier 2 key-resolver wave, before this roadmap was last updated). `NativeCharacterInfoCatalog` lives at `src/CrimsonAtomtic.RustInterop/NativeKeyInfoCatalogs.cs`, `CharacterKey` is in `LocalizationProvider.TableDrivenKeyTypes`, and `ResolveKeyTableOne` routes through `DisplayOrFallback(_characterInfo, ...)` with the lo24 cat-byte strip + internal-name fallback. |
| 8 | ~~**NPC portrait pipeline + Rename Mercenary portrait column**~~ | ~~MEDIUM~~ | ✅ **Shipped.** `PortraitProvider` (`src/CrimsonAtomtic.Ui/Services/PortraitProvider.cs`) lazy-loads `.dds` portraits into `%LOCALAPPDATA%\CrimsonAtomtic\PortraitCache\` via `crimson_characterinfo_resolve_portrait`. `MercenaryRow.StartPortraitLoad` kicks off the background load on row construction; Rename Mercenary dialog renders the Image when one matches above the score threshold, falling back to the per-category Unicode glyph (`🐎` / `🛒` / `🎈` / `🦌` / `👤` / `🐾` / `❔`) otherwise. The same `PortraitProvider` instance also drives the Browse Characters / NPCs picker (#9) and the new Character Refs Browser (2026-05-17). |
| 9 | ~~**Browse Characters / NPCs dialog**~~ | ~~EASY~~ | ✅ **Shipped 2026-05-15 part 6.** Tools → Browse Characters / NPCs… mirrors "Browse Items" but is driven by `characterinfo.pabgb` + the portrait pipeline. `CharacterPickerViewModel` enumerates every `(CharacterKey, internal_name)` pair via `crimson_characterinfo_get_entry` (two-call), joins each with PALOC-resolved display name (English + optional secondary), and shows an NPC portrait when one matches above `MinAcceptableScore`. Read-only — no Add-to-bag analog. Useful for FieldNPC investigation work (deferred item #5's other half). |

**Lower-priority deferred items** (the older list — none blocking; pick by appetite):
- Items #1, #5b, #7, #9–#14 from the original list below. (Items #2, #3, #4, #5a, #6, #8 already shipped or addressed — see strikethrough rows there.)
- Plus crimson-rs "optional follow-ons" surfaced by the wave-2 refresh: Knowledge group breadcrumb (resolve KnowledgeKey to "Knowledge › 〈group〉 › 〈entry〉"), Quest chapter rollup ("Quest › 〈chapter heading〉 › 〈quest title〉"), broader CharacterKey PALOC namespaces beyond `lo32=0x30` (needs a save sample touching more named NPCs), portrait matcher mesh / customisation tokens (CrimsonForge-style — current matcher scores name tokens only).

## Sealed Abyss Artifact investigation (HISTORICAL — superseded by 2026-05-15)

> **NOTE**: this whole investigation has been superseded by the
> 2026-05-15 work documented at the top of this file (Pattern B v1).
> Key findings:
> - The original "edit `_chargedUseableCount`" approach was operating on
>   the wrong layer entirely — it modifies the artifact ITEM, not the
>   challenge progression bookkeeping that the engine actually reads.
> - The engine's completion bookkeeping uses a four-piece structure
>   (catalog row + adjacent twin + FAR tracker + X_2 follow-up) plus
>   alertHistory. Documented in detail at the top of this file.
> - Pattern B v1 (FAR tracker flip + X_2 sub-mission insert, leaving
>   catalog + alertHistory for the engine to fill in at reward claim)
>   is the safe per-challenge recipe. Per-row button only.
> - Section kept below for historical context — don't use any of the
>   recipes / hypotheses in it directly.

### Symptom

Workflow: load slot103 (1.06, chapter VIII, Harmonious Hooves 2
challenge **in progress** in slot102 then engine-completed in
slot103). Try to replicate via editor: take slot102, edit
`_inventorylist[4]._itemList[37]` (the Sealed Abyss Artifact, item
1002011) so `_chargedUseableCount = 15` + `_maxChargeUseableCount`
becomes absent. Save. Copy resulting file to a new slot. Load
in-game → **artifact stays in "in progress" state, can't be clicked
to claim reward**. (Compare: slot103 itself loads in-game and the
artifact IS claimable.)

### What we verified

- **Our encoder is byte-perfect**. ctypes-driven test (see
  `out/diff-slot102-103/slot102_edited.save`) replicates the engine's
  slot102→slot103 transition. The 6 byte diffs between our output
  and slot103 are all u32/u64 `payload_offset` values, each
  consistent with a global +512-byte shift caused by other content
  differences earlier in the save. Mask matches (`9f282900`),
  data_size matches (228 bytes, was 232), every scalar field
  matches. PR B.6 `payload_offset` canonicalization is producing
  correct output.
- **The artifact item has no external cross-reference.** itemKey
  `1002011` appears only at `_inventorylist[4]._itemList[37]._itemKey`
  in both slot102 and slot103. No `_useItemSaveList` entry. No
  shortcut. No equipment-bar binding.
- **`_useItemSaveList` 8→9 element change was for a different item**
  (1002130 = `Kuku_Pot_BackPack`). Don't confuse it with the artifact.

### Suspected remaining state

Between slot102 and slot103 the engine wrote **2570 diff rows**
(mostly noise: faction reputation, field state, cooldown timer ticks).
Among the smaller-footprint changes that COULD relate to artifact
claimability:

- **`ContentsMiscSaveData._alertHistorySaveDataList[1832]` — new
  entry**:
  - `_alertType = 30`
  - `_generatedLocalTime = 1778794559`
  - `_missionKey = 4294966809` (= `0xFFFFFE99`, looks like a
    negative-encoded internal mission ID — same sentinel pattern as
    `_lastCompletedMissionKey` updates documented earlier)
  - `_saveVersion = 2`
- `ContentsMiscSaveData._lastFieldGimmickSaveDataKey: 251658240 → 1577058304`
- `EquipmentSaveData._equipCacheSequenceNo: 25519 → 25529` (+10)
- Many `_chargedUseableCount` timestamps tick forward by a fixed
  delta (engine game-clock counter advancing 9 minutes).

The alertHistory entry is the most plausible "ready to claim"
trigger. Next session: try adding a matching entry via list_insert
+ field patches before re-testing.

### What NOT to retry

- **Mask + 2-field edit on artifact alone** — confirmed insufficient.
  This is the user's tested case ("無效").
- **Looking for a "shortcut" / pointer in equip bar** — there is
  none for this item class.
- **Bytes-level encoder review** — encoder is correct.

### Files / scripts that help

- `out/diff-slot102-103/slot102.jsonl` / `slot103.jsonl` — every
  scalar field, JSONL per row. duckdb / pandas friendly.
- `out/diff-slot102-103/slot102_edited.save` — our editor's output
  (engine-equivalent for the artifact item). Use as the byte-level
  reference for "what our edit produces".
- `out/diff-slot102-103/slot102_body/` / `slot104_body/` — extracted
  bodies via `tools/extract/extract_save.py`. (slot104 is a 1.05-era
  save — DIFFERENT SCHEMA, don't compare against 1.06 saves.)
- `tools/analyze/dump_save_fields.py` — flattens to one-row-per-field
  JSONL for diff queries. `--include-array-elements` for socket sub-data.

### When picking this up again

1. Read [this section](#sealed-abyss-artifact-investigation-paused)
   from the top — context evaporates fast.
2. Verify slot102 / slot103 are still the right minimal pair (check
   their save chapter in-game + `parse_save_body_from_bytes` schema
   signature). If user has played further, take a fresh minimal pair.
3. Try the alertHistory insertion as the first hypothesis. If it
   doesn't unblock, do a comprehensive `_alertHistorySaveDataList`
   diff to see if the entry's shape is recipe-specific.

## (historical) Add-to-bag investigation — RESOLVED

### Symptom

Workflow: load slot100 → enter `_inventorylist[1]._itemList` (the
user's backpack, 168 items, first element happens to be Gold Bar) →
optionally select a same-shape donor row (Water, item 22008) →
Browse Items → pick Beer (item 22007) → "+ Bag" → Save → load in
game → **crash on save load**.

**Status: RESOLVED.** The five-fix series (`b393b03..b0a015a`) plus
PR B.6's `payload_offset` canonicalization produces saves the game
loads cleanly. The beer appears in the player's backpack and
behaves correctly. The investigation below is kept for historical
context — `_transferredItemKey` formula, mask shape constraints,
clone-template selection — but no action items remain.

The editor's status footer reports the operation as successful; the
on-disk save round-trips through our loader cleanly (HMAC ok, body
re-parses); only the game engine rejects it.

### What we already verified

- **HMAC + LZ4 + ChaCha20** are correct — saves we DON'T modify
  re-write byte-perfect and the game loads them. The crypto layer is
  fine.
- **Encoder is structurally correct** — round-trip + idempotence
  tests pass on every live save (slot0/1/2/100..105).
- **Per-element byte length** of the cloned beer matches the
  donor's; no list-count desync, no TOC offset drift.
- **`_transferredItemKey` encoding cracked** (this session):
  `_transferredItemKey = ((itemKey & 0xFFFF) << 16) | 0x0101` for
  every observed item across the user's full save. Verified on
  beer (22007 → 0x55F70101), Investigative Report
  (1000677 → 0x44E50101), and several others. Universal — works
  for itemKey ≤ 0xFFFF AND > 0xFFFF.
- **`payload_offset` canonicalization** (this session, upstream
  #32) — encoder now writes each locator wrapper's `payload_offset`
  u32 as the absolute body offset of wrapper_end, not the
  potentially-stale stored value. This was the most likely culprit
  per the legacy `parc_inserter3.py` reference, but **the fix
  alone didn't unblock the crash**.

### Five fixes attempted this session (all green, none unblocking)

| Commit | Fix | Test result | Game test |
|---|---|---|---|
| `b393b03` | Patch 4 fields on clone (`_itemKey`, `_stackCount`, `_slotNo`, `_itemNo`) instead of 1 | 119/119 ok | crash |
| `4a18a3a` | Honour `SelectedElement` as clone template — user can pick Water (consumable) instead of always cloning `[0]` (Gold Bar / currency) | 119/119 ok | crash |
| `52e2161` | Add `_transferredItemKey` patch (delta-shift) + `_isNewMark = true` | 119/119 ok | crash |
| `b0a015a` | Simplify `_transferredItemKey` to canonical `((itemKey & 0xFFFF) << 16) \| 0x0101` after user spotted my hex arithmetic error | 119/119 ok | crash |
| `ab815e6` (upstream) | Encoder rewrites every `payload_offset` to canonical wrapper_end | 119/119 ok | crash |

### Reference data

Empirical RE from slot100 → slot101 (engine wrote beer naturally
via in-game purchase). Documented in
`out/diff-slot100-101/`:

- slot100[123] = some item, displaced to slot101[124] after the
  insert (the engine inserts at array index 123, shifts the rest
  down by 1).
- slot101[123] = NEW BEER, fields:
  - `_saveVersion = 1`
  - `_itemNo = 3489`
  - `_itemKey = 22007 (0x55F7)`
  - `_slotNo = 123`
  - `_stackCount = 3` (player bought 3)
  - `_endurance = 65535` (max — present on every item observed)
  - `_maxSocketCount = 5`
  - `_socketSaveDataList` = 5 empty `ItemSocketSaveData` elements
  - `_transferredItemKey = 0x55F70101` (matches our formula)
  - `_chargedUseableCount = 0x0000_0001_DC37_0000` (looks
    timestamp-encoded — see below)
  - `_coolTimePerCharge = 0x0000_0001_DC37_0000` (same value!)
  - `_timeWhenPushItem = 116571626995711` (clearly a timestamp;
    looks like .NET Ticks or PA's analog)
  - `_isNewMark = true`
- **Element mask drift**: cloning shifts the mask shape:
  - Gold Bar `_itemList[0]` mask: `9f 28 2d 00` (has
    `_maxChargeUseableCount`, no `_coolTimePerCharge`, no
    `_isNewMark`)
  - Beer mask: `9f 28 b9 00` (no `_maxChargeUseableCount`, has
    `_coolTimePerCharge` + `_isNewMark`)
  - Water (22008) mask = beer's mask (same item-type, both are
    consumable drinks).

### Most likely remaining culprits (in priority order)

1. **`_chargedUseableCount` / `_coolTimePerCharge` / `_timeWhenPushItem`
   are stale.** Cloning Water keeps Water's encoded values
   verbatim. These look like per-instance timestamp-packed values
   the engine generates fresh per item; if the engine validates
   them against `_itemNo` / current time / each other on load,
   our reused values fail validation.
   *Recipe to test next session*: after clone, also patch these to
   either 0 or `DateTime.UtcNow.Ticks` (best-effort). The engine
   may also accept "all zero" as "freshly spawned, never used".

2. **`_socketSaveDataList` sub-elements have stale
   `_transferredItemKey` (or other) values.** Cloning Water →
   Beer keeps Water's `_socketSaveDataList` elements, including
   their wrapper payload_offsets (fixed by B.6 encoder) AND any
   sub-element scalars (NOT fixed). If `ItemSocketSaveData` has
   a `_transferredItemKey`-like field tied to the parent item,
   it'd be stale.
   *Recipe*: dump the beer element's complete byte breakdown
   side-by-side with the engine's slot101 beer; find any
   non-matching sub-element value.

3. **Hash / checksum we haven't found yet.** The reference repos
   don't have one over the decompressed body — but PaChecksum
   (Pearl Abyss's Jenkins lookup3 variant, in CrimsonForge
   `core/checksum_engine.py`) is used for PAZ/PAMT/PAPGT
   archives. The save engine MIGHT compute it per-item or
   per-block on load and crash on mismatch. None of our tooling
   detects it; needs `plcli` + hexpat probing.

4. **`item_use_info_list` cross-reference**: in iteminfo,
   beer's `item_use_info_list` is a list of MissionInfoKey hashes
   that point at the in-game "drinking beer" interaction tree. If
   the save references one of these per-instance somewhere we're
   missing, mismatch → crash.

### Files / scripts that help

- `out/diff-slot100-101/` — already has decompressed bodies and
  the field-by-field diff dump.
- `D:\Github\crimson-rs\out\items.jsonl` — every iteminfo entry
  for 1.06; useful for "what should THIS field look like for
  beer vs Water" comparisons.
- `D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS\CrimsonGameMods\parc_inserter3.py`
  — the legacy reference's full insert recipe. We've cribbed 6
  of its 7 fixup passes; the gap is either in the per-item field
  patches it does (lines 202–259 build_item_from_template) or in
  a structural fixup we haven't translated.
- `D:\Github\crimsonforge\core\checksum_engine.py` — PaChecksum
  implementation, kept in case the save engine uses it
  somewhere we haven't found.
- `tools/analyze/dump_save_fields.py` — flattens a save to
  one-row-per-field JSONL. Useful for cross-saving diff queries.

### What NOT to retry

- **Encoder fixes** — done correctly. Round-trip is byte-perfect
  modulo intentional canonicalization. Don't touch.
- **`_transferredItemKey` formula** — verified universal on every
  observed item. Don't tweak.
- **Mask shape concerns** — cloning a same-class donor (Water →
  Beer, both `_itemList` consumables) gives the right mask.
  Don't add mask-derivation logic.
- **Cloning from item `[0]` indiscriminately** — fixed in
  `4a18a3a`; the picker now respects `SelectedElement`.

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

4. ~~**`skill_info` bridge** for SkillKey / KnowledgeKey name
   resolution.~~ ✅ **Shipped.** `skill_info` C ABI now lives at
   `vendor/crimson-rs/src/c_abi/skill_info.rs`; C# consumes via
   `NativeSkillInfoCatalog` + `LocalizationProvider._skillInfo` →
   `LookupStringKey` (no PALOC chain). Same wave brought 8 sibling
   Key bridges online — `MissionKey`, `QuestKey`, `StageKey`,
   `KnowledgeKey`, `QuestGaugeKey`, `SkillKey`, `GimmickInfoKey`,
   `LevelGimmickSceneObjectInfoKey`, `SubLevelKey` — all in
   `LocalizationProvider.TableDrivenKeyTypes`. Mission/Quest/Stage/
   Knowledge resolve through `LookupDisplayName → PALOC localized
   title`; Gauge/Skill/SubLevel are internal-name only (no PALOC
   chain). So follow-on #6 below also dropped.

5a. ~~**`FieldNPCSaveData._characterKey` resolution.**~~
    🟡 **Substantially addressed by roadmap pick #7** once shipped —
    the new `character_info` C ABI bridge does the lo24 cat-byte
    strip we don't do today, plus internal-name fallback when PALOC
    misses. Upstream measures 22% PALOC display + ~100% internal-name
    coverage on the 221-key sample save.

5b. **`FieldGimmickSaveDataKey` resolution.** Still no path —
    looks like a spawn template ID (structured u32, not a localized
    namespace reference). Probably needs a different data file
    parsed; lower priority since most users don't care about
    anonymous field gimmicks.

6. ~~**`MissionKey` / `KnowledgeKey` / `QuestKey` proper names.**~~
   ✅ **Shipped** as part of the table-driven Key bridge wave (see
   #4). All three resolve via their dedicated `.pabgb` bridges
   (`mission_info` / `quest_info` / `knowledge_info`) +
   `LookupDisplayName → PALOC` for the localized **title**; internal
   name fallback when PALOC misses.

   **Original entry was misframed.** It claimed titles need a
   template-resolver pass because of `{staticInfo:Mission:...}`
   references at 0xC1. That's wrong: PA's title strings are flat in
   the localized catalog at the Key bridges' lo32 namespaces — they
   don't pass through the template layer. Template references only
   appear in the longer **description / dialogue / quest-objective**
   strings (different PALOC entries, different lo32). Those aren't
   currently surfaced anywhere in the editor, so no work is needed
   today; if a future feature wants to show e.g. a quest's
   objective text inline, *that* would need a template-resolver
   pass — separate scope from the "proper name" goal which is now
   fully covered.

7. **Length-changing edits (PR B)**. List add / remove / reorder
   + inline-byte resize. Needs an `ObjectBlock` re-serializer
   (mirror of `decoder.rs`) and body re-emit that recomputes TOC
   offsets. Hard but unlocks adding new inventory items (not just
   editing existing slots). Value-prop dropped now that Item
   Picker + slot replace + Fill stacks cover most edit needs.

8. ~~**Cross-bag item search beyond one nested level.**~~
   ✅ **Superseded** by `Tools → Find Items` (2026-05-15 part 7) which
   uses `crimson_save_list_inventory_items` to flat-list every item
   slot across every container in one FFI call — strictly better than
   walking nested haystacks on the C# side. `BuildNestedHaystack`
   stays as the in-block element-picker's filter helper (it's the
   right tool for "filter the elements you're currently looking at"),
   but the cross-bag "where is item X?" use case the original entry
   targeted is fully covered by the new Find Items dialog. If a
   future 1.x patch introduces a list-of-lists shape that breaks
   the element-picker filter specifically, revisit — but no current
   gap.

9. **Avalonia.Diagnostics 12.x** — add back behind a `Debug`
   condition once published.

10. **Re-evaluate the DataGrid AOT warning suppression** —
    `<NoWarn>IL2104;IL3053</NoWarn>` in `CrimsonAtomtic.Ui.csproj`
    is a workaround for Avalonia DataGrid 12.0.0 internals. Drop
    when 12.1+ ships a trim-safe DataGrid.

11. 🟡 **Save backup management** — partially superseded by 2026-05-16
    part 10 (ChangeJournal + close-on-dirty confirm). The
    "auto-backup on every load" half is no longer pursued — the
    user's actual concern ("warn me I'm about to lose edits") is
    met by the close-on-dirty modal. The "configurable retention"
    half (making `SaveBackupService.MaxVersionsPerSlot` adjustable
    via `AppSettings`) is still deferred — no strong demand.

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
