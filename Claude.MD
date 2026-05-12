# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> For detailed specs, implementation history, and debugging notes, see the **[docs/](docs/)** directory.

-----

## Build & Deploy
- After making code changes, always do a full rebuild and verify the build output is actually updated before testing. Never assume a build succeeded without checking.

-----

## Code Changes
- When asked to refactor or rename modules/files, make actual code changes (move files, update imports, rename classes) — not just documentation updates. Confirm structural changes before proceeding to docs.

-----

## Debugging
- When fixing bugs, verify the fix against the actual memory layout or data structure rather than assuming. If the first fix doesn't work, re-examine fundamental assumptions about the data format before iterating.

-----

## Git Operations
- When creating PRs, check for branch divergence and resolve merge conflicts before attempting `gh pr create`. Run `git status` and `git log --oneline -5` first.

-----

## Cheat Engine
- When working with CE Lua APIs, verify that functions/methods actually exist in the Cheat Engine Lua API before using them. Do not invent API calls.

-----

## Platform/packages
- Avalonia UI 12 application (net10.0-windows)
- .Net 10
- xUnitTest 3
- AoT compiled for destribution

-----

## Build and testing
- refer `D:\Github\UE5CEDumper\build.cmd` and related scrits for auto MSVC path detion and deployment.

-----

## Rules (MUST follow)

- **Language**: Code comments and UI strings in English
- **Single Instance**: UI app uses Mutex to ensure only one instance runs
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) MUST go through an interface in `Core` project. `Core` must NEVER contain direct platform-specific code
- **Log output**: All logs go to `%LOCALAPPDATA%\CrimsonAtomtic\Logs`, split by category into per-process subfolders. 4-file rotation, 8MB max per file. Root `Logs/` has no loose files — only subfolders
- **Magic words management**: All magic strings kept in one file per project with proper comments
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings. However, in-game items must multi-language enabled (get from game data)
- **AOT compatible**: All C# code must be Native AOT / trimming compatible. No reflection-based APIs — use source generators instead (e.g. `[JsonSerializable]` context for `System.Text.Json`, `[ObservableProperty]` for MVVM). The UI is published as a self-contained trimmed binary
- **Vendor deps**: Clone into `vendor/`, NEVER use git submodules. Use a central .ps1 for updates

-----

## Project Goal

Develop a Cromson Desert OS-independent game save editor -- can run on Windows (must), Linux, MacOS. if Avalonia UI cannot fulfill this, choose another one framework.

-----