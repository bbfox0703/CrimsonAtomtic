"""Pure helpers inside the analyze/ scripts."""

from __future__ import annotations

import json
from pathlib import Path
from types import ModuleType

import pytest
from conftest import load_tool

from common import crimson_abi


@pytest.fixture(scope="module")
def catalogs() -> ModuleType:
    return load_tool("analyze/dump_catalogs.py")


@pytest.fixture(scope="module")
def keycases() -> ModuleType:
    return load_tool("analyze/extract_keycases.py")


@pytest.fixture(scope="module")
def challenges() -> ModuleType:
    return load_tool("analyze/dump_challenges.py")


@pytest.fixture(scope="module")
def save_fields() -> ModuleType:
    return load_tool("analyze/dump_save_fields.py")


def test_split_paloc_string_key(catalogs: ModuleType) -> None:
    n = (0x0001_E240 << 32) | (0xABCDEF << 8) | 0x70
    assert catalogs.split_paloc_string_key(str(n)) == (0x0001_E240, 0x70, 0xABCDEF)
    assert catalogs.split_paloc_string_key("not-a-number") == (None, None, None)
    assert catalogs.split_paloc_string_key(str(1 << 64)) == (None, None, None)
    assert catalogs.split_paloc_string_key("-1") == (None, None, None)


def test_coerce_json_safe_hexes_bytes(catalogs: ModuleType) -> None:
    assert catalogs.coerce_json_safe({"a": [b"\x01\xff", (1, b"")]}) == {
        "a": ["01ff", [1, ""]]
    }


def test_load_iteminfo_treats_empty_string_key_as_unresolved(
    keycases: ModuleType, tmp_path: Path
) -> None:
    path = tmp_path / "iteminfo.jsonl"
    rows = [
        {"key": 1, "string_key": "Sword"},
        {"key": 2, "string_key": ""},
        {"key": 3},
    ]
    path.write_text("\n".join(json.dumps(r) for r in rows), encoding="utf-8")
    items = keycases.load_iteminfo(path)
    assert items == {1: "Sword"}

    unresolved = keycases.build_resolves(2, items, {})
    assert unresolved["iteminfo_id"] is None
    assert keycases._is_fully_unresolved(unresolved)
    assert not keycases._is_fully_unresolved(keycases.build_resolves(1, items, {}))


def test_split_internal_name(challenges: ModuleType) -> None:
    assert challenges.split_internal_name(
        "Challenge_SealedArtifact_Mastery_OneHandSword_I"
    ) == (["SealedArtifact", "Mastery", "OneHandSword"], "I")
    assert challenges.split_internal_name("Mission_MiniGame_Duel_Unarmed_0") == (
        ["MiniGame", "Duel", "Unarmed"], "0")
    assert challenges.split_internal_name("Challenge_Abyss") == (["Abyss"], None)


def test_clean_title_strips_static_info_references(challenges: ModuleType) -> None:
    raw = "{StaticInfo:Knowledge:Knowledge_Node_Abyssone_0001#Ethereal Pathway}"
    assert challenges.clean_title(raw) == "Ethereal Pathway"
    assert challenges.clean_title("Plain") == "Plain"
    assert challenges.clean_title(None) is None


def test_parse_ign_challenges_text(challenges: ModuleType, tmp_path: Path) -> None:
    text = (
        "Combat List and Rewards\n"
        "Swordplay\n"
        "Challenge\tGoal\tReward\tCompleted\n"
        "Sword of Trials I\tWin a duel\t100 Silver\t\n"
        "\n"
        "Sword of Trials I\n"
        "Bow Aimed At Fate\tHit\n"
        "three targets\tA bow\t\n"
        "\n"
        "Bow Aimed at Fate\n"
    )
    path = tmp_path / "ign.txt"
    path.write_text(text, encoding="utf-8")
    rows = challenges.parse_ign_challenges_text(path)
    assert [(r["category"], r["subcategory"], r["name"]) for r in rows] == [
        ("Combat", "Swordplay", "Sword of Trials I"),
        ("Combat", "Swordplay", "Bow Aimed At Fate"),
    ]
    assert rows[0]["name_key"] == "sword of trials 1"
    assert rows[1]["goal"] == "Hit\nthree targets"
    assert rows[1]["reward"] == "A bow"


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        (b"\x01\x02", (None, None, "0102")),
        (True, (1, None, None)),
        (False, (0, None, None)),
        (7, (7, None, None)),
        (1.5, (None, 1.5, None)),
        (None, (None, None, None)),
        ("odd", (None, None, "odd")),
    ],
)
def test_split_typed_value(save_fields: ModuleType, raw: object, expected: tuple) -> None:
    assert save_fields._split_typed_value("u32", raw) == expected


def test_mission_info_abi_binds_against_the_built_library(
    challenges: ModuleType,
) -> None:
    path = crimson_abi.dll_path()
    if not path.is_file():
        pytest.skip(f"no C ABI build at {path}")
    abi = challenges.MissionInfoAbi(crimson_abi.load_dll(path))
    rc, handle = abi.load_missioninfo(b"", b"")
    assert rc == crimson_abi.OK
    abi.mi_free(handle)
