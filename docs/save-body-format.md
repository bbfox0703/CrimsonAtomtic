# Save body format

> **Status**: complete against the live 1.06 save.
>
> ```
> 1,112 / 1,112 blocks                  100.0%
> 3,099 / 3,099 fields decoded          100.0%
> Undecoded bytes: 0 / 5,172,506        0.0000%
> trailing_pad: 233 × 1 byte + 1 × 3 bytes
> ```
>
> Every byte is either decoded into a typed field or captured as
> `ObjectBlock.trailing_pad`. The crimson-rs decode test asserts both
> "zero undecoded" and "zero Unknown fields" as hard invariants, so a
> future game patch that drifts the format will fail loudly.

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
| `prefix_00xx0100`   | `00 00 XX 01 00`, count u32, data, trailing `01×5` | `XX` is a flag byte. Observed values: `0x06` (most arrays) and `0x01` (`FactionNodeElementSaveData._reviveQuestList`). Other prefix/trailer constraints + count bound are strict enough that this isn't ambiguous. |
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

## Engine trailer (`ObjectBlock.trailing_pad`)

234 blocks have a small region between the end of the forward walk
and the start of the reverse-peeled tail (or, when no reverse pass
ran, at the very end of the block) that the schema doesn't model.
The original Python parser leaves these bytes as undecoded; we
capture them on `ObjectBlock.trailing_pad: Vec<u8>` so they
round-trip.

### Distribution

| Length | Count | Classes |
| -----: | ----: | ------- |
| 1 byte | 233   | 226 × `FieldNPCSaveData` + one block each of `GameEventSaveData`, `InventoryItemContentsSaveData`, `InventorySaveData`, `KnowledgeSaveData`, `ContentsMiscSaveData`, `FieldSaveData`, plus one we picked up after the FactionSaveData fix |
| 3 bytes | 1    | `MercenaryClanSaveData` — `01 01 01` between `_mercenaryDataList` (object_list) and the reverse-peeled tail |

### Value notes

- The 1-byte pads in `FieldNPCSaveData` have **123 unique values** across
  the 226 blocks (63% have the high bit set). They're payload data, not
  a constant marker, but no schema field claims them — likely an engine
  artifact carrying state we don't yet model.
- The 3-byte pad in `MercenaryClanSaveData` is the constant `01 01 01`.
  Looks like a list-level trailer pattern; the rule still feels under-
  characterised since we only have one example in this save.

### Decoder rule

In `decode_one_block`, if `decode_fields_in_region` returns a single
undecoded range of 1..=16 bytes, we move those bytes to
`ObjectBlock.trailing_pad` and clear the range. Anything larger or
non-contiguous is left in `undecoded_ranges` so a real decode failure
still surfaces (e.g. the FactionSaveData case before that fix landed).

The PyO3 binding surfaces `trailing_pad` as `bytes` on each decoded
block dict when non-empty (omitted otherwise).

## Locator inline-payload precedence

The wrapper's `child_payload_offset` is treated as **advisory**, not
authoritative. The decoder tries `wrapper_end` as the inline payload
start first, and only falls back to the stated offset when that fails.

Empirically the engine sometimes writes a stale or wrong-looking
`payload_offset` value while the actual payload still sits immediately
after the wrapper — observed across many `FactionNodeElementSaveData`
list elements where `payload_offset` pointed hundreds of bytes later
into the block. The original Python parser only recurses when
`payload_offset == wrapper_end` and consequently leaves all those
"non-inline-looking" elements as undecoded; we don't, so the residual
that motivated this work no longer appears.

## Best-effort field walk

When a sub-decoder hits a shape it doesn't recognize (in practice,
an unfamiliar `dynamic_array` header variant inside one specific
quest-list field), the field walk in `decode_inline_object_payload`
breaks out of the loop instead of propagating an error. The
trailing-size probe then finds the payload's true end from wherever
cursor stopped, so:

- The outer forward walk still advances past the right number of
  bytes — downstream elements in the same list remain decodable.
- Fields that did decode are kept; the offending field stays as
  `Unknown` rather than being silently filled with garbage.
- `ObjectBlock.undecoded_ranges` is now reliable as a "we don't know
  what's here" signal rather than a "we gave up" signal.

This is the only departure from the Python `save_parser.py`'s decode
strategy beyond what's already documented above.

## Not ported from the Python source

- The compact-list-element decode path from `save_parser.py`. It has
  undefined-local bugs in the original and is unreachable on our test
  save. Re-add only if a future patch needs it.
