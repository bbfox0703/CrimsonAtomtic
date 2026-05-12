"""CLI helpers shared by every tool under tools/.

The canonical pattern:

    from common.cli import require_args

    def main() -> int:
        p = argparse.ArgumentParser(
            prog="my_tool.py",
            description=__doc__,
        )
        p.add_argument("--in", dest="input", required=True)
        require_args(p)  # prints help + sys.exit(2) when no args were given
        args = p.parse_args()
        ...
        return 0

    if __name__ == "__main__":
        raise SystemExit(main())
"""

from __future__ import annotations

import argparse
import sys


def require_args(parser: argparse.ArgumentParser, *, exit_code: int = 2) -> None:
    """If the process was invoked with no CLI args, print full --help and exit.

    Why: argparse on its own only emits a one-line "the following arguments
    are required: ..." message, which buries the description and option list.
    Future-us (and Claude) should be able to discover a tool by running it
    naked. Exit code 2 follows the argparse convention for usage errors.
    """
    if len(sys.argv) <= 1:
        parser.print_help(sys.stderr)
        sys.exit(exit_code)
