# Paths the build scripts share. Dot-source it: . (Join-Path $PSScriptRoot "paths.ps1")
# The MSBuild side has its own copy in Directory.Build.props (CrimsonRsTargetDir);
# the two must name the same folder.

$RepoRoot           = Split-Path -Parent $PSScriptRoot
$CrimsonRsDir       = Join-Path $RepoRoot "vendor\crimson-rs"
# The C# projects always link the release profile, so that is the only one
# the scripts build.
$CrimsonRsTargetDir = Join-Path $CrimsonRsDir "target\release"
$CrimsonRsDll       = Join-Path $CrimsonRsTargetDir "crimson_rs.dll"
$CrimsonRsLib       = Join-Path $CrimsonRsTargetDir "crimson_rs.lib"
# Written by build_rust.ps1: the vendor commit the DLL / .lib were built from.
$CrimsonRsBuiltSha  = Join-Path $CrimsonRsTargetDir ".crimson-rs-sha"
# Committed; written by vendor\update_vendors.ps1. The commit CI builds against.
$CrimsonRsRefFile   = Join-Path $RepoRoot "vendor\crimson-rs.ref"
$BuildNumberFile    = Join-Path $RepoRoot "build_number.txt"
$UiProject          = Join-Path $RepoRoot "src\CrimsonAtomtic.Ui\CrimsonAtomtic.Ui.csproj"
$TestProject        = Join-Path $RepoRoot "src\CrimsonAtomtic.Tests\CrimsonAtomtic.Tests.csproj"

# Only the Windows build exists: the Rust artifacts are named for Windows
# (crimson_rs.dll / .lib) and the AOT link uses Windows import libraries.
$SupportedRuntimes  = @("win-x64")
