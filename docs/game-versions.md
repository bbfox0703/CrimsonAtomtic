# Game versions

## Current install

`D:\SteamLibrary\steamapps\common\Crimson Desert` — **version 2.01**

Top-level layout:

```
Crimson Desert/
├── 0000/ ... 0035/      # 36 asset pack groups, each contains *.paz + 0.pamt
├── bin64/               # CrimsonDesert.exe, DX12, DLSS/FSR/XeSS, Steam, Sentry
├── CDMods/              # cdumm.db (SQLite mod registry) + vanilla/ unpack cache
├── gamedata/            # <lang>/*.paloc (2.01; one blob per language before)
├── meta/                # 0.papgt (master pack tree), 0.pathc, 0.paver (version stamp)
├── mods/                # _enabled/, _asi/, _lang/ (user mods)
└── config.json
```

- **Engine**: proprietary Pearl Abyss engine, DirectX 12.
- **Total size on disk**: ~126 GB.
- **Version stamp**: `meta/0.paver` is 10 bytes, binary-encoded. Layout
  is fully decoded (see `crimson-rs` `src/binary/paver.rs`, exposed via
  `crimson_paver_read_from_*`): three little-endian u16s `(major, minor,
  patch)` followed by a little-endian u32 `build`. The schema-compatibility
  key is the **`(major, minor)` pair** — it used to be described as "the
  minor", which was true only while the major sat at 1 for every shipped
  patch. **Game 2.00 is the first major bump, and it RESETS the minor**
  (1.18 → 2.00 is `18` → `0`), so a bare minor no longer identifies a schema:
  minor `0` means 2.00 today and would have meant a hypothetical 1.00
  yesterday. Live 2.01.00 install:
  `02 00 01 00 00 00 cb 5f 1e a3` → major 2, minor 1, patch 0,
  build `0xa31e5fcb`. (2.00.00 was `02 00 00 00 00 00 14 d2 41 b2`,
  build `0xb241d214`; 1.18.00 was `01 00 12 00 00 00 0f 7c 57 28`,
  build `0x28577c0f`; 1.17.00 was `01 00 11 00 00 00 97 4c 5e d0`,
  build `0xd05e4c97`; 1.16.00 was `01 00 10 00 00 00 e1 6d 1d 8d`,
  build `0x8d1d6de1`; 1.15.00 was `01 00 0f 00 00 00 e1 88 84 6a`,
  build `0x6a8488e1`; 1.14.00 was `01 00 0e 00 00 00 f8 42 7d 59`,
  build `0x597d42f8`; 1.13.00 was `01 00 0d 00 00 00 0d 2c 6a 53`,
  build `0x536a2c0d`; 1.12.00 was `01 00 0c 00 00 00 02 84 73 ac`,
  build `0xac738402`; 1.11.00 was `01 00 0b 00 00 00 24 7a 2c 20`,
  build `0x202c7a24`; 1.10.00 was `01 00 0a 00 00 00 ac b2 84 cf`,
  build `0xcf84b2ac`; 1.09.00 was `01 00 09 00 00 00 24 48 f3 bb`,
  build `0xbbf34824`; 1.08.00 was `01 00 08 00 00 00 3e b0 39 dc`,
  build `0xdc39b03e`.) The editor reads this at startup and warns when the
  install's `(major, minor)` isn't one the parser can load —
  `GameDataVersion.IsCompatibleWithParser` now gates on
  `Major == ParserTargetMajor` **and** `Minor ∈ CompatibleMinors`
  (currently `2` and `{0}`). The major half was added at the 2.00 alignment
  and is not cosmetic: with the minor reset to `0`, the old minor-only test
  would have reported a hypothetical `1.00.xx` install as compatible with the
  2.00 parser. **2.00 carries two iteminfo layout drifts** (crimson-rs commit
  `0f2363b`): (1) `SubItem` gained a payload-free `type_id == 18` — the same
  renumbering 1.12 (16) and 1.13 (17) did, with no item still reading
  14..=17 — and (2) a new always-zero `u32` (`unk_pre_max_endurance_a`) sits
  directly ahead of the 1.12-era `unk_pre_max_endurance`. Iteminfo went
  6,573 → 6,810 items (+237, none removed), 6,190,316 → 6,446,719 B;
  `skill.pabgb` grew to 2,046 entries with no drift. Because those changes
  shift every item, a 1.18 install really does mis-decode against this
  build — the flagged incompatibility is **substantive**, like
  1.17-on-a-1.18-build and 1.15-on-a-1.16-build, not the target-only
  convention it was at 1.14/1.15/1.17. `ParserTargetMajor.ParserTargetMinor`
  = `2.0` is what the dialog displays (it used to hard-code the `1.` prefix,
  which would have rendered as `1.00.xx` here). All three values are read from
  the crimson-rs C ABI, not hand-coded. The **C ABI surface only grew** —
  `crimson_parser_target_gamedata_major()` is purely additive, nothing
  existing moved — so the C# interop change was one new `LibraryImport`.

## Historical versions

Used for **cross-version diffing** when the game patches and a parser breaks.

| Path                              | Version  | Storage |
| --------------------------------- | -------- | ------- |
| `F:\Crimson Desert\1.06.01\`      | 1.06.01  | SSD     |
| `X:\Crimson Desert\1.05.01\`      | 1.05.01  | HDD     |
| `X:\Crimson Desert\1.04.01\`      | 1.04.01  | HDD     |
| `X:\Crimson Desert\1.03.01\`      | 1.03.01  | HDD     |

Each subfolder contains the same layout as the current install (pack groups + bin64 + CDMods + meta + mods).

**Migration note**: SSD on F: is finite. Older versions may move to X: as new patches arrive. Code that scans for historical installs should accept multiple roots, not hardcode F: or X:.

## Save files

Per-user, not per-version:

```
%LOCALAPPDATA%\Pearl Abyss\CD\save\<UserID>\
├── slot0/
│   ├── save.save     # main state, ~1.6 MB
│   └── lobby.save    # quick-select metadata, ~506 B
├── slot1/ ... slot105/
└── steam_autocloud.vdf
```

- `<UserID>` is the Pearl Abyss account ID (numeric, e.g. `102190433`).
- Magic header on both files: ASCII `SAVE`.
- Body is ChaCha20 + HMAC + LZ4 — see [save-format.md](save-format.md).

## Version detection (planned approach)

1. Read `meta/0.paver` if present in the chosen install root — authoritative when format is decoded.
2. Fall back to comparing iteminfo item count and pamt directory hashes against known fingerprints (1.03/1.04/1.05/1.06/1.07 each have distinct sets).
3. Save files do not embed a game version directly. We infer compatibility from the TOC layout and field hashes, similar to how `crimson-rs` auto-detects skill format flags.

## Diffing playbook

When a new patch lands and a parser fails:

1. Snapshot the new install (manual: rename the live folder to `<root>\<ver>\`, restage from Steam).
2. Run `tools/diff/diff_iteminfo.py --from <old_root> --to <new_root>` to enumerate added/removed/changed items.
3. Run `tools/diff/diff_pamt.py` to find which pack groups changed (most patches only touch a few).
4. If a binary format's bytes shifted, narrow the field range with `crimson-rs`'s `BinaryReadTracked` trait, then patch the parser. Add a fixture + regression test.

The 1.05 → 1.06 jump turned out to be **zero schema changes** (only +17 items), so the parser stayed the same. The 1.06 → 1.07, 1.07 → 1.08, and 1.08 → 1.09 jumps similarly stayed data-only — the editor loads them all through one schema path. 1.08 → 1.09 in particular was content-only with **no schema drift** (`crimson-rs` commit `0619789`): iteminfo byte-identical to 1.08, all 30 gamedata tables parse, save read/decode/mutate/write-reseal roundtrip clean; per-table key deltas vs 1.08 were character +6, skill +5, knowledge +5, factionspawn +1, gimmick −8, the other 25 tables unchanged. We expect most patches to be data-only; structural changes are the exception (e.g. the slot104 / 1.05-era 23-field `ItemSaveData` vs 1.06+'s 25-field shape — see [status.md](status.md)), and our pipeline is built to make those exceptions detectable.

**1.09 → 1.10 was the first iteminfo schema drift since the 1.05/1.06-era ItemSaveData change** — the data-only streak ended. Two iteminfo layout changes (`crimson-rs` commit `dd2ed2e`, byte-perfect on all 6,325 items): (1) **removed** `money_icon_path: StringInfoKey` (the 4-byte `0x73e1c5ea` "no money icon" stub between `map_icon_path` and `use_map_icon_alert`), and (2) **added** `UnitData.unk_post_icon_path: u32` between `icon_path` and `item_name` (populated on `MoneyTypeDefine` — camp/contribution currencies, pinball coin). Separately, the save body changed too: 1.10 widened the `ContentsMiscSaveData` ReflectObject-list leading-pad from 3 to 4 bytes (`crimson-rs` `f1513b8`) — the decoder's leading-pad scan was extended 0..=3 → 0..=4; **without that fix the editor silently corrupted any 1.10 save it wrote** (107 KB undecoded → dropped on re-encode). The furthest-reach tiebreak keeps 1.09 and older saves byte-identical, so old-save load/round-trip is unaffected. The parser now targets 1.10 exclusively (`CompatibleMinors = {10}`).

**1.10 → 1.11 was a second consecutive iteminfo schema drift, but with NO save-body change.** iteminfo (`crimson-rs` commit `8fdeb45`, byte-perfect on all 6,333 items, +8 vs 1.10): a new boolean `u8` `unk_post_apply_drop_stat_type` is inserted between `apply_drop_stat_type` and `drop_default_data`, so every item grows by exactly one byte at the `drop_default_data` boundary (RE'd by a tandem byte-walk against the kept real-1.10 binary; anchored export ok=6,333, leftover=0, fail=0). The save body did **not** drift this time: the format is unchanged (v2 / flags `0x0080`), every live slot (`slot0/1/2`, `slot100`–`slot108`) parses with `hmac_ok` and body decode `undecoded_bytes=0`, and a body-stable write round-trips — including `slot100` (old-format) and `slot102` (its 1.11 save-as), which both decode/re-encode clean. The parser now targets 1.11 exclusively (`CompatibleMinors = {11}`); 1.10 iteminfo no longer round-trips against it, so a user still on 1.10 is warned. Per-table gamedata deltas captured in `crimson-rs` `data/gamedata-keys-1.11/` (e.g. `gameplayvariableinfo` 47 → 55).

**1.11 → 1.12 was a third consecutive iteminfo schema drift, again with NO save-body change.** iteminfo (`crimson-rs` commit `0694dfb`, byte-perfect on all 6,483 items, +150 vs 1.11) drifted in four places, RE'd by a tandem byte-walk against the kept real-1.11 binary: (1) a payload-free `SubItem` `type_id == 16` variant (15 → 16 on 4,496 items); (2) an unconditional `unk_pre_max_endurance: u32` before `max_endurance`; (3) a sibling-value-gated `unk_pre_gimmick_visual: u32` (present when `equip_type_info != 0 || item_type == 74` — the first sibling-gated field, which extended the `py_binary_struct!` macro with a `=> <cond>` conditional-field form); and (4) inter-element `u32` separators in `EnchantData` (N−1 per N elements, via the new `EnchantDataList`). `serialize_iteminfo` round-trips byte-identical on the live binary (export ok=6,483, leftover=0, fail=0). Separately, `partprefabdyeslotinfo` drifted (−143 rows, 1,111 → 968, plus a new 5-byte per-slot field — `u8` + `u32`, uniformly `0xFF`/0); the dye-editor bridge parses the live 1.12 table again. The save body did **not** drift: format unchanged (v2 / flags `0x0080`); the new-format `slot106` / `slot107` both parse `hmac_ok` with `undecoded_bytes=0` (1107 blocks, 3098/3098 fields decoded) and re-seal decode-stable. The one save-side change is a `relocate_trailing_pad_offsets` bug fix (confined to trailing_pad byte ranges so the offset-relocation pass no longer rewrites decoded content that coincidentally equals `old_off + p + 4` — fixes the clear-then-set and batch-vs-single mutation round-trip invariants). The parser now targets 1.12 exclusively (`CompatibleMinors = {12}`); 1.11 iteminfo no longer round-trips against it, so a user still on 1.11 is warned. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.12/` (30 tables, 94,608 keys).

**1.12 → 1.13 was a fourth consecutive iteminfo schema drift, again with NO save-body change.** iteminfo (crimson-rs tag `v1.0.13.x`, byte-perfect on all 6,508 items, +25 vs 1.12) drifted in the item-payload layout: (1) the payload-free `SubItem` variant's `type_id` bumped 16 → 17 (both sites); and (2) the former `prefab_data_list` and `gimmick_visual_prefab_data_list` were merged into a single `MergedPrefabVisualData` block relocated to the *end* of each item (the enchant-data list and the equip/gem-gated `unk_pre_gimmick_visual` stay in the middle; a constant `0xff00` item tail follows). `serialize_iteminfo` round-trips byte-identical on the live binary. Separately, `partprefabdyeslotinfo` grew 968 → 1,538 rows (+570) and the 1.12 `(0xFF, 0)` 5-byte per-slot pad was RE'd as a `u8` marker + `u32 extra_layer_count`; 1.13's new dyeable gear sets `count = 1`, adding a second material/dye layer (`DyeExtraLayer`) exposed via four new *additive* getters (`crimson_..._lookup_slot_extra_layer_{count,material,mask,flag}`); the same schema refinement also recovered 9 new-gear rows the old blind-pad model could not parse (1,529 → all 1,538). The existing C# dye bridge still parses the live 1.13 table (additive change — surfacing the 2nd layer in the UI is optional feature work, not a correctness requirement). The save body did **not** drift: format unchanged (v2 / flags `0x0080`); `slot107` is the live 1.13 native save and parses `hmac_ok` with `undecoded_bytes=0`, and every live slot round-trips decode-stable. The parser now targets 1.13 exclusively (`CompatibleMinors = {13}`); 1.12 iteminfo no longer round-trips against it, so a user still on 1.12 is warned. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.13/` (30 tables). Note one game-side content shuffle rather than a parse drift: the `Pyeonjeon_Arrow` (key 2200) `item_type` was remapped 0 → 23. **This alignment also retired the manual `ParserTargetMinor` / `CompatibleMinors` bump chain (8→9→10→11→12→13):** the C# values are now read from the crimson-rs C ABI (`crimson_parser_target_gamedata_minor()` + `crimson_parser_compatible_gamedata_minors()`, commit `a3ab5ee`), so Rust is the single source of truth. (Editor `VerMinor` still tracks it as a manual lock-step build-identity bump.)

**1.13 → 1.14 was CONTENT-ONLY — no schema drift in any subsystem.** After four consecutive iteminfo schema drifts (1.10 → 1.11 → 1.12 → 1.13), 1.14 broke the streak: the iteminfo item **values** changed but the layout is byte-identical to 1.13 (crimson-rs tag `v1.0.14.x`, `serialize_iteminfo` round-trips byte-perfect on all 6,508 items, 0 skipped), and the save body, `skill.pabgb`, and all 30 gamedata bridges parse unchanged. The gamedata-key diff vs 1.13 is a single row (`knowledgeinfo −1`; 95,185 keys across 30 tables). The save body did **not** drift: format unchanged (v2 / flags `0x0080`); `slot107` is the live 1.14 native save (paver `1/14/0/0x597d42f8`, 2026-07-17) and parses `hmac_ok` with `undecoded_bytes=0`, and every live slot round-trips decode-stable. The **only** parser change was the version pin `PARSER_TARGET_GAMEDATA_MINOR` 13 → 14; because the C# `ParserTargetMinor` / `CompatibleMinors` have been ABI-sourced since 1.13, the editor picked up the new target with no hand-edit beyond the manual `VerMinor` lock-step bump (13 → 14) and the version-pin test refresh. `CompatibleMinors` stays a single-element allow-list (`{14}`) by convention, so 1.13-and-earlier installs are warned even though the 1.13 layout is byte-compatible. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.14/` (30 tables).

**1.14 → 1.15 was CONTENT-ONLY — no schema drift in any subsystem** (a second content-only patch in a row, following the 1.10 → 1.13 run of four consecutive iteminfo drifts). The `iteminfo.pabgb` keeps the exact 1.13/1.14 layout — only item field **values** changed (identical 5,938,891 B; SHA256 `c7ae5543…` vs 1.14 `de621624…`) — and the save body, `skill.pabgb`, and all 30 gamedata bridges parse unchanged (crimson-rs commit `82d0bae` / tag `v1.0.15.x`; `serialize_iteminfo` round-trips byte-perfect on all 6,508 items, 0 skipped — anchored export ok=6,508, leftover=0, fail=0). The gamedata-key snapshot in `crimson-rs` `data/gamedata-keys-1.15/` (30 tables, 95,185 keys) is **byte-identical to 1.14** — zero key changes in any table. The save body did **not** drift: format unchanged (v2 / flags `0x0080`); the live 1.15 install stamps paver `1/15/0/0x6a8488e1` (2026-07-24), and every live slot round-trips decode-stable. The **only** parser change was the version pin `PARSER_TARGET_GAMEDATA_MINOR` 14 → 15; because the C# `ParserTargetMinor` / `CompatibleMinors` have been ABI-sourced since 1.13, the editor picked up the new target with no hand-edit beyond the manual `VerMinor` lock-step bump (14 → 15) and the version-pin test refresh. `CompatibleMinors` stays a single-element allow-list (`{15}`) by convention, so 1.14-and-earlier installs are warned even though the 1.14 layout is byte-compatible.

**1.15 → 1.16 was a STRUCTURAL drift — the largest since 1.13, and the first patch ever to break the skill parser.** The two-patch content-only streak (1.14, 1.15) ended. crimson-rs commit `e81acc5` / tag `v1.0.16.x`; verified against the live 1.16 install (paver `1/16/0/0x8d1d6de1`, 2026-08-01).

*iteminfo* (6,508 → 6,581 items; 5,938,891 → 6,145,386 B) drifted in **four** places, RE'd by a tandem byte-walk (`scripts/diff_115_116.py`) against the kept real-1.15 binary: (1) the head-side `inventory_info: InventoryKey` was **removed**; (2) `DockingChildData::unk_post_summon_tag: u8` (added back in 1.08) was **removed** — a conditional field visible only on the 391 items that carry `docking_child_data`, and that discriminator partitions the table with fp=0/fn=0; (3) a 10 + 28·N byte block (`u32` + `u8` flag + `CArray<UnkPreRespawnData>` + `u8`) was **inserted** before `respawn_time_seconds`, with `unk_pre_max_endurance` swapped to sit *before* it — only 14 items have a non-empty list, so the block costs a flat 10 B elsewhere, which is why it first read as a fixed 10-byte insert; and (4) `inventory_info` **reappears at the item END** as `inventory_info_list: [u16; 9]`, absorbing the 1.13-era constant `unk_tail` as slot 8. Anchored export ok=6,581, leftover=0, fail=0, no_anchor=0; full `serialize_iteminfo` round-trip byte-identical.

*skill* (1,999 → 2,013 entries) took its **first-ever** drift: `PostBuff::unk_pre_damage_type: u8` before `damage_type`, so every entry gains exactly 1 B. Before the fix 589 of 2,013 entries failed to parse — the `BuffData` brute-force tail probe does not cover `PostBuff`, so unlike previous patches this drift was *not* absorbed automatically (`try_parse_post_buff_end`'s flag block went 6 → 7).

Two RE findings worth keeping: (a) **a byte-identical round-trip does NOT validate field boundaries** — the first 1.16 model round-tripped perfectly while `respawn_time_seconds` decoded as `-4294967296`; the *value distribution* (0 / −1 / 604800 = 7 days) is what caught the 4-byte error. (b) `[u16; 9]` rather than 8 + a separate tail: all 9 × 6,581 slot values fall in `{1,2,3,5,6,7,8,9,10,13,14,255}` with no out-of-domain `u16`, and slot 7, slot 8 and `unk_pre_max_endurance` deviate on exactly the same 59 `Trade_*_PackedInVehicle` items.

The save body did **not** drift: format unchanged (v2 / flags `0x0080`); all 12 live slots parse `hmac_ok` with `undecoded_bytes=0`. The **C ABI surface is unchanged** — `CrimsonItemInfoSummary` still exports `inventory_info`, now sourced from `inventory_info_list[0]`, which is byte-for-byte the value that field carried pre-1.16 — so no C# interop change was needed and the 80-byte struct pin still holds. `CompatibleMinors` = `{16}`; unlike 1.14/1.15 this is a **genuine** incompatibility rather than the target-only convention, since 1.15 data really does mis-decode. Soft test pins bumped alongside: `gameplayvariableinfo` 57→56, `itemgroupinfo` 1550→1597, `mercenaryinfo` 19→21, `reserveslot` 30→28, and `Pyeonjeon_Arrow` `item_type` 23→0 (a game-side enum remap — the second for this key, which read 0 through 1.12 and 23 in 1.13–1.15; `item_tier` / `quick_slot_index` / flags unchanged, which is what proves the field is still aligned). Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.16/` (30 tables, 96,076 keys, +891; 13 tables changed, all PABGH shapes still auto-detect).

**1.16 → 1.17 was CONTENT-ONLY — no schema drift in any subsystem.** The structural 1.16 patch was a one-off; 1.17 goes back to the 1.14/1.15 pattern. crimson-rs commit `0767361` / tag `v1.0.17.x`, validated against the live 1.17 install (paver `1/17/0/0xd05e4c97`, 2026-08-09). The only crimson-rs code changes are version pins — the iteminfo parser itself is untouched.

*iteminfo* went 6,581 → **6,572** items (6,145,386 → 6,139,734 B, SHA256 `f5d1ba50…`): nine keys were removed (1004912–1004920, the `Item_Set_*_Tier0_Reminiscence` block) and none added. Anchored export ok=6,572, leftover=0, fail=0, no_anchor=0, and `serialize_iteminfo` round-trips byte-identical. The check that proves this is content-only rather than a layout drift masked by compensating value changes: of the 6,572 surviving items **6,435 are byte-identical, 137 changed values, and zero changed size** — and the −5,652 B file delta is *exactly* the sum of the nine removed items' spans, so nothing else moved. (Contrast the 1.16 lesson that a byte-identical round-trip alone does not validate field boundaries; here the size-delta accounting is what carries the proof.)

*skill* did not drift: `skill.pabgb` / `.pabgh` are byte-identical to 1.16, so the parser cannot have regressed — `_probe_skill_entry_failures` reports 2,013/2,013 ok.

The save body did **not** drift: format unchanged (v2 / flags `0x0080`); `slot107` is the live 1.17 save and decodes 1,107 blocks / 3,097 fields at 100%, undecoded bytes 0/5,204,773, with all live-save c_abi round-trips (inventory, socket, dye, deferred-redecode, mutate→write) green. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.17/` (30 tables, 96,076 keys): `gimmickinfo` +1 (13,690 — new key 1012695) and `itemgroupinfo` −1 (1,596 — removed key 18566); the other 28 tables are key-identical. `CompatibleMinors` = `{17}` by the usual target-only convention, so a 1.16 install is warned even though its layout would still parse. On the editor side the alignment cost only the manual `VerMinor` 16→17 lock-step bump plus the `NativePaverReaderTests` version-pin refresh — **no** count/value pin moved this time (notably `Pyeonjeon_Arrow` `item_type` stayed 0), and the C ABI surface is unchanged.

**1.17 → 1.18 ships exactly ONE iteminfo drift; everything else is content-only.** crimson-rs commit `87fd09f` (PR #90, merged `e4261be`), validated against the live 1.18 install (paver `1/18/0/0x28577c0f`, 2026-08-15).

*iteminfo* (6,572 → **6,573** items; 6,139,734 → 6,190,316 B, SHA256 `771fecb3…`): every `MergedPrefabVisualData` element gained a `u32` between `tribe_gender_list` and the 3-byte flag tail. It reads the **same constant on all 12,274 elements of all 6,573 items** — `0xeac5e173`, the "empty string" Jenkins sentinel already documented under the 1.10 `money_icon_path` removal — so it is very likely a string/prefab name hash shipping unset; it is typed as a bare `u32` until a populated value turns up. The one new item is key 1005446 `Demian_Greyfur_Fabric_Cloak_II`. `serialize_iteminfo` round-trips byte-identical on the live binary.

RE'd with `scripts/diff_117_118.py` (a clone of `diff_115_116.py`) against the kept 1.17 binary. **Two other length-changing signatures in that report are walk artifacts, not drifts**: 1.18 reorders the `item_group_info_list` u16s, which manufactures compensating ±1 B pairs against `look_detail_mission_info` (93×) and `enable_alert_system_to_ui` (5×). Worth remembering as its own trap — a value **reordering** inside a fixed-size list can present in a byte-walk diff as a pair of offsetting inserts/deletes on unrelated neighbouring fields.

*skill* did not drift: 2,027 entries, probe 2,027/2,027 ok, format still `WithField58`. The save body did **not** drift either: format unchanged (v2 / flags `0x0080`); `slot107` is the live 1.18 save and decodes with `undecoded_bytes=0`. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-1.18/` (30 tables, 96,197 keys): 9 tables moved, 21 key-identical. The extracted-bin roster went 270 → **268** — `zoneinfo` was dropped, and nothing in the toolchain references it.

Soft pins bumped Rust-side: `itemgroupinfo` 1596→1597, `houseinfo` 4→12, `triggerregioninfo` 12→13, `IS_EQUIP_QUICK_SLOT_VISIBLE` 1005→1006 (the one new item is equipment). One pin was **re-shaped rather than re-numbered**: `part_prefab_dye_slot_info_lossy_live` asserted `slot_count == 1` on more than a quarter of rows, and 1.18's 65 new rows pushed that to 399/1,619 = 24.6%. All 1,619 rows parse, every KNOWN name+slot_count still matches, and the histogram is textbook right-skewed — so this is content, not the record-schema drift the check guards (1.12 broke it to 0 rows). The fixed fraction was replaced by "`slot_count == 1` must be the modal bucket".

`CompatibleMinors` = `{18}`; unlike 1.14/1.15/1.17 this is a **genuine** incompatibility rather than the target-only convention, since 1.17 iteminfo really does mis-decode. On the editor side the alignment cost the manual `VerMinor` 17→18 lock-step bump plus the `NativePaverReaderTests` version-pin refresh — **no** count/value pin moved on the C# side (the drift is absorbed entirely in Rust and the C ABI surface is unchanged), so all 381 C# tests pass with only those pins touched.

**1.18 → 2.00 is the FIRST MAJOR BUMP, and it ships two iteminfo drifts.** crimson-rs commit `0f2363b` (PR #91, merge `8e942d7`), validated against the live 2.00 install (paver `2/0/0/0xb241d214`, 2026-08-26).

The version-model change matters more than either drift. `meta/0.paver`'s **`minor` resets across a major bump** — 1.18 → 2.00 is `18` → `0` — so the minor alone stopped identifying a schema: minor `0` means 2.00 today and would have meant a hypothetical 1.00 yesterday. Rust added `PARSER_TARGET_GAMEDATA_MAJOR = 2` beside `PARSER_TARGET_GAMEDATA_MINOR = 0`, exposed as a new `crimson_parser_target_gamedata_major()` bridge (purely additive; the existing minor + compatible-minors surfaces are untouched, and their entries are now read as minors *within* that major). On the editor side that closed a real hole: `GameDataVersion.IsCompatibleWithParser` was `Array.IndexOf(CompatibleMinors, Minor) >= 0`, which with `CompatibleMinors = {0}` would have waved a `1.00.xx` install straight through into a mis-decode. It now gates on `Major == ParserTargetMajor` first, and `NativePaverReaderTests` carries a dedicated regression case for exactly that shape (same minor, wrong major). The mismatch dialog had the same bug in its readout — it hard-coded a `"1."` prefix and would have shown the 2.00 target as `1.00.xx`.

*iteminfo* (6,573 → **6,810** items, +237 with none removed; 6,190,316 → 6,446,719 B) drifted in two places: (1) `SubItem` gained a payload-free `type_id == 18`, the same renumbering 1.12 (16) and 1.13 (17) did — every site that read 17 in 1.18 reads 18 in 2.00 (item-level `default_sub_item` on all 6,810 items, `drop_default_data.default_sub_item` on 6,806, the other 4 reading tag 0), and no item still reads 14..=17; and (2) a new always-zero `u32` (`unk_pre_max_endurance_a`) inserted directly ahead of the 1.12-era `unk_pre_max_endurance`, so the block before `respawn_time_seconds` now carries two `u32`s (unconditional: 6,513 of the 6,573 carried-over items are exactly +4 B, and on 1,500 sampled carry-overs `old[max_endurance..] == new[max_endurance + 4..]` byte-for-byte). `serialize_iteminfo` round-trips byte-identical on the live binary; anchored export ok=6,810, leftover=0, fail=0, no_anchor=0.

**Drift (2)'s *position* is not decidable from the byte diff** — this is the new trap worth keeping. It lands inside an all-zero run, so inserting it before `unk_pre_max_endurance`, between the two, or after `respawn_time_seconds` all produce identical bytes and all round-trip byte-perfectly. What settles it is the value-sanity argument the 1.16 swap already rests on: only with the new `u32` first do both neighbours keep their known distributions — `unk_pre_max_endurance` still reads `0x01000000` on exactly the 59 `Trade_*_PackedInVehicle` items, and `respawn_time_seconds` still reads 0 (6,053) / −1 (755) / 604800 (2, the `Item_Egg_KuKu` and `Item_Egg_Wyvern` 7-day respawns). The wrong placements drag `0x01000000` into the new field and turn 604800 into `604800 << 32` and −1 into `-4294967296` — the exact nonsense signature 1.16 documented. A round-trip proves nothing about placement inside a constant run; check value distributions.

*skill* did not drift: 2,027 → **2,046** entries, probe 2,046/2,046 ok, format still `WithField58` — even a major bump left it alone. The save body did **not** drift either: format unchanged (v2 / flags `0x0080`); the live 2.00 save decodes 1,103 blocks / 3,315 fields with `undecoded_bytes` 0/5,229,306. Per-table gamedata snapshot in `crimson-rs` `data/gamedata-keys-2.00/` (30 tables, 96,997 keys): 13 tables moved, 17 key-identical. The extracted-bin roster went 268 → **269**: `levelgimmicksceneobjectinfo_misc.base.pabgm` is new while `levelgimmicksceneobjectinfo.pabgb` shrank 3,410,666 → 21,376 B as its payload moved into that new `.pabgm` container — nothing in the toolchain parses either.

Soft pins bumped Rust-side: `gameadviceinfo` 472→478, `gameplayvariableinfo` 56→59, `globalgameevent` 188→191, `IS_EQUIP_QUICK_SLOT_VISIBLE` 1006→1008. One pin was **re-shaped rather than re-numbered** on both sides: the whole `globalgameevent` 188 → 191 delta is 2.00 removing `RoyalSupply` (`0x424a`) and adding four per-faction rows `RoyalSupply_{Her,Dem,Del,Var}` (`0x4308`–`0x430b`), all still group `0x4241` with `paloc_key = 0` — so Rust's `KNOWN_BODY` triple and the C# `NicheBridges_LiveInstall_LoadAllAndResolveKnownKeys` assertion were each replaced one-for-four. That C# assertion was the **only** count/value pin that moved this side; unlike 1.17/1.18 it did move, because the row it named no longer exists.

`CompatibleMinors` = `{0}` (read as "minor 0 within major 2"); like 1.16 and 1.18 this is a **genuine** incompatibility rather than the target-only convention, since 1.18 iteminfo really does mis-decode. On the editor side the alignment cost the manual `VerMajor` 1→2 / `VerMinor` 18→0 lock-step bump (`VerPatch` back to 1), the new `ParserTargetGamedataMajor` `LibraryImport` and the major-aware compatibility gate, the mismatch-dialog prefix fix, and the paver + RoyalSupply pin refreshes — 382 C# tests green, 0 skipped.

**2.00 → 2.01 is a RENAME PATCH: every gamedata file moved and not one byte inside them changed.** Vendored from `crimson-rs` `main` (PR #93, merge `5dbeefb`), validated against the live 2.01 install (paver `2/1/0/0xa31e5fcb`, 2026-09-04).

The archive layer moved wholesale:

| | 1.05 – 2.00 | 2.01+ |
|---|---|---|
| table directory | `gamedata/binary__/client/bin` | `gamedata/binarystaticinfo__/bin` |
| table body | `<table>.pabgb` | `<table>.staticinfobody` |
| table index | `<table>.pabgh` | `<table>.staticinfoheader` |
| misc blob | `<table>.pabgm` | `<table>.staticinfomisc` (in `bin/misc`) |
| gimmick charts | `binarygimmickchart__/bin/*.pabgb` | `binarygimmickchart__/bin/*.binarygimmick` |
| localization | `stringtable/binary__/localizationstring_<lang>.paloc` | `stringtable/binary__/<lang>/<namespace>.paloc` |

Contents are byte-identical either way, so **no parser changed** — `PARSER_TARGET_GAMEDATA_MINOR` 0 → 1 is the only Rust-side edit, and `CompatibleMinors` = `{1}` is the *target-only convention* (as with 1.14 / 1.15 / 1.17), not a real incompatibility: 2.00 data still parses byte-perfectly against this build.

Editor-side the rename was the whole job, and it was **not** cosmetic: `LocalizationProvider` hardcoded the pre-2.01 directory and all 40-odd filenames, so on a 2.01 install every bridge returned NOT_FOUND and the app resolved no names at all. Both sides now go through a new `GameDataLayout` (mirroring crimson-rs's `gamedata_layout.rs`, which is `#[cfg(test)]` there and so unreachable over the C ABI): tables are named by *stem* and the directory + extensions come from a newest-first probe of the group-0008 manifest, so a kept pre-2.01 install still works. Localization needed more than a rename — 2.01 splits a language into one file per namespace, and `crimson_paloc_load_from_bytes` takes one blob — so a language is now held as its parts behind `MultiPalocCatalog`, and the `*_lookup_display_name` bridges (which each take a single PALOC *native handle*) are offered one part at a time. Namespaces don't overlap, so at most one part answers.

One soft pin moved, on the C# side only: `MissionKey 1000157`'s English title was reworded **"Unfamiliar Lands" → "Unfamiliar Land"**. A game-side text edit, not a parse drift — the 25-character `MissionKey 1000083` title resolves unchanged through the identical path.

**The Dye editor is greyed out as of this alignment.** 2.01 widened `partprefabdyeslotinfo`'s per-slot `mask` from 3 bytes to **12** *and re-encoded the contents* — in 5,572 of 6,555 comparable slot pairs the old three bytes do not appear as a contiguous window anywhere inside the new twelve, so no sub-slice is the pre-2.01 field. The twelve read as four groups of three, and **which group a slot uses is not yet RE'd**. Measured through the release dll over all 1,626 prefabs / 6,585 slots, the legacy 3-byte getter the editor calls reads all-zero on **2,196 slots (33.3%)** whose full field is non-zero — a third of the dye UI would render blank and write edits from that wrong reading. Per the foundation-over-workaround rule the menu item is disabled (with the reason in its tooltip) until the mask groups are decoded upstream and the editor moves to the new `crimson_part_prefab_dye_slot_info_lookup_slot_{,extra_layer_}mask_full` sized-buffer bridges. Everything else about dyeing still parses: the table loads all 1,626 rows on 2.01.

On the editor side the alignment cost the manual `VerMinor` 0→1 lock-step bump (`VerPatch` back to 1), the `GameDataLayout` + `MultiPalocCatalog` additions, the paver pin refresh, and the one mission-title pin — 395 C# tests green, 0 skipped.
