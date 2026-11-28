# UI / UX design

## Goal

A save editor that loads fast, makes the common edit path obvious, and never makes the user wonder where to start.

## Design constraints

A few concrete constraints derived from prior experience and the AOT target — calling them out so design decisions stay aligned with them.

1. **No tab soup on launch.** A flat tab strip with many top-level surfaces makes the entry point unclear. Use a left-rail navigator scoped to the loaded save instead.
2. **No god-class window.** UI / view-model / data layers stay separated; one giant `MainWindow` is not allowed to grow into an everything-class.
3. **Open-save is the first obvious action.** Startup must not ask the user to configure paths, languages, or filters before they can load a file.
4. **No eager data parsing on launch.** Reference tables (iteminfo, paloc, world map JSON, etc.) load lazily, in the background, only on first need.
5. **Icons are resized once, then cached.** Never load full-resolution WebP into the pixel buffer; the thumbnail cache is the rendering source of truth.
6. **No synchronous network fetches.** Icon / asset fetches happen off the UI thread and are visible only via a progress indicator.
7. **Typed edit model.** Edits are typed calls on the save held by crimson-rs; the C# layer never patches save bytes in place.
8. **Single source of truth per data file.** No shadow / duplicate copies (e.g. `items.jsonl` vs `item_templates.json`) where labels can disagree depending on which path read which file.

## Principles for the new UI

### Startup must be fast

- **First paint < 500 ms** on a modern dev machine.
- No game-data parsing on startup. The app comes up with an empty workspace and a single, obvious **Open save** action.
- Game-data tables (iteminfo, paloc, skill) load **lazily on first need**, in the background, with a visible progress indicator.

### Single dominant action per screen

- Launch → empty workspace + Open save (or recent-saves list, if a recent file exists).
- After open → the save shows up as a left-rail navigator (Inventory / Skills / Stats / World / …), not a tab strip.
- Each pane has one primary action and a small set of secondary ones, not a wall of buttons.

### Lazy and incremental

- Inventory list virtualised — no eager rendering of 6,000+ items.
- Icons rendered from a thumbnail cache, not the source WebP. Cache lives under `%LOCALAPPDATA%\CrimsonAtomtic\IconCache\` and is regenerated from game extraction.
- Localized strings resolved on demand from PALOC; the language switch is a UI control, not an app restart.

### Typed model

- The decoded save lives behind the FFI in `crimson-rs`. The UI edits it through `ISaveLoader` (RustInterop): typed scalar writes, validated first by `SaveModel.ScalarFieldEditing`, plus named operations for lists, inventory and the Tools-menu editors.
- There is no undo. `Services/ChangeJournal.cs` lists what the user changed since the last save, a close-on-dirty prompt guards unsaved edits, and every Save first copies the file it overwrites into the backup tree (`Services/SaveBackupService.cs`), which Restore reads.

### Localisation

- **UI shell strings**: `Resources/Strings/en.axaml`, `ja.axaml` and `zh-TW.axaml`, bound with `DynamicResource`. The language comes from the user's pick in settings, else the OS UI language, and switches at runtime from Tools → UI Language.
- **In-game item / skill / region names**: resolved from PALOC at display time, in whichever language the user picked. The shell language and the in-game language are independent settings.

## Asset pipeline

```
PAZ group 0012          IconExtractionService      IconCache/<ItemKey>.webp
ui/texture/icon/*.dds ─► (in the app; SkiaSharp ─► under %LOCALAPPDATA%\CrimsonAtomtic\
                          decode, 64 px, WebP)
```

- The only icon source is in-game extraction, run from Tools → Extract Icons from Game Data, in the background with progress. Rerun it after a game patch to pick up new items.
- `IconProvider` reads the cache at display time; an item with no cached icon shows without one.

## Open questions

- Theme: the app follows the system (FluentTheme, `RequestedThemeVariant="Default"`); there is no in-app light / dark switch. Add one?
- Mod awareness: how to surface "this item came from a mod" without cluttering vanilla item display.
- Multi-slot view: should the user see all 100+ slots at once, or pick one slot at a time? Probably a slot picker on launch.

These get resolved as we build, but write the answer here once decided so the rationale doesn't evaporate.
