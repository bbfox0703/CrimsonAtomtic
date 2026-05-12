# `.save` file format

> **Status (header + crypto)**: ported. Lives in
> [`vendor/crimson-rs/src/save/`](../vendor/crimson-rs/src/save/) as
> `header.rs`, `crypto.rs`, `io.rs`. Verified against a live 1.06 save
> end-to-end (HMAC ok, LZ4 decompresses, write-parse round-trip preserves
> the body).
>
> **Status (body parsing)**: ported. See
> [save-body-format.md](save-body-format.md) for the decompressed body
> layout (schema + TOC + per-block decoder). 100% block + present-field
> coverage against the same live save.

## Header

| Offset | Size | Field           | Notes                                              |
| ------ | ---- | --------------- | -------------------------------------------------- |
| 0      | 4    | `magic`         | ASCII `SAVE`                                       |
| 4      | 1    | `format_ver`    | observed `02` in 1.04/1.05/1.06                    |
| 5      | ?    | (TBD)           | The old parser handles "v2" specifically — full byte layout to be re-derived during port |

We will re-derive the rest of the header from `save_crypto.py` source rather than trust the old layout blindly. Mark each field with a confidence level in the Rust port.

## Body crypto

- **ChaCha20** stream cipher (already available in `crimson-rs` `crypto::chacha20`).
- **HMAC** validates the post-decryption blob; the old code uses the `cryptography` Python package — TODO confirm which HMAC variant (SHA256? Blake2?) the format uses.
- **LZ4** decompression after HMAC validation (`lz4_flex` already a dependency in `crimson-rs`).

Key/nonce derivation is not yet documented here. Port that knowledge from `save_crypto.py` with a written summary, do not blindly copy code.

## Body structure (after decompress)

The decompressed payload contains, roughly:

- **TOC** — table of contents listing every section by type + offset + length.
- **Type metadata** — schema definitions per type (field names, sizes, hashes).
- **Bag contents** — inventory items with stack count, enchant, durability, slot.
- **Character stats** — level, skill points, attribute values, currencies.
- **Quests / buffs / world state** — quest stage flags, active buffs, region progression.

Old `save_parser.py` is ~1,590 lines and parses all of these. The port priorities:

1. **Read path first** — everything needed to display a save in the UI.
2. **Mutation API** — typed setters with bounds checks; no untyped byte poking from C#.
3. **Write path with byte-perfect roundtrip** — same standard as `crimson-rs` iteminfo: serialise an untouched save and verify it matches the original bytes.

## Where parsing lives (target)

- `vendor/crimson-rs/src/save/mod.rs` (new module)
  - `header.rs` — magic / format version detection.
  - `crypto.rs` — wraps existing `chacha20::` + add HMAC + lz4 stream.
  - `toc.rs` — TOC reader and writer.
  - `types/` — section parsers (bag, stats, quests, buffs, …).
- Exposed via C ABI for the C# app and via PyO3 for the Python tools.

## Regression corpus

Before any save-write code lands, we need a corpus of test saves covering:

- Fresh character / minimal save.
- Mid-game with mixed inventory + enchants.
- End-game with many quest flags + buffs.
- A save from each historical game version we still have on disk (1.03/1.04/1.05/1.06).

Each fixture: a copy of `save.save` + a JSON snapshot of what we expect to extract. Round-trip test = `parse → serialize → bytes equal original`.

## What we will not preserve from the old code

- **Side effects on import**: the old `save_parser.py` does layout work at module import; the new code is pure functions.
- **In-place mutation of opaque dicts**: the old `item_scanner.py` mutates Python dicts. The new model is typed Rust structs marshalled to typed C# records.
- **Magic field-name strings sprinkled across files**: any string keys we still need become enums or interned constants in a single magic-strings file (CLAUDE.md rule 7).
