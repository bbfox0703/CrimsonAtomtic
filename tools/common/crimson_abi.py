"""Where the crimson-rs C ABI library lives, and the status codes it returns.

The PyO3 module covers most tables; a tool that needs a bridge PyO3 does not
expose yet loads the C ABI build of the same crate through ctypes. That
build is the one MSBuild links into the editor, so both resolve the library
to the same file: `vendor/crimson-rs/target/release/<library>` under the
repo root.

The status codes mirror `pub mod error` in `vendor/crimson-rs/src/c_abi/mod.rs`.
Upstream never reuses a number, so a code here keeps its meaning across
vendor refreshes; a new code there needs a constant here before a tool can
name it.
"""

from __future__ import annotations

import ctypes
import os
import sys
from pathlib import Path

#: Repository root: `tools/common/` is two levels below it.
REPO_ROOT: Path = Path(__file__).resolve().parents[2]

#: The vendored crate, read-only (see vendor/README.md).
CRIMSON_RS_DIR: Path = REPO_ROOT / "vendor" / "crimson-rs"

#: Cargo's release output; `scripts/build_rust.ps1` builds into it.
CRIMSON_RS_RELEASE_DIR: Path = CRIMSON_RS_DIR / "target" / "release"

#: Overrides the default library path, e.g. for a build kept elsewhere.
DLL_ENV_VAR = "CRIMSON_RS_DLL"

OK = 0
NULL_ARG = -1
INVALID_PATH = -2
IO = -3
TOO_SMALL = -4
BAD_MAGIC = -5
UNSUPPORTED_VERSION = -6
PAYLOAD_OUT_OF_RANGE = -7
DECOMPRESS = -8
BODY_PARSE = -9
OUT_OF_RANGE = -10
BUFFER_TOO_SMALL = -11
NOT_FOUND = -16
PANIC = -99

_STATUS_NAMES: dict[int, str] = {
    OK: "OK",
    NULL_ARG: "NULL_ARG",
    INVALID_PATH: "INVALID_PATH",
    IO: "IO",
    TOO_SMALL: "TOO_SMALL",
    BAD_MAGIC: "BAD_MAGIC",
    UNSUPPORTED_VERSION: "UNSUPPORTED_VERSION",
    PAYLOAD_OUT_OF_RANGE: "PAYLOAD_OUT_OF_RANGE",
    DECOMPRESS: "DECOMPRESS",
    BODY_PARSE: "BODY_PARSE",
    OUT_OF_RANGE: "OUT_OF_RANGE",
    BUFFER_TOO_SMALL: "BUFFER_TOO_SMALL",
    NOT_FOUND: "NOT_FOUND",
    PANIC: "PANIC",
}


def status_name(code: int) -> str:
    """`-9` -> `"BODY_PARSE (-9)"`; an unnamed code keeps its number."""
    name = _STATUS_NAMES.get(code)
    return f"{name} ({code})" if name else str(code)


def library_file_name(platform: str | None = None) -> str:
    """The file name cargo gives a `cdylib` named `crimson_rs` on `platform`."""
    platform = platform or sys.platform
    if platform.startswith("win"):
        return "crimson_rs.dll"
    if platform == "darwin":
        return "libcrimson_rs.dylib"
    return "libcrimson_rs.so"


def default_dll_path(platform: str | None = None) -> Path:
    """The release build of the C ABI library inside the vendored crate."""
    return CRIMSON_RS_RELEASE_DIR / library_file_name(platform)


def dll_path() -> Path:
    """`$CRIMSON_RS_DLL` when set, otherwise `default_dll_path()`."""
    override = os.environ.get(DLL_ENV_VAR)
    return Path(override) if override else default_dll_path()


def load_dll(path: Path | None = None) -> ctypes.CDLL:
    """Load the C ABI library, failing with the build step to run.

    Raises `FileNotFoundError` when the file is absent, and `OSError` when
    it exists but fails to load.
    """
    path = path or dll_path()
    if not path.is_file():
        raise FileNotFoundError(
            f"crimson-rs C ABI library not found at {path}. "
            "Build it with scripts\\build_rust.ps1, or point "
            f"{DLL_ENV_VAR} at an existing build."
        )
    return ctypes.CDLL(str(path))
