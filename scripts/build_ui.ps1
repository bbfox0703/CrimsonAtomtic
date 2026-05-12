#requires -Version 7

<#
.SYNOPSIS
    Build the CrimsonAtomtic solution.

.DESCRIPTION
    Restores, builds, and (optionally) tests the C# solution. Use
    -Run to launch the UI after a successful build.

.PARAMETER Configuration
    "Debug" (default) or "Release".

.PARAMETER Test
    Run the test suite after building.

.PARAMETER Run
    Launch the UI after a successful build (Debug only by default).

.PARAMETER NoRestore
    Skip the initial `dotnet restore`. Useful in CI where restore
    is a separate step.
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Test,
    [switch]$Run,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Sln = Join-Path $ProjectRoot "CrimsonAtomtic.slnx"

if (-not (Test-Path $Sln)) {
    # `dotnet new sln` produces .sln; some SDK setups produce .slnx.
    # Fall back to the classic name.
    $Sln = Join-Path $ProjectRoot "CrimsonAtomtic.sln"
}
if (-not (Test-Path $Sln)) {
    throw "Solution file not found at $Sln. Did you delete CrimsonAtomtic.{sln,slnx}?"
}

Push-Location $ProjectRoot
try {
    if (-not $NoRestore) {
        Write-Host "Restoring packages..." -ForegroundColor Cyan
        & dotnet restore $Sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
    }

    Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
    & dotnet build $Sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    if ($Test) {
        Write-Host "Running tests..." -ForegroundColor Cyan
        & dotnet test $Sln -c $Configuration --no-build --logger "console;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }
    }

    if ($Run) {
        Write-Host "Launching UI..." -ForegroundColor Cyan
        & dotnet run --project "$ProjectRoot\src\CrimsonAtomtic.Ui\CrimsonAtomtic.Ui.csproj" -c $Configuration --no-build
    }
}
finally {
    Pop-Location
}

Write-Host "build_ui: OK" -ForegroundColor Green
