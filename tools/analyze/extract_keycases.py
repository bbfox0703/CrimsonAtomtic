"""extract_keycases.py — package real-world Key examples for the crimson_rs RE session.

Given a save + the catalog dumps from `dump_catalogs.py`, emit one row per
distinct `(type, value)` for every schema TypeName that ends in `Key`.
Each row carries the *context* a parser-writing session needs to figure
out which game-data file resolves the Key:

  type                  schema TypeName (e.g. "MissionKey", "QuestGaugeKey")
  value                 the integer value (single-typed; bool packed as 0/1)
  total_occurrences     how many times this exact (type, value) was seen in the save
  examples              up to --max-per-value occurrences; each one is
                        `{path, block_class, top_class, siblings, sibling_types}`.
                        `siblings` is every *other* scalar field in the
                        enclosing block (the "context" — `_state`,
                        `_completedTime`, etc. — which is what tells you
                        what the Key is actually labelling).
                        `sibling_types` is a parallel dict from the same
                        field name to its schema TypeName (e.g.
                        `_state: "QuestStateType"`,
                        `_delayedFromMissionKey: "MissionKey"`,
                        `_completedTime: "uint64"`). Lets the receiving
                        session see which siblings are enums, which are
                        cross-namespace Keys, and which are raw primitives.
  resolves              cross-catalog hits: `iteminfo_id` from iteminfo, and
                        `paloc` = `{ "0xNN": name | null }` for each PALOC
                        type byte. Helps the receiving session see which
                        catalogs already explain the value (and which Keys
                        sit in a still-unresolved namespace).

Filter by `--type <TypeName>` (repeatable) to scope. Default is every
`*Key` TypeName seen in the save.

Versions tested: 1.06.

Example — build a real-case pack for MissionKey + QuestKey + QuestGaugeKey,
which the other session can iterate over without re-running crimson_rs:

    python tools/analyze/extract_keycases.py \\
        --save     "%LOCALAPPDATA%/Pearl Abyss/CD/save/<userid>/slot0/save.save" \\
        --catalogs out/analyze/2026-05-14_catalogs/ \\
        --type     MissionKey --type QuestKey --type QuestGaugeKey \\
        --out      out/analyze/2026-05-14_keycases/quest_family.jsonl
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable, TextIO

# Make tools/ importable when run as a plain script.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402

# PALOC type bytes we *know* a meaning for (status.md). Carrying them as a
# fixed list — rather than dumping every byte we saw — keeps each row's
# `resolves.paloc` compact (one slot per known namespace) and stable
# across saves. Unknown bytes are still discoverable via the raw paloc
# JSONL; this is the "labelled" subset.
KNOWN_PALOC_TYPE_BYTES: tuple[tuple[int, str], ...] = (
    (0x70, "item"),
    (0x30, "char_or_faction"),
    (0x00, "gimmick"),
    (0x93, "knowledge_category"),
    (0xC1, "mission_text_template"),
)

SCALAR_KINDS = ("fixed_prefix", "fixed_suffix")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="extract_keycases.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--save",
        type=Path,
        required=True,
        help="path to a .save file.",
    )
    p.add_argument(
        "--catalogs",
        type=Path,
        required=True,
        help="directory containing iteminfo.jsonl and paloc_<lang>.jsonl "
        "(from tools/analyze/dump_catalogs.py).",
    )
    p.add_argument(
        "--paloc-lang",
        type=str,
        default="eng",
        help="which paloc_<lang>.jsonl to use for resolves. Default: eng.",
    )
    p.add_argument(
        "--type",
        action="append",
        default=None,
        help="schema TypeName to extract cases for. Repeatable. "
        "Default: every *Key TypeName seen in the save.",
    )
    p.add_argument(
        "--max-per-value",
        type=int,
        default=3,
        help="cap on `examples` per distinct (type, value). Default: 3. "
        "Total occurrence count is still tracked in `total_occurrences`.",
    )
    p.add_argument(
        "--max-siblings",
        type=int,
        default=24,
        help="cap on how many sibling scalars to include per example. "
        "Blocks with very wide field tables (e.g. CharacterStatusSaveData) "
        "would otherwise dominate row size. Default: 24.",
    )
    p.add_argument(
        "--unresolved-only",
        action="store_true",
        help="emit only rows where every catalog returned null (no iteminfo "
        "hit and no PALOC hit at any known type byte). This is the worklist "
        "for the parser-writing session — values that need a brand-new "
        "resolver, not just an existing bridge.",
    )
    p.add_argument(
        "--out",
        type=Path,
        required=True,
        help="output JSONL path.",
    )
    return p


# ── Catalog loading ────────────────────────────────────────────────────────


def load_iteminfo(path: Path) -> dict[int, str]:
    if not path.is_file():
        return {}
    out: dict[int, str] = {}
    with path.open(encoding="utf-8") as f:
        for line in f:
            d = json.loads(line)
            k = d.get("key")
            s = d.get("string_key")
            if isinstance(k, int) and isinstance(s, str):
                out[k] = s
    return out


def load_paloc(path: Path) -> dict[tuple[int, int], str]:
    """(type_byte, key) -> localized value."""
    if not path.is_file():
        return {}
    out: dict[tuple[int, int], str] = {}
    with path.open(encoding="utf-8") as f:
        for line in f:
            d = json.loads(line)
            k = d.get("key")
            tb = d.get("type_byte")
            v = d.get("value")
            if isinstance(k, int) and isinstance(tb, int) and isinstance(v, str):
                out[(tb, k)] = v
    return out


# ── Save walking ───────────────────────────────────────────────────────────


def scalar_value(field_dict: dict) -> Any:
    """Same shaping rule as `dump_save_fields._split_typed_value`'s int slot.

    Returns the integer-or-bool value when the scalar is integral / boolean;
    returns `None` for floats and bytes (those aren't useful Key contexts).
    Floats and byte-blobs are skipped on the *sibling* side too — they're
    almost never the "this is a `Key`" companion field.
    """
    raw = field_dict.get("value")
    if isinstance(raw, bool):
        return 1 if raw else 0
    if isinstance(raw, int):
        return raw
    return None


def iter_blocks(
    block: dict,
    *,
    top_class: str,
    parent_path: str,
) -> Iterable[tuple[str, str, str, dict]]:
    """Yield `(top_class, block_class, path_prefix, block_dict)` for every
    enclosing block reachable from `block`, including itself.

    `path_prefix` is the prefix to which a leaf field's name should be
    appended (with `.` separator) to form a full descent path. For the
    root block this is the empty string; for nested blocks it's e.g.
    `_missionStateList[42]`.
    """
    block_class = block.get("class_name") or "?"
    yield top_class, block_class, parent_path, block
    for f in block.get("fields", []):
        if not f.get("present"):
            continue
        kind = f.get("kind")
        name = f.get("name") or ""
        next_prefix = name if not parent_path else f"{parent_path}.{name}"
        if kind == "object_locator":
            child = f.get("child")
            if isinstance(child, dict):
                yield from iter_blocks(
                    child, top_class=top_class, parent_path=next_prefix
                )
        elif kind == "object_list":
            for k, elem in enumerate(f.get("elements") or []):
                if isinstance(elem, dict):
                    yield from iter_blocks(
                        elem,
                        top_class=top_class,
                        parent_path=f"{next_prefix}[{k}]",
                    )


def collect_block_siblings(
    block: dict, exclude_field_index: int, max_siblings: int
) -> tuple[dict[str, Any], dict[str, str]]:
    """Build the `(siblings, sibling_types)` pair for one row.

    `siblings` is `{field_name: value_or_compound_stub}` for every *other*
    present field in the block. Scalars come through as ints / bools; the
    three compound kinds (object_list / dynamic_array / object_locator)
    are stubbed to a short tag like `"<object_list, 46541 elements>"` —
    the receiving session sees the block has more structure underneath
    without us dumping the sub-tree.

    `sibling_types` is the *parallel* dict mapping the same field names to
    their schema TypeName (e.g. `"QuestStateType"`, `"MissionKey"`,
    `"uint64"`). The receiver can pick out cross-namespace Keys, enums,
    and raw primitives directly without rerunning the decoder.
    """
    siblings: dict[str, Any] = {}
    types: dict[str, str] = {}
    overflow = 0
    for idx, f in enumerate(block.get("fields", [])):
        if idx == exclude_field_index:
            continue
        if not f.get("present"):
            continue
        name = f.get("name") or f"#{idx}"
        kind = f.get("kind")
        type_name = f.get("type_name") or ""
        slot: Any = None
        if kind in SCALAR_KINDS:
            v = scalar_value(f)
            if v is None:
                continue
            slot = v
        elif kind == "object_list":
            count = f.get("count") or 0
            if count:
                slot = f"<object_list, {count} elements>"
        elif kind == "dynamic_array":
            count = f.get("count") or 0
            if count:
                slot = f"<dynamic_array, {count} elements>"
        elif kind == "object_locator":
            child = f.get("child")
            if isinstance(child, dict):
                slot = f"<locator -> {child.get('class_name', '?')}>"
        if slot is None:
            continue
        if len(siblings) >= max_siblings:
            overflow += 1
            continue
        siblings[name] = slot
        if type_name:
            types[name] = type_name
    if overflow:
        siblings["__truncated__"] = overflow
    return siblings, types


# ── Main aggregation ──────────────────────────────────────────────────────


def build_resolves(
    value: int,
    items_by_key: dict[int, str],
    paloc_by_type_key: dict[tuple[int, int], str],
) -> dict[str, Any]:
    paloc: dict[str, str | None] = {}
    for tb, label in KNOWN_PALOC_TYPE_BYTES:
        paloc[f"0x{tb:02X}_{label}"] = paloc_by_type_key.get((tb, value))
    return {
        "iteminfo_id": items_by_key.get(value),
        "paloc": paloc,
    }


def aggregate_cases(
    blocks: list[dict],
    type_filter: set[str] | None,
    max_siblings: int,
) -> dict[tuple[str, int], dict[str, Any]]:
    """Walk every block, group occurrences by (type, value)."""
    cases: dict[tuple[str, int], dict[str, Any]] = {}

    for top_idx, top_block in enumerate(blocks):
        top_class = top_block.get("class_name") or "?"
        for _, block_class, path_prefix, block in iter_blocks(
            top_block, top_class=top_class, parent_path=""
        ):
            for field_idx, f in enumerate(block.get("fields", [])):
                if not f.get("present"):
                    continue
                if f.get("kind") not in SCALAR_KINDS:
                    continue
                type_name = f.get("type_name") or ""
                if not type_name.endswith("Key"):
                    continue
                if type_filter is not None and type_name not in type_filter:
                    continue
                value = scalar_value(f)
                if value is None:
                    continue

                bucket = cases.setdefault(
                    (type_name, value),
                    {"total_occurrences": 0, "examples": []},
                )
                bucket["total_occurrences"] += 1

                # Keep first max-per-value examples (caller bounds it later).
                # We always collect; the truncate happens at emit time so
                # `total_occurrences` stays accurate.
                field_name = f.get("name") or f"#{field_idx}"
                leaf_path = (
                    field_name if not path_prefix
                    else f"{path_prefix}.{field_name}"
                )
                siblings, sibling_types = collect_block_siblings(
                    block, field_idx, max_siblings
                )
                bucket["examples"].append(
                    {
                        "path": leaf_path,
                        "block_class": block_class,
                        "top_class": top_class,
                        "top_block_idx": top_idx,
                        "siblings": siblings,
                        "sibling_types": sibling_types,
                    }
                )
    return cases


def _is_fully_unresolved(resolves: dict[str, Any]) -> bool:
    """True iff iteminfo and every known PALOC type byte all returned null."""
    if resolves.get("iteminfo_id") is not None:
        return False
    paloc = resolves.get("paloc") or {}
    return all(v is None for v in paloc.values())


def emit_cases(
    cases: dict[tuple[str, int], dict[str, Any]],
    items_by_key: dict[int, str],
    paloc_by_type_key: dict[tuple[int, int], str],
    max_per_value: int,
    unresolved_only: bool,
    sink: TextIO,
) -> tuple[int, int]:
    """Returns (rows_written, rows_skipped_by_filter)."""
    rows = 0
    skipped = 0
    # Stable order: by type name, then by value. Makes diffs across runs readable.
    for (type_name, value) in sorted(cases.keys()):
        bucket = cases[(type_name, value)]
        resolves = build_resolves(value, items_by_key, paloc_by_type_key)
        if unresolved_only and not _is_fully_unresolved(resolves):
            skipped += 1
            continue
        examples = bucket["examples"][:max_per_value]
        row = {
            "type": type_name,
            "value": value,
            "total_occurrences": bucket["total_occurrences"],
            "resolves": resolves,
            "examples": examples,
        }
        sink.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")))
        sink.write("\n")
        rows += 1
    return rows, skipped


def main() -> int:
    parser = build_parser()
    require_args(parser)
    args = parser.parse_args()

    try:
        import crimson_rs  # type: ignore[import-not-found]
    except ImportError as e:
        print(
            "crimson_rs is not importable. "
            "Run scripts\\setup_python_env.ps1 to install it from "
            "vendor/crimson-rs.\n"
            f"  underlying error: {e}",
            file=sys.stderr,
        )
        return 1

    if not args.save.is_file():
        print(f"not a file: {args.save}", file=sys.stderr)
        return 1
    if not args.catalogs.is_dir():
        print(f"not a directory: {args.catalogs}", file=sys.stderr)
        return 1

    iteminfo_path = args.catalogs / "iteminfo.jsonl"
    paloc_path = args.catalogs / f"paloc_{args.paloc_lang}.jsonl"

    print(f"loading iteminfo: {iteminfo_path}", file=sys.stderr)
    items_by_key = load_iteminfo(iteminfo_path)
    print(f"  {len(items_by_key):,} items", file=sys.stderr)

    print(f"loading paloc:    {paloc_path}", file=sys.stderr)
    paloc_by_type_key = load_paloc(paloc_path)
    print(f"  {len(paloc_by_type_key):,} resolvable entries", file=sys.stderr)

    print(f"parsing save:     {args.save}", file=sys.stderr)
    parsed = crimson_rs.parse_save_from_file(str(args.save))
    decoded = crimson_rs.decode_save_body_blocks(parsed["body"])
    blocks: list[dict] = decoded["blocks"]
    print(f"  {len(blocks):,} top-level blocks", file=sys.stderr)

    type_filter: set[str] | None = set(args.type) if args.type else None

    cases = aggregate_cases(blocks, type_filter, args.max_siblings)
    print(
        f"aggregated:       {len(cases):,} distinct (type, value) pairs",
        file=sys.stderr,
    )

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8") as f:
        rows, skipped = emit_cases(
            cases,
            items_by_key,
            paloc_by_type_key,
            args.max_per_value,
            args.unresolved_only,
            f,
        )
    msg = f"wrote {args.out}: {rows:,} rows"
    if args.unresolved_only:
        msg += f" (filtered out {skipped:,} resolved by --unresolved-only)"
    print(msg, file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
