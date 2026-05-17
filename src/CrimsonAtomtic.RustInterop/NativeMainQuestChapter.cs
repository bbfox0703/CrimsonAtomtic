using System.Buffers;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Curated <c>(chapter, arc, mission)</c> rollup for the main story.
/// Backed by the crimson-rs C ABI's <c>main_quest_chapter</c> bridge —
/// a static lookup table sourced from
/// <c>vendor/crimson-rs/docs/ref-gamedata/main-quest-list.md</c>
/// (~170 rows across Prologue + 12 chapters + Epilogue).
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
/// Three mission titles repeat across chapters ("In Ashes",
/// "Reclamation", "The Counterattack"); first-match-by-table-order
/// wins for <see cref="ChapterForMission"/> + <see cref="ArcForMission"/>.
/// Disambiguate by resolving the mission's arc separately and routing
/// through <see cref="ChapterForArc"/>.
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
    /// ("In Ashes", "Reclamation", "The Counterattack") — disambiguate
    /// by pairing with the arc title and routing through
    /// <see cref="ChapterForArc"/>.
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
