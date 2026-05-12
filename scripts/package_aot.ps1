#requires -Version 7

<#
.SYNOPSIS
    Produce a Native AOT release bundle. Stub until src/ + C ABI exist.

.DESCRIPTION
    Final form will:
      1. Build vendor/crimson-rs with the C ABI feature -> crimson_rs.dll
      2. dotnet publish -c Release -r win-x64 -p:PublishAot=true \
         -p:PublishTrimmed=true the Avalonia UI project.
      3. Stage the .dll alongside the AOT exe + Avalonia native runtime files.
      4. Produce a zip in dist/.
#>

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$UiProj      = Join-Path $ProjectRoot "src\CrimsonAtomtic.Ui\CrimsonAtomtic.Ui.csproj"

if (-not (Test-Path $UiProj)) {
    Write-Warning "src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj does not exist yet."
    Write-Warning "Run this once the C# project + C ABI are in place. See docs/architecture.md."
    exit 0
}

Write-Warning "TODO: full implementation. See docstring above."
exit 0
