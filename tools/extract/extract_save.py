"""extract_save.py — decrypt + decompress a Crimson Desert .save file.

Reads a single `save.save` (or `lobby.save`) and writes:

    <out>/<slot_name>.json   metadata: header fields, sizes, HMAC status,
                             body SHA-256, source path
    <out>/<slot_name>.bin    raw decompressed body bytes (for hex/inspect)

Where `<slot_name>` is taken from the immediate parent folder of the save
file (e.g. `slot0`), so multiple invocations against different slots don't
collide in the same output dir.

The body is the same byte sequence the game sees after decompression —
parsing into structured records is the next layer (TOC, items, stats,
quests) and isn't done here.

Versions tested: 1.06.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

# Make tools/ importable when run as a plain script (without `pip install -e`).
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from common.cli import require_args  # noqa: E402
from common.paths import list_save_users, save_root  # noqa: E402


def autodiscover_save() -> Path | None:
    """Pick the first slot0/save.save we can find under %LOCALAPPDATA%."""
    try:
        users = list_save_users()
    except RuntimeError:
        return None
    for user in users:
        candidate = user / "slot0" / "save.save"
        if candidate.exists():
            return candidate
    return None


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="extract_save.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--save",
        type=Path,
        default=None,
        help=(
            "path to a save.save file. "
            "If omitted, the script picks the first slot0 under "
            f"{save_root()}."
        ),
    )
    p.add_argument(
        "--out",
        type=Path,
        required=True,
        help="output directory (created if missing)",
    )
    p.add_argument(
        "--force",
        action="store_true",
        help="overwrite existing output files",
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
            "Run scripts\\setup_python_env.ps1 to install it from "
            "vendor/crimson-rs.\n"
            f"  underlying error: {e}",
            file=sys.stderr,
        )
        return 1

    save_path: Path | None = args.save
    if save_path is None:
        save_path = autodiscover_save()
        if save_path is None:
            print(
                "No --save was given and no slot0/save.save could be found "
                f"under {save_root()}.",
                file=sys.stderr,
            )
            return 1

    if not save_path.is_file():
        print(f"not a file: {save_path}", file=sys.stderr)
        return 1

    args.out.mkdir(parents=True, exist_ok=True)
    slot_name = save_path.parent.name or save_path.stem
    json_path = args.out / f"{slot_name}.json"
    bin_path = args.out / f"{slot_name}.bin"

    if not args.force:
        existing = [p for p in (json_path, bin_path) if p.exists()]
        if existing:
            joined = ", ".join(str(p) for p in existing)
            print(
                f"refusing to overwrite existing output: {joined}\n"
                "  pass --force to replace.",
                file=sys.stderr,
            )
            return 1

    print(f"reading {save_path}")
    parsed = crimson_rs.parse_save_from_file(str(save_path))

    body: bytes = parsed["body"]
    body_sha = hashlib.sha256(body).hexdigest()

    summary = {
        "source": str(save_path),
        "slot": slot_name,
        "version": int(parsed["version"]),
        "flags": int(parsed["flags"]),
        "payload_size": int(parsed["payload_size"]),
        "uncompressed_size": int(parsed["uncompressed_size"]),
        "hmac_ok": bool(parsed["hmac_ok"]),
        "body_sha256": body_sha,
        "body_path": str(bin_path),
    }

    with json_path.open("w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, ensure_ascii=False)
    bin_path.write_bytes(body)

    print(
        f"  version={summary['version']}  flags={summary['flags']:#06x}\n"
        f"  payload={summary['payload_size']} bytes  "
        f"body={summary['uncompressed_size']} bytes  "
        f"hmac={'ok' if summary['hmac_ok'] else 'FAIL'}\n"
        f"  body sha256={body_sha}\n"
        f"wrote {json_path}\n"
        f"wrote {bin_path}"
    )

    if not summary["hmac_ok"]:
        print("WARNING: HMAC failed — the save may be tampered or our key derivation is off.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
