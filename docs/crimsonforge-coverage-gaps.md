# CrimsonForge — coverage gaps vs crimson-rs

> Reference doc for cross-checking format coverage between our
> `vendor/crimson-rs` and **NattKh/CrimsonForge** (a separate, more
> mature Crimson Desert modding studio living at
> `D:\Github\crimsonforge`). When a parsing question comes up that
> we don't yet have an answer for, CrimsonForge is the first place
> to look — it has been actively reverse-engineering CD formats for
> longer and shipped a working round-trip for most of them.

Last surveyed: **2026-05-13** (CrimsonForge v1.11.0).

## Quick map: do we have it?

The "Module" column points at the canonical CrimsonForge file under
`D:\Github\crimsonforge\core\`. ✅ = present and tested in
`vendor/crimson-rs`; ⚠️ = partial; ❌ = absent.

| Format / capability                              | Module                          | Us  |
| ------------------------------------------------ | ------------------------------- | --- |
| ChaCha20 + key derivation (per-file)             | `crypto_engine.py`              | ✅  |
| PaChecksum (Bob Jenkins lookup3 variant)         | `checksum_engine.py`            | ✅  |
| PAMT index parse                                 | `pamt_parser.py`                | ✅  |
| PAPGT root index                                 | `papgt_manager.py`              | ✅  |
| PAZ entry extract — type 0 (none), type 2 (LZ4)  | `compression_engine.py`         | ✅  |
| PAZ entry extract — type 1 identity (c == u)     | `compression_engine.py`         | ✅  |
| PAZ entry extract — type 1 header(128)+LZ4       | `_decompress_type1_prefixed_lz4`| ✅ (we extend with prefix-dict) |
| PAZ entry extract — type 1 DDS per-mip table     | `_decompress_type1_dds_per_mip_sizes` | ✅ ([paz.rs](../vendor/crimson-rs/src/binary/paz.rs)) |
| PAZ entry extract — **type 1 PAR container**     | `_decompress_type1_par`         | ❌  |
| PAZ entry extract — **type 1 top-mip-LZ4+raw-tail** | `_decompress_type1_dds_first_mip_lz4_tail` | ❌  |
| PAZ entry extract — type 3 / type 4 zlib         | `compression_engine.py`         | ⚠️ (likely enum swap — see below) |
| Full PAZ **repack** (compress + encrypt + size-fit) | `repack_engine.py`           | ❌  |
| PALOC localization parse                         | `paloc_parser.py`               | ✅  |
| PABGB / PABGH (game-data binary tables)          | `pabgb_parser.py`               | ✅ (iteminfo, skill, string_info) |
| iteminfo / skill / string_info bridges           | `item_catalog.py`               | ✅  |
| **PABC** (PAR-family character morph)            | `pabc_parser.py`                | ❌  |
| **PABC skin palette**                            | `pabc_skin_palette.py`          | ❌  |
| **PAC_XML** (XML descriptor + texture resolver)  | `pac_xml_parser.py`             | ❌  |
| **PAA metabin** sidecar                          | `paa_metabin_parser.py`         | ❌  |
| **PASEQ** sequencer                              | `paseq_parser.py`               | ❌  |
| **PREFAB**                                       | `prefab_parser.py`              | ❌  |
| **NAV** (navmesh)                                | `navmesh_parser.py`             | ❌  |
| **PAM / PAMLOD / PAC mesh**                      | `mesh_parser.py` (~103 KB)      | ❌  |
| **Havok HKX (TAG0)**                             | `havok_parser.py`               | ❌  |
| **PAA animation**                                | `animation_parser.py` (~1438 LOC) | ❌ |
| **PAB skeleton**                                 | `skeleton_parser.py`            | ❌  |
| Font builder                                     | `font_builder.py`               | ❌  |
| Audio (WEM / Wwise) round-trip                   | `audio_converter.py`            | ❌  |
| Dialogue catalog (live game-data join)           | `dialogue_catalog.py`           | ❌  |
| Mesh export to OBJ / FBX                         | `mesh_exporter.py` (~125 KB)    | ❌  |
| Mesh import + topology editing                   | `mesh_importer.py` (~190 KB)    | ❌  |

## Most actionable gaps (ranked by relevance to *this* project)

CrimsonAtomtic is a save editor + game-data toolchain; mesh / audio /
animation editing is mostly out of scope. The gaps below are the ones
that would actually unblock work we plan to do.

### 1 — PAR-container partial-compression (~93k entries)

`_decompress_type1_par` in `D:\Github\crimsonforge\core\compression_engine.py`.

For `raw_compression == 1` entries whose payload starts with the
`PAR ` magic, the engine stores an 80-byte header verbatim, then 8
slots at offset `0x10` of the form `[u32 comp_size, u32 decomp_size]`.
Non-zero `comp_size` means that section is LZ4-compressed inside the
file payload; `comp_size == 0` with `decomp_size > 0` means stored
raw at the natural offset.

Unlocks extraction of `.pam` (~49k), `.pamlod` (~31k), `.pac` (~13k)
mesh assets in groups 0009 and 0015 — see `probe_partial_validate`
session report (deleted, in git history). Mesh data isn't on the
save-editor roadmap, so the port stays deferred until someone wants
it.

### 2 — DDS top-mip-LZ4 + raw mip tail fallback

`_decompress_type1_dds_first_mip_lz4_tail` in the same file.

Legacy DDS layout where only mip 0 is LZ4-compressed and the
remaining mip levels are appended raw. Our current strategy chain
(header+LZ4-with-prefix-dict, then per-mip table) catches every DDS
in `0012/ui/texture/icon/` and the worldmap SDFs without needing
this. CrimsonForge keeps it as a fallback for older assets that
predated the per-mip header layout. Port if we ever see live-install
DDS files that fail both of our current strategies.

### 3 — Compression-type enum swap (latent bug?)

CrimsonForge defines:
```
COMP_NONE = 0, COMP_RAW = 1, COMP_LZ4 = 2, COMP_CUSTOM = 3, COMP_ZLIB = 4
```

`vendor/crimson-rs/src/binary/pamt.rs` defines:
```rust
Compression::None = 0, Partial = 1, Lz4 = 2, Zlib = 3, QuickLz = 4
```

The CrimsonForge values come from a working repack pipeline that
round-trips successfully against the live engine, so it's the more
trustworthy source. If real CD 1.06 has any entry with raw flag `3`
or `4`, our current decoder would try the wrong codec (zlib on a
type that's actually unsupported, or QuickLz on what's actually
zlib).

Action item: probe live 1.06 for entries with `raw_compression in
(3, 4)`, count by extension. If non-zero, fix the enum and update
`decompress`'s match arm. Nothing visible is currently broken
because no observed file in our pipeline uses these flags.

### 4 — Full PAZ repack pipeline

`repack_engine.py`. Compress → encrypt → size-fit (multi-phase
padding to hit exact `comp_size`) → write into the PAZ at the right
offset → restore NTFS timestamps so the game's integrity check
doesn't notice. Required for "extract → edit → put back" workflows.

Not on the save-editor roadmap (we only need to write back `.save`
files, not modify archives), but worth knowing it exists if we ever
move into modding territory.

## Where these formats live in shipping CD 1.06

| Extension       | Where                                              | Count   |
| --------------- | -------------------------------------------------- | ------- |
| `.pam`          | 0009/0015 mesh assets                              | ~49,589 |
| `.pamlod`       | 0009/0015 mesh LOD chains                          | ~31,498 |
| `.pac`          | 0009/0015 character + weapon meshes                | ~12,795 |
| `.pabc` / `.pabv` | character morph data                             | TBD     |
| `.pac_xml`      | mesh material/texture XML descriptors              | TBD     |
| `.paa_metabin`  | animation sidecar metadata                         | TBD     |
| `.paseq`        | scene sequencer                                    | TBD     |
| `.prefab`       | spawn prefabs                                      | TBD     |
| `.nav`          | navmesh                                            | TBD     |
| `.paa`          | animations                                         | TBD     |
| `.pab`          | skeletons                                          | TBD     |

The `.pam` / `.pamlod` / `.pac` counts come from
`vendor/crimson-rs/src/binary/paz.rs::probe_partial_validate` (a
one-off `#[ignore]`d test, deleted after landing — see git history).

## When to port from CrimsonForge

CrimsonForge is **GPL-licensed**, so we can't copy code verbatim into
our (currently unlicensed / TBD-licensed) repo. The right shape for
each port is:

1. Read the relevant CrimsonForge file as a *reverse-engineering
   spec*, the same way we read the format docs in
   `docs/save-format.md`.
2. Re-implement in Rust against the spec, citing CrimsonForge
   only as the source of the format knowledge (not as a code
   ancestor — credit goes in code comments, e.g. as we do in
   `_decompress_type1_dds_per_mip_sizes` reference in
   [paz.rs](../vendor/crimson-rs/src/binary/paz.rs)).
3. Validate against the live install with a `#[cfg(test)]` walk
   over real PAMT entries that match the format in question.

## Not useful: `D:\Github\crimson-desert-unpacker`

For the record — a similarly-named third repo
(`D:\Github\crimson-desert-unpacker`) targets a *different* PA
archive format: 32-bit per-entry flags, Bob Jenkins lookup3 with
`initval=0xC5EDE`, and a different key-derivation scheme. Likely
either an older CD client or a sibling PA title (BDO?). Doesn't
handle partial compression at all. **Don't mine it for our work** —
the format details don't transfer.
