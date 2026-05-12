"""inspect_save_section.py — show the decoded contents of save sections.

Reads a body.bin produced by `tools/extract/extract_save.py`, runs the
full per-object decoder (every TOC entry), and prints either:

- a one-line summary per matched block (default), or
- the decoded fields inline (`--pretty`), or
- the full structure as JSON (`--json <path>`).

Use `--class NAME` (substring match on class_name) or `--toc-index N` to
narrow down to a specific section. `--class` matches all blocks of a
class; `--toc-index` picks one specific entry.

Versions tested: 1.06.
"""

from __future__ import annotations

import argparse
import base64
import json
import sys
from pathlib import Path
from typing import Any

# Make tools/ importable when run as a plain script.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402

DEFAULT_BODY = Path("out") / "save-extract" / "slot0.bin"


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="inspect_save_section.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("--body", type=Path, default=DEFAULT_BODY)
    p.add_argument(
        "--class",
        dest="class_filter",
        metavar="NAME",
        default=None,
        help="show only blocks whose class_name contains this substring (case-insensitive)",
    )
    p.add_argument(
        "--toc-index",
        type=int,
        default=None,
        help="show only the block at this TOC index (overrides --class)",
    )
    p.add_argument("--top", type=int, default=10, help="when nothing else narrows, show at most this many blocks")
    p.add_argument("--pretty", action="store_true", help="print decoded fields inline")
    p.add_argument(
        "--bytes-as",
        choices=("hex", "base64", "drop"),
        default="hex",
        help="how to encode raw byte fields in JSON output (default hex)",
    )
    p.add_argument("--json", dest="json_out", type=Path, default=None, help="dump matched blocks as JSON to this path")
    return p


def main() -> int:
    parser = build_parser()
    require_args(parser)
    args = parser.parse_args()

    try:
        import crimson_rs
    except ImportError as e:
        print(f"crimson_rs not importable: {e}", file=sys.stderr)
        return 1

    if not args.body.is_file():
        print(f"body file not found: {args.body}", file=sys.stderr)
        return 1

    body_bytes = args.body.read_bytes()
    print(f"reading {args.body} ({len(body_bytes):,} bytes)")
    parsed = crimson_rs.decode_save_body_blocks(body_bytes)
    stats = parsed["stats"]
    print(
        f"decoded: {stats['block_count']} blocks  "
        f"fields {stats['decoded_fields']}/{stats['present_fields']}  "
        f"undecoded {stats['undecoded_bytes']:,}/{stats['total_block_bytes']:,} bytes"
    )

    blocks: list[dict] = parsed["blocks"]
    matched = filter_blocks(blocks, args)
    if not matched:
        print("\nno blocks matched the filter.")
        return 0

    print(f"\nmatched {len(matched)} block(s):")
    if args.pretty:
        for b in matched:
            print()
            print_block_pretty(b)
    else:
        for b in matched:
            n_decoded = sum(1 for f in b["fields"] if f["present"] and f["kind"] not in ("unknown", "absent"))
            n_present = sum(1 for f in b["fields"] if f["present"])
            undec = sum(end - start for start, end in b["undecoded_ranges"])
            print(
                f"  [{b['class_index']:3}] {b['class_name']:<40} "
                f"offset={b['data_offset']:>10,}  size={b['data_size']:>8,}  "
                f"fields={n_decoded}/{n_present}  undecoded={undec}"
            )

    if args.json_out is not None:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        encoded = [encode_block_for_json(b, args.bytes_as) for b in matched]
        with args.json_out.open("w", encoding="utf-8") as f:
            json.dump(
                {"source": str(args.body), "stats": stats, "blocks": encoded},
                f,
                indent=2,
                ensure_ascii=False,
            )
        print(f"\nwrote {args.json_out}")

    return 0


def filter_blocks(blocks: list[dict], args: argparse.Namespace) -> list[dict]:
    if args.toc_index is not None:
        if not 0 <= args.toc_index < len(blocks):
            print(f"--toc-index {args.toc_index} out of range (0..{len(blocks)})", file=sys.stderr)
            return []
        return [blocks[args.toc_index]]
    if args.class_filter:
        needle = args.class_filter.lower()
        return [b for b in blocks if needle in b["class_name"].lower()]
    return blocks[: args.top]


def print_block_pretty(b: dict) -> None:
    pad = b.get("trailing_pad")
    pad_str = f"  trailing_pad=0x{pad:02x}" if pad is not None else ""
    print(
        f"=== [{b['class_index']:3}] {b['class_name']}  "
        f"offset={b['data_offset']:,}  size={b['data_size']:,}  "
        f"mask={b['mask_bytes'].hex()}{pad_str}"
    )
    for f in b["fields"]:
        if not f["present"]:
            continue
        print(f"  {f['name']:<32} : {format_field_value(f)}  ({f['kind']})")
    if b["undecoded_ranges"]:
        total = sum(end - start for start, end in b["undecoded_ranges"])
        ranges_str = ", ".join(f"[{s}..{e})" for s, e in b["undecoded_ranges"])
        print(f"  -- undecoded {total} bytes at {ranges_str}")


def format_field_value(f: dict) -> str:
    kind = f["kind"]
    if kind in ("fixed_prefix", "fixed_suffix"):
        return f"{f.get('value')!r}  <{f.get('value_type', '?')}>"
    if kind == "inline_bytes":
        return f"<{f['count']} items, {len(f['bytes'])} bytes>"
    if kind == "dynamic_array":
        return f"<{f['count']} items, {len(f['bytes'])} bytes, {f.get('header_variant', '?')}>"
    if kind == "object_locator":
        child = f.get("child")
        target = f.get("child_type_name", "?")
        suffix = "" if child is None else f" inline -> {len(child['fields'])} fields"
        return f"-> {target} (offset {f.get('child_payload_offset')}){suffix}"
    if kind == "object_list":
        return (
            f"[{f['count']} elements, variant={f.get('header_variant', '?')}]"
        )
    if kind == "absent":
        return "(absent)"
    return f"<{kind}>"


def encode_block_for_json(b: dict, bytes_as: str) -> dict:
    """Recursively transform a block dict so bytes fields become strings."""
    return {
        **b,
        "mask_bytes": _encode_bytes(b["mask_bytes"], bytes_as),
        "fields": [_encode_field_for_json(f, bytes_as) for f in b["fields"]],
    }


def _encode_field_for_json(f: dict, bytes_as: str) -> dict:
    out = dict(f)
    if "bytes" in out:
        out["bytes"] = _encode_bytes(out["bytes"], bytes_as)
    if "child" in out and isinstance(out["child"], dict):
        out["child"] = encode_block_for_json(out["child"], bytes_as)
    if "elements" in out and isinstance(out["elements"], list):
        out["elements"] = [encode_block_for_json(e, bytes_as) for e in out["elements"]]
    return out


def _encode_bytes(b: Any, mode: str) -> Any:
    if not isinstance(b, (bytes, bytearray)):
        return b
    if mode == "drop":
        return None
    if mode == "base64":
        return base64.b64encode(b).decode("ascii")
    return b.hex()


if __name__ == "__main__":
    raise SystemExit(main())
