#requires -Version 7

<#
.SYNOPSIS
    Build vendor/crimson-rs with the C ABI feature.

.DESCRIPTION
    Wraps `cargo build --features c_abi` against the vendor copy of
    crimson-rs. The resulting cdylib (crimson_rs.dll on Windows) exposes
    the `crimson_save_*` extern "C" surface that the C# RustInterop
    project P/Invokes into.

    Note: the Python extension build is handled by maturin via
    setup_python_env.ps1. This script is the C# side.

.PARAMETER Profile
    cargo profile: "release" (default) or "debug".

.EXAMPLE
    .\scripts\build_rust.ps1
    .\scripts\build_rust.ps1 -Profile debug
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
    $cargoArgs = @("build", "--features", "c_abi")
    if ($Profile -eq "release") { $cargoArgs += "--release" }

    Write-Host "Running: cargo $($cargoArgs -join ' ') (in $VendorRs)" -ForegroundColor Cyan
    & cargo @cargoArgs
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

$TargetDir = Join-Path $VendorRs "target\$Profile"
$DllPath   = Join-Path $TargetDir "crimson_rs.dll"

Write-Host ""
if (Test-Path $DllPath) {
    $sizeKb = [math]::Round((Get-Item $DllPath).Length / 1KB, 1)
    Write-Host "Built: $DllPath ($sizeKb KB)" -ForegroundColor Green
    Write-Host ""
    Write-Host "The C# Ui + Tests projects reference this path directly via" -ForegroundColor DarkGray
    Write-Host "a <Content Include=...> item with CopyToOutputDirectory, so" -ForegroundColor DarkGray
    Write-Host "subsequent 'dotnet build' / 'dotnet test' picks it up." -ForegroundColor DarkGray
} else {
    Write-Warning "Expected $DllPath but the file was not produced."
    exit 1
}
