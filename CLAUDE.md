# CLAUDE.md

> **Minimal index.** Detailed specs, history, and rules live elsewhere — this
> file points to them.
>
> **🎯 Picking up a new session?** Read [docs/status.md](docs/status.md)
> first — it captures where we are, the next concrete task, and the
> gotchas worth not relearning.

## Rule 0 — Minimalism

This file follows the **minimalism principle**. Anything that needs more than
one line of context belongs in a referenced doc, not here. Before treating
this file as the source of truth, always check:

- [`docs/`](docs/) — architecture, format specs, policies
- Sub-tree `CLAUDE.md` files (e.g. [`tools/CLAUDE.md`](tools/CLAUDE.md))
- `README.md` in each folder

If a rule grows past one or two lines, move it to a referenced doc and leave a
one-line pointer here.

## Project

Crimson Desert save editor + game-data toolchain. Tracks the live game install
(currently **1.09**) and stays compatible with 1.05 / 1.06 saves via schema
auto-detection. Cross-platform goal: Windows (must), Linux, macOS.

- High-level architecture: [docs/architecture.md](docs/architecture.md)
- Data hygiene policy: [docs/data-policy.md](docs/data-policy.md)
- UI/UX principles: [docs/ui-design.md](docs/ui-design.md)

## Stack

- **App / UI**: Avalonia UI 12 on .NET 10, published as Native AOT trimmed binary
- **Native core**: Rust — [`vendor/crimson-rs`](vendor/crimson-rs/) with PyO3 (for tools) **and** C ABI (for the AOT app)
- **Tooling**: Python 3.12+ under [`tools/`](tools/) — extraction, diff, inspect, analyze
- **Tests**: xUnit 3 (C#), pytest (Python), `cargo test` (Rust)

## Mandatory rules

1. **Language**: code, comments, UI strings in English. In-game item names use game-data language (see [docs/ui-design.md](docs/ui-design.md)).
2. **Single instance**: UI uses a Mutex.
3. **Async everywhere**: all I/O, IPC, alerts are async.
4. **Platform abstraction**: any OS-specific call (P/Invoke, registry, OS commands) goes through an interface in the `Core` project. `Core` itself contains no platform-specific code.
5. **AOT-safe C#**: no reflection-based APIs. Source generators only (`[JsonSerializable]`, `[ObservableProperty]`, etc.).
6. **Logs**: `%LOCALAPPDATA%\CrimsonAtomtic\Logs\<category>\<process>\`. 4-file rotation, 8 MB max each. Root `Logs/` has only subfolders, no loose files.
7. **Magic strings**: one centralised file per project, well-commented.
8. **Vendor deps**: cloned into [`vendor/`](vendor/), never git submodules. `vendor/<name>/` is **read-only** — any change to `crimson-rs` is committed at the source repo `D:\Github\crimson-rs` first, then mirrored in via [`vendor/update_vendors.ps1`](vendor/update_vendors.ps1) (which does `reset --hard origin/dev` and silently wipes any local edits). See [`vendor/README.md`](vendor/README.md).
9. **Python tools**: every `.py` prints usage + exits with code 2 when called with no args. No side effects at import time. See [tools/CLAUDE.md](tools/CLAUDE.md).
10. **No derived data committed**: if file B is generated from A, only A is committed. B is in `.gitignore` and the generator is documented in the relevant `README.md`. See [docs/data-policy.md](docs/data-policy.md).
11. **`CRIMSON-DESERT-SAVE-EDITOR` is one-time-mining only**: `D:\Github\CRIMSON-DESERT-SAVE-EDITOR` may be mined once for images / parser ideas; the new project must not depend on it going forward. The only ongoing external dependency is `vendor/crimson-rs`.
12. **Foundation over workaround**: when parsing produces wrong data, fix the parser or schema, not the consumer. See [docs/data-policy.md](docs/data-policy.md).

## Workflow rules

- **Build verification**: after code changes, fully rebuild and inspect the actual build output before claiming success.
- **Refactoring**: when asked to refactor/rename, change the code (move files, update imports, rename classes) — not just docs.
- **Debugging**: verify fixes against actual memory layout / data structure; if the first attempt fails, re-examine fundamental assumptions before iterating.
- **PRs**: before `gh pr create`, run `git status` and `git log --oneline -5`; resolve any divergence.
- **Cheat Engine Lua**: verify each API exists in the CE Lua reference before using it; do not invent calls.

## Pointers

- Architecture decision (A1, Avalonia + crimson-rs C ABI): [docs/architecture.md](docs/architecture.md)
- Game install + save paths, version history: [docs/game-versions.md](docs/game-versions.md)
- `.save` format (header, ChaCha20, HMAC, LZ4): [docs/save-format.md](docs/save-format.md)
- Decompressed save body (schema + TOC + decoder): [docs/save-body-format.md](docs/save-body-format.md)
- PABGB format family (iteminfo, skill, store, field, …): [docs/pabgb-formats.md](docs/pabgb-formats.md)
- Asset containers (PAZ, PAMT, PAPGT, PALOC): [docs/paz-containers.md](docs/paz-containers.md)
- CrimsonForge coverage gaps (the canonical RE-reference repo for CD formats): [docs/crimsonforge-coverage-gaps.md](docs/crimsonforge-coverage-gaps.md)
- Python toolchain conventions: [tools/CLAUDE.md](tools/CLAUDE.md)
- Rust core (our fork): [vendor/crimson-rs/README.md](vendor/crimson-rs/README.md) (after first vendor refresh)
