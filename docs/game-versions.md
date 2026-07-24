# Game versions

## Current install

`D:\SteamLibrary\steamapps\common\Crimson Desert` — **version 1.15**

Top-level layout:

```
Crimson Desert/
├── 0000/ ... 0035/      # 36 asset pack groups, each contains *.paz + 0.pamt
├── bin64/               # CrimsonDesert.exe, DX12, DLSS/FSR/XeSS, Steam, Sentry
├── CDMods/              # cdumm.db (SQLite mod registry) + vanilla/ unpack cache
├── gamedata/            # localizationstring_eng.paloc
├── meta/                # 0.papgt (master pack tree), 0.pathc, 0.paver (version stamp)
├── mods/                # _enabled/, _asi/, _lang/ (user mods)
└── config.json
```

- **Engine**: proprietary Pearl Abyss engine, DirectX 12.
- **Total size on disk**: ~126 GB.
- **Version stamp**: `meta/0.paver` is 10 bytes, binary-encoded. Layout
  is fully decoded (see `crimson-rs` `src/binary/paver.rs`, exposed via
  `crimson_paver_read_from_*`): three little-endian u16s `(major, minor,
  patch)` followed by a little-endian u32 `build`. The **minor** is the
  schema-compatibility key. Live 1.15.00 install:
  `01 00 0f 00 00 00 e1 88 84 6a` → major 1, minor 15, patch 0,
  build `0x6a8488e1`. (1.14.00 was `01 00 0e 00 00 00 f8 42 7d 59`,
  build `0x597d42f8`; 1.13.00 was `01 00 0d 00 00 00 0d 2c 6a 53`,
  build `0x536a2c0d`; 1.12.00 was `01 00 0c 00 00 00 02 84 73 ac`,
  build `0xac738402`; 1.11.00 was `01 00 0b 00 00 00 24 7a 2c 20`,
  build `0x202c7a24`; 1.10.00 was `01 00 0a 00 00 00 ac b2 84 cf`,
  build `0xcf84b2ac`; 1.09.00 was `01 00 09 00 00 00 24 48 f3 bb`,
  build `0xbbf34824`; 1.08.00 was `01 00 08 00 00 00 3e b0 39 dc`,
  build `0xdc39b03e`.) The editor reads this at startup and warns when
  the install's minor isn't one the parser can load
  (`GameDataVersion.CompatibleMinors`, now `{15}`). **1.15 is a
  content-only patch over 1.14** (crimson-rs tag `v1.0.15.x`: item field
  values changed but the iteminfo layout is byte-identical, and the save
  body, skill, and every gamedata bridge parse unchanged — only the
  `PARSER_TARGET_GAMEDATA_MINOR` pin moved 14→15). The allow-list is kept
  target-only by convention, so 1.14 and earlier are flagged incompatible
  even though the 1.14 layout is in fact readable; `ParserTargetMinor = 15`
  is the version the dialog displays. Both values are read from the
  crimson-rs C ABI, not hand-coded.

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
