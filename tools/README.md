# tools/

Game-data tooling for Crimson Desert: extract, diff, inspect, analyze.

See [CLAUDE.md](CLAUDE.md) for conventions every script must follow.

## Layout

```
tools/
├── CLAUDE.md           # conventions (read this first)
├── README.md           # you are here
├── pyproject.toml      # project metadata, requires Python 3.12+
├── common/             # shared library modules (no CLI)
├── extract/            # pull data out of game files into structured form
├── diff/               # compare two installs / two versions (empty)
├── inspect/            # interactive exploration, byte-range diagnostics
├── analyze/            # higher-level analyses across many files
└── tests/              # pytest suite for the scripts and common/
```

## Tool inventory

What is in the tree today. Every runnable script prints full usage and exits
2 when run with no args. Add a row when you add a script; delete the row
when you delete the script.

### `extract/`

| Tool | Purpose |
| --- | --- |
| `extract_save.py` | Decrypt + decompress one `.save`; writes `<slot>.json` (header, sizes, HMAC status, body SHA-256) and `<slot>.bin` (the body) |

### `diff/`

No scripts yet.

### `inspect/`

| Tool | Purpose |
| --- | --- |
| `inspect_save_body.py` | Show the schema + TOC of a decoded save body |
| `inspect_save_section.py` | Show the decoded contents of save sections, filtered by class or TOC index |

### `analyze/`

| Tool | Purpose |
| --- | --- |
| `dump_save_fields.py` | Flatten a `.save` into one-row-per-field JSONL (for duckdb / pandas correlation work) |
| `dump_catalogs.py` | Pull the iteminfo + PALOC catalogs out of a game install as JSONL |
| `extract_keycases.py` | Package real-world Key examples, per `(type, value)` with siblings and catalog resolves, for the crimson_rs RE session |
| `dump_challenges.py` | Enumerate every `Challenge_*` row from missioninfo, with save-side completion state |

### `common/`

Library modules imported by the scripts; not run directly.

| Module | Purpose |
| --- | --- |
| `cli.py` | CLI helpers shared by every tool (`require_args`: usage + exit 2 on no args) |
| `paths.py` | Game install discovery and save-file location helpers |
| `gamedata_layout.py` | Resolve where an install keeps its gamedata tables and paloc files (pre- and post-2.01 layouts) |
| `crimson_abi.py` | Where the crimson-rs C ABI library lives, and the status codes it returns, for tools that load it through ctypes |

### `tests/`

| File | Covers |
| --- | --- |
| `conftest.py` | Shared fixtures: loads the scripts by file path |
| `test_rule9.py` | Rule 9 for every runnable script: no-args usage, no import-time effects |
| `test_common.py` | The shared modules under `common/` |
| `test_analyze_helpers.py` | Pure helpers inside the `analyze/` scripts |

## How to run

From the project root, after the one-time Python env setup
(`.\scripts\setup_python_env.ps1`):

```powershell
python tools\extract\extract_save.py --help
python -m pytest tools/tests
```

If a tool prints its help and exits 2 when you pass no args, it's compliant
with `tools/CLAUDE.md`. If it does anything else, it's a bug.

## What is **not** in `tools/`

- The save editor app itself (that's `src/`).
- Anything that links into the Avalonia binary (FFI via C ABI is the boundary).
- Long-lived integration tests against game files (those live near `crimson-rs`).
- Anything that depends on **`CRIMSON-DESERT-SAVE-EDITOR`**. The only external dependency is `vendor/crimson-rs`.
