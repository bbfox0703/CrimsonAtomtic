"""dump_catalogs.py — pull iteminfo + PALOC catalogs out of a game install as JSONL.

Companion to `tools/analyze/dump_save_fields.py`. Where that tool flattens
a `.save` into one-row-per-field JSONL, this one dumps the **lookup
tables** the save references: iteminfo (ItemKey → string_key + metadata)
and PALOC (localized strings, keyed by `(type_byte, upper32)` extracted
from each entry's decimal-encoded u64 string_key).

Outputs (under `--out <dir>`):
    iteminfo.jsonl              one row per item, full `parse_iteminfo_from_bytes` dict
    paloc_<lang>.jsonl          one row per PALOC entry; columns include
                                `unk_id`, raw `string_key` string, derived
                                `key` (upper 32 bits), `type_byte` (lower 8
                                bits), `mid` (middle 24 bits), and the
                                localized `value` string.

Joined in duckdb against `dump_save_fields.py` output, this gives the RE
substrate the user asked for: "given an unknown Key value, does any catalog
recognise it? does it collide with a known namespace's value range?".

Skipped from this v1 (still useful, but additional work):
- `stringinfo.pabgb` — no Python binding yet; needs an upstream PR adding
  one. Lives at `0008/gamedata/binary__/client/bin/stringinfo.pabgb`.
- `skill.pabgb` + `skill.pabgh` — `parse_skillinfo_from_bytes` exists but
  the dict shape is more involved; add when the user picks up the
  `skill_info` bridge follow-up (#4 in docs/status.md).

Versions tested: 1.06.

Example — dump English PALOC + iteminfo, then ask duckdb which Keys in the
save resolve to a known item name:

    python tools/analyze/dump_catalogs.py \\
        --game-dir "D:/SteamLibrary/steamapps/common/Crimson Desert" \\
        --out out/analyze/2026-05-14_catalogs/

    duckdb -c "
      CREATE VIEW save  AS SELECT * FROM read_json_auto(
        'out/analyze/2026-05-14_save_fields/slot0.jsonl', format='newline_delimited');
      CREATE VIEW items AS SELECT * FROM read_json_auto(
        'out/analyze/2026-05-14_catalogs/iteminfo.jsonl', format='newline_delimited');
      CREATE VIEW paloc AS SELECT * FROM read_json_auto(
        'out/analyze/2026-05-14_catalogs/paloc_eng.jsonl', format='newline_delimited');

      -- For every (type, value) seen in the save, ask each catalog if it knows.
      SELECT s.type, s.value, items.string_key AS item_id, p70.value AS item_name_eng
      FROM   (SELECT DISTINCT type, value FROM save WHERE type = 'ItemKey') s
      LEFT   JOIN items ON CAST(s.value AS BIGINT) = items.key
      LEFT   JOIN paloc p70 ON CAST(s.value AS BIGINT) = p70.key AND p70.type_byte = 112
      LIMIT  20;
    "
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, TextIO

# Make tools/ importable when run as a plain script.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402
from common.paths import discover_installs  # noqa: E402

# language code -> (PAZ group dir, paloc file name). Mirrors
# LocalizationProvider.KnownLanguageCodes on the C# side; keep in sync
# when Pearl Abyss adds a new language. Group range 0019..0050.
LANG_TO_GROUP: dict[str, str] = {
    "kor": "0019",
    "eng": "0020",
    "jpn": "0021",
    "rus": "0022",
    "tur": "0023",
    "spa-es": "0024",
    "spa-mx": "0025",
    "fre": "0026",
    "ger": "0027",
    "ita": "0028",
    "pol": "0029",
    "por-br": "0030",
    "zho-tw": "0031",
    "zho-cn": "0032",
}

CATALOG_CHOICES = ("iteminfo", "paloc")

ITEMINFO_VFS_DIR = "gamedata/binary__/client/bin"
ITEMINFO_FILE_NAME = "iteminfo.pabgb"
PALOC_VFS_DIR = "gamedata/stringtable/binary__"


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="dump_catalogs.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--game-dir",
        type=Path,
        default=None,
        help="path to a Crimson Desert install. Default: first install "
        "found by common.paths.discover_installs().",
    )
    p.add_argument(
        "--out",
        type=Path,
        required=True,
        help="output directory; created if missing.",
    )
    p.add_argument(
        "--catalog",
        action="append",
        choices=CATALOG_CHOICES,
        default=None,
        help="catalog to dump. Repeatable. Default: all.",
    )
    p.add_argument(
        "--language",
        action="append",
        default=None,
        help=f"PALOC language code (repeatable). Default: eng. "
        f"Known: {', '.join(sorted(LANG_TO_GROUP))}.",
    )
    p.add_argument(
        "--force",
        action="store_true",
        help="overwrite existing JSONL files in the output dir.",
    )
    return p


def resolve_game_dir(explicit: Path | None) -> Path | None:
    if explicit is not None:
        return explicit if explicit.is_dir() else None
    installs = discover_installs()
    return installs[0] if installs else None


def coerce_json_safe(value: Any) -> Any:
    """Walk a nested PyO3 dict/list and turn bytes into hex strings.

    json.dumps refuses bytes outright; we want lossless emission so the
    JSONL is round-trip-able even when iteminfo entries carry raw byte
    fields (none observed in 1.06, but the contract should hold).
    """
    if isinstance(value, (bytes, bytearray)):
        return value.hex()
    if isinstance(value, dict):
        return {k: coerce_json_safe(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [coerce_json_safe(v) for v in value]
    return value


def split_paloc_string_key(string_key: str) -> tuple[int | None, int | None, int | None]:
    """Decompose PALOC's decimal-encoded u64 string_key.

    Returns `(key, type_byte, mid)` where:
      key       = upper 32 bits (the namespace key — ItemKey / CharacterKey / …)
      type_byte = lowest 8 bits (0x70 == item name, 0x30 == character / faction, …)
      mid       = middle 24 bits (not predictable; kept for completeness)

    Returns `(None, None, None)` when `string_key` isn't a pure decimal u64.
    """
    try:
        n = int(string_key)
    except ValueError:
        return None, None, None
    if n < 0 or n > 0xFFFF_FFFF_FFFF_FFFF:
        return None, None, None
    key = (n >> 32) & 0xFFFF_FFFF
    mid = (n >> 8) & 0xFF_FFFF
    type_byte = n & 0xFF
    return key, type_byte, mid


def dump_iteminfo(crimson_rs: Any, game_dir: Path, out_path: Path, force: bool) -> int:
    if out_path.exists() and not force:
        print(
            f"refusing to overwrite {out_path} — pass --force to replace.",
            file=sys.stderr,
        )
        return -1

    raw = crimson_rs.extract_file(
        str(game_dir), "0008", ITEMINFO_VFS_DIR, ITEMINFO_FILE_NAME
    )
    items = crimson_rs.parse_iteminfo_from_bytes(raw)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    written = 0
    with out_path.open("w", encoding="utf-8") as f:
        for item in items:
            row = coerce_json_safe(item)
            f.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")
            written += 1
    return written


def dump_paloc(
    crimson_rs: Any,
    game_dir: Path,
    lang: str,
    out_path: Path,
    force: bool,
) -> int:
    group = LANG_TO_GROUP.get(lang)
    if group is None:
        print(
            f"unknown language code {lang!r}; known: {', '.join(sorted(LANG_TO_GROUP))}",
            file=sys.stderr,
        )
        return -1
    if out_path.exists() and not force:
        print(
            f"refusing to overwrite {out_path} — pass --force to replace.",
            file=sys.stderr,
        )
        return -1

    file_name = f"localizationstring_{lang}.paloc"
    raw = crimson_rs.extract_file(str(game_dir), group, PALOC_VFS_DIR, file_name)
    entries = crimson_rs.parse_paloc_bytes(raw)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    written = 0
    with out_path.open("w", encoding="utf-8") as f:
        for entry in entries:
            string_key = entry.get("string_key", "")
            key, type_byte, mid = split_paloc_string_key(string_key)
            row = {
                "lang": lang,
                "unk_id": entry.get("unk_id"),
                "string_key": string_key,
                "key": key,
                "type_byte": type_byte,
                "mid": mid,
                "value": entry.get("string_value", ""),
            }
            f.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")
            written += 1
    return written


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

    game_dir = resolve_game_dir(args.game_dir)
    if game_dir is None:
        if args.game_dir is None:
            print(
                "no --game-dir given and discover_installs() found nothing. "
                "Pass --game-dir <CrimsonDesert install root>.",
                file=sys.stderr,
            )
        else:
            print(f"not a directory: {args.game_dir}", file=sys.stderr)
        return 1
    print(f"game-dir: {game_dir}", file=sys.stderr)

    catalogs = args.catalog or list(CATALOG_CHOICES)
    languages = args.language or ["eng"]

    args.out.mkdir(parents=True, exist_ok=True)

    rc = 0
    if "iteminfo" in catalogs:
        out_path = args.out / "iteminfo.jsonl"
        n = dump_iteminfo(crimson_rs, game_dir, out_path, args.force)
        if n < 0:
            rc = 1
        else:
            print(f"wrote {out_path}: {n:,} items", file=sys.stderr)

    if "paloc" in catalogs:
        for lang in languages:
            out_path = args.out / f"paloc_{lang}.jsonl"
            n = dump_paloc(crimson_rs, game_dir, lang, out_path, args.force)
            if n < 0:
                rc = 1
            else:
                print(
                    f"wrote {out_path}: {n:,} entries (lang={lang})",
                    file=sys.stderr,
                )

    return rc


if __name__ == "__main__":
    raise SystemExit(main())
