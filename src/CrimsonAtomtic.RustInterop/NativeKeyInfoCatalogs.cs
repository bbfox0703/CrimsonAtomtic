using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

// ─────────────────────────────────────────────────────────────────────────────
//  Six small bridges to the new c_abi key-resolver surfaces:
//
//      Mission / Quest / Stage / Knowledge
//          load_from_bytes, free, entry_count, lookup_string_key,
//          lookup_display_name (chains through PALOC via hashlittle2),
//          get_entry  *
//
//      QuestGauge
//          same minus lookup_display_name (gauges aren't in PALOC)
//
//      Skill
//          two-file load (pabgh + pabgb), no lookup_display_name
//
//  All six share the same two-call buffer dance for string output. The
//  helper [NameBuffer.ReadString] holds that dance once; each catalog
//  composes it with the appropriate [LibraryImport] entry point.
//
//  * get_entry is exposed at the NativeMethods layer but no managed
//    wrapper here — no editor surface needs Mission/Quest/etc.
//    enumeration yet (the keys come from the save, not from a "Browse"
//    dialog). Add a thin wrapper when an enumerator UX appears.
// ─────────────────────────────────────────────────────────────────────────────

internal static class NameBuffer
{
    /// <summary>
    /// Run the canonical two-call buffer dance against a native getter.
    ///
    /// <para>Pass <c>(buf=null, bufLen=0)</c> first to learn the required
    /// size; allocate; pass again to fill. The pattern is identical
    /// across every <c>crimson_*_lookup_*</c> / <c>crimson_*_get_entry</c>
    /// surface, so we abstract it once and let each catalog plug in its
    /// own native delegate.</para>
    ///
    /// <para>The <c>required</c> count from native includes the trailing
    /// NUL, so the decoded string length is <c>required - 1</c>.</para>
    /// </summary>
    public static unsafe string? ReadString(NativeStringGetter call, string errPrefix)
    {
        nuint required = 0;
        var rc = call(null, 0, out required);
        if (rc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc, $"{errPrefix} size query failed: {ErrorName(rc)}");
        }
        if (required <= 1)
        {
            return string.Empty;
        }
        var rented = ArrayPool<byte>.Shared.Rent((int)required);
        try
        {
            fixed (byte* b = rented)
            {
                rc = call(b, (nuint)rented.Length, out _);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc, $"{errPrefix} fill failed: {ErrorName(rc)}");
            }
            return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Native getter signature shared by every two-call lookup.</summary>
    public unsafe delegate int NativeStringGetter(byte* buf, nuint bufLen, out nuint required);

    public static string ErrorName(int code) => code switch
    {
        NativeMethods.OK                 => "OK",
        NativeMethods.NULL_ARG           => "NULL_ARG",
        NativeMethods.INVALID_PATH       => "INVALID_PATH",
        NativeMethods.IO                 => "IO",
        NativeMethods.BODY_PARSE         => "BODY_PARSE",
        NativeMethods.OUT_OF_RANGE       => "OUT_OF_RANGE",
        NativeMethods.BUFFER_TOO_SMALL   => "BUFFER_TOO_SMALL",
        NativeMethods.NOT_FOUND          => "NOT_FOUND",
        NativeMethods.PANIC              => "PANIC",
        _                                => $"UNKNOWN({code})",
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  MissionInfo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>MissionKey (u32)</c> → internal name or PALOC-localized title.
/// Backed by <c>crimson_missioninfo_*</c>. Display-name path needs an
/// English (or alt-language) <see cref="NativePalocCatalog"/> so the
/// hash-hop chain can resolve in one FFI call.
/// </summary>
public sealed class NativeMissionInfoCatalog : IDisposable
{
    /// <summary>PALOC <c>lo32</c> for the individual mission title.</summary>
    public const uint TitleLo32 = 0x101;

    private readonly CrimsonMissionInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeMissionInfoCatalog(CrimsonMissionInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entryCount;
        }
    }

    public static NativeMissionInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.MissionInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_missioninfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonMissionInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.MissionInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_missioninfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeMissionInfoCatalog(handle, (int)count);
            }
        }
    }

    /// <summary>Internal ASCII identifier (e.g. <c>Mission_Intro_Tutorial_I</c>); null if the key isn't in the table.</summary>
    public string? LookupStringKey(uint missionKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.MissionInfoLookupStringKey(_handle, missionKey, buf, bufLen, out required),
                $"crimson_missioninfo_lookup_string_key({missionKey})");
        }
    }

    /// <summary>
    /// Localized display title via the hash hop. <paramref name="lo32"/>
    /// defaults to <see cref="TitleLo32"/> (individual quest title);
    /// pass <c>0x100</c> for the sub-arc heading variant.
    /// </summary>
    public string? LookupDisplayName(uint missionKey, NativePalocCatalog paloc, uint lo32 = TitleLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.MissionInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, missionKey, lo32, buf, bufLen, out required),
                $"crimson_missioninfo_lookup_display_name({missionKey}, 0x{lo32:X})");
        }
    }

    /// <summary>
    /// Enumerate the <paramref name="index"/>'th
    /// <c>(MissionKey, internal_name)</c> pair from the table's insertion
    /// order. Returns <c>null</c> when the index is out of range. Used
    /// by Browse Challenges to walk every entry and filter by
    /// <c>internal_name</c> prefix (e.g. <c>Challenge_*</c>).
    ///
    /// <para>Caveat: the underlying anchor-scan parser may currently
    /// emit rows whose <c>internal_name</c> contains U+FFFD bytes
    /// (16% of missioninfo rows in 1.06 — see issue
    /// <c>001-missioninfo-invalid-utf8-names.md</c>). Callers that
    /// filter by ASCII prefix won't trip on those rows, but callers
    /// iterating blindly should plan to skip names containing
    /// <c>�</c>.</para>
    /// </summary>
    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.MissionInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_missioninfo_get_entry({index}) size query failed: {NameBuffer.ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outKey, string.Empty);
            }
            var rented = System.Buffers.ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.MissionInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_missioninfo_get_entry({index}) fill failed: {NameBuffer.ErrorName(rc)}");
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonMissionInfoHandle : SafeHandle
{
    public CrimsonMissionInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonMissionInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonMissionInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.MissionInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  QuestInfo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>QuestKey (u32)</c> → internal name or PALOC-localized arc heading.
/// Quest entries in the game's terminology are the sub-arc / region
/// headings shown above individual quests ("## Trials of Kindness", etc.).
/// </summary>
public sealed class NativeQuestInfoCatalog : IDisposable
{
    /// <summary>PALOC <c>lo32</c> for the quest arc heading.</summary>
    public const uint ArcLo32 = 0x100;

    private readonly CrimsonQuestInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeQuestInfoCatalog(CrimsonQuestInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeQuestInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.QuestInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_questinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonQuestInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.QuestInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_questinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeQuestInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint questKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.QuestInfoLookupStringKey(_handle, questKey, buf, bufLen, out required),
                $"crimson_questinfo_lookup_string_key({questKey})");
        }
    }

    public string? LookupDisplayName(uint questKey, NativePalocCatalog paloc, uint lo32 = ArcLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.QuestInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, questKey, lo32, buf, bufLen, out required),
                $"crimson_questinfo_lookup_display_name({questKey}, 0x{lo32:X})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonQuestInfoHandle : SafeHandle
{
    public CrimsonQuestInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonQuestInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonQuestInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.QuestInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  StageInfo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>StageKey (u32)</c> → internal name or PALOC-localized title /
/// description. lo32 = 0x101 for the title, 0x102 for the description.
/// </summary>
public sealed class NativeStageInfoCatalog : IDisposable
{
    /// <summary>PALOC <c>lo32</c> for the stage title.</summary>
    public const uint TitleLo32 = 0x101;
    /// <summary>PALOC <c>lo32</c> for the stage description / shop text.</summary>
    public const uint DescriptionLo32 = 0x102;

    private readonly CrimsonStageInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeStageInfoCatalog(CrimsonStageInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeStageInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.StageInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_stageinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonStageInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.StageInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_stageinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeStageInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint stageKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.StageInfoLookupStringKey(_handle, stageKey, buf, bufLen, out required),
                $"crimson_stageinfo_lookup_string_key({stageKey})");
        }
    }

    public string? LookupDisplayName(uint stageKey, NativePalocCatalog paloc, uint lo32 = TitleLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.StageInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, stageKey, lo32, buf, bufLen, out required),
                $"crimson_stageinfo_lookup_display_name({stageKey}, 0x{lo32:X})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonStageInfoHandle : SafeHandle
{
    public CrimsonStageInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonStageInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonStageInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.StageInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  KnowledgeInfo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>KnowledgeKey (u32)</c> → internal name or PALOC-localized title.
/// Knowledge entries live at <c>lo32 = 0x490</c> (title) and
/// <c>0x491</c> (description).
/// </summary>
public sealed class NativeKnowledgeInfoCatalog : IDisposable
{
    public const uint TitleLo32 = 0x490;
    public const uint DescriptionLo32 = 0x491;

    private readonly CrimsonKnowledgeInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeKnowledgeInfoCatalog(CrimsonKnowledgeInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeKnowledgeInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.KnowledgeInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_knowledgeinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonKnowledgeInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.KnowledgeInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_knowledgeinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeKnowledgeInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint knowledgeKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.KnowledgeInfoLookupStringKey(_handle, knowledgeKey, buf, bufLen, out required),
                $"crimson_knowledgeinfo_lookup_string_key({knowledgeKey})");
        }
    }

    public string? LookupDisplayName(uint knowledgeKey, NativePalocCatalog paloc, uint lo32 = TitleLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.KnowledgeInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, knowledgeKey, lo32, buf, bufLen, out required),
                $"crimson_knowledgeinfo_lookup_display_name({knowledgeKey}, 0x{lo32:X})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonKnowledgeInfoHandle : SafeHandle
{
    public CrimsonKnowledgeInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonKnowledgeInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonKnowledgeInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.KnowledgeInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  QuestGaugeInfo  (no PALOC chain — internal name IS the label)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>QuestGaugeKey (u32)</c> → internal name. Gauges aren't in PALOC,
/// so this is the only resolution surface (no <c>LookupDisplayName</c>).
/// </summary>
public sealed class NativeQuestGaugeInfoCatalog : IDisposable
{
    private readonly CrimsonQuestGaugeInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeQuestGaugeInfoCatalog(CrimsonQuestGaugeInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeQuestGaugeInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.QuestGaugeInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_questgaugeinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonQuestGaugeInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.QuestGaugeInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_questgaugeinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeQuestGaugeInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint gaugeKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.QuestGaugeInfoLookupStringKey(_handle, gaugeKey, buf, bufLen, out required),
                $"crimson_questgaugeinfo_lookup_string_key({gaugeKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonQuestGaugeInfoHandle : SafeHandle
{
    public CrimsonQuestGaugeInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonQuestGaugeInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonQuestGaugeInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.QuestGaugeInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SkillInfo  (two-file load — pabgh + pabgb — and no PALOC chain)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>SkillKey (u32)</c> → internal name. Loaded from the pair
/// <c>skill.pabgh</c> (index) + <c>skill.pabgb</c> (body). Skills don't
/// resolve through PALOC in the current model, so the internal name is
/// the user-facing label.
/// </summary>
public sealed class NativeSkillInfoCatalog : IDisposable
{
    private readonly CrimsonSkillInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeSkillInfoCatalog(CrimsonSkillInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeSkillInfoCatalog LoadFromBytes(ReadOnlySpan<byte> pabgh, ReadOnlySpan<byte> pabgb)
    {
        unsafe
        {
            fixed (byte* ph = pabgh)
            fixed (byte* pb = pabgb)
            {
                var rc = NativeMethods.SkillInfoLoadFromBytes(
                    ph, (nuint)pabgh.Length, pb, (nuint)pabgb.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_skillinfo_load_from_bytes(pabgh={pabgh.Length},pabgb={pabgb.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonSkillInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.SkillInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_skillinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeSkillInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint skillKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.SkillInfoLookupStringKey(_handle, skillKey, buf, bufLen, out required),
                $"crimson_skillinfo_lookup_string_key({skillKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonSkillInfoHandle : SafeHandle
{
    public CrimsonSkillInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonSkillInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonSkillInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.SkillInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  GimmickInfo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>GimmickInfoKey (u32)</c> → internal name or PALOC-localized title
/// via the hash hop at <c>lo32 = 0x200</c>. Coexists with the legacy
/// PALOC-byte-0x00 path on the editor side — the editor consults this
/// bridge first and falls back to 0x00 when the bridge doesn't cover
/// the value (e.g. <c>LevelGimmickSceneObjectInfoKey</c> values that
/// don't appear in <c>gimmickinfo.pabgb</c> but do appear at PALOC 0x00).
/// </summary>
public sealed class NativeGimmickInfoCatalog : IDisposable
{
    /// <summary>PALOC <c>lo32</c> for the localized gimmick title.</summary>
    public const uint TitleLo32 = 0x200;

    private readonly CrimsonGimmickInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGimmickInfoCatalog(CrimsonGimmickInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGimmickInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.GimmickInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_gimmickinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGimmickInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GimmickInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_gimmickinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGimmickInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint gimmickKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GimmickInfoLookupStringKey(_handle, gimmickKey, buf, bufLen, out required),
                $"crimson_gimmickinfo_lookup_string_key({gimmickKey})");
        }
    }

    public string? LookupDisplayName(uint gimmickKey, NativePalocCatalog paloc, uint lo32 = TitleLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GimmickInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, gimmickKey, lo32, buf, bufLen, out required),
                $"crimson_gimmickinfo_lookup_display_name({gimmickKey}, 0x{lo32:X})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGimmickInfoHandle : SafeHandle
{
    public CrimsonGimmickInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGimmickInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGimmickInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GimmickInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SubLevelInfo  (Pattern A only — no PALOC chain exposed)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>SubLevelKey (u32)</c> → internal name. Sub-level entries don't
/// have a localized title bridge yet (no <c>lookup_display_name</c>
/// exported); the internal name is the user-facing label.
/// </summary>
public sealed class NativeSubLevelInfoCatalog : IDisposable
{
    private readonly CrimsonSubLevelInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeSubLevelInfoCatalog(CrimsonSubLevelInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeSubLevelInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.SubLevelInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_sublevelinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonSubLevelInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.SubLevelInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_sublevelinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeSubLevelInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint subLevelKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.SubLevelInfoLookupStringKey(_handle, subLevelKey, buf, bufLen, out required),
                $"crimson_sublevelinfo_lookup_string_key({subLevelKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonSubLevelInfoHandle : SafeHandle
{
    public CrimsonSubLevelInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonSubLevelInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonSubLevelInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.SubLevelInfoFree(handle);
        return true;
    }
}
