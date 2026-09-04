"""Resolve where an install keeps its gamedata tables and paloc files.

Crimson Desert 2.01 renamed the `0008` gamedata directory and every one of
its file extensions, and split each language's single paloc blob into one
file per namespace:

    1.05 - 2.00   gamedata/binary__/client/bin/<table>.pabgb / .pabgh
                  gamedata/stringtable/binary__/localizationstring_<lang>.paloc
    2.01+         gamedata/binarystaticinfo__/bin/<table>.staticinfobody
                                                 /<table>.staticinfoheader
                  gamedata/stringtable/binary__/<lang>/<namespace>.paloc

The file *contents* did not change, so every parser is untouched and only
the lookup path moved. Resolution is newest-layout-first with a fallback,
so a kept pre-2.01 install still works.

Mirrors `vendor/crimson-rs/scripts/gamedata_layout.py` (upstream's own
test helper) and `src/CrimsonAtomtic.RustInterop/GameDataLayout.cs`.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

#: PAZ group holding the static-info gamedata tables.
GAMEDATA_GROUP = "0008"

#: Archive directory holding the localization files — or, since 2.01, the
#: per-language subdirectories holding them. The root itself did not move.
PALOC_ROOT = "gamedata/stringtable/binary__"

_PALOC_PREFIX = "localizationstring_"
_PALOC_SUFFIX = ".paloc"


@dataclass(frozen=True)
class BinLayout:
    """One naming scheme for the static-info tables."""

    dir: str
    body_ext: str
    header_ext: str

    def body(self, table_stem: str) -> str:
        """`"skill"` -> `skill.pabgb` / `skill.staticinfobody`."""
        return f"{table_stem}.{self.body_ext}"

    def header(self, table_stem: str) -> str:
        """`"skill"` -> `skill.pabgh` / `skill.staticinfoheader`."""
        return f"{table_stem}.{self.header_ext}"


#: Newest layout first, so a current install resolves on the first probe.
BIN_LAYOUTS: tuple[BinLayout, ...] = (
    BinLayout("gamedata/binarystaticinfo__/bin", "staticinfobody", "staticinfoheader"),
    BinLayout("gamedata/binary__/client/bin", "pabgb", "pabgh"),
)


def _read_pamt(game_dir: str | Path, group: str) -> dict | None:
    """Parse one group's manifest, or `None` when it isn't readable."""
    import crimson_rs  # type: ignore[import-not-found]

    pamt_path = Path(game_dir) / group / "0.pamt"
    if not pamt_path.is_file():
        return None
    try:
        return crimson_rs.parse_pamt_bytes(pamt_path.read_bytes())
    except Exception:
        return None


def _directory_names(pamt: dict) -> set[str]:
    return {d.get("path") or d.get("name") or "" for d in pamt["directories"]}


def resolve_bin_layout(
    game_dir: str | Path, group: str = GAMEDATA_GROUP
) -> BinLayout:
    """Pick the static-info layout this install actually ships.

    Raises `LookupError` when the manifest is unreadable or holds neither
    known layout — the latter means Pearl Abyss moved the tables again and
    `BIN_LAYOUTS` needs a new entry.
    """
    pamt = _read_pamt(game_dir, group)
    if pamt is None:
        raise LookupError(
            f"cannot read {Path(game_dir) / group / '0.pamt'} — "
            f"is the game installed at {game_dir}?"
        )
    present = _directory_names(pamt)
    for layout in BIN_LAYOUTS:
        if layout.dir in present:
            return layout
    raise LookupError(
        f"no known static-info directory in {group}/0.pamt; tried "
        + ", ".join(repr(x.dir) for x in BIN_LAYOUTS)
        + ". The gamedata tables moved again — add the new layout to "
        "BIN_LAYOUTS in tools/common/gamedata_layout.py."
    )


def paloc_files(
    game_dir: str | Path, group: str, lang: str
) -> tuple[str, tuple[str, ...]] | None:
    """Where one language's paloc file(s) live in `group`.

    Returns `(archive directory, filenames)` — one filename pre-2.01,
    many (sorted, so load order is stable) from 2.01 on. `None` when the
    group's manifest or the language is absent, which is the caller's cue
    to keep probing other groups.
    """
    pamt = _read_pamt(game_dir, group)
    if pamt is None:
        return None

    lang_dir = f"{PALOC_ROOT}/{lang}"
    for directory in pamt["directories"]:
        path = directory.get("path") or directory.get("name") or ""
        if path == lang_dir:
            # 2.01+: the directory itself is the language.
            names = tuple(sorted(
                f["name"] for f in directory["files"]
                if f["name"].endswith(_PALOC_SUFFIX)
            ))
            return (path, names) if names else None

    legacy_name = f"{_PALOC_PREFIX}{lang}{_PALOC_SUFFIX}"
    for directory in pamt["directories"]:
        path = directory.get("path") or directory.get("name") or ""
        if path != PALOC_ROOT:
            continue
        if any(f["name"] == legacy_name for f in directory["files"]):
            return (path, (legacy_name,))
    return None
