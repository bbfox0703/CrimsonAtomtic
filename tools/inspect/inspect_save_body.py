"""inspect_save_body.py — show schema + TOC of a decoded save body.

Reads a body.bin produced by `tools/extract/extract_save.py` and prints
the record types and TOC that live inside. Use this to figure out where
in a save a given category of data sits, before writing a per-section
decoder.

Default behavior is a terse summary suitable for grepping. Pass `--all`
or `--top N` to widen the display, or `--json <path>` to dump the full
structure for offline inspection.

Versions tested: 1.06.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

# Make tools/ importable when run as a plain script.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402

DEFAULT_BODY = Path("out") / "save-extract" / "slot0.bin"


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="inspect_save_body.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--body",
        type=Path,
        default=DEFAULT_BODY,
        help=f"path to a body.bin (default: {DEFAULT_BODY})",
    )
    p.add_argument(
        "--top",
        type=int,
        default=10,
        help="show this many top types + TOC entries (default 10)",
    )
    p.add_argument(
        "--all",
        action="store_true",
        help="show every type and every TOC entry (overrides --top)",
    )
    p.add_argument(
        "--filter-type",
        metavar="NAME",
        default=None,
        help="when set, list only TOC entries whose class_name matches this string (substring match)",
    )
    p.add_argument(
        "--json",
        dest="json_out",
        type=Path,
        default=None,
        help="dump the full parsed structure as JSON to this path",
    )
    return p


def main() -> int:
    parser = build_parser()
    require_args(parser)
    args = parser.parse_args()

    try:
        import crimson_rs
    except ImportError as e:
        print(
            "crimson_rs is not importable. "
            "Run scripts\\setup_python_env.ps1 first.\n"
            f"  underlying error: {e}",
            file=sys.stderr,
        )
        return 1

    if not args.body.is_file():
        print(
            f"body file not found: {args.body}\n"
            "Run tools\\extract\\extract_save.py first, or pass --body <path>.",
            file=sys.stderr,
        )
        return 1

    body_bytes = args.body.read_bytes()
    print(f"reading {args.body} ({len(body_bytes):,} bytes)")
    parsed = crimson_rs.parse_save_body_from_bytes(body_bytes)

    schema = parsed["schema"]
    toc = parsed["toc"]

    print(
        f"schema: types={schema['type_count']}  "
        f"root='{schema['root_type']}'  "
        f"header_tag={schema['header_tag']:#06x}  "
        f"schema_end={schema['schema_end']:,}"
    )
    print(
        f"toc:    entries={toc['toc_count']}  "
        f"stream_size={toc['stream_size']:,}  "
        f"prefix_zero={toc['prefix_zero']}"
    )

    # ── Types ──────────────────────────────────────────────────────────
    types = schema["types"]
    show_n = len(types) if args.all else min(args.top, len(types))
    print()
    print(f"top {show_n} types (by index):")
    for t in types[:show_n]:
        print(
            f"  [{t['index']:3}] {t['name']:<45} "
            f"fields={len(t['fields'])}"
        )

    # ── TOC entries ────────────────────────────────────────────────────
    entries = toc["entries"]
    if args.filter_type:
        needle = args.filter_type.lower()
        entries = [e for e in entries if needle in e["class_name"].lower()]
        print()
        print(f"TOC entries matching '{args.filter_type}': {len(entries)}")
    show_n = len(entries) if args.all else min(args.top, len(entries))
    print()
    print(f"top {show_n} TOC entries:")
    for e in entries[:show_n]:
        print(
            f"  [{e['index']:4}] class={e['class_index']:3} "
            f"{e['class_name']:<40} "
            f"offset={e['data_offset']:>10,}  size={e['data_size']:>8,}"
        )

    # ── Aggregate: TOC entries per class ───────────────────────────────
    counts = Counter(e["class_name"] for e in toc["entries"])
    print()
    print("TOC entries per class (top 15):")
    for name, n in counts.most_common(15):
        print(f"  {n:>6}  {name}")

    if args.json_out is not None:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        with args.json_out.open("w", encoding="utf-8") as f:
            json.dump(
                {
                    "source": str(args.body),
                    "schema": schema,
                    "toc": toc,
                },
                f,
                indent=2,
                ensure_ascii=False,
                default=_json_default,
            )
        print(f"\nwrote {args.json_out}")

    return 0


def _json_default(obj: object) -> object:
    if isinstance(obj, bytes):
        return obj.hex()
    raise TypeError(f"unserializable: {type(obj).__name__}")


if __name__ == "__main__":
    raise SystemExit(main())
