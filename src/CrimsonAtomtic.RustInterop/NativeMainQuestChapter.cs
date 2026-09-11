using System.Buffers;
using System.Globalization;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Curated <c>(chapter, arc, mission)</c> rollup for the main story.
/// Backed by the crimson-rs C ABI's <c>main_quest_chapter</c> bridge —
/// a static lookup table sourced from
/// <c>vendor/crimson-rs/docs/ref-gamedata/main-quest-list.md</c>
/// (170 rows across Prologue + 12 chapters + Epilogue). Every row also
/// records the game row its title comes from — see
/// <see cref="GetEntryKeys"/>.
/// </summary>
/// <remarks>
/// <para>
/// No file load, no handle — pure static data with lazy
/// <c>OnceLock</c> indices on the Rust side. The C# facade is a static
/// class for the same reason; no instance state.
/// </para>
/// <para>
/// The <b>arc</b> layer matches <c>questinfo.pabgb</c> display titles
/// at <c>lo32 = 0x100</c>; the <b>mission</b> layer matches
/// <c>missioninfo.pabgb</c> display titles at <c>lo32 = 0x101</c>.
/// Callers can chain <c>QuestKey → arc → chapter</c> via the existing
/// <c>crimson_questinfo_lookup_display_name</c>.
/// </para>
/// <para>
/// <b>Prefer the key lookups.</b> <see cref="ChapterForMissionKey"/>,
/// <see cref="ArcForMissionKey"/> and <see cref="ChapterForQuestKey"/>
/// take the <c>MissionKey</c> / <c>QuestKey</c> a save stores, so they
/// keep answering when a patch retitles a mission and they are exact
/// where the title lookups are not: three mission titles repeat across
/// chapters ("In Ashes", "Reclamation", "The Counterattack"), and for
/// those <see cref="ChapterForMission"/> + <see cref="ArcForMission"/>
/// can only return the first match by table order. The titles were
/// reconciled against the live 2.02 English PALOC upstream; the few
/// wiki-only titles with no live counterpart stay
/// <see cref="QuestRollupKeyKind.Unresolved"/> — the title lookups still
/// answer for them, the key lookups cannot.
/// </para>
/// </remarks>
public static class NativeMainQuestChapter
{
    /// <summary>Total row count in the curated table.</summary>
    public static int EntryCount
    {
        get
        {
            var rc = NativeMethods.MainQuestTableEntryCount(out var count);
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_main_quest_table_entry_count failed: {ErrorName(rc)}");
            }
            return (int)count;
        }
    }

    /// <summary>
    /// Read row <paramref name="index"/> as a <c>(Chapter, Arc, Mission)</c>
    /// tuple. <c>Arc</c> is empty for Prologue rows. Returns null when
    /// <paramref name="index"/> is out of range.
    /// </summary>
    public static (string Chapter, string Arc, string Mission)? GetEntry(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            // Two-call: probe required sizes with all bufs null/0 first.
            nuint chapReq = 0, arcReq = 0, missReq = 0;
            var rc = NativeMethods.MainQuestTableGetEntry(
                (uint)index,
                null, 0, out chapReq,
                null, 0, out arcReq,
                null, 0, out missReq);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_main_quest_table_get_entry({index}) size query failed: {ErrorName(rc)}");
            }
            var chapBuf = ArrayPool<byte>.Shared.Rent((int)chapReq);
            var arcBuf = ArrayPool<byte>.Shared.Rent((int)arcReq);
            var missBuf = ArrayPool<byte>.Shared.Rent((int)missReq);
            try
            {
                fixed (byte* pc = chapBuf)
                fixed (byte* pa = arcBuf)
                fixed (byte* pm = missBuf)
                {
                    rc = NativeMethods.MainQuestTableGetEntry(
                        (uint)index,
                        pc, (nuint)chapBuf.Length, out chapReq,
                        pa, (nuint)arcBuf.Length, out arcReq,
                        pm, (nuint)missBuf.Length, out missReq);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_main_quest_table_get_entry({index}) fill failed: {ErrorName(rc)}");
                }
                return (
                    DecodeNulTerminated(chapBuf, chapReq),
                    DecodeNulTerminated(arcBuf, arcReq),
                    DecodeNulTerminated(missBuf, missReq));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chapBuf);
                ArrayPool<byte>.Shared.Return(arcBuf);
                ArrayPool<byte>.Shared.Return(missBuf);
            }
        }
    }

    /// <summary>
    /// Read row <paramref name="index"/>'s game keys — the companion of
    /// <see cref="GetEntry"/>, which returns its strings. <c>Arc</c> is
    /// <see cref="QuestRollupKeyKind.Unresolved"/> for Prologue rows (they
    /// have no arc) and a <see cref="QuestRollupKeyKind.Mission"/> only for
    /// "Cradle of Defense", whose heading is a mission title; every other
    /// arc is a <see cref="QuestRollupKeyKind.Quest"/>. <c>Entry</c> is
    /// <see cref="QuestRollupKeyKind.Unresolved"/> for the wiki-only titles
    /// with no live counterpart. Returns null when <paramref name="index"/>
    /// is out of range.
    /// </summary>
    public static (QuestRollupKey Arc, QuestRollupKey Entry)? GetEntryKeys(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var rc = NativeMethods.MainQuestTableGetEntryKeys(
            (uint)index,
            out var arcKind, out var arcKey,
            out var entryKind, out var entryKey);
        if (rc == NativeMethods.OUT_OF_RANGE)
        {
            return null;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_main_quest_table_get_entry_keys({index}) failed: {ErrorName(rc)}");
        }
        return (QuestRollupKey.FromAbi(arcKind, arcKey), QuestRollupKey.FromAbi(entryKind, entryKey));
    }

    /// <summary>
    /// Resolve a quest arc display title (bold bullets in the source MD —
    /// e.g. "Trials of Kindness", "Journey's End") to its chapter heading.
    /// Returns null when the arc isn't in the curated set.
    /// </summary>
    public static string? ChapterForArc(string arcTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(arcTitle);
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestChapterForArc(arcTitle, null, 0, out req);
            return DecodeLookupResult(rc, req, arcTitle, nameof(NativeMethods.MainQuestChapterForArc),
                (buf, len) => NativeMethods.MainQuestChapterForArc(arcTitle, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a mission display title (e.g. "Where Rumors Gather") to its
    /// chapter heading. Returns null when the mission isn't in the curated
    /// set. First-match-by-table-order for the three repeated titles
    /// ("In Ashes", "Reclamation", "The Counterattack") — a caller holding
    /// the <c>MissionKey</c> should use <see cref="ChapterForMissionKey"/>,
    /// which is exact.
    /// </summary>
    public static string? ChapterForMission(string missionTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(missionTitle);
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestChapterForMission(missionTitle, null, 0, out req);
            return DecodeLookupResult(rc, req, missionTitle, nameof(NativeMethods.MainQuestChapterForMission),
                (buf, len) => NativeMethods.MainQuestChapterForMission(missionTitle, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a mission display title to its quest arc title. Prologue
    /// missions have no arc and return the empty string. Returns null
    /// when the mission isn't in the curated set.
    /// </summary>
    public static string? ArcForMission(string missionTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(missionTitle);
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestArcForMission(missionTitle, null, 0, out req);
            return DecodeLookupResult(rc, req, missionTitle, nameof(NativeMethods.MainQuestArcForMission),
                (buf, len) => NativeMethods.MainQuestArcForMission(missionTitle, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a <c>MissionKey</c> — the key a save stores — to its chapter
    /// heading. Unlike <see cref="ChapterForMission"/> this never goes
    /// through the display title, so it survives a retitle and is exact
    /// for the repeated titles ("In Ashes" is 1000160 in the Prologue and
    /// 1000783 in Chapter 6). Returns null when the key isn't in the
    /// curated set.
    /// </summary>
    public static string? ChapterForMissionKey(uint missionKey)
    {
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestChapterForMissionKey(missionKey, null, 0, out req);
            return DecodeLookupResult(rc, req, KeyForErr(missionKey), nameof(NativeMethods.MainQuestChapterForMissionKey),
                (buf, len) => NativeMethods.MainQuestChapterForMissionKey(missionKey, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a <c>MissionKey</c> to its quest arc title — the empty
    /// string for Prologue missions, as with <see cref="ArcForMission"/>.
    /// Returns null when the key isn't in the curated set.
    /// </summary>
    public static string? ArcForMissionKey(uint missionKey)
    {
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestArcForMissionKey(missionKey, null, 0, out req);
            return DecodeLookupResult(rc, req, KeyForErr(missionKey), nameof(NativeMethods.MainQuestArcForMissionKey),
                (buf, len) => NativeMethods.MainQuestArcForMissionKey(missionKey, buf, len, out _));
        }
    }

    /// <summary>
    /// Resolve a <c>QuestKey</c> to its chapter heading: the key of an
    /// arc's quest (e.g. 1000027 <c>Quest_MeetAlustain_Test</c>, "Trials
    /// of Kindness") or of a quest-kind row (10001 <c>Quest_Intro</c>, the
    /// Prologue's "Ambush"). Returns null when the key isn't in the curated
    /// set. <c>MissionKey</c> and <c>QuestKey</c> overlap numerically,
    /// which is why this is its own entry point.
    /// </summary>
    public static string? ChapterForQuestKey(uint questKey)
    {
        unsafe
        {
            nuint req = 0;
            var rc = NativeMethods.MainQuestChapterForQuestKey(questKey, null, 0, out req);
            return DecodeLookupResult(rc, req, KeyForErr(questKey), nameof(NativeMethods.MainQuestChapterForQuestKey),
                (buf, len) => NativeMethods.MainQuestChapterForQuestKey(questKey, buf, len, out _));
        }
    }

    private unsafe delegate int FillCallback(byte* buf, nuint bufLen);

    private static unsafe string? DecodeLookupResult(
        int probeRc, nuint required, string inputForErr, string apiName, FillCallback fill)
    {
        if (probeRc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        if (probeRc != NativeMethods.BUFFER_TOO_SMALL && probeRc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(probeRc,
                $"{apiName}('{inputForErr}') size query failed: {ErrorName(probeRc)}");
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
                    $"{apiName}('{inputForErr}') fill failed: {ErrorName(fillRc)}");
            }
            return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string KeyForErr(uint key) => key.ToString(CultureInfo.InvariantCulture);

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
