using System.Buffers;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Curated <c>(quest, faction)</c> rollup for side quests. Backed by
/// the crimson-rs C ABI's <c>side_quest_faction</c> bridge — a static
/// lookup table sourced from
/// <c>vendor/crimson-rs/docs/ref-gamedata/side-quest-list.md</c>
/// (84 quests across 22 factions).
/// </summary>
/// <remarks>
/// <para>
/// Sibling of <see cref="NativeMainQuestChapter"/>. Side quests are
/// organized by faction rather than chapter/arc, so the bridge ships
/// both directions: quest→faction (1:1) and faction→ordered list of
/// quests.
/// </para>
/// <para>
/// <b>Caveat</b>: the source MD contains one preserved typo
/// ("Encirlement on the Cliff" — canonical English is
/// "Encirclement"). If a live PALOC cross-check returns the correct
/// spelling, the row needs a one-character fix at the vendor side.
/// User-curated list — completeness vs. shipped game content not
/// guaranteed; quests outside the MD return <c>null</c>.
/// </para>
/// </remarks>
public static class NativeSideQuestFaction
{
    /// <summary>Total row count in the curated table (~84).</summary>
    public static int EntryCount
    {
        get
        {
            var rc = NativeMethods.SideQuestTableEntryCount(out var count);
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_side_quest_table_entry_count failed: {ErrorName(rc)}");
            }
            return (int)count;
        }
    }

    /// <summary>
    /// Read row <paramref name="index"/> as a <c>(Quest, Faction)</c>
    /// tuple. Returns null when <paramref name="index"/> is out of range.
    /// </summary>
    public static (string Quest, string Faction)? GetEntry(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            nuint qReq = 0, fReq = 0;
            var rc = NativeMethods.SideQuestTableGetEntry(
                (uint)index,
                null, 0, out qReq,
                null, 0, out fReq);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_side_quest_table_get_entry({index}) size query failed: {ErrorName(rc)}");
            }
            var qBuf = ArrayPool<byte>.Shared.Rent((int)qReq);
            var fBuf = ArrayPool<byte>.Shared.Rent((int)fReq);
            try
            {
                fixed (byte* pq = qBuf)
                fixed (byte* pf = fBuf)
                {
                    rc = NativeMethods.SideQuestTableGetEntry(
                        (uint)index,
                        pq, (nuint)qBuf.Length, out qReq,
                        pf, (nuint)fBuf.Length, out fReq);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_side_quest_table_get_entry({index}) fill failed: {ErrorName(rc)}");
                }
                return (
                    DecodeNulTerminated(qBuf, qReq),
                    DecodeNulTerminated(fBuf, fReq));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(qBuf);
                ArrayPool<byte>.Shared.Return(fBuf);
            }
        }
    }

    /// <summary>
    /// Resolve a side-quest display title to its faction (1:1). Returns
    /// null when the quest isn't in the curated set.
    /// </summary>
    public static string? FactionForQuest(string questTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(questTitle);
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.SideQuestFactionForQuest(questTitle, null, 0, out req);
            if (rc == NativeMethods.NOT_FOUND)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_side_quest_faction_for_quest('{questTitle}') size query failed: {ErrorName(rc)}");
            }
            if (req <= 1) return string.Empty;
            var rented = ArrayPool<byte>.Shared.Rent((int)req);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.SideQuestFactionForQuest(
                        questTitle, b, (nuint)rented.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_side_quest_faction_for_quest('{questTitle}') fill failed: {ErrorName(rc)}");
                }
                return Encoding.UTF8.GetString(rented, 0, (int)req - 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Number of curated quests under the given faction. Returns 0
    /// when the faction has no quests in the table (i.e. unknown
    /// faction).
    /// </summary>
    public static int QuestCountForFaction(string factionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(factionName);
        var rc = NativeMethods.SideQuestQuestCountForFaction(factionName, out var count);
        if (rc == NativeMethods.NOT_FOUND)
        {
            return 0;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_side_quest_quest_count_for_faction('{factionName}') failed: {ErrorName(rc)}");
        }
        return (int)count;
    }

    /// <summary>
    /// Quest title at position <paramref name="index"/> within the
    /// faction's quest list. Returns null when out of range or the
    /// faction is unknown.
    /// </summary>
    public static string? QuestAtForFaction(string factionName, int index)
    {
        ArgumentException.ThrowIfNullOrEmpty(factionName);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.SideQuestQuestAtForFaction(
                factionName, (uint)index, null, 0, out req);
            if (rc == NativeMethods.NOT_FOUND || rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_side_quest_quest_at_for_faction('{factionName}', {index}) size query failed: {ErrorName(rc)}");
            }
            if (req <= 1) return string.Empty;
            var rented = ArrayPool<byte>.Shared.Rent((int)req);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.SideQuestQuestAtForFaction(
                        factionName, (uint)index, b, (nuint)rented.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_side_quest_quest_at_for_faction('{factionName}', {index}) fill failed: {ErrorName(rc)}");
                }
                return Encoding.UTF8.GetString(rented, 0, (int)req - 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static string DecodeNulTerminated(byte[] buf, nuint required)
    {
        if (required <= 1) return string.Empty;
        return Encoding.UTF8.GetString(buf, 0, (int)required - 1);
    }

    private static string ErrorName(int code) => code switch
    {
        NativeMethods.OK                    => "OK",
        NativeMethods.NULL_ARG              => "NULL_ARG",
        NativeMethods.OUT_OF_RANGE          => "OUT_OF_RANGE",
        NativeMethods.BUFFER_TOO_SMALL      => "BUFFER_TOO_SMALL",
        NativeMethods.NOT_FOUND             => "NOT_FOUND",
        NativeMethods.PANIC                 => "PANIC",
        _                                   => $"UNKNOWN({code})",
    };
}
