# Release process (Windows binary)

> How to cut a release of the CrimsonAtomtic editor. The CI workflow is
> [`.github/workflows/release.yml`](../.github/workflows/release.yml).

## TL;DR

```sh
# 1. main must already have everything you want shipped (merged via PR).
# 2. the crimson-rs fix you want must be on GitHub `main` (CI clones it; see below).
git fetch origin
git tag -a v1.10.01-fix origin/main      # ANNOTATED tag — write bilingual notes in the message
git push origin v1.10.01-fix
# 3. CI builds the AOT exe + creates a DRAFT release. Verify the zip, then Publish in the UI.
```

## What triggers a build

- **A pushed `v*` tag** → full AOT build + packaged `.zip` + sha256 + a **DRAFT** GitHub Release.
- **Manual `workflow_dispatch`** → build + artifact only (no release). Use it to smoke-test the pipeline.
- A normal push / PR-merge does **NOT** build or release.

## Tagging — read this before you tag

- **Use an ANNOTATED tag (`git tag -a`), not a lightweight one (`git tag`).** The tag's
  message is prepended verbatim to the release notes, so that's where the bilingual
  (English + 繁體中文) highlights go. A lightweight tag has no message → CI skips the
  highlights section and emits only the auto English changelog (and logs a CI warning).
  CI tells them apart with `git cat-file -t <tag>` (`tag` = annotated, `commit` = lightweight).
- **Suggested tag-message shape** (free-form; CI pastes it as-is at the top of the notes):

  ```
  ## Highlights
  - English bullet …

  ## 重點
  - 繁體中文重點 …
  ```

- **Build number (4th version digit):** a *pure-numeric* `vNNN` tag pins
  `build_number.txt` = NNN for that build (so the embedded version's build digit equals
  the tag). Any other shape — e.g. `v1.10.01`, `v1.10.01-fix` — leaves the committed
  `build_number.txt` as-is.
- **The notes are auto-assembled**: annotated-tag message (top) + an English changelog
  grouped by conventional-commit type (`feat` / `fix` / other) over the
  *previous-tag → this-tag* commit range + a compare link + a standing footer. So write
  good commit subjects (`feat:` / `fix:`) — they become the English detail list.
- The checkout uses `fetch-depth: 0` so CI can see the full history + tag objects (a
  shallow checkout has neither) — don't remove it.

## The native core (crimson-rs) — IMPORTANT

- CI does **not** use your local `vendor/crimson-rs` (it is gitignored, never committed).
  It **clones crimson-rs fresh from GitHub** at `CRIMSON_RS_REF` (default `main`,
  repo `CRIMSON_RS_REPO`).
- **So before tagging, make sure the crimson-rs change/fix you want shipped is merged to
  `bbfox0703/crimson-rs` `main`.** Updating your *local* vendor folder affects local
  builds only — it has no effect on CI.
- For a fully reproducible release, pin `CRIMSON_RS_REF` in the workflow to a crimson-rs
  tag or commit SHA instead of `main`.

## After the tag is pushed

1. CI builds the single-file AOT exe (links the freshly-cloned crimson-rs), zips
   `dist/win-x64`, writes a `.sha256`, and creates a **DRAFT** release with the notes.
2. Download the zip from the draft (or the run artifact) and verify it loads against a
   real save.
3. Click **Publish release** in the GitHub UI — or, to redo, delete the draft + the tag,
   fix, and re-tag.

## Undoing a tag

```sh
git push origin :refs/tags/v1.10.01-fix   # delete the remote tag
git tag -d v1.10.01-fix                    # delete the local tag
# also delete the DRAFT release in the GitHub UI if one was created
```
