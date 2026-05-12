"""Game install discovery and save-file location helpers.

Kept tiny on purpose; do not turn this into a heavyweight registry.
The authoritative list of game paths lives in docs/game-versions.md.
"""

from __future__ import annotations

import os
from pathlib import Path

# Default install roots in priority order: current Steam install first,
# then per-version archives on SSD, then per-version archives on HDD.
DEFAULT_GAME_ROOTS: tuple[Path, ...] = (
    Path(r"D:\SteamLibrary\steamapps\common\Crimson Desert"),
    Path(r"F:\Crimson Desert"),
    Path(r"X:\Crimson Desert"),
)


def save_root() -> Path:
    """Pearl Abyss save root: %LOCALAPPDATA%\\Pearl Abyss\\CD\\save."""
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise RuntimeError("LOCALAPPDATA is not set; cannot locate Pearl Abyss save root")
    return Path(local) / "Pearl Abyss" / "CD" / "save"


def list_save_users(root: Path | None = None) -> list[Path]:
    """Return one path per <UserID> folder under the save root."""
    root = root or save_root()
    if not root.exists():
        return []
    return sorted([p for p in root.iterdir() if p.is_dir() and p.name.isdigit()])


def is_game_install(p: Path) -> bool:
    """A folder is a game install if it has bin64/, meta/, and at least one pack group."""
    return (
        p.is_dir()
        and (p / "bin64").is_dir()
        and (p / "meta").is_dir()
        and (p / "0000").is_dir()
    )


def discover_installs(roots: tuple[Path, ...] = DEFAULT_GAME_ROOTS) -> list[Path]:
    """Walk the configured roots and return every game install we can see."""
    found: list[Path] = []
    for root in roots:
        if not root.exists():
            continue
        if is_game_install(root):
            found.append(root)
            continue
        # roots like F:\Crimson Desert/ hold per-version subfolders
        for sub in sorted(root.iterdir()):
            if is_game_install(sub):
                found.append(sub)
    return found
