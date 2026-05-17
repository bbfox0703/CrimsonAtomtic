"""_proto_sa_item_mission_map.py - enumerate every Sealed_Abyss_Artifact_* item
in iteminfo.pabgb and dump (item_key, item_string_key, look_detail_mission_info)
so we can see which SA items reference mission key 1000913 (Feast for the
Road) — and whether ANY do. If none do, Feast for the Road is invisible
to the bulk sweep because no held-side mapping exists.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert")

# Catalog key of interest (Feast for the Road).
TARGET_MISSION = 1000913


def main():
    print(f"extracting iteminfo.pabgb")
    raw = crimson_rs.extract_file(
        str(GAME_ROOT), "0008", "gamedata/binary__/client/bin", "iteminfo.pabgb")
    print(f"  {len(raw):,} bytes")
    parsed = crimson_rs.parse_iteminfo_lossy(raw)
    items = parsed["items"]
    print(f"  {len(items)} items")

    sa_items = [it for it in items
                if isinstance(it.get("string_key"), str)
                and it["string_key"].startswith("Sealed_Abyss_Artifact_")]
    print(f"\nSA items: {len(sa_items)}")

    # The look_detail_mission_info field name is in the parsed dict. Let's first
    # discover the schema by checking a single item.
    if sa_items:
        sample = sa_items[0]
        print(f"\nSample SA item keys: {sorted(sample.keys())}")

    # Map (item_key -> mission_key)
    hits_for_target = []
    has_field = "look_detail_mission_info" in (sa_items[0] if sa_items else {})
    if not has_field:
        print("\n!! 'look_detail_mission_info' field not in parse output — checking alternates.")
        # Try other plausible field names.
        candidates = ["look_detail_mission", "mission_info_key", "challenge_key",
                      "look_detail_mission_info_key", "missionInfoKey"]
        for k in candidates:
            if sa_items and k in sa_items[0]:
                print(f"  Found alt: {k}")
                has_field = True
                break

    # Sample look_detail_mission_info values for the first 5 SA items.
    print("\n=== first 5 SA items: look_detail_mission_info ===")
    for it in sa_items[:5]:
        ik = it.get("key") or it.get("item_key")
        print(f"  key={ik}  string_key={it.get('string_key')!r}  "
              f"look_detail_mission_info={it.get('look_detail_mission_info')!r}")

    # Pull look_detail_mission_info from every SA item; tally which point at TARGET.
    mission_to_items: dict[int, list] = {}
    no_mission = 0
    for it in sa_items:
        mk = it.get("look_detail_mission_info")
        if mk is None or mk == 0:
            no_mission += 1
            continue
        ik = it.get("key") or it.get("item_key")
        mission_to_items.setdefault(mk, []).append((ik, it.get("string_key")))
    print(f"\nSA items with look_detail_mission_info: {len(sa_items) - no_mission}")
    print(f"SA items WITHOUT mission link: {no_mission}")
    print(f"Distinct mission keys referenced: {len(mission_to_items)}")

    if TARGET_MISSION in mission_to_items:
        print(f"\nTarget mission {TARGET_MISSION} is referenced by:")
        for ik, sk in mission_to_items[TARGET_MISSION]:
            print(f"  item_key={ik}  string_key={sk}")
    else:
        print(f"\n!! Target mission {TARGET_MISSION} NOT REFERENCED by any Sealed_Abyss_Artifact_* item.")
        # Find nearest hits
        keys = sorted(mission_to_items.keys())
        nearby = [k for k in keys if abs(k - TARGET_MISSION) < 200]
        if nearby:
            print("  Nearby referenced mission keys (within 200):")
            for k in nearby:
                items_here = mission_to_items[k]
                print(f"    {k}: {[(ik, sk) for ik, sk in items_here[:3]]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
