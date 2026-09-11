namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Which game table a curated quest-rollup row's title comes from — the
/// kind code the crimson-rs <c>*_table_get_entry_key(s)</c> C ABI reports
/// for <see cref="NativeMainQuestChapter"/> and
/// <see cref="NativeSideQuestFaction"/> rows.
/// </summary>
public enum QuestRollupKeyKind : uint
{
    /// <summary>No game row: a wiki-transcribed title that matches no live
    /// mission, quest or stage title — or, for a main-quest arc, a Prologue
    /// row, which has no arc. <see cref="QuestRollupKey.Key"/> is 0, and
    /// the key lookups never return such a row.</summary>
    Unresolved = 0,

    /// <summary>A <c>missioninfo</c> row (<c>MissionKey</c>); its display
    /// title sits at PALOC <c>lo32 = 0x101</c>.</summary>
    Mission = 1,

    /// <summary>A <c>questinfo</c> row (<c>QuestKey</c>); its display title
    /// sits at PALOC <c>lo32 = 0x100</c>.</summary>
    Quest = 2,
}

/// <summary>
/// The game row a curated quest-rollup title comes from — the key a save
/// actually stores, as opposed to the display string, which a patch can
/// reword. The <c>MissionKey</c> and <c>QuestKey</c> spaces overlap
/// numerically, so <see cref="Key"/> means nothing without
/// <see cref="Kind"/>.
/// </summary>
public readonly record struct QuestRollupKey(QuestRollupKeyKind Kind, uint Key)
{
    /// <summary>
    /// Build from the raw <c>(kind, key)</c> pair the C ABI writes. A kind
    /// code outside 0..2 means the Rust side grew a kind this interop layer
    /// has not learned — fail loudly rather than hand a caller an enum
    /// value no <c>switch</c> expects.
    /// </summary>
    internal static QuestRollupKey FromAbi(uint kind, uint key) => kind switch
    {
        (uint)QuestRollupKeyKind.Unresolved
            or (uint)QuestRollupKeyKind.Mission
            or (uint)QuestRollupKeyKind.Quest => new QuestRollupKey((QuestRollupKeyKind)kind, key),
        _ => throw new InvalidDataException(
            $"crimson-rs reported quest-rollup key kind {kind}; this build knows 0..2 "
            + "— the C ABI grew a kind QuestRollupKeyKind has not learned yet."),
    };
}
