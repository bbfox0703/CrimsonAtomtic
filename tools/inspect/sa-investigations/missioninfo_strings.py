"""_proto_missioninfo_strings.py - dump every Challenge_SealedArtifact_*
ASCII string in missioninfo.pabgb to see which catalog rows have a _2
follow-up sibling. Pattern B v1 needs the follow-up to exist; if it
doesn't, TryBuildChallengeContextFromCatalogRow returns false silently
and the bulk sweep skips the challenge.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert")


def main():
    print(f"extracting missioninfo.pabgb from {GAME_ROOT}")
    pabgb = crimson_rs.extract_file(str(GAME_ROOT), "0008", "gamedata/binary__/client/bin", "missioninfo.pabgb")
    print(f"  {len(pabgb):,} bytes")

    # Search for ASCII Challenge_SealedArtifact_* substrings.
    rx = re.compile(rb"Challenge_SealedArtifact_[A-Za-z0-9_]+")
    names = sorted({m.group(0).decode("ascii") for m in rx.finditer(pabgb)})
    print(f"\nFound {len(names)} distinct Challenge_SealedArtifact_* names:")
    for n in names:
        print(f"  {n}")

    # Group by base name; flag which have _2 siblings.
    bases = {}
    for n in names:
        if n.endswith("_2"):
            base = n[:-2]
            bases.setdefault(base, {})["x2"] = n
        else:
            bases.setdefault(n, {})["base"] = n

    print(f"\nBases with X_2 sibling: {sum(1 for b in bases.values() if 'x2' in b and 'base' in b)}")
    print(f"Bases WITHOUT X_2 sibling (Pattern B v1 would skip):")
    missing = [b for b, v in sorted(bases.items()) if "base" in v and "x2" not in v]
    for b in missing:
        print(f"  {b}")

    print(f"\nX_2 names WITHOUT base sibling (orphan follow-ups):")
    orphan = [v["x2"] for b, v in sorted(bases.items()) if "x2" in v and "base" not in v]
    for n in orphan:
        print(f"  {n}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
