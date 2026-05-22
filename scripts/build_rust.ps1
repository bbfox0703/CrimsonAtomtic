#requires -Version 7

<#
.SYNOPSIS
    Build vendor/crimson-rs with the C ABI feature.

.DESCRIPTION
    Wraps `cargo build --features c_abi` against the vendor copy of
    crimson-rs. One build produces both artifacts the C# side needs:

      - crimson_rs.dll  : cdylib for dev / dotnet run / dotnet test
                          (LoadLibrary path; tests aren't AOT-published)
      - crimson_rs.lib  : staticlib for AOT publish (folded into
                          CrimsonAtomtic.exe via <DirectPInvoke> +
                          <NativeLibrary> in CrimsonAtomtic.Ui.csproj —
                          so dist/ ships a single self-contained exe)

    Both are emitted from the same `cargo build` because
    vendor/crimson-rs/Cargo.toml declares
    `crate-type = ["cdylib", "staticlib"]`. See
    vendor/crimson-rs/docs/c-sharp-nativeaot-integration.md.

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
$LibPath   = Join-Path $TargetDir "crimson_rs.lib"

Write-Host ""
$missing = @()
if (Test-Path $DllPath) {
    $dllKb = [math]::Round((Get-Item $DllPath).Length / 1KB, 1)
    Write-Host "Built: $DllPath ($dllKb KB)" -ForegroundColor Green
} else {
    Write-Warning "Expected $DllPath but the file was not produced."
    $missing += "crimson_rs.dll"
}
if (Test-Path $LibPath) {
    $libMb = [math]::Round((Get-Item $LibPath).Length / 1MB, 1)
    Write-Host "Built: $LibPath ($libMb MB)" -ForegroundColor Green
} else {
    Write-Warning "Expected $LibPath but the file was not produced. Check vendor/crimson-rs/Cargo.toml has crate-type = [\"cdylib\", \"staticlib\"]."
    $missing += "crimson_rs.lib"
}
if ($missing.Count -gt 0) {
    exit 1
}

Write-Host ""
Write-Host "Dev / 'dotnet build' / 'dotnet test' pick up crimson_rs.dll via" -ForegroundColor DarkGray
Write-Host "the <Content Include=...> item in the Ui + Tests csprojs." -ForegroundColor DarkGray
Write-Host "AOT publish picks up crimson_rs.lib via <DirectPInvoke> +" -ForegroundColor DarkGray
Write-Host "<NativeLibrary> in CrimsonAtomtic.Ui.csproj — dist/ ships as" -ForegroundColor DarkGray
Write-Host "a single .exe with no crimson_rs.dll alongside." -ForegroundColor DarkGray
