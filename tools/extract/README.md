# tools/extract/

Pull data out of game files into structured form.

| Script                  | Input                  | Output                                       |
| ----------------------- | ---------------------- | -------------------------------------------- |
| `extract_iteminfo.py`   | `--game-dir <root>`    | `<out>/items.jsonl`                          |
| `extract_skillinfo.py`  | `--game-dir <root>`    | `<out>/skill.json`                           |
| `extract_paloc.py`      | `--game-dir <root>`    | `<out>/paloc/<lang>.json` (×14 languages)    |
| `extract_save.py`       | `--save <path>`        | `<out>/save.json`                            |
| `build_iconcache.py`    | `--game-dir <root>`    | `<out>/icons/{32,64,128}/<key>.png`          |

All scripts:

- Take `--out <dir>` (created if missing).
- Default to no overwrite; pass `--force` to replace existing output.
- Tag output with the source-file SHA so consumers can detect staleness.
- Print full `--help` on no-args invocation.

No scripts live here yet — this folder is a planning placeholder. See
[../CLAUDE.md](../CLAUDE.md) for the conventions a new script must follow.
