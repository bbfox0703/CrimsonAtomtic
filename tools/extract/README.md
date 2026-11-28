# tools/extract/

Pull data out of game files into structured form.

| Script | Input | Output |
| --- | --- | --- |
| `extract_save.py` | `--save <path>` (default: the first `slot0\save.save` under the Steam save root) | `<out>/<slot>.json` (header fields, sizes, HMAC status, body SHA-256, source path) and `<out>/<slot>.bin` (the decompressed body) |

`<slot>` is the save file's parent folder name (e.g. `slot0`), so runs against
different slots can share one output folder. The `.bin` is what
`tools/inspect/` reads.

Conventions for scripts here:

- Take `--out <dir>` (created if missing).
- Default to no overwrite; pass `--force` to replace existing output.
- Print full `--help` on no-args invocation.

See [../CLAUDE.md](../CLAUDE.md) for the conventions a new script must follow.
