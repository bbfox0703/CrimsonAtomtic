# tools/analyze/

Higher-level analyses across many files. Distinct from `inspect/` (one file at
a time) and `diff/` (exactly two inputs).

| Script                          | Purpose                                                       |
| ------------------------------- | ------------------------------------------------------------- |
| `analyze_item_distribution.py`  | Tier / type / price distribution across the iteminfo table    |
| `analyze_save_corpus.py`        | Roll up stats across all saves under a save root              |

Output is always written as a report file (`.md` or `.json`) — not just
printed to stdout. Reports go to `out/analyze/<date>_<topic>/`.

No scripts here yet — placeholder.
