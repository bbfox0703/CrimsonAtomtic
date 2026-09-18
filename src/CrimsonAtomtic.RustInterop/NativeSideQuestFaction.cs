using System.Buffers;
using System.Globalization;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Curated <c>(quest, faction)</c> rollup for side quests. Backed by
/// the crimson-rs C ABI's <c>side_quest_faction</c> bridge — a static
/// lookup table sourced from
/// <c>vendor/crimson-rs/docs/ref-gamedata/side-quest-list.md</c>
/// (84 rows across 23 factions). Every row also records the game row its
/// title comes from — see <see cref="GetEntryKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sibling of <see cref="NativeMainQuestChapter"/>. Side quests are
/// organized by faction rather than chapter/arc, so the bridge ships
/// both directions: quest→faction (1:1) and faction→ordered list of
/// quests.
/// </para>
/// <para>
/// Despite the table's name, most rows are <i>missions</i>
/// (<c>missioninfo</c>, PALOC <c>lo32 = 0x101</c>) — 64 of the 84 at the
/// 2.02 reconciliation — and the rest are quests; the title column holds
/// whichever the row is. <b>Prefer the key lookups</b>:
/// <see cref="FactionForMissionKey"/> / <see cref="FactionForQuestKey"/>
/// take the key a save stores and keep answering when a patch retitles
/// the row. The titles were reconciled against the live 2.02 English
/// PALOC upstream, which also settled the old "Encirlement on the Cliff"
/// transcription typo as "Encirclement", and follow the live strings
/// since (2.03 retitled one mission; its key did not move). User-curated list —
/// completeness vs. shipped game content not guaranteed; quests outside
/// the MD return <c>null</c>.
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
    /// Read row <paramref name="index"/>'s game key — the companion of
    /// <see cref="GetEntry"/>, which returns its strings. Every side-quest
    /// row resolves, so the kind is <see cref="QuestRollupKeyKind.Mission"/>
    /// or <see cref="QuestRollupKeyKind.Quest"/>. Returns null when
    /// <paramref name="index"/> is out of range.
    /// </summary>
    public static QuestRollupKey? GetEntryKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var rc = NativeMethods.SideQuestTableGetEntryKey((uint)index, out var kind, out var key);
        if (rc == NativeMethods.OUT_OF_RANGE)
        {
            return null;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_side_quest_table_get_entry_key({index}) failed: {ErrorName(rc)}");
        }
        return QuestRollupKey.FromAbi(kind, key);
    }

    /// <summary>
    /// Resolve a side-quest row's display title (a mission or quest title —
    /// see the remarks) to its faction (1:1). Returns null when the title
    /// isn't in the curated set.
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
    /// Resolve a <c>MissionKey</c> — the key a save stores — to its
    /// faction, independent of the display title. Returns null when no
    /// curated row is that mission.
    /// </summary>
    public static string? FactionForMissionKey(uint missionKey)
    {
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.SideQuestFactionForMissionKey(missionKey, null, 0, out req);
            return DecodeKeyLookup(rc, req, missionKey, "crimson_side_quest_faction_for_mission_key",
                (buf, len) => NativeMethods.SideQuestFactionForMissionKey(missionKey, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a <c>QuestKey</c> to its faction. Same contract as
    /// <see cref="FactionForMissionKey"/>; the two key spaces overlap
    /// numerically, which is why they are separate entry points.
    /// </summary>
    public static string? FactionForQuestKey(uint questKey)
    {
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.SideQuestFactionForQuestKey(questKey, null, 0, out req);
            return DecodeKeyLookup(rc, req, questKey, "crimson_side_quest_faction_for_quest_key",
                (buf, len) => NativeMethods.SideQuestFactionForQuestKey(questKey, buf, len, out _));
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

    private unsafe delegate int FillCallback(byte* buf, nuint bufLen);

    /// <summary>Two-call decode shared by the key lookups: NOT_FOUND →
    /// null, otherwise size, fill and decode the faction name.</summary>
    private static unsafe string? DecodeKeyLookup(
        int probeRc, nuint required, uint key, string apiName, FillCallback fill)
    {
        if (probeRc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        var keyText = key.ToString(CultureInfo.InvariantCulture);
        if (probeRc != NativeMethods.BUFFER_TOO_SMALL && probeRc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(probeRc,
                $"{apiName}({keyText}) size query failed: {ErrorName(probeRc)}");
        }
        if (required <= 1)
        {
            return string.Empty;
        }
        var rented = ArrayPool<byte>.Shared.Rent((int)required);
        try
        {
            int fillRc;
            fixed (byte* b = rented)
            {
                fillRc = fill(b, (nuint)rented.Length);
            }
            if (fillRc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(fillRc,
                    $"{apiName}({keyText}) fill failed: {ErrorName(fillRc)}");
            }
            return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
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
