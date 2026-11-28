# Status / session handoff

> **Read this first on a new session.** Lean by design — it carries the
> current state, the next task, the active backlog, and the gotchas worth not
> relearning. What shipped and why, entry by entry, is in
> **[dev-log.md](dev-log.md)** (newest first); the history before 2026-06-12
> and the long-form investigations are in
> **[status-archive.md](status-archive.md)**. Look there only when you need
> the history behind a decision.
>
> Last updated: **2026-11-27** — the editor is aligned to game **2.06**
> (paver `2/6/0/0xe1bd9840`, `dlc_01` revision 11, Steam build 26949954,
> live since 2026-11-27), and **v2.06.02 is tagged, built and PUBLISHED**
> (2026-11-27T13:22:50Z, marked Latest): version bumped 2.6.1 → 2.6.2 in
> PR #61, tag → `b9034bb` (PR #61 merge), CI run `46005852913` (3 m 44 s),
> crimson-rs `0a551a6`, exe `2.6.2.36`, zip 22,155,661 B, sha256
> `f850854b…08ffd` (equal to the asset's GitHub digest and the run log).
> The downloaded zip was checked before publishing: its `.sha256` matches,
> it holds the four expected files with no `crimson_rs.dll`, the exe's
> ProductVersion ends in `b9034bb`, and it launches as
> `CrimsonAtomtic v2.06.02.36`. It ships the dye editor (re-enabled, slot
> add/remove, free colour), Known Dye Colours, the faction relation /
> contribution / node editors, the Abyss gate location columns and Add Item
> learning item knowledge (PRs #57–#60). v2.06.01 (2026-11-27T03:55:25Z) was
> the plain 2.06 alignment. slot103 is restored to its 2026-11-15 original
> and the test backups are deleted. **Next task: pick from the backlog**
> (not yet checked in-game: dye slot add/remove, Add Item knowledge, the
> Abyss gate columns, relation-group edits).
>
> **History moved to [dev-log.md](dev-log.md) (2026-11-28):** the
> per-version narratives that used to follow here (2.00 to 2.06) and the
> session changelog are now dev-log entries. Record new work there.

## Current state

As of 2026-11-28. What shipped, release by release, is in
[dev-log.md](dev-log.md).

- **Release: v2.06.02 is the current Latest** (published
  2026-11-27T13:22:50Z; tag → `b9034bb`, exe `2.6.2.36`; full record in the
  dev log). `main` and `dev` matched at `a336412` (PR #63) before the dev-log
  change. The csproj stamps `2.6.2`, so the next editor-only release is
  **v2.06.03**; a game minor bump resets `VerPatch` to 1.
- **Game: aligned to 2.06** (paver `2/6/0/0xe1bd9840`, `dlc_01` revision 11,
  Steam build 26949954). The compatibility gate is the `(major, minor)` pair
  plus a patch floor, all read from the crimson-rs C ABI, not hand-coded;
  per-version layouts in [game-versions.md](game-versions.md).
- **Saves are version-agnostic:** each save carries its own schema, so 1.05
  to 2.06 saves load and re-save in their own format. Names and icons resolve
  against the installed game, with each DLC's tables as a fallback.
- **crimson-rs is vendored at `ebf7304`** (its 2026-11-28 audit fixes), on the
  fork's `main`. CI builds exactly the commit in `vendor/crimson-rs.ref`, which
  `vendor/update_vendors.ps1` writes; commit that file with the change that
  needs the new vendor. The fork still has **no pushed version tags**
  (`git ls-remote --tags origin` returned 0 refs on 2026-11-28); the
  `v1.0.x` tags exist only in the local clone.
- **Stack:** Avalonia 12.2.0 (with `Avalonia.Controls.ColorPicker`;
  DataGrid 12.1.2), .NET 10 (`global.json` pins SDK 10.0.404 exactly,
  `rollForward: disable`), ILCompiler pin 10.0.15. SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5 and
  Tmds.DBus.Protocol 0.94.2 stay at the versions Avalonia 12.2 is built
  against; `Directory.Packages.props` says why.
- **Health:** 452 C# tests green at PR #60, against the live 2.06 install; the
  v2.06.02 AOT publish had zero IL/trim warnings.
- **Not yet checked in-game:** dye slot add / remove, Add Item learning the
  item's knowledge, the Abyss gate region / altitude columns, relation-group
  edits. slot103 is restored to its 2026-11-15 original, and no test backups
  remain.

## Feature ledger

The shipped editor surface (generic block/field editor, inventory, sockets,
dye, sealed-abyss, abyss-gates, mount-unlock, knowledge, vendor-buyback,
mercenary-rename, browsers, 32 key-resolver bridges, …) is listed in the
[README](../README.md#editor-features-current). Deep design notes per feature
are in [status-archive.md](status-archive.md).

## Open work / backlog

- **A DLC item added to a save whose owner lacks the DLC is untested
  in-game.** The pickers offer DLC items and label them in a "DLC" column
  rather than hiding them (the user's call, 2026-11-07).
- **Mission keys the bridge cannot name.** 1,420 distinct `MissionKey` values
  in slot102 stay blank, 1,415 of them already in the 2.03 snapshot. Mission
  has no `_with_pabgh` loader in crimson-rs; stage and quest do.
- **✅ Dye editor re-enabled 2026-11-27 (on `dev`; save side not yet
  checked in-game).** crimson-rs PR #122 (vendored at `c8f699d`) decoded
  the 12-byte mask: there are no groups. Each material channel's
  `mat_indices` entry is a **dye slot number** (0..11, `0xFF` = none),
  mask byte `n` flags dye slot `n`, and the save's `_dyeSlotNo` is that
  number (absent = 0); a dye element's colour applies to every channel of
  the prefab carrying its number. The editor was wrong on two counts: it
  offered `_dyeSlotNo` 0..N-1 with N = the prefab's *sub-mesh* count, and
  labelled slots from the legacy 3-byte mask. Now
  `LocalizationProvider.LookupDyeSlotNumbers` (`..._lookup_dye_slot_bits`,
  sparse, e.g. 0,1,2,4,5,6) feeds the slot count, the Add-Dye picker and
  `DescribeDyeSlot` (`..._lookup_dye_slot_material`); an absent
  `_dyeSlotNo` shows as Slot 0; `IsDyeEditorAvailable` is true and the
  "disabled" tooltip is gone. **Reach, then fixed the same day:** the old
  item → prefab *name* join reached 102 items on 2.06 and none of the
  body-type armour players actually dye (it was also wrong on 5, e.g. a
  spear resolving to a saddle hook). crimson-rs PR #123 joins through each
  item's `.prefab` files in group 0009 — a prefab lists every mesh of the
  outfit, and an item's dye slot numbers are the union over those meshes
  and over body types (`crimson_item_part_prefab_lookup_dye_slot_bits` /
  `_lookup_dye_slot_material`, ~2 s to build, so `LocalizationProvider`
  builds it lazily on the first dye lookup, which the editor runs off the
  UI thread). PR #124 merges dlc_01's items into it. Coverage: 909 of 917
  base dyeable items, 991 of 1,002 with the DLC. Vendored at `ab62800`.
  **Save side confirmed on 2.06**: the user dyed 111147 (Demeniss Honor
  Armor) in-game — 3 parts — and the save holds `_dyeSlotNo` absent(0) / 1
  / 7, `_texturePalleteKey` 1 on the first, `_grimeOpacity` 85 / 101 /
  absent against repair 33 / 20 / 0 in the UI (fits repair = 100 − grime ×
  100 / 127 with absent = 127; two points only, range unconfirmed). Pins in
  `DyeSlotNumberTests`: 111147 → 0..7 (⊇ 0, 1, 7), 9102 → {0,2,3,6,7,9},
  DLC 1004390 → 0..4, > 800 base items resolve. **Write side confirmed
  in-game on 2.06**: slot103 with gloves 12406 dyed pure red / green / blue
  on slots 0 / 1 / 2 (`_dyeColorGroupInfoKey` 3363967477) shows the three
  zones in exactly those colours, so the editor's writes load and
  **off-palette RGB renders as written** (none of the three is a palette
  colour). History below.
- ✅ **Dye follow-ups (2026-11-27, after the in-game check).** (1) The
  palette picker gained a free colour picker (Avalonia `ColorPicker`, any
  RGB, alpha off) next to the theme grid; the off-palette banner no longer
  says the engine snaps. (2) The menu item lost its "(unsupported since
  2.01)" suffix in all three languages. (3) **Known dyes are knowledge, not
  an item flag**: every `dyecolorgroupinfo` colour record is gated by an
  inline `CheckKnowledge(<KnowledgeKey>)` (crimson-rs
  `dye_color_group_info/mod.rs`, 2.06 22-byte records), and the keys are
  `Knowledge_<Theme>_DyeColor_<1..5>` (each unlocks about two palette rows;
  `Knowledge_<Theme>_DyeColor` is the theme itself, e.g. "Theoric") plus
  `Knowledge_Collection_Prop_Dyewater_NNNN` for the dye items. slot102 holds
  Her 1/2/4, Por 1/3/4, Grey 5 and the two themes: 128 dye/colour
  knowledge rows exist.
- ✅ **Tools → Known Dye Colours (2026-11-27).** `Services/KnownDyeService`
  parses `Knowledge_<Theme>[_<I..III>]_DyeColor[_<n>]` (10 themes with a
  dyer-named theme entry × 5 shades, Grey × 10 with no theme entry) and
  matches each colour to its dye item's collection entry by English name
  ("Bright Red" ↔ "Bright Red Dye"). `KnownDyesViewModel` / Window: theme
  filter with known/total counts, search, unknown-only, learn selected /
  learn all shown; learning a colour also learns its theme; "also collect
  the dye items" is opt-in (codex / achievements). Writes through
  `LearnKnowledgeAsync` (the Knowledge editor's inject); learn only, no
  forget. Pinned by `KnownDyeServiceTests` (parse cases, theme / shade
  sets, Her_1 = 1000297 / theme 2147483454 / dye item 1001352). A test save
  built from slot102 (7 → 60 of 60 colours, 61 keys, list 1,988 → 2,049,
  nothing lost) is at the session scratchpad `factiontest/slot103-knowndyes.save`,
  installed in slot103 (backup `data/_saves/slot103-before-knowndyes-test-20261127`).
  **In-game, first look (2026-11-27):** the journal page Adventure Log →
  Knowledge → "Dye recipes" stayed at 7/50 — that page counts the 50
  `Knowledge_Collection_Prop_Dyewater_*` dye items (red 4/5 = Dyewater
  0001–0004, exactly slot102's), which the test deliberately left out. Whether
  the dyehouse palette unlocked is **not checked yet**; the DyeColor entries
  may not show in the journal at all. **Asked crimson-rs** ("crimson-rs main
  session") for `crimson_dye_color_group_info_palette_knowledge_at` (the
  KnowledgeKey of each palette position's inline `CheckKnowledge`) plus the
  RE answers: DyeColor vs Dyewater keys, keys per group, any group-level
  condition, group ↔ theme code / display name. **Upstream answered the same
  day (uncommitted there, waiting for the user's commit / PR call):** new
  `crimson_dye_color_group_info_palette_knowledge_at` (UNSUPPORTED_VERSION on
  pre-2.06 8-byte records) and `..._lookup_display_name` (PALOC theme name,
  "Hernandian Dye"; the three Dem groups share "Demenissian Dye"). The 1,090
  records use exactly the 60 DyeColor keys (no Dyewater, so the journal's
  7/50 is a separate system — confirms the reading above). Every group:
  positions 0–8 = its own Grey_DyeColor_n (Her 1, Dem_I 2, Dem_II 3, Dem_III 4,
  Kwe 5, Del 6, Cal 7, Por 8, Tom 9, Bar 10), then 20 positions per shade 1..5
  (9–28, 29–48, 49–68, 69–88, 89–108); no key is shared between groups. So the
  editor's name mapping is right, except that "Grey (every theme)" is wrong:
  each Grey_n belongs to one theme. The theme entries
  `Knowledge_<Theme>_DyeColor` are not referenced by dyecolorgroupinfo, and
  no group-level condition exists, so what puts a theme on the dyer's list is
  still open — the slot103 in-game check decides it.
  **Merged as crimson-rs PR #125 (`0a551a6`), vendored and integrated the
  same day**: `NativeDyeColorGroupInfoCatalog.PaletteKnowledgeAt` /
  `LookupDisplayName`, `LocalizationProvider.ResolveDyeThemeName`;
  `KnownDyeService.Scan` now builds entries from the palette (falls back to
  the names without it): each theme has six rows, shade 0 = its own grey
  (9 colours) and shades 1–5 (20 each), each with the swatches it unlocks;
  the theme label is the game's name ("Hernandian Dye / 埃爾南德染劑 (Her)").
  Pinned by `KnownDyeServiceTests.LivePalette_MapsSixKeysPerTheme`.
  **In-game (2026-11-27): the colour knowledge works.** With all 60 colours
  + 10 themes learned, the Hernand dyer's palette is fully unlocked (every
  "?" gone, grey row and the previously missing Red / Deep Red shades
  included). Only the Hernandian theme shows there: a dyer offers their own
  theme, so the other nine need their dyers (or a made dye) to check. Next
  test in slot103 (backup `data/_saves/slot103-before-dyeitems-test-20261127`):
  2 each of Bright Blue / Bright Green / Bright Purple Dye (1001630 /
  1001645 / 1001625; bag 2, cat 3001, type 31, stack 50, each item's
  KnowledgeInfo is its Dyewater collection entry), cloned from a type-31
  stack in bag 2 — to see what using a made dye does.
  **Result (2026-11-27):** the three stacks show in the bag but as "Unknown
  item" (? icon); using one gives no message and teaches nothing (journal
  stays 7/50). After a reload, selling them to the dyer and opening Buyback,
  they were recognised before buying back ("also obtain 2 knowledge"); bought
  back, the journal reads 10/50 (Bright Purple / Bright Blue / … each 1/5).
  So an item is "known" through its iteminfo `KnowledgeInfo` (here the
  `Knowledge_Collection_Prop_Dyewater_*` entry = the journal's "dye
  recipes"), which the game teaches when the item is acquired through its
  own paths, not when it merely appears in the bag. Using a dye unlocks no
  theme at a dyer: each dyer still offers only their own theme. **Lesson for
  Add Item:** an added item whose KnowledgeInfo is unlearned shows as
  unknown — **done (2026-11-27)**: `AddItemToCurrentListAsync` and
  `GrantItemToContainerAsync` now learn the item's KnowledgeInfo when the
  save lacks it (`LearnItemKnowledgeAsync`, noted in the status line).
- ✅ **Dye UI text (2026-11-27).** The slot editor's theme combo (column now
  "Dye theme") shows the game's names ("Demenissian Dye / 德梅尼斯染劑
  (Dem II)"); the palette picker's header / sub-header are localized, it
  explains that a dyer offers only their own theme and only known colours,
  each cell's tooltip names the knowledge that unlocks it, and the grid
  starts at column 1 so its rows line up with the dyehouse's (empty first
  cell, then the 9 greys).
- ✅ **Dye slot editor: add / remove slots (2026-11-27).** The user found Edit
  could only change existing elements and "+ Add" made exactly one. The slot
  editor now has an "Add dye slot" combo (the item's dye slot numbers no
  element uses, "Slot 2 (cloth)") that clones element 0 and clears every
  scalar but `_dyeSlotNo` (the same blank element "+ Add" makes; on an
  absent list it materialises one from a sibling template), and a per-row
  Remove (`ListRemoveElement`; the last one makes the list absent, like an
  undyed item). Both reload the rows. Exercised on slot102's 111147 in
  memory: add 2, remove 1, remove all, add 7 from empty, add 0, write, reload
  — clean, 0 undecoded. Not checked in-game.
- ✅ **Abyss gate editor: region, altitude, position, filters
  (2026-11-27).** `_ownerLevelName` is an inline `staticstringA`; the
  dialog showed "<18 items, 18 bytes>" until it read the bytes through
  `GetInlineBytesField`. Region is guessed from the level name
  (`AbyssRuins_Her_0030`, `Challenge_Sealed_Artifact_Dem`; `AbyssIsland_`,
  `AbyssBridge_`, `AbyssOne_` = Abyss) through the shared
  `Services/GameRegions.cs` (also the faction-node editor's mapping now).
  Height comes from `ListFieldPositions` (gimmick transforms): on slot102
  ground ruins sit at 367–1,035 and sky pieces at 1,775–3,0xx, so
  `SkyMinHeight` = 1,400 splits them (211 ground / 195 sky of 406 rows).
  Search box + region / altitude combos; pinned by `AbyssGateRegionTests`.
- ✅ **NuGet refresh (2026-11-27).** Avalonia 12.1.1 → 12.2.0 (BuildServices
  12.0.0, ColorPicker added at 12.2.0; DataGrid 12.1.2 is still its
  latest), MicroCom.Runtime 0.11.7 (Avalonia 12.2 needs it), xunit.v3 4.0.3
  / runner 4.0.1, Test SDK 18.12, MTP 2.5.3, Microsoft.Extensions.* and
  System.* 11.0.0 (they ship net10.0 builds), Azure / OpenTelemetry /
  ApplicationInsights to latest, OpenTelemetry 1.21 pinned transitively.
  **Held on purpose**: SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.5 (Avalonia
  12.2.0 is built against 3.x / 8.x; 4.x / 15.x are new majors),
  Tmds.DBus.Protocol 0.94.2 (Avalonia.FreeDesktop needs ≥ 0.94.1; 0.95 is a
  0.x minor), `runtime.win-x64.Microsoft.DotNet.ILCompiler` 10.0.15 (must
  match the .NET 10 runtime; 11.0.0 is .NET 11's). 439 tests green, AOT 0
  warnings.
- ~~Dye editor is disabled since 2.01 — parked~~ (the user's call, stated in the v2.03.01 release notes,
  2026-09-18); it needs the mask-group RE. **Re-checked 2026-11-27 on 2.06
  (the user thought upstream had fixed it): it has not.** No crimson-rs
  branch touches the mask after 2.05's named-row fix (that one restored
  0-row reads on 2.04, not the group selection). Measured on 2.06 through
  `_lookup_slot_mask_full`: 1,703 prefabs / 6,955 slots, 12-byte mask,
  non-zero groups per slot 0×1,023 / 1×4,519 / 2×1,332 / 3×81 / 4×0, and the
  legacy 3-byte getter reads blank on **2,304 (33.1%)** slots whose full
  mask is set. (Still
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

## Gotchas — don't relearn these

Condensed; the exhaustive list (PALOC keying internals, per-TypeName
name-resolution coverage, nested-edit path mechanics, fill-stack regimes,
window-restore quirks, etc.) is in
[status-archive.md → "Important context / gotchas"](status-archive.md).

- **A green suite says nothing about keys it never asks for.** On 2.04 the
  untouched suite failed only five pins, while every 2.04-written save held
  rows the editor could not name, because no test walked a save's keys through
  the resolver. On a new patch, count unresolved distinct keys per TypeName
  over the newest save, and attribute each group to a base table, a DLC table
  or the previous snapshot before calling it content.
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
- **A deferred batch changes which primitives are valid, not just how fast
  they are.** Inside a `RunDeferred` batch the tree is re-decoded only at
  commit, so any code path that has only ever run in immediate mode is
  untested against the deferred contract. With crimson-rs `ebf7304`
  vendored: a field promoted inside a batch can be written in place (its
  length is checked against the schema size, not a stale range);
  `set_object_list_present(1)` on a list that is already present is a
  no-op; earlier ops in a batch stay applied when a later one fails (no
  savepoint), so a batch body throws to abort rather than returning; and
  the mutation version only moves forward, an abort included. The history
  is in dev-log.md (2026-11-28).
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
- **"No save-body drift" is about the format, not the schema.** Each save
  embeds its own schema, so the decoder follows a class gaining or losing
  fields for free — but anything that carries *captured bytes* from one save
  into another is pinned to the schema they came from. `MercenarySaveData`
  lost `_occupationState` between 1.12 and 2.00 and gained
  `_shipStationSaveList` in 2.01 while every alignment reported "no
  save-body drift", and the dragon's captured element broke without a
  sound because its only test ran on a June save. Remapping type indices by
  class name is not enough; fields have to be mapped by name too — which is
  what crimson-rs element templates do
  (`crimson_save_export_element_template`). And a live-save test pinned to
  a slot tests that slot's patch, not the game — use `LiveSaves` (newest
  first). Before reading a field-level diff between two saves, compare their
  schema signature (`type_count`, `schema_end`, the class's field count):
  if it differs, the same field index names different fields. File mtime
  proves nothing, because the editor preserves it.
- **Sealed Abyss challenges: write the pre-claim shape, not the post-claim
  one.** Pattern B v1 flips only the FAR tracker (negative key = adjacent
  twin's key − 1) and appends the `X_2` follow-up sub-mission; the artifact
  item, the catalog row and the negative-key visibility twin stay untouched,
  and the engine fills them in when the player claims. Pattern A (writing the
  catalog's `_state=5` / tags directly) corrupted visible cards — don't retry
  it. Multi-objective shapes (Living_* / Cooking) are out of scope. History:
  status-archive.md, 2026-05-15..17.
- **An absent dynamic array / object list is one `0x01` byte.** Every other
  absent field is zero bytes, and a present array / list opens with `00` +
  a u32 count. Until 2026-09-18 the decoder skipped absent fields without
  consuming that byte, so values behind an absent list were misread, and
  older notes in [status-archive.md](status-archive.md) quote some of
  them: the dragon's "packed TStat" `_currentHp`
  (`01 00 01 01 01 [u16] 00`) and Silver Fang's `_currentHp=494784544309249`
  (same shape) are markers + `_occupationState` + the real value's low
  bytes. `_currentHp` is a plain u64. Anything that builds element bytes
  has to write the markers; crimson-rs does in
  `make_empty_element_bytes`, templates and list make-absent.
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
- **crimson-rs is read-only here** and CI **checks out the commit in
  `vendor/crimson-rs.ref`** (`scripts/checkout_crimson_rs.ps1`; the local
  `vendor/` clone is gitignored, not used by CI). Land any crimson-rs fix on
  `bbfox0703/crimson-rs` `main`, refresh the vendor and commit the new ref
  *before* tagging a release.
  `main` is branch-protected (clippy `-D warnings` + `cargo test`); always go
  via PR; never push to upstream `potter420/crimson-rs`.
- **Run tests with `dotnet run --project src/CrimsonAtomtic.Tests`** — the
  .NET 10 SDK removed the `dotnet test` → VSTest bridge that MTP (xunit.v3)
  used to ride on. `build.ps1 -Target Test` and `build_ui.ps1 -Test` call the
  test project's own runner too.
- **`Microsoft.NET.Test.Sdk` and `Microsoft.CodeCoverage` ship as a matched
  set.** They are versioned and released together (Test.Sdk depends on the
  same-version CodeCoverage), so bumping one alone leaves a silent mismatch —
  restore still succeeds and the tests still pass, so nothing tells you. The
  2026-08-18 bump moved CodeCoverage 18.6.0 → 18.9.0 and left Test.Sdk behind
  at 18.6.0; both are now 18.12.0. When bumping either, bump both.
- **The ILCompiler central pin tracks the installed SDK's runtime.** A newer
  SDK auto-injects a newer `Microsoft.DotNet.ILCompiler`, which then demands
  `runtime.win-x64.Microsoft.DotNet.ILCompiler >=` that version → **NU1109 at
  restore**, blocking every build. Bumped 10.0.8 → 10.0.9 (1.11) → 10.0.10
  (1.14) → 10.0.11 (1.18, SDK 10.0.400) → 10.0.12 (2.02, SDK 10.0.401 —
  it failed even the untouched pre-edit baseline test run) → 10.0.14 (2.04,
  SDK 10.0.403) → **10.0.15** (2.05, SDK 10.0.404). Expect this every time the
  SDK moves; it is not related to the game patch. At 2.05 the .NET 10 SDK had
  vanished entirely (only 11.0.100 was left), so `global.json` matched nothing
  and every `dotnet` command failed; reinstalling the .NET 10 SDK, not
  retargeting, is the fix. `global.json` now pins the SDK exactly
  (`rollForward: disable`) and CI installs that version from it, so moving
  the SDK means editing `global.json` and this pin together.
- **CI's Rust and yours can differ.** Both workflows pin the toolchain
  (`RUST_TOOLCHAIN`, 1.100.1, in `ci.yml` and `release.yml`), while this
  machine runs whatever `rustup` has. A new Rust can change what the
  staticlib needs at link time: Rust 1.100's std needed
  `Synchronization.lib` in the AOT link and broke the first v2.05.01 cut
  while every local build passed. When a link fails only in CI, compare the
  `rustc` line in the run log with `rustc --version` here first, and get the
  staticlib's needs from rustc's `print native-static-libs` option.
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
  2026-08-26 15:48**, built into its own target dir, so the `CARGO_TARGET_DIR`
  split below has been exercised on this machine. Verified by round-tripping the live 2.00 `iteminfo.pabgb` through
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
  that reads like a broken ABI but is really a build-artifact collision.
  `setup_python_env.ps1` now sets `CARGO_TARGET_DIR` to
  `vendor/crimson-rs/target/py` for the maturin call only (inside the crate's
  ignored `target/`, so `update_vendors.ps1` does not see a dirty clone). If you see `EntryPointNotFoundException : Unable to
  find an entry point named 'crimson_…'`, check the dll's **size** before
  suspecting the ABI. The two builds differ by roughly 150 KB, and that gap —
  not any absolute number — is the tell: **don't memorise the byte counts,
  they move with every patch.** At 2.03 the c_abi build is **1,129,984 B** and
  the PyO3 one **1,286,656 B**, a ~153 KB gap (1,136,640 / 1,272,320 at 2.02;
  1,114,112 / 1,272,832 at 2.00; 1,097,216 / 1,274,368 at 1.18).
  Windows Explorer and `Get-Item .Length/1KB` divide by 1024, so they show the
  2.03 pair as **1,103.5 KB** and **1,256.5 KB** — don't be thrown when those
  disagree with decimal-KB (÷1000) figures.
- **Avalonia 12 quirks**: DataGrid is at **12.1.2 while core is 12.2.0** —
  it ships on its own cadence, so a version mismatch between the two is
  expected and is **not** something to "fix" by pinning them together;
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

## History

What shipped, and why, is in [dev-log.md](dev-log.md) (newest first).
History before 2026-06-12 is in [status-archive.md](status-archive.md).
