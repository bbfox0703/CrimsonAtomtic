namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// A loaded PALOC localization table — maps engine string keys
/// (e.g. <c>ITEM_GOLD</c>) to localized display strings (e.g. <c>"Gold"</c>).
/// Wraps the <c>crimson_paloc_*</c> C ABI surface exposed by
/// <c>vendor/crimson-rs</c>.
/// </summary>
/// <remarks>
/// Implementations own a native handle and must be disposed.
/// </remarks>
public interface IPalocCatalog : IDisposable
{
    /// <summary>Total number of (key, value) pairs in the table.</summary>
    int EntryCount { get; }

    /// <summary>
    /// Look up the localized text for <paramref name="key"/>. Returns
    /// <c>null</c> when the key isn't in the table; does not throw for
    /// the common "not found" case (mirrors <c>crimson_paloc_lookup</c>
    /// surfacing NOT_FOUND distinctly from BUFFER_TOO_SMALL).
    /// </summary>
    string? Lookup(string key);

    /// <summary>
    /// Get the (key, value) pair at <paramref name="index"/> in insertion
    /// order. Returns <c>null</c> when <paramref name="index"/> is out of
    /// range.
    /// </summary>
    (string Key, string Value)? GetEntry(int index);
}
