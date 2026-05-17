# `sa-investigations/` — Sealed Abyss Artifact RE scratch

> **What lives here**: throwaway probe scripts retained from the
> 2026-05-17 investigation into why Pattern B v1 fails on
> multi-objective SA challenges (the "Feast for the Road" case). Kept
> because the next RE pass — designing Pattern B v2 for multi-step
> shapes — will need them as a starting point.

These scripts originally carried the `_proto_` prefix per
[`tools/CLAUDE.md`](../../CLAUDE.md). They graduated out of the
"will be deleted" bucket by being moved here under a stable name +
this README. Two of the originals (`_proto_peek.py` /
`_proto_feast_names.py`) were dropped on the move — they were
one-off sanity checks already superseded by what remained.

## When to use these

You need them if you're answering questions like:

- *"Why did the bulk SA sweep skip this catalog row?"*
- *"What does the FAR tracker look like for this in-progress challenge
  vs a not-started one?"*
- *"Does this missioninfo name actually resolve through the
  anchor-scan bridge, or is it dropped by the negative-key filter?"*
- *"What's the sub-step structure for Living_* / Cooking-style
  multi-objective challenges?"*

## What each script does

| Script | One-line purpose |
|---|---|
| `feast_diff.py` | Loads slot102/105/106/107 bodies + dumps the Feast for the Road (1000913) catalog + adjacent twin + FAR tracker for each. The original "what's different?" probe. |
| `feast_vs_hooves.py` | Side-by-side full-field dump of Feast (Living_VII) vs Hooves II (Vehicle_II) catalog / twin / FAR. Shows the `_completeCount` divergence + data_size delta. |
| `all_sa_far.py` | Walks every SA-shaped catalog row in a save (positive catalog key + adjacent negative twin) and prints a tidy FAR-shape comparison table across all of them. Catches outliers. |
| `missioninfo_strings.py` | Byte-greps `Challenge_SealedArtifact_*` names out of `missioninfo.pabgb` to identify which catalog rows have / don't have `_2` sibling sub-steps. |
| `missioninfo_key_to_name.py` | Byte-scans `missioninfo.pabgb` for specific u32 keys, recovers the adjacent name field via a length-prefix heuristic. Useful when the anchor-scan bridge drops a row. |
| `missioninfo_ctypes.py` | The DEFINITIVE test — opens the `crimson_missioninfo_load_from_bytes` C ABI directly via `ctypes` from Python, iterates every entry, and reports whether a specific name is in the bridge's name→key map. This is the probe that confirmed `Living_VII_2` is filtered out. |
| `sa_item_mission_map.py` | Enumerates every `Sealed_Abyss_Artifact_*` item in `iteminfo.pabgb` + dumps each item's `look_detail_mission_info` field. Identifies which mission a given SA artifact rewards. |
| `search_x2.py` | Pattern-discovery probe — finds raw byte offsets of `Challenge_*` literals in `missioninfo.pabgb`, then reads the 32-byte window before each to identify the row's u32 key. The probe that revealed `Living_VII_1` / `_2` have **negative-encoded keys** (`0xFFFFFD7E` / `0xFFFFFD7D`) while `_3` has a positive key. |

## How to run

From the project root, with the Python venv set up
(`.\scripts\setup_python_env.ps1`):

```pwsh
.\.venv\Scripts\python.exe tools\inspect\sa-investigations\missioninfo_ctypes.py
```

Most scripts target the live game install at
`D:\SteamLibrary\steamapps\common\Crimson Desert` and the live save
under `%LOCALAPPDATA%\Pearl Abyss\CD\save\`. A few read pre-extracted
body bytes from `out/sa-feast-diff/slot10*.bin` — those need
`tools\extract\extract_save.py` to have been run first.

## Key findings these scripts produced (2026-05-17)

1. **`Challenge_SealedArtifact_Living_VII_2`** (the X_2 follow-up
   Pattern B v1 wants to clone for Feast for the Road) is keyed at
   `0xFFFFFD7D` — a **negative-encoded** sentinel range. The
   missioninfo anchor-scan parser in `crimson-rs` filters out rows in
   that range to avoid garbage, so the name doesn't appear in the C#
   bridge's name→key map → `LookupMissionKeyByInternalName` returns
   null → context build fails.

2. Multi-objective challenges (`Living_*` / Cooking series) store
   sub-step missions with **negative keys** in a visibility/sub-step
   chain rather than appending standalone positive-keyed `_N` rows
   like linear single-step challenges do (`Vehicle_II_2` has positive
   key `0x000F4F49`).

3. Pattern B v1 is therefore **scope-limited** to linear single-step
   SA challenges (Shield II / Spear I / Hooves II / Slash III). The
   editor's bulk sweep correctly skips multi-objective challenges and
   surfaces the reason in the confirm dialog
   (`docs/status.md` 2026-05-17 entry, MainWindowViewModel diff).

A future Pattern B v2 for multi-objective shapes would need:

- Confirmation of the natural-completion shape for one of these
  challenges (RE diff slot106 → slot107 for the "progress 2/3 → 3/3"
  transition, then slot107 → post-claim).
- Either a `crimson-rs` parser change to surface negative-keyed
  missioninfo rows, OR a C# byte-scan fallback that recovers the
  key directly from `missioninfo.pabgb` bytes.

Both are out of scope for current work. Reference scripts stay here
until someone picks that up.
