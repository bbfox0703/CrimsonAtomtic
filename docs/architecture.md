# Architecture

## TL;DR

```
+----------------------------------------------------+
| Avalonia UI 12 (net10.0, Native AOT)               |
|   src/CrimsonAtomtic.Ui                            |
+-------------------------+--------------------------+
                          | source generators only
+-------------------------v--------------------------+
| C# Core (platform-abstracted, AOT-safe)            |
|   src/CrimsonAtomtic.Core                          |
|   src/CrimsonAtomtic.SaveModel                     |
+-------------------------+--------------------------+
                          | DllImport (P/Invoke)
+-------------------------v--------------------------+
| crimson_rs.dll (C ABI, cdylib)                     |
|   vendor/crimson-rs (Rust, OUR fork)               |
|   - Save crypto / parse                            |
|   - PABGB family parse/serialize                   |
|   - PAZ/PAMT/PAPGT/PALOC archive ops               |
|   - ChaCha20, Jenkins hashlittle2                  |
+----------------------------------------------------+
        ^                                  ^
        | PyO3                             | (none)
+-------+--------+                  +------+----------+
| Python tooling |                  | Game files      |
|   tools/       |                  | D:\Steam... CD\ |
+----------------+                  +-----------------+
```

## Path chosen: A1

**Avalonia + .NET 10 AOT app + a C ABI added to our `crimson-rs` fork.**

### Why A1

- **Performance and AOT**: matches the project rule that the deliverable is a Native AOT trimmed binary. A managed-only path (option A2: rewriting parsers in C#) would duplicate logic and break byte-perfect roundtrip we already have in Rust.
- **Single source of truth for binary formats**: all parse/serialize logic stays in Rust (`vendor/crimson-rs`). The C# side never reinvents byte layouts.
- **Cross-platform**: Avalonia covers Windows / Linux / macOS. Rust produces `.dll` / `.so` / `.dylib` from the same source.
- **We own the Rust crate**: adding `extern "C"` exports is cheap because the upstream contract doesn't apply — we are downstream-of-ourselves.

### Why not B (Python + new UI) or C (Rust-native UI)

- **B (Python)** is the shortest path but conflicts with the AOT rule and brings the usual runtime-cost / packaging trade-offs of a Python UI app.
- **C (Rust UI)** is fastest at runtime but means a different UI toolchain (egui / Slint / Tauri) and more research. Reserved as a fallback if Avalonia AOT proves blocking.

## Layers

### Rust core — `vendor/crimson-rs`

- Cargo crate, `crate-type = ["cdylib"]`.
- Two integration surfaces:
  - **PyO3** (already exists) — consumed by `tools/`.
  - **C ABI** (new, to add) — feature-gated `extern "C"` exports for the C# app.
- New responsibility: **save file crypto + parser** (ChaCha20 + HMAC + LZ4
  header, plus TOC / items / stats decoding) lives in this crate, not in C#.

### C# Core — `src/CrimsonAtomtic.Core`

- Platform abstractions: filesystem paths, registry/credential storage, process model, OS-version checks.
- Pure interfaces; **no** direct P/Invoke or registry access inside `Core`.

### C# RustInterop — `src/CrimsonAtomtic.RustInterop`

- `DllImport` wrappers around the C ABI of `crimson_rs.dll`.
- Marshal-by-pointer; we return opaque handles + accessor functions, not big structs across the FFI boundary, to keep ABI stability simple.
- See `docs/c-abi.md` (to be created when we start work on the C ABI).

### C# SaveModel — `src/CrimsonAtomtic.SaveModel`

- High-level domain types (Character, InventorySlot, Bag, etc.).
- Owns the in-memory edit model + undo/redo. Pure C# — no FFI calls here.

### Avalonia UI — `src/CrimsonAtomtic.Ui`

- AXAML views + ViewModels (CommunityToolkit.Mvvm source generators).
- Strict separation: ViewModels know nothing about FFI; they talk to SaveModel.

### Python tooling — `tools/`

- For game-data extraction, cross-version diffing, ad-hoc inspection, and analysis when the game patches.
- Calls into `crimson-rs` via PyO3, never re-implements format parsing.
- Conventions in [tools/CLAUDE.md](../tools/CLAUDE.md).

## Crimson-rs sync model

- The user maintains the source-of-truth Rust crate at `D:\Github\crimson-rs`.
- `vendor/crimson-rs/` in this repo is a refreshable clone, ignored by parent git, updated via [vendor/update_vendors.ps1](../vendor/update_vendors.ps1).
- Never commit anything under `vendor/crimson-rs/` from this project. All Rust changes flow through the upstream-of-vendor repo.

## Current scaffolding (2026-05)

`src/` now contains the 5 projects shown above:

- **CrimsonAtomtic.Core** — platform abstractions (`IPlatformPaths`,
  `ISingleInstanceGuard`, settings persistence, save backup retention).
  No direct P/Invoke or registry access lives here.
- **CrimsonAtomtic.SaveModel** — domain records, `[JsonSerializable]`
  source-generated context for AOT-safe serialization, and the in-memory
  edit model.
- **CrimsonAtomtic.RustInterop** — `[LibraryImport]` / `SafeHandle`
  wrappers around the C ABI in `vendor/crimson-rs`. Surface today:
  `ISaveLoader` + `NativeSaveLoader` (save load / scalar+path mutations /
  mutation-version cache / inventory enumeration / write-to-file /
  **deferred-redecode batch** for bulk-mutation flows),
  `IItemInfoCatalog`, `NativeSkillInfoCatalog`, `NativeCharacterInfoCatalog`,
  `NativeKnowledgeInfoCatalog`, `NativeGimmickInfoCatalog`,
  `NativeStoreInfoCatalog`, the three dye catalogs, plus
  `NativePalocCatalog` and `NativePazExtractor`. Source-generated
  marshalling, no reflection.
- **CrimsonAtomtic.Ui** — Avalonia 12 / .NET 10 app with `PublishAot=true`,
  `BuiltInComInteropSupport=false`, `AvaloniaUseCompiledBindingsByDefault=true`.
  Mutex-based single-instance guard on Windows. Left-rail navigator over
  the loaded save, per-block field editor with typed validation +
  present/absent toggle + close-on-dirty `ChangeJournal` confirm. Tools
  menu covers Find Items, Browse Items / Characters, Sockets editor,
  Dye editor, Mercenary rename, Abyss Gates (bulk + per-gate), Sealed
  Abyss Artifact challenges (per-row + bulk via deferred-redecode
  batch, ~0.1 s for the full 141-artifact sweep), Vendor Buyback,
  and the icon / portrait caches under `%LOCALAPPDATA%\CrimsonAtomtic\`.
- **CrimsonAtomtic.Tests** — xUnit 3 covering the RustInterop wrappers,
  the catalog bridges, and the save loader against a live save (skips
  gracefully when none is present). Current count: **190/190 pass**.

For the running ledger of what shipped per session see
[status.md](status.md).

## Open questions / future work

- Cross-platform save path on Linux/macOS: probably Wine/Proton prefix
  only. Document once we test.
- Avalonia.Diagnostics 12.x doesn't exist yet (only 11.3.15). Add back
  behind a Debug-only condition once Avalonia ships the 12.x version.
- DataGrid lags Avalonia core (12.0.0 vs 12.0.3 for core).
