#requires -Version 7

<#
.SYNOPSIS
    CrimsonAtomtic unified build entry point — Rust DLL + C# Avalonia UI.

.DESCRIPTION
    Single command for every build flavour the project ships:

      - Debug    : Unoptimized cargo + dotnet build (fast iteration)
      - Release  : Optimized cargo --release + dotnet build (default)
      - Publish  : Release cargo + dotnet publish -p:PublishAot=true to
                   dist\<rid>\  (matches the old scripts\package_aot.ps1)

    Delegates to the legacy per-step scripts under scripts\ so anyone
    used to those still gets the same behaviour:

      - scripts\build_rust.ps1  - Rust DLL build
      - scripts\build_ui.ps1    - C# build / test
      - scripts\package_aot.ps1 - AOT publish

    The entry-point pattern mirrors UE5CEDumper's build.cmd / build.ps1
    so muscle memory carries across both projects.

.PARAMETER Mode
    Debug, Release (default), or Publish.

.PARAMETER Target
    All (default), DLL (Rust only), UI (C# only), or Test (run xUnit).

.PARAMETER Clean
    Wipe dist\ + bin\ + obj\ before building.

.PARAMETER Runtime
    .NET runtime identifier for Publish. Default: win-x64.

.EXAMPLE
    .\build.ps1                          # Release build, all targets
    .\build.ps1 -Mode Debug              # Debug build
    .\build.ps1 -Mode Publish            # AOT publish
    .\build.ps1 -Mode Publish -Clean     # Clean + publish
    .\build.ps1 -Target DLL              # Rust DLL only
    .\build.ps1 -Target Test             # Build + run xUnit suite
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Publish")]
    [string]$Mode = "Release",

    [ValidateSet("All", "DLL", "UI", "Test")]
    [string]$Target = "All",

    [switch]$Clean,

    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$Scripts     = Join-Path $ProjectRoot "scripts"
$VendorRs    = Join-Path $ProjectRoot "vendor\crimson-rs"
$Sln         = Join-Path $ProjectRoot "CrimsonAtomtic.slnx"
if (-not (Test-Path $Sln)) { $Sln = Join-Path $ProjectRoot "CrimsonAtomtic.sln" }
if (-not (Test-Path $Sln)) {
    throw "Solution file not found at $ProjectRoot (looked for .slnx + .sln)."
}

function Write-Banner([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Step([string]$Name, [scriptblock]$Block) {
    Write-Banner $Name
    & $Block
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
        throw "$Name failed (exit $LASTEXITCODE)"
    }
}

# ── Clean step ──────────────────────────────────────────────────────────────
if ($Clean) {
    Write-Banner "Clean"
    $artifacts = @(
        (Join-Path $ProjectRoot "dist"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Core\bin"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Core\obj"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.SaveModel\bin"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.SaveModel\obj"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.RustInterop\bin"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.RustInterop\obj"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Tests\bin"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Tests\obj"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Ui\bin"),
        (Join-Path $ProjectRoot "src\CrimsonAtomtic.Ui\obj")
    )
    foreach ($p in $artifacts) {
        if (Test-Path $p) {
            Write-Host "    rm $p"
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $p
        }
    }
    # Cargo target/ is intentionally left alone -- it's vendored under
    # vendor\crimson-rs\target\ and a clean cargo build is genuinely
    # expensive (pyo3 chain). The dist artifact gets re-staged anyway.
}

# ── Rust DLL step ───────────────────────────────────────────────────────────
$buildDll = $Target -in @("All", "DLL", "UI", "Test")
$rustProfile = if ($Mode -eq "Debug") { "debug" } else { "release" }

if ($buildDll) {
    Invoke-Step "Build Rust DLL ($rustProfile)" {
        & (Join-Path $Scripts "build_rust.ps1") -Profile $rustProfile
    }
}

# ── C# build / test step ────────────────────────────────────────────────────
switch ($Target) {
    "DLL"  { } # already done above; nothing further.
    "UI" {
        $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
        Invoke-Step "Build C# UI ($cs)" {
            & dotnet build $Sln -c $cs
        }
    }
    "Test" {
        $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
        Invoke-Step "Build + Test ($cs)" {
            & dotnet test $Sln -c $cs --logger "console;verbosity=normal"
        }
    }
    "All" {
        if ($Mode -eq "Publish") {
            Invoke-Step "AOT Publish ($Runtime)" {
                & (Join-Path $Scripts "package_aot.ps1") -Runtime $Runtime -SkipRustBuild
            }
        } else {
            $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
            Invoke-Step "Build C# ($cs)" {
                & dotnet build $Sln -c $cs
            }
        }
    }
}

# ── Standalone Publish dispatch ─────────────────────────────────────────────
# Publish + non-All target combinations:
#   Publish + DLL  : just the Rust build (above) — no UI publish.
#   Publish + UI   : AOT publish UI (DLL already built above).
#   Publish + Test : Build + test in Release; skip the publish bundle
#                    (a publish bundle isn't useful before tests are green).
if ($Mode -eq "Publish" -and $Target -eq "UI") {
    Invoke-Step "AOT Publish ($Runtime)" {
        & (Join-Path $Scripts "package_aot.ps1") -Runtime $Runtime -SkipRustBuild
    }
}

Write-Banner "Done"
Write-Host "Mode=$Mode Target=$Target Clean=$Clean Runtime=$Runtime" -ForegroundColor DarkGray
