# Save body format

> **Status**: schema + TOC + per-object field decoder all implemented in
> [`vendor/crimson-rs/src/save/body/`](../vendor/crimson-rs/src/save/body/).
> 100% block coverage and 100% present-field coverage against a live 1.06
> save; ~1.2% of bytes (concentrated in 226 `FieldNPCSaveData` entries)
> remain in `block.undecoded_ranges`.

The decompressed save body has three sequential sections:

1. **Prefix** — 14 bytes at offset 0. The first 4 are magic `FF FF 04 00`; the next 10 are opaque and preserved verbatim for round-trip.
2. **Schema** — a self-describing type table starting at offset `0x0E`. See [Schema layout](#schema-layout).
3. **TOC + data** — a 12-byte TOC header at `schema_end`, then 20-byte TOC entries, then the data section those entries point into. See [TOC layout](#toc-layout) and [Per-block layout](#per-block-layout).

## Schema layout

Each schema type carries an ASCII name + a list of fields. The name appears **before** each type's body except for the first type (whose name follows the schema-level header). Byte layout:

```text
u16 header_tag       (purpose unknown, observed nonzero)
u16 header_zero      (typically 0)
u16 type_count       N

u32 root_name_len
u8  root_name[len]   first type's name (ASCII, no NUL)

repeat N times:
  u16 field_count
  repeat field_count times:
    u32 field_name_len + bytes
    u32 type_name_len  + bytes
    u16 meta_kind
    u16 meta_size
    u32 meta_aux
  if not the last type:
    u32 next_name_len + bytes   (name of the NEXT type)
```

The `meta_kind` value selects the field decoder used when reading instance data.

## TOC layout

```text
u32 prefix_zero    (typically 0)
u32 toc_count      N entries
u32 stream_size    total data section size in bytes

repeat N times (20 bytes):
  u32 class_index     (index into schema.types)
  u32 sentinel1       (validation marker, often 0)
  u32 sentinel2       (validation marker, often 0)
  u32 data_offset     (absolute offset inside body)
  u32 data_size       (length of this entry's data, in bytes)
```

## Per-block layout

```text
u16 mask_byte_count       1..16
u8  mask_bytes[mask_byte_count]   one presence bit per schema field
u32 reserved_u32

...field payloads encoded per the schema, walked head-to-tail with a
   reverse pass for trailing fixed-size fields and a forward pass for
   the rest...
```

## Field decoder dispatch (meta_kind)

| meta_kind | What it decodes                              | Layout sketch                                                            |
| --------: | -------------------------------------------- | ------------------------------------------------------------------------ |
| 0, 2      | Fixed-size scalar                            | exactly `meta_size` bytes                                                |
| 1         | Inline byte array                            | `u32 count` then `count * meta_size` bytes                               |
| 3         | Dynamic primitive array                      | one of 4 header variants (see below)                                     |
| 4, 5      | Inline-object locator                        | wrapper {mask, child_type, sentinels, offset}; child payload if inline    |
| 6, 7      | Object list                                  | one of 5 header variants, then `count` list elements                     |

### `meta_kind == 3` (dynamic array) header variants

| Variant name        | First-bytes pattern                                | Source notes                              |
| ------------------- | -------------------------------------------------- | ----------------------------------------- |
| `prefix_0000060100` | `00 00 06 01 00`, count u32, data, trailing `01×5` | Verified empirically; matches Python case |
| `marker_prefix`     | 1+ leading `01` bytes, then `00`, then count u32   | Variable-length marker run                |
| `compact`           | `00 00 <u16 count> 00 00`                          | Short header for small arrays             |
| `generic`           | `<u8 prefix> <u32 count>`                          | Fallback                                  |

### `meta_kind` 6/7 (object list) header variants

| Variant name             | Distinguishing pattern                                          |
| ------------------------ | --------------------------------------------------------------- |
| `marker_run_plus_zeros`  | 1+ leading `01` bytes, then `00`, then count u32, then 13 zeros |
| `zero4_count_u32`        | `00 00 00 00 <u32 count>`                                       |
| `zero1_count_u24`        | `00 <u24 count>`                                                |
| `ones_then_count`        | `01 01 01 00 <u32 count>`                                       |
| `one_count_u16be`        | `01 <u16 BE count>`                                             |

The decoder tries body offsets `{cursor, cursor+1, cursor+2, cursor+3}` and picks whichever produces the deepest decode. Matches the Python parser's heuristic.

## Type-name → scalar dispatch

| `type_name` (lowercased) condition | `meta_size` | Decoded as       |
| ---------------------------------- | ----------- | ---------------- |
| `"bool"`                           | 1           | `bool`           |
| contains `"float"`                 | 4           | `f32`            |
| contains `"float"`                 | 8           | `f64`            |
| starts with `"int"`                | 1 / 2 / 4 / 8 | `i8 / i16 / i32 / i64` |
| otherwise                          | 1 / 2 / 4 / 8 | `u8 / u16 / u32 / u64` |
| otherwise                          | anything else | raw `bytes`     |

## Naming map (ours vs. the legacy Python parser)

The Python `save_parser.py` in the old reference repo used different
names for the same concepts. We don't try to stay compatible (per
[data-policy.md](data-policy.md)); the table below is just for cross-
referencing while the old repo is still useful to read.

| Our Rust API (`vendor/crimson-rs/src/save/body/`) | Legacy Python (`save_parser.py`)          |
| ------------------------------------------------- | ----------------------------------------- |
| `Body { prefix, schema, toc }`                    | output dict of `parse_schema` + `parse_toc` |
| `Schema { header_tag, header_zero, type_count, root_type, types, schema_end }` | `parse_schema()` return dict        |
| `TypeDef { index, name, fields, start_offset, end_offset }`                    | `TypeDef` dataclass                  |
| `FieldDef { name, type_name, meta_kind, meta_size, meta_aux, start_offset, end_offset }` | `FieldDef` dataclass            |
| `Toc { prefix_zero, toc_count, stream_size, entries }`                         | `parse_toc()` return dict             |
| `TocEntry { index, class_index, sentinel1, sentinel2, data_offset, data_size, entry_offset }` | `TocEntry` dataclass         |
| `ObjectBlock { class_index, class_name, data_offset, data_size, mask_byte_count, mask_bytes, reserved_u32, fields, undecoded_ranges }` | `ObjectBlock` dataclass             |
| `DecodedField { field_index, name, type_name, meta_kind, meta_size, meta_aux, present, kind, value, start, end, note }` | `GenericFieldValue` dataclass (many optional attributes) |
| `FieldKind::FixedPrefix / FixedSuffix / InlineBytes / DynamicArray / ObjectLocator / ObjectList / Absent / Unknown` | `decode_kind` string `"fixed_prefix" / …`                |
| `ScalarValue::U32(u32)` (typed)                  | `value_repr: str` (e.g. `"42"`) + `edit_format: str` (e.g. `"<I"`) |
| `FieldValue::InlineBytes { count, bytes }`       | `count` + raw bytes encoded back into `value_repr`               |
| `FieldValue::DynamicArray { count, bytes, header_variant }` | same plus `note: str`                                 |
| `FieldValue::Locator { child_type_*, child_payload_offset, child }` | numerous `child_*` attributes on `GenericFieldValue` |
| `FieldValue::ObjectList { count, header_variant, elements }` | `list_count`, `list_header_size`, `list_elements`, etc.    |

## What the inspect tools do with this

- [`tools/inspect/inspect_save_body.py`](../tools/inspect/inspect_save_body.py) — show schema + TOC only (cheap).
- [`tools/inspect/inspect_save_section.py`](../tools/inspect/inspect_save_section.py) — run the full decoder; filter by `--class` or `--toc-index`; pretty-print or dump JSON.

## Known gaps

- 226 `FieldNPCSaveData` blocks have a small undecoded tail (~280 bytes each). The forward walk completes all schema fields; the residue is structural data not captured by the schema. Investigate when we need NPC state.
- 8 other classes have tiny single-block residues (~1 byte each), likely alignment/padding.
- The compact-list-element decode path from the Python parser isn't ported because it has known undefined-local bugs and isn't reached by our test save. Re-add only if a future patch needs it.
