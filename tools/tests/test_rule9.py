"""Rule 9 for every runnable script: no-args usage, no import-time effects."""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest
from conftest import TOOLS_DIR, script_files, tool_files

SCRIPTS = script_files()
SCRIPT_IDS = [p.relative_to(TOOLS_DIR).as_posix() for p in SCRIPTS]
#: argparse's usage-error code, which `require_args` reuses.
USAGE_EXIT = 2

TOOL_IDS = [p.relative_to(TOOLS_DIR).as_posix() for p in tool_files()]

# Imports one file by path, then reports on stderr whether the import
# touched sys.stdout or pulled in the native module.
_IMPORT_PROBE = """
import importlib.util, sys
sys.path.insert(0, sys.argv[2])
before = sys.stdout
spec = importlib.util.spec_from_file_location("_probe", sys.argv[1])
module = importlib.util.module_from_spec(spec)
sys.modules["_probe"] = module
spec.loader.exec_module(module)
problems = []
if sys.stdout is not before:
    problems.append("sys.stdout replaced")
if getattr(sys.stdout, "encoding", None) != getattr(before, "encoding", None):
    problems.append("sys.stdout reconfigured")
if "crimson_rs" in sys.modules:
    problems.append("crimson_rs imported")
sys.stderr.write("PROBLEMS:" + ";".join(problems))
"""


def _run(args: list[str], env: dict[str, str] | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
        check=False,
        env=env,
        cwd=TOOLS_DIR,
    )


def test_suite_finds_the_scripts() -> None:
    assert SCRIPTS, "no runnable scripts discovered under tools/"
    assert any(p.name == "dump_challenges.py" for p in SCRIPTS)


@pytest.mark.parametrize("script", SCRIPTS, ids=SCRIPT_IDS)
def test_no_args_prints_usage_and_exits_2(script: Path) -> None:
    result = _run([str(script)])
    assert result.returncode == USAGE_EXIT, result.stderr
    assert "usage:" in result.stderr
    assert result.stdout == ""


@pytest.mark.parametrize("script", SCRIPTS, ids=SCRIPT_IDS)
def test_help_works_without_localappdata(script: Path) -> None:
    env = {k: v for k, v in os.environ.items() if k.upper() != "LOCALAPPDATA"}
    result = _run([str(script), "--help"], env=env)
    assert result.returncode == 0, result.stderr
    assert "usage:" in result.stdout


@pytest.mark.parametrize("path", tool_files(), ids=TOOL_IDS)
def test_import_has_no_side_effects(path: Path) -> None:
    result = _run(["-c", _IMPORT_PROBE, str(path), str(TOOLS_DIR)])
    assert result.returncode == 0, result.stderr
    assert result.stdout == "", "import wrote to stdout"
    problems = result.stderr.rsplit("PROBLEMS:", 1)[-1]
    assert problems == "", problems
