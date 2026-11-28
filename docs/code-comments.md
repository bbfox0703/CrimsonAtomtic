# Writing code comments

Applies to every comment in the repo's own code: C# (`//`, `///`, `/* */`), XAML and MSBuild
(`<!-- -->`), Python (`#` and docstrings), PowerShell and workflow YAML (`#`). `vendor/` follows
crimson-rs's own rule.

Why the rules exist: a comment that restates the code, lists what must stay in sync, or tells a
story goes stale the first time someone changes the code without opening the comment, and from
then on it misleads. The history of a change lives in [dev-log.md](dev-log.md), which is dated
and never rewritten; a comment describes the code as it stands.

## The rules

1. **Say why, and state the invariant.** Do not restate what the next line visibly does. A fact
   about the game data or the save format is fine when it is the reason for the code ("2.06 moved
   the row name into the header, so ...").
2. **Do not enumerate** callers, fields, keys, windows or states that must be kept in sync. State
   the rule ("every tool window that writes re-checks the snapshot"), or name the one symbol that
   owns the list.
3. **No uniqueness or role claims** ("the only caller", "used only by", "both X and Y") unless a
   test or an assert enforces them. They go stale the day a second caller appears.
4. **No plan wording** in shipped code: no "will", "v2", "phase 2", "reserved for", "for now".
   Open work goes in status.md's backlog; the comment describes what the code does today.
5. **No line numbers** into this repo or into crimson-rs (`NativeSaveLoader.cs:131`,
   `mod.rs:4321`). Name the function, type, constant or doc section: those move with the code.
   A line in a third-party codebase is fine with its version beside it.
6. **A number the code depends on is a test or a constant, not prose.** Do not write "the 187
   dragon keys" in a comment; pin the count in a test and say "the dragon key set".
7. **No history.** No fix narrative ("used to", "before the fix", "previously", "turned out"),
   no audit or review ids (`B1-01`), PR numbers, commit hashes, or "found / confirmed on
   <date>". That goes in a dev-log entry. A header comment states the contract: what the member
   takes, returns, refuses, and the invariant it keeps, never how it got there. A why that stays
   true while the code stands is fine; a why that only explains an old bug is history.
8. **C#: one `<summary>` per member.** A second doc block with no declaration between them
   re-attaches the first to the wrong member, silently.

## The gate

`CommentRulesTests` (in the C# suite, so it runs with every test pass) scans the scope above:

- **Line references** (rule 5), **audit / review ids, PR numbers and commit hashes** (rule 7) and
  **stacked `<summary>` blocks** (rule 8) must be zero.
- **History phrases** (rule 7) are a per-file ratchet against
  `src/CrimsonAtomtic.Tests/comment_history_baseline.json`: a file's count may not grow, and a cut
  must lower the baseline (`$env:UPDATE_COMMENT_BASELINE = '1'` and re-run the test), so a new
  history line cannot hide in the slack an old one left. The target is an empty baseline.
- A failure lists every offending `file:line` with the text.

The patterns are heuristics chosen for few false hits on a new line. If one fires on a
legitimate sentence, reword the sentence; change a pattern only with the reason in the dev log.

## At change time

Before committing a change that adds or removes a caller, a field, a key or an enum value, grep
for comments that name the symbol and fix the ones that list or count it. **Boy-scout rule:**
fix a stale comment you read in a file you are already touching.
