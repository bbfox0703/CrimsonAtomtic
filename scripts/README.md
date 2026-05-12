# scripts/

Top-level build / setup / package scripts.

| Script                    | Purpose                                                       |
| ------------------------- | ------------------------------------------------------------- |
| `setup_python_env.ps1`    | Create `.venv\`, install maturin + tools deps, build `crimson_rs` for Python |
| `build_rust.ps1`          | Build `vendor/crimson-rs` (release + cdylib for C ABI when present) |
| `build_ui.ps1`            | Build the Avalonia app (`src/CrimsonAtomtic.Ui`) — stub until `src/` exists |
| `package_aot.ps1`         | Produce a Native AOT trimmed release bundle — stub            |

All scripts:

- Use `#requires -Version 7` and PowerShell 7+ syntax (pwsh).
- Are idempotent — re-running them does the right thing.
- Print what they're doing in colour.
- Exit non-zero on failure; never swallow errors silently.

## Order for a fresh dev machine

```powershell
# 1. Fetch vendored deps
.\vendor\update_vendors.ps1

# 2. Build Rust core (also produces the .pyd for the Python tools)
.\scripts\build_rust.ps1

# 3. Set up Python env (depends on step 2 having produced crimson_rs)
.\scripts\setup_python_env.ps1

# 4. Build the app (once src/ exists)
.\scripts\build_ui.ps1
```
