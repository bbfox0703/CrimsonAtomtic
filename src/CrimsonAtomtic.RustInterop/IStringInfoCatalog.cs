namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Resolves <c>StringInfoKey</c> hashes (u32) to their underlying string
/// values — most often texture filenames like <c>cd_icon_arrow_basic.dds</c>
/// referenced from <c>iteminfo.pabgb</c>'s <c>icon_path</c> /
/// <c>map_icon_path</c> fields. Wraps the <c>crimson_string_info_*</c>
/// C ABI surface.
/// </summary>
/// <remarks>
/// The downstream icon-extraction pipeline feeds an item's
/// <c>icon_path</c> (already parsed out of <c>iteminfo</c>) through
/// <see cref="LookupByHash(uint)"/> to get the <c>.dds</c> filename,
/// then hands that filename to <see cref="IPazExtractor"/> for the raw
/// bytes.
/// </remarks>
public interface IStringInfoCatalog : IDisposable
{
    /// <summary>Total entries in the loaded <c>stringinfo.pabgb</c>.</summary>
    int EntryCount { get; }

    /// <summary>
    /// Resolve <paramref name="hash"/> to its string value. Returns
    /// <c>null</c> when the hash isn't in the loaded table. Does not
    /// throw on the common "not found" path.
    /// </summary>
    string? LookupByHash(uint hash);

    /// <summary>
    /// Get the <c>(hash, value)</c> pair at insertion index
    /// <paramref name="index"/>. Returns <c>null</c> when the index is
    /// out of range.
    /// </summary>
    (uint Hash, string Value)? GetEntry(int index);
}
