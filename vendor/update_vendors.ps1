#requires -Version 7

<#
.SYNOPSIS
    Clone or refresh vendored dependencies into vendor/.

.DESCRIPTION
    Project rule (CLAUDE.md rule 8): vendored deps are clones, not git
    submodules. This is the single entry point for refreshing them.

    Each entry in the $Vendors table below is cloned from its Source
    path (typically another local working repo) into a folder under
    vendor/, tracking the specified Branch.

    Re-running is idempotent.

.PARAMETER Force
    Discard local uncommitted changes in the vendor copy. Without -Force,
    the script refuses to refresh a dirty working tree and prints a
    warning instead.

.PARAMETER DryRun
    Show what would happen without touching anything.

.EXAMPLE
    .\vendor\update_vendors.ps1

.EXAMPLE
    .\vendor\update_vendors.ps1 -DryRun

.EXAMPLE
    .\vendor\update_vendors.ps1 -Force
#>

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# Edit this table when adding a new vendored dep.
$Vendors = @(
    @{
        Name   = "crimson-rs"
        Source = "https://github.com/bbfox0703/crimson-rs.git"
        Branch = "main"
    }
)

$VendorDir = $PSScriptRoot
$failed = @()

foreach ($v in $Vendors) {
    $name   = $v.Name
    $source = $v.Source
    $branch = $v.Branch
    $target = Join-Path $VendorDir $name

    Write-Host ""
    Write-Host "==> $name" -ForegroundColor Cyan
    Write-Host "    source: $source"
    Write-Host "    branch: $branch"
    Write-Host "    target: $target"

    # Source may be a local path or a remote URL (https/ssh/git). Only the
    # local-path form can be existence-checked on disk.
    $isUrl = $source -match '^(https?|git|ssh)://' -or $source -match '^[^/\\]+@[^/\\]+:'
    if (-not $isUrl -and -not (Test-Path $source)) {
        Write-Warning "Source path does not exist; skipping."
        $failed += $name
        continue
    }

    $exists = Test-Path $target

    if ($DryRun) {
        if ($exists) {
            Write-Host "    [dry-run] would: fetch + reset --hard origin/$branch"
        } else {
            Write-Host "    [dry-run] would: git clone --branch $branch $source $target"
        }
        continue
    }

    try {
        if (-not $exists) {
            Write-Host "    cloning..."
            git clone --branch $branch -- "$source" "$target"
            if ($LASTEXITCODE -ne 0) { throw "git clone failed (exit $LASTEXITCODE)" }
        } else {
            # Safety: refuse to clobber uncommitted local changes
            $dirty = git -C "$target" status --porcelain
            if ($dirty -and -not $Force) {
                Write-Warning "Vendor copy has uncommitted changes:"
                $dirty | ForEach-Object { Write-Host "      $_" }
                Write-Warning "Re-run with -Force to discard, or land them in the source repo at $source first."
                $failed += $name
                continue
            }

            # Re-point the remote in case Source changed (e.g. a local-path
            # clone now tracks the GitHub URL). Idempotent — always matches the
            # table's Source so a fresh checkout and an updated one converge.
            git -C "$target" remote set-url origin "$source"
            if ($LASTEXITCODE -ne 0) { throw "git remote set-url failed (exit $LASTEXITCODE)" }

            Write-Host "    fetching..."
            git -C "$target" fetch origin --prune
            if ($LASTEXITCODE -ne 0) { throw "git fetch failed (exit $LASTEXITCODE)" }

            Write-Host "    checking out $branch..."
            git -C "$target" checkout $branch
            if ($LASTEXITCODE -ne 0) { throw "git checkout failed (exit $LASTEXITCODE)" }

            Write-Host "    resetting to origin/$branch..."
            git -C "$target" reset --hard "origin/$branch"
            if ($LASTEXITCODE -ne 0) { throw "git reset failed (exit $LASTEXITCODE)" }
        }

        $head = git -C "$target" rev-parse --short HEAD
        Write-Host "    OK at $head" -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed for $name : $_"
        $failed += $name
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Warning "Some vendors failed: $($failed -join ', ')"
    exit 1
}
Write-Host "All vendor deps updated." -ForegroundColor Green
