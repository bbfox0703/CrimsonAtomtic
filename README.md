# CrimsonAtomtic

<img src="./img/Logo.jpg" alt="CrimsonAtomic"/>  

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![Built with Claude Code](https://img.shields.io/badge/Built%20with-Claude%20Code-CC785C?logo=claude)](https://claude.ai/code)
[![Game Version](https://img.shields.io/badge/Game-Crimson%20Desert%201.05%20%2F%201.07-D63B3B)](https://www.playcrimsondesert.com/)

A clean, fast save editor + game-data toolchain for **Crimson Desert** (Pearl
Abyss). Tracks the live game install (currently **1.07**) and remains
compatible with 1.05 / 1.06 saves via schema auto-detection. Cross-platform
goal: Windows (primary), Linux, macOS.

## Layout

```
CrimsonAtomtic/
├── CLAUDE.md                 # minimal rule index — start here
├── CrimsonAtomtic.slnx       # .NET solution
├── Directory.Build.props     # shared MSBuild settings
├── Directory.Packages.props  # central package versions
├── global.json               # .NET SDK pin (10.0.x)
├── docs/                     # architecture, format specs, data hygiene policy
├── src/                      # C# / Avalonia 12 / .NET 10 / Native AOT
│   ├── CrimsonAtomtic.Core         # platform abstractions
│   ├── CrimsonAtomtic.SaveModel    # domain types (records + AOT JSON ctx)
│   ├── CrimsonAtomtic.RustInterop  # crimson-rs P/Invoke layer
│   ├── CrimsonAtomtic.Ui           # Avalonia app
│   └── CrimsonAtomtic.Tests        # xUnit 3
├── tools/                    # Python 3.12+ toolchain: extract / diff / inspect / analyze
├── vendor/                   # cloned external deps (only crimson-rs for now)
└── scripts/                  # build / setup / package scripts (PowerShell 7+)
```

## Why this project exists

A fresh save editor for Crimson Desert, built around a few clear architectural choices:

- Native AOT C# / Avalonia UI for performance and a fast startup path.
- A single Rust core (`vendor/crimson-rs`, our fork) owns all binary-format
  knowledge. No format logic is duplicated into C# or Python.
- Hygienic data flow: only sources are committed; derived files are
  regenerated, not stored. See [docs/data-policy.md](docs/data-policy.md).

For the full architectural rationale see [docs/architecture.md](docs/architecture.md).

## Editor features (current)

The UI ships with a left-rail navigator over the decoded save and a focused
set of Tools-menu bulk operations. Highlights:

- **Generic block / field editor** — every TOC block surfaced as a tree;
  per-field edit with typed validation, present/absent toggle, undo journal,
  and a close-on-dirty save prompt.
- **Inventory** — virtualised lists per container, item picker with icons
  via the on-disk `IconCache/`, add-to-bag, fill-stacks-to-max (per bag and
  across all inventories), remove-element.
- **Sockets editor (v2)** — Fill / Change / Clear per slot, durability
  reset for greater gems, 3 built-in + 3 user-customisable gem sets with
  an Apply-Set toolbar, automatic `_validSocketCount` bump.
- **Dye editor** — per-item slot editor with R/G/B/A + grime + material
  (palette tier) + colour-group dropdowns, all resolved live from the
  three dye gamedata bridges.
- **Sealed Abyss Artifact challenges** — per-row "Mark Challenge Complete"
  (Pattern B v1: FAR tracker flip + X_2 sub-mission insert) plus
  **Tools → Complete All Held Sealed Abyss Artifact Challenges** which
  sweeps every held artifact in one action.
- **Abyss Gates** — bulk **Unlock All Abyss Gates (Map Discovery)** for
  the knowledge layer plus a per-gate Lock/Unlock dialog for the gate-state
  layer.
- **Mercenary rename** — with character portrait column driven by the
  PAZ NPC portrait pipeline.
- **Browse Items / Browse Characters / NPCs** — reference dialogs over the
  full `iteminfo` and `characterinfo` tables.
- **Find Items** — cross-bag search with per-row Go button that jumps the
  main window straight to the item-detail view.
- **Auto-find saves on launch** — Steam / Epic / Game Pass plain-folder
  probe + most-recent preference + per-platform backup tree.

See [docs/status.md](docs/status.md) for the full feature ledger and roadmap.

## Stack

| Layer | Tech |
|---|---|
| App / UI | Avalonia UI 12 on .NET 10, Native AOT trimmed |
| Native core | Rust ([`vendor/crimson-rs`](vendor/crimson-rs/), C ABI + PyO3 from one cdylib) |
| Tooling | Python 3.12+ under [`tools/`](tools/) |
| Tests | xUnit 3 (C#), pytest (Python), `cargo test` (Rust) |

## Thanks

Thanks to the community save editor `CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS` for being a useful reference point during early reverse-engineering.

## First-time setup

```powershell
# 1. Fetch vendored deps (clones crimson-rs from D:\Github\crimson-rs)
.\vendor\update_vendors.ps1

# 2. Build Rust core (produces crimson_rs.dll with both C ABI + PyO3)
.\scripts\build_rust.ps1

# 3. Set up Python venv + install crimson_rs via maturin develop
.\scripts\setup_python_env.ps1

# 4. Build the Avalonia UI
.\scripts\build_ui.ps1
```

## Running a tool

```powershell
# After setup, with the venv active:
python tools\extract\extract_iteminfo.py --help
```

Every script in `tools/` prints full usage when run with no arguments. See
[tools/CLAUDE.md](tools/CLAUDE.md) for conventions.

## License

[MIT](LICENSE). This project does not derive from any other editor's code;
the Rust core under `vendor/crimson-rs` is our own work.
