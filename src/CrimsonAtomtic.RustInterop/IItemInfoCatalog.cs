namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Bridge between a save file's <c>u32</c> item IDs and the localization
/// string keys that PALOC turns into display names. Wraps the
/// <c>crimson_iteminfo_*</c> C ABI surface.
/// </summary>
/// <remarks>
/// Each entry is the <c>(key, string_key)</c> pair from an
/// <c>ItemInfo</c> record in <c>iteminfo.pabgb</c>. The other ~100
/// fields on the Rust side (icon paths, descriptions, equip data, …)
/// are dropped at load time; future PRs can expose them additively
/// without reshaping this surface.
/// </remarks>
public interface IItemInfoCatalog : IDisposable
{
    /// <summary>Total number of items in the loaded <c>iteminfo.pabgb</c>.</summary>
    int EntryCount { get; }

    /// <summary>
    /// Resolve <paramref name="itemKey"/> to its localization string key
    /// (e.g. <c>ITEMNAME_GoldCoin</c>). Returns <c>null</c> when the key
    /// isn't in the loaded table. Does not throw on the common
    /// "not found" path.
    /// </summary>
    string? LookupStringKey(uint itemKey);

    /// <summary>
    /// Get the <c>(itemKey, stringKey)</c> pair at insertion index
    /// <paramref name="index"/>. Returns <c>null</c> when the index is
    /// out of range.
    /// </summary>
    (uint ItemKey, string StringKey)? GetEntry(int index);

    /// <summary>
    /// Resolve <paramref name="itemKey"/> to the game-defined
    /// <c>max_stack_count</c> (u64). Returns <c>null</c> when the
    /// key isn't in the loaded table. The editor uses this to drive a
    /// "Set to max stack" action: if the user wants to top up an item
    /// slot's count, this is the value the game itself considers
    /// valid (so the save stays consistent — exceeding this can break
    /// the bag's dynamic slot computation).
    /// </summary>
    ulong? LookupMaxStackCount(uint itemKey);

    /// <summary>
    /// Resolve <paramref name="itemKey"/> to the <c>StringInfoKey</c>
    /// (u32 hash) of the item's first <c>item_icon_list[0].icon_path</c>.
    /// Returns <c>null</c> when the key isn't in the loaded table OR
    /// the item ships without an icon entry. The icon-extraction
    /// pipeline pipes the returned hash through the stringinfo bridge
    /// to obtain the underlying texture filename
    /// (e.g. <c>"ItemIcon_Prefab_cd_phm_04_arw_0020"</c>), lowercases
    /// it, appends <c>.dds</c>, and PAZ-extracts it.
    /// </summary>
    uint? LookupIconPathHash(uint itemKey);

    /// <summary>
    /// Resolve <paramref name="itemKey"/> to its
    /// <c>look_detail_mission_info</c> (a catalog <c>MissionKey</c>
    /// u32). Returns <c>null</c> when the key isn't in the loaded
    /// table OR the item ships without a mission link (the field is 0
    /// — the overwhelming majority case for vanilla items).
    /// </summary>
    /// <remarks>
    /// Items where this returns non-null are typically quest-reward
    /// items. The Sealed Abyss Artifact series in particular points at
    /// the catalog mission key of the challenge that rewards them
    /// (verified across all 12 SA item samples in slot102 — itemKey
    /// 1002011 (<c>Sealed_Abyss_Artifact_0083</c>) →
    /// <c>look_detail_mission_info = 1000898</c> = Hooves II catalog).
    /// </remarks>
    uint? LookupLookDetailMissionInfo(uint itemKey);

    /// <summary>
    /// Reverse of <see cref="LookupLookDetailMissionInfo"/>: given a
    /// <paramref name="missionKey"/>, return the artifact ItemKey
    /// whose pickup triggers that challenge, or <c>null</c> for
    /// missions that aren't artifact-gated. 1:1 invariant verified
    /// upstream — every <c>Challenge_SealedArtifact_*</c> mission
    /// has exactly one artifact.
    /// </summary>
    uint? LookupArtifactForMission(uint missionKey);

    /// <summary>
    /// Gamedata-defined socket caps for <paramref name="itemKey"/>.
    /// Returns <c>null</c> when the item isn't in iteminfo. Otherwise:
    /// <see cref="ValueTuple{T1,T2}.Item1"/> = <c>UseSocket</c>
    /// (non-zero when the item is socket-capable) and
    /// <see cref="ValueTuple{T1,T2}.Item2"/> = <c>ValidCount</c>
    /// (gamedata-defined max sockets — only meaningful when
    /// <c>UseSocket != 0</c>).
    /// </summary>
    /// <remarks>
    /// Save's <c>_validSocketCount</c> may legitimately diverge from
    /// gamedata's <c>ValidCount</c> (CE-bumped overflows); the editor
    /// surfaces both but doesn't enforce the gamedata cap — per user
    /// request, the v2 editor lets you fill the underlying slot list's
    /// actual capacity regardless of gamedata.
    /// </remarks>
    (byte UseSocket, byte ValidCount)? LookupSocketCaps(uint itemKey);

    /// <summary>
    /// Advisory check — does <paramref name="itemKey"/>'s
    /// gamedata-defined allowed-gem list contain
    /// <paramref name="gemKey"/>? Returns <c>null</c> when the item
    /// is missing from iteminfo; <c>true</c> / <c>false</c> when
    /// known. CE-bypassed gem placements load cleanly in-game even
    /// when this is <c>false</c>; callers decide whether to warn.
    /// </summary>
    bool? SocketAllowsGem(uint itemKey, uint gemKey);

    /// <summary>
    /// Number of itemkeys in the canonical gem set (sorted-ascending
    /// union of every item's <c>socket_item_list</c>). Drives the
    /// authoritative gem-picker dropdown.
    /// </summary>
    int CanonicalGemCount { get; }

    /// <summary>
    /// Read the canonical gem itemkey at sorted-ascending index
    /// <paramref name="index"/>. Returns <c>null</c> past the end.
    /// </summary>
    uint? GetCanonicalGemKey(int index);
}
