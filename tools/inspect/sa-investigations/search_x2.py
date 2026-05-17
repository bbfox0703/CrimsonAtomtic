"""_proto_search_x2.py - byte-scan missioninfo.pabgb for the literal name
`Challenge_SealedArtifact_Living_VII_2`. Compare with `_3` and `_1` so
we can see what record shape looks like + recover the missing X_2 key.
"""

from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert")


def main():
    pabgb = crimson_rs.extract_file(
        str(GAME_ROOT), "0008", "gamedata/binary__/client/bin", "missioninfo.pabgb")
    print(f"missioninfo.pabgb: {len(pabgb):,} bytes")

    for name in [
        "Challenge_SealedArtifact_Living_VII",
        "Challenge_SealedArtifact_Living_VII_1",
        "Challenge_SealedArtifact_Living_VII_2",
        "Challenge_SealedArtifact_Living_VII_3",
        "Challenge_SealedArtifact_Vehicle_II",
        "Challenge_SealedArtifact_Vehicle_II_2",
    ]:
        positions = [m.start() for m in re.finditer(re.escape(name.encode("ascii")), pabgb)]
        print(f"\n{name!r}: {len(positions)} occurrence(s) at {positions[:8]}")
        for p in positions:
            # Check if 4 bytes before is a plausible length prefix.
            if p >= 4:
                length_prefix = struct.unpack_from("<I", pabgb, p - 4)[0]
                if length_prefix == len(name):
                    # Look at the bytes BEFORE the length prefix for a u32 key.
                    # Common shape: key (u32) + small marker + length (u32) + name + body
                    # Let's print a 32-byte window before length prefix.
                    pre_start = max(0, p - 4 - 32)
                    pre_bytes = pabgb[pre_start:p - 4]
                    hexs = " ".join(f"{b:02x}" for b in pre_bytes)
                    ascii_repr = "".join(chr(b) if 32 <= b < 127 else "." for b in pre_bytes)
                    print(f"  @ {p} (len-prefix MATCH {length_prefix}):")
                    print(f"    pre32 hex: {hexs}")
                    print(f"    pre32 ascii: {ascii_repr}")
                    # Try common offsets for the key: -4 (just before len), -8, -12
                    for k_off in (-8, -12, -16, -20, -24):
                        kp = p - 4 + k_off
                        if kp >= 0:
                            k = struct.unpack_from("<I", pabgb, kp)[0]
                            if 1_000_000 <= k <= 2_000_000:  # plausible mission key
                                print(f"    candidate key @ offset {k_off}: {k}")


if __name__ == "__main__":
    raise SystemExit(main())
