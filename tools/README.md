# tools/

Game-data tooling for Crimson Desert: extract, diff, inspect, analyze.

See [CLAUDE.md](CLAUDE.md) for conventions every script must follow.

## Layout

```
tools/
├── CLAUDE.md           # conventions (read this first)
├── README.md           # you are here
├── pyproject.toml      # project metadata, requires Python 3.12+
├── common/             # shared helpers (CLI boilerplate, game-path discovery)
├── extract/            # pull data out of game files into structured form
├── diff/               # compare two installs / two versions
├── inspect/            # interactive exploration, byte-range diagnostics
└── analyze/            # higher-level analyses across many files
```

## Tool inventory

Tools are added over time, one per concrete need. The table below lists what we
plan to have; entries are checked off as they land. Every entry's script must
print full usage when run with no args.

### `extract/`

| Tool                          | Status | Purpose                                                    |
| ----------------------------- | ------ | ---------------------------------------------------------- |
| `extract_iteminfo.py`         | TODO   | Pull `iteminfo.pabgb`, emit JSONL (one item per line)      |
| `extract_skillinfo.py`        | TODO   | Pull `skill.pabgb` + `.pabgh`, emit JSON skill tree         |
| `extract_paloc.py`            | TODO   | Pull all PALOC tables, emit per-language JSON              |
| `extract_save.py`             | TODO   | Decrypt one `save.save`, emit JSON snapshot                |
| `build_iconcache.py`          | TODO   | Generate thumbnail tiers (32 / 64 / 128) from source icons |

### `diff/`

| Tool                  | Status | Purpose                                                        |
| --------------------- | ------ | -------------------------------------------------------------- |
| `diff_iteminfo.py`    | TODO   | Cross-version item add / remove / change report                |
| `diff_paloc.py`       | TODO   | Cross-version localization string diff                          |
| `diff_pamt.py`        | TODO   | Pack-group / file-path diff between two installs                |
| `diff_save.py`        | TODO   | Diff two save files (same character, before / after edit)       |

### `inspect/`

| Tool                   | Status | Purpose                                                          |
| ---------------------- | ------ | ---------------------------------------------------------------- |
| `inspect_save.py`      | TODO   | Interactive REPL on a parsed save                                |
| `inspect_pabgb.py`     | TODO   | Hex / field viewer for a single PABGB file                       |
| `_proto_<format>.py`   | as-needed | Scratch parser prototyping — to be deleted once promoted to `crimson-rs` |

### `analyze/`

| Tool                          | Status | Purpose                                                |
| ----------------------------- | ------ | ------------------------------------------------------ |
| `dump_save_fields.py`         | DONE   | Flatten a `.save` into one-row-per-field JSONL (for duckdb/pandas correlation work — unknown-Key RE) |
| `dump_catalogs.py`            | DONE   | Pull iteminfo + PALOC out of an install as JSONL (the lookup tables `dump_save_fields.py` joins against) |
| `extract_keycases.py`         | DONE   | Real-case pack per Key TypeName — value + siblings + catalog resolves — for handing to the crimson_rs parser-writing session |
| `analyze_item_distribution.py`| TODO   | Statistical view over the iteminfo table               |
| `analyze_save_corpus.py`      | TODO   | Aggregate statistics across many save fixtures         |

## How to run

From the project root, after the one-time Python env setup:

```powershell
python tools\extract\extract_iteminfo.py --help
```

If a tool prints its help and exits 2 when you pass no args, it's compliant
with `tools/CLAUDE.md`. If it does anything else, it's a bug.

## What is **not** in `tools/`

- The save editor app itself (that's `src/`).
- Anything that links into the Avalonia binary (FFI via C ABI is the boundary).
- Long-lived integration tests against game files (those live near `crimson-rs`).
- Anything that depends on the **old reference repo**. The only external dependency is `vendor/crimson-rs`.
