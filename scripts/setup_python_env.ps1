#requires -Version 7

<#
.SYNOPSIS
    Bootstrap the Python toolchain virtual environment.

.DESCRIPTION
    Creates .venv\ at the project root, installs maturin + tools[dev], and
    runs `maturin develop --release` against vendor/crimson-rs so the
    `crimson_rs` Python module is importable from the venv.

    Requires Python 3.12+ on PATH (matching crimson-rs's abi3-py312 wheel
    contract) and the Rust toolchain (cargo, rustc).

.PARAMETER PythonExe
    Override the Python interpreter used to create the venv.
    Default: the first `py -3.12` (Windows) or `python3.12` on PATH.

.PARAMETER Force
    Recreate .venv\ from scratch even if it already exists.
#>

[CmdletBinding()]
param(
    [string]$PythonExe = $null,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$VenvDir     = Join-Path $ProjectRoot ".venv"
$VendorRs    = Join-Path $ProjectRoot "vendor\crimson-rs"

if (-not (Test-Path $VendorRs)) {
    throw "vendor/crimson-rs is missing. Run .\vendor\update_vendors.ps1 first."
}

# Resolve Python 3.12+ launcher. We split into command + args so PowerShell
# can splat them correctly via `& $cmd @args`.
$LauncherCmd = $null
$LauncherArgs = @()
if ($PythonExe) {
    # User override: assume it's a single bare command.
    $LauncherCmd  = $PythonExe
    $LauncherArgs = @()
} elseif (Get-Command "py" -ErrorAction SilentlyContinue) {
    $LauncherCmd  = "py"
    $LauncherArgs = @("-3.12")
} elseif (Get-Command "python3.12" -ErrorAction SilentlyContinue) {
    $LauncherCmd  = "python3.12"
} elseif (Get-Command "python" -ErrorAction SilentlyContinue) {
    $LauncherCmd  = "python"
} else {
    throw "No Python interpreter found. Install Python 3.12+ and re-run."
}

Write-Host "Using Python launcher: $LauncherCmd $($LauncherArgs -join ' ')" -ForegroundColor Cyan

if ($Force -and (Test-Path $VenvDir)) {
    Write-Host "Removing existing .venv\ (-Force)" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $VenvDir
}

if (-not (Test-Path $VenvDir)) {
    Write-Host "Creating .venv\..." -ForegroundColor Cyan
    & $LauncherCmd @LauncherArgs -m venv $VenvDir
    if ($LASTEXITCODE -ne 0) { throw "venv creation failed" }
}

$VenvPy = Join-Path $VenvDir "Scripts\python.exe"
if (-not (Test-Path $VenvPy)) {
    $VenvPy = Join-Path $VenvDir "bin\python"  # Linux/macOS fallback
}

Write-Host "Upgrading pip..." -ForegroundColor Cyan
& $VenvPy -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed" }

Write-Host "Installing maturin..." -ForegroundColor Cyan
& $VenvPy -m pip install "maturin>=1.0,<2.0"
if ($LASTEXITCODE -ne 0) { throw "maturin install failed" }

Write-Host "Building crimson_rs Python module from vendor/crimson-rs..." -ForegroundColor Cyan
Push-Location $VendorRs
try {
    & $VenvPy -m maturin develop --release
    if ($LASTEXITCODE -ne 0) { throw "maturin develop failed" }
}
finally {
    Pop-Location
}

Write-Host "Installing dev tooling (pytest, ruff)..." -ForegroundColor Cyan
& $VenvPy -m pip install "pytest>=8.0" "ruff>=0.5"
if ($LASTEXITCODE -ne 0) { throw "dev tooling install failed" }
# Scripts under tools/ use a sys.path bootstrap to import tools/common/,
# so we deliberately do NOT `pip install -e tools/` — see tools/CLAUDE.md.

Write-Host ""
Write-Host "Done. Activate with:" -ForegroundColor Green
Write-Host "  .\.venv\Scripts\Activate.ps1"
Write-Host "Sanity check:"
Write-Host "  python -c `"import crimson_rs; print(crimson_rs)`""
