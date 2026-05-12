#requires -Version 7

<#
.SYNOPSIS
    Produce a Native AOT release bundle of CrimsonAtomtic.

.DESCRIPTION
    1. Build vendor/crimson-rs with the c_abi feature -> crimson_rs.dll
       (via .\scripts\build_rust.ps1).
    2. dotnet publish -c Release -r <Runtime> -p:PublishAot=true on the
       Avalonia UI project. The Ui csproj copies crimson_rs.dll next to
       the AOT exe automatically (CopyToPublishDirectory=PreserveNewest).
    3. Stage the published directory under dist/<Runtime>/.
    4. Print a summary of what was produced.

    No zip step yet — the user's distribution flow may want signing /
    code-signing in between. Keep it explicit for now.

.PARAMETER Runtime
    .NET runtime identifier. Defaults to win-x64. Cross-target with
    linux-x64 / osx-arm64 once the C ABI is built for those platforms.

.PARAMETER SkipRustBuild
    Skip the cargo step. Useful when iterating on the C# side and the
    Rust dll is already current.

.EXAMPLE
    .\scripts\package_aot.ps1
    .\scripts\package_aot.ps1 -SkipRustBuild
#>

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$SkipRustBuild
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$UiProj      = Join-Path $ProjectRoot "src\CrimsonAtomtic.Ui\CrimsonAtomtic.Ui.csproj"
$DistRoot    = Join-Path $ProjectRoot "dist\$Runtime"

if (-not (Test-Path $UiProj)) {
    throw "UI project not found at $UiProj."
}

# ── 1. Rust core ────────────────────────────────────────────────────────────
if (-not $SkipRustBuild) {
    Write-Host "==> Building Rust core (vendor/crimson-rs --features c_abi --release)" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build_rust.ps1") -Profile release
    if ($LASTEXITCODE -ne 0) { throw "build_rust.ps1 failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "==> Skipping Rust build (per -SkipRustBuild)" -ForegroundColor Yellow
}

$DllSource = Join-Path $ProjectRoot "vendor\crimson-rs\target\release\crimson_rs.dll"
if (-not (Test-Path $DllSource)) {
    throw "Expected crimson_rs.dll at $DllSource. Run without -SkipRustBuild."
}

# ── 2. dotnet publish (AOT) ─────────────────────────────────────────────────
Write-Host ""
Write-Host "==> dotnet publish -c Release -r $Runtime -p:PublishAot=true" -ForegroundColor Cyan
Push-Location $ProjectRoot
try {
    if (Test-Path $DistRoot) {
        Remove-Item -Recurse -Force $DistRoot
    }
    New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null

    & dotnet publish $UiProj `
        --configuration Release `
        --runtime $Runtime `
        -p:PublishAot=true `
        -p:PublishTrimmed=true `
        --output $DistRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

# ── 3. Summary ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Bundle staged at $DistRoot" -ForegroundColor Green

$exe = Get-ChildItem $DistRoot -Filter "CrimsonAtomtic.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
$dll = Get-ChildItem $DistRoot -Filter "crimson_rs.dll"     -ErrorAction SilentlyContinue | Select-Object -First 1
if ($exe) {
    Write-Host ("    CrimsonAtomtic.exe : {0:N1} MB" -f ($exe.Length / 1MB))
} else {
    Write-Warning "    CrimsonAtomtic.exe not found in the publish output."
}
if ($dll) {
    Write-Host ("    crimson_rs.dll     : {0:N1} MB" -f ($dll.Length / 1MB))
} else {
    Write-Warning "    crimson_rs.dll not found in the publish output."
}

$totalBytes = (Get-ChildItem $DistRoot -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("    total              : {0:N1} MB ({1} files)" -f ($totalBytes / 1MB), (Get-ChildItem $DistRoot -Recurse -File).Count)
