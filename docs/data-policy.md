# Data policy

> "Pave the foundation well, so we don't have to wonder later if it's a data issue."

This is the contract for how data flows into and through this project. The rules below keep the codebase coherent over time and avoid the data-drift failure modes that compound silently otherwise.

## 1. Old reference repo is one-time mining only

- **Repo**: `D:\Github\CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS`.
- **Permitted use**: read it once to understand a format, copy icon assets, or extract parser logic that we then rewrite cleanly.
- **Forbidden**: importing from it at runtime, depending on its scripts or data, syncing updates from it.
- After we have mined what we need, this repo is no longer a dependency of the codebase.

## 2. The only ongoing external dependency is `vendor/crimson-rs`

- It is **our** fork, maintained at `D:\Github\crimson-rs`. We can change it any time, including breaking changes.
- Refreshed via `vendor/update_vendors.ps1`. Never a submodule. Never committed under `vendor/` in this repo.
- If we need new functionality that touches binary formats, it goes into `crimson-rs`, not somewhere else.

## 3. No derived data in git

A classic failure mode is committing both an extracted source AND a file generated from it — for example:

- `data/iteminfo_dump/items.jsonl` (extracted from game)
- `data/item_templates.json` (generated from the JSONL)

…and then letting them drift over time. We do not do this.

- For each generated artifact, **only the source** goes in git.
- Derived files live in `.gitignore` and are produced by a documented generator.
- The generator script names exactly what it produces, in its `--help` output and in the relevant `README.md`.
- If you find yourself committing a generated file "for convenience", it means the generator is too slow or too undocumented. Fix the generator instead.

## 4. Source of truth lives in the game

- The canonical source of an item, skill, region, or string is the **game's own files**, parsed by `crimson-rs`.
- Any project-local JSON catalog is **derived**, even if we hand-curate notes on top of it (curated overlays go in a separate file referencing the canonical key).
- When a game patch lands, the extraction pipeline reruns; we never edit derived catalogs by hand to "match" the new game state.

## 5. Foundation-fix over consumer-workaround

When a parser produces wrong data:

- **Right**: fix `crimson-rs`, fix the schema, fix the extraction script. Add a regression test.
- **Wrong**: special-case the bad value in the UI, in the save model, or in a tool downstream.

If you're tempted to add `if value == "weird_legacy_thing": handle specially` in C# or in a Python tool, stop and ask whether the parser should have produced a normal value in the first place.

## 6. Images: copy once, reprocess, then own

- Any icon WebP files mined once from the reference repo run through a thumbnail/cache step before the UI ever sees them — full-size icons displayed at 32 px are wasteful.
- Going forward, new icons come from extracting game assets via `crimson-rs` directly.
- See `docs/ui-design.md` for the asset pipeline plan.

## 7. Game version is metadata, not a build-time constant

- The current game install is **1.07**, but the project must work against historical versions in `F:\Crimson Desert\` and `X:\Crimson Desert\` for diffing.
- Code that parses a binary format should accept a version hint when ambiguous, and otherwise rely on auto-detection (`crimson-rs` already does this for skill format flags).
- See [game-versions.md](game-versions.md) for the exact layout.

## 8. Regenerate on demand, cache on disk

- Heavy extractions (iteminfo dump, paloc lookups) cache to disk under `data/<version>/` after extraction.
- Cache files include the source-file SHA in their header/manifest so we can detect staleness automatically rather than trusting timestamps.
- A `--force` flag on each extractor bypasses the cache. No silent reuse of cache files that don't match the source.
