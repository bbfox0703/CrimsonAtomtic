"""_proto_feast_diff.py - probe the Feast for the Road SA challenge data shape
across slot102/105/106/107 to find what makes the partial-progress case
(progress 1/3) different from the 11 SA challenges the bulk sweep handles.

Catalog mission key: 1000913 (Feast for the Road / Lu Cheng De Wan Yan).
Expected partner-row keys per the 4-piece structure (status.md):
  - adjacent twin (negative key, idx N+1)
  - FAR tracker  (negative key = twin_key - 1, idx 3600-3900 range)
  - X_2 sub-mission (positive key, appended at list end)

Output: per slot, dump the catalog row + every MissionStateData whose key
matches the same series number range so we can eyeball field differences.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import crimson_rs  # noqa: E402

CATALOG_KEY = 1000913   # Feast for the Road
SLOTS = ["slot102", "slot105", "slot106", "slot107"]
OUT_DIR = Path("out/sa-feast-diff")


def find_mission_list(blocks):
    """Return (block_idx, list_field_dict, elements). Picks first QuestSaveData."""
    for bi, b in enumerate(blocks):
        if b["class_name"] != "QuestSaveData":
            continue
        for f in b["fields"]:
            if f.get("name") == "_missionStateList" and f.get("kind") == "object_list":
                return bi, f, f.get("elements") or []
    raise RuntimeError("no QuestSaveData._missionStateList found")


def get_scalar_value(elem, field_name):
    for f in elem.get("fields", []):
        if f.get("name") == field_name:
            if not f.get("present"):
                return None
            return f.get("value")
    return None


def get_field_kind(elem, field_name):
    for f in elem.get("fields", []):
        if f.get("name") == field_name:
            return f.get("kind")
    return None


def get_list_count(elem, field_name):
    for f in elem.get("fields", []):
        if f.get("name") == field_name:
            return f.get("count")
    return None


def parse_u32(value):
    """The Python binding emits scalar `value` as the int already
    (unlike the C ABI which produces "1000913 <u32>" strings)."""
    if value is None:
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        parts = value.rsplit(" ", 1)
        try:
            return int(parts[0])
        except (ValueError, IndexError):
            return None
    return None


def get_used_tag_list(elem):
    """_usedTagList is dynamic_array<u32>; decode the bytes."""
    for f in elem.get("fields", []):
        if f.get("name") == "_usedTagList":
            count = f.get("count") or 0
            if count == 0:
                return []
            raw = f.get("bytes")
            if isinstance(raw, (bytes, bytearray)) and len(raw) >= count * 4:
                tail = raw[-count * 4:]
                return [
                    int.from_bytes(tail[i*4:(i+1)*4], "little")
                    for i in range(count)
                ]
    return []


def dump_row(elem, label):
    key = parse_u32(get_scalar_value(elem, "_key"))
    out = {
        "label": label,
        "_key": key,
        "_key_hex": f"0x{key:08X}" if key is not None else None,
        "_state": parse_u32(get_scalar_value(elem, "_state")),
        "_completedTime": parse_u32(get_scalar_value(elem, "_completedTime")),
        "_completedTime_present": get_scalar_value(elem, "_completedTime") is not None,
        "_branchedTime": parse_u32(get_scalar_value(elem, "_branchedTime")),
        "_branchedTime_present": get_scalar_value(elem, "_branchedTime") is not None,
        "_uiState": parse_u32(get_scalar_value(elem, "_uiState")),
        "_newAlarm": parse_u32(get_scalar_value(elem, "_newAlarm")),
        "_progressIndex": parse_u32(get_scalar_value(elem, "_progressIndex")),
        "_subMissionStateList_count": get_list_count(elem, "_subMissionStateList"),
        "_usedTagList": get_used_tag_list(elem),
        "data_size": elem.get("data_size"),
    }
    known = {"_key", "_state", "_completedTime", "_branchedTime", "_uiState",
             "_newAlarm", "_progressIndex", "_subMissionStateList", "_usedTagList"}
    extra = {}
    for f in elem.get("fields", []):
        name = f.get("name")
        if name in known or not f.get("present"):
            continue
        if f.get("kind") in ("fixed_prefix", "fixed_suffix"):
            extra[name] = f.get("value")
        elif f.get("kind") == "dynamic_array":
            extra[name] = f"<dynarr count={f.get('count')} bytes={len(f.get('bytes') or b'')}>"
        elif f.get("kind") == "object_list":
            extra[name] = f"<objlist count={f.get('count')}>"
        elif f.get("kind") == "object_locator":
            extra[name] = "<object_locator>"
        else:
            extra[name] = f"<{f.get('kind')}>"
    if extra:
        out["other_present_fields"] = extra
    return out


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    summary = {}
    for slot in SLOTS:
        body_path = OUT_DIR / f"{slot}.bin"
        if not body_path.exists():
            print(f"missing {body_path}", file=sys.stderr)
            continue
        body = body_path.read_bytes()
        parsed = crimson_rs.decode_save_body_blocks(body)
        blocks = parsed["blocks"]
        bi, list_field, elements = find_mission_list(blocks)

        cat_idx = None
        cat_row = None
        for i, e in enumerate(elements):
            k = parse_u32(get_scalar_value(e, "_key"))
            if k == CATALOG_KEY:
                cat_idx = i
                cat_row = e
                break
        if cat_row is None:
            print(f"[{slot}] catalog key {CATALOG_KEY} not found in {len(elements)} rows", file=sys.stderr)
            summary[slot] = None
            continue

        rows = [{"idx": cat_idx, **dump_row(cat_row, "CATALOG")}]
        twin = elements[cat_idx + 1] if cat_idx + 1 < len(elements) else None
        twin_key = parse_u32(get_scalar_value(twin, "_key")) if twin else None
        if twin and twin_key and twin_key > 0x80000000:
            rows.append({"idx": cat_idx + 1, **dump_row(twin, "ADJACENT_TWIN")})
            far_key = twin_key - 1
            far_idx = None
            for j, e in enumerate(elements):
                if parse_u32(get_scalar_value(e, "_key")) == far_key:
                    far_idx = j
                    rows.append({"idx": j, **dump_row(e, f"FAR_TRACKER (key=0x{far_key:08X})")})
                    break
            if far_idx is None:
                rows.append({"idx": None, "label": f"FAR_TRACKER (key=0x{far_key:08X}) NOT FOUND"})
        else:
            rows.append({"idx": cat_idx + 1, "label": f"NO_TWIN (next row key={twin_key})"})

        # X_2 follow-up: scan the tail for positive-key entries appended after the FAR-tracker range.
        # The X_2 key is engine-assigned; just dump the last ~30 rows for visual inspection.
        tail_rows = []
        for j in range(max(0, len(elements) - 30), len(elements)):
            k = parse_u32(get_scalar_value(elements[j], "_key"))
            tail_rows.append({"idx": j, "_key": k, "_key_hex": f"0x{k:08X}" if k else None})

        rows.append({"summary": f"total_elements={len(elements)}, catalog at idx {cat_idx}",
                     "tail_30_keys": tail_rows})

        summary[slot] = rows
        out_path = OUT_DIR / f"feast_{slot}.json"
        with out_path.open("w", encoding="utf-8") as f:
            json.dump(rows, f, indent=2, ensure_ascii=False)
        print(f"wrote {out_path}")

    summary_path = OUT_DIR / "feast_summary.json"
    with summary_path.open("w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, ensure_ascii=False)
    print(f"wrote {summary_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
