#requires -Version 7

<#
.SYNOPSIS
    Build the Rust core in vendor/crimson-rs.

.DESCRIPTION
    Wraps `cargo build` for the standalone library targets. Note: the
    Python extension build is handled by maturin via setup_python_env.ps1;
    this script is for producing the C ABI cdylib once that target exists.

.PARAMETER Profile
    cargo profile: "release" (default) or "debug".
#>

[CmdletBinding()]
param(
    [ValidateSet("release", "debug")]
    [string]$Profile = "release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$VendorRs    = Join-Path $ProjectRoot "vendor\crimson-rs"

if (-not (Test-Path $VendorRs)) {
    throw "vendor/crimson-rs is missing. Run .\vendor\update_vendors.ps1 first."
}

Push-Location $VendorRs
try {
    $args = @("build")
    if ($Profile -eq "release") { $args += "--release" }

    Write-Host "Running: cargo $($args -join ' ') (in $VendorRs)" -ForegroundColor Cyan
    & cargo @args
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

$TargetDir = Join-Path $VendorRs "target\$Profile"
Write-Host ""
Write-Host "Build output in: $TargetDir" -ForegroundColor Green
Get-ChildItem $TargetDir -Filter "crimson_rs*" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1KB, 1)) KB)" }
