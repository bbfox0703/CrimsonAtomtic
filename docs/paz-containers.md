# Asset containers: PAZ / PAMT / PAPGT / PALOC

> The Pearl Abyss engine packs assets into a small set of related container formats. `crimson-rs` already handles all of them with byte-perfect roundtrip — this doc captures the model so we don't reinvent it.

## File-level layout per game install

```
<install>/
├── meta/
│   └── 0.papgt        # master pack-group tree; lists all groups + checksums
├── 0000/
│   ├── 0.pamt         # pack metadata: directory/file index for this group
│   ├── 0.paz          # compressed asset chunks
│   ├── 1.paz
│   └── ...
├── 0001/
│   ├── 0.pamt
│   └── 0.paz
└── ...
```

| Format    | Role                                  | `crimson-rs` module           |
| --------- | ------------------------------------- | ----------------------------- |
| `.papgt`  | Master index: lists pack groups + per-group checksums and metadata | `binary::papgt::PackGroupTreeMeta` |
| `.pamt`   | Per-group VFS: directory/file listing, compression + encryption flags, chunk refs | `binary::pamt::PackMeta`      |
| `.paz`    | Compressed/encrypted asset chunks (LZ4, ZLIB, or uncompressed)                    | `binary::paz::PackGroupBuilder` |
| `.paloc`  | Localization tables (string key → translated text), 14 languages auto-discovered  | `binary::paloc::LocalizationFile` |
| `.pathc`  | Patch metadata (purpose TBD)          | not yet in `crimson-rs`       |
| `.paver`  | Version stamp (10 bytes)              | not yet in `crimson-rs`       |

## Crypto and compression

- **Encryption**: ChaCha20, key handled inside the pamt entry flags.
- **Compression**: LZ4 (most common) or ZLIB (`flate2`). Some chunks are stored uncompressed.
- **Checksum**: Jenkins hashlittle2 — used on PAPGT to validate pack groups against the master tree.

All of this is already implemented in `crimson-rs::crypto::*` and the binary modules.

## Mod loading

The game supports first-party mod overlay via the `mods/_enabled/` directory plus `CDMods/cdumm.db` (SQLite registry). Overlay mechanics:

- Mods are packed in the same PAZ / PAMT structure.
- The master `papgt` is upserted to register new pack groups.
- The game prefers mod entries when a path collision occurs.

This is relevant to the save editor in one specific way: an item the user added via a mod may appear in their save but not in vanilla iteminfo. The editor should detect the mod registry and display modded items gracefully. The mod *authoring* path is out of scope for the save editor — that belongs to a separate tool, possibly later.

## PALOC: 14 languages

The 14 detected language codes (from `crimson-rs`):

```
eng, jpn, kor, zho-tw, zho-cn, cht, chs, ger, fra, spa, por, rus, tur, tha, ind, ara
```

`crimson-rs` already parses + roundtrips paloc files. The save editor uses paloc lookups to resolve item / skill / region names for display — see [ui-design.md](ui-design.md) for how this hooks into the UI.

## What the C# app actually needs

For a save editor (not a full mod tool), the asset pipeline only needs:

- **Read PALOC** to resolve in-game string keys for display.
- **Read iteminfo / skill / regioninfo via PABGB** to know what items exist, what their tiers are, etc.
- **Read PAMT** opportunistically to enumerate mod additions.

The app does **not** need to write PAZ archives or modify the master papgt. Those operations stay in the Python toolchain (via the existing PyO3 bindings) for occasional use.

This narrows the C ABI surface we have to add: roughly a dozen functions, not the whole crate.

## Extracting one file (cheat sheet)

From Python (already works today):

```python
import crimson_rs

raw = crimson_rs.extract_file(
    game_dir="D:/SteamLibrary/steamapps/common/Crimson Desert",
    group="0008",
    dir_path="gamedata/binary__/client/bin",
    filename="iteminfo.pabgb",
)
```

From C# (after we add C ABI): same idea, returns an opaque buffer handle plus length, freed via a paired `free_buffer` call.
