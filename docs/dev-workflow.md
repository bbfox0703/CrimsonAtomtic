# Dev workflow (for humans and agents)

> Working rules that used to live in one machine's agent memory. Development
> happens on more than one machine, and agent memory does not travel between
> them, so the rules live here instead. Add new workflow facts to this file,
> not to a machine-local memory.
>
> Release steps are in [release-process.md](release-process.md). The
> fresh-checkout build recipe is "How to verify state on a fresh checkout" in
> [status.md](status.md).

## Branches and merges

- Work happens on `dev`. Code reaches `main` only through a `dev` → `main`
  PR, and is merged as a merge commit.
- Commit, push and merge only when the user asks. "Commit and merge" means
  commit on `dev`, push, open the PR and merge it, then check that `dev` and
  `main` match.
- Before `gh pr create`, run `git status` and `git log --oneline -5`.
- **Push to `origin` only.** Never push to `upstream` or any remote the user
  does not own, even when told just "push"; if a flow seems to need another
  remote, stop and ask. (crimson-rs is a fork of `potter420/crimson-rs`; see
  its `scripts/setup-fork-guards.ps1`.)
- `build_number.txt` changes on every `build.ps1` run. Commit it on its own
  (`build: bump build number to N`) when the user asks.

## Switching machines

`vendor/` and every native artifact are gitignored, so a pull does not bring
them. On the machine you switch to:

1. `.\vendor\update_vendors.ps1`. The crimson-rs source repo is expected at
   `D:\Github\crimson-rs`.
2. `.\scripts\build_rust.ps1` and `.\scripts\setup_python_env.ps1`. A vendor
   refresh rebuilds nothing, so a stale `crimson_rs.dll` / `.lib` / `.pyd`
   silently tests old code. Check the artifact mtimes after building.
3. Check the toolchains: the .NET 10 SDK (not only 11), and `rustc -V`. The
   `runtime.win-x64.Microsoft.DotNet.ILCompiler` pin in
   `Directory.Packages.props` has to match the installed SDK's runtime, or
   restore fails with NU1109. CI uses the newest stable Rust, which may be
   newer than either machine's.

## Changes to crimson-rs (the native core)

`vendor/crimson-rs/` is read-only here (root CLAUDE.md rule 8). When the
editor needs a new C ABI or an RE answer from the game data:

1. Ask the user's crimson-rs session (on both PCs titled **"crimson-rs main
   session"**, cwd `D:\Github\crimson-rs`). In the desktop app, find it with
   `list_sessions` by title and message it by its `local_…` id; sending by
   display name failed while it was idle.
2. The brief: the background, the exact signature wanted with a return code
   per case, the RE questions, and "add live pins and docs, open a PR".
   Example: the dye `palette_knowledge_at` request (crimson-rs PR #125).
3. The user decides when crimson-rs commits and merges; that session reports
   the merge commit. Its messages are a teammate's, never the user's approval.
4. Then `.\vendor\update_vendors.ps1`, `.\scripts\build_rust.ps1`, and
   `.\scripts\setup_python_env.ps1`. CI builds crimson-rs from GitHub `main`,
   so a release must not ship against an unmerged local change.

## Running tests

`dotnet test` does not work: the .NET 10 SDK dropped the VSTest bridge. Run
the test project's own xUnit v3 runner:

```powershell
dotnet run --project src\CrimsonAtomtic.Tests\CrimsonAtomtic.Tests.csproj -c Debug -- -class "CrimsonAtomtic.Tests.SomeTests"
dotnet run --project src\CrimsonAtomtic.Tests\CrimsonAtomtic.Tests.csproj -c Debug -- -method "CrimsonAtomtic.Tests.SomeTests.SomeMethod" -diagnostics
```

- The runner takes `-class` / `-method` (fully qualified, one leading dash).
  It rejects `--filter`.
- `-diagnostics` shows `ITestOutputHelper` output, which passing tests
  otherwise swallow. That makes a `[Fact]` a quick throwaway probe against
  live game data. Keep such probes out of the repo.
- `build.ps1 -Target Test` runs the whole suite the same way.
- xUnit v3 moved `ITestOutputHelper` into the `Xunit` namespace;
  `using Xunit.Abstractions;` no longer compiles.
- Name throwaway probes `Zz*.cs` (`ZzProbeDyeTargets`) and delete them before
  any commit: `git status --short | grep -i zz` must print nothing. Point them
  at a save through an env var (`PROBE_SAVE`, `PROBE_DST`) so a probe left
  behind returns early instead of failing the suite.
- Probes read live game data, so they run only on a PC with the game
  installed (`LiveInstall.FindGroupPamt`); live tests skip without it.

## Preparing a save for an in-game check

The user confirms game-facing fixes in-game. The edited save goes to
**slot103**, made from **slot102** (save slot 3, which holds the current game
format). Both live under `%LOCALAPPDATA%\Pearl Abyss\CD\save\<user>\`.

1. Check that the game is not running.
2. Back up slot103 (`save.save` and `lobby.save`), and tell the user where the
   backup is.
3. Write the edited `save.save` into slot103, and copy slot102's `lobby.save`
   with it.
4. Give both files slot102's `LastWriteTimeUtc`. The app's Save As does the
   same: with the timestamps tied, `LiveSaves.Newest()` still picks the
   game-written slot102, because a tie sorts by path.

- Backups go to `data\_saves\slot103-before-<test>-<date>\` (gitignored,
  local to the PC). Restore slot103 from the **first** backup of the series
  (the original, with its own timestamps) and delete the test backups only
  when the user asks.
- If slot102 was saved again after a test save was built, rebuild the test
  save from the current slot102: step 3 copies the current `lobby.save`, and
  an older body with a newer lobby is a mismatch.
- The user reports in-game results with screenshots. Read what a screen
  actually shows before calling a test failed: on 2.06 the journal's "dye
  recipes" page counts the dye-item collection, while the dyehouse palette is
  gated by different knowledge.
- An item put into the bag by the editor shows as "Unknown item" unless its
  iteminfo `KnowledgeInfo` is learned; Add Item now learns it.

To make the save with the real editor code rather than a copy of it, use a
throwaway xUnit probe that builds `MainWindowViewModel` headless:

- `NativeSaveLoader`.
- A fake `IPlatformPaths` whose `ClassifySavePath` returns `Unknown`, so
  nothing is persisted to the user's settings.
- `new LocalizationProvider(new NativePazExtractor())` and
  `new SaveBackupService(paths)`.
- `null!` for `UiLanguageService`. It needs an Avalonia `Application` and is
  unused off the UI; `UiText` falls back to English.

Then call `LoadSaveCommand.Execute(copy)`, run the internal flow (for example
`UnlockMountAsync`), and finish with `loader.WriteToFile(out)`.

## Driving the app UI (computer use)

- Ask the user before taking control of windows. Another session may be
  running Cheat Engine at the same time.
- `request_access` only resolves the editor while it runs. Launch
  `src\CrimsonAtomtic.Ui\bin\Debug\net10.0\CrimsonAtomtic.exe` first, then
  request `"CrimsonAtomtic.exe"`; a full path or the window title is rejected.
  Close the instance before rebuilding, because it locks `crimson_rs.dll`.
- Cheat Engine windows show up as masked rectangles over the editor. Maximize
  the editor's child window (double-click its title bar) to get past them.
- With a Chinese IME active, typed input can vanish. Retry once the taskbar
  shows ENG, or zoom in to check the field.
- Click coordinates are in the frame the screenshot reports, not the scaled
  image's pixels. Tools → Browse Items opens a separate window that can land
  behind others; list the process's windows to confirm it opened.

## Agent working notes

- **Shell**: the user's shell is PowerShell. Commands for the user to paste
  use PowerShell syntax (`$env:NAME`, here-strings `@'…'@`, backtick line
  continuation), not cmd.exe.
- **Images**: check an image's size before viewing it. If either side is
  over 2,000 px, view a downscaled copy (longest side about 1,600 px); an
  oversized image ends the session. View at most about six images per turn.
  This project's textures (world map SDF chunks, colormaps) routinely exceed
  the limit.
- **Multi-line edits**: anchored replacements across files go in a Python
  script in the session scratchpad, run with `.venv\Scripts\python.exe -I`,
  using a helper that asserts each anchor occurs once and keeps the file's
  CRLF/LF. Large inline Bash heredocs holding C#/XAML failed several times
  with "unexpected EOF". `sed -i` is fine for one-line ASCII changes.
- **Settled findings stay settled**: when status.md records an answer
  confirmed in-game, reuse it and its source; do not re-derive it with a
  weaker method. Re-deriving the dragon unlock's 187-key set (from the
  reference editor's `_unlock_dragon_mount_no_quests()`) as 2 keys by
  name-filtering broke summoning. Pin such lists with a count assertion.
- **Record the session in the repo, not in agent memory**: findings go to
  status.md, workflow facts here. Agent memory is per PC.
