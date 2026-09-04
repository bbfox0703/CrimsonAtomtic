# vendor/

Externally-maintained dependencies, cloned (never submoduled) into this folder
and refreshed via [update_vendors.ps1](update_vendors.ps1).

## Why not git submodules

Submodules are silently fragile: collaborators forget to `--recurse-submodules`,
CI checkouts skip them, and the parent repo records a SHA that doesn't update
when the submodule advances. The "clone + central refresh script" model makes
the dependency state explicit and easy to reproduce.

## Why not pip / cargo / npm dependencies

The single vendored dep is `crimson-rs`, **our own fork**. It is not
published to PyPI or crates.io and we don't intend to publish it. The
canonical source-of-truth is `bbfox0703/crimson-rs` on GitHub (developed locally
at `D:\Github\crimson-rs`, landed on `main` via PR); this vendor folder is a
refreshable snapshot of that `main`, and CI clones the same branch fresh.

## Only clones live here

`vendor/<name>/` is a **pristine clone and nothing else** - do not park project
data beside it. Local game saves belong in `data/_saves/`, not `vendor/`.

## What is here

| Name         | Source                  | Branch | Purpose                                                |
| ------------ | ----------------------- | ------ | ------------------------------------------------------ |
| `crimson-rs` | `bbfox0703/crimson-rs`  | `main` | Rust core: PABGB / PAZ / PALOC parse, ChaCha20, etc.   |

## Refreshing

```powershell
# from the project root
.\vendor\update_vendors.ps1
```

What it does:

1. For each entry in the script's `$Vendors` table:
   - If the target has no `.git` of its own → `git clone` from the source.
   - Otherwise → `git fetch origin && git checkout <branch> && git reset --hard origin/<branch>`.
2. Refuses to discard uncommitted local changes unless `-Force` is passed.
3. Verifies `git -C vendor/<name> rev-parse --show-toplevel` really is
   `vendor/<name>` before mutating anything. A folder that exists but is not a
   repo would otherwise make every `git -C` command walk up and hit the parent
   project instead.

## Do not edit files inside `vendor/<name>/`

If `crimson-rs` needs a change:

1. Make and commit it in `D:\Github\crimson-rs`, then land it on `main`.
2. Re-run `.\vendor\update_vendors.ps1` here.

Otherwise the next vendor refresh will silently wipe your edits. The
update script's safety check helps but is not foolproof.

## Gitignore

The parent repo ignores everything under `vendor/<name>/` (see root
`.gitignore`). The vendor folder itself, this `README.md`, and
`update_vendors.ps1` are tracked.
