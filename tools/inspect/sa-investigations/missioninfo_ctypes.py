"""_proto_missioninfo_ctypes.py - call crimson_missioninfo_* C ABI via
ctypes to confirm whether `Challenge_SealedArtifact_Living_VII_2` resolves
to a non-null mission key. If it doesn't, that's why Pattern B v1 fails
on Feast for the Road (1000913) — the X_2 follow-up lookup needs the
sibling name to exist in the anchor-scan bridge.
"""

from __future__ import annotations

import ctypes as C
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs  # noqa: F401  -- used implicitly for extract_file

DLL = Path(r"D:\Github\CrimsonAtomtic\vendor\crimson-rs\target\release\crimson_rs.dll")
GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert")


def main():
    dll = C.WinDLL(str(DLL))

    # Function signatures
    load = dll.crimson_missioninfo_load_from_bytes
    load.argtypes = [C.c_char_p, C.c_size_t, C.POINTER(C.c_void_p)]
    load.restype = C.c_int

    free = dll.crimson_missioninfo_free
    free.argtypes = [C.c_void_p]
    free.restype = None

    count = dll.crimson_missioninfo_entry_count
    count.argtypes = [C.c_void_p, C.POINTER(C.c_uint32)]
    count.restype = C.c_int

    get_entry = dll.crimson_missioninfo_get_entry
    get_entry.argtypes = [C.c_void_p, C.c_uint32, C.POINTER(C.c_uint32),
                          C.c_char_p, C.c_size_t, C.POINTER(C.c_size_t)]
    get_entry.restype = C.c_int

    lookup_key = dll.crimson_missioninfo_lookup_string_key
    lookup_key.argtypes = [C.c_void_p, C.c_uint32,
                           C.c_char_p, C.c_size_t, C.POINTER(C.c_size_t)]
    lookup_key.restype = C.c_int

    # Load missioninfo.pabgb
    import crimson_rs
    pabgb = crimson_rs.extract_file(
        str(GAME_ROOT), "0008", "gamedata/binary__/client/bin", "missioninfo.pabgb")
    print(f"missioninfo.pabgb: {len(pabgb):,} bytes")
    buf = (C.c_ubyte * len(pabgb))(*pabgb)
    handle = C.c_void_p()
    rc = load(C.cast(buf, C.c_char_p), len(pabgb), C.byref(handle))
    if rc != 0:
        print(f"load failed rc={rc}")
        return 1
    print(f"loaded; handle={handle}")

    try:
        # Get entry count
        n = C.c_uint32(0)
        rc = count(handle, C.byref(n))
        print(f"entry count: {n.value}  (rc={rc})")

        # Build name -> key map by iterating GetEntry
        name_to_key = {}
        sa_names = []
        # First call to size the buffer
        for i in range(n.value):
            okey = C.c_uint32(0)
            req = C.c_size_t(0)
            rc1 = get_entry(handle, i, C.byref(okey), None, 0, C.byref(req))
            if rc1 not in (0, -11):  # 0=OK, -11=BUFFER_TOO_SMALL
                continue
            if req.value <= 1:
                # empty name — still add the key
                continue
            outbuf = (C.c_ubyte * req.value)()
            req2 = C.c_size_t(0)
            rc2 = get_entry(handle, i, C.byref(okey),
                            C.cast(outbuf, C.c_char_p), req.value, C.byref(req2))
            if rc2 != 0:
                continue
            name = bytes(outbuf[:req2.value - 1]).decode("utf-8", errors="replace")
            name_to_key[name] = okey.value
            if name.startswith("Challenge_SealedArtifact_Living_VII"):
                sa_names.append((name, okey.value))
            if i % 5000 == 0:
                print(f"  iter {i}/{n.value}")

        print(f"\nbuilt {len(name_to_key)} (name -> key) entries")
        print(f"\nAll Challenge_SealedArtifact_Living_VII* in bridge:")
        for name, key in sorted(sa_names):
            print(f"  {name:60s} -> {key}")

        # Check the exact lookup Pattern B v1 needs
        targets = [
            "Challenge_SealedArtifact_Living_VII",         # catalog itself
            "Challenge_SealedArtifact_Living_VII_2",       # X_2 follow-up
            "Challenge_SealedArtifact_Vehicle_II",         # Hooves II catalog (known-success)
            "Challenge_SealedArtifact_Vehicle_II_2",       # Hooves II X_2 (known-success)
        ]
        print(f"\nLookupMissionKeyByInternalName equivalents:")
        for t in targets:
            k = name_to_key.get(t)
            print(f"  {t:60s} -> {k!r}")

    finally:
        free(handle)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
