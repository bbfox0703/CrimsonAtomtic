# Status archive — full session history

> **Archived verbatim from docs/status.md on 2026-06-12** (when status.md
> was trimmed to a lean current-state doc — it had grown past 300 KB).
> This file is the complete, append-only session-by-session log and the
> long-form investigations (Sealed Abyss, add-to-bag, icon pipeline, key
> resolvers, mount-unlock saga, per-version alignment notes, etc.).
>
> **For current state, next task, and active gotchas, read
> [status.md](status.md) — not this file.** Look here only when you need
> the deep history behind a decision. Do not append new sessions here;
> append to status.md and let it be trimmed into this archive when it grows.

---
# Status / session handoff

> **Read this first on a new session.** Living document — update at the end
> of every session so the next pickup is seamless.
>
> Last updated: 2026-06-12 (**Editor aligned to game 1.11** — the live install bumped 1.10 → 1.11 (`meta/0.paver` = `01 00 0b 00 00 00 24 7a 2c 20`, build `0x202c7a24`). 1.11 is the **second consecutive iteminfo schema drift** (after 1.09→1.10) but, unlike 1.10, brought **no save-body change**. crimson-rs had already absorbed it on `dev`/`cc37011`: iteminfo `8fdeb45` inserts a new per-item boolean `u8` `unk_post_apply_drop_stat_type` between `apply_drop_stat_type` and `drop_default_data` (every item +1 byte; anchored export ok=**6,333**/+8 vs 1.10, leftover=0). Save format unchanged (v2 / flags `0x0080`); all live slots parse `hmac_ok`, body decode `undecoded_bytes=0`. **The built `crimson_rs.dll`/`.lib` were stale (2026-06-10, predating the 1.11 commit), so this session rebuilt the native lib** (`scripts/build_rust.ps1` → dll 1193.5 KB / lib 14.5 MB). C# side: `GameDataVersion.ParserTargetMinor` 10 → 11 + `CompatibleMinors {10}` → `{11}` (1.10 iteminfo no longer round-trips → a user on 1.10 is now warned); editor version `VerMinor` 10 → 11 (`VerPatch` reset to 1 per the lock-step convention → title `v1.11.01.{build}`). Paver tests re-pinned (happy-path → 1.11 stamp; `_PreviousMinor_FlagsIncompatible` now asserts **1.10** is rejected; future-minor guard moved 1.11 → 1.12). Docs bumped (root CLAUDE.md, game-versions.md current-install + a 1.10→1.11 diffing-history paragraph). **Also fixed an unrelated build blocker: `Directory.Packages.props` pinned `runtime.win-x64.Microsoft.DotNet.ILCompiler` at 10.0.8 but the local SDK (10.0.301) ships runtime 10.0.9, so `PublishAot`'s auto-injected ILCompiler 10.0.9 tripped NU1109 (downgrade) — bumped the pin to 10.0.9.** **346 tests pass (0 skipped — live-install + catalog tests parsed the real 1.11 iteminfo OK), Debug build clean (0/0), AOT publish clean (single `CrimsonAtomtic.exe` 27.5 MB, `crimson_rs.dll` folded in → the rebuilt `.lib` links).** **Live-save verification (throwaway test, since deleted) through the C# `NativeSaveLoader` FFI:** slot107 (1.11 native — HmacOk, schemaTypes=101, blocks=1109, fields 3102/3102), slot100 (old-format — HmacOk, schemaTypes=100, blocks=1144, fields 3172/3172), slot102 (slot100's 1.11 save-as — identical to slot100) — all load HMAC-ok, decode every present field (no undecoded drift), and survive a write round-trip → reload HMAC-ok. **Vendor `crimson-rs` now at `cc37011`.** **Not yet done: an in-app GUI run-through (load slot107 → confirm NO version-mismatch warning, item names resolve against 1.11 iteminfo, title shows v1.11.01, edit→save→reload).** World Map parchment layer-alignment bug from part 14 still open. — Earlier: 2026-06-09 (**Editor v1.10.01 + 4 UX fixes** — version bumped to **1.10.01** with a documented game-sync convention: `VerMajor.VerMinor` track the live game data version (`VerMinor == NativePaverReader.ParserTargetMinor`, bumped in lock-step), `VerPatch` is the editor's own per-release counter (reset to 01 when the game minor bumps). `GetAppVersion()` zero-pads minor+patch so the title bar reads **"v1.10.01.{build}"**. Four user-reported fixes: (1) the structural-edit "may not load" **file-length save warning is now localized**; (2) the sealed-artifact "Mark Challenge Complete" **confirm dialog is now localized** (en/ja/zh-TW; `&#10;`-newline resource strings + a `{0}..{10}` format string for the parameterised detail); (3) **Browse Items gained a "Go to item in save" top-bar button** that jumps the main editor to the catalog item's slot when the save holds it (reuses `NavigateToInventoryItemAsync`; reports "not in this save" otherwise); (4) **Restore from Backup no longer snapshots the current save first** — backups are taken only on an editor Save. 13 new resource keys in all three languages; 346 tests pass, Debug clean (0/0). Earlier: 2026-06-05 (**Editor aligned to game 1.10** — the live install bumped 1.09 → 1.10 (`meta/0.paver` = `01 00 0a 00 00 00 ac b2 84 cf`, build `0xcf84b2ac`). Unlike the content-only 1.06→1.09 jumps, **1.10 is the first iteminfo schema drift since the 1.05/1.06-era `ItemSaveData` change**, and crimson-rs had already absorbed it on `dev`/`fc5be9d`: (1) iteminfo `dd2ed2e` dropped `money_icon_path` + added `UnitData.unk_post_icon_path` (byte-perfect on all 6,325 items); (2) save-body `f1513b8` widened the `ContentsMiscSaveData` ReflectObject-list leading-pad scan 0..=3 → 0..=4 — **without this fix the editor silently corrupted any 1.10 save it wrote** (107 KB undecoded → dropped on re-encode); the furthest-reach tiebreak keeps 1.09/older saves byte-identical. Vendor was already at `fc5be9d` (no refresh needed) but the built `crimson_rs.dll`/`.lib` were stale from May 23, so this session **rebuilt the native lib** (`scripts/build_rust.ps1` → dll 1166 KB / lib 14.4 MB). C# side: `GameDataVersion.ParserTargetMinor` 9 → 10 and the allow-list **tightened `CompatibleMinors {8, 9}` → `{10}`** (1.09 iteminfo no longer round-trips against the 1.10 parser, so a user still on 1.09 is now warned — the opposite of the 1.08→1.09 "don't warn" case). Paver tests re-pinned (happy-path → 1.10 stamp; `_PreviousMinor_FlagsIncompatible` now asserts 1.09 is rejected; future-minor guard moved 1.10 → 1.11). Docs bumped (root CLAUDE.md, game-versions.md current-install + diffing-history). **346 tests pass (0 skipped — live-install + catalog tests parsed the real 1.10 iteminfo OK), Release build clean (0/0).** **Not yet done: AOT publish smoke (the rebuilt `.lib` must link) + an in-app run-through against a live 1.10 save (load → edit → write → HMAC ok).** **Vendor `crimson-rs` now at `fc5be9d`.** World Map parchment layer-alignment bug from part 14 still open. — Prior session 2026-05-31: **Add-Item localization + mercenary-name read-back** — two follow-ups on shipped features. (A) The Add-Item picker's top bar (`+ Add "X" from "Y"`), its hint/prompt, and the main-window status line are now fully localized via **format-string resources** (`{0}/{1}` placeholders reordered per language — en "Add X from Y" vs zh「從 Y 新增 X」); the picker is passed the raw source *name* (`AddItemSourceName`) instead of a pre-composed English phrase, and composes the localized phrase itself via `LookupUiResourceString(key) ?? fallback` + `string.Format`. (B) The Rename-Mercenary dialog now **pre-fills the 新名稱 box with the current `_mercenaryName`** — required a new **read-side FFI** `crimson_save_get_inline_bytes_field` (the symmetric getter the setter lacked: block-JSON only renders inline_bytes as `<N items, M bytes>`). Source-first per Rule 8: added at `D:\Github\crimson-rs` (`src/c_abi/mod.rs`, reuses `navigate_to_parent_ref` + new `write_bytes_to_buf`; roundtrip test; cargo test + clippy clean; committed `7191a7d` on dev), vendored via `update_vendors.ps1`, dll rebuilt via `build_rust.ps1`. C# `ISaveLoader.GetInlineBytesField` + `NativeSaveLoader` impl (two-call buffer), read per element in `RenameMercenaryViewModel.TryCreate`, UTF-8-decoded into `NewName`. 346 tests pass, Debug clean. **Vendor `crimson-rs` now at `7191a7d`.** **Earlier today: Faction-node editor shipped** — Tools → Edit Faction Nodes: discover / set-state for the ~1,158 faction strongholds. Foundation-first verified against slot102: `FactionSaveData._factionNodeElementSaveDataList` elements carry `_ownerFactionKey` (TypeName **`FactionNodeKey`**, NOT FactionKey — resolves to internal names like "Node_Her_HernandCastle"), `_factionState` (`FactionNodeStateType` u8: 0=Undiscovered…2=Active…4=Lost), `_conquerorFactionKey` (FactionKey), `_isCapital`. "Discover" = set `_factionState`→2, a plain in-place u8 write via `SetScalarFieldsBatch` (no list growth). Added the **`crimson_factionnode_*` C# name bridge** (`NativeFactionNodeInfoCatalog` + LocalizationProvider `FactionNodeKey` routing) — dll already exported it, no vendor change; this also lights up the generic field tree's name column for faction-node keys. Checkbox + filter + bulk "Set selected to <state>" + "Discover all" dialog (mirrors the Knowledge/Sealed-Abyss dialogs). 346 tests pass (+11), Debug clean. **Earlier today: Three inventory/UX improvements landed on `dev`** — see the ✅ entry directly below. (1) Unified, discoverable Add-Item flow: a per-row "+ Add Item…" button on the elements grid + a top action bar in the item picker (`+ Add "X" from "Y"`, live-updating "from" as the user reselects rows); Tools → Browse Items now uses the same bar (no more per-row `+ Bag`); the Sockets gem-picker keeps its per-row "Pick". (2) Bulk fills (`Fill ALL stacks`, per-container `Fill stacks`) now cap huge-cap items at 9,999,999 and leave already-larger stacks alone; single `Fill stack` + edit-panel `Set to Max` stay uncapped. (3) Sealed Abyss bulk-complete is now a checkbox **preview dialog** (search + Select all / Unselect all / Invert), replacing the one-shot confirm. UI-only — no vendor work, no new ABI. 335 tests pass (+13), Debug build clean (0/0). **Earlier today: Mount-Unlock dialog + Knowledge editor shipped to `main`** — see the ✅ entries below; both reuse the `ResolveKnowledgeList`/`ApplyKnowledgeInjectAsync` + element bytes-insert primitives, no vendor work. Earlier this week: editor aligned to game **1.09** — the live install bumped 1.08 → 1.09, so `GameDataVersion.ParserTargetMinor` 8 → 9 and the compatibility gate became an allow-list `CompatibleMinors = {8, 9}` (1.08/1.09 share a byte-identical iteminfo schema, so un-updated 1.08 users aren't warned). Vendor `crimson-rs` was already at `0619789`, which validated the full toolkit against live 1.09 (content-only delta over 1.08, **no schema drift**: iteminfo byte-identical, all 30 gamedata tables parse, save roundtrip clean) — so no vendor refresh was needed, only the C#-side constant + tests + docs. World Map parchment layer-alignment bug from part 14 still open — unchanged this session).
>
> ## ✅ This session — what shipped (2026-06-12 — Editor aligned to game 1.11)
>
> User directive: "1.11 版上線，檢查所有的功能是否正常" — game patched to 1.11; verify the editor still works, using the provided saves (slot107 = 1.11 native; slot100 = old-format → slot102 = its 1.11 save-as).
>
> **Foundation-first findings before touching code:** live `meta/0.paver` = `01 00 0b 00 00 00 24 7a 2c 20` (1.11.00, build `0x202c7a24`). Vendor **and** source `crimson-rs` were **already** at `cc37011`, which lands commit `8fdeb45` "feat(1.11): support Crimson Desert 1.11 iteminfo + validate full pipeline" — so the Rust parser was ready. But the built `crimson_rs.dll`/`.lib` in the tree were **stale (2026-06-10, before the 1.11 commit on 2026-06-12)**, so they'd mis-decode item names against the shifted 1.11 iteminfo. 1.11 is the second consecutive **iteminfo** drift but, unlike 1.10, has **no save-body drift** (confirmed by crimson-rs against all live slots and re-confirmed here at the C# loader level).
>
> | Area | Scope |
> |---|---|
> | **Native rebuild** | `scripts/build_rust.ps1` rebuilt from vendored `cc37011` → `crimson_rs.dll` (1193.5 KB) + `crimson_rs.lib` (14.5 MB). Picks up iteminfo `8fdeb45` (1.11 layout: +`unk_post_apply_drop_stat_type` `u8` per item). No vendor refresh needed (already current). |
> | **`ParserTargetMinor` 10 → 11 + allow-list `{10}` → `{11}`** | `NativePaverReader.cs` — `ParserTargetMinor` 10 → 11; `CompatibleMinors [10]` → `[11]`. 1.11 drifted iteminfo so 1.10 no longer round-trips → a user still on 1.10 IS now warned (same shape as the 1.09→1.10 bump). Doc comments rewritten to record the new per-item `u8` + the no-save-body-drift fact. `GameVersionMismatchDialog` target string auto-derives from `ParserTargetMinor` → "1.11.xx" (no edit). |
> | **Editor version → v1.11.01** | `CrimsonAtomtic.Ui.csproj` `VerMinor` 10 → 11, `VerPatch` reset to 1 (per the lock-step `VerMinor == ParserTargetMinor` + patch-resets-on-game-minor convention) → title `v1.11.01.{build}`. |
> | **Paver tests re-pinned** | `NativePaverReaderTests.cs` — happy-path now pins the 1.11 stamp (minor 11, build `0x202c7a24`, "1.11.00"); `_PreviousMinor_FlagsIncompatible` flipped to assert **1.10** is now rejected; the future-minor upper-bound guard moved 1.11 → **1.12**; legacy-1.07 + live-install comments retargeted to `{11}`/1.11. |
> | **Build blocker fix (unrelated to 1.11)** | `Directory.Packages.props` centrally pinned `runtime.win-x64.Microsoft.DotNet.ILCompiler` at 10.0.8, but the local SDK 10.0.301 ships runtime **10.0.9**, so `PublishAot`'s auto-injected `Microsoft.DotNet.ILCompiler 10.0.9` required `runtime.win-x64.* >= 10.0.9` → **NU1109 downgrade error** blocking every build. Bumped the pin to 10.0.9 (commented to track the installed runtime). |
> | **Docs** | Root `CLAUDE.md` "currently 1.10" → "1.11"; `docs/game-versions.md` current-install stamp + `CompatibleMinors {11}` rationale + a 1.10→1.11 iteminfo-drift / no-save-body-drift diffing-playbook paragraph. |
>
> Tests: **346 pass (0 skipped)** — no count change (constants/native bump, not a feature); the live-install + key/string catalog tests ran against the real 1.11 install and parsed 1.11 iteminfo cleanly. Debug build clean (0/0). **AOT publish smoke PASSED** (`build.cmd publish` → single `CrimsonAtomtic.exe` 27.5 MB, `crimson_rs.dll` absent = folded into the exe, so the rebuilt `.lib` links). **Live-save verification PASSED** via a throwaway test through the C# `NativeSaveLoader` FFI (since deleted): slot107 (1.11 native: HmacOk, schemaTypes=101, blocks=1109, fields 3102/3102), slot100 (old-format: HmacOk, schemaTypes=100, blocks=1144, fields 3172/3172), slot102 (slot100's 1.11 save-as: identical to slot100) — all load HMAC-ok, decode **every present field** (`fieldsDecoded == fieldsPresent`, the C#-level "undecoded_bytes=0"), and survive a write round-trip → reload HMAC-ok. **Not yet done: an in-app GUI run-through** (load slot107 → confirm NO version-mismatch warning, item names resolve against 1.11 iteminfo, title shows v1.11.01, edit→save→reload HMAC ok). **Vendor `crimson-rs` now at `cc37011`.**
>
> ### Open follow-on noted this session
>
> - **`ParserTargetMinor` / `CompatibleMinors` STILL hard-coded C#-side** — this is now the *fourth* manual bump (8→9→10→11). The part-17 drift hazard stands; promote to a `crimson_parser_target_gamedata_minor()` + a compatible-set ABI so Rust is the single source of truth. (Friction keeps recurring every patch.)
>
> ## ✅ Prior session — what shipped (2026-06-09 — Editor v1.10.01 + 4 UX fixes)
>
> User directive: bump the editor to **1.10.01** and make the first version components track the game data version going forward; plus four fixes — localize the file-length (structural-edit) save warning, localize the Sealed-Artifact "set Complete" confirm, add a "switch to that item" button in Tools → Browse Items, and stop the restore-time double-backup.
>
> **Version semantics confirmed with the user:** first two components = game data version (`VerMajor`/`VerMinor`, `VerMinor == ParserTargetMinor`), third = editor's OWN release iteration within that game version (reset to 01 on a game-minor bump). NOT a game patch level.
>
> | Area | Scope |
> |---|---|
> | **Version 1.10.01 + sync convention** | `CrimsonAtomtic.Ui.csproj` `VerMinor` 0 → 10, `VerPatch` 0 → 1 (full 4-part = `1.10.1.{build}`, build from `build_number.txt`). Comment documents the lock-step `VerMinor == ParserTargetMinor` rule + the patch-is-editor-iteration convention. `GetAppVersion()` now zero-pads minor+patch (`v{Major}.{Minor:D2}.{Build:D2}.{Revision}`) so the title reads **"v1.10.01.{build}"**, mirroring the game's "1.10.xx" format. Verified: assembly stamps `1.10.1.5`, title shows `v1.10.01.5`. |
> | **Structural-edit warning localized** | `MainWindowViewModel.ConfirmStructuralEditOrAbortAsync` now resolves `StructuralEditWarningTitle`/`StructuralEditWarningBody` (was hard-coded English), with inline English fallbacks. |
> | **Sealed-artifact confirm localized** | `MarkCurrentChallengeCompleteAsync`'s big confirm split into resources: `MarkChallengeConfirmTitle` + a `MarkChallengeConfirmDetail` format string (`{0}..{10}`; {5}=update/create word, {8}/{9}=tag counts, {10}=follow-up bullet line) + a static `MarkChallengeConfirmWarnings` block + `MarkChallengeWordUpdate`/`MarkChallengeWordCreate`, `MarkChallengeFollowUpExists`/`New`, `MarkChallengeCancelled`. Inline English fallbacks kept (the `?? "..."` shape also dodges CA1863/CompositeFormat). |
> | **Browse Items "Go to item in save"** | `ItemPickerViewModel` gained `GotoItemRequested` + `GotoSelectedCommand` (enabled on any selected row, independent of `CanAddToTarget`); a second top-bar button in `ItemPickerWindow.axaml` (top-action-bar mode only, so the Sockets gem-picker is unaffected); `MainWindow.OpenAddItemPicker` routes it via new `NavigateToBrowseItemAsync` → `MainWindowViewModel.NavigateToItemByKeyAsync(itemKey)` — scans `ListInventoryItems` for the first slot with that `_itemKey` and reuses `NavigateToInventoryItemAsync`; reports `ItemPickerGotoNotFound` if the save doesn't hold the item. |
> | **Restore no longer double-backs-up** | `RestoreFromBackupAsync` dropped its pre-restore `BackupBeforeWriteSilent(targetSavePath)` call; the confirm message + `RestoreFromBackupColumnHint`/`RestoreButtonTip`/`MaxVersionsPerSlot` + `SaveBackupService` class/`Restore` docs reworded. Backups are now created **only on an editor Save** (`SaveAsync`/`SaveAsAsync`). |
>
> 13 new resource keys added to all three of en/ja/zh-TW (validated well-formed XML + key-parity, 13/13/13). Multi-line resource strings use `&#10;` + `xml:space="preserve"`. Tests: **346 pass (0 skipped)**, Debug build clean (0/0). **Not yet done: in-app run-through (zh-TW: trigger a structural edit → localized "may not load" warning; Mark Challenge Complete → Chinese confirm; Browse Items → "Go to item in save" jumps when held / status when not; Restore → confirm NO fresh backup folder is created).** Follow-up done same session: the shared `ConfirmDialog` (was fixed 440×200, non-resizable, clipping the long mark-challenge confirm) now uses `SizeToContent="Height"` + `MaxHeight="640"` + a `ScrollViewer` + `CanResize="True"`, so it grows to fit short confirms and scrolls long ones — the localized warning is now readable end-to-end.
>
> ### 2026-06-09 (cont.) — broad Tools-menu dialog localization pass (COMPLETE)
>
> User in-app testing (zh-TW) found the deeper gap: the structural-edit warning worked, but **most confirm dialogs, status lines, and journal (變更) entries across the Tools-menu flows were hard-coded English** — NOT an AOT failure (the resource mechanism + `{DynamicResource}` bindings work; these strings were just never routed through it). Also: the per-row Mark-Challenge confirm was localized last round but the user used the **bulk** path (a different method). User chose: localize **all Tools-menu dialogs**, done sequentially.
>
> Introduced a shared **`Services/UiText.cs`** (`Get(key, fallback)` / `Format(key, fallback, args)`) — AOT-safe `TryGetResource` + `string.Format`, replaces the per-VM `LookupUiResourceString` copies. **Key insight:** all three language dictionaries stay merged (active one last-wins), so a key present only in zh-TW/ja LEAKS into the English view — every key must be in all three. So en.axaml carries the canonical English (= the inline code fallback) too.
>
> **Done + tested (346 pass, Debug 0/0, 638 keys × 3 langs, full parity):** `SealedArtifactChallengeViewModel` (broad-scan warning + bulk confirm + all status/`NoCandidatesSummary` + the `SealedArtifactBroadScan` checkbox in its XAML), `MainWindowViewModel` (per-row + bulk Mark-Challenge status/journal, stack-fill single + all-inventories confirm/status/journal, remove-element, unlock-all-abyss-gates confirm/status/journal, `ResolveKnowledgeList`/`InjectKnowledge` errors + knowledge learn flow, faction/add-item/mount/dragon journal + dragon-unlock return strings, restore confirm/status, backup status, field-edit journal, `DetailsErrorBlockNotFound`), `FactionNodeEditorViewModel`, `KnowledgeEditorViewModel`, `MountUnlockViewModel`, `AbyssGatesViewModel`. ~125 new resource keys (en + zh-TW + ja). Shared keys `DialogFilterSummary`/`DialogCancelled`/`DialogNoSaveLoaded`. Pure `"{ex.Message} (code N)"` strings left as-is (no translatable text); the per-row Mark-Challenge disabled-button `skipReason` diagnostics left English (technical, tooltip-only).
>
> **Round 2 (same session) — remaining surfaces DONE.** Finished: (1) the 4 hard-coded-literal `MainWindow.axaml.cs` alert dialogs (`SaveFailed*`, `DyeScanFailedTitle`, `DyeSlotOpenFailedTitle`, `AbyssGatesLoadFailed*`) — the other ~8 `ShowAlertAsync` sites already resolved via `FindResource` and were left; (2) all lower-traffic dialog VMs: `SocketEditorViewModel` (apply-set / pick / clear status + journal, incl. the `Set`/`Filled` verb split into full strings keyed on `wasFilled`), `DyeSlotEditorViewModel`, `VendorBuybackViewModel`, `RenameMercenaryViewModel` (status + journal + `(empty)`/name-bytes fragments), `CharacterRefsBrowserViewModel`, `CustomGemSetsEditorViewModel`, `RestoreFromBackupViewModel.Subtitle`; (3) the `IconExtractionProgressDialog` (Tools → Extract Icons) — XAML defaults → `{DynamicResource}` + all code-behind progress/finished/cancel strings via `UiText`; (4) `CustomGemSetsEditorWindow` "Label" placeholder. **Final tally: 696 resource keys × 3 langs, full parity, every `UiText` code key present in en, build clean (0/0), 346 tests pass.** Deliberately left English (no translatable text / technical): pure `"{ex.Message} (code N)"` passthroughs, the per-row Mark-Challenge `skipReason` diagnostics (tooltip-only), and the 5 numbered `"ItemKey #1"…"#5"` gem-slot placeholders (the `ItemKey` field name shows untranslated elsewhere too). **Round 3 (user-reported follow-up) — dialog header/count statuses fixed.** The Edit-Knowledge / Vendor-Buyback / Faction-Node dialogs still showed an English **load-time header status** (e.g. "1,141 faction node(s). Filter / tick rows…"). Root cause: those — plus `SocketEditorViewModel`'s header + `VendorBuybackViewModel.FilterCountText` getter + the `_journal.Log("Dye"/"Vendor Buyback", …)` categories — are built as **multi-line ternaries / expression-bodied getters**, which the round-1/2 survey grep (`StatusMessage = "…"` literal-after-`=`) didn't match. Localized all of them (`FactionNoNodes`/`FactionNodeHeaderStatus`, `KnowledgeNotLoaded`/`KnowledgeHeaderStatus`, `BuybackHeaderStatus`/`BuybackCountAll`/`BuybackCountFiltered`/`BuybackRemoveDone` + `JournalCatVendorBuyback`/`JournalBuybackRemoved`, `SocketHeaderStatus`/`SocketGemSetsRefreshed`, `JournalCatDye`/`JournalDyeEdited`). `FactionNodeEditorViewModel.NoNodesSummary` changed `const` → `static` property so it can resolve via `UiText`. **Final: 710 keys × 3 langs, full parity, no hard-coded `Journal.Log` categories remain, build clean (0/0), 346 tests pass.** **Not yet done:** in-app zh-TW run-through + an AOT `build.cmd publish` smoke before tagging.
>
> ## ✅ Prior session — what shipped (2026-06-05 — Editor aligned to game 1.10)
>
> User directive: "遊戲版本更新，crimson_rs已經更新，請更新本Editor" — game patched to 1.10; crimson-rs already carries the parser; bring the editor across.
>
> Findings before touching code: live `meta/0.paver` = `01 00 0a 00 00 00 ac b2 84 cf` (1.10.00, build `0xcf84b2ac`). Vendor `crimson-rs` was **already** at `dev`/`fc5be9d` (1.10 fully validated upstream), but the built `crimson_rs.dll`/`.lib` in the tree were stale (May 23). **1.10 is NOT a data-only patch** — it drifted the iteminfo schema, so the old constants and the stale dll would have mis-decoded item names and (worse) silently corrupted any 1.10 save written.
>
> | Area | Scope |
> |---|---|
> | **Native rebuild** | `scripts/build_rust.ps1` rebuilt from vendored `fc5be9d` → `crimson_rs.dll` (1166 KB) + `crimson_rs.lib` (14.4 MB). Picks up iteminfo `dd2ed2e` (1.10 layout: −`money_icon_path`, +`UnitData.unk_post_icon_path`) and save-body `f1513b8` (`ContentsMiscSaveData` ReflectObject-list leading-pad scan 0..=3 → 0..=4 — the fix that stops 1.10-save corruption; old saves stay byte-identical via the furthest-reach tiebreak). No vendor refresh needed (already current). |
> | **`ParserTargetMinor` 9 → 10 + allow-list tightened** | `src/CrimsonAtomtic.RustInterop/NativePaverReader.cs` — `ParserTargetMinor` 9 → 10; **`CompatibleMinors {8, 9}` → `{10}`**. This is the inverse of the 1.08→1.09 bump: because 1.10 drifted iteminfo, 1.09 no longer round-trips against this parser, so a user still on 1.09 IS now warned. Doc comments rewritten to record the schema drift + the corruption-fix rationale. `GameVersionMismatchDialog` target string auto-derives from `ParserTargetMinor` → "1.10.xx" (no edit). |
> | **Paver tests re-pinned** | `src/CrimsonAtomtic.Tests/NativePaverReaderTests.cs` — happy-path now pins the 1.10 stamp (minor 10, build `0xcf84b2ac`, "1.10.00"); the previous-minor test flipped from `_StaysCompatible` to **`_PreviousMinor_FlagsIncompatible`** (1.09 now rejected); legacy 1.07 test unchanged; the future-minor upper-bound guard moved 1.10 → **1.11**. Live-install pin still checks only Major, so it survived the bump. |
> | **Docs** | Root `CLAUDE.md` "currently 1.09" → "1.10"; `docs/game-versions.md` current-install stamp → 1.10 + `CompatibleMinors {10}` rationale + a diffing-playbook history paragraph documenting the 1.09→1.10 iteminfo drift + the leading-pad save-body fix. |
>
> Tests: **346 pass (0 skipped)** — no count change (this is a constants/native bump, not a feature); the live-install + key/string catalog tests ran against the real 1.10 install and parsed 1.10 iteminfo cleanly, which is the real proof the new schema loads. Release build clean (0/0). **AOT publish smoke PASSED** (`build.cmd publish` → single `CrimsonAtomtic.exe` 27.3 MB, `crimson_rs.dll` absent = folded into the exe, so the rebuilt `.lib` links). **Not yet done: an in-app run-through against a live 1.10 save (load → edit → write → reload → HMAC ok), to confirm the leading-pad fix end-to-end through the editor.**
>
> ### Open follow-on noted this session
>
> - **`ParserTargetMinor` / `CompatibleMinors` STILL hard-coded C#-side** — this is now the *third* manual bump (8→9→10). The part-17 drift hazard stands; promote to a `crimson_parser_target_gamedata_minor()` + a compatible-set ABI so Rust is the single source of truth. (Lower friction each time we re-touch this.)
>
> ## ✅ Prior session — what shipped (2026-05-31 — Add-Item localization + merc-name read-back)
>
> User report on shipped features: (1) the Add-Item picker's top button + status message weren't localized (and word order differs en/zh/ja); (2) Rename-Mercenary didn't show the already-applied custom name in the 新名稱 box.
>
> | Area | Scope |
> |---|---|
> | **A. Add-Item localization** | Picker top bar / hint / prompt + `MainWindowViewModel.AddItemToCurrentListAsync` status lines now resolve **format-string resources** (`{0}/{1}`) via `LookupUiResourceString(key) ?? fallback` + `string.Format`. Word order is per-language (en `+ Add "{0}" from "{1}"` / zh `+ 從「{1}」新增「{0}」` / ja `「{1}」から「{0}」を追加`). Restructured: `MainWindowViewModel` exposes `AddItemSourceName` (raw clone-source name, or null) instead of the English `AddItemTargetDescription` phrase; the picker (`SourceName`, was `TargetDescription`) composes the full localized string. New keys: `ItemPickerSelectPrompt`/`AddWithSource`/`AddNoSource`/`OpenBagHint`, `AddItemStatus*` (OpenBag/WrongList/MissingFields/Progress/Success/Failed), `AddItemSourceSelected`/`First`. (Note: the `?? "literal"` shape matters — passing a bare literal to `string.Format` trips **CA1863**/CompositeFormat; the null-coalesce makes the format non-constant.) |
> | **B. Merc-name read-side FFI** | block-JSON renders inline_bytes only as `<N items, M bytes>` (lossy), so reading `_mercenaryName` needed a new getter. **`crimson_save_get_inline_bytes_field`** added at the crimson-rs source (`src/c_abi/mod.rs`): read-only `navigate_to_parent_ref` + `meta_kind==1` guard + new no-NUL `write_bytes_to_buf`; absent/empty → OK with required=0. Roundtrip test (`c_abi_get_inline_bytes_field_roundtrip`) asserts byte-equality vs the decoder payload — passed on a live save. `cargo test` + strict clippy clean; committed `7191a7d` (dev). Vendored (`update_vendors.ps1`) + dll rebuilt (`build_rust.ps1`); symbol confirmed in the dll. C# `ISaveLoader.GetInlineBytesField` + `NativeSaveLoader` impl (two-call buffer, mirrors `DynamicArrayGetU32Elements`). `RenameMercenaryViewModel.TryCreate` reads each element's name (cheap, no re-decode) and UTF-8-decodes it into the editable `NewName`; dialog header reworded (en/ja/zh-TW) to drop the "read-side FFI 尚未實作" caveat. |
>
> Tests: **346 pass** (no new C# test — the picker-VM format path needs a heavy LocalizationProvider/IPazExtractor fixture for trivial logic; the Rust roundtrip + the run-through cover it). Debug build clean (0/0). **Not yet done: in-app run-through (zh-TW: picker bar word order + Chinese status line; Rename-Mercenary pre-filled names persist on reload + HMAC ok) and AOT publish smoke (the new `.lib` export must link).**
>
> ## ✅ Prior session — what shipped (2026-05-31 — Faction-node editor)
>
> User directive: "開始：Faction 據點（探索/設定狀態）功能" — build the dedicated faction-stronghold discover/set-state editor (was 🔸 field-edit-only).
>
> **Step 0 (foundation-first, verified on `vendor/savedata/slot102/save.save` via `tools/analyze/dump_save_fields.py`):** `FactionSaveData._factionNodeElementSaveDataList` = **1,158** `FactionNodeElementSaveData` elements. Per element: `_ownerFactionKey` (TypeName **`FactionNodeKey`** u32, 1000000–1001261 — the node identity; **not** a FactionKey), `_factionState` (`FactionNodeStateType` u8, present on all 1158: 0=Undiscovered/1=Discovered/2=Active/3=Conquered/4=Lost), `_conquerorFactionKey` (`FactionKey`, 117 present), `_isCapital` (bool, 60), plus `_blockSubType`/`_operationStateType`/`_lastRevivedFieldTimeRaw`/`_isBlock`/`_blockadingFactionKey`/etc. The earlier guess that owner was a `FactionKey` was wrong — corrected before writing code.
>
> | Area | Scope |
> |---|---|
> | **factionnode name bridge** | `_ownerFactionKey` is a `FactionNodeKey`, which has no PALOC display name — resolved via the `crimson_factionnode_*` C-ABI (the vendored dll **already exported** it; no vendor change). Added `NativeFactionNodeInfoCatalog` (+ `CrimsonFactionNodeInfoHandle`) in `NativeKeyInfoCatalogs.cs` (mirrors `NativeHouseInfoCatalog`), 4 P/Invokes in `NativeSaveLoader.cs`, and the 6 LocalizationProvider touch-points (const filenames, type set, field, `TryLoadNicheBridge` bootstrap from 0008/0.pamt, `ResolveByFieldTypeName "FactionNodeKey"→LookupStringKey`, dispose). → "Node_Her_HernandCastle"-style names; also lights up the generic field tree's name column. Conqueror resolves via existing FactionKey PALOC. |
> | **Scan + apply** | `MainWindowViewModel.ScanFactionNodes()` (block-walk → `FactionNodeTarget` records: block idx, 1-step path, `_factionState` field idx, current state, owner/conqueror keys, isCapital) + `SetFactionNodeStatesAsync(changes)` (one in-place `SetScalarFieldsBatch`, skips no-ops — no list growth/clone). `FactionNodeStates` label map + `All` list. |
> | **Dialog** | `FactionNodeEditorViewModel` + `FactionNodeEditorWindow` (near-clone of the Sealed-Abyss dialog): keyword filter, Select all / Unselect all / Invert, target-state combo + "Set selected", "Discover all" (every state<2 → Active), re-scan after apply. Menu item + `OnEditFactionNodesClick` (mirrors `OnCompleteSealedArtifactChallengesClick`); en/ja/zh-TW strings; `FactionNodeEditorTests` (state map + row display props). |
>
> Tests: **335 → 346 pass** (+11). Debug build clean (0/0). **Not yet done: in-app run-through (open dialog → verify owner names resolve, filter/select, Set selected → Active → reload persists + HMAC ok) and in-game confirm a discovered stronghold shows revealed.** AOT publish smoke also still pending from the prior feature.
>
> ## ✅ Prior session — what shipped (2026-05-31 — Three inventory/UX improvements)
>
> User directive (three rough edges to smooth): adding an item is non-obvious; bulk fills overshoot huge-cap items; sealed-abyss bulk-complete is all-or-nothing.
>
> | Area | Scope |
> |---|---|
> | **(1) Discoverable Add-Item flow** | `ItemPickerViewModel` gained a top-action-bar mode (`ShowTopActionBar` init flag + `SelectedRow` two-way + `TargetDescription`/`CanAddToTarget` pushed live from the main window + `AddActionText`/`AddSelectedCommand`). `ItemPickerRow.DisplayLabel` = `"Eng / Secondary"`. `MainWindowViewModel` exposes live `CanAddItemToCurrentList` + `AddItemTargetDescription`, recomputed in a new `RecomputeAddItemTarget()` hooked into `NotifyNavigationChanged()` + `OnSelectedElementChanged`. A new per-row **"+ Add Item…"** button (`MainWindow.axaml`, visible on `IsSingleFillCandidate` rows) + `OnAddItemFromRowClick` sets the row as clone template and opens the picker via a shared `OpenAddItemPicker(vm)` (also used by Tools → Browse Items). The code-behind subscribes to `vm.PropertyChanged` to live-sync the picker bar, unsubscribing on `Window.Closed`. The Sockets gem-picker (`OpenGemPicker`) leaves `ShowTopActionBar` default `false` → per-row "Pick" unchanged. |
> | **(2) Bulk-fill cap (9,999,999)** | `TryComputeTargetStack` (now `internal static`) takes a `bool capLarge`: when set, leaves `current > 9,999,999` alone, clamps any target above the cap to it, and skips no-op/backwards writes. Threaded through `TryBuildSingleCandidate` + `CollectStackFillCandidates`. Call-site mapping: `FillAllStacksAcrossInventoriesAsync` + container `Fill stacks` → `capLarge: true`; single `Fill stack` → `capLarge: false`; `Set to Max` (`FillSelectedFieldToMaxStack`) untouched (never routed through here → true max). Confirm-dialog text updated. |
> | **(3) Sealed Abyss preview dialog** | New `SealedArtifactChallengeViewModel` + `SealedArtifactChallengeWindow` modeled on the Knowledge editor (checkbox rows, search filter, Select all / Unselect all / Invert, all ticked by default). The old `BulkCompleteHeldSealedArtifactChallengesCommand` was split into reusable `internal ScanSealedArtifactCandidates()` + `internal ApplySealedArtifactChallengesAsync(contexts)` (behavior-preserving extraction of the deferred-redecode apply loop); the menu now opens the dialog via `OnCompleteSealedArtifactChallengesClick`, and "Complete selected" applies Pattern B v1 to only the ticked challenges then re-scans. `BulkSaPreview` + `CurrentChallengeContext` promoted `private`→`internal` for the dialog VM. The per-row "Mark Challenge Complete" button is untouched. |
> | **Strings + tests** | New en/ja/zh-TW keys (`ElementsColAddItem`/`ElementsAddItemButton`/`ElementsAddItemTip`, `SealedArtifact*`). `InternalsVisibleTo("CrimsonAtomtic.Tests")` added to the Ui csproj. New `StackFillCapTests` (cap behavior) + `ItemPickerRowTests` (DisplayLabel). |
>
> Tests: **322 → 335 pass** (+13), Debug build clean (0 warnings / 0 errors). Run tests with `dotnet run --project src/CrimsonAtomtic.Tests` (this SDK rejects the legacy `dotnet test` VSTest path for MTP). **Not yet done this session: in-app run-through of all three features against a live save, and an AOT publish smoke (`build.cmd publish`) — UI-only change, but worth confirming before a release tag.**
>
> ## ✅ Prior session — what shipped (2026-05-29 — Editor aligned to game 1.09)
>
> User directive: "1.09版出來了，修正Editor版本為對齊 1.09版" — game patched to 1.09; align the editor's parser target.
>
> Findings before touching anything: the live install's `meta/0.paver` now reads `01 00 09 00 00 00 24 48 f3 bb` (major 1, minor 9, patch 0, build `0xbbf34824`). Both `vendor/crimson-rs` and source `D:\Github\crimson-rs` were **already** at `0619789` ("validate toolkit on Crimson Desert 1.09") — 1.09 is a content-only delta over 1.08 with no schema drift, so the Rust foundation was ready and **no vendor refresh / rebuild was required** (the built `crimson_rs.lib`/`.dll` already reflect it). Per-table key deltas vs 1.08: character +6, skill +5, knowledge +5, factionspawn +1, gimmick −8; other 25 unchanged.
>
> | Area | Scope |
> |---|---|
> | **`ParserTargetMinor` 8 → 9 + `CompatibleMinors` allow-list** | `src/CrimsonAtomtic.RustInterop/NativePaverReader.cs` — `ParserTargetMinor` (the latest/displayed target) bumped to 9, and the gate changed from a strict single-minor equality to an allow-list: `public static readonly ushort[] CompatibleMinors = [8, 9]` with `IsCompatibleWithParser => Array.IndexOf(CompatibleMinors, Minor) >= 0` (AOT-safe, no LINQ). 1.08 and 1.09 share a byte-identical iteminfo schema (crimson-rs `0619789`), so both load fine and a user mid-update from 1.08 isn't warned; 1.07-and-earlier (different iteminfo layout) and any not-yet-validated future minor stay flagged. `GameVersionMismatchDialog`'s target string auto-derives from `ParserTargetMinor` (`$"1.{ParserTargetMinor:D2}.xx"` → "1.09.xx"), so no dialog edit needed. |
> | **Paver tests re-pinned** | `src/CrimsonAtomtic.Tests/NativePaverReaderTests.cs` — happy-path now pins the 1.09 stamp (minor 9, build `0xbbf34824`, "1.09.00"); `TryReadFromBytes_PreviousMinor_StaysCompatible` pins 1.08 as still-compatible (shared schema), and new `TryReadFromBytes_FutureMinor_FlagsIncompatible` (synthetic 1.10) locks the allow-list's upper bound so we don't drift into accept-everything-≥8. Live-install pin (`TryReadFromInstall_LiveInstall_PinsCurrent`) only checks Major, so it already survived the bump. The live-install catalog tests (`KeyInfoCatalogsTests`, `StringInfoCatalogTests`) use lower-bound `>=` / round-trip checks, so the table deltas (incl. gimmick −8) didn't break them. |
> | **Docs** | Root `CLAUDE.md` "currently 1.07" → "1.09". `docs/game-versions.md`: current install → 1.09, resolved the 6-trailing-bytes paver-layout TODO (fully decoded: `(major,minor,patch)` u16 + `build` u32 LE), added the 1.09 stamp example, and extended the diffing playbook with the 1.07→1.08 / 1.08→1.09 data-only history. |
>
> Tests: **301 → 303 pass** (+2: `_PreviousMinor_StaysCompatible` and `_FutureMinor_FlagsIncompatible`). Debug build clean (0 warnings / 0 errors). Live-install tests ran (0 skipped) against the real 1.09 install. AOT publish NOT re-run this session — no native or publish-shape change; worth a smoke `build.cmd publish` before the next release tag.
>
> ### Open follow-ons noted during this session
>
> - **`ParserTargetMinor` / `CompatibleMinors` still hard-coded C#-side** — the part-17 drift hazard stands: promote to a `crimson_parser_target_gamedata_minor()` (and a compatible-set) ABI so the values aren't duplicated between Rust and C#. Even lower-friction now that we've done one more manual bump + an allow-list.
>
> ### Feature-parity backlog vs `CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS` (audit 2026-05-30)
>
> Reference editor = Python/PySide6 at `D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`. Plain comparison for backlog planning. Legend: ✅ dedicated feature · 🔸 partial / only via the generic field-tree editor · ❌ absent · ❓ unconfirmed. Direction for **game-data-driven mount/character unlock**: enumerate mountable charKeys from `characterinfo`/`mercenaryinfo` (both already parsed + C-ABI bridged), then `ListCloneElement` an existing mercenary + `SetScalarField` the new `_characterKey`/`_mercenaryNo`, instead of the reference's hardcoded per-mount hex templates.
>
> **⚠️ Blocker found — 2026-05-30 clone experiment (throwaway harness, since deleted).** Mechanics + discovery all proven against live slot104: `MercenaryClanSaveData` = block 10, `_mercenaryDataList` = field#1 with 96 elements, per-element `_characterKey`=#0 / `_mercenaryNo`=#2 (matches the in-app screenshot), source Rokade (charKey 31378) at element[8], freeMercNo = max(3475)+1 = 3476. **But `ListCloneElement` fails with `LIST_VARIANT_UNSUPPORTED`**: `_mercenaryDataList` uses the `marker_run_plus_zeros` object-list header variant (run of `01` markers → `00` → u32 count → 13 zero bytes; see `save/body/decoder.rs:803`), and the length-changing ops only support the 4 fixed-size variants — `update_object_list_count_in_header` (`c_abi/mod.rs:1615`) bails on `marker_run_plus_zeros` because `FieldValue::ObjectList` doesn't retain the marker-run length to re-emit the header. **Adding a mount requires growing this list, so it is hard-blocked until crimson-rs supports the variant for clone/insert/remove.** Fix = upstream `D:\Github\crimson-rs` (dev) change: capture marker-run length at decode into `FieldValue::ObjectList`, re-emit it on encode, patch the u32 count at offset `run_len+1`, add roundtrip + clone/insert/remove tests on a `marker_run_plus_zeros` list, then `vendor/update_vendors.ps1` + rebuild dll. Settles the earlier "Rust vs editor?" question: **crimson-rs first, then the C# UI.**
>
> **✅ RESOLVED — same session (2026-05-30).** Turned out simpler than the comment in `update_object_list_count_in_header` feared: `FieldValue::ObjectList` *already* keeps `header_bytes` verbatim, and the `marker_run_plus_zeros` header is fixed at the TAIL (`[01…][00][u32 count LE][13 zero bytes]`), so the count is always the u32 17 bytes before the end of `header_bytes` — no struct/decoder/encoder change needed, just a tail-anchored count patch in the count-update fn. crimson-rs `dev` commit **`858cd30`** ("feat(c-abi): support marker_run_plus_zeros in length-changing list ops") + 2 tests (live-save clone→remove byte-roundtrip on the marker list + a pure-logic offset guard incl. a `01`-pad case); `cargo test` list-ops all pass, clippy clean (`c_abi,python -D warnings`). Vendored via `update_vendors.ps1` (vendor now at `858cd30`), dll rebuilt. **End-to-end C# probe now passes**: clone Rokade[8] → append[96] → set `_characterKey=1003918` (Silver Fang/Wolf) + `_mercenaryNo=3476` → write → reload → HMAC ok, list 96→97, new element verified. The verified save was placed in **slot104** for an in-game check (original backed up to `%TEMP%\slot104-save.premount-backup.save`; slot105 left pristine). **Next: in-game load to confirm the engine spawns a usable mount** (caveat: Rokade base — if broken, clone an existing *mount* for a richer field shape), then the **C# Mount-Unlock UI** (game-data-driven charKey enumeration via `characterinfo`/`mercenaryinfo`, reusing `ListCloneElement` + `SetScalarField`).
>
> **🧪 In-game findings — 2026-05-30 (mount insertion is NOT just a charKey swap).** Multiple slot104 in-game loads (all against the real on-disk save, reset from pristine slot105 each time; user confirmed via hashes):
> - **Round 1** — clone Rokade (charKey 31378, a *unique* special mount, also user-renamed to "AE86") → re-key Wolf (1003918): **CTD**.
> - **Round 2/3** — clone Herspia (charKey 1003120, a *normal* Tiuta horse, no custom name) → re-key Wolf: **still CTD**. Rules out the humanoid/name/stale-base theories.
> - **Round 4 (control)** — clone Herspia, **charKey unchanged** (duplicate horse, fresh `_mercenaryNo`): **loads without CTD, but no new mount appears in the stable.**
> - **Read-only save probe** (`MountProbe`, since deleted): the save has **26 block classes and NO separate stable/mount/vehicle/roster structure** — only `MercenaryClanSaveData`. The working mount Rokade is referenced outside `_mercenaryDataList` *only* by `QuestSaveData._stageStateData[..]._connectCharacterList` (×2) and `ContentsMiscSaveData._alertHistorySaveDataList` — neither a roster. So the **stable is derived directly from `_mercenaryDataList`** (no second structure to update).
>
> **Conclusions:** (1) the marker_run fix + insert mechanism are sound — a cloned element yields a loadable save; (2) the stable **de-dups by charKey**, so the duplicate-Herspia control showed no new mount (inconclusive-by-design); (3) **re-keying an element to a foreign-species charKey CTDs** — the element's content (`_levelData`/ExperienceLevelSaveData + others) must be consistent with the charKey. Corroborated by the reference editor: its *confirmed* mounts each shipped a **real captured per-mount hex element**; its charKey-swap-on-generic-template mounts were **untested** (and, per our result, don't work). **Therefore adding a mount the player lacks needs that mount's REAL element content, not a charKey swap.** Open options: (A) capture a real element from a save that contains the target mount; (B) port `vehicleinfo`/`characterinfo` mount data to construct correct elements from game data (large); (C) adapt the reference's hardcoded per-mount templates (fast, version-fragile, needs type-index patching). Diffing Rokade-vs-Herspia won't reveal wolf requirements (both are working mounts; difference is special-vs-normal-*horse* config — Herspia's minimal field set already works).
>
> **✅ MOUNT UNLOCK SOLVED — 2026-05-30 (transplant + knowledge).** Recipe = **(1) graft the target mount's REAL element** from a save that owns it via the new `crimson_save_transplant_list_element` (source crimson-rs `501a493`, C# `NativeSaveLoader.TransplantListElement`) — remaps type-indices by class name, so it works cross-content-schema (downloaded save's embedded schema = 99 types, user's = 101; both 1.09 — the count differs by CONTENT, not version), **plus (2) inject the mount's riding/knowledge keys** into `KnowledgeSaveData._list`. Validated end-to-end on the user's live save with the **dragon (charKey 1000799, "深暗之星"/Blackstar)** from `vendor/savedata/slot102` (a downloaded save): element-only → mount registers (icon/name/stats in the quick-menu) but **"cannot summon"**; element + 32 missing dragon-knowledge keys (reference editor's confirmed 187-key set; user already had 155) → **summons + rides in-game.** So a special mount = real element + its knowledge; charKey-swap is dead (use transplant). Wolf (1003918) is presumably the same shape but needs a *source save that contains a wolf* (not present in the user's saves nor the downloaded one) — or the reference's captured hex.
>
> **Knowledge injection needs NO new ABI** — it's pure composition of existing primitives (`ListCloneElement` + `SetScalarField`, same as the shipped "Unlock All Abyss Gates"). The Rust foundation is complete (marker_run fix + transplant). Productization = a reusable C# `InjectKnowledgeKeys(keys)` helper (extract the inline Abyss-Gates loop in `MainWindowViewModel`) shared by abyss + mount-unlock, plus per-mount data (element source save + knowledge key set — hardcode from the reference, or derive by diffing a source-that-has-it vs the target's `KnowledgeSaveData._list`).
>
> **🦊 Creature mounts (wolf/bear) need ANIMAL-riding knowledge — 2026-05-30.** The dragon (charKey 1000799, a *Vehicle*-type mount) works with the reference's dragon knowledge set. Wolf (1003918) + Wild Bear (1000270) transplanted the same way both summon the WRONG entity (the player's current active mount, e.g. a balloon) instead of themselves. Ruled out by experiment: source ownership (the bear came from a clean `owned=1`, `present=16` slot100 entry with `_isMainMercenary`/`_lastSummoned` already set — still failed), state flags (added to the wolf — still failed), cooldown timers (zeroed — still failed). The cause is **knowledge**: the reference's dragon knowledge set is explicitly "Riding/dragon/skills/UI… **No … animals**", so it does NOT include the creature/animal-riding knowledge a wolf/bear needs. Confirmed the user's repeated hypothesis. **Next: find the per-creature animal-riding knowledge keys** — diff a source save that has the creature tamed (slot100 has "all bears/wolves") against the target's `KnowledgeSaveData._list`, resolve names via `LocalizationProvider` (knowledgeinfo), filter to creature/riding/living-prefixed keys, inject those. (Caveat: slot100 is a full-achievement save, so the raw knowledge diff is huge — must filter by name, not inject wholesale, to avoid unlocking achievements.) Also note: editing a mount then switching the active mount IN-GAME re-saves and can overwrite patched timer/state fields.
>
> **🗝️ BREAKTHROUGH — special creature mounts are gated by a "Sigil of Solidarity" ITEM, not the element — 2026-05-30 (user-found).** Silver Fang etc. are quest-chain rewards: the chain ends with crafting a `Sigil of Solidarity (<mount>)` item at a Witch, and **USING that item** triggers the game's own full unlock (summon + ride + knowledge). Our element-transplant + knowledge made the wolf *appear* in the special-mount wheel (icon/name/riding-knowledge "風行狼" all showed) but it would not summon ("呼叫需要冷卻時間" / stuck "召喚中") — because the engine's summon path checks the sigil/quest, which the structural insert doesn't satisfy. **So the clean, game-legitimate path for these mounts is: add the Sigil item to inventory, then use it in-game** — far simpler than transplant+knowledge+state-patching. Sigil item keys (from iteminfo): White Bear `1003843`, **Silver Fang `1003844`**, Snowwhite Deer `1003845`, Icicle Edge Alpine Ibex `1003846`, Rock Tusk Warthog `1003847`, Phoenix `1003921`. Our editor's existing **Add-Item** (ListCloneElement an inventory element + SetScalarField `_itemKey`) adds the sigil cleanly — no CE/buyback hack needed. The reference Python editor never did the sigil path — it force-inserts the mount element + knowledge structurally (what we replicated), which is why it (and we) can make the mount show but not necessarily summon a sigil-gated one. **Recommended mount-unlock design: for sigil-gated mounts, grant the Sigil item; the dragon-style element+knowledge transplant remains for non-sigil cases.**
>
> **✅ ALL SIGIL MOUNTS CONFIRMED WORKING — 2026-05-30.** All six sigil-gated mounts summon/ride in-game after the user used the sigil: 純白鹿/Snowwhite Deer, 岩牙紅野豬/Rock Tusk Warthog, 寒霜白熊/White Bear, 寒霜羱羊/Alpine Ibex, 銀色獠牙/Silver Fang (特殊坐騎), plus 鳳凰/Phoenix (寵物). Two corrections to earlier notes: (1) **CE item-editing is perfectly viable** on a clean save (it is NOT inherently crude/corrupting — only gem-socket edits need extra care); (2) the "data corrupt / sigil unusable" we saw came specifically from CE-adding a sigil **on top of the heavily element-transplanted slot104** (the structural mount/knowledge inserts conflicted) — on a clean save (slot102 copied from pristine slot105) the CE'd sigil works fine, and after use it was saved to slot101. **Container placement matters**: the sigil lives in **Quest Artifacts (`_inventoryKey=5`)**, not the main Backpack (`key=2`) or Camp & Contributions (`key=1`). Full container map (resolved via InventoryKey): 1=Camp&Contributions, 2=Backpack, 5=Quest Artifacts, 8=Private Storage, 10=Valuables, 20=Collectibles, 13=Kuku Pot, 16=Enhanced Kuku Cooler, 19=Gatherables Chest (others empty/virtual). The add-item recipe (proven, in `MainWindowViewModel.AddItemToCurrentListAsync`) must set `_itemKey`, `_stackCount`, unique `_slotNo`/`_itemNo`, `_isNewMark`, **and `_transferredItemKey = ((itemKey & 0xFFFF) << 16) | 0x0101`** (the engine cross-check field — omitting it corrupts; e.g. 1003844 → 0x51440101). **Next session: (B) diff slot101 (sigil-used) vs slot105 (pristine) to capture the exact legitimate-unlock footprint; build the Mount-Unlock UI** (sigil-gated → grant Sigil item into Quest Artifacts + tell the user to use it in-game; non-sigil → element+knowledge transplant).
>
> **✅ (B) SIGIL-UNLOCK FOOTPRINT CAPTURED — 2026-05-31 (slot101 sigil-used vs slot105 pristine).** Flattened both saves with `tools/analyze/dump_save_fields.py --include-array-elements` (325,389 vs 325,390 rows, 1107 blocks each) and field-level-diffed by `(top_block, path)` (throwaway diff in `out/analyze/2026-05-31_sigil_diff/`, gitignored). **The entire net footprint of a legitimately-used Silver Fang sigil is just TWO appended elements — no schema change, no knowledge change:**
> - **(1) `MercenaryClanSaveData._mercenaryDataList` +1 element** (block 10, list 96→97). The engine-written element[96] for Silver Fang: `_characterKey=1003918`, **`_mercenaryNo=1000002`** (← the engine assigns sigil/creature mounts a number in the **1000000+ range**, NOT `max(normalNo)+1`; it was the ONLY entry ≥1000000 among 126 — all bred/recruited mercs are <1000000), `_ownedCharacterKey=1`, `_occupationState=1`, `_currentHp=494784544309249` (packed TStat), `_currentMp=0`, full state flags `_isInitialize=1 / _isMainMercenary=1 / _lastSummoned=1`, `_lastBreedingTime=_lastPaidTime=7664699040`, `_spawnFieldInfoKey=1`, `_spawnPosition` (float3), `_spawnYaw`. Side-effect: the previous main mount (element[6]) **lost** its `_isMainMercenary`/`_ownedCharacterKey` fields (became absent = de-main'd, because the new mount auto-became main).
> - **(2) `ContentsMiscSaveData._alertHistorySaveDataList` +1 element** (block 14) — the "new mount acquired" toast record: `_alertType=16`, `_characterKey=1003918`, `_generatedLocalTime`, `_saveVersion=2`.
>
> **Key conclusions:** (a) **NO `KnowledgeSaveData` change at all** — sigil-gated creature mounts do **not** need the knowledge injection the dragon required; riding comes with the element/quest. Big simplification. (b) **The sigil item leaves ZERO inventory trace** — itemKey `1003844`/`0x51440101` is absent from the diff (consumed on use). (c) **Everything else in the diff was pure play-session noise**: `FactionSaveData` 694× `_lastRevivedFieldTimeRaw`, `Inventory/Store/Equipment` `_chargedUseableCount` charge timers, other mounts' `_spawnPosition`/`_spawnYaw` drift, player HP/field-time. None unlock-related. (d) The engine's `_mercenaryNo=1000002` (1000000-range) likely explains why our earlier manual transplant (used `max+1=3476`) registered the wolf but couldn't summon it — wrong number space, on top of the sigil/quest gate. **Productization takeaway for the Mount-Unlock UI:** the sigil-grant path stays the cleanest *user-facing* route, but we can now ALSO replicate the unlock structurally without the in-game step — append the merc element (charKey + a fresh 1000000-range `_mercenaryNo` + the state-flag set above) and an `_alertHistorySaveDataList` record; no knowledge work needed for the 6 sigil mounts. **Next: build the Mount-Unlock UI.**
>
> **✅ MOUNT-UNLOCK UI SHIPPED — 2026-05-31 (dragon + Sigil of Solidarity series).** Tools → **Unlock Mounts…** dialog (`MountUnlockWindow` + `MountUnlockViewModel`, mirrors the Edit Abyss Gates pattern, launched via `OnUnlockMountsClick`). Lists the 7 catalog mounts, one "Unlock" button per row:
> - **6 sigil mounts** (White Bear, Silver Fang, Snowwhite Deer, Alpine Ibex, Rock Tusk Warthog, Phoenix) → **grant the Sigil of Solidarity item into Quest Artifacts** (`_inventoryKey=5`), then the user uses it in-game (the engine does the full unlock). New `MainWindowViewModel.GrantItemToContainerAsync(inventoryKey, itemKey)` — a container-targeted generalization of `AddItemToCurrentListAsync` (same `_transferredItemKey=((k&0xFFFF)<<16)|0x0101` recipe).
> - **Dragon** (charKey `1000799`, Blackstar) → **transplant the real merc element + inject knowledge**, fully in-editor (no in-game step). The donor is a **bundled embedded asset** (`Assets/mounts/dragon_donor.save` = the downloaded slot102, `EmbeddedResource` logical name `CrimsonAtomtic.Ui.Assets.mounts.dragon_donor.save`); at runtime it's written to a temp file (the save loader is file-path only), loaded as a source `NativeSaveLoader`, and the dragon element (located by charKey) is grafted via `ISaveLoader.TransplantListElement` (promoted from concrete-only to the interface, takes `ISaveLoader source` + casts). Post-graft the element is re-numbered (fresh u64 `_mercenaryNo`) + de-mained (`_isMainMercenary=0`).
> - **Dragon knowledge = the reference's 187-key "no-quests" set** (`Services/MountCatalog.cs::DragonKnowledgeKeys`). **CORRECTION (2026-05-31, same day):** a first pass wrongly pruned this to 2 keys (`1000560`+`1000174`) by re-surveying the donor with a name filter — the dragon then **showed an icon but would not summon** (user-reported). The actual proven set is the reference editor's `_unlock_dragon_mount_no_quests()` list (`CrimsonSaveEditor/gui.py:9654`), which is **exactly 187 keys** — reconciling the prior session's "187-key set; user had 155". That same function ALSO proves the dragon element is a **~140-byte hardcoded hex blob** (`DRAGON_HEX` at `gui.py:9578`) with type-indices remapped by class name — i.e. the whole-save 1.47 MB donor embed is NOT required. Pinned by `MountCatalogTests` (`Assert.Equal(187, …)`).
>   - **✅ EMBED REMOVED (2026-05-31, after in-game dragon confirm) — NO vendor work needed.** Turned out `ISaveLoader.ListInsertElement` (insert a caller-supplied element-bytes blob, decoded+validated against the loaded schema) is enough — the only thing `transplant_list_element` added was class-name → type-index remapping, which we now do in C#. Captured slot102's real dragon element (212 bytes, `MountCatalog.DragonElementHex`) + a type-index fixup table (`DragonElementTypeIndexFixups`: offsets 8/46/68/94/120 → Mercenary/ExperienceLevel/FriendlyDailyCount; positions found via the decoder's own `data_offset`+`mbc` for the main object and the `ffffffff`-sentinel −3 rule for the locator-wrapped nested ones). At unlock, `InsertDragonElementAsync` reads THIS save's type-index for each class from its own merc elements (`CollectClassIndices`), rewrites the u16 at each offset, then `ListInsertElement`s. The stale slot102 payload-offsets in the blob are tolerated on decode and canonicalized by the encoder on write (PR B.6). `MainWindowViewModel.InsertDragonElementAsync` replaces `GraftDragonElementAsync`; `dragon_donor.save` + the csproj `EmbeddedResource` + `DragonDonorResourceName` are gone. AOT exe 28.5 → 27.0 MB. Loader round-trip pinned by `MountUnlockMechanicsTests.InsertDragonElement_RemapsTypeIndices_FillsHp_AndSurvivesRoundTrip` (remap → insert → HP fill → HMAC reload; asserts the nested objects resolve to the right classes, proving the remap). Timestamps are NOT a summon gate — the dragon block was purely knowledge, not a game-time/cooldown issue.
> - **Dragon HP fill (2026-05-31).** The donor's dragon is captured mid-fight (1038/2500), so `MainWindowViewModel.FillDragonHpAsync` heals it to full after graft. `_currentHp` is a packed TStat — the donor's dragon is `01 00 01 01 01 [u16 current] 00` (the `08 04`=1032 u16 ≈ the in-game 1038); max isn't even in the field. The current-HP slot was cross-confirmed by the reference's full-HP `DRAGON_HEX` carrying `c4 09`=2500 at the analogous spot. The fill overwrites ONLY that inner u16 to `MountCatalog.DragonFullHp` (2500) and only when the bytes match the exact known donor shape (else it skips — never corrupts an unknown layout). Runs on both the fresh-transplant and the already-present (repair) paths, so a save from the earlier broken run gets healed on re-run. Loader round-trip pinned by `MountUnlockMechanicsTests.FillDragonHp_WritesFullAndSurvivesRoundTrip` (write current=2500 → HMAC ok → reads back full). The leaner in-place fix was chosen over the hex-insert ABI because the HP slot is now cross-validated (no longer a blind write); the embed-removal stays parked (above).
> - **Refactor:** extracted `ResolveKnowledgeList` + `ApplyKnowledgeInjectAsync` from `UnlockAllAbyssGatesAsync` (behavior-preserving); both the abyss flow and the dragon path now share them.
>
> Files: `Services/MountCatalog.cs` (catalog + dragon keys + container/charKey constants), `ViewModels/MountUnlockViewModel.cs`, `Views/MountUnlockWindow.axaml(.cs)`, donor asset + csproj embed, `ISaveLoader.TransplantListElement`, menu item + en/ja/zh-TW strings. Tests: `MountCatalogTests` (catalog integrity + embedded-donor presence) + `MountUnlockMechanicsTests` (loader-level end-to-end: extract donor → transplant dragon into a live save → list grows by 1, tail = dragon → write + reload → **HMAC ok**, ran live against slot105). **310 tests pass (0 skipped), Debug clean, AOT publish clean (single 28.5 MB exe).** **Still needs the user's in-game confirmation** that (a) granted sigils are usable and (b) the transplanted dragon summons + rides — the mechanics + round-trip are proven, the in-game gate is not yet re-verified this session.
>
> **✅ KNOWLEDGE EDITOR SHIPPED — 2026-05-31 (Tools → Edit Knowledge, user-confirmed in-app).** Per-category / per-item knowledge learning — deliberately NOT a blunt "learn all" (which would trip codex/achievement state). `Views/KnowledgeEditorWindow` + `ViewModels/KnowledgeEditorViewModel` (+ `KnowledgeRow`): enumerates all ~5,500 `knowledgeinfo` entries (key + internal name + resolved display name on a bg thread), buckets each into one of **16 curated categories** by the `Knowledge_<Prefix>_…` token (else "Other") — the same set the reference editor uses — and shows a filterable, **checkbox-per-row** table (category dropdown + search + learned/unlearned toggles + Select all / Unselect all / Invert). "Learn selected" injects ticked-and-unlearned rows; "Learn all in category" injects the current view's unlearned rows with a **warning confirm for map-reveal `Node` / codex `Collection` sets**. Inject routes through new `MainWindowViewModel.LearnKnowledgeAsync` + `GetLearnedKnowledgeKeys`, reusing the abyss/mount `ResolveKnowledgeList` + `ApplyKnowledgeInjectAsync` primitives — **no vendor work, no new ABI**. (Categories are derived from the internal-name prefix, not `knowledgegroupinfo.pabgb` — the game's true category bridge stays unbuilt, only needed if we ever want the exact in-game codex tabs.) Menu + en/ja/zh-TW strings; `KnowledgeEditorTests` pins the `CategoryFor` bucketing. **322 tests pass, Debug clean.** Bugfix during review: "Learn all in category" did nothing because `_selectedCategory` used `NotifyPropertyChangedFor` instead of `NotifyCanExecuteChangedFor` (the command's CanExecute never refreshed) — fixed.
>
> **Save-side editing**
>
> | Feature | Reference | Ours |
> |---|---|---|
> | Generic field/block-tree editor (edit any scalar) | ❌ | ✅ |
> | Inventory: list / add / remove / itemKey swap / stack count | ✅ | ✅ |
> | Bulk fill max stacks | ✅ | ✅ |
> | Sockets / gems | ✅ | ✅ |
> | Dye / cosmetics | ✅ | ✅ |
> | Vendor buyback / repurchase | ✅ (also add/clone to vendor) | ✅ list/remove + jump-to-inventory (≈ inventory itemKey edit) |
> | Mercenary rename | ✅ | ✅ |
> | Abyss Gates unlock | ✅ | ✅ |
> | Sealed Abyss artifact challenge complete | ❓ | ✅ |
> | Auto-backup + restore | ✅ (+ pristine reference) | ✅ |
> | Change review / undo | ✅ | ✅ |
> | Equipment enchant level | ✅ | 🔸 field edit |
> | Mount unlock (sigil mounts + dragon) | ✅ (hardcoded hex) | ✅ (Tools → Unlock Mounts: sigil-grant + dragon bytes-insert/remap + 187-key knowledge + HP fill) |
> | Quest state / stage editing | ✅ (Quest Editor + Quest DB) | 🔸 field edit |
> | Knowledge / codex learn–unlearn | ✅ (all categories) | ✅ (Tools → Edit Knowledge: 16 curated categories, search, per-item/per-category learn) |
> | Faction nodes (discover / set state) | ✅ | ✅ (Tools → Edit Faction Nodes: checkbox + filter + bulk set-state / Discover all) |
> | Reveal map / fog of war | ✅ | ❌ (World Map view-only) |
> | Item packs (share / import / export) | ✅ | ❌ |
> | Auto-find save across launchers | ✅ (Steam/Epic/GamePass/Proton) | 🔸 picker + manual install path |
> | Multi-language UI (en/ja/zh-TW + secondary) | 🔸 | ✅ |
> | Game-data version detect + mismatch warn | ❌ | ✅ |
> | Icon extraction / cache | ✅ | ✅ |
>
> **Game-data ("Mods") editing — writes modified tables back into game PAZ; we currently treat game data as read-only reference**
>
> | Feature (table edited) | Reference | Ours |
> |---|---|---|
> | ItemBuffs — iteminfo stats / buffs / enchant, transmog | ✅ | ❌ (iteminfo parsed read-only) |
> | Stores — storeinfo prices / stock | ✅ | ❌ (name lookup only) |
> | DropSets — dropsetinfo loot tables | ✅ | ❌ (no parser) |
> | SpawnEdit — spawn density | ✅ | ❌ (no parser) |
> | Skills — skill.pabgb params | ✅ | ❌ (skill parsed read-only) |
> | FieldEdit — fieldinfo / vehicleinfo (mounts-everywhere, invincible, killable NPC) | ✅ | ❌ (no parser) |
> | Storage expansion (inventory.pabgb patch) | ✅ | ❌ |
> | Low-level PABGB browser | ✅ | 🔸 Python inspect tools (read-only) |
>
> The whole game-data-mods column is a direction decision, not just features: the Rust core parses iteminfo + skill byte-perfect but exposes the rest (characterinfo / storeinfo / mercenaryinfo / gimmickinfo) only as name-lookup bridges; vehicleinfo / fieldinfo / dropsetinfo / spawn tables have no parser yet.
>
> ### Vendor state
>
> `vendor/crimson-rs` at `7191a7d` (in sync with source `D:\Github\crimson-rs`, branch `dev`). Bumped this session: `7191a7d` adds the read-side `crimson_save_get_inline_bytes_field` FFI (counterpart to the inline-bytes setter; used to read back `_mercenaryName`). Still targets 1.09 — content unchanged.
>
> ---
>
> Last previous update: 2026-05-23 part 17 (Game-data version detection — vendor `61c2f52` adds a `meta/0.paver` reader exposed as `crimson_paver_read_from_file` / `_read_from_bytes`. C# `NativePaverReader.TryReadFromInstall` + new `GameDataVersion` record-struct; `LocalizationProvider.GameDataVersion` getter populated at bootstrap. App startup now checks `Minor == ParserTargetMinor` (currently 8) before the disclaimer; a new `GameVersionMismatchDialog` warns the user with Continue / Quit when the install is e.g. 1.07 against a 1.08-targeted parser. World Map parchment layer-alignment bug from part 14 still open — unchanged this session).
>
> ## ✅ This session — what shipped (2026-05-23 part 17)
>
> User question: "新的讀取 1.08 的程式，讀取 1.07 會 crash，那有沒有什麼方式可偵測遊戲資料版本，提供 ABI?" — find a way to detect game-data version so the C# side can warn before iteminfo / save-body parsing crashes.
>
> Answer found in `docs/game-versions.md`: every Crimson Desert install carries a 10-byte `meta/0.paver` version stamp. Layout decode confirmed against the live 1.08 install:
>
> ```text
> 1.08.00 build 0xdc39b03e:  01 00 08 00 00 00 3e b0 39 dc
>                            └─┬─┘ └─┬─┘ └─┬─┘ └────┬────┘
>                            major minor patch    build (LE u32)
> ```
>
> `minor` is the schema-compatibility key: 1.08 vs 1.08.01 share minor=8 (compatible), 1.07 has minor=7 (incompatible).
>
> | Area | Scope |
> |---|---|
> | **Vendor: `Paver` parser + C ABI** (`crimson-rs` commit `61c2f52`) | `src/binary/paver.rs` is a 10-byte fixed-layout parser with `from_bytes(&[u8])` + `from_file(P)`. `src/c_abi/paver.rs` exposes two extern "C" entry points: `crimson_paver_read_from_file(path, *out_major, *out_minor, *out_patch, *out_build)` (auto-appends `meta/0.paver` when `path` is the install-root directory via `is_dir()`) and `crimson_paver_read_from_bytes(data, len, *out_…)` (in-memory variant). Error codes: `OK` / `NULL_ARG` / `INVALID_PATH` / `NOT_FOUND` / `IO` / `BODY_PARSE` / `PANIC`. Outputs untouched on failure. 10 new tests (5 binary + 5 c_abi); upstream cargo test `311 → 321`, clippy clean. |
> | **C# binding** | `NativePaverReader.TryReadFromInstall(string?)` → `GameDataVersion?` and `TryReadFromBytes(ReadOnlySpan<byte>)` thin wrappers. `GameDataVersion` is a `readonly record struct (Major, Minor, Patch, Build)` with `IsCompatibleWithParser`, `DisplayString` (`"1.08.00 build 0xdc39b03e"`), and `ShortVersionString` (`"1.08.00"`). `ParserTargetMinor = 8` constant baked into the C# side — bump this on the next vendor refresh that targets 1.09. P/Invokes live next to `crimson_paloc_load_from_file` in `NativeSaveLoader.NativeMethods`. |
> | **Bootstrap wiring** | `LocalizationProvider.GameDataVersion` getter populated inside `TryBootstrapFromGameRoot` as the very first step (before iteminfo / PALOC). Read failure leaves the property `null` and bootstrap proceeds — the iteminfo `try`/`catch (CrimsonSaveException)` path already degrades gracefully, so a 1.07 install loaded against a 1.08 parser still yields a startable editor minus item-name resolution. |
> | **`GameVersionMismatchDialog`** | New modal dialog (sibling of `DisclaimerDialog`): headline, "detected install" + "parser targets" labels, explanation paragraph, Continue / Quit buttons. Shown via `ShowIfMismatchedAsync(owner, detected)` — skips itself when `detected` is null OR compatible. The hosting `App.axaml.cs` runs it on `mainWindow.Opened` BEFORE the disclaimer (so users on a mismatched install can quit without first being nagged about the legal text); Quit calls `desktop.Shutdown()`. |
> | **Bilingual strings (en / ja / zh-TW)** | Six new resource keys: `GameVersionMismatchTitle`, `Headline`, `DetectedLabel`, `TargetLabel`, `Explanation`, `Continue`, `Quit`. Wording emphasises "Continue at your own risk" / 「リスクは自己責任で」 / 「請自行斟酌風險」 per the user's directive for warn-then-continue (not hard-block). |
>
> Tests: **296 → 301 pass** (+5 — `TryReadFromBytes_HappyPath_Returns_1_08_Live`, `_ShortBuffer_ReturnsNull`, `_LegacyMinor_FlagsIncompatible`, `TryReadFromInstall_NullOrEmpty_ReturnsNullWithoutCallingNative`, `_LiveInstall_PinsCurrent`). Debug build clean. AOT publish verified — `dist\win-x64\CrimsonAtomtic.exe` 26.8 MB, single-file shape preserved (no `crimson_rs.dll`).
>
> ### Open follow-ons noted during this session
>
> - **`ParserTargetMinor` drift hazard** — the constant is duplicated between the Rust parser's implicit assumption (it targets the latest patch only, per upstream's CLAUDE.md note) and `GameDataVersion.ParserTargetMinor = 8` on the C# side. Worth promoting to a `crimson_parser_target_gamedata_minor()` ABI in a future vendor commit so the C# side stops hard-coding; the user opted for the simpler "game-data version only" ABI shape this session, so this is parked as a follow-on.
> - **Save-header version (offset 0x04 u16)** — the save file's own version field is distinct from the game-data version and is not currently surfaced through any ABI. Useful as a cross-check ("this save was written by game version X") if a future patch breaks save-format compat too. Out of scope for this session.
> - **About / Settings version readout** — `localization.GameDataVersion?.DisplayString` is computed but not yet surfaced anywhere except the warning dialog. A status-bar field or an About dialog entry would let users self-diagnose without re-launching.
>
> ### Vendor state
>
> `vendor/crimson-rs` at `61c2f52` (was `2b1307a` per part 16). Run `vendor\update_vendors.ps1` at session start to refresh. **Note**: `crimson-rs` follows a PR-to-`main` workflow (see its CLAUDE.md); this session's `61c2f52` is pushed to `origin/dev` and a PR to `main` should be opened manually for CI green-light + merge.
>
> ---
>
> Last previous update: 2026-05-22 part 16 (Iteminfo static-metadata surface — vendor `2b1307a` opens up 28 static flags + a one-shot 80-byte `CrimsonItemInfoSummary` getter. C# now has a `[Flags] ItemInfoFlags` enum + `ItemInfoSummary` `[StructLayout(Sequential)]` struct + `NativeItemInfoCatalog.LookupSummary`; FindItems window grows a right-side detail pane that renders the active row's flag chips + key scalar fields. Closes the long-standing "why isn't `is_housing_only` visible when I look up an inventory item?" gap. World Map parchment layer-alignment bug from part 14 still open — unchanged this session).
>
> ## ✅ This session — what shipped (2026-05-22 part 16)
>
> User-directive: "vendor/crimson_rs 已經更新，依更新來改善我們的功能" — and pick the depth: full ABI + UI surface in the inventory item detail.
>
> | Area | Scope |
> |---|---|
> | **`ItemInfoFlags` + `ItemInfoSummary` C# binding** | New `src/CrimsonAtomtic.RustInterop/ItemInfoSummary.cs`. `[Flags] enum ItemInfoFlags : uint` mirrors the 28 `CRIMSON_ITEMINFO_FLAG_*` constants (bits 0–27; bits 28–31 reserved). `readonly struct ItemInfoSummary` with `[StructLayout(LayoutKind.Sequential)]` mirrors the Rust 80-byte `CrimsonItemInfoSummary` field-for-field (u64×3 → u32×9 → u16×4 → u8×8, plus 4 bytes of struct-alignment padding). Field order is byte-identical to the Rust side — pinned by a unit test calling `Marshal.SizeOf<ItemInfoSummary>() == 80`. Two new `[LibraryImport]` entries in `NativeSaveLoader.NativeMethods`: `ItemInfoLookupFlags` (returns just the 27-bit bitmask, cheaper for hot paths) and `ItemInfoLookupSummary` (one-shot fill of the whole struct via `out` parameter). |
> | **`NativeItemInfoCatalog.LookupSummary` + `LookupFlags`** | Thin `NOT_FOUND → null` wrappers on top of the P/Invokes. The catalog already exposed `LookupStringKey` / `LookupMaxStackCount` / `LookupSocketCaps` / `LookupIconPathHash`; the summary surface fills in the long tail (item_type / item_tier / equipable_level / max_endurance / cooltime / respawn_time_seconds / category_info / inventory_info + the 28 flags). `LocalizationProvider.LookupItemInfoSummary(uint itemKey)` exposes the catalog method to view models. |
> | **Live-install pin for the binding** | `LookupSummary_LiveInstall_PinsKnownItem` in `ItemInfoCatalogTests` pins two items against the 1.08 install: Pyeonjeon_Arrow (key 2200, item_type 0, **NOT** `IS_EQUIP_QUICK_SLOT_VISIBLE`) and Marni_Devotee_PlateArmor_Helm (key 14510, item_type 24, `IS_EQUIP_QUICK_SLOT_VISIBLE` set). Mirrors the upstream `c_abi_iteminfo_static_lookups_live` pin. Also asserts the `LookupFlags` and `LookupSummary` paths agree bit-for-bit (they read from the same cache) and that the `_reserved` padding byte round-trips through the marshaller as 0. |
> | **FindItems detail pane** | `src/CrimsonAtomtic.Ui/Views/FindItemsWindow.axaml` grew a fixed 320-px right column. When a `FindItemsRow` is selected, the pane shows: the item icon + English name + ItemKey; a **Static flags** section with wrap-panel chips (each chip carries a tooltip describing the flag's semantics); a **Static metadata** grid showing item_type, tier, equipable_level, max_endurance, max_stack, cooltime, respawn (s), category. Empty state when nothing is selected; explanatory "this key isn't in iteminfo.pabgb" hint for dev / content-stripped items. `FindItemsRow` is now populated at row-construction time via `LocalizationProvider.LookupItemInfoSummary` — one extra O(1) FFI call per row, negligible against the existing PALOC name resolution. The `SelectedRow` ObservableProperty on the VM drives the pane through `DataGrid.SelectedItem` TwoWay binding. |
> | **Bilingual strings (en / ja / zh-TW)** | 28 flag display labels + 28 flag tooltip explanations + 8 scalar field labels + 6 pane-level strings (title / empty / no-summary / section headers / no-flags-set) — 70 entries × 3 languages. Resource keys: `ItemFlagLabel*` / `ItemFlagTip*` / `FindItemsDetailPane*` / `FindItemsDetailSection*` / `FindItemsDetail{ItemType,ItemTier,EquipableLevel,MaxEndurance,MaxStack,Cooltime,RespawnTime,CategoryInfo}`. Loaded at chip-construction time via `Application.Current.TryGetResource(...)` (same pattern as `MarkChallengeCompleteTip` in `MainWindowViewModel`); falls back to a stable English default if the key is missing so the pane never renders a blank chip. |
>
> Tests: **294 → 296 pass** (+2 — `ItemInfoSummary_Layout_MatchesRustAbi` + `LookupSummary_LiveInstall_PinsKnownItem`). Debug build clean. AOT publish verified — `dist\win-x64\CrimsonAtomtic.exe` 26.8 MB (was 26.7 — the new summary cache + getters add ~100 KB net), single-file shape preserved (no `crimson_rs.dll` in dist).
>
> ### Open follow-ons noted during this session
>
> - **Additional UI surfaces for the summary** — the binding is now general-purpose; other dialogs that show items (Browse Items, Vendor Buyback, Socket Editor, Dye Editor) could surface the same detail pane or a flag-chip column. Left as a follow-on since the FindItems case is the user-question motivator and the rest would benefit from the same scoping conversation.
> - **Flag-based filtering in FindItems** — the user could ask "show me only dyeable items" or "show me only housing-only items". `ItemInfoFlags` is now plumbed end-to-end; adding a filter row above the DataGrid is straightforward but out of scope for this session.
>
> ### Vendor state
>
> `vendor/crimson-rs` at `2b1307a` (was `b0cbd38` per part 15). Run `vendor\update_vendors.ps1` at session start to refresh.
>
> ---
>
> Last previous update: 2026-05-22 part 15 (Staticlib pivot for AOT publish — `dist\win-x64\` no longer ships `crimson_rs.dll`; the Rust core is folded into `CrimsonAtomtic.exe` via NativeAOT's `<DirectPInvoke>` + `<NativeLibrary>`. Vendor refreshed to `b0cbd38` (1.08 baseline). GlobalGameEvent body-field bridge wired (`group_key` + `paloc_key`). World Map parchment layer-alignment bug from part 14 still open — unchanged this session).
>
> ## 🎯 This session — what shipped (2026-05-22 part 15)
>
> Two cross-cutting changes from the user's directive "vendor 有更新：對應的功能更新。另外，這次使用 library 方式 import crimson_rs (dist 不需要再帶有 crimson_rs.dll)":
>
> | Area | Scope |
> |---|---|
> | **Staticlib pivot — `dist\win-x64\` drops `crimson_rs.dll`** | Vendor `b0cbd38` (specifically `e4f77ca`) makes `cargo build --features c_abi --release` emit both `crimson_rs.dll` (cdylib, 1.1 MB) **and** `crimson_rs.lib` (staticlib, 14.3 MB) in one pass. `CrimsonAtomtic.Ui.csproj` now declares `<DirectPInvoke Include="crimson_rs"/>` + `<NativeLibrary Include="..\..\vendor\crimson-rs\target\release\crimson_rs.lib"/>` so the ILC linker folds the Rust code into the AOT exe. The existing `<Content Include="...crimson_rs.dll">` flipped to `<CopyToPublishDirectory>Never</CopyToPublishDirectory>` — dev / `dotnet run` / `dotnet test` still LoadLibrary the dll from `bin\`, but publish bundles drop it. Result: `dist\win-x64\` contains 4 files (`CrimsonAtomtic.exe` 26.7 MB, `av_libglesv2.dll`, `libHarfBuzzSharp.dll`, `libSkiaSharp.dll`) — the .NET-side single-file shape was already there, and now the Rust-side dll is gone too. Verified via `dumpbin /dependents`: the exe's import list shows only Windows system DLLs (`KERNEL32`, `ntdll`, `bcryptprimitives`, `ADVAPI32`, `bcrypt`, `ole32`, `OLEAUT32`, `api-ms-win-*`) — no `crimson_rs.dll`. **One linker gotcha not mentioned in the upstream doc**: Rust std's `env::home_dir` transitively pulls `GetUserProfileDirectoryW` from `userenv.dll`, so we also add `<NativeLibrary Include="Userenv.lib"/>` — without it the AOT publish fails with `LNK2019: unresolved external symbol __imp_GetUserProfileDirectoryW`. `scripts\package_aot.ps1` now asserts no `crimson_rs.dll` in `dist\` + runs `dumpbin /imports` for the second invariant (skipped cleanly when dumpbin isn't on PATH — needs a VS dev shell to activate). `scripts\build_rust.ps1` now reports both artifacts and fails loudly if the `.lib` is missing. |
> | **`NativeGlobalGameEventCatalog` body-field wiring** | The only un-wired ABI from the vendor refresh: vendor `2be7493` adds `crimson_global_game_event_info_lookup_group_key` (returns the row's `GlobalGameEventGroupKey` for cross-reference into the existing group bridge — universal coverage across all 103/188 rows) and `_lookup_paloc_key` (returns the 64-bit PALOC key for the row's localized display name, or 0 for the ~24 `RoyalSupply` + `FactionBlockEvent_*` rows that lack the embedded `PalocStringRef`). C# side: two new `[LibraryImport]` entries in `NativeSaveLoader.cs::NativeMethods`, two new `LookupGroupKey(uint) → uint?` / `LookupPalocKey(uint) → ulong?` methods on `NativeGlobalGameEventInfoCatalog` in `NativeKeyInfoCatalogs.cs` (the `?` on the return type distinguishes "row not found" from "row exists but no PALOC"; for paloc, the `ulong?` is non-null when found, with the value 0 being the absent-PalocStringRef sentinel). Test pin added to `NicheBridges_LiveInstall_LoadAllAndResolveKnownKeys`: Drought_Varnian (0x4258) → group 0x4240 + paloc 72_945_724_555_969; RoyalSupply_Hernand (0x424a) → paloc 0. No UI consumer yet — the wrapper is parked for whenever the editor wants to surface localized event names. |
>
> Side maintenance from the 1.07 → 1.08 vendor bump (`5583e0e`): live-install row-count pins in `KeyInfoCatalogsTests.NicheBridges_LiveInstall_LoadAllAndResolveKnownKeys` flipped from exact equals to lower-bound `>=` (GlobalGameEvent grew 103 → 188, GlobalGameEventGroup grew 7 → 12 — the others stayed at 1.07 counts, but the lower-bound shape future-proofs them too). The exact `LookupStringKey(known_key) == known_value` assertions are unchanged — those still catch genuine schema drift. The `StringInfoCatalogTests.LoadFromBytes_LiveInstall_ResolvesItemIconPath` first-entry pin (1.06-specific `(0x2ad9f89e, "RealWorld")`) was replaced with a round-trip property check (`entry[0].value == LookupByHash(entry[0].hash)`) since the on-disk first-entry hash drifts across game patches.
>
> Tests: **287 → 294 pass** (+7 — most are from the 1.08 vendor refresh adding new tests upstream that cascade into the live-install suite; the GlobalGameEvent body assertions added here are inline with an existing test method so don't change the test count). Debug build clean. AOT publish verified — `dist\win-x64\CrimsonAtomtic.exe` 26.7 MB, single-file shape confirmed via `dumpbin /dependents`. **Visual UI verification still pending for the part-14 World Map parchment alignment bug** — that follow-on carries forward unchanged.
>
> ### Open follow-ons noted during this session
>
> - **`GlobalGameEvent` consumer wiring** — the `LookupGroupKey` / `LookupPalocKey` surface exists but has no UI consumer. Wire it into the localization pipeline whenever the editor surfaces event names (e.g., a future event-filter UI).
> - **AOT publish gotcha doc** — the `Userenv.lib` requirement isn't in `vendor/crimson-rs/docs/c-sharp-nativeaot-integration.md`. Worth a PR to the vendor doc next time we touch it.
>
> ### Vendor state
>
> `vendor/crimson-rs` at `b0cbd38` (was `090a73d` per part 7). Run `vendor\update_vendors.ps1` at session start to refresh.
>
> ---
>
> Last previous update: 2026-05-18 part 14 (World Map parchment composite shipped but **user-reported visual mismatch** — the blur_height layer + road_sdf layer don't agree on world coverage, so roads land in the wrong places relative to the coastline. Iteration paused; tomorrow's session should validate per-layer world ranges and likely fall back to the 785-tile terrain composite where world coverage is known per-tile).
>
> ## 🎯 Next-session quick pickup
>
> Working tree being committed at the end of this session. Test suite
> **287/287** still. AOT bundle freshly rebuilt to `dist\win-x64\`
> after part 14. **One known regression** — see "World Map parchment
> composite layer-alignment bug" in the follow-ons. Next-session
> pickup should start with the inspection helpers in
> `src/CrimsonAtomtic.Tests/WorldMapLayerInspectionTests.cs` to
> validate per-layer world coverage before re-attempting the composite.
>
> ### Open follow-ons (consolidated across parts 1–14)
>
> Sized small → large, freshest-context first:
>
> - **🐞 World Map parchment composite layer-alignment bug** (part 14
>   — user-reported 2026-05-18): the parchment basemap is generated
>   but `cd_worldmap_blur_height.dds` (land/water mask) and
>   `cd_worldmap_road_sdf_32768x32768.dds` (road network) don't agree
>   on world coverage — roads appear in the water region instead of
>   over land, and the cream-coloured land area doesn't fill the
>   playable continent shape from the JPG. Same 8192² dimensions but
>   the two layers are sampling different rectangles of the world.
>   Next-session diagnostic steps:
>   1. Run `WorldMapLayerInspectionTests.Inspect_EachLayerAsStandalonePng`
>      to dump each layer as its own PNG to
>      `%LOCALAPPDATA%\CrimsonAtomtic\WorldMap\inspect\layer_*.png`.
>   2. Compare against `D:\Github\crimson-rs\out\worldmap\world-map-1024.jpg`
>      to find each layer's world coverage rectangle (e.g. crop +
>      offset).
>   3. Either align by cropping/offsetting per layer in
>      `WorldMapCompositor`, or pivot to **Path B (terrain tiles)** —
>      stitch the 785 per-chunk 512² tiles in
>      `0015/leveldata/rootlevel/terrain/color/`; each tile name
>      embeds its `_X_Y_` chunk coords so world coverage is
>      unambiguous. The terrain-color tile inspection helper
>      (`Inspect_TerrainColorTilesIn0015`) is staged for that path.
>   4. If pivoting to Path B, the per-pixel scale is 0.5 px / world
>      unit (512² per 1000-unit chunk), giving a ~14,332² stitched
>      output before downsampling — needs a sensible cache size
>      target (3K-4K likely).
> - **World Map visual verification** (part 12) — Phase 1 ships but I
>   only smoke-tested compile + tests + extraction. Markers and the
>   pan/zoom UI haven't been visually verified against a real save
>   loaded in the running app. Re-test after the layer-alignment bug
>   is fixed; otherwise the markers will look mis-placed for the
>   wrong reason.
> - **World Map affine calibration** (part 14) — the
>   `WorldMapAffine.ParchmentComposite` constants
>   (`scaleX = 0.183`, `offsetX = 2515`, `offsetY = 1864` on a 4096²
>   canvas) are arithmetic from the web-map fit, not a least-squares
>   fit against landmarks on the composite output. Active char at
>   world (−10502, −4373) lands somewhere around pixel (593, 1058).
>   Pin-point accuracy needs landmark anchoring (user-facing "drag two
>   landmarks" calibration step, or an offline fit analogous to
>   `vendor/crimson-rs/scripts/worldmap_tp_fit.py`). Previous
>   `GlobalColormap` and `NavigatorGuide` affines were scrapped along
>   with the corresponding basemap swaps in parts 13 + 14.
> - **Surface "no dictionary found" in the UI** (part 11) — when
>   `UiLanguageService.LastApplyOutcome == DictionaryNotFound` the menu
>   pick is silently swallowed. A status-bar message or alert would
>   catch any future regression that breaks the marker-key probe (e.g.
>   someone removes `__UiLangCode__` from a new language file).
> - **CharacterKey picker tooltip variant** (from part 1's per-row
>   button work — *NB: this is the original-original status doc item,
>   not the Mark-Challenge tooltip; that's already shipped*). Likely
>   already-resolved-by-collateral; verify before starting.
> - **OCT forum post URL placeholder** (from part 1) —
>   `docs/oct/features-highlights.md` no longer has a Get-it section,
>   so this is moot unless the post needs the URL re-added separately.
> - **Visual verification of the part-4 palette picker** —
>   user-confirmed working in part 6's note; this is closed but listed
>   here for completeness. Skip.
> - **AOT publish smoke test** (from part 8) — automate the
>   "publish → launch → click → assert repaint" chain. Worth doing
>   only if a third AOT-only regression surfaces.
> - **Headless Avalonia integration test for language switching**
>   (parts 9 + 11) — same shape as above but for the i18n chain.
> - **Safe re-attempt of "+ Add Dye"** with a per-prefab valid-slot
>   picker (rolled back in part 5; recipe documented inline at the
>   part-5 entry). Needs the `partprefabdyeslotinfo.LookupSlotCount`
>   gamedata bridge (already loaded) + a small slot-picker UX step.
> - **World Map Phase 2 — filters + name resolution** (part 12
>   roadmap) — checkbox per region, hover tooltip showing the
>   localized owner name (CharacterKey → display name via the
>   existing localization bridge), click-marker side panel with full
>   record fields. Phase 1 markers carry placeholder `OwnerLabel`
>   strings like "Mercenary #1234"; the proper resolver hookup is
>   straightforward but deferred.
> - **World Map Phase 3 — interactive editing** (part 12 roadmap) —
>   drag-to-move a marker writes the underlying `_position` bytes via
>   `SetScalarFieldsBatch`. Requires careful validation (don't drag
>   into geometry, don't move uniqueness-coupled gimmicks) and a
>   confirm step before persistence. RISKY — leave until the basemap
>   accuracy is good enough to trust visual placement.
> - **World Map Path B — terrain-tile compositor** (part 12 roadmap)
>   — instead of the single 2048² `global_colormap.dds`, composite
>   the 785 per-chunk 512² tiles in `0015/.../terrain/color/` into a
>   higher-resolution basemap (~14,332² stitched). User picked this as
>   a separate path back in the session-start question. Adds detail
>   but multiplies the cache size (~3 MB → ~30+ MB) and the
>   composite time (one-shot job; cached after).
> - **Marker performance optimisation** (part 12) — the 3,317-marker
>   `ItemsControl`-on-`Canvas` rendering may lag during pan / zoom on
>   slower hardware. If user reports laggy interaction, swap the
>   `ItemsControl` for a custom `Control` overriding `Render(...)`
>   that draws all markers in a single Skia pass.
> - **Pattern B v2 for multi-objective SA challenges** (from part 1)
>   — RE-heavy; needs slot106 → slot107 → post-claim diff to determine
>   the engine-natural completion shape for negative-keyed sub-step
>   challenges. Diagnostic scripts in `tools/inspect/sa-investigations/`.
>
> ### Vendor state
>
> `vendor/crimson-rs` at `090a73d` (see part 7 — `crimson_paz_list_dir`
> binding). Run `vendor\update_vendors.ps1` at session start to refresh;
> the upstream history was force-pushed once during this session, so
> use `-Force` if vendor diverges.
>
> ### Quick verification checklist
>
> Before committing new code:
>
> 1. `dotnet build src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj` — must be 0 errors, 0 warnings
> 2. `dotnet test src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj --no-build` — should be 281/281
> 3. For UI changes: launch via `dotnet run` (or F5 in VS) and exercise the changed surface
> 4. For shippable changes: `.\build.cmd publish` → smoke-test `dist\win-x64\CrimsonAtomtic.exe`
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 14)
>
> Basemap is now a runtime parchment-style composite. User reviewed
> part 13's `cd_global_map_navigator_guide_00.dds` and called it
> "also NG" — the navigator guide ships with Korean region labels +
> the abyss-grid checkerboard around the playable continent, which
> doesn't match what they want. The user-pinned reference look is
> `crimson-desert-full-world-map.jpg` (parchment-cream land,
> muted-teal water, road network) — a community-fetched JPG with a
> PowerPyx.com watermark, not shippable from our side. There's no
> single game asset that matches; the game UI composites the look at
> runtime from multiple layers. We replicate that compositing locally
> to a 4096² PNG cache.
>
> | Area | Scope |
> |---|---|
> | **`WorldMapCompositor`** | New `Services/WorldMapCompositor.cs` that takes three DDS byte streams + outputs a parchment-style PNG. Layer mapping: (1) `cd_worldmap_blur_height.dds` (8192² L8 / DDPF_LUMINANCE grayscale; **R = elevation**, 0 = sea floor, ≥40 = land) drives the land/water silhouette with depth-shaded water (deeper blue offshore) + height-modulated land brightness (subtle relief shading); (2) `cd_worldmap_paper_pattern.dds` (512² BC1 tileable parchment texture) fills the land via wraparound sampling; (3) `cd_worldmap_road_sdf_32768x32768.dds` (8192² L8 SDF; histogram pinned: road network packed into bytes 120..135, mode at 0 for far-from-road) overlays a warm-gray road line on top of land with a smooth 8-byte ramp for anti-aliased edges. Output is 4096×4096 RGBA (16M pixels) — composited by direct-buffer iteration with precomputed per-axis source-pixel index tables (8 KB) so the hot loop is just an array lookup + 1 conditional + 2 splats. ~5-10 second runtime + ~400 MB transient buffer for the 8192² source decodes. |
> | **L8 / DDPF_LUMINANCE decoder path** | `IconImageEncoder.ParseDdsHeader` previously rejected any DDS without DDPF_FOURCC; both `blur_height` and `road_sdf` are uncompressed 8-bit grayscale (DDPF_LUMINANCE, R-mask = 0xFF). Branch added: FOURCC → BC1/BC3, LUMINANCE → new L8 case. `DecodeBlocks` short-circuits to new `DecodeL8(pixels, w, h)` which splats the single luminance byte to R/G/B (alpha = 255). Pinned by the live-install test below. |
> | **`WorldMapBasemapService` rewired** | The cache path swaps to `parchment_basemap.png`. `EnsureBasemapAsync` now extracts three layer DDSes from `0012/` (blur_height + road_sdf under `ui/texture/image/worldmap/`, paper_pattern under `ui/texture/`) and runs them through the compositor on a background thread (one `Task.Run` covers all three extracts + the composite + the encode so the UI dispatcher doesn't see the 5-10 s synchronous cost). |
> | **`WorldMapAffine.ParchmentComposite`** | New 4096² affine: `scaleX = 0.183`, `offsetX = 2515`, `scaleZ = −0.183`, `offsetY = 1864`. Derived arithmetically from the web-map fit (5178×5240, scale 0.432, world origin at pixel (5937.5, 1864.08)) → playable continent on the composite occupies the inner ~2200×2700 of the 4096² canvas; per-pixel scale ≈ 0.183 px/world-unit. Old `GlobalColormap` (part 12) + `NavigatorGuide` (part 13) affines removed. |
> | **Tests + i18n** | `WorldMapBasemapServiceTests` smoke pin shifted to 4096×4096 + the new affine pair (`WebMap5178x5240` + `ParchmentComposite` — `WebMap` kept as the regression baseline since the affine was originally fit against it). Bilingual extracting-message strings updated across en/ja/zh-TW to mention the three composite layers + the ~10 s first-launch cost. |
>
> Tests: **287/287 pass** (same count — the new L8 path is covered by the existing live-install smoke). Debug build clean. **AOT publish verified** — `dist\win-x64\CrimsonAtomtic.exe` rebuilt clean. **UI still not visually verified by me end-to-end** — only the composite PNG was reviewed in isolation; the user should open Tools → World Map and confirm the markers overlay correctly on the new basemap.
>
> ### Open follow-ons noted during this session
>
> - **🐞 Layer alignment bug** — user opened the composite and reported
>   roads land in the ocean + the cream land area doesn't match the
>   playable continent shape. `cd_worldmap_blur_height.dds` and
>   `cd_worldmap_road_sdf_32768x32768.dds` are both 8192² but don't
>   share the same world coverage; the compositor naively scales both
>   to the 4096² output, mixing the misaligned content. Full
>   description + reproduction steps moved to the "🐞 World Map
>   parchment composite layer-alignment bug" entry in the consolidated
>   follow-on list above.
> - **Likely pivot to Path B (terrain tiles)** — the 785 per-chunk
>   tiles in `0015/leveldata/rootlevel/terrain/color/` have unambiguous
>   world coverage (each = 1 chunk = 1000 world units, indexed by
>   `_X_Y_` in the filename). Stitching them yields a 14,332² basemap
>   with byte-perfect alignment guarantees; downsample to 3K-4K for
>   the cache. The `Inspect_TerrainColorTilesIn0015` helper is staged.
> - **Same calibration drift as parts 12 + 13** — the new parchment affine is still arithmetic, not a fit. Active char projected to ~(593, 1058) on 4096²; markers should cluster around the right region but expect a few-percent residual.
> - **Region labels missing** — the web JPG carries text labels for HERNAND / ILLUZ / CRIMSON DESERT / DEMENISS / DELESYA; our composite ships geography only. Could be added by rendering text at known label coords (would need a per-region label table + world coords) or by extracting the 234 region-title decals from `ui/texture/image/worldmapregiontitle/` and compositing them at their gamedata-defined positions.
> - **Anti-aliasing on the coastline** — the land/water mask uses a hard threshold (height ≥ 40 = land). A 2-3 pixel soft transition would smooth the coastline.
>
> ### Open follow-ons carried over (no change)
>
> - World Map Phase 2 (filters + name resolution) (from part 12).
> - World Map Phase 3 (interactive editing) (from part 12).
> - World Map Path B terrain-tile compositor (from part 12 — now mostly subsumed by this part's compositor).
> - Marker rendering performance (from part 12).
> - Owner-label resolution (from part 12).
> - Surface LastApplyOutcome in the UI (from part 11).
> - AOT publish smoke test (from part 8).
> - Headless Avalonia integration test for language switching (from parts 9 + 11).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 13)
>
> Basemap source swap. User reviewed part 12's output and reported the
> 2048×2048 `global_colormap.dds` basemap "isn't what the player sees";
> the actual in-game map looks like the web-fetched JPG with colored
> regions + labels. Cross-referencing the vendor's worldmap-plotting
> doc, the right asset is **`cd_global_map_navigator_guide_00.dds`** —
> explicitly tagged "Game's in-engine world map (faction-colored, chunk
> grid visible)". Visual inspection confirmed: this is the same map the
> player sees in the in-game UI (1024×1024 BC1, region tints, region
> labels, chunk-grid abyss border around the playable continent).
>
> | Area | Scope |
> |---|---|
> | **Basemap source swap** | `WorldMapBasemapService` now extracts from `0000/object/texture/cd_global_map_navigator_guide_00.dds` (was `0015/leveldata/rootlevel/terrain/global/global_colormap.dds`). Cached PNG renamed `global_colormap.png` → `navigator_guide.png` so the old cache doesn't shadow the new asset. Group probe witness also flipped to `0000/0.pamt`. |
> | **New affine for navigator guide** | `WorldMapAffine.GlobalColormap` removed, `WorldMapAffine.NavigatorGuide` added: scale-X = 700/11984 ≈ 0.0584, scale-Z = -700/12101 ≈ -0.0579, world origin pixel (964, 449). Derived arithmetically — playable continent assumed to occupy the inner 700×700 of the 1024² image (rest is abyss-grid border), world-origin position scaled from the web-map fit's pixel (5937.5, 1864.08) on its 5178×5240 image proportionally. Not a least-squares fit; calibration follow-on noted. |
> | **i18n + tests + handler** | Three language strings updated (the "extracting basemap" body now mentions `cd_global_map_navigator_guide_00.dds` and group `0000`). `WorldMapBasemapServiceTests` pin shifted to 1024×1024 + the new affine pair. `MainWindow.axaml.cs` passes `WorldMapAffine.NavigatorGuide` to the VM constructor. |
>
> Tests: **287/287 pass** (same count as part 12). Debug build clean. **AOT publish verified** — `dist\win-x64\CrimsonAtomtic.exe` rebuilt (26.1 MB). **UI still not visually verified by me** — the user should open Tools → World Map again and confirm the new basemap looks like the in-game UI map (which it should, since the inspection-extracted PNG was the deciding visual).
>
> ### Open follow-ons noted during this session
>
> - **Same calibration drift as part 12** — the navigator-guide affine
>   is still arithmetic, not a fit. Markers should land in roughly the
>   right region but expect a few-percent residual. The calibration
>   follow-on from part 12 still applies.
> - **Higher-resolution layered composite** is even more attractive
>   now that we've committed to "show what the player sees" — the
>   navigator guide is only 1024×1024, so dense gimmick clusters will
>   pixel-pile. Compositing `bitmap_region.dds` + road SDF + region
>   titles at 4K-8K would give a much higher-fidelity basemap. Still
>   a Phase-2+ item.
>
> ### Open follow-ons carried over (no change)
>
> - World Map Phase 2 (filters + name resolution) (from part 12).
> - World Map Phase 3 (interactive editing) (from part 12).
> - World Map Path B terrain-tile compositor (from part 12).
> - Marker rendering performance (from part 12).
> - Owner-label resolution (from part 12).
> - Surface LastApplyOutcome in the UI (from part 11).
> - AOT publish smoke test (from part 8).
> - Headless Avalonia integration test for language switching (from parts 9 + 11).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 12)
>
> World Map UX Phase 1. User asked to develop the Tools → World Map
> dialog (deferred since parts 6 + 7 when the two backing ABIs landed)
> with three explicit feature picks: yaw arrows, region-tint markers,
> mouse coord readout + distance ruler, PNG export. Phase 2 (filters,
> name resolution, tooltips) and Phase 3 (drag-to-move editing) are
> queued in the roadmap but deliberately out of scope.
>
> | Area | Scope |
> |---|---|
> | **`WorldMapAffine` value-record** | New `Services/WorldMapAffine.cs` defines the world-coord → basemap-pixel affine plus the inverse. Two pinned instances: `WebMap5178x5240` (the 5178×5240 user-fetched JPG — kept as a regression baseline since the vendor's affine was fit against it; we don't ship the JPG) and `GlobalColormap` (the game-extracted 2048×2048 colormap, with scale + offsets derived from chunk-grid math: 2048 / 14332 world units, centred on the world origin). The `GlobalColormap` constants are an educated guess — chunk-grid arithmetic without a least-squares fit against known landmarks. Expect ~50 px residual; precise calibration is a follow-on. Inverse method enables the mouse-coord readout (basemap pixel → world coords) and the distance ruler (two basemap clicks → world-unit Δ). |
> | **`WorldMapBasemapService`** | Extracts `global_colormap.dds` from `0015/leveldata/rootlevel/terrain/global/` via `IPazExtractor.ExtractFile`, decodes the BC3 DDS via the new public `IconImageEncoder.DecodeDdsToRgba` (factored out of the existing icon path; the BC1/BC3 kernel is reused, only the resize+WebP encode step is unique to icons), and writes a full-resolution PNG to `%LOCALAPPDATA%\CrimsonAtomtic\WorldMap\global_colormap.png`. Same on-demand-from-user-install pattern as `IconExtractionService` — no asset shipped with the editor, sidesteps the asset-license question. `EnsureBasemapAsync(paz, gameRoot, forceRefresh: false)` returns the cached path on second call. The DDS-to-PNG pipeline uses a new `IconImageEncoder.EncodeRgbaAsPng(rgba, w, h)` helper (lossless full-res, distinct from the icon-side WebP encoder). |
> | **`WorldMapViewModel`** | Holds the loaded basemap `Bitmap` (eagerly loaded in constructor), every `PositionedEntityRecord` from `loader.ListFieldPositions(out _)` precomputed into `WorldMapMarker` records (pixel position via affine + yaw in degrees + region key + owner-label placeholder), three per-kind `ObservableCollection`s (ActiveChar / Mercenary / Gimmick) so the per-kind filter checkboxes drop entire AXAML layers via `IsVisible` rather than per-item visibility bindings (perf — 3,000+ items), and the small bits of UI state: `ZoomScale` (1.0 default, bound to the `ScaleTransform` since AXAML doesn't auto-generate fields for non-Control elements), `CursorCoordsText`, distance-tool state machine (`Idle` → `WaitingForFirst` → `WaitingForSecond` → final-text), region-coloring toggle. Region color: a deterministic HSL hue derived from `_fieldInfoKey` via a Knuth-multiplier hash → hue space — different regions get visually distinct colors without us curating a region→color table. |
> | **`WorldMapWindow.axaml`** | DockPanel-laid window with: toolbar of three per-kind checkboxes (with live count labels), yaw-arrow + region-tint toggles, distance-tool + export-PNG + close buttons. Map surface: `ScrollViewer` wrapping a `Grid` (sized to basemap dims) with a `ScaleTransform.RenderTransform` (bound to `ZoomScale`); three `ItemsControl` layers over the per-kind collections (gimmicks under, mercenaries middle, active char top); a top-most distance-overlay `Canvas` with two ruler dots + a dashed connector line. Marker visuals: gimmick = 4×4 ellipse (no yaw arrow, no per-marker `Grid` wrapper — 3,240 of them, performance-sensitive), mercenary = 14×14 blue triangle with yaw line, active char = 18×18 gold 5-point star with yaw line. Marker fill: a `MultiBinding` over (Kind, FieldInfoKey, UseRegionColoring) → `SolidColorBrush` via the new `WorldMapMarkerBrushConverter` (kind-color or region-color depending on the toggle). Marker positioning: `ContentPresenter` style setter pins `Canvas.Left/Top` to `PixelX/Y` via `ReflectionBinding` (compiled binding fails inside `ItemsControl.Styles` since the Window's `x:DataType` resolves the binding type incorrectly for ContentPresenter children — `ReflectionBinding` falls through to runtime DataContext resolution). |
> | **Code-behind: pan + zoom + distance + export** | `WorldMapWindow.axaml.cs`: pan via left/middle mouse drag (anchor on Pressed, update `ScrollViewer.Offset` on Moved, release on Released — `e.Pointer.Capture(MapStage)` keeps drag alive when the cursor exits the map area); zoom via `PointerWheel` (step factor 1.15, clamped to [0.1, 8.0], **zoom-around-cursor math** — after the scale change, shifts `ScrollViewer.Offset` by `pointerOnStage × (newScale − oldScale)` so the pixel under the cursor stays in place; without this the view drifts away from where the user is looking, which feels broken); coord readout updates on every PointerMoved via `vm.UpdateCursorWorldCoords(px, py)`; distance-tool clicks route through `vm.HandleDistanceClick(px, py)` when the tool is armed; export via `RenderTargetBitmap.Render(MapStage)` at the native basemap resolution (snapshots + restores the current zoom level so the export is always 1:1, regardless of how far the user has zoomed). |
> | **Menu wiring + i18n** | New `MenuItem Header="{DynamicResource MenuWorldMap}"` under Tools, gated on `HasSave`. `OnWorldMapClick` handler in MainWindow.axaml.cs: requires loaded save + non-empty `Localization.GameRoot`, surfaces alert dialogs for the two pre-launch failure modes (no game install, basemap extract failed), then constructs + shows `WorldMapWindow`. Bilingual strings added for the menu header + 17 dialog-internal strings (window title, three filter labels with tooltips, yaw / region / distance / export toggles, status-bar placeholders, two pre-launch alerts). en/ja/zh-TW all updated. |
> | **Tests** | New `WorldMapBasemapServiceTests` (6 tests, **281 → 287**): live-install end-to-end smoke (extracts the DDS, decodes to PNG, verifies PNG magic + 2048×2048 dims — pins that a future game patch swapping `global_colormap.dds` to a BC format we don't decode surfaces here), affine round-trip property (world → pixel → world reproduces input within 1e-6 for both affines + several known coords), web-map affine regression pin against the vendor's `(−10502.729, −4373.9663) → (1399.9, 3758.3)` calibration point. |
>
> Tests: **281 → 287** (+6 World Map). Debug build clean, 0 errors / 0 warnings. **UI not visually verified end-to-end** — the AXAML compiles, the VM types check, the basemap extracts correctly (PNG inspected, shows the Pywel continent reddish-desert + green forest), but I haven't watched the window render with all 3,317 markers overlaid. Next session pick-up should open Tools → World Map and confirm the visual checklist in the "Next-session quick pickup" callout above.
>
> ### Open follow-ons noted during this session
>
> - **Affine calibration drift** (see follow-ons above) — the `GlobalColormap` affine is an educated guess. Markers will land in roughly the right region but may be visibly off. Pin-point accuracy needs landmark anchoring.
> - **Phase 2 (filters + tooltips) and Phase 3 (drag-to-edit)** — both in the roadmap, both deferred. Phase 3 is risky and needs the affine fix first.
> - **Path B (terrain-tile compositor)** — user picked "both" for the basemap-source question at session start. Phase 1 ships Path A (single 2048² colormap); Path B (785-tile composite for higher resolution) is the same shape but scaled up + needs tile-grid stitching code. Roadmap follow-on.
> - **Marker rendering performance** — 3,000+ `ContentPresenter`s on a `Canvas` may chug. If so, swap for a single custom `Control` drawing all markers in `Render(DrawingContext)`.
> - **Owner-label resolution** — markers carry placeholder labels (`"Mercenary #1234"`, `"Gimmick 0xabcd"`). Wiring through the existing `LocalizationProvider` to surface the localized CharacterKey / GimmickInfoKey display name is straightforward but deferred to Phase 2's tooltip work.
>
> ### Open follow-ons carried over (no change)
>
> - Surface LastApplyOutcome in the UI (from part 11).
> - AOT publish smoke test (from part 8).
> - Headless Avalonia integration test for language switching (from parts 9 + 11).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 11)
>
> The UI language switch bug from parts 8/9/10 — finally resolved via
> two distinct fixes after a diagnostic-log iteration with the user
> surfaced the real root causes. Parts 8/9/10's earlier theories
> (avares URI parser AOT regression, Avalonia not auto-raising
> ResourcesChanged on reorder, AOBMaker-style Clear+Add) were each
> plausible mechanisms but none was the actual blocker for this
> codebase. The diag log revealed:
> - `SnapshotDictionaries: merged.Count=3` — the dictionaries ARE in
>   `Application.Resources.MergedDictionaries` at startup
> - `[0] type=Avalonia.Controls.ResourceDictionary` (not `ResourceInclude`)
> - `<unrecognised type — add a branch above to claim it>`
>
> So Avalonia 11.3's XAML compiler inlines `<ResourceInclude Source="…">`
> targets directly as `Avalonia.Controls.ResourceDictionary` instances
> in the merged list. That concrete type **does not expose the original
> Source URI** (we hit CS1061 trying to read it). The previous
> URI-based snapshot returned 0 entries because nothing in the list
> matched the `ResourceInclude` type-check.
>
> | Area | Scope |
> |---|---|
> | **`__UiLangCode__` marker key for snapshot identification** | Each shipped language dictionary now carries `<sys:String x:Key="__UiLangCode__">{code}</sys:String>` in en.axaml / ja.axaml / zh-TW.axaml. `SnapshotDictionaries` probes each merged `ResourceDictionary` for this key via the standard IDictionary indexer (no reflection, AOT-safe) and uses the value to identify which language each entry is. Independent of: the concrete Avalonia type used for the dictionary, the order entries appear in App.axaml, the URI scheme registration state. AOBMaker's same use case works without the marker key because it relies on positional indexing (merged[0] = en, [1] = zh-TW, [2] = ja), trusting App.axaml's declared order — the marker-key approach is slightly more robust against future App.axaml reorders. |
> | **Win32 `GetUserDefaultUILanguage` for auto-detect** | User report on a zh-TW OS: the app auto-detected English instead of Traditional Chinese. Root cause: csproj sets `<InvariantGlobalization>true</InvariantGlobalization>` for AOT binary-size reasons, which makes .NET strip globalization data — `CultureInfo.CurrentUICulture.Name` returns `""` regardless of OS UI language, so `DetectFromCulture` always falls through to English. AOBMaker doesn't set this flag, hence its culture-based detect works. Fix: new `DetectFromOsUiLanguage()` P/Invokes Win32 `GetUserDefaultUILanguage()` which returns a 16-bit LCID independent of .NET's globalization configuration. The LCID's primary-language bits (low 10) + sublanguage bits (next 6) classify directly: primary 0x11 → ja, primary 0x04 + sublang ∈ {1=TW, 3=HK, 5=MO} → zh-TW, else en. New `ResolveActiveFromOs(string?)` overload mirrors `ResolveActive` but uses the Win32 detector. `App.axaml.cs` startup + `MainWindowViewModel.SetUiLanguage` now call this overload. Used `DllImport` instead of `[LibraryImport]` for the kernel32 P/Invoke since the source generator requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` which the Ui project doesn't enable. |
> | **The Clear+Add reorder pattern from part 10** | Kept — once the snapshot actually populates (this part 11), Clear+Add is the correct reorder shape because `Clear()` fires AvaloniaList's Reset notification which Avalonia 11.3 propagates as `ResourcesChanged` down the visual tree. Parts 8/9's URI matching hardening + LastApplyOutcome diagnostic also stays in place for defense-in-depth. |
>
> Tests: **281/281 still pass.** Debug verified end-to-end via the diagnostic log (cleaned up before this final commit since the bug is resolved). AOT bundle rebuilt at `dist\win-x64\CrimsonAtomtic.exe`.
>
> ### Open follow-ons noted during this session
>
> - **Surface "no dictionary found" in the UI** when `LastApplyOutcome == DictionaryNotFound` — currently silent. If a future regression breaks the marker-key probing (e.g. someone removes `__UiLangCode__` from a new language file), the UI would silently freeze without a clue. A status-bar message would help.
> - **Headless Avalonia integration test for language switching** (carried forward from part 9) — the unit tests cover matcher logic + Win32 detect classification but not the actual swap-and-repaint chain. A `Avalonia.Headless` test harness could verify end-to-end. Worth adding if a fourth regression surfaces.
>
> ### Open follow-ons carried over (no change)
>
> - AOT publish smoke test (from part 8).
> - World-map UX layer (deferred).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 10)
>
> Part 9's "manually raise ResourcesChanged" still didn't make the UI
> repaint in AOT — confirmed by the user against a fresh AOT bundle.
> Pivoted to the proven working pattern from a sibling repo
> (`D:\Github\AOBMaker\src\AOBMaker.UI\I18n\Lang.cs`) which ships
> language switching in Avalonia 11.3 AOT.
>
> | Area | Scope |
> |---|---|
> | **UiLanguageService rewrite to Clear+Add pattern** | The AOBMaker pattern, distilled: (1) at construction time, snapshot each language's `IResourceProvider` from `Application.Resources.MergedDictionaries` into a `Dictionary<string, IResourceProvider>` keyed by code. (2) On every `Apply(code)`, call `merged.Clear()` then `merged.Add(...)` the non-active dictionaries first, then the active one last. **`Clear()` fires the AvaloniaList's reset notification which Avalonia 11.3 DOES propagate as a `ResourcesChanged` event down the visual tree** — that's the cascade `DynamicResource` bindings listen on. The previous `RemoveAt + Add` of the same item instance never triggered the cascade (the `AvaloniaList.ForEachItem` callbacks only manage AddOwner/RemoveOwner; for an in-place reorder of a single instance, no resource-change notification fires). Even part 9's manual `IResourceHost.NotifyHostedResourcesChanged` calls weren't enough — the visual-tree resolver caches DynamicResource lookups in a way that needs the actual collection reset to invalidate, not just an external event. (Verified empirically: AOBMaker uses Clear+Add and works; CrimsonAtomtic with manual NotifyHostedResourcesChanged on top of RemoveAt+Add didn't.) Side benefit: snapshot-at-construction means `Apply` no longer parses URIs — robust against any future avares://-scheme regression. Part 8's URI matcher + `MatchesUri` helper stays, but is now used only ONCE at construction inside `SnapshotDictionaries` to map URI → code; the per-Apply hot path is pure dictionary lookup + Clear+Add. |
>
> Tests: 281/281 still pass (the 7 part-8 MatchesUri tests cover the snapshot-time URI matching, which is still in use). Debug build clean. Fresh AOT bundle at `dist\win-x64\CrimsonAtomtic.exe`.
>
> ### Open follow-ons noted during this session
>
> - **None new** — if part 10's Clear+Add doesn't fix it, the root cause is in a completely different layer (something the AOBMaker repro disproves), and we'd need a different investigation entirely.
>
> ### Open follow-ons carried over (no change)
>
> - Headless Avalonia integration test for language switching (from part 9).
> - AOT publish smoke test (from part 8).
> - Surface LastApplyOutcome in the UI (from part 8).
> - World-map UX layer (deferred).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 9)
>
> Real root-cause fix for the UI language switch bug. Part 8's URI matching
> hardening helped robustness but didn't fix the symptom — the user
> rebuilt the AOT bundle with part 8 in place and the language switch
> still didn't repaint. Deeper investigation against Avalonia 11.3.12's
> source surfaced the actual issue.
>
> | Area | Scope |
> |---|---|
> | **Manual ResourcesChanged notification after reorder** | Avalonia 11.3.12's `ResourceDictionary.MergedDictionaries` is an `AvaloniaList` whose `ForEachItem` callbacks only manage `AddOwner` / `RemoveOwner` on item add / remove — they do **NOT** raise `ResourcesChanged` on the parent dictionary for an in-place `RemoveAt + Add` of the SAME item instance. So the visual tree's `DynamicResource` listeners never get notified, and the UI keeps rendering the pre-swap language. (The bug exists in Debug too — it just happens not to matter at startup because `Apply` runs before any window is constructed, so `DynamicResource` resolves correctly on first paint.) The fix: after the reorder, explicitly call `IResourceHost.NotifyHostedResourcesChanged(new ResourcesChangedEventArgs())` on **both** the `Application` (covers bindings whose nearest IResourceHost ancestor is the app) AND each open top-level `Window` (covers bindings that attached to the TopLevel's resource host). Belt-and-suspenders — Avalonia 11.3.12 doesn't ship a "refresh everything" helper, and walking both sets ensures every DynamicResource binding gets re-evaluated regardless of which host it subscribed to. Wrapped in defensive try/catch so a notification glitch can't roll back the swap itself. |
>
> Cross-references:
> - [Avalonia 11.3.12 ResourceDictionary.cs](https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.12/src/Avalonia.Base/Controls/ResourceDictionary.cs) — `MergedDictionaries` setup, item callbacks (lines around `ForEachItem`).
> - [IResourceHost.cs](https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.12/src/Avalonia.Base/Controls/IResourceHost.cs) — `NotifyHostedResourcesChanged` is public.
> - [Application.cs](https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.12/src/Avalonia.Controls/Application.cs) — implements `IResourceHost2`; its `NotifyHostedResourcesChanged` invokes local handlers (which the bindings subscribe to via the ancestor walk).
>
> Tests: **281/281 pass** (no new tests — the notification path is hard to unit-test without standing up an Avalonia app; verified manually via AOT rebuild + menu pick). Debug build clean. Part-8 URI matching hardening + `LastApplyOutcome` diagnostic kept in place — defense-in-depth, useful if a different URI matching regression surfaces later.
>
> ### Open follow-ons noted during this session
>
> - **Headless Avalonia integration test for language switching**: the unit tests cover matcher logic but not the actual ResourcesChanged propagation. A `Avalonia.Headless` test harness could verify the swap end-to-end. Worth adding if a third regression surfaces.
> - **Audit other "swap by reorder" patterns** in the codebase, if any exist. None currently surfaced but worth a grep next time.
>
> ### Open follow-ons carried over (no change)
>
> - AOT publish smoke test (from part 8).
> - Surface LastApplyOutcome in the UI (from part 8).
> - World-map UX layer (deferred).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 8)
>
> One AOT-specific bug fix. User-reported symptoms in the published
> binary: clicking Tools → UI Language to switch languages updated the
> menu checkmark but didn't repaint the UI; restarting the app showed
> the persisted pick in the menu but the UI still rendered in the
> startup language. All three symptoms tracked back to a single root
> cause.
>
> | Area | Scope |
> |---|---|
> | **UiLanguageService.Apply URI matching** | `Apply()` walked `Application.Resources.MergedDictionaries` looking for the target language's `ResourceInclude` by matching `Source.AbsolutePath.EndsWith("/Resources/Strings/<code>.axaml")`. In Debug builds Avalonia's avares:// scheme parser populates `AbsolutePath` correctly. In **AOT-trimmed publishes** the parser registration can be partially elided, leaving `AbsolutePath` empty for the same URIs that round-trip fine in Debug — so the `EndsWith` check failed for every entry, `target` came back null, and `Apply` returned silently with no reorder. The UI then stayed pinned to whatever language happened to be last in App.axaml's declared order (zh-TW). The menu checkmark update path runs after `Apply` regardless of outcome, hence the symptom of "checkmark moves but UI doesn't repaint". The startup `Apply` from `App.OnFrameworkInitializationCompleted` also silently failed — so even after restart with a persisted "en" pick, the dictionary order stayed at the App.axaml-declared en/ja/zh-TW with zh-TW winning by virtue of being last. Fix: the new `MatchesUri` helper checks **three** URI surfaces in order — `OriginalString` (always-set verbatim form, no parser dependency), `ToString()` (canonical), `AbsolutePath` (parsed path — empty in the AOT regression). First non-empty match wins. New public `ApplyOutcome` enum exposed via `LastApplyOutcome` property captures the outcome of each call (Swapped / AlreadyActive / UnsupportedCode / DictionaryNotFound) so future silent failures surface for diagnosis. 7 new unit tests pin the matcher against the three shipped avares:// URIs (positive + negative cases) plus a well-formed https:// fallback case. |
>
> Tests: **274 → 281** (+7 MatchesUri tests). Debug build clean. **Visual verification deferred** to the user since the bug only manifests in AOT publishes — Debug `dotnet run` worked fine throughout because Avalonia's avares parser is fully wired in Debug. The user should rebuild the AOT bundle (`scripts\package_aot.ps1`) and confirm Tools → UI Language now repaints the UI live.
>
> ### Open follow-ons noted during this session
>
> - **AOT publish smoke test**: there's no automated check that the published AOT binary's UI language switch actually repaints. The unit test added here verifies the matcher logic, but the full chain (AOT publish → launch → menu pick → repaint) still requires manual verification per publish. Worth automating if a similar AOT-only regression surfaces again.
> - **Surface `LastApplyOutcome` in the UI** when it lands on `DictionaryNotFound` (currently it's a code-only diagnostic). A status-bar message or a small "couldn't switch — see logs" alert would let users diagnose without dropping to the developer.
>
> ### Open follow-ons carried over (no change)
>
> - World-map UX layer (deferred — DataGrid first vs full basemap dialog; basemap rendering needs DDS decoding + game-extracted asset shipping decision).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
>
> ## ✅ This session — what shipped (2026-05-17 part 7)
>
> One commit consuming the new `crimson_paz_list_dir` C ABI from
> vendor `090a73d`. Vendor refreshed `cd02b28` → `090a73d` (upstream
> force-pushed; `aeb2fa2` is content-equivalent to our previously
> consumed `cd02b28` `crimson_save_list_field_positions` so no
> re-work needed on that surface — only the new tile-discovery ABI
> shipped here).
>
> | Area | Scope |
> |---|---|
> | **PAZ directory enumerator binding** | New `crimson_paz_list_dir(pamt_path, directory)` upstream ABI lists every file in a PAMT directory as a flat 272-byte `repr(C)` `CrimsonPazFileEntry` stream — filename (256-byte fixed buffer) + `compressed_size` + `uncompressed_size` + `is_partial` + `name_truncated`. Built as the discovery primitive for the player-facing world map (procedurally composited at runtime from ~785 terrain color tiles in `0015/leveldata/rootlevel/terrain/color` + sibling height / normal / region layers + 2 prestitched `0012/.../worldmap/*.dds` files + 234 region-title decals). New `PazFileEntry` blittable struct uses `[InlineArray(256)]` for the name buffer so the public surface stays a single 272-byte value with a managed `Name` string accessor (NUL-stripped, UTF-8 decoded). `IPazExtractor.ListDir` exposes the wrapper with the same two-call buffer-dance shape as the other enumerators. Three new tests: live-install terrain-tile enumeration with round-trip extraction of the first tile (verifies DDS magic — pins that `list_dir`'s reported filename feeds straight into `extract_file` without normalization gaps) + `NOT_FOUND` for an unknown directory + `ArgumentException` for null/empty args. |
>
> Tests: **271 → 274** (+3 PAZ tests). Debug build clean.
>
> ### Open follow-ons noted during this session
>
> - **None new** — `paz_list_dir` is the second of the two ABIs feeding
>   the deferred World Map UX (the first being `list_field_positions`
>   from part 6). Both bindings now exist; the UX decision still
>   stands.
>
> ### Open follow-ons carried over (no change)
>
> - World-map UX layer (deferred — DataGrid first vs full basemap dialog; basemap rendering needs DDS decoding + game-extracted asset shipping decision).
> - Safe re-attempt of "+ Add Dye" with per-prefab slot picker (from part 5).
> - Pattern B v2 for multi-objective SA challenges (from part 1).
> - OCT forum post URL placeholder (from part 1).
>
> ---
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
> | **Dye color group palette accessors** | Upstream investigation (vendor `d8c15cd`) pinned a critical model correction: the save's `_dyeColorR/G/B/A` scalars are **NOT freeform** — they index into a 109-position palette per Color_Group theme (9 grayscale + 10×10 chromatic). All 11 RGBs observed in slot103 (6 Hernand + 5 Pororin) hit exact gradient positions; **zero off-grid values**. CRIMSON-DESERT-SAVE-EDITOR's freeform R/G/B sliders are technically valid bytes but reach colors the engine can't display. Critical byte-order finding: the on-disk palette is BGRA but the save uses logical RGBA — the parser now swaps automatically so palette values compare directly. 3 new C ABI exports bound on `NativeDyeColorGroupInfoCatalog`: `PaletteSize(themeKey)` → 109 in 1.07, `PaletteAt(themeKey, idx)` → logical `(R, G, B, A)` ready to write into the save, `PositionForRgb(themeKey, r, g, b)` → reverse lookup (NOT_FOUND for off-grid). New live-install roundtrip test pins forward + reverse symmetry on the first theme. **DyeSlot editor UX rewrite deferred** — vendor docs spec a visual 11-row palette picker grid replacing the freeform sliders; tracked as a follow-on so the ABI binding ships independently of the bigger UX change. |
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
> v1 scope is **edit-existing-dye only** — adding dye to a previously-undyed item is deferred until the upstream `set_object_list_present` ABI lands (per `vendor/crimson-rs/docs/dye-editor-scope.md`). The 3 schema corrections from the upstream survey are honoured: `_dyeSlotNo` is signed `int8`, `_texturePalleteKey` is fixed `u16`, `_disableSymbol` is the 9th field (CRIMSON-DESERT-SAVE-EDITOR missed it).
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
> | **Tools → Edit Item Sockets…** | New menu item; opens [`SocketEditorWindow`](../src/CrimsonAtomtic.Ui/Views/SocketEditorWindow.axaml) bound to [`SocketEditorViewModel`](../src/CrimsonAtomtic.Ui/ViewModels/SocketEditorViewModel.cs). Walks every `InventorySaveData._inventorylist[*]._itemList[*]._socketSaveDataList[*]`, surfaces one row per **filled** socket (mask present + `_itemKey > 0`). Empty sockets are not shown — per CRIMSON-DESERT-SAVE-EDITOR's hard caveat, embedding gems into empty sockets requires the in-game Witch NPC and forcing fills can crash. |
> | **DataGrid columns** | Bag (InventoryKey-resolved label) / Item name (PALOC-resolved) / ItemKey / Slot # / Current gem name / Current gem key / Change Gem… button / Applied gem name (post-edit). |
> | **Change Gem… flow** | Clicking the per-row button raises `SocketEditorViewModel.ChangeGemRequested`; MainWindow code-behind opens a **gem-filtered** [`ItemPickerWindow`](../src/CrimsonAtomtic.Ui/Views/ItemPickerWindow.axaml). Picker is filtered via the new `ItemPickerViewModel(localization, allowedStringKeyPrefixes)` overload to `Item_Stat_AbyssGear_*` + `Item_Skill_AbyssGear_*`. Action button relabelled to "Pick" via the new `ItemPickerViewModel.ActionButtonLabel` / `ActionButtonTooltip` init-only properties. |
> | **Apply** | On pick, `SocketEditorViewModel.ApplyGemPick(row, gemKey)` writes the 4-byte gem `_itemKey` in-place via `ISaveLoader.SetScalarField` with a 3-step path: `_inventorylist[bag] → _itemList[item] → _socketSaveDataList[socket] → _itemKey`. No length-changing edits. Mirrors CRIMSON-DESERT-SAVE-EDITOR's "swap-only" practice — fill_socket_slots / clear_socket_slots / socket-count unlock are explicitly out of v1 scope per the safe-edit contract. |
>
> Gem identification: TWO string-key prefixes (`Item_Stat_AbyssGear_*` for stat-mod gems, `Item_Skill_AbyssGear_*` for skill-bestowing gems). Internally Pearl Abyss called these "AbyssGear" — only localised to "gem" in display. 100% of CRIMSON-DESERT-SAVE-EDITOR's curated 189-entry gem list falls under one of these two prefixes in the 1.06.01 baseline. No vendored JSON; the picker enumerates live from `iteminfo.pabgb` via the existing iteminfo bridge.
>
> Out of v1 scope (documented in the [`SocketEditorViewModel`](../src/CrimsonAtomtic.Ui/ViewModels/SocketEditorViewModel.cs) docstring):
> - **Fill empty socket** — length-changing splice; CRIMSON-DESERT-SAVE-EDITOR exposes the Python function but the UI route is "swap only" because empty sockets need the in-game Witch NPC.
> - **Clear filled socket** — length-changing splice with sibling-offset cascading.
> - **Socket-count unlock** — triple coupled write (`_maxSocketCount` + `_validSocketCount` + `_endurance` high byte). CRIMSON-DESERT-SAVE-EDITOR explicitly warns "0 → positive on a zero-record list may crash".
>
> Tests: **170/170 pass** (no new tests for v1 — the FFI surface is `SetScalarField` which is already covered by `SetScalarField_NestedPath_RoundTripsThroughWriteToFile`; v1's net code is UI plumbing over existing primitives). AOT build clean.
>
> ## ✅ This session — what shipped (2026-05-15 part 4)
>
> Pick #3 of the porting roadmap — **Rename Mercenary** (the "Pet rename"
> feature from CRIMSON-DESERT-SAVE-EDITOR; pets live as mercenary
> entries in this game's save model). Required a new Rust FFI entry
> point and the C# side around it.
>
> Equipment-set duplicator dropped from this iteration — see the
> survey notes below; CRIMSON-DESERT-SAVE-EDITOR's "duplicator" is actually a
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
The original 5-pick survey against CRIMSON-DESERT-SAVE-EDITOR
(`D:\Github\CRIMSON-DESERT-SAVE-EDITOR`, mined once for
ideas per CLAUDE.md rule 11) is fully consumed. For the current
open-task list see the top of this file.

### Porting roadmap — 5 picks (EASY → MEDIUM) (historical, all 9 items resolved)

| # | Feature | Difficulty | Notes |
|---|---|---|---|
| 1 | ~~**Auto-find saves on launch**~~ | ~~EASY~~ | ✅ **Shipped 2026-05-15 part 2.** Steam / Epic / Game Pass plain-folder probe + most-recent-mtime preference + `preferred_platform` settings persistence + platform-scoped backup tree with legacy migration. Game Pass wgs UWP container deferred. Linux Proton prefix detection (appid `3321460`) deferred. |
| 2 | ~~**Item Pack import**~~ | DEFERRED | Decided not to ship for now — user can already use the Item Picker + Add-to-bag for individual items, and curating safe pack JSONs against the 1.06 schema is more upfront work than the convenience gain warrants. Revisit if/when there's demand for batch-imported gear loadouts. |
| 3 | ~~**Pet rename + Equipment-set duplicator**~~ | ~~EASY~~ | ✅ **Pet rename shipped 2026-05-15 part 4** (as Rename Mercenary; CRIMSON-DESERT-SAVE-EDITOR's "Pet rename" is mercenary rename in this game's save model). Required a new `set_inline_bytes_field` Rust FFI because `_mercenaryName` is `meta_kind=1` which the existing scalar setters reject. Equipment-set duplicator dropped — CRIMSON-DESERT-SAVE-EDITOR's "duplicator" is a stack-count exploit, not a true duplication, and our `EquipmentSaveData` shape has no multi-loadout slot to duplicate into. |
| 4 | ~~**Sockets editor (fill / clear / swap gems, up to 5 sockets/item)**~~ | ~~MEDIUM~~ | ✅ **v1 (swap-only) shipped 2026-05-15 part 5.** Tools → Edit Item Sockets… surfaces every filled socket across inventory; per-row Change Gem… opens a gem-filtered Item Picker (`Item_Stat_AbyssGear_*` / `Item_Skill_AbyssGear_*`) → writes new gem `_itemKey` via `SetScalarField`. Fill/clear/unlock deferred per CRIMSON-DESERT-SAVE-EDITOR's safe-edit contract (empty-socket fill needs in-game Witch NPC; socket-count unlock has triple-coupled-write risk). |
| 5 | ~~**Dye editor (RGB / material / grime)**~~ | ~~MEDIUM~~ | ✅ **Shipped 2026-05-16 part 9.** Master `Tools → Edit Item Dyes…` lists every dyed item; per-row Edit opens a child slot editor (R/G/B/A NumericUpDowns + grime + material/color-group dropdowns + per-slot Apply). v1 scope = edit-existing-dye only; add-dye-to-undyed-item deferred until upstream `set_object_list_present` ABI ships. All three `dye*.pabgb` gamedata bridges from the 2026-05-16 vendor refresh bound + integrated into `LocalizationProvider` (replaces CRIMSON-DESERT-SAVE-EDITOR's `dye_slot_counts.json`). |
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
- `D:\Github\CRIMSON-DESERT-SAVE-EDITOR\CrimsonGameMods\parc_inserter3.py`
  — CRIMSON-DESERT-SAVE-EDITOR's full insert recipe. We've cribbed 6
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
  `<ItemKey>.webp` files; CRIMSON-DESERT-SAVE-EDITOR
  (`D:\Github\CRIMSON-DESERT-SAVE-EDITOR\icons_local\`)
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
  derived data. CRIMSON-DESERT-SAVE-EDITOR (`D:\Github\CRIMSON-DESERT-SAVE-EDITOR`)
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
