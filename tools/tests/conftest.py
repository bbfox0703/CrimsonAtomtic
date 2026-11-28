"""Shared fixtures for the tools/ test suite.

The tools are plain scripts, not an installed package, so tests load them
by file path the same way `python tools/<dir>/<script>.py` would.
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from types import ModuleType

TOOLS_DIR: Path = Path(__file__).resolve().parents[1]

# Scripts import `common.*` with tools/ on sys.path; tests do the same.
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

#: Folders whose scripts are not held to the tools/ conventions.
EXCLUDED_DIRS: frozenset[str] = frozenset({"tests", "__pycache__"})


def tool_files() -> list[Path]:
    """Every `.py` under tools/ outside `EXCLUDED_DIRS`, sorted."""
    return sorted(
        p for p in TOOLS_DIR.rglob("*.py")
        if not EXCLUDED_DIRS.intersection(p.relative_to(TOOLS_DIR).parts)
    )


def script_files() -> list[Path]:
    """The tool files that define a `main()`, i.e. the runnable scripts."""
    return [p for p in tool_files() if "\ndef main(" in p.read_text(encoding="utf-8")]


def load_tool(rel_path: str) -> ModuleType:
    """Import `tools/<rel_path>` under a private module name."""
    path = TOOLS_DIR / rel_path
    name = "_tool_" + path.stem
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    # dataclasses resolve annotations through sys.modules.
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module
