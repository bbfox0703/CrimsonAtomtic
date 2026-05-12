# tools/inspect/

Interactive / single-shot exploration. Use these when you don't yet know what
you're looking for and need to poke around.

| Script                | Purpose                                                          |
| --------------------- | ---------------------------------------------------------------- |
| `inspect_save.py`     | Parse a save and drop into a REPL with the data pre-bound        |
| `inspect_pabgb.py`    | Hex / field viewer for a single PABGB file                       |
| `_proto_<format>.py`  | **Scratch only.** Prototype a new parser; delete once promoted    |

The `_proto_` prefix is a contract: such files are not maintained, not tested,
and not allowed to be imported by anything else. They die when the format goes
into `crimson-rs`.

No scripts here yet — placeholder.
