#requires -Version 7.2

<#
.SYNOPSIS
    Clone crimson-rs into vendor/crimson-rs at the commit vendor/crimson-rs.ref names.

.DESCRIPTION
    For CI, which has no vendor clone (vendor/*/ is gitignored). Every git
    step is checked, and HEAD is compared with the ref at the end, so a
    failed checkout cannot fall through to a build of the default branch.

    Locally, use vendor/update_vendors.ps1 instead: it refreshes the clone
    and writes the ref this script reads.

.PARAMETER Repository
    The crimson-rs remote. Defaults to the GitHub repository.
#>

[CmdletBinding()]
param(
    [string]$Repository = "https://github.com/bbfox0703/crimson-rs.git"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "paths.ps1")

if (-not (Test-Path $CrimsonRsRefFile)) {
    throw "$CrimsonRsRefFile is missing; vendor/update_vendors.ps1 writes it."
}
$ref = (Get-Content $CrimsonRsRefFile -Raw).Trim()
if ($ref -notmatch '^[0-9a-f]{40}$') {
    throw "$CrimsonRsRefFile must hold one full commit SHA, found '$ref'."
}

function Invoke-Git {
    git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit $LASTEXITCODE)" }
}

if (Test-Path $CrimsonRsDir) { Remove-Item -Recurse -Force $CrimsonRsDir }
# Fetch the one commit instead of cloning a branch: the ref need not be a
# branch tip any more by the time CI runs.
Invoke-Git init --quiet $CrimsonRsDir
Invoke-Git -C $CrimsonRsDir remote add origin $Repository
Invoke-Git -C $CrimsonRsDir fetch --quiet --depth 1 origin $ref
Invoke-Git -C $CrimsonRsDir checkout --quiet --detach FETCH_HEAD

$head = (git -C $CrimsonRsDir rev-parse HEAD).Trim()
if ($head -ne $ref) {
    throw "vendor/crimson-rs is at $head, not $ref."
}
Write-Host "vendor/crimson-rs @ $head"
