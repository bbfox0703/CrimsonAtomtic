"""_proto_all_sa_far.py - dump every SA-shape (catalog + adjacent neg-twin
+ FAR tracker) trio in slot102. Compare Feast for the Road (1000913)
with all other SA candidates to find what makes its FAR shape unique.

Output a tidy table per catalog row so we can eyeball the differences.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs

BODY = Path("out/sa-feast-diff/slot102.bin")
TARGET_KEY = 1000913


def field_value(elem, name):
    for f in elem.get("fields", []):
        if f.get("name") == name:
            return (f.get("present"), f.get("value"), f.get("kind"))
    return (None, None, None)


def used_tags(elem):
    for f in elem.get("fields", []):
        if f.get("name") == "_usedTagList":
            count = f.get("count") or 0
            if count == 0:
                return []
            raw = f.get("bytes")
            if isinstance(raw, (bytes, bytearray)) and len(raw) >= count * 4:
                tail = raw[-count * 4:]
                return [int.from_bytes(tail[i*4:(i+1)*4], "little") for i in range(count)]
    return []


def all_present_extra(elem, exclude):
    out = {}
    for f in elem.get("fields", []):
        if not f.get("present") or f.get("name") in exclude:
            continue
        if f.get("kind") in ("fixed_prefix", "fixed_suffix"):
            out[f["name"]] = f.get("value")
    return out


def summarise(elem, label, idx):
    return {
        "label": label,
        "idx": idx,
        "_key": field_value(elem, "_key")[1],
        "_state": field_value(elem, "_state")[1],
        "_completedTime_present": field_value(elem, "_completedTime")[0],
        "_completedTime": field_value(elem, "_completedTime")[1],
        "_branchedTime_present": field_value(elem, "_branchedTime")[0],
        "_branchedTime": field_value(elem, "_branchedTime")[1],
        "_uiState": field_value(elem, "_uiState")[1],
        "_newAlarm": field_value(elem, "_newAlarm")[1],
        "_completeCount": field_value(elem, "_completeCount")[1],
        "_completeCount_present": field_value(elem, "_completeCount")[0],
        "_progressIndex": field_value(elem, "_progressIndex")[1],
        "tags": used_tags(elem),
        "extras": all_present_extra(elem, {"_key", "_state", "_completedTime",
                                            "_branchedTime", "_uiState", "_newAlarm",
                                            "_completeCount", "_progressIndex"}),
    }


def main():
    body = BODY.read_bytes()
    parsed = crimson_rs.decode_save_body_blocks(body)
    elements = None
    for b in parsed["blocks"]:
        if b["class_name"] != "QuestSaveData":
            continue
        for f in b["fields"]:
            if f.get("name") == "_missionStateList":
                elements = f.get("elements") or []
                break
        if elements is not None:
            break
    if elements is None:
        print("no missionStateList", file=sys.stderr)
        return 1

    # Find every catalog where idx+1 is a negative-key twin with state=5
    # and completedTime present (the "artifact picked up" marker).
    print(f"total elements: {len(elements)}")
    catalogs = []
    for i in range(len(elements) - 1):
        cat = elements[i]
        twin = elements[i + 1]
        if cat.get("class_name") != "MissionStateData":
            continue
        if twin.get("class_name") != "MissionStateData":
            continue
        cat_key = field_value(cat, "_key")[1]
        twin_key = field_value(twin, "_key")[1]
        if not (isinstance(cat_key, int) and isinstance(twin_key, int)):
            continue
        if not (0 < cat_key < 0x80000000 and twin_key >= 0xFFFF0000):
            continue
        # Twin must be state=5 + _completedTime present (the SA pickup marker)
        if field_value(twin, "_state")[1] != 5:
            continue
        if not field_value(twin, "_completedTime")[0]:
            continue
        # Find FAR tracker (key = twin_key - 1)
        far_key = twin_key - 1
        far_idx = None
        for j, e in enumerate(elements):
            if field_value(e, "_key")[1] == far_key:
                far_idx = j
                break
        far = elements[far_idx] if far_idx is not None else None
        catalogs.append({
            "catalog": summarise(cat, "CAT", i),
            "twin": summarise(twin, "TWIN", i + 1),
            "far": summarise(far, "FAR", far_idx) if far else None,
        })

    print(f"\nSA-shape candidates: {len(catalogs)}")

    # Print a compact comparison table for FAR shapes.
    print("\nFAR tracker comparison:")
    headers = ["catalog_key", "FAR _state", "FAR ct_pres", "FAR bt_pres", "FAR _completeCount",
               "FAR tags_n", "FAR _progressIdx", "FAR extras"]
    print("  " + " | ".join(f"{h:<16}" for h in headers))
    print("  " + "-" * (18 * len(headers)))
    feast_row = None
    for c in catalogs:
        far = c["far"]
        row = [
            str(c["catalog"]["_key"]),
            str(far["_state"]) if far else "-",
            str(far["_completedTime_present"]) if far else "-",
            str(far["_branchedTime_present"]) if far else "-",
            (str(far["_completeCount"]) if far["_completeCount_present"] else "absent") if far else "-",
            str(len(far["tags"])) if far else "-",
            str(far["_progressIndex"]) if far else "-",
            str(far["extras"]) if far else "-",
        ]
        is_feast = c["catalog"]["_key"] == TARGET_KEY
        marker = "<-- FEAST" if is_feast else ""
        print("  " + " | ".join(f"{x:<16}" for x in row) + marker)
        if is_feast:
            feast_row = c

    # Dump full Feast trio + the first non-Feast trio for side-by-side
    out_path = Path("out/sa-feast-diff/all_sa_far_summary.json")
    out_path.write_text(json.dumps(catalogs, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\nwrote {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
