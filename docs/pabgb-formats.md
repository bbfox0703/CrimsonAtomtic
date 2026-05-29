# PABGB format family

> `.pabgb` (and its paired `.pabgh` index) are Pearl Abyss's binary game-data containers. They hold static tables: items, skills, stores, regions, factions, NPCs, mounts, gimmicks, equipment slots, terrain spawns, and more.

## What we have

`crimson-rs` already handles two members of the family **byte-perfectly**:

| Format             | Coverage                                                         | Status |
| ------------------ | ---------------------------------------------------------------- | ------ |
| `iteminfo.pabgb`   | 6,236 items (1.05) → 6,253 items (1.06), no schema drift         | ✅ done |
| `skill.pabgb` + `skill.pabgh` | 1.03 / 1.04 / 1.05 cross-version, byte-perfect roundtrip          | ✅ done |

PyO3 bindings exposed:

- `parse_iteminfo_from_file`, `write_iteminfo_to_file`, `serialize_iteminfo`
- `parse_skillinfo_from_bytes`, `serialize_skillinfo`

## What CRIMSON-DESERT-SAVE-EDITOR has (to port)

The CRIMSON-DESERT-SAVE-EDITOR Python parsers cover many other PABGB tables, each in its own file:

- `storeinfo_parser.py` — vendor inventory, prices, stock limits
- `dropsetinfo` — loot tables (referenced by `dropset_editor.py`)
- `fieldinfo_parser.py` — region rules, vehicle/mount permissions
- `vehicleinfo_parser.py` — mount/vehicle stats
- `regioninfo_parser.py` — region / zone metadata
- `equipslotinfo_parser.py` — equipment slot constraints
- `factionnode_operator_parser.py` — faction spawn mechanics
- `gimmickinfo_parser.py` — interactive object data
- `characterinfo_full_parser.py` — NPC / boss data
- `mercenaryinfo_parser.py` — mercenary / pet companion data
- `wantedinfo_parser.py` — wanted / bounty mechanics
- `reserveslot_parser.py` — reserve slot rules
- `terrain_spawn_parser.py` — terrain spawn cadence

…plus a dispatch layer (`universal_pabgb_parser.py`) and field-level helpers (`pabgb_field_parsers.py`, `pabgb_parser_local.py`).

## Port plan

Port each table into `crimson-rs` one at a time, in priority order. For each:

1. **Capture a fixture** from the current game install (extract via existing `crimson_rs.extract_file`).
2. **Write the Rust parser** following the iteminfo / skill template (uses the `BinaryRead` / `BinaryReadTracked` / `BinaryWrite` traits).
3. **Add round-trip test** — serialize matches input bytes for the live game files.
4. **Expose via PyO3** for the Python tools.
5. **Expose via C ABI** only if the C# app needs it (most game-data tables are tooling-only and won't be linked into the UI).
6. **Delete the corresponding Python parser** from anywhere in our project (it stays in CRIMSON-DESERT-SAVE-EDITOR, which we don't touch).

### Priority order (proposal — to confirm)

| Tier | Tables                                                                 | Reason |
| ---- | --------------------------------------------------------------------- | ------ |
| 1    | iteminfo, skill                                                       | done in `crimson-rs` |
| 2    | storeinfo, dropsetinfo, equipslotinfo                                 | save editor needs these to look up valid item/slot combos |
| 3    | fieldinfo, regioninfo, vehicleinfo                                    | needed for waypoint / mount editing |
| 4    | characterinfo, mercenaryinfo, gimmickinfo                             | needed for richer inventory display |
| 5    | factionnode_operator, wantedinfo, reserveslot, terrain_spawn          | niche; port when a tool actually needs them |

## What we will not preserve from the old code

- Per-file ad-hoc binary readers with manual offset bookkeeping. We use `crimson-rs` traits.
- Duplicate JSON catalogs committed alongside the binary source (`item_templates.json` vs `items.jsonl`). The parser is the catalog. See [data-policy.md](data-policy.md).
- Auto-decoder heuristics that guess the table type from file shape (`universal_pabgb_parser.py`). We dispatch by filename + magic, with an explicit error if it doesn't match.

## Cross-version stability

PABGB tables tend to be data-only between minor patches. The 1.05 → 1.06 evidence is clean: iteminfo has +17 items, zero schema changes. We expect:

- **Most patches**: parser unchanged, just new rows. The toolchain detects them automatically.
- **Major patches**: a schema field shifts; we add a version flag (the way `crimson-rs` handles `field_58` for skill format detection).

Build the regression corpus across 1.03..1.06 so a schema shift triggers a CI failure rather than a runtime surprise.
