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
canonical source-of-truth lives at `D:\Github\crimson-rs`; this vendor
folder is a refreshable snapshot.

## What is here

| Name         | Source                  | Branch | Purpose                                                |
| ------------ | ----------------------- | ------ | ------------------------------------------------------ |
| `crimson-rs` | `D:\Github\crimson-rs`  | `dev`  | Rust core: PABGB / PAZ / PALOC parse, ChaCha20, etc.   |

## Refreshing

```powershell
# from the project root
.\vendor\update_vendors.ps1
```

What it does:

1. For each entry in the script's `$Vendors` table:
   - If the target folder is missing → `git clone` from the local source path.
   - Otherwise → `git fetch origin && git checkout <branch> && git reset --hard origin/<branch>`.
2. Refuses to discard uncommitted local changes unless `-Force` is passed.

## Do not edit files inside `vendor/<name>/`

If `crimson-rs` needs a change:

1. Make and commit it in `D:\Github\crimson-rs`.
2. Re-run `.\vendor\update_vendors.ps1` here.

Otherwise the next vendor refresh will silently wipe your edits. The
update script's safety check helps but is not foolproof.

## Gitignore

The parent repo ignores everything under `vendor/<name>/` (see root
`.gitignore`). The vendor folder itself, this `README.md`, and
`update_vendors.ps1` are tracked.
