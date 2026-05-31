# CrimsonAtomtic — Crimson Desert Save Editor

A free save editor for **Crimson Desert**, built for the current game (patch **1.08 / 1.09**). It also opens older saves (1.05 / 1.06) and re-saves them in **their own format** — your save is never converted to a newer version. It finds your save file automatically whether you bought the game on Steam, Epic, or Game Pass.

Just download one `.exe`, double-click it, no installer.

---

## Quick rundown

- **Items** — edit stack counts and durability; search across every bag at once; fill all stacks to max with one click
- **Dye your gear** — pick colors from a **visual palette grid** (the exact colors the game itself uses); change material (cloth / leather / metal) and grime per dye slot
- **Sockets / Gems** — put any gem in any socket on any item (equipped or in bags); save your own gem sets for one-click apply
- **Rename mounts, mercenaries, and pets** — give your horse a better name; full Unicode support
- **Recover sold items** — accidentally sold something to a vendor? Vendor Buyback lists every item sitting in a store's queue, one click to bring it back
- **Abyss Gates** — unlock individual gates or all of them at once
- **Unlock mounts** — get the six special sigil mounts (White Bear, Silver Fang, Snowwhite Deer, Alpine Ibex, Rock Tusk Warthog, Phoenix) and the **Dragon (Blackstar)** without grinding the quest chains
- **Sealed Abyss Artifact challenges** — mark a challenge as complete after picking up the artifact in-game; a bulk version completes every eligible challenge in one click
- **Find anything** — search all items by name in any of the 14 game languages; browse all 600+ characters with portraits

---

## In a bit more detail

### Items

Search the entire save by item name — the search box matches in **any of the 14 game languages**, so you can type 真皮 or *leather* or *cuir*, whatever language you play in. The editor walks every bag at once: your storage, your equipped gear, your mercenaries' inventories, and even your mounts' saddlebags.

Per-item, you can change stack count, durability, the "new" flag, the lock flag — anything the game stores on the item. Or use **Fill stacks to max** to top up every stack across every bag in one go.

### Dye

A picker dialog with a **visual color palette** — the same 109 colors the in-game dye system uses for each theme. Click a color, the editor writes it back to your save. Works on items in your bags and items you (or Damine / Oongka / your mounts) are currently wearing.

You can also change the material (cloth / leather / metal / etc.) and the dirt-and-wear level per slot.

### Sockets / Gems

Three actions per socket: **Fill** (empty → put a gem in), **Change** (replace one gem with another), **Clear** (gem → empty). Works on every socket-bearing item including currently equipped weapons and armor.

The **Apply Set** dropdown lets you take a saved gem layout and apply it to a whole weapon in one click. 3 built-in sets are included; you can save 3 of your own under `Tools → Edit Custom Gem Sets`.

### Mounts and Mercenaries

Rename any of them — your horses, your wagon, your balloon, any tamed animal, any human follower. Full Unicode names supported.

**Unlock Mounts** (`Tools → Unlock Mounts`) adds the special mounts you'd otherwise have to grind long quest chains for:

- The six **sigil mounts** — White Bear, Silver Fang, Snowwhite Deer, Alpine Ibex, Rock Tusk Warthog and the Phoenix pet — are unlocked the clean, game-legitimate way: the editor drops the matching *Sigil of Solidarity* into your Quest Artifacts, and you **use it in-game** to finish. The game itself does the unlock, so the mount summons and rides exactly as if you'd earned it.
- The **Dragon (Blackstar)** is unlocked entirely in the editor — it's added straight to your stable at full HP, ready to summon. No in-game step needed.

Everything is reversible: if you don't save, nothing is written.

### Vendor Buyback

Lists every item sitting in any store's buyback queue. Per-row **Remove** drops it (useful for cleanup); the **Jump…** button takes you straight to the item in the main editor view so you can edit it before recovering it.

### Abyss Gates

A per-gate dialog with Lock / Unlock toggles, OR a bulk **Unlock All** menu item. Useful if you want to skip the discovery-by-walking phase.

### Sealed Abyss Artifact challenges

If you've picked up a Sealed Abyss Artifact in-game but haven't triggered the challenge completion, the editor can write what the game would write naturally — then you just claim the reward in-game as if you completed the challenge yourself. Per-challenge button on each row, OR **Complete all held challenges** to run the whole batch.

The button is grayed out (with a tooltip explaining why) for challenges that aren't ready — e.g. you haven't picked up the artifact yet, or you've already completed it.

### Browse characters / NPCs

A grid of every character in the game (600+) with portraits and resolved names. Useful as a reference, or as a picker when you're editing a field that wants a CharacterKey value.

---

## Safety

- **Every** save edit creates a backup automatically — 6 versions kept per save slot
- Backups stay organized per platform — Steam saves, Epic saves, and Game Pass saves don't mix
- Use `File → Restore from Backup…` to roll back any time
- If you try to quit with unsaved changes, the editor will ask first

---

## Languages

In-game text (item names, quest titles, character names, …) shows in your game's language. All 14 are supported:

English · 繁體中文 · 简体中文 · 日本語 · 한국어 · Deutsch · Français · Italiano · Español (ES + MX) · Русский · Türkçe · Polski · Português (BR)

The editor's own menus are available in **English**, **繁體中文**, and **日本語** — switch any time under `Settings → Language`.

---

## Requirements

- Windows 10 or 11 (64-bit)
- Crimson Desert installed on your PC (the editor reads icons, portraits, and translated names from the game folder — but **never modifies anything in your game install**)
- About 50 MB free disk space for backups and the icon cache

No .NET runtime to install. No installer to run.

---

## ⚠ Please read before using

>! This editor is provided **as-is** with no warranty. Editing save files is inherently risky — even with auto-backups, no tool can guarantee your save will keep working after a future game update.
>!
>! - The author is **not responsible** for save corruption, lost progress, account issues, or any other consequence
>! - Always test on a backed-up save first
>! - Steam / platform achievements only fire on the natural in-game completion path — completing content via the editor does **not** unlock achievements
>! - This is a **single-player** tool — don't use it if Crimson Desert ever adds online modes
>!
>! 本編輯器**不保證**能正常運作。雖然每次寫入都會自動備份，但編輯存檔本身就有風險，且無法保證未來遊戲更新後仍能正常使用。作者**不負任何責任** — 包含存檔損壞、進度丟失、帳號封禁等任何後果。建議先在備份存檔上測試。Steam 或平台成就僅在遊戲內自然取得時觸發，無法透過編輯解鎖。
>!
>! 第一次啟動時會跳出同意對話框，同意後就不會再出現。
