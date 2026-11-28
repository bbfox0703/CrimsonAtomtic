"""The shared modules under tools/common/."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest
from conftest import TOOLS_DIR

from common import crimson_abi, gamedata_layout, paths

# ── crimson_abi ──────────────────────────────────────────────────────────────


def test_repo_root_is_the_parent_of_tools() -> None:
    assert TOOLS_DIR.parent == crimson_abi.REPO_ROOT
    assert (crimson_abi.REPO_ROOT / "tools" / "common" / "crimson_abi.py").is_file()


def test_default_dll_path_is_the_vendored_release_build() -> None:
    expected = (
        crimson_abi.REPO_ROOT / "vendor" / "crimson-rs" / "target" / "release"
        / "crimson_rs.dll"
    )
    assert crimson_abi.default_dll_path("win32") == expected


@pytest.mark.parametrize(
    ("platform", "name"),
    [("win32", "crimson_rs.dll"), ("linux", "libcrimson_rs.so"),
     ("darwin", "libcrimson_rs.dylib")],
)
def test_library_file_name_follows_cargo(platform: str, name: str) -> None:
    assert crimson_abi.library_file_name(platform) == name


def test_dll_path_honours_the_env_override(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.delenv(crimson_abi.DLL_ENV_VAR, raising=False)
    assert crimson_abi.dll_path() == crimson_abi.default_dll_path()
    custom = tmp_path / "custom.dll"
    monkeypatch.setenv(crimson_abi.DLL_ENV_VAR, str(custom))
    assert crimson_abi.dll_path() == custom


def test_load_dll_names_the_missing_file(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match="absent.dll"):
        crimson_abi.load_dll(tmp_path / "absent.dll")


def test_status_codes_match_the_vendored_c_abi() -> None:
    source = crimson_abi.CRIMSON_RS_DIR / "src" / "c_abi" / "mod.rs"
    if not source.is_file():
        pytest.skip("vendor/crimson-rs is not cloned")
    upstream = {
        name: int(value)
        for name, value in re.findall(
            r"pub const ([A-Z_]+): i32 = (-?\d+);", source.read_text(encoding="utf-8")
        )
    }
    mirrored = {
        name: value
        for name, value in vars(crimson_abi).items()
        if name.isupper() and isinstance(value, int)
    }
    assert mirrored, "no status constants found in crimson_abi"
    for name, value in mirrored.items():
        assert upstream.get(name) == value, (
            f"{name}: ours {value}, upstream {upstream.get(name)}"
        )


def test_status_name_reads_known_and_unknown_codes() -> None:
    assert crimson_abi.status_name(crimson_abi.BODY_PARSE) == "BODY_PARSE (-9)"
    assert crimson_abi.status_name(-12345) == "-12345"


# ── paths ────────────────────────────────────────────────────────────────────


def test_save_root_needs_localappdata(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("LOCALAPPDATA", raising=False)
    with pytest.raises(RuntimeError):
        paths.save_root()


def test_save_root_display_names_the_same_folder(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    shown = paths.SAVE_ROOT_DISPLAY.replace("%LOCALAPPDATA%", str(tmp_path))
    assert Path(shown.replace("\\", "/")) == paths.save_root()


def test_game_roots_put_the_env_var_first(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.delenv(paths.GAME_ROOT_ENV_VAR, raising=False)
    assert paths.game_roots() == paths.DEFAULT_GAME_ROOTS
    monkeypatch.setenv(paths.GAME_ROOT_ENV_VAR, str(tmp_path))
    assert paths.game_roots() == (tmp_path, *paths.DEFAULT_GAME_ROOTS)


def _fake_install(root: Path) -> Path:
    for sub in ("bin64", "meta", "0000"):
        (root / sub).mkdir(parents=True)
    return root


def test_discover_installs_finds_direct_and_nested_installs(tmp_path: Path) -> None:
    direct = _fake_install(tmp_path / "steam")
    archive = tmp_path / "archive"
    nested = _fake_install(archive / "2.05")
    (archive / "notes").mkdir()
    stray_file = tmp_path / "a-file"
    stray_file.write_text("x")
    found = paths.discover_installs((direct, archive, stray_file, tmp_path / "absent"))
    assert found == [direct, nested]


def test_list_save_users_keeps_numeric_folders(tmp_path: Path) -> None:
    for name in ("123", "456", "not-a-user"):
        (tmp_path / name).mkdir()
    assert [p.name for p in paths.list_save_users(tmp_path)] == ["123", "456"]


# ── gamedata_layout ──────────────────────────────────────────────────────────


def _install_with_pamt(tmp_path: Path, group: str = "0008") -> Path:
    (tmp_path / group).mkdir()
    (tmp_path / group / "0.pamt").write_bytes(b"pamt")
    return tmp_path


def _fake_crimson_rs(monkeypatch: pytest.MonkeyPatch, parse: Any) -> None:
    monkeypatch.setitem(sys.modules, "crimson_rs", SimpleNamespace(parse_pamt_bytes=parse))


def test_missing_manifest_reads_as_no_install(tmp_path: Path) -> None:
    with pytest.raises(LookupError, match="is the game installed"):
        gamedata_layout.resolve_bin_layout(tmp_path)
    assert gamedata_layout.paloc_files(tmp_path, "0020", "eng") is None


def test_unparseable_manifest_keeps_the_parser_error(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    def parse(_data: bytes) -> dict:
        raise ValueError("bad trie")

    _fake_crimson_rs(monkeypatch, parse)
    game = _install_with_pamt(tmp_path)
    with pytest.raises(LookupError, match="bad trie") as info:
        gamedata_layout.resolve_bin_layout(game)
    assert isinstance(info.value.__cause__, ValueError)
    assert "is the game installed" not in str(info.value)


def test_unexpected_errors_are_not_swallowed(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    def parse(_data: bytes) -> dict:
        raise KeyError("bug in the caller")

    _fake_crimson_rs(monkeypatch, parse)
    with pytest.raises(KeyError):
        gamedata_layout.resolve_bin_layout(_install_with_pamt(tmp_path))


def test_resolve_bin_layout_prefers_the_newest_layout(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    pamt = {"directories": [
        {"path": "gamedata/binary__/client/bin", "files": []},
        {"path": "gamedata/binarystaticinfo__/bin", "files": []},
    ]}
    _fake_crimson_rs(monkeypatch, lambda _data: pamt)
    layout = gamedata_layout.resolve_bin_layout(_install_with_pamt(tmp_path))
    assert layout.dir == "gamedata/binarystaticinfo__/bin"
    assert layout.body("missioninfo") == "missioninfo.staticinfobody"
    assert layout.header("missioninfo") == "missioninfo.staticinfoheader"


def test_paloc_files_reads_both_layouts(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    root = gamedata_layout.PALOC_ROOT
    split = {"directories": [{"path": f"{root}/eng", "files": [
        {"name": "b.paloc"}, {"name": "a.paloc"}, {"name": "readme.txt"},
    ]}]}
    legacy = {"directories": [{"path": root, "files": [
        {"name": "localizationstring_eng.paloc"},
    ]}]}
    game = _install_with_pamt(tmp_path, "0020")

    _fake_crimson_rs(monkeypatch, lambda _data: split)
    assert gamedata_layout.paloc_files(game, "0020", "eng") == (
        f"{root}/eng", ("a.paloc", "b.paloc"))

    _fake_crimson_rs(monkeypatch, lambda _data: legacy)
    assert gamedata_layout.paloc_files(game, "0020", "eng") == (
        root, ("localizationstring_eng.paloc",))
    assert gamedata_layout.paloc_files(game, "0020", "jpn") is None
