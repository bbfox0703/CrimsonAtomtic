# Game versions

## Current install

`D:\SteamLibrary\steamapps\common\Crimson Desert` — **version 1.12**

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
  schema-compatibility key. Live 1.12.00 install:
  `01 00 0c 00 00 00 02 84 73 ac` → major 1, minor 12, patch 0,
  build `0xac738402`. (1.11.00 was `01 00 0b 00 00 00 24 7a 2c 20`,
  build `0x202c7a24`; 1.10.00 was `01 00 0a 00 00 00 ac b2 84 cf`,
  build `0xcf84b2ac`; 1.09.00 was `01 00 09 00 00 00 24 48 f3 bb`,
  build `0xbbf34824`; 1.08.00 was `01 00 08 00 00 00 3e b0 39 dc`,
  build `0xdc39b03e`.) The editor reads this at startup and warns when
  the install's minor isn't one the parser can load
  (`GameDataVersion.CompatibleMinors`, now `{12}` — **1.12 drifted the
  iteminfo schema again** (crimson-rs `0694dfb`: +150 items and four
  byte-perfect layout changes), so 1.11 and earlier no
  longer round-trip against this parser; `ParserTargetMinor = 12` is the
  latest of that set and the version the dialog displays).

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
