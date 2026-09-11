# Release process (Windows binary)

> How to cut a release of the CrimsonAtomtic editor. The CI workflow is
> [`.github/workflows/release.yml`](../.github/workflows/release.yml).
>
> **When the user says 「啟動 release CI」 (start the release CI), run the
> [runbook](#runbook--啟動-release-ci) below end to end.** It is the exact
> sequence used for v2.02.01 (2026-09-11); nothing in it needs re-deciding.
> Stop and report at the first check that fails.

## Runbook — 「啟動 release CI」

Precondition: the release's code is already on `main` through a `dev` → `main`
PR, and was verified locally (full test suite green, AOT publish clean — see
the "How to verify" recipe in [status.md](status.md)).

### 0. Pre-flight (read-only)

```sh
git fetch origin --tags
git log --oneline origin/main..dev               # must be empty: everything to ship is on main
git log --oneline --no-merges dev..origin/main   # must be empty: main has nothing dev lacks
git show origin/main:src/CrimsonAtomtic.Ui/CrimsonAtomtic.Ui.csproj | grep -E '<Ver(Major|Minor|Patch)>'
git show origin/main:build_number.txt
git ls-remote https://github.com/bbfox0703/crimson-rs.git refs/heads/main   # what CI will clone …
git -C vendor/crimson-rs rev-parse HEAD                                     # … vs what you tested
curl -s https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json
```

- **Tag name = the build identity**: `v<VerMajor>.<VerMinor, 2 digits>.<VerPatch, 2 digits>`
  (2 / 2 / 1 → `v2.02.01`), and it must not exist yet (`git tag -l <tag>`,
  `git ls-remote --tags origin <tag>`). A second editor release for the same
  game version needs `VerPatch` bumped in the merged code first — stop if it
  wasn't.
- The exe will stamp `<major>.<minor>.<patch>.<build_number.txt>`; only a
  pure-numeric `vNNN` tag overrides the build number.
- **crimson-rs**: CI builds against GitHub `main` at tag time, never the local
  vendor copy. If the two SHAs differ, land the crimson-rs change on its
  `main` first.
- **SDK**: CI installs the newest `10.0.x`. If the releases index's `10.0`
  `latest-runtime` is newer than the `runtime.win-x64.Microsoft.DotNet.ILCompiler`
  pin in `Directory.Packages.props`, restore will fail NU1109 on the runner —
  bump the pin (through a PR) before tagging.

### 1. Write the release notes — for players, in English and 繁體中文

Model them on the previous two releases
(`gh release view <tag> --json body --jq .body`):

```
## Highlights
- **Bold lead sentence: what changed for the player.** One or two plain sentences on why, or what to do.

## 重點
- **對應英文的粗體開頭句。** 白話說明……
```

- **Player-facing, no technical detail.** Say what now works, what is switched
  off and why in everyday words, and what to do if affected. No parsers,
  formats, bytes, ABI, tests, file or class names.
- The two sections mirror each other bullet for bullet; typically 3–5
  bullets. Keep "Crimson Desert" in English in the Chinese text, as earlier
  releases do.
- **One line per bullet, no hard wraps.** The same file becomes the tag
  message *and* the final release body, so what the tag carries is exactly
  what gets published.
- Save it outside the repo (the session scratchpad), e.g.
  `release-notes-v2.02.01.md`. The draft stays private, so the user reviews
  the notes at publish time; no approval round is needed before tagging.

### 2. Tag `origin/main` and push the tag — this starts the CI

```sh
git tag -a v2.02.01 origin/main --cleanup=verbatim -F release-notes-v2.02.01.md
git tag -l --format='%(contents)' v2.02.01 | head -1          # must print: ## Highlights
git tag -l --format='%(contents)' v2.02.01 | grep -cx '## 重點'  # must print: 1
git push origin v2.02.01                                      # origin only, never another remote
```

`--cleanup=verbatim` is not optional: the default (`strip`) treats every
line starting with `#` as a comment and silently deletes both headings.

### 3. Wait for the run (about 3–5 minutes; v2.02.01 took 5 m 13 s)

```sh
gh run list --workflow release.yml --limit 1   # the run whose branch is the tag
gh run watch <run-id> --exit-status
```

On failure: `gh run view <run-id> --log-failed`, fix through `dev` → PR →
`main`, then [undo the tag](#undoing-a-tag) and re-tag.

### 4. Replace the draft's notes with the bilingual sections

CI assembles *tag message* + an auto `## What's changed` list + a compare
link + a standing footer. Every published release since v1.18.01 carries
**only** `## Highlights` + `## 重點`, so overwrite the body with the notes
file:

```sh
gh release edit v2.02.01 --notes-file release-notes-v2.02.01.md
gh release view v2.02.01 --json isDraft,body   # still a draft; body == the notes file
```

### 5. Verify the draft

- **No download needed**: `gh release view <tag> --json isDraft,assets` must
  show a draft with exactly `CrimsonAtomtic-<tag>-win-x64.zip` (about
  20.6 MB) and its `.sha256`, each with a GitHub-computed `digest`. The zip's
  digest must equal the hash in the run log's `Packaged … (sha256 …)` line —
  that proves the file on the release is the one CI hashed. The same log
  shows which crimson-rs CI built and package_aot's bundle summary (4 files,
  `crimson_rs.dll : absent`; the script throws on a regression):

  ```sh
  gh run view <run-id> --log | grep -E 'vendor/crimson-rs @|Packaged |crimson_rs\.dll +:|total +:' | grep -v Write-Host
  ```
- **Binary checks need the zip — ask the user before downloading it** (name +
  size), then: the `.sha256` matches; the zip holds exactly four files with
  **no `crimson_rs.dll`** (the Rust core is linked into the exe); the exe's
  FileVersion is `<major>.<minor>.<patch>.<build>` and its ProductVersion
  ends in the tagged `main` SHA; launched, its window title reads
  `CrimsonAtomtic v<major>.<minor:D2>.<patch:D2>.<build>`.

### 6. Stop at the draft and report

Report the tag, the run id, the draft URL, the asset sizes and hash, and what
was verified. **Publishing is the user's call** — the draft is private until
then. When they say publish: `gh release edit <tag> --draft=false --latest`
(or the Publish button). Afterwards record it in [status.md](status.md)
through `dev` → PR → `main` ("docs: record vX.YY.ZZ tagged, built and
published").

## Background

### What triggers a build

- **A pushed `v*` tag** → full AOT build + packaged `.zip` + sha256 + a **DRAFT** GitHub Release.
- **Manual `workflow_dispatch`** → build + artifact only (no release). Use it to smoke-test the pipeline.
- A normal push / PR merge does **NOT** build or release — PRs to `main` have no CI checks at all.

### Tags and notes

- **Annotated tags only (`git tag -a`).** The tag's message is prepended verbatim to the release
  notes; a lightweight tag has no message, so CI emits only the auto English changelog and logs a
  warning. CI tells them apart with `git cat-file -t <tag>` (`tag` = annotated, `commit` = lightweight).
- **`--cleanup=verbatim`** — v1.17.01 lost both `##` headings to the default `strip` mode and had to
  be fixed with `gh release edit --notes-file`. Verify the first line *before* pushing: once pushed,
  correcting a tag means deleting it and the draft and re-running CI.
- **Hard-wrapped tag messages** (every tag up to v2.01.01) had to be unwrapped by hand at step 4.
  Writing the notes one line per bullet from the start removes that step.
- **Build number (4th digit):** a pure-numeric `vNNN` tag pins `build_number.txt` = NNN for that
  build; any other shape (`v2.02.01`, `v1.10.01-fix`) uses the committed value.
- The auto changelog groups the previous-tag → this-tag commits by conventional-commit type
  (`feat` / `fix` / other), so commit subjects still matter even though step 4 drops the section.
- The checkout uses `fetch-depth: 0` so CI can see the full history + tag objects (a shallow
  checkout has neither) — don't remove it.

### The native core (crimson-rs)

- CI does **not** use your local `vendor/crimson-rs` (it is gitignored, never committed). It
  **clones crimson-rs fresh from GitHub** at `CRIMSON_RS_REF` (default `main`, repo
  `CRIMSON_RS_REPO`).
- So before tagging, the crimson-rs change you want shipped must be merged to
  `bbfox0703/crimson-rs` `main`. Updating your *local* vendor folder affects local builds only.
- For a fully reproducible release, pin `CRIMSON_RS_REF` in the workflow to a crimson-rs tag or
  commit SHA instead of `main`.

## Undoing a tag

```sh
git push origin :refs/tags/v1.10.01-fix   # delete the remote tag
git tag -d v1.10.01-fix                    # delete the local tag
gh release delete v1.10.01-fix            # delete the DRAFT release CI created (or in the web UI)
```
