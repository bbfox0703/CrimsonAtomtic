#requires -Version 7.2

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

    After each refresh the commit is written to vendor\<name>.ref, which
    is committed: CI checks out exactly that commit, so a release builds
    against what was vendored and tested here. When the commit moved, the
    script prints the rebuild steps (the DLL / .lib and the Python module
    are built from the vendor and do not follow it on their own).

.PARAMETER Force
    Discard local changes in the vendor copy: modified and untracked files
    (git clean -fd; ignored files such as target\ stay) and commits not on
    origin. Without -Force, the script refuses to refresh a clone holding
    either and lists what it found.

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
$moved = @()
$headBefore = $null

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

    # A folder counts as an existing clone only when it holds its own .git.
    # A leftover empty (or half-deleted) folder does NOT: every `git -C $target`
    # below would then walk *up* and operate on the parent repository. That is
    # exactly how this project's own origin/branches once got rewritten to
    # crimson-rs. Treat such a folder as "not a clone" and clone into it.
    $exists = Test-Path (Join-Path $target ".git")

    if (-not $exists -and (Test-Path $target)) {
        $leftover = @(Get-ChildItem -LiteralPath $target -Force -ErrorAction SilentlyContinue)
        if ($leftover.Count -gt 0) {
            Write-Warning "Target exists but is not a git clone, and is not empty."
            Write-Warning "Refusing to touch it. Move or delete it, then re-run."
            $failed += $name
            continue
        }
        # Empty folder: git clone accepts it as the destination.
    }

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
            # Hard guard: never let `git -C` escape into a parent repository.
            # Without this, a $target that is not itself a repo silently
            # redirects fetch/checkout/reset --hard onto the parent project.
            $top = git -C "$target" rev-parse --show-toplevel 2>$null
            if ($LASTEXITCODE -ne 0 -or -not $top) { throw "not a git repository: $target" }
            $topFull = (Resolve-Path -LiteralPath $top).Path
            $wantFull = (Resolve-Path -LiteralPath $target).Path
            if ($topFull -ne $wantFull) {
                throw "refusing to run: 'git -C $target' resolves to '$topFull', not the vendor clone"
            }

            # Safety: refuse to clobber uncommitted local changes
            $dirty = git -C "$target" status --porcelain
            if ($dirty -and -not $Force) {
                Write-Warning "Vendor copy has uncommitted changes:"
                $dirty | ForEach-Object { Write-Host "      $_" }
                Write-Warning "Re-run with -Force to discard, or land them in the source repo at $source first."
                $failed += $name
                continue
            }
            $headBefore = (git -C "$target" rev-parse HEAD).Trim()

            # Re-point the remote in case Source changed (e.g. a local-path
            # clone now tracks the GitHub URL). Idempotent — always matches the
            # table's Source so a fresh checkout and an updated one converge.
            git -C "$target" remote set-url origin "$source"
            if ($LASTEXITCODE -ne 0) { throw "git remote set-url failed (exit $LASTEXITCODE)" }

            Write-Host "    fetching..."
            git -C "$target" fetch origin --prune
            if ($LASTEXITCODE -ne 0) { throw "git fetch failed (exit $LASTEXITCODE)" }

            # A clean tree can still hold commits made in the clone; reset
            # --hard would drop them with no warning.
            $ahead = @(git -C "$target" rev-list "origin/$branch..HEAD")
            if ($LASTEXITCODE -ne 0) { throw "git rev-list failed (exit $LASTEXITCODE)" }
            if ($ahead.Count -gt 0 -and -not $Force) {
                Write-Warning "Vendor copy has $($ahead.Count) commit(s) not on origin/${branch}:"
                git -C "$target" log --oneline "origin/$branch..HEAD" | ForEach-Object { Write-Host "      $_" }
                Write-Warning "Re-run with -Force to discard, or land them in the source repo at $source first."
                $failed += $name
                continue
            }

            Write-Host "    checking out $branch..."
            git -C "$target" checkout $branch
            if ($LASTEXITCODE -ne 0) { throw "git checkout failed (exit $LASTEXITCODE)" }

            Write-Host "    resetting to origin/$branch..."
            git -C "$target" reset --hard "origin/$branch"
            if ($LASTEXITCODE -ne 0) { throw "git reset failed (exit $LASTEXITCODE)" }

            if ($Force) {
                git -C "$target" clean -fd
                if ($LASTEXITCODE -ne 0) { throw "git clean failed (exit $LASTEXITCODE)" }
            }
        }

        $head = (git -C "$target" rev-parse HEAD).Trim()
        [System.IO.File]::WriteAllText((Join-Path $VendorDir "$name.ref"), "$head`n")
        Write-Host "    OK at $($head.Substring(0, 7)) (written to vendor\$name.ref)" -ForegroundColor Green
        if (-not $exists -or $head -ne $headBefore) {
            $moved += $name
        }
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
if ($moved -contains "crimson-rs") {
    Write-Host ""
    Write-Host "crimson-rs moved. Rebuild what is built from it, then commit vendor\crimson-rs.ref:" -ForegroundColor Yellow
    Write-Host "    .\scripts\build_rust.ps1          # crimson_rs.dll / .lib for the C# side"
    Write-Host "    .\scripts\setup_python_env.ps1    # the Python module the tools import"
}
