# **CrimsonAtomtic** — Crimson Desert Save Editor

> Cross-version save editor for **Crimson Desert** — supports patches **1.05 / 1.06 / 1.07** with auto schema detection. Single-file Native AOT executable, no .NET runtime to install. Steam / Epic / Game Pass (plain folder) saves auto-detected.

---

## Quick highlights

| Area | What you get |
|:-|:-|
| **Save engine** | Full ChaCha20 + HMAC + LZ4 round-trip — *zero* tolerance for byte drift |
| **Localization** | 14 PALOC languages mirrored from the game install (en / zh-tw / zh-cn / jpn / kor / ger / fre / ita / spa-es / spa-mx / rus / tur / pol / por-br) |
| **Safety** | Auto-backup *before every write* — 6-version retention per slot, scoped per Steam / Epic / Game Pass |
| **Editing surface** | Per-field scalar edits + 8 specialized dialogs + 7 bulk sweeps |
| **Resolved names** | **32** key-resolver bridges (Item / Mission / Quest / Stage / Knowledge / Character / Skill / Store / Region / GameAdvice / …) |
| **Native AOT** | ~24 MB single .exe, trimmed, no GC stalls on click |

---

## Specialized editors

- **Items** — Picker with icon search, multi-language name match, stack count, endurance, transferred-item tracking
- **Sockets** — Fill / Change / Clear per slot, Apply Set (3 built-in + 3 user-defined gem sets), works on equipped gear AND inventory
- **Dye** — RGBA + grime + material + color-group dropdowns, per-slot, driven by game's own `dye*.pabgb` (replaces the old PyQt5 JSON pack)
- **Rename Mercenary / Pet** — UTF-8 in-save rename, portrait column with NPC head-shots from PAZ + class glyph fallback
- **Vendor Buyback** — Lists every sold item across all stores; per-row Remove or *Jump to item in main editor*
- **Abyss Gates** — Per-gate Lock/Unlock dialog AND bulk "Unlock All" (map discovery layer)
- **Sealed Abyss Artifact Challenges** — Per-challenge **Mark Complete** + Bulk "Complete all". Pattern B v1 recipe verified on Shield II / Spear I / Hooves II / Slash III
- **Item Dye master** — Lists every dyed item; Edit opens a per-item slot editor

---

## Bulk operations (with **deferred-redecode batch** = 200× speedup)

- [x] Bulk-complete held Sealed Abyss Artifact challenges
- [x] Unlock all Abyss Gates (Knowledge bulk-inject)
- [x] Fill all item stacks to max
- [x] Drop all Sealed Abyss Artifacts from inventory
- [x] Apply gem set across all sockets of a target item
- [x] Multi-scalar Dye slot Apply
- [ ] *(planned)* Bulk Pattern B v2 for multi-objective challenges

> Every length-changing edit used to trigger a full body re-decode (~25 ms / 5 MB body). The deferred batch suspends per-call decode and commits *one* re-decode at end. Bulk SA sweep went from **~10 s → <1 s** on 141 challenges.

---

## Discovery / navigation

- **Find Items** — flat-list every item slot across all 18 containers + Go button to navigate the block tree
- **Browse Characters / NPCs** — 600+ characters with portraits; doubles as a `CharacterKey` *picker* for field editing
- **Browse Character References** — every `CharacterKey` referenced anywhere in the save (top-level + nested) with Jump-to-block

> ⚠ Browser + picker show a banner: *"資料串接不一定正確"* / *"Reference linkage is best-effort — cross-verify before relying on the resolved name."*

---

## What you can edit directly in the block tree

For every scalar field the schema declares:

```
bool  u8/u16/u32/u64  i8/i16/i32/i64  f32  f64
*Key typedefs (ItemKey, MissionKey, CharacterKey, …)
StringInfoKey (Jenkins hash → name reverse)
f32x3 / f32x4 / u32x4 composite (position, quaternion, SceneObjectUuid)
```

Inline `dynamic_array<u32>` / `<u64>` rendering shows contents inline (up to 12 elements, then a `… (N more)` continuation).

---

## Change journal

Every mutation logs a per-operation summary. **Close-on-dirty modal** confirms before quitting with unsaved changes. Tools → Review Pending Changes lists everything since the last Save.

---

## Requirements

- Windows 10 / 11 (x64)
- A licensed Crimson Desert install (game files needed for icon / portrait / PALOC lookups — *the editor never touches your install*; reads only)
- ~50 MB free under `%LOCALAPPDATA%\CrimsonAtomtic\` for icon cache + backup tree

---

## Source / docs

- Source: *(GitHub URL — fill in before posting)*
- Architecture notes: see `docs/architecture.md` and `docs/status.md` in the repo

---

## Spoiler: under the hood

>! **Native core**: Rust crate (`crimson-rs`, MIT-licensed fork) reading binary save format with byte-perfect round-trip guarantees. PyO3 Python bindings for diff tooling + C ABI for the C# UI.
>! **UI**: Avalonia UI 12 on .NET 10, AOT-published.
>! **Save format**: header { magic, version, flags, payload_size, uncompressed_size } + ChaCha20-encrypted + HMAC-SHA256-tagged + LZ4-compressed body. Body = schema (TypeName + field layout per class) + TOC + per-block payloads.
>! **Localization**: PALOC binary catalog files extracted from the game's PAZ archives. `(typeByte, key)` → string lookup, plus a `lookup_display_name` hash-hop variant for Mission / Quest / Stage / Knowledge titles.

---

## ⚠ Disclaimer

>! 本編輯器**不保證**能正常運作，也沒有針對所有 save 結構做詳細檢查。如使用後造成資料錯誤、存檔損壞或任何遊戲帳號相關問題，作者**不負任何責任**。
>!
>! This editor is provided **as-is** with no warranty of correct operation and no exhaustive validation of save structures. The author accepts **no responsibility** for any data corruption, save damage, or account-related issues that may result from its use.
>!
>! First-launch prompts an in-app dialog; accepting it persists to `%LOCALAPPDATA%\CrimsonAtomtic\settings.json` so the prompt only fires once per machine.

* * *

*Generated against the* `feat(c#): character refs browser + CharacterKey picker + first-launch disclaimer` *checkpoint, 2026-05-17.*
