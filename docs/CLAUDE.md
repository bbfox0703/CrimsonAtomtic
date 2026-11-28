# docs/ — index

> Documentation lives here. Root [CLAUDE.md](../CLAUDE.md) is intentionally
> minimal and points at this folder.

## How to read

- **status.md** — current progress, next concrete task, roadmap, gotchas. **Start here on a new session.** (Lean by design.)
- **dev-log.md** — what shipped and why: append-only, newest-first entries per release or merged milestone. Write one in the same PR as the work; its header says how. Read it when you need to know when or why something changed.
- **status-archive.md** — frozen history before 2026-06-12 + long-form investigations moved out of status.md. Read only for deep history behind a decision; new history goes to dev-log.md, not here.
- **architecture.md** — the big picture: layers, language boundaries, why we chose A1
- **data-policy.md** — hygiene rules for game data and reference data (foundation-first)
- **game-versions.md** — paths to current and historical game installs; version detection; diffing
- **save-format.md** — `.save` file format (header, ChaCha20 + HMAC + LZ4 body)
- **save-body-format.md** — decompressed body: schema, TOC, per-block decoder, meta_kind dispatch
- **pabgb-formats.md** — `.pabgb` family (iteminfo, skill, store, field, etc.)
- **paz-containers.md** — asset archives: `.paz`, `.pamt`, `.papgt`, `.paloc`, trie buffers
- **ui-design.md** — UI/UX principles for the editor, with the design constraints they derive from
- **crimsonforge-coverage-gaps.md** — what NattKh's CrimsonForge modding tool can decode that we can't (yet); the canonical "look here when you don't know how to parse format X" pointer
- **dev-workflow.md** — branch/merge flow, switching machines, running single tests, preparing in-game test saves (slot102 → slot103), driving the app UI; shared across machines in place of agent memory
- **release-process.md** — the 「啟動 release CI」 runbook (pre-flight → player-facing bilingual notes → annotated tag → CI draft → replace notes → verify → stop at the draft), plus background: annotated `v*` tags, what the CI workflow does, the crimson-rs-from-`main` gotcha

## How to extend

- One topic → one file. Do not let a doc grow past ~400 lines without splitting.
- When you add a doc, add its row to **How to read** above. Add a pointer to [root CLAUDE.md](../CLAUDE.md) only if every session needs it: every `CLAUDE.md` stays ≤ 40 KB (40,960 bytes), checked by `ClaudeMdSizeTests`.
- For decisions, record **what** was chosen, **why** (alternatives + tradeoffs), and **when**. Keep history short and useful, not a diary.
- Format specs should distinguish: what we know with byte-perfect certainty, what is reverse-engineered guesswork, and what is still TODO. Mark each section.
