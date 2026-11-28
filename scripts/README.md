# scripts/

Build / setup / package scripts. The day-to-day entry point is `build.ps1`
(or `build.cmd`) at the repo root, which calls the scripts below; see the
root [README.md](../README.md#first-time-setup) for its modes and targets.

| Script | Purpose |
| --- | --- |
| `paths.ps1` | Shared paths, dot-sourced by the other scripts: the vendor clone, `target\release` and its artifacts, `vendor\crimson-rs.ref`, `build_number.txt`, the Ui and Tests projects, and the supported runtimes (`win-x64` only). `Directory.Build.props` names the same `target\release` folder for MSBuild. |
| `build_rust.ps1` | `cargo build --locked --release --features c_abi` in `vendor\crimson-rs` (no parameters). One build makes `crimson_rs.dll` (dev / tests) and `crimson_rs.lib` (linked into the AOT exe), and writes the vendor commit to `target\release\.crimson-rs-sha`; the C# build warns when that differs from `vendor\crimson-rs.ref`. Windows only. |
| `build_ui.ps1` | Restore and build the C# solution (`-Configuration Debug` default, or `Release`); `-Test` runs the tests, `-Run` starts the app, `-NoRestore` skips the restore. Does not build Rust. |
| `package_aot.ps1` | Native AOT publish to `dist\win-x64\`: builds Rust unless `-SkipRustBuild`, publishes with `-BuildNumber` as the version's fourth part (default: `build_number.txt`), strips `.pdb` files, then fails if `crimson_rs.dll` is in the output, if the exe imports it, or if dumpbin cannot be found (on PATH, else through vswhere). Zipping is left to the caller. |
| `setup_python_env.ps1` | Create `.venv\` (Python 3.12+), install maturin, pytest and ruff at the versions in `python-constraints.txt`, and run `maturin develop --release` in `vendor\crimson-rs` with `CARGO_TARGET_DIR` set to `target\py`, so the PyO3 build never overwrites the C ABI DLL. `-Force` recreates the venv. |
| `python-constraints.txt` | Exact versions of the Python dev tools (`pip install -c`). |
| `checkout_crimson_rs.ps1` | **CI only.** Fetches the one commit in `vendor\crimson-rs.ref` into `vendor\crimson-rs` and checks HEAD matches. Locally, use `vendor\update_vendors.ps1`. |

Related, outside this folder:

- `vendor\update_vendors.ps1` clones or refreshes `vendor\crimson-rs` from
  `github.com/bbfox0703/crimson-rs` `main` and writes the commit to the
  committed `vendor\crimson-rs.ref`, which CI builds. It refuses to refresh a
  clone with local changes or local commits unless `-Force`.
- `global.json` pins the .NET SDK exactly (`rollForward: disable`).
- `.github/workflows/ci.yml` builds and tests on every pull request and every
  push to `dev` / `main`; `release.yml` builds the release on a `v*` tag.

All scripts:

- Run under PowerShell 7 (`pwsh`); most declare `#requires -Version 7.2`.
- Are idempotent — re-running them does the right thing.
- Exit non-zero on failure; never swallow errors silently.

## Order for a fresh dev machine

```powershell
# 1. Fetch vendored deps (writes vendor\crimson-rs.ref)
.\vendor\update_vendors.ps1

# 2. Build the Rust core and the C# solution
.\build.ps1

# 3. For the Python tools: venv + the PyO3 module (independent of step 2)
.\scripts\setup_python_env.ps1
```

After every vendor refresh, re-run both step 2 and step 3: neither the DLL
nor the Python module follows the vendor clone on its own.

## Running the tests

```powershell
dotnet run --project src/CrimsonAtomtic.Tests/CrimsonAtomtic.Tests.csproj -c Debug
python -m pytest tools/tests
```

`dotnet test` does not work: the .NET 10 SDK dropped the VSTest bridge.
