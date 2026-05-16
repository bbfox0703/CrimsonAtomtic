namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// A named gem combination — applied via Sockets editor's
/// "Apply Set" toolbar to overwrite an item's socket slots in order.
/// 1..5 entries; out-of-range entries past the item's slot capacity
/// are dropped silently. Per the user's contract, applying a set
/// with N entries overwrites slots 0..N-1 ONLY — slots [N..max] are
/// left alone (so a 1-entry set just sets slot 0, etc.).
/// </summary>
public sealed record GemSet(string Label, IReadOnlyList<uint> GemKeys)
{
    /// <summary>Maximum gems per set — matches the engine's per-item socket cap.</summary>
    public const int MaxGems = 5;
}

/// <summary>
/// Three hardcoded gem sets the user pre-selected. Names are
/// generic ("Built-in Set 1/2/3") — the actual gem labels resolve
/// at runtime via PALOC so the dropdown text reflects whatever
/// language the user has loaded.
/// </summary>
public static class BuiltInGemSets
{
    public static readonly IReadOnlyList<GemSet> All =
    [
        new("Built-in Set 1",
            new uint[] { 1002972, 1002973, 1002974, 1002970, 1002970 }),
        new("Built-in Set 2",
            new uint[] { 1002862, 1002979, 1002977, 1002969, 1002606 }),
        new("Built-in Set 3",
            new uint[] { 1002982, 1002969, 1002862, 1002979, 1002977 }),
    ];
}
