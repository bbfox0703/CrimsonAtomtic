@echo off
setlocal EnableDelayedExpansion

:: ============================================================
:: build.cmd -- CrimsonAtomtic build wrapper
::
:: Mirrors the UE5CEDumper command shape so the user can reuse
:: muscle memory across both projects.
::
:: Usage:
::   build              Build Release (Rust DLL + C# UI)
::   build debug        Build Debug
::   build release      Build Release (explicit)
::   build publish      AOT publish to dist/win-x64/
::   build clean        Combine with another verb -- clean first
::   build dll          Rust DLL only
::   build ui           C# UI only
::   build test         Build + run xUnit suite
::   build publish clean    Clean + publish
:: ============================================================

set "MODE=Release"
set "TARGET=All"
set "CLEAN="
set "HAS_ARGS=0"

:parse_args
if "%~1"=="" goto :run
set "HAS_ARGS=1"

set "ARG=%~1"
for %%A in ("%ARG%") do set "UPPER=%%~A"
call :to_upper UPPER

if "!UPPER!"=="DEBUG"   ( set "MODE=Debug"   & shift & goto :parse_args )
if "!UPPER!"=="RELEASE" ( set "MODE=Release" & shift & goto :parse_args )
if "!UPPER!"=="PUBLISH" ( set "MODE=Publish" & shift & goto :parse_args )
if "!UPPER!"=="CLEAN"   ( set "CLEAN=-Clean" & shift & goto :parse_args )
if "!UPPER!"=="DLL"     ( set "TARGET=DLL"   & shift & goto :parse_args )
if "!UPPER!"=="UI"      ( set "TARGET=UI"    & shift & goto :parse_args )
if "!UPPER!"=="TEST"    ( set "TARGET=Test"  & shift & goto :parse_args )
if "!UPPER!"=="ALL"     ( set "TARGET=All"   & shift & goto :parse_args )
if "!UPPER!"=="/?"      goto :usage
if "!UPPER!"=="-H"      goto :usage
if "!UPPER!"=="--HELP"  goto :usage

echo.
echo  ERROR: Unknown argument '%~1'
goto :usage_error

:run
echo.
echo  CrimsonAtomtic Build
echo  Mode: %MODE%  Target: %TARGET%  Clean: %CLEAN%

if "!HAS_ARGS!"=="0" (
    echo  Hint: No arguments -- using defaults. Available verbs:
    echo    build debug          Debug build
    echo    build dll            Rust DLL only
    echo    build ui             C# UI only
    echo    build test           Build + run tests
    echo    build publish        AOT bundle to dist\
    echo    build clean          Combine with another verb -- clean first
    echo    build --help         Full usage
)
echo.

:: Prefer PowerShell 7+ (pwsh); the script #requires it (cleaner
:: cmdlet semantics, modern parameter binding). Fall back to Windows
:: PowerShell only if pwsh isn't on PATH, but warn loudly because the
:: #requires line will still reject the older shell.
where /q pwsh.exe
if "%ERRORLEVEL%"=="0" (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Mode %MODE% -Target %TARGET% %CLEAN%
) else (
    echo  WARNING: pwsh not found on PATH -- falling back to Windows PowerShell 5.1.
    echo  Install PowerShell 7+ from https://github.com/PowerShell/PowerShell for a clean run.
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Mode %MODE% -Target %TARGET% %CLEAN%
)
set "EC=%ERRORLEVEL%"

if %EC% neq 0 (
    echo.
    echo  BUILD FAILED [exit code %EC%]
    echo.
) else (
    echo.
    echo  BUILD SUCCEEDED
    echo.
)

exit /b %EC%

:usage_error
call :print_usage
exit /b 1

:usage
call :print_usage
exit /b 0

:print_usage
echo.
echo  Usage: build [mode] [target] [options]
echo.
echo  Modes:
echo    debug       Unoptimized, debug symbols (fast iteration)
echo    release     Optimized build (default)
echo    publish     AOT single-file publish to dist\
echo.
echo  Targets:
echo    all         Rust DLL + C# UI (default)
echo    dll         Rust DLL only (vendor\crimson-rs)
echo    ui          C# UI only (uses existing crimson_rs.dll)
echo    test        Build + run xUnit suite
echo.
echo  Options:
echo    clean       Wipe dist\ + bin\ + obj\ before building
echo.
echo  Examples:
echo    build                   Release build, all targets
echo    build debug             Debug build
echo    build publish clean     Clean + AOT publish
echo    build dll               Rust DLL only
echo    build test              Run tests
echo.
goto :eof

:to_upper
for %%a in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    set "%1=!%1:%%a=%%a!"
)
goto :eof
