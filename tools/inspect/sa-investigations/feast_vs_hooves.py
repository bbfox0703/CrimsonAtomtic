"""_proto_feast_vs_hooves.py - dump every present field of Feast for the
Road (catalog 1000913) FAR tracker side-by-side with Hooves II (catalog
1000898, which Pattern B v1 successfully applied) FAR tracker. Show
schema indices + sizes so we can spot any structural difference.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

BODY = Path("out/sa-feast-diff/slot102.bin")


def field_value(elem, name):
    for f in elem.get("fields", []):
        if f.get("name") == name:
            return f
    return None


def find_far(elements, catalog_key):
    for i, e in enumerate(elements):
        if e.get("class_name") != "MissionStateData":
            continue
        if field_value(e, "_key").get("value") != catalog_key:
            continue
        # Twin at i+1
        if i + 1 >= len(elements):
            return None
        twin = elements[i + 1]
        twin_key = field_value(twin, "_key").get("value")
        if not isinstance(twin_key, int) or twin_key < 0xFFFF0000:
            return None
        far_key = twin_key - 1
        # FAR somewhere
        for j, ff in enumerate(elements):
            if field_value(ff, "_key").get("value") == far_key:
                return {"cat_idx": i, "twin_idx": i + 1, "far_idx": j,
                        "cat": e, "twin": twin, "far": ff,
                        "twin_key_hex": f"0x{twin_key:08X}",
                        "far_key_hex": f"0x{far_key:08X}"}
        return None


def dump_full_fields(elem, label):
    print(f"\n=== {label} ===")
    print(f"  class={elem.get('class_name')}  data_size={elem.get('data_size')}  "
          f"mask={elem.get('mask_bytes').hex() if isinstance(elem.get('mask_bytes'), (bytes, bytearray)) else elem.get('mask_bytes')}")
    for f in elem.get("fields", []):
        name = f.get("name")
        present = f.get("present")
        kind = f.get("kind")
        idx = f.get("field_index")
        if kind in ("fixed_prefix", "fixed_suffix") and present:
            print(f"  [{idx:2}] {name:<32} present  {kind:<14}  value={f.get('value')}")
        elif kind in ("fixed_prefix", "fixed_suffix"):
            print(f"  [{idx:2}] {name:<32} absent   {kind:<14}")
        elif kind == "dynamic_array":
            count = f.get("count") or 0
            blen = len(f.get("bytes") or b"")
            print(f"  [{idx:2}] {name:<32} present  dynamic_array count={count} bytes={blen}")
        elif kind == "object_list":
            print(f"  [{idx:2}] {name:<32} present  object_list count={f.get('count')}")
        elif kind == "absent":
            print(f"  [{idx:2}] {name:<32} absent")
        else:
            print(f"  [{idx:2}] {name:<32} {kind} present={present}")


def main():
    body = BODY.read_bytes()
    parsed = crimson_rs.decode_save_body_blocks(body)
    elements = None
    for b in parsed["blocks"]:
        if b["class_name"] == "QuestSaveData":
            for f in b["fields"]:
                if f.get("name") == "_missionStateList":
                    elements = f.get("elements") or []
                    break
            if elements:
                break

    print(f"total elements: {len(elements)}")
    feast = find_far(elements, 1000913)
    hooves = find_far(elements, 1000898)

    if not feast:
        print("Feast not found")
        return 1
    if not hooves:
        print("Hooves II not found")
        return 1

    print(f"\nFeast for the Road (1000913): cat_idx={feast['cat_idx']}  twin_idx={feast['twin_idx']} twin_key={feast['twin_key_hex']}  far_idx={feast['far_idx']} far_key={feast['far_key_hex']}")
    print(f"Hooves II (1000898):           cat_idx={hooves['cat_idx']}  twin_idx={hooves['twin_idx']} twin_key={hooves['twin_key_hex']}  far_idx={hooves['far_idx']} far_key={hooves['far_key_hex']}")

    dump_full_fields(feast["cat"], "FEAST CATALOG (1000913)")
    dump_full_fields(hooves["cat"], "HOOVES II CATALOG (1000898)")
    dump_full_fields(feast["twin"], "FEAST TWIN")
    dump_full_fields(hooves["twin"], "HOOVES II TWIN")
    dump_full_fields(feast["far"], "FEAST FAR")
    dump_full_fields(hooves["far"], "HOOVES II FAR")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
