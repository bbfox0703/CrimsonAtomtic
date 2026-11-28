# Architecture

## TL;DR

```
+------------------------------------------------------------+
| Avalonia UI 12 (net10.0, Native AOT)                       |
|   src/CrimsonAtomtic.Ui                                    |
|     Views / ViewModels / Services / Platform (Windows)     |
+-----------+-------------------------------+----------------+
            |                               |
            v                               v
+-----------------------------+   +--------------------------+
| src/CrimsonAtomtic.Core     |   | src/CrimsonAtomtic.      |
|   platform interfaces only  |   |   RustInterop            |
| src/CrimsonAtomtic.SaveModel|   |   ISaveLoader, catalogs, |
|   save records, scalar edit |   |   [LibraryImport] decls  |
+-----------------------------+   +------------+-------------+
                                               | C ABI ("crimson_rs")
                                               |  dev/tests: crimson_rs.dll
                                               |  AOT exe:   crimson_rs.lib
                                               v  (static link)
+------------------------------------------------------------+
| vendor/crimson-rs (Rust, our fork)                         |
|   save crypto + body decode/encode, PABGB tables,          |
|   PAZ/PAMT/PAPGT/PALOC archives, paver version stamp       |
+------------------------------------------------------------+
            ^ PyO3 (maturin, `python` feature)
            | + ctypes on the C ABI build for bridges PyO3 lacks
+-----------+----------+                  +-----------------+
| Python tooling       |                  | Game install    |
|   tools/             |                  | (read-only)     |
+----------------------+                  +-----------------+
```

## Path chosen: A1

**What:** Avalonia + .NET 10 Native AOT app, reaching a C ABI added to our
`crimson-rs` fork.

**When:** chosen when the project was scaffolded (2026-05-12) and still the
architecture today.

### Why A1

- **Performance and AOT**: matches the project rule that the deliverable is a Native AOT trimmed binary. A managed-only path (option A2: rewriting parsers in C#) would duplicate logic and break the byte-perfect roundtrip we already have in Rust.
- **Single source of truth for binary formats**: all parse/serialize logic stays in Rust (`vendor/crimson-rs`). The C# side never reinvents byte layouts.
- **Cross-platform potential**: Avalonia covers Windows / Linux / macOS, and Rust can build the same core for each. Only the Windows build exists today (see [Platform layer](#platform-layer)).
- **We own the Rust crate**: adding `extern "C"` exports is cheap because the upstream contract doesn't apply — we are downstream-of-ourselves.

### Why not B (Python + new UI) or C (Rust-native UI)

- **B (Python)** is the shortest path but conflicts with the AOT rule and brings the usual runtime-cost / packaging trade-offs of a Python UI app.
- **C (Rust UI)** is fastest at runtime but means a different UI toolchain (egui / Slint / Tauri) and more research. Kept as the fallback had Avalonia AOT proved blocking; it did not.

## Projects under `src/`

Project references point one way only: Core ← SaveModel ← RustInterop ← Ui,
with Tests referencing all four.

| Project | References | What is in it |
|---|---|---|
| `CrimsonAtomtic.Core` | — | Platform interfaces only: `IPlatformPaths` (with `SavePlatform` / `DiscoveredSaveRoot`), `ISingleInstanceGuard`, `IOsUiLanguage`, `IAppLog`. No implementations, no packages, no P/Invoke. |
| `CrimsonAtomtic.SaveModel` | Core | `SaveSummary` and the other save records with their `[JsonSerializable]` context, and `ScalarFieldEditing` (parses the C ABI's formatted scalar strings and builds the little-endian bytes a scalar write takes). Pure C#, no FFI. |
| `CrimsonAtomtic.RustInterop` | Core, SaveModel | The only project that calls crimson-rs: `ISaveLoader` / `NativeSaveLoader`, the gamedata catalogs (`NativeItemInfoCatalog`, `NativeStringInfoCatalog`, the key-info catalogs in `NativeKeyInfoCatalogs.cs`, the dye catalogs), `NativePalocCatalog` / `MultiPalocCatalog`, `NativePazExtractor`, `NativePaverReader`, `GameDataLayout`. `AllowUnsafeBlocks` for the `byte*` buffer signatures. |
| `CrimsonAtomtic.Ui` | Core, SaveModel, RustInterop | The Avalonia app (`PublishAot=true`, `TrimMode=full`, `InvariantGlobalization`, compiled bindings by default). `Views/`, `ViewModels/` (CommunityToolkit.Mvvm source generators), `Services/`, `Platform/`, `Resources/Strings/` (en / ja / zh-TW). |
| `CrimsonAtomtic.Tests` | all four | xUnit v3 on Microsoft.Testing.Platform. Unit tests, plus smoke tests against the real crimson-rs DLL, the live game install and live saves, which skip when those are absent. |

## Layers

### Rust core — `vendor/crimson-rs`

- One crate, `crate-type = ["cdylib", "staticlib"]`, with two optional
  features:
  - **`c_abi`** — the `extern "C"` surface (`crimson_*` symbols, sources in
    `src/c_abi/`) the C# app calls. Catalogued in crimson-rs
    `docs/c-abi-surface.md`.
  - **`python`** — the PyO3 module (`src/python.rs`) the tools import.
- The two are built separately, with different features, into different
  target dirs (see [Build outputs](#build-outputs)).
- It owns all format knowledge: save header crypto (ChaCha20 + HMAC) and LZ4,
  the embedded-schema body decoder and the write path, every PABGB /
  static-info table the editor resolves keys against, the PAZ / PAMT / PAPGT /
  PALOC archive formats, and the `meta/0.paver` version stamp. The parser's
  target game version (`ParserTargetMajor` / `Minor`) is read from the C ABI,
  not hard-coded in C#.

### C# RustInterop — `src/CrimsonAtomtic.RustInterop`

- Every C ABI entry point is a `[LibraryImport("crimson_rs", EntryPoint =
  "crimson_…")]` declaration in the `NativeMethods` class
  (`NativeSaveLoader.cs`). Marshalling is source-generated; there is no
  reflection and no `DllImport` on this path.
- Native objects come back as opaque handles wrapped in `SafeHandle`
  subclasses; data crosses as small records, accessor calls and the two-call
  buffer fill (ask for the size, then fill a caller buffer). Status codes are
  named in `CrimsonErrorCode`.
- Public surface is interfaces where the UI needs a seam (`ISaveLoader`,
  `IItemInfoCatalog`, `IStringInfoCatalog`, `IPalocCatalog`,
  `IPazExtractor`) and sealed `Native*` classes otherwise.

### C# SaveModel — `src/CrimsonAtomtic.SaveModel`

- Two files: the save summary records and the scalar-edit helpers. The edit
  state itself lives in the Rust save handle behind `ISaveLoader`; there is
  no separate C# edit model and no undo / redo. What the user changed since
  the last save is listed by `Services/ChangeJournal.cs` in the UI.

### Avalonia UI — `src/CrimsonAtomtic.Ui`

- `App.axaml.cs` is the composition root: it creates `NativeSaveLoader`,
  `NativePazExtractor`, `LocalizationProvider`, `WindowsPlatformPaths`, the
  file log, settings, the UI language service and the backup service, and
  hands them to `MainWindowViewModel`.
- ViewModels talk to RustInterop directly. `MainWindowViewModel` takes an
  `ISaveLoader`, and twelve ViewModels import `CrimsonAtomtic.RustInterop`
  (for `ISaveLoader`, the catalogs and their record types). That is the
  current design: RustInterop's interfaces are the seam, not SaveModel.
- `Services/` holds app logic that is not a view: settings
  (`AppSettings.cs`), save backups (`SaveBackupService.cs`), localization and
  icon / portrait caches, the change journal, the write gate, window
  placement, and the file log.
- `Program.cs` holds the single-instance guard (a `Local\` named Mutex on
  Windows) and starts Avalonia.

### Python tooling — `tools/`

- For game-data extraction, cross-version diffing, ad-hoc inspection, and
  analysis when the game patches. Inventory in [tools/README.md](../tools/README.md);
  conventions in [tools/CLAUDE.md](../tools/CLAUDE.md).
- Calls into `crimson-rs` through the PyO3 module (`import crimson_rs`),
  installed into `.venv\` by `scripts/setup_python_env.ps1` (`maturin develop
  --release`). A tool that needs a bridge PyO3 does not expose loads the C
  ABI build through ctypes; `tools/common/crimson_abi.py` resolves it to the
  same `target/release` DLL the C# side uses.
- Never re-implements format parsing.

## Platform layer

Rule 4: OS-specific calls go through interfaces in `Core`, which itself has
no platform code.

| Interface (Core) | Implementation (Ui) | Notes |
|---|---|---|
| `IPlatformPaths` | `Platform/WindowsPlatformPaths.cs` | `%LOCALAPPDATA%\CrimsonAtomtic`, the log root, Steam / Epic / Game Pass save roots, game install discovery (with `SteamLibraryProbe` and `EpicManifestProbe`). |
| `IOsUiLanguage` | `Platform/OsUiLanguage.cs` | `WindowsOsUiLanguage` (`GetUserDefaultUILanguage`, because `InvariantGlobalization` leaves `CultureInfo` empty) and `EnvironmentOsUiLanguage` (POSIX locale variables). |
| `IAppLog` | `Services/FileAppLog.cs` | Rule 6 layout and rotation; not OS-specific. |
| `ISingleInstanceGuard` | `Program.cs` | Named Mutex on Windows; a no-op guard elsewhere. |

Only the Windows layer exists. `App` throws `PlatformNotSupportedException`
on any other OS at startup, and the build scripts accept only `win-x64`.

## Build outputs

| Output | Built by | Used by |
|---|---|---|
| `vendor/crimson-rs/target/release/crimson_rs.dll` | `scripts/build_rust.ps1` (`cargo build --locked --release --features c_abi`) | `dotnet build` / `dotnet run` and the tests: the Ui and Tests projects copy it next to their output, and `[LibraryImport]` loads it at run time. |
| `vendor/crimson-rs/target/release/crimson_rs.lib` | same build (staticlib) | The AOT publish: `<DirectPInvoke Include="crimson_rs" />` plus `<NativeLibrary>` in the Ui csproj link it into `CrimsonAtomtic.exe`. `dist/win-x64/` ships no `crimson_rs.dll`, and `scripts/package_aot.ps1` fails the publish if one appears or the exe imports it. |
| `vendor/crimson-rs/target/release/.crimson-rs-sha` | `build_rust.ps1` | `Directory.Build.targets` warns when it differs from `vendor/crimson-rs.ref`. |
| `vendor/crimson-rs/target/py/` + the in-tree `crimson_rs.pyd` | `scripts/setup_python_env.ps1` (maturin, `python` feature) | The Python tools. Kept apart from `target/release` so the PyO3 build never overwrites the C ABI DLL. |

`CrimsonRsTargetDir` in `Directory.Build.props` and `scripts/paths.ps1` name
the same `target\release` folder. Build commands are in
[scripts/README.md](../scripts/README.md).

## Crimson-rs sync model

- The user maintains the source-of-truth Rust crate at `D:\Github\crimson-rs`,
  pushed to `github.com/bbfox0703/crimson-rs`.
- `vendor/crimson-rs/` is a clone of that repo's `main`, ignored by this
  repo's git and refreshed by [vendor/update_vendors.ps1](../vendor/update_vendors.ps1).
  The script records the commit in the committed `vendor/crimson-rs.ref`;
  CI (`scripts/checkout_crimson_rs.ps1`) builds exactly that commit.
- Never edit or commit under `vendor/crimson-rs/` from this project. Rust
  changes land in crimson-rs first, then come in through a vendor refresh.
- The AOT exe's product version carries the first seven characters of that
  ref (`+crimson-rs.<sha>`), so a shipped build names the core it links.

## Current state

For what shipped, release by release, see [dev-log.md](dev-log.md); for the
current version, test health and backlog see [status.md](status.md).

## Open questions / future work

- Linux / macOS: needs an `IPlatformPaths` and single-instance guard per OS,
  the Rust artifacts for that RID (`libcrimson_rs.a` and the OS's link
  libraries in the Ui csproj), and a save-path answer (probably the
  Wine / Proton prefix only).
- `Avalonia.Diagnostics` is not referenced: it had no 12.x release when last
  checked (see the note in `Directory.Packages.props`).
- `Avalonia.Controls.DataGrid` ships on its own cadence (12.1.2 while core is
  12.2.0), so the two versions are not expected to match.
