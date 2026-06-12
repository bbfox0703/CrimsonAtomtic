#requires -Version 7

<#
.SYNOPSIS
    Produce a Native AOT release bundle of CrimsonAtomtic.

.DESCRIPTION
    1. Build vendor/crimson-rs with the c_abi feature -> crimson_rs.dll
       (LoadLibrary path for dev/test) + crimson_rs.lib (NativeAOT
       staticlib for the publish bundle). Both are produced in one pass
       via .\scripts\build_rust.ps1.
    2. dotnet publish -c Release -r <Runtime> -p:PublishAot=true on the
       Avalonia UI project. The Ui csproj wires crimson_rs.lib into ILC
       via <DirectPInvoke> + <NativeLibrary>, so the Rust core is folded
       into CrimsonAtomtic.exe — no separate crimson_rs.dll in dist/.
    3. Stage the published directory under dist/<Runtime>/.
    4. Verify the single-file shape (no crimson_rs.dll on disk; no
       crimson_rs.dll import in the exe) and print a summary.

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
$LibSource = Join-Path $ProjectRoot "vendor\crimson-rs\target\release\crimson_rs.lib"
if (-not (Test-Path $LibSource)) {
    throw "Expected crimson_rs.lib (NativeAOT staticlib) at $LibSource. Run without -SkipRustBuild."
}
if (-not (Test-Path $DllSource)) {
    # The dll isn't used at publish time, but its absence suggests an
    # incomplete cargo build — flag it so dev/test workflows don't break
    # silently after the publish.
    Write-Warning "crimson_rs.dll is missing at $DllSource — dev/test paths that LoadLibrary the dll will fail until you re-run build_rust.ps1."
}

# ── 2. dotnet publish (AOT) ─────────────────────────────────────────────────
Write-Host ""
Write-Host "==> dotnet publish -c Release -r $Runtime -p:PublishAot=true" -ForegroundColor Cyan
Push-Location $ProjectRoot
try {
    # Don't pre-clean $DistRoot: when a previous dotnet host still
    # holds file handles (mmap on CrimsonAtomtic.exe), Remove-Item
    # marks for delete-on-close but leaves the directory in flux long
    # enough that the subsequent ilc pass on first invocation flakes
    # with exit code -1. dotnet publish handles its own overwrite.
    New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null

    # PublishAot=true implies trimming; passing -p:PublishTrimmed=true
    # in addition flips some AOT analyzers into error mode on Avalonia
    # 12.x DataGrid trim warnings. The csproj already pins TrimMode=full,
    # which is enough.
    # Prefer the MSVC linker already on PATH (dev shell / inherited dev env) over
    # ILC's findvcvarsall.bat auto-discovery, which captures cmd.exe stdout to find
    # link.exe and so corrupts the linker path ("The input line is too long." ->
    # exit 123) when the environment is bloated by stacked vcvars/DevShell layers.
    # Gated on a linker being on PATH; a plain shell still lets ILC auto-discover
    # (cargo + ILC both self-locate MSVC), so no regression.
    #
    # CAVEAT — must be the *MSVC* link.exe, not GNU coreutils' `link` (the
    # hard-link utility). Git-for-Windows ships C:\Program Files\Git\usr\bin\
    # link.exe, which is on the default GitHub Actions runner PATH. Handing ILC
    # *that* link makes it choke on the MSVC-style "/DEF: @link.rsp" args
    # ("link: extra operand …" -> exit 1, the whole publish fails). So opt in
    # only when the resolved path looks like the VS/MSVC toolchain; on a clean
    # CI runner (only the GNU link on PATH) we fall through and let ILC's own
    # vcvars discovery find the real MSVC link.
    $aotArgs = @(
        "publish", $UiProj,
        "--configuration", "Release",
        "--runtime", $Runtime,
        "-p:PublishAot=true",
        "--output", $DistRoot
    )
    $linkCmd = Get-Command link.exe -ErrorAction SilentlyContinue
    if ($linkCmd -and $linkCmd.Source -match '(?i)\\VC\\Tools\\MSVC\\|\\VC\\bin\\|Microsoft Visual Studio') {
        $aotArgs += "-p:IlcUseEnvironmentalTools=true"
    }
    & dotnet @aotArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

# ── 2b. Strip residual .pdb files ───────────────────────────────────────────
# The Ui csproj has a StripPdbsAfterPublish target that catches managed PDBs +
# the NuGet-shipped native PDBs (libSkiaSharp, libHarfBuzzSharp). It does NOT
# catch CrimsonAtomtic.pdb, which is the *native* PDB produced by the ILC AOT
# compiler — that file is emitted after the Publish target chain completes, so
# the in-MSBuild Delete misses it. We sweep one more time here.
$pdbs = Get-ChildItem $DistRoot -Recurse -Filter *.pdb -ErrorAction SilentlyContinue
if ($pdbs) {
    $pdbBytes = ($pdbs | Measure-Object Length -Sum).Sum
    Write-Host ""
    Write-Host ("==> Stripping {0} residual .pdb file(s) ({1:N1} MB)" -f $pdbs.Count, ($pdbBytes / 1MB)) -ForegroundColor Cyan
    foreach ($p in $pdbs) {
        Write-Host "    rm $($p.Name)" -ForegroundColor DarkGray
        Remove-Item -Force $p.FullName
    }
}

# ── 3. Verify single-file shape ─────────────────────────────────────────────
# Two invariants for the staticlib publish path:
#   (a) dist/ must NOT contain crimson_rs.dll — the Ui csproj sets
#       CopyToPublishDirectory=Never, and the AOT publish must respect
#       that or we've silently regressed to the dual-file shape.
#   (b) CrimsonAtomtic.exe must NOT import crimson_rs.dll — confirms ILC
#       actually picked up <DirectPInvoke> + <NativeLibrary> and folded
#       the staticlib in. If this fails, the Include= name probably
#       doesn't match the [DllImport("crimson_rs")] string.
$strayDll = Get-ChildItem $DistRoot -Recurse -Filter "crimson_rs.dll" -ErrorAction SilentlyContinue
if ($strayDll) {
    Write-Warning "Found crimson_rs.dll in the publish output (single-file shape regression):"
    $strayDll | ForEach-Object { Write-Host "      $($_.FullName)" -ForegroundColor Yellow }
}

$exe = Get-ChildItem $DistRoot -Filter "CrimsonAtomtic.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
$dumpbinImportsOk = $null
if ($exe) {
    $dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($dumpbin) {
        $imports = & dumpbin.exe /imports $exe.FullName 2>$null
        $crimsonImport = $imports | Select-String -SimpleMatch "crimson_rs.dll"
        if ($crimsonImport) {
            Write-Warning "CrimsonAtomtic.exe still imports crimson_rs.dll — DirectPInvoke didn't take effect:"
            $crimsonImport | ForEach-Object { Write-Host "      $($_.Line)" -ForegroundColor Yellow }
            $dumpbinImportsOk = $false
        } else {
            $dumpbinImportsOk = $true
        }
    } else {
        Write-Host "    (dumpbin not on PATH — skipping import verification; run from a VS dev shell to enable)" -ForegroundColor DarkGray
    }
}

# ── 4. Summary ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Bundle staged at $DistRoot" -ForegroundColor Green

if ($exe) {
    Write-Host ("    CrimsonAtomtic.exe : {0:N1} MB" -f ($exe.Length / 1MB))
} else {
    Write-Warning "    CrimsonAtomtic.exe not found in the publish output."
}
if ($strayDll) {
    Write-Host ("    crimson_rs.dll     : present (REGRESSION — should be folded into the exe via staticlib)") -ForegroundColor Yellow
} else {
    Write-Host "    crimson_rs.dll     : absent (folded into exe via staticlib)" -ForegroundColor DarkGreen
}
if ($dumpbinImportsOk -eq $true) {
    Write-Host "    exe imports        : no crimson_rs.dll import (DirectPInvoke OK)" -ForegroundColor DarkGreen
} elseif ($dumpbinImportsOk -eq $false) {
    Write-Host "    exe imports        : crimson_rs.dll STILL IMPORTED (DirectPInvoke regression)" -ForegroundColor Yellow
}

$totalBytes = (Get-ChildItem $DistRoot -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("    total              : {0:N1} MB ({1} files)" -f ($totalBytes / 1MB), (Get-ChildItem $DistRoot -Recurse -File).Count)
