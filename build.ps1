#requires -Version 7.2

<#
.SYNOPSIS
    CrimsonAtomtic unified build entry point — Rust DLL + C# Avalonia UI.

.DESCRIPTION
    Single command for every build flavour the project ships:

      - Debug    : dotnet build -c Debug (fast iteration)
      - Release  : dotnet build -c Release (default)
      - Publish  : dotnet publish -p:PublishAot=true to dist\<rid>\

    The Rust core is always built with the release profile: the C#
    projects link target\release whatever their own configuration.

    The version's fourth part (build_number.txt) goes up by one after a
    successful C# build or publish; a failed build leaves the file alone.

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
    .NET runtime identifier for Publish. Only win-x64 exists (see
    scripts\paths.ps1).

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

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$Scripts     = Join-Path $ProjectRoot "scripts"
. (Join-Path $Scripts "paths.ps1")
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
    # A block made only of cmdlets leaves $LASTEXITCODE as the previous
    # native command set it; reset it so that value is not read as this
    # step's result.
    $global:LASTEXITCODE = 0
    & $Block
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed (exit $LASTEXITCODE)"
    }
}

# ── Build number ─────────────────────────────────────────────────────────────
# build_number.txt holds the version's fourth part. This build uses the next
# number (passed to MSBuild as VerBuild) and writes it back only once the
# build succeeded, so failed builds do not churn the file. A plain
# `dotnet build` reads the file and never changes it.
$buildNum = if (Test-Path $BuildNumberFile) {
    [int]((Get-Content $BuildNumberFile -Raw).Trim())
} else { 0 }
$nextBuild = $buildNum + 1
if ($nextBuild -gt 65535) {
    throw "build_number.txt would pass 65535, the largest version part Windows accepts."
}
$verProp = "-p:VerBuild=$nextBuild"
Write-Banner "Build #$nextBuild"

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
    $leftover = @()
    foreach ($p in $artifacts) {
        if (Test-Path $p) {
            Write-Host "    rm $p"
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $p
            if (Test-Path $p) { $leftover += $p }
        }
    }
    if ($leftover.Count -gt 0) {
        # Usually a running CrimsonAtomtic or test host holding files open.
        Write-Warning "Clean could not remove (the build is not clean):"
        $leftover | ForEach-Object { Write-Warning "    $_" }
    }
    # Cargo target/ is intentionally left alone -- it's vendored under
    # vendor\crimson-rs\target\ and a clean cargo build is genuinely
    # expensive (pyo3 chain). The dist artifact gets re-staged anyway.
}

# ── Rust DLL step ───────────────────────────────────────────────────────────
Invoke-Step "Build Rust DLL (release)" {
    & (Join-Path $Scripts "build_rust.ps1")
}

# ── C# build / test step ────────────────────────────────────────────────────
switch ($Target) {
    "DLL"  { } # already done above; nothing further.
    "UI" {
        $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
        Invoke-Step "Build C# UI ($cs)" {
            & dotnet build $Sln -c $cs $verProp
        }
    }
    "Test" {
        $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
        Invoke-Step "Build + Test ($cs)" {
            # MTP (xunit.v3) — the .NET 10 SDK dropped the `dotnet test`
            # VSTest bridge, so run the test project's own runner.
            & dotnet run --project $TestProject -c $cs $verProp
        }
    }
    "All" {
        if ($Mode -eq "Publish") {
            Invoke-Step "AOT Publish ($Runtime)" {
                & (Join-Path $Scripts "package_aot.ps1") -Runtime $Runtime -SkipRustBuild -BuildNumber $nextBuild
            }
        } else {
            $cs = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
            Invoke-Step "Build C# ($cs)" {
                & dotnet build $Sln -c $cs $verProp
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
        & (Join-Path $Scripts "package_aot.ps1") -Runtime $Runtime -SkipRustBuild -BuildNumber $nextBuild
    }
}

# Every step above throws on failure, so reaching here means the build that
# used $nextBuild succeeded. A Rust-only run built no versioned output.
if ($Target -ne "DLL") {
    [System.IO.File]::WriteAllText($BuildNumberFile, "$nextBuild`n")
}

Write-Banner "Done"
Write-Host "Mode=$Mode Target=$Target Clean=$Clean Runtime=$Runtime" -ForegroundColor DarkGray
