"""dump_save_fields.py — flatten a Crimson Desert .save into one-row-per-field JSONL.

Reads one or more .save files, runs the full crimson_rs decoder, and emits
one JSON object per line (JSONL) for every decoded scalar field. Useful for
correlation / RE work: load into duckdb / pandas to find numerical
relationships between key types (e.g. does `QuestKey` value X also appear
as a `MissionKey` somewhere else? Which `StringInfoKey` hashes show up in
which lists?).

Row shape — every key present unless noted:
  save        save id, derived from the .save file's parent dir name (e.g. "slot0")
  top_block   top-level TOC block index (uint)
  top_class   top-level block class_name (e.g. "QuestSaveData")
  class       immediate enclosing block class_name (== top_class for top-level scalars)
  path        dotted descent path, e.g. "_missionStateList[0]._key"
  field       leaf field name
  type        schema TypeName (e.g. "MissionKey", "QuestKey", "uint32", "bool")
  prim        low-level primitive tag from the decoder ("u32", "f32", "bool", "bytes")
  kind        "fixed_prefix" | "fixed_suffix" | "dynamic_array_elem"
  value       integer value (any unsigned/signed int, or bool packed as 0/1).
              `null` when `prim` is `f32` / `f64` / `bytes`. Single typed
              column so `WHERE value = N` and `CAST(value AS BIGINT)` are
              clean in duckdb without a polymorphic type clash.
  value_f     float64 value when `prim` is `f32` / `f64`, else `null`.
  value_hex   lowercase hex string when `prim` is `bytes`, else `null`.
  array_index optional; only present on `dynamic_array_elem` rows.

Skipped: `absent`, `object_locator` and `object_list` shells (recursed into,
not emitted), `inline_bytes`. `dynamic_array` is opt-in via
`--include-array-elements`; emitted only when every element has the same
1/2/4/8-byte width.

Versions tested: 1.06.

Example — dump every key field across two save snapshots into one file,
ready for duckdb:

    python tools/analyze/dump_save_fields.py \\
        --save "C:/path/to/slot0/save.save" \\
        --save "X:/backups/1.06/2026-04-29/slot0/save.save" \\
        --include-array-elements \\
        --out out/analyze/2026-05-14_save_fields/all.jsonl

    duckdb -c "SELECT type, COUNT(DISTINCT value) AS n_unique, MIN(value), MAX(value) \\
               FROM read_json_auto('out/analyze/2026-05-14_save_fields/all.jsonl', \\
                                   format='newline_delimited') \\
               WHERE type LIKE '%Key' GROUP BY type ORDER BY n_unique DESC;"
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Iterator, TextIO

# Make tools/ importable when run as a plain script.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402

SCALAR_KINDS = ("fixed_prefix", "fixed_suffix")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="dump_save_fields.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--save",
        type=Path,
        action="append",
        required=True,
        help="path to a .save file. Repeatable; pass once per save to "
        "dump multiple snapshots into the same JSONL stream.",
    )
    p.add_argument(
        "--out",
        type=Path,
        default=None,
        help="output JSONL path. Default: stdout.",
    )
    p.add_argument(
        "--include-array-elements",
        action="store_true",
        help="emit one row per element for `dynamic_array` fields whose "
        "elements share a uniform 1/2/4/8-byte width. Off by default; "
        "turn on when hunting Key types stored in dynamic arrays "
        "(e.g. StringInfoKey lists on MissionStateData).",
    )
    p.add_argument(
        "--save-id",
        type=str,
        default=None,
        help="override the auto-derived save id for the single --save case. "
        "Ignored when more than one --save is given.",
    )
    return p


def derive_save_id(save_path: Path) -> str:
    """`<dir>/slot0/save.save` -> `slot0`; falls back to file stem."""
    parent = save_path.parent.name
    return parent or save_path.stem


def emit_rows(
    blocks: list[dict],
    save_id: str,
    include_array: bool,
) -> Iterator[dict]:
    for block_idx, block in enumerate(blocks):
        top_class = block.get("class_name") or "?"
        yield from _walk_block(
            block=block,
            save_id=save_id,
            top_block=block_idx,
            top_class=top_class,
            current_class=top_class,
            path="",
            include_array=include_array,
        )


def _walk_block(
    *,
    block: dict,
    save_id: str,
    top_block: int,
    top_class: str,
    current_class: str,
    path: str,
    include_array: bool,
) -> Iterator[dict]:
    for f in block.get("fields", []):
        if not f.get("present"):
            continue
        name = f.get("name") or ""
        kind = f.get("kind")
        field_path = name if not path else f"{path}.{name}"

        if kind in SCALAR_KINDS:
            yield _scalar_row(
                save_id=save_id,
                top_block=top_block,
                top_class=top_class,
                current_class=current_class,
                path=field_path,
                field=name,
                field_dict=f,
            )
        elif kind == "object_locator":
            child = f.get("child")
            if isinstance(child, dict):
                yield from _walk_block(
                    block=child,
                    save_id=save_id,
                    top_block=top_block,
                    top_class=top_class,
                    current_class=child.get("class_name") or current_class,
                    path=field_path,
                    include_array=include_array,
                )
        elif kind == "object_list":
            for k, elem in enumerate(f.get("elements") or []):
                if not isinstance(elem, dict):
                    continue
                yield from _walk_block(
                    block=elem,
                    save_id=save_id,
                    top_block=top_block,
                    top_class=top_class,
                    current_class=elem.get("class_name") or current_class,
                    path=f"{field_path}[{k}]",
                    include_array=include_array,
                )
        elif kind == "dynamic_array" and include_array:
            yield from _dynamic_array_rows(
                save_id=save_id,
                top_block=top_block,
                top_class=top_class,
                current_class=current_class,
                path=field_path,
                field=name,
                field_dict=f,
            )


def _scalar_row(
    *,
    save_id: str,
    top_block: int,
    top_class: str,
    current_class: str,
    path: str,
    field: str,
    field_dict: dict,
) -> dict:
    prim = field_dict.get("value_type")
    value_int, value_f, value_hex = _split_typed_value(prim, field_dict.get("value"))
    return {
        "save": save_id,
        "top_block": top_block,
        "top_class": top_class,
        "class": current_class,
        "path": path,
        "field": field,
        "type": field_dict.get("type_name"),
        "prim": prim,
        "kind": field_dict.get("kind"),
        "value": value_int,
        "value_f": value_f,
        "value_hex": value_hex,
    }


def _split_typed_value(
    prim: Any, raw: Any
) -> tuple[int | None, float | None, str | None]:
    """Route a decoder-emitted scalar into (int, float, hex) columns.

    Rationale: duckdb's `read_json_auto` infers one type per column. If we
    leave `value` polymorphic (int vs hex string vs float vs bool), the
    inferred type is VARCHAR and every numeric query needs an explicit cast
    that fails on the string rows. Splitting at emit-time keeps each column
    homogeneous: `value` is always BIGINT-or-null, `value_f` is DOUBLE-or-
    null, `value_hex` is VARCHAR-or-null.

    bool is folded into `value` as 0/1 so the column stays BIGINT and so
    Key-style state enums (`_state: QuestStateType` u8 vs `_isWaitBranch:
    bool`) compare on the same axis.
    """
    if isinstance(raw, (bytes, bytearray)):
        return None, None, bytes(raw).hex()
    # PyO3 surfaces bools as Python `bool`, which is a subclass of `int`.
    # Check `bool` first so True/False don't fall into the int branch with
    # `prim == 'bool'` and `value: True` (legal JSON, but mixes types with
    # the predominantly-int rows once duckdb sees the column).
    if isinstance(raw, bool):
        return (1 if raw else 0), None, None
    if isinstance(raw, int):
        return raw, None, None
    if isinstance(raw, float):
        return None, float(raw), None
    if raw is None:
        return None, None, None
    # Defensive fallback: an unexpected type slipped through (e.g. a
    # future patch's new prim tag). Preserve the value as a string so we
    # don't silently drop it.
    return None, None, str(raw)


def _dynamic_array_rows(
    *,
    save_id: str,
    top_block: int,
    top_class: str,
    current_class: str,
    path: str,
    field: str,
    field_dict: dict,
) -> Iterator[dict]:
    count = field_dict.get("count") or 0
    blob = field_dict.get("bytes") or b""
    if count <= 0 or not isinstance(blob, (bytes, bytearray)) or len(blob) % count != 0:
        return
    width = len(blob) // count
    if width not in (1, 2, 4, 8):
        return
    type_name = field_dict.get("type_name")
    for k in range(count):
        chunk = bytes(blob[k * width : (k + 1) * width])
        value = int.from_bytes(chunk, "little", signed=False)
        yield {
            "save": save_id,
            "top_block": top_block,
            "top_class": top_class,
            "class": current_class,
            "path": f"{path}[{k}]",
            "field": field,
            "type": type_name,
            "prim": f"u{width * 8}",
            "kind": "dynamic_array_elem",
            "value": value,
            "value_f": None,
            "value_hex": None,
            "array_index": k,
        }


def process_save(
    save_path: Path,
    save_id: str,
    include_array: bool,
    sink: TextIO,
) -> tuple[int, int]:
    import crimson_rs  # type: ignore[import-not-found]

    parsed = crimson_rs.parse_save_from_file(str(save_path))
    body: bytes = parsed["body"]
    decoded = crimson_rs.decode_save_body_blocks(body)
    blocks: list[dict] = decoded["blocks"]
    rows = 0
    for row in emit_rows(blocks, save_id, include_array):
        sink.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")))
        sink.write("\n")
        rows += 1
    return len(blocks), rows


def main() -> int:
    parser = build_parser()
    require_args(parser)
    args = parser.parse_args()

    try:
        import crimson_rs  # noqa: F401
    except ImportError as e:
        print(
            "crimson_rs is not importable. "
            "Run scripts\\setup_python_env.ps1 to install it from "
            "vendor/crimson-rs.\n"
            f"  underlying error: {e}",
            file=sys.stderr,
        )
        return 1

    saves: list[Path] = []
    for s in args.save:
        if not s.is_file():
            print(f"not a file: {s}", file=sys.stderr)
            return 1
        saves.append(s)

    if args.save_id is not None and len(saves) != 1:
        print(
            "--save-id can only be combined with exactly one --save; "
            f"got {len(saves)}",
            file=sys.stderr,
        )
        return 1

    if args.out is not None:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        out_handle: TextIO = args.out.open("w", encoding="utf-8")
        close_after = True
    else:
        out_handle = sys.stdout
        close_after = False

    try:
        total_rows = 0
        for s in saves:
            sid = args.save_id if args.save_id else derive_save_id(s)
            block_count, row_count = process_save(
                s, sid, args.include_array_elements, out_handle
            )
            total_rows += row_count
            print(
                f"{s}: save_id={sid!r}  blocks={block_count}  "
                f"rows={row_count:,}",
                file=sys.stderr,
            )
        print(
            f"total: {total_rows:,} rows from {len(saves)} save(s)",
            file=sys.stderr,
        )
    finally:
        if close_after:
            out_handle.close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
