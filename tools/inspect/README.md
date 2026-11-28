# tools/inspect/

Interactive / single-shot exploration. Use these when you don't yet know
what you're looking for and need to poke around a save body or a PABGB.

## What's here

| Path                                       | Purpose                                                                  |
| ------------------------------------------ | ------------------------------------------------------------------------ |
| [`inspect_save_body.py`](inspect_save_body.py)       | Decode a `.save` body and dump per-block summaries + per-field details |
| [`inspect_save_section.py`](inspect_save_section.py) | Filter the decoded save to one class / one block — pretty-print or JSON |
| `_proto_<format>.py`                       | **Scratch only.** Prototype a new parser; delete once the format is promoted into `crimson-rs`. Contract: not maintained, not tested, not allowed as an import. |

## The `_proto_` contract

Files prefixed with `_proto_` are throwaway scratch. They:

- Are not tested
- Are not imported by anything else
- Die when the format goes into `crimson-rs`, OR get **promoted** by being
  moved out of `_proto_` to a stable name (with a docstring + README entry)
  if they remain useful as ongoing reference

Findings worth keeping go into the docs (a format doc, or the dev-log
entry of the work they served), not into a folder of retained scripts:
scripts that nothing runs drift out of step with the format and the
vendored library.

## How to run

From the project root, with the Python venv set up
(`.\scripts\setup_python_env.ps1`):

```pwsh
.\.venv\Scripts\python.exe tools\inspect\inspect_save_body.py --help
```

All scripts print full usage when run with no args (per
[`tools/CLAUDE.md`](../CLAUDE.md) rule 1).
