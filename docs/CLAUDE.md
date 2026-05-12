# docs/ — index

> Documentation lives here. Root [CLAUDE.md](../CLAUDE.md) is intentionally
> minimal and points at this folder.

## How to read

- **architecture.md** — the big picture: layers, language boundaries, why we chose A1
- **data-policy.md** — hygiene rules for game data and reference data (foundation-first)
- **game-versions.md** — paths to current and historical game installs; version detection; diffing
- **save-format.md** — `.save` file format (header, ChaCha20 + HMAC + LZ4 body, TOC, items)
- **pabgb-formats.md** — `.pabgb` family (iteminfo, skill, store, field, etc.)
- **paz-containers.md** — asset archives: `.paz`, `.pamt`, `.papgt`, `.paloc`, trie buffers
- **ui-design.md** — UI/UX principles for the new editor; explicit anti-patterns from the old reference repo

## How to extend

- One topic → one file. Do not let a doc grow past ~400 lines without splitting.
- When you add a doc, also add a one-line pointer to it in [root CLAUDE.md](../CLAUDE.md) under the **Pointers** section.
- For decisions, record **what** was chosen, **why** (alternatives + tradeoffs), and **when**. Keep history short and useful, not a diary.
- Format specs should distinguish: what we know with byte-perfect certainty, what is reverse-engineered guesswork, and what is still TODO. Mark each section.
