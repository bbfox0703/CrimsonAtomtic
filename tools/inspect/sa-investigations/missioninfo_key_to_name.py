"""_proto_missioninfo_key_to_name.py - locate the internal name attached to
specific mission keys in missioninfo.pabgb by byte-scanning for the
little-endian u32 then walking the surrounding bytes for an ASCII name.
Not robust, but enough to confirm the Feast for the Road catalog name +
whether its sub-step siblings (_0/_1/_2/...) exist.
"""

from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert")

# Keys we care about: catalog rows that bulk sweep needs to handle.
# 1000913 = Feast for the Road (the one that DIDN'T apply).
# Throw a few others in for comparison: a known applied one + its X_2.
KEYS_OF_INTEREST = [
    1000913,    # Feast for the Road
    1000898,    # Hooves II catalog (from status.md)
    1003337,    # Vehicle_II_2 (the X_2 for Hooves II)
]


def main():
    print(f"extracting missioninfo.pabgb")
    pabgb = crimson_rs.extract_file(
        str(GAME_ROOT), "0008", "gamedata/binary__/client/bin", "missioninfo.pabgb")
    print(f"  {len(pabgb):,} bytes")

    # For each key, find every little-endian occurrence, then walk forward
    # for an ASCII printable run. Report the first plausible name match.
    for key in KEYS_OF_INTEREST:
        kbytes = struct.pack("<I", key)
        positions = [m.start() for m in re.finditer(re.escape(kbytes), pabgb)]
        print(f"\nKey {key} ({hex(key)}): {len(positions)} occurrence(s)")
        # Heuristic: missioninfo rows look like {u32 key, u32 name_len, name_bytes, ...}
        for p in positions[:5]:
            # Try interpreting next 4 bytes as a name length, then read that many bytes.
            if p + 8 > len(pabgb):
                continue
            name_len = struct.unpack_from("<I", pabgb, p + 4)[0]
            if 1 <= name_len <= 200 and p + 8 + name_len <= len(pabgb):
                name_bytes = pabgb[p + 8 : p + 8 + name_len]
                if all(32 <= b < 127 for b in name_bytes):
                    print(f"  @ {p}: name_len={name_len}  name={name_bytes.decode('ascii')!r}")
                    continue
            # Otherwise, look for nearby ASCII run.
            window = pabgb[max(0, p - 40):p + 200]
            text = window.decode("latin1", errors="replace")
            # Find any 5+ char ASCII identifier
            for m in re.finditer(r"[A-Za-z_][A-Za-z0-9_]{4,}", text):
                if "Challenge" in m.group(0) or "Mission" in m.group(0):
                    print(f"  @ {p}: nearby identifier = {m.group(0)!r}")
                    break

    # Bonus: dump strings near the Feast for the Road key in a 200-byte window.
    print(f"\n--- 200-byte windows around Feast for the Road (1000913) occurrences ---")
    kbytes = struct.pack("<I", 1000913)
    for m in re.finditer(re.escape(kbytes), pabgb):
        p = m.start()
        window = pabgb[max(0, p - 20):p + 200]
        print(f"\n@ {p}:")
        # Print as hex + ASCII gutter, 16 bytes/line
        for off in range(0, len(window), 16):
            chunk = window[off:off + 16]
            hexs = " ".join(f"{b:02x}" for b in chunk)
            ascii_repr = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
            print(f"  {off:4d} {hexs:<48}  {ascii_repr}")
        print(f"--- end window ---")
        break  # one occurrence is enough


if __name__ == "__main__":
    raise SystemExit(main())
