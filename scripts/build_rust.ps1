#requires -Version 7.2

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

    The build is always the release profile: the C# projects link
    target\release, so a debug build would leave them on a stale DLL.
    Cargo.lock is honoured (--locked), and CARGO_TARGET_DIR is set to the
    vendor's own target\ for the call, so a value left in the environment
    cannot send the artifacts elsewhere.

    On success the vendor commit is written next to the artifacts
    (.crimson-rs-sha); the C# build warns when it differs from
    vendor\crimson-rs.ref.

    Note: the Python extension build is handled by maturin via
    setup_python_env.ps1. This script is the C# side.

.EXAMPLE
    .\scripts\build_rust.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "paths.ps1")

if (-not $IsWindows) {
    throw "Only the Windows build exists: crimson_rs.dll / .lib and the AOT link are Windows-specific."
}
if (-not (Test-Path $CrimsonRsDir)) {
    throw "vendor/crimson-rs is missing. Run .\vendor\update_vendors.ps1 first."
}

$VendorRs = $CrimsonRsDir
Push-Location $VendorRs
$prevTargetDir = $env:CARGO_TARGET_DIR
try {
    $env:CARGO_TARGET_DIR = Join-Path $VendorRs "target"
    $cargoArgs = @("build", "--locked", "--release", "--features", "c_abi")

    Write-Host "Running: cargo $($cargoArgs -join ' ') (in $VendorRs)" -ForegroundColor Cyan

    # Capture cargo's combined output. The vendor crate ships ~51
    # dead_code / unused warnings that we don't act on — we don't
    # develop on vendor/crimson-rs from this project, we just consume
    # the build artifact (the vendor's own CI gates -D warnings via
    # clippy). On success we filter those warning blocks out so the
    # build log stays readable; on failure we dump the full unfiltered
    # log so real errors are never hidden.
    $rawLines = & cargo @cargoArgs 2>&1 | ForEach-Object { "$_" }
    $cargoExit = $LASTEXITCODE

    if ($cargoExit -ne 0) {
        foreach ($line in $rawLines) { Write-Host $line }
        throw "cargo build failed (exit $cargoExit)"
    }

    # Stateless warning-block filter. Cargo's warning blocks are made
    # of these line shapes — none of them appear in cargo's normal
    # progress / error output, so we can drop each independently
    # without tracking "are we still inside a warning block?":
    #
    #   warning: <text>            — block start (or the rollup summary
    #                                "warning: `crate` (lib) generated
    #                                N warnings")
    #   <sp>--> file:line:col       — location pointer
    #   <sp>|                       — column-gutter / code-context line
    #   <sp>= note: | = help:       — annotations
    #   <num> | <code>             — source-code line (with-or-without
    #                                leading whitespace on the line
    #                                number)
    #   ...                         — source-elision marker (no leading
    #                                whitespace — caught the previous
    #                                state-machine version off guard
    #                                and broke its end-of-block heuristic)
    #   <sp>^^^                     — caret highlight
    #
    # Real cargo errors (`error:`, `error[Exxxx]:`) and progress lines
    # (`   Compiling`, `   Finished`, `   Running`, etc.) don't match
    # any of these patterns, so they pass through untouched.
    #
    # Also collapse runs of consecutive blank lines into a single blank
    # — dropping warning blocks leaves 50+ trailing blanks (their
    # natural separators) which would visually look like the build hung
    # between cargo's "Compiling" and "Finished" lines.
    $prevBlank = $false
    foreach ($line in $rawLines) {
        if ($line -match '^warning:' -or
            $line -match '^\s+-->' -or
            $line -match '^\s+\|' -or
            $line -match '^\s+=' -or
            $line -match '^\s*\d+\s*\|' -or
            $line -match '^\.\.\.$' -or
            $line -match '^\s*\^') {
            continue
        }
        $isBlank = $line -match '^\s*$'
        if ($isBlank -and $prevBlank) { continue }
        $prevBlank = $isBlank
        Write-Host $line
    }
}
finally {
    $env:CARGO_TARGET_DIR = $prevTargetDir
    Pop-Location
}

$DllPath = $CrimsonRsDll
$LibPath = $CrimsonRsLib

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

$builtSha = (git -C $VendorRs rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "git rev-parse failed in $VendorRs" }
[System.IO.File]::WriteAllText($CrimsonRsBuiltSha, "$builtSha`n")
Write-Host "Built from crimson-rs $builtSha" -ForegroundColor DarkGray

Write-Host ""
Write-Host "Dev / 'dotnet build' / 'dotnet test' pick up crimson_rs.dll via" -ForegroundColor DarkGray
Write-Host "the <Content Include=...> item in the Ui + Tests csprojs." -ForegroundColor DarkGray
Write-Host "AOT publish picks up crimson_rs.lib via <DirectPInvoke> +" -ForegroundColor DarkGray
Write-Host "<NativeLibrary> in CrimsonAtomtic.Ui.csproj — dist/ ships as" -ForegroundColor DarkGray
Write-Host "a single .exe with no crimson_rs.dll alongside." -ForegroundColor DarkGray
