# tools/analyze/

Higher-level analyses across many files. Distinct from `inspect/` (one file at
a time) and `diff/` (exactly two inputs).

| Script                          | Purpose                                                                                       |
| ------------------------------- | --------------------------------------------------------------------------------------------- |
| `dump_save_fields.py`           | Flatten a `.save` into one-row-per-field JSONL — feeds duckdb/pandas RE on unknown Key types  |
| `dump_catalogs.py`              | Dump iteminfo + PALOC (per language) out of an install as JSONL — the JOIN tables for the above |
| `extract_keycases.py`           | Real-case pack per Key TypeName — denormalized rows with siblings + catalog resolves baked in. Hand-off to the crimson_rs RE session |
| `analyze_item_distribution.py`  | Tier / type / price distribution across the iteminfo table (TODO)                             |
| `analyze_save_corpus.py`        | Roll up stats across all saves under a save root (TODO)                                       |

Output is always written as a report file (`.md` / `.json` / `.jsonl`) —
not just printed to stdout. Reports go to `out/analyze/<date>_<topic>/`.

## `dump_save_fields.py`

JSONL line per decoded scalar field. Useful when you need *raw values*
across the whole save (e.g. "what numeric ranges does `MissionKey`
actually use? does any `MissionKey` value collide with an `ItemKey` at
position X?"). The shape of each row is documented in the script's
docstring; the headline columns are `save / top_class / class / path /
field / type / prim / kind / value / value_f / value_hex`. `value` is
the integer slot (every uint, sint, and bool packed as 0/1); `value_f`
holds floats; `value_hex` holds raw `bytes` fixed_prefix payloads. Exactly
one of the three is non-null per row — keeps each column homogeneous so
duckdb's `read_json_auto` infers clean types and `WHERE value = N`
queries don't need any cast. Object lists and locators are recursed into;
`dynamic_array` is opt-in via `--include-array-elements`.

Recipe (single save → duckdb correlation):

```powershell
python tools\analyze\dump_save_fields.py `
    --save "$env:LOCALAPPDATA\Pearl Abyss\CD\save\<userid>\slot0\save.save" `
    --include-array-elements `
    --out out\analyze\<date>_save_fields\slot0.jsonl

duckdb -c @"
SELECT type, COUNT(*) AS rows, COUNT(DISTINCT value) AS uniq,
       MIN(value) AS lo, MAX(value) AS hi
FROM   read_json_auto('out/analyze/<date>_save_fields/slot0.jsonl',
                      format='newline_delimited')
WHERE  type LIKE '%Key'
GROUP  BY type ORDER BY rows DESC;
"@
```

## `dump_catalogs.py`

The lookup tables `dump_save_fields.py` needs to JOIN against. Pulls
`iteminfo.pabgb` and the localization PALOC for the requested language(s)
out of the game install, runs them through `crimson_rs.extract_file` +
`parse_*_from_bytes`, and writes:

- `iteminfo.jsonl` — one row per item, the full
  `parse_iteminfo_from_bytes` dict.
- `paloc_<lang>.jsonl` — one row per PALOC entry, with derived columns:
  - `key`       upper 32 bits of the entry's decimal-encoded u64 string_key
  - `type_byte` lowest 8 bits (e.g. `0x70` == item name, `0x30` == character / faction, `0x00` == gimmick)
  - `mid`       middle 24 bits (informational; not predictable)
  - `value`     localized text

Recipe — full RE workflow (save + catalog dumps + cross-resolve):

```powershell
$out = "out/analyze/$(Get-Date -Format yyyy-MM-dd)"
python tools\analyze\dump_save_fields.py `
    --save "$env:LOCALAPPDATA\Pearl Abyss\CD\save\<userid>\slot0\save.save" `
    --out  "$out\save_fields\slot0.jsonl"
python tools\analyze\dump_catalogs.py --out "$out\catalogs"
```

Then ask each catalog whether it recognises every distinct Key value in
the save (works in duckdb or plain Python; see the script header for a
duckdb-flavoured version):

```sql
-- duckdb. Shows iteminfo + 3 PALOC type bytes for every ItemKey in the save.
CREATE VIEW save  AS SELECT * FROM read_json_auto('save_fields/slot0.jsonl', format='newline_delimited');
CREATE VIEW items AS SELECT * FROM read_json_auto('catalogs/iteminfo.jsonl', format='newline_delimited');
CREATE VIEW paloc AS SELECT * FROM read_json_auto('catalogs/paloc_eng.jsonl', format='newline_delimited');

SELECT s.type, s.value,
       items.string_key                                            AS iteminfo_id,
       MAX(CASE WHEN p.type_byte = 112 THEN p.value END)           AS name_item,
       MAX(CASE WHEN p.type_byte =  48 THEN p.value END)           AS name_char_or_faction,
       MAX(CASE WHEN p.type_byte =   0 THEN p.value END)           AS name_gimmick
FROM   (SELECT DISTINCT type, value FROM save WHERE type LIKE '%Key') s
LEFT   JOIN items ON s.value = items.key
LEFT   JOIN paloc p ON s.value = p.key
GROUP  BY s.type, s.value, items.string_key
ORDER  BY s.type, s.value;
```

### Known gaps

- **`stringinfo.pabgb`** isn't dumped yet — no Python binding exists.
  Adding `parse_string_info_from_bytes` to `vendor/crimson-rs/src/python.rs`
  is a small upstream PR (mirror of `parse_iteminfo_from_bytes` shape).
  Without it, icon-filename hashes can't be labelled in the JSONL.
- **`skill.pabgb` + `skill.pabgh`** has Python bindings but the dict
  shape is more involved than iteminfo; add when the `skill_info`
  bridge follow-up in `docs/status.md#2-n` lands.

## `extract_keycases.py`

The hand-off shape for "I want the crimson_rs RE session to write a
parser for this Key namespace — give it concrete examples." Walks the
save once, groups every scalar Key occurrence by `(type, value)`, and
emits one row per pair with:

- `total_occurrences` — how often the value appears across the save
- `examples` — up to N (default 3) `{path, block_class, top_class, top_block_idx, siblings, sibling_types}` records. **`siblings`** is the gold: every *other* scalar in the same block, which is the context that tells the RE session what the Key actually labels (`_state=5, _completedTime=…, _uiState=1` → completed mission; `_alertType=3, _generatedLocalTime=…` → log entry). **`sibling_types`** is the parallel dict mapping the same field names to their schema TypeName (`_state: "QuestStateType"`, `_usedTagList: "StringInfoKey"`, `_branchedTime: "uint64"`) — so the receiver sees which siblings are enums, cross-namespace Keys, or raw primitives without rerunning the decoder.
- `resolves` — `iteminfo_id` + the five known PALOC type bytes (`0x70 item`, `0x30 char_or_faction`, `0x00 gimmick`, `0x93 knowledge_category`, `0xC1 mission_text_template`). When *all* are null, the value is a true RE candidate — pass `--unresolved-only` to dump just these rows as a worklist file.

Compound siblings (object_list / locator / dynamic_array) are stubbed as
`"<object_list, N elements>"` so the row stays bounded while the consumer
still sees the block has more structure underneath.

```powershell
# Quest-family focused
python tools\analyze\extract_keycases.py `
    --save     "$env:LOCALAPPDATA\Pearl Abyss\CD\save\<userid>\slot0\save.save" `
    --catalogs "out\analyze\<date>_catalogs" `
    --type     MissionKey --type QuestKey --type QuestGaugeKey --type KnowledgeKey `
    --out      "out\analyze\<date>_keycases\quest_family.jsonl"

# Full hand-off bundle: every *Key TypeName + the unresolved worklist
python tools\analyze\extract_keycases.py `
    --save "..." --catalogs "..." `
    --out  "out\analyze\handoff\keycases_full.jsonl"
python tools\analyze\extract_keycases.py `
    --save "..." --catalogs "..." --unresolved-only `
    --out  "out\analyze\handoff\keycases_unresolved.jsonl"
```

Triage tip without `--unresolved-only`: pipe through
`jq 'select(.resolves.iteminfo_id == null and (.resolves.paloc | to_entries | map(.value) | all(. == null)))'`.
