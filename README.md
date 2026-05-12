# CrimsonAtomtic

A clean, fast save editor + game-data toolchain for **Crimson Desert** (Pearl
Abyss). Target game version: **1.06**. Cross-platform goal: Windows (primary),
Linux, macOS.

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

There is an existing community save editor for Crimson Desert
(`CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`). It works but it is slow,
cluttered, and structurally messy. We build from the ground up:

- Native AOT C# / Avalonia UI for performance and a usable startup path.
- A single Rust core (`vendor/crimson-rs`, our fork) owns all binary-format
  knowledge. No format logic is duplicated into C# or Python.
- Hygienic data flow: only sources are committed; derived files are
  regenerated, not stored. See [docs/data-policy.md](docs/data-policy.md).
- The old repo is a one-time reference; nothing in this project depends on it
  going forward.

For the full architectural rationale see [docs/architecture.md](docs/architecture.md).

## First-time setup

```powershell
# 1. Fetch vendored deps (clones crimson-rs from D:\Github\crimson-rs)
.\vendor\update_vendors.ps1

# 2. Build Rust core (standalone library; produces crimson_rs.* in vendor/crimson-rs/target/)
.\scripts\build_rust.ps1

# 3. Set up Python venv + install crimson_rs via maturin develop
.\scripts\setup_python_env.ps1

# 4. (Once src/ exists) build the Avalonia UI
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

TBD. The old reference repo is MPL-2.0; this project does not derive from its
code, only from a one-time reading of its parsers.
