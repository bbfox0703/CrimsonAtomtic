# tools/ — Python toolchain conventions

> The Python tools are for **game-data extraction, diff, inspection, and analysis**.
> They are **not** part of the user-facing save editor.
> The save editor is C# / Avalonia and consumes `crimson-rs` directly via its C ABI.

## Scope

- Run by a developer (us) when investigating a format, a patch, or a bug.
- Always invokable from a clean PowerShell or bash with no GUI dependency.
- Outputs to `data/<game_version>/` or `out/` (project-local), never to user-facing folders.

## Mandatory rules

### 1. `--help` discoverable, no-args = print usage + exit 2

Every script under `tools/` **must**:

```python
from common.cli import require_args

def main():
    p = argparse.ArgumentParser(
        prog="extract_iteminfo.py",
        description="Extract iteminfo.pabgb from a Crimson Desert install "
                    "and emit one JSON record per item.",
    )
    p.add_argument("--game-dir", required=True, help="path to a Crimson Desert install")
    p.add_argument("--out", required=True, help="output directory")
    require_args(p)  # prints help + sys.exit(2) when no args are given
    args = p.parse_args()
    ...

if __name__ == "__main__":
    main()
```

Without `require_args`, argparse exits silently with a one-line error when a required arg is missing. We want the **full help text** so a future-us (or Claude) discovers the tool just by running it.

### 2. Docstring header on every script

```python
"""extract_iteminfo.py — pull iteminfo.pabgb and emit JSONL.

Input:  --game-dir <CrimsonDesert install root>
Output: <out>/items.jsonl (one item record per line)
Versions tested: 1.05, 1.06.
"""
```

The docstring is what `--help` prints as the description. Keep it usable.

### 3. No side effects at import time

- No code at module top level beyond imports, constants, and function/class definitions.
- All work happens inside `main()`, called from `if __name__ == "__main__":`.
- Reason: side effects at import time break static analysis, unit testing, and tool composition (e.g. constructing a UI / network client at import time is a common offender).

### 4. Type hints everywhere

`from __future__ import annotations` at the top of every file. Type hints on every public function. Helps the next person (and the next model) read the code.

### 5. Shared helpers go in `common/`

- CLI boilerplate: `common/cli.py`
- Game path discovery / version detection: `common/paths.py`
- Anything reused by more than one script: add to `common/`, don't copy-paste.

### 6. `crimson_rs` is the only allowed binary-format dependency

Tools call `import crimson_rs`. They **do not** re-implement byte-level parsing in Python. If a format isn't yet in `crimson-rs`, either:

- Add it to `crimson-rs` (preferred); or
- Write the Python prototype in a clearly-named scratch file (e.g. `inspect/_proto_<format>.py`) and remove it once `crimson-rs` lands the parser.

The `_proto_` prefix marks scratch code that **will be deleted**, not maintained.

### 7. No data committed under `tools/`

- Outputs go to `../data/<version>/` (gitignored) or `../out/` (gitignored).
- If a tool needs a fixture, it goes under `tests/fixtures/` with a `README.md` explaining what it is.

## Naming convention

- File: `<verb>_<target>.py` — `extract_iteminfo.py`, `diff_paloc.py`, `inspect_save.py`.
- Folder: by verb category — `extract/`, `diff/`, `inspect/`, `analyze/`.
- No `_test_*.py` / `_trace*.py` / `diag_*.py` clutter at top level. Real tests go in `tests/` (when we have any); throwaway scripts go in `inspect/_proto_*.py` and get deleted.

## Adding a new tool

1. Decide which folder (`extract/`, `diff/`, `inspect/`, `analyze/`).
2. Create the file following the rules above.
3. Add a one-line entry to `tools/README.md` so future-us can find it.
4. If the tool produces a new output format, add it to `docs/data-policy.md`.

## Removing a tool

If a script is no longer used, **delete it** along with its `README.md` entry. Don't let `_test_*` / `diag_*` files accumulate in the tree indefinitely.

## Running

From the project root:

```powershell
# First-time setup (or after a vendor refresh):
.\scripts\setup_python_env.ps1

# Then any tool:
python tools\extract\extract_iteminfo.py --game-dir "D:\SteamLibrary\steamapps\common\Crimson Desert" --out data\1.06\
```

The setup script creates `.venv\`, installs `crimson_rs` from `vendor/crimson-rs` via `maturin develop`, and is idempotent.
