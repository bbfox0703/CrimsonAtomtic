"""dump_challenges.py — enumerate every `Challenge_*` row from missioninfo, with save-side state.

Phase A.1 of the Browse Challenges feature: produce the authoritative
challenge catalog from the game's own data, plus (optionally) the
completion state for each one in a given save file.

Workflow:

1. Extract `missioninfo.pabgb` + `localizationstring_eng.paloc` from a
   live Crimson Desert install via PyO3 `crimson_rs.extract_file`.
2. Load both through the c_abi bridges via ctypes (Python bindings
   don't expose the new bridges yet — see issue #2 in
   `out/crimson-rs-issues/`).
3. Walk every entry via `crimson_missioninfo_get_entry`. Keep rows
   whose `internal_name` starts with `Challenge_`. Filter out U+FFFD
   rows (the issue-#1 mojibake bug — none of those are real challenges
   anyway).
4. For each challenge, hash-hop via `lookup_display_name(lo32=0x101)`
   to get the English title.
5. If `--save` is provided, parse the save and walk every
   `MissionStateData` block: for each challenge key found, record its
   `_state` value (and whether `_completedTime` is present — important
   for the "one-click complete" feature design, since promoting that
   absent field needs PR B / length-changing edits).
6. Emit JSONL — one challenge per line — with category derived from
   the internal-name structure.

Row shape:
    {
      "key": 1002173,
      "internal_name": "Mission_MiniGame_Duel_Unarmed_0",
      "title": "Win once in an unarmed duel",
      "category_path": ["MiniGame", "Duel", "Unarmed"],
      "tier": null,
      "save_state": 5,                  // null if --save omitted or key absent from save
      "save_has_completed_time": true,  // null if key absent
    }

NOTE: `internal_name` may NOT start with `Challenge_` exactly — IGN
groups some entries as "Challenges" that PA's data labels as
`Mission_MiniGame_*` (e.g. arm-wrestling, shooting contest, duels).
The user's screenshots confirmed both `Mission_MiniGame_*` and
`Challenge_*` rows appear in the editor's challenges list. Default
prefix filter is `Challenge_,Mission_MiniGame_` — override with
`--prefix` if needed.

Versions tested: 1.06.
"""

from __future__ import annotations

import argparse
import ctypes
import difflib
import json
import re
import sys
from collections import Counter
from ctypes import (
    c_char_p, c_int, c_size_t, c_uint32, c_void_p, POINTER, byref,
)
from pathlib import Path
from typing import Any, TextIO


# ── IGN HTML parsing ─────────────────────────────────────────────────────────


_ROMAN_TO_INT = {
    "I": 1, "II": 2, "III": 3, "IV": 4, "V": 5,
    "VI": 6, "VII": 7, "VIII": 8, "IX": 9, "X": 10,
}


def _normalise_title(s: str) -> str:
    """Title comparison key: lowercase + Roman→Arabic suffix + collapsed
    whitespace + apostrophe normalised. IGN ships titles like
    `"Sword of Trials 1"` (Arabic) while the bridge ships
    `"Sword of Trials I"` (Roman). Convert both to the same shape."""
    s = s.strip().lower()
    s = s.replace("’", "'")        # right-single-quote → ASCII apostrophe
    s = re.sub(r"\s+", " ", s)
    # Trailing Roman numeral → Arabic
    parts = s.rsplit(" ", 1)
    if len(parts) == 2:
        suffix = parts[1].upper()
        if suffix in _ROMAN_TO_INT:
            s = f"{parts[0]} {_ROMAN_TO_INT[suffix]}"
    return s


def parse_ign_challenges_text(text_path: Path) -> list[dict]:
    """Parse the user-pasted IGN text dump.

    Each logical row in the page table is 4 tab-separated cells
    (`name`, `goal`, `reward`, `completed`). When IGN displays multi-
    bullet `goal` or `reward` values, the cell wraps across multiple
    physical lines — the tab separator that follows that cell sits on
    a later physical line. After the row's `reward` cell, there's a
    BLANK line, then the `completed` cell's value (just the row's name
    again) on its own line, then the next row.

    Parsing strategy: build one logical row's text buffer line-by-line,
    flush on blank line, then expect the next non-blank line to be the
    completed-cell echo (skipped). Categories and subcategories are
    no-tab lines at the same level — distinguished by their literal
    suffix ('List and Rewards' = category).

    Returns rows shaped like:
        {category, subcategory, name, name_key, goal, reward}
    """
    rows: list[dict] = []
    category = ""
    subcategory = ""
    buf: str | None = None     # currently-collecting logical row text
    last_name: str | None = None  # last completed row's name (for echo skip)
    expecting_echo = False

    def flush() -> None:
        nonlocal buf, last_name, expecting_echo
        if buf is None:
            return
        i1 = buf.find("\t")
        if i1 == -1:
            # No tabs at all — not a data row, just discard.
            buf = None
            return
        name = buf[:i1].strip()
        rest = buf[i1 + 1 :]
        # IGN's text sometimes drops the goal/reward separator tab when
        # the source HTML uses a different layout for that row (observed
        # on "Under the Ashen Banner Pike" — only 1 tab in the row). For
        # those, surface everything as `goal` and leave `reward` empty.
        # The match-by-name still works for cross-referencing the bridge.
        i2 = rest.find("\t")
        if i2 == -1:
            goal = rest.strip()
            reward = ""
        else:
            goal = rest[:i2].strip()
            reward = rest[i2 + 1 :].split("\t")[0].strip()
        rows.append({
            "category": category,
            "subcategory": subcategory,
            "name": name,
            "name_key": _normalise_title(name),
            "goal": goal,
            "reward": reward,
        })
        last_name = name
        expecting_echo = True
        buf = None

    for raw_line in text_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.rstrip()
        if not line.strip():
            # Blank line — flush any in-progress row, then expect echo next.
            flush()
            continue

        # If we just flushed a row, the next non-blank line should be the
        # completed-cell echo (just the row's name again). Skip and move
        # on; if it doesn't match the name, fall through and process
        # normally (defensive — shouldn't happen in well-formed input).
        if expecting_echo:
            # IGN's "Completed" column has minor typos and capitalisation
            # drift compared to the row name (e.g. row "Crystals in the
            # Veins" → echo "Crystal in the Veins"; row "Well-Informed
            # Greymane" → echo "Well-Informed Greyman"; row "Bow Aimed
            # At Fate" → echo "Bow Aimed at Fate"). Use a fuzzy ratio so
            # the typos don't get re-classified as subcategory headers.
            # 0.85 thresh tolerates a missing trailing char ('s'/'e') but
            # still rejects real subcategory headers like "Goyen's Advice
            # - Two-Handed Weapon".
            expecting_echo = False
            if last_name is not None:
                ratio = difflib.SequenceMatcher(
                    None, line.strip().casefold(), last_name.casefold()
                ).ratio()
                if ratio >= 0.85:
                    continue

        # Currently collecting a multi-line row? Append + continue.
        if buf is not None:
            buf += "\n" + line
            continue

        # Top-level category heading.
        if line.endswith("List and Rewards"):
            category = line.replace(" List and Rewards", "").strip()
            subcategory = ""
            continue

        # Column-header row.
        if "\t" in line and line.split("\t", 1)[0].strip().lower() == "challenge":
            continue

        # Data row start.
        if "\t" in line:
            buf = line
            continue

        # No tabs and not a category line — subcategory header.
        subcategory = line.strip()

    # Flush trailing row, if any (no trailing blank line in pasted text).
    flush()
    return rows

# PALOC stores some titles as template references of the form
#   {StaticInfo:Knowledge:Knowledge_Node_Abyssone_0001#Ethereal Pathway}
# The "real" display string is the bit after `#` — the game's UI layer
# expands the {StaticInfo:…} reference, but our editor doesn't have a
# template expander yet (separate upstream feature). Strip the wrapper
# locally so the catalog reads cleanly. ~4% of challenges have this
# shape in 1.06.
_TEMPLATE_TITLE_RE = re.compile(r"^\{[^}#]*#([^}]+)\}$")


def clean_title(title: str | None) -> str | None:
    if not title:
        return title
    m = _TEMPLATE_TITLE_RE.match(title)
    return m.group(1) if m else title

# Force UTF-8 stdout so CJK + U+FFFD don't trip cp950 on Windows.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from common.cli import require_args  # noqa: E402
from common.paths import discover_installs  # noqa: E402

DLL_PATH = r"D:\Github\CrimsonAtomtic\vendor\crimson-rs\target\release\crimson_rs.dll"
ITEMINFO_DIR = "gamedata/binary__/client/bin"
PALOC_DIR = "gamedata/stringtable/binary__"

DEFAULT_PREFIXES = "Challenge_,Mission_MiniGame_"

OK = 0
NOT_FOUND = -16
BUFFER_TOO_SMALL = -11
OUT_OF_RANGE = -10


# ── c_abi bindings (ctypes) ──────────────────────────────────────────────────

_dll = ctypes.CDLL(DLL_PATH)


def _bind(name: str, argtypes, restype):
    fn = getattr(_dll, name)
    fn.argtypes = argtypes
    fn.restype = restype
    return fn


mi_load_bytes = _bind("crimson_missioninfo_load_from_bytes",
                      [c_char_p, c_size_t, POINTER(c_void_p)], c_int)
mi_free = _bind("crimson_missioninfo_free", [c_void_p], None)
mi_count = _bind("crimson_missioninfo_entry_count",
                 [c_void_p, POINTER(c_uint32)], c_int)
mi_get_entry = _bind("crimson_missioninfo_get_entry",
                     [c_void_p, c_uint32, POINTER(c_uint32),
                      c_char_p, c_size_t, POINTER(c_size_t)], c_int)
mi_lookup_dn = _bind("crimson_missioninfo_lookup_display_name",
                     [c_void_p, c_void_p, c_uint32, c_uint32,
                      c_char_p, c_size_t, POINTER(c_size_t)], c_int)

pl_load_bytes = _bind("crimson_paloc_load_from_bytes",
                      [c_char_p, c_size_t, POINTER(c_void_p)], c_int)
pl_free = _bind("crimson_paloc_free", [c_void_p], None)


def _two_call(call) -> tuple[int, bytes]:
    """Standard two-call buffer dance for any (buf, buf_len, *required) getter."""
    required = c_size_t(0)
    rc = call(None, 0, byref(required))
    if rc == NOT_FOUND or rc == OUT_OF_RANGE:
        return rc, b""
    if rc not in (OK, BUFFER_TOO_SMALL):
        return rc, b""
    if required.value <= 1:
        return OK, b""
    buf = ctypes.create_string_buffer(required.value)
    rc2 = call(buf, required.value, byref(required))
    if rc2 != OK:
        return rc2, b""
    return OK, bytes(buf.raw[: required.value - 1])  # strip NUL


def get_entry(handle, idx: int) -> tuple[int, int, bytes]:
    """Returns (rc, key, name_bytes). key undefined when rc != OK."""
    out_key = c_uint32(0)
    required = c_size_t(0)
    rc = mi_get_entry(handle, idx, byref(out_key), None, 0, byref(required))
    if rc == OUT_OF_RANGE:
        return rc, 0, b""
    if rc not in (OK, BUFFER_TOO_SMALL):
        return rc, 0, b""
    if required.value <= 1:
        return OK, int(out_key.value), b""
    buf = ctypes.create_string_buffer(required.value)
    rc2 = mi_get_entry(handle, idx, byref(out_key), buf, required.value, byref(required))
    if rc2 != OK:
        return rc2, 0, b""
    return OK, int(out_key.value), bytes(buf.raw[: required.value - 1])


# ── Category derivation ──────────────────────────────────────────────────────


def split_internal_name(name: str) -> tuple[list[str], str | None]:
    """Derive `(category_path, tier)` from an internal name.

    Example: `Challenge_SealedArtifact_Mastery_OneHandSword_I`
        → category_path = ["SealedArtifact", "Mastery", "OneHandSword"]
          tier          = "I"

    The leading `Challenge` / `Mission` segment is dropped (that's the
    prefix filter). Trailing single roman numerals are stripped off as
    the tier; everything in between is the category breadcrumb.
    """
    parts = name.split("_")
    if not parts:
        return [], None
    # Drop the leading "Challenge" / "Mission" sentinel.
    parts = parts[1:]
    # Trailing tier: "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"
    # — plus pure-digit suffixes like "_0" / "_1" that some Mission rows use.
    tier: str | None = None
    if parts:
        last = parts[-1]
        is_roman = last and all(c in "IVX" for c in last) and last
        is_digit = last.isdigit()
        if is_roman or is_digit:
            tier = last
            parts = parts[:-1]
    return parts, tier


# ── Save walking ─────────────────────────────────────────────────────────────


def walk_save_mission_states(save_path: Path) -> dict[int, dict]:
    """For each MissionStateData block in the save, record:
        { mission_key: { state, has_completed_time, completed_time, ui_state } }

    Walks via PyO3 `crimson_rs.decode_save_body_blocks` because the
    save's MissionStateData lives inside QuestSaveData's
    _missionStateList object_list.
    """
    import crimson_rs  # type: ignore[import-not-found]

    parsed = crimson_rs.parse_save_from_file(str(save_path))
    decoded = crimson_rs.decode_save_body_blocks(parsed["body"])

    out: dict[int, dict] = {}
    for block in decoded["blocks"]:
        if block.get("class_name") != "QuestSaveData":
            continue
        for f in block.get("fields", []):
            if f.get("name") != "_missionStateList" or f.get("kind") != "object_list":
                continue
            for elem in f.get("elements") or []:
                if not isinstance(elem, dict):
                    continue
                key = None
                state = None
                completed_time = None
                has_completed_time = False
                ui_state = None
                for ef in elem.get("fields", []):
                    if not ef.get("present"):
                        if ef.get("name") == "_completedTime":
                            has_completed_time = False
                        continue
                    name = ef.get("name")
                    val = ef.get("value")
                    if name == "_key":
                        key = val
                    elif name == "_state":
                        state = val
                    elif name == "_completedTime":
                        has_completed_time = True
                        completed_time = val
                    elif name == "_uiState":
                        ui_state = val
                if isinstance(key, int):
                    out[key] = {
                        "state": state,
                        "has_completed_time": has_completed_time,
                        "completed_time": completed_time,
                        "ui_state": ui_state,
                    }
    return out


# ── Main ─────────────────────────────────────────────────────────────────────


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="dump_challenges.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("--game-dir", type=Path, default=None,
                   help="path to a Crimson Desert install. Default: auto-discover.")
    p.add_argument("--save", type=Path, default=None,
                   help="path to a .save file. When provided, each row gets save_state + "
                        "save_has_completed_time columns.")
    p.add_argument("--out", type=Path, default=None,
                   help="output JSONL path. Default: stdout.")
    p.add_argument("--prefix", type=str, default=DEFAULT_PREFIXES,
                   help=f"comma-separated internal_name prefixes to include. "
                        f"Default: {DEFAULT_PREFIXES}.")
    p.add_argument("--ign-text", type=Path, default=None,
                   help="path to a plain-text dump of the IGN "
                        "\"Challenges List and Rewards\" page (paste the "
                        "table region from the page into a .txt file). "
                        "Enriches each row with IGN category / subcategory / "
                        "goal / reward when the challenge title matches.")
    p.add_argument("--summary", action="store_true",
                   help="print a category breakdown + save-state histogram to stderr after dumping.")
    return p


def main() -> int:
    parser = build_parser()
    require_args(parser)
    args = parser.parse_args()

    try:
        import crimson_rs  # type: ignore[import-not-found, unused-ignore]  # noqa: F401
    except ImportError as e:
        print(f"crimson_rs not importable: {e}", file=sys.stderr)
        return 1

    # Resolve the install
    game_dir = args.game_dir
    if game_dir is None:
        installs = discover_installs()
        game_dir = installs[0] if installs else None
    if game_dir is None or not Path(game_dir).is_dir():
        print(f"no game install found at {game_dir}", file=sys.stderr)
        return 1
    print(f"game-dir: {game_dir}", file=sys.stderr)

    # Extract source files
    print("extracting missioninfo + paloc(eng)…", file=sys.stderr)
    missioninfo = crimson_rs.extract_file(
        str(game_dir), "0008", ITEMINFO_DIR, "missioninfo.pabgb")
    paloc = crimson_rs.extract_file(
        str(game_dir), "0020", PALOC_DIR, "localizationstring_eng.paloc")

    # Load bridges
    h_mi = c_void_p()
    h_pl = c_void_p()
    rc = mi_load_bytes(missioninfo, len(missioninfo), byref(h_mi))
    if rc != OK:
        print(f"missioninfo load failed rc={rc}", file=sys.stderr)
        return 1
    rc = pl_load_bytes(paloc, len(paloc), byref(h_pl))
    if rc != OK:
        print(f"paloc load failed rc={rc}", file=sys.stderr)
        mi_free(h_mi)
        return 1

    # Walk save (optional)
    save_states: dict[int, dict] = {}
    if args.save is not None:
        if not args.save.is_file():
            print(f"--save not a file: {args.save}", file=sys.stderr)
            return 1
        print(f"walking save: {args.save}", file=sys.stderr)
        save_states = walk_save_mission_states(args.save)
        print(f"  {len(save_states):,} MissionStateData entries found in save",
              file=sys.stderr)

    # Load IGN enrichment (optional)
    ign_by_key: dict[str, dict] = {}
    ign_rows: list[dict] = []
    if args.ign_text is not None:
        if not args.ign_text.is_file():
            print(f"--ign-text not a file: {args.ign_text}", file=sys.stderr)
            return 1
        print(f"parsing IGN text: {args.ign_text}", file=sys.stderr)
        ign_rows = parse_ign_challenges_text(args.ign_text)
        for r in ign_rows:
            ign_by_key[r["name_key"]] = r
        print(f"  parsed {len(ign_rows):,} IGN rows "
              f"({len(ign_by_key):,} unique normalised titles)",
              file=sys.stderr)

    # Enumerate every challenge / mini-game mission
    prefixes = tuple(p.strip() for p in args.prefix.split(",") if p.strip())
    print(f"enumerating missioninfo entries with prefixes: {prefixes}", file=sys.stderr)

    n = c_uint32(0)
    mi_count(h_mi, byref(n))
    total = int(n.value)
    print(f"  total missioninfo rows: {total:,}", file=sys.stderr)

    rows: list[dict] = []
    seen_keys: set[int] = set()
    mojibake_skipped = 0
    not_matched_prefix = 0
    for idx in range(total):
        rc, key, raw = get_entry(h_mi, idx)
        if rc != OK or not raw:
            continue
        # Skip the issue-#1 mojibake rows so they don't pollute the catalog.
        if b"\xef\xbf\xbd" in raw:
            mojibake_skipped += 1
            continue
        try:
            name = raw.decode("utf-8")
        except UnicodeDecodeError:
            mojibake_skipped += 1
            continue
        if not name.startswith(prefixes):
            not_matched_prefix += 1
            continue
        # Dedupe by key (mostly a defensive measure — see issue #1's
        # finding that the bridge can return duplicate keys for some
        # mojibake rows; well-formed rows shouldn't dupe but we guard
        # anyway).
        if key in seen_keys:
            continue
        seen_keys.add(key)

        # Hash-hop to localized title.
        lo32 = 0x101  # individual challenge / mission title
        rc_dn, dn_bytes = _two_call(
            lambda buf, blen, req: mi_lookup_dn(h_mi, h_pl, key, lo32, buf, blen, req))
        title_raw = dn_bytes.decode("utf-8", errors="replace") if rc_dn == OK else None
        title = clean_title(title_raw)

        category_path, tier = split_internal_name(name)

        row: dict[str, Any] = {
            "key": key,
            "internal_name": name,
            "title": title,
            "title_raw": title_raw if title_raw != title else None,
            "category_path": category_path,
            "tier": tier,
        }
        if args.save is not None:
            s = save_states.get(key)
            if s is None:
                row["save_state"] = None
                row["save_has_completed_time"] = None
                row["save_completed_time"] = None
            else:
                row["save_state"] = s["state"]
                row["save_has_completed_time"] = s["has_completed_time"]
                row["save_completed_time"] = s["completed_time"]
        if args.ign_text is not None and title:
            ign = ign_by_key.get(_normalise_title(title))
            if ign is not None:
                row["ign_category"] = ign["category"]
                row["ign_subcategory"] = ign["subcategory"]
                row["ign_goal"] = ign["goal"]
                row["ign_reward"] = ign["reward"]
        rows.append(row)

    print(f"  matched: {len(rows):,} challenge rows", file=sys.stderr)
    print(f"  skipped mojibake rows: {mojibake_skipped:,}", file=sys.stderr)
    print(f"  skipped non-matching prefix: {not_matched_prefix:,}", file=sys.stderr)

    # Emit
    if args.out is not None:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        sink: TextIO = args.out.open("w", encoding="utf-8")
        close_after = True
    else:
        sink = sys.stdout
        close_after = False
    try:
        # Stable order: category_path lexicographic, then tier, then key.
        rows.sort(key=lambda r: (tuple(r["category_path"]), r["tier"] or "", r["key"]))
        for r in rows:
            sink.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")))
            sink.write("\n")
    finally:
        if close_after:
            sink.close()

    if args.out is not None:
        print(f"wrote {args.out}", file=sys.stderr)

    # Summary
    if args.summary:
        print("\n=== Category breakdown ===", file=sys.stderr)
        cats = Counter(tuple(r["category_path"][:2]) for r in rows)
        for cat, count in cats.most_common():
            print(f"  {count:>4}  {' / '.join(cat) if cat else '<root>'}",
                  file=sys.stderr)

        if args.ign_text is not None:
            print("\n=== IGN enrichment coverage ===", file=sys.stderr)
            matched = sum(1 for r in rows if "ign_category" in r)
            bridge_titles = {_normalise_title(r["title"]) for r in rows if r.get("title")}
            ign_only = [r for r in ign_rows if r["name_key"] not in bridge_titles]
            print(f"  bridge ↔ IGN matched : {matched:,} of {len(ign_rows):,} IGN rows "
                  f"({100.0 * matched / len(ign_rows) if ign_rows else 0:.1f}%)",
                  file=sys.stderr)
            print(f"  IGN-only (no bridge match): {len(ign_only):,}", file=sys.stderr)
            if ign_only:
                print("  sample IGN-only entries (no bridge MissionKey — Combat/Life "
                      "category in IGN may use a different namespace, OR victim of "
                      "issue #1 mojibake):", file=sys.stderr)
                for r in ign_only[:10]:
                    print(f"    [{r['category']} / {r['subcategory']}] \"{r['name']}\"",
                          file=sys.stderr)

        if args.save is not None:
            print("\n=== Save-state histogram (challenges only) ===", file=sys.stderr)
            state_counts: Counter = Counter()
            absent_count = 0
            has_completed_time = 0
            for r in rows:
                s = r["save_state"]
                if s is None:
                    absent_count += 1
                else:
                    state_counts[s] += 1
                if r["save_has_completed_time"] is True:
                    has_completed_time += 1
            for state, count in sorted(state_counts.items()):
                print(f"  _state={state}: {count:,}", file=sys.stderr)
            print(f"  not in save:  {absent_count:,}", file=sys.stderr)
            print(f"  _completedTime present: {has_completed_time:,}",
                  file=sys.stderr)

    mi_free(h_mi)
    pl_free(h_pl)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
