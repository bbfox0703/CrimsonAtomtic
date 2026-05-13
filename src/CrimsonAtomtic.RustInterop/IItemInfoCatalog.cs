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
}
