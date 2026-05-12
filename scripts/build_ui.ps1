#requires -Version 7

<#
.SYNOPSIS
    Build the Avalonia UI app. Stub until src/ exists.
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$UiProj      = Join-Path $ProjectRoot "src\CrimsonAtomtic.Ui\CrimsonAtomtic.Ui.csproj"

if (-not (Test-Path $UiProj)) {
    Write-Warning "src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj does not exist yet."
    Write-Warning "This script will start working once the C# project is scaffolded."
    exit 0
}

Write-Host "Building $UiProj ($Configuration)..." -ForegroundColor Cyan
dotnet build $UiProj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

Write-Host "Build OK." -ForegroundColor Green
