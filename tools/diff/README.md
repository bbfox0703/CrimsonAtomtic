# tools/diff/

Compare two installs or two extracted data sets. Used when a game patch lands
and we need to know what shifted.

| Script              | Inputs                            | Output                                |
| ------------------- | --------------------------------- | ------------------------------------- |
| `diff_iteminfo.py`  | `--from <ver_a> --to <ver_b>`     | added / removed / changed item report |
| `diff_paloc.py`     | `--from <ver_a> --to <ver_b>`     | string add / remove / retranslated    |
| `diff_pamt.py`      | `--from <ver_a> --to <ver_b>`     | files added / removed per pack group  |
| `diff_save.py`      | `--from <save_a> --to <save_b>`   | per-field diff of a parsed save       |

Conventions:

- Inputs are either install roots (we re-extract on demand, cached) or
  pre-extracted data folders (we trust them).
- Output is human-readable by default; pass `--json` for machine-readable.
- Diffs that exceed a sane threshold (e.g. > 1000 changed items) abort with
  a warning unless `--all` is passed — protects against accidental comparison
  of unrelated versions.

No scripts here yet — placeholder.
