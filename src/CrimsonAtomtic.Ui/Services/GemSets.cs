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
/// The hardcoded gem sets the user pre-selected. Names are generic
/// ("Built-in Set N") — the actual gem labels resolve at runtime via
/// PALOC, so the dropdown text reflects whatever language is loaded.
/// </summary>
/// <remarks>
/// Every key below is verified to be in the engine's own canonical gem
/// set (<c>item_type == 74 &amp;&amp; category_info == 2501</c>, 190
/// entries on game 2.00) — a mistyped key would otherwise be written
/// into a socket silently, since the save format accepts any u32 as a
/// gem. The comments carry the resolved names so a future reader can
/// spot a drifted key without a game install to hand.
/// <para>
/// Repeats are intentional and legal: a set may name the same gem
/// twice (see set 6), and the apply path writes each slot independently.
/// </para>
/// </remarks>
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

        // 4 — elemental wards + support:
        //   Greater Flameward / 暴走的火焰屏障
        //   Greater Frostward / 暴走的寒霜屏障
        //   Greater Shockward / 暴走的閃電屏障
        //   Solidarity III    / 羈絆III
        //   Equestrian III    / 交流III
        new("Built-in Set 4",
            new uint[] { 1002972, 1002973, 1002974, 1002522, 1000531 }),

        // 5 — armour-type crit spread + disarm:
        //   Disarm III  / 強奪III
        //   Shred III   / 貫穿III      (crit vs fabric armour)
        //   Shatter III / 破裂III      (crit vs plate armour)
        //   Rend III    / 切開III      (crit vs leather armour)
        //   Lightning God's Affliction / 雷神刑罰
        new("Built-in Set 5",
            new uint[] { 1003231, 1003767, 1003761, 1003764, 1002496 }),

        // 6 — attack speed x2 + utility:
        //   Greater Swift x2 / 暴走的疾風 ×2
        //   Parting Gift     / 狡詐禮物
        //   Energy Drain III / 能量吸收III
        //   Fortune III      / 幸運III
        new("Built-in Set 6",
            new uint[] { 1002970, 1002970, 1003700, 1002955, 1002922 }),
    ];
}
