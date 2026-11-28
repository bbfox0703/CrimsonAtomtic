# tools/diff/

Compare two installs or two extracted data sets, for when a game patch lands
and we need to know what shifted.

No scripts live here yet. Cross-version diffing has so far been done in
crimson-rs and through the `analyze/` dumps; see
[docs/game-versions.md](../../docs/game-versions.md) for the playbook.

Conventions for a script added here:

- Inputs are either install roots (re-extracted on demand, cached) or
  pre-extracted data folders (trusted as they are).
- Output is human-readable by default; `--json` for machine-readable.
- A diff past a sane threshold (e.g. > 1000 changed items) aborts with a
  warning unless `--all` is passed, so two unrelated versions are not
  compared by accident.
