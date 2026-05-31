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

    /// <summary>
    /// Two-call enumerate over the loaded knowledge entries by
    /// insertion index. Returns <c>null</c> when <paramref name="index"/>
    /// is past the catalog's end. Used by the Abyss-Gate bulk-unlock
    /// flow to scan all knowledge keys for the abyss / hyperspace
    /// name prefixes.
    /// </summary>
    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.KnowledgeInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_knowledgeinfo_get_entry({index}) size query failed: {NameBuffer.ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outKey, string.Empty);
            }
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.KnowledgeInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_knowledgeinfo_get_entry({index}) fill failed: {NameBuffer.ErrorName(rc)}");
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
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
//  StoreInfo  (two-file load — pabgb + pabgh — and no PALOC chain)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>StoreKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"Store_Her_General"</c>, <c>"Store_BlackMarket"</c>). 292
/// rows in 1.07. Name-only — no <c>LookupDisplayName</c> entry point,
/// stores aren't (yet) on a PALOC chain. The internal template name is
/// what the editor surfaces; future work could probe PALOC for
/// localized store titles.
/// </summary>
public sealed class NativeStoreInfoCatalog : IDisposable
{
    private readonly CrimsonStoreInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeStoreInfoCatalog(CrimsonStoreInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeStoreInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.StoreInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_store_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonStoreInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.StoreInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_store_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeStoreInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint storeKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.StoreInfoLookupStringKey(_handle, storeKey, buf, bufLen, out required),
                $"crimson_store_info_lookup_string_key({storeKey})");
        }
    }

    /// <summary>
    /// Two-call enumeration by insertion index. Returns <c>null</c> when
    /// <paramref name="index"/> is past the catalog's end. Drives the
    /// Vendor Buyback dialog's distinct-store dropdown.
    /// </summary>
    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.StoreInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_store_info_get_entry({index}) size query failed: " +
                    $"{NameBuffer.ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outKey, string.Empty);
            }
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.StoreInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_store_info_get_entry({index}) fill failed: " +
                        $"{NameBuffer.ErrorName(rc)}");
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
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

internal sealed class CrimsonStoreInfoHandle : SafeHandle
{
    public CrimsonStoreInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonStoreInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonStoreInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.StoreInfoFree(handle);
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

    /// <summary>
    /// Two-call enumerate over the loaded gimmick entries by
    /// insertion index. Returns <c>null</c> when <paramref name="index"/>
    /// is past the catalog's end. Used by the Abyss-Gate per-gate
    /// dialog to build the allowlist of abyss / hyperspace
    /// <c>_gimmickInfoKey</c> values.
    /// </summary>
    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.GimmickInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_gimmickinfo_get_entry({index}) size query failed: {NameBuffer.ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outKey, string.Empty);
            }
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.GimmickInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_gimmickinfo_get_entry({index}) fill failed: {NameBuffer.ErrorName(rc)}");
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
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
//  CharacterInfo  (cat-byte lo24 strip, PALOC chain at lo32 = 0x30, no hash hop)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>CharacterKey (u32)</c> → internal name or PALOC-localized display
/// via the dedicated <c>characterinfo.pabgb</c> bridge. The lookup
/// strips a "cat byte" (hi-byte, <c>0x02..=0xFE</c> region / variant /
/// faction marker) before consulting the catalog — so any
/// <c>_characterKey</c> with that high byte set (FieldNPC spawn rows
/// in particular) resolves through this bridge but would have missed
/// the legacy generic PALOC byte-0x30 path that uses the raw u32.
///
/// <para>
/// Resolution preference at the editor's <c>ResolveByFieldTypeName</c>
/// level: <c>LookupDisplayName</c> at <see cref="DisplayNameLo32"/>
/// against the loaded PALOC → internal-name fallback via
/// <c>LookupStringKey</c>. Upstream measured 22% PALOC display + ~100%
/// internal-name coverage on the editor's 221-key sample save.
/// </para>
/// </summary>
public sealed class NativeCharacterInfoCatalog : IDisposable
{
    /// <summary>
    /// Default PALOC <c>lo32</c> for character display names. The vast
    /// majority of named characters resolve at this namespace. Pearl
    /// Abyss does ship a handful of characters with display entries at
    /// other <c>lo32</c> values; surfacing them is part of the
    /// "broader CharacterKey PALOC namespaces" follow-on documented in
    /// <c>docs/status.md</c>.
    /// </summary>
    public const uint DisplayNameLo32 = 0x30;

    private readonly CrimsonCharacterInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeCharacterInfoCatalog(CrimsonCharacterInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeCharacterInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.CharacterInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_characterinfo_load_from_bytes(len={bytes.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonCharacterInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.CharacterInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_characterinfo_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeCharacterInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint characterKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.CharacterInfoLookupStringKey(_handle, characterKey, buf, bufLen, out required),
                $"crimson_characterinfo_lookup_string_key({characterKey})");
        }
    }

    public string? LookupDisplayName(uint characterKey, NativePalocCatalog paloc, uint lo32 = DisplayNameLo32)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.CharacterInfoLookupDisplayName(
                        _handle, paloc.NativeHandle, characterKey, lo32, buf, bufLen, out required),
                $"crimson_characterinfo_lookup_display_name({characterKey}, 0x{lo32:X})");
        }
    }

    /// <summary>
    /// Two-call enumerate over the loaded characterinfo entries by
    /// insertion index. Returns <c>null</c> when <paramref name="index"/>
    /// is past the catalog's end. The key is the lo24 row key (no
    /// cat-byte); save-side <c>_characterKey</c> values still need the
    /// strip before matching against these.
    /// </summary>
    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.CharacterInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_characterinfo_get_entry({index}) size query failed: {NameBuffer.ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outKey, string.Empty);
            }
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.CharacterInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_characterinfo_get_entry({index}) fill failed: {NameBuffer.ErrorName(rc)}");
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Resolve <paramref name="characterKey"/> to its best-scoring NPC
    /// portrait DDS path against <paramref name="portraitListBuffer"/>
    /// (the raw NUL-separated buffer from
    /// <see cref="IPazExtractor.ListNpcPortraits"/>). Returns
    /// <c>null</c> when the character has no resolvable name at either
    /// the display-name or internal-name surface, or when no portrait
    /// scored above zero.
    /// </summary>
    /// <param name="characterKey">Save-side _characterKey (cat-byte tolerated).</param>
    /// <param name="paloc">Loaded PALOC for the display-name primary signal.</param>
    /// <param name="portraitListBuffer">
    /// NUL-separated UTF-8 portrait paths emitted by
    /// <c>crimson_paz_list_npc_portraits</c>. Empty span = no portraits
    /// to match against; the call short-circuits to <c>null</c>.
    /// </param>
    /// <returns>
    /// Tuple of the winning portrait's <c>&lt;dir&gt;/&lt;filename&gt;</c>
    /// path plus a confidence score (0 = no match, ~30 = noise floor,
    /// ~100 = exact normalised match). Callers apply their own
    /// threshold.
    /// </returns>
    public (string Path, int Score)? ResolvePortrait(
        uint characterKey,
        NativePalocCatalog paloc,
        ReadOnlySpan<byte> portraitListBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paloc);
        if (portraitListBuffer.IsEmpty)
        {
            return null;
        }
        // Two-call buffer pattern inlined (vs. the NameBuffer.ReadString
        // helper used by other lookups) because C# can't capture a
        // `fixed` local in a lambda — we need the portrait-list pointer
        // pinned across both calls and that has to live in straight-
        // line code.
        unsafe
        {
            fixed (byte* listPtr = portraitListBuffer)
            {
                var listLen = (nuint)portraitListBuffer.Length;
                nuint required = 0;
                int score = 0;
                int rc = NativeMethods.CharacterInfoResolvePortrait(
                    _handle, paloc.NativeHandle, characterKey,
                    listPtr, listLen,
                    null, 0, out required,
                    out score);
                if (rc == NativeMethods.NOT_FOUND)
                {
                    return null;
                }
                if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_characterinfo_resolve_portrait({characterKey}) " +
                        $"size query failed: {NameBuffer.ErrorName(rc)}");
                }
                if (required == 0)
                {
                    return null;
                }
                Span<byte> outBuf = required <= 256
                    ? stackalloc byte[(int)required]
                    : new byte[required];
                fixed (byte* outPtr = outBuf)
                {
                    rc = NativeMethods.CharacterInfoResolvePortrait(
                        _handle, paloc.NativeHandle, characterKey,
                        listPtr, listLen,
                        outPtr, (nuint)outBuf.Length, out _,
                        out score);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_characterinfo_resolve_portrait({characterKey}) " +
                        $"fill failed: {NameBuffer.ErrorName(rc)}");
                }
                // Strip the trailing NUL the Rust side writes for C-string
                // compatibility — mirror NameBuffer.ReadString's behaviour.
                var len = outBuf.Length;
                while (len > 0 && outBuf[len - 1] == 0)
                {
                    len--;
                }
                var path = System.Text.Encoding.UTF8.GetString(outBuf[..len]);
                return (path, score);
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

internal sealed class CrimsonCharacterInfoHandle : SafeHandle
{
    public CrimsonCharacterInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonCharacterInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonCharacterInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.CharacterInfoFree(handle);
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

// ─────────────────────────────────────────────────────────────────────────────
//  13 name-only bridges generated by impl_name_only_bridge! on the Rust side
//
//  All share the StoreInfo shape: two-file load (.pabgb body + .pabgh index),
//  internal-name only (no PALOC chain), key widened to u32 by Rust.
//  GetEntry intentionally omitted — these resolve save-side keys to a label
//  for the resolved-name column; no enumerator UX consumes them today.
// ─────────────────────────────────────────────────────────────────────────────

// ── FactionNodeInfo ───────────────────────────────────────────────────────────

/// <summary>
/// <c>FactionNodeKey (u32)</c> → row internal name
/// (e.g. <c>"Node_Her_HernandCastle"</c>). 1,158 rows in 1.09. Name-only
/// (no PALOC display name — the place name a player sees comes through a
/// different gamedata path). Used to label faction-stronghold rows in the
/// Faction-node editor; the save stores this key in
/// <c>FactionNodeElementSaveData._ownerFactionKey</c> (TypeName
/// <c>FactionNodeKey</c>).
/// </summary>
public sealed class NativeFactionNodeInfoCatalog : IDisposable
{
    private readonly CrimsonFactionNodeInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeFactionNodeInfoCatalog(CrimsonFactionNodeInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeFactionNodeInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.FactionNodeInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_factionnode_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonFactionNodeInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.FactionNodeInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_factionnode_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeFactionNodeInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint factionNodeKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.FactionNodeInfoLookupStringKey(_handle, factionNodeKey, buf, bufLen, out required),
                $"crimson_factionnode_lookup_string_key({factionNodeKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonFactionNodeInfoHandle : SafeHandle
{
    public CrimsonFactionNodeInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonFactionNodeInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonFactionNodeInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.FactionNodeInfoFree(handle);
        return true;
    }
}

// ── HouseInfo ───────────────────────────────────────────────────────────────

/// <summary>
/// <c>HouseKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"DefaultHouse_Lv1"</c>). 4 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeHouseInfoCatalog : IDisposable
{
    private readonly CrimsonHouseInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeHouseInfoCatalog(CrimsonHouseInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeHouseInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.HouseInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_house_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonHouseInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.HouseInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_house_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeHouseInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint houseKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.HouseInfoLookupStringKey(_handle, houseKey, buf, bufLen, out required),
                $"crimson_house_info_lookup_string_key({houseKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonHouseInfoHandle : SafeHandle
{
    public CrimsonHouseInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonHouseInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonHouseInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.HouseInfoFree(handle);
        return true;
    }
}

// ── RoyalSupplyInfo ─────────────────────────────────────────────────────────

/// <summary>
/// <c>RoyalSupplyKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"RoyalSupply_Hernand"</c>). 4 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeRoyalSupplyInfoCatalog : IDisposable
{
    private readonly CrimsonRoyalSupplyInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeRoyalSupplyInfoCatalog(CrimsonRoyalSupplyInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeRoyalSupplyInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.RoyalSupplyInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_royal_supply_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonRoyalSupplyInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.RoyalSupplyInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_royal_supply_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeRoyalSupplyInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint royalSupplyKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.RoyalSupplyInfoLookupStringKey(_handle, royalSupplyKey, buf, bufLen, out required),
                $"crimson_royal_supply_info_lookup_string_key({royalSupplyKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonRoyalSupplyInfoHandle : SafeHandle
{
    public CrimsonRoyalSupplyInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonRoyalSupplyInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonRoyalSupplyInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.RoyalSupplyInfoFree(handle);
        return true;
    }
}

// ── CraftToolInfo ───────────────────────────────────────────────────────────

/// <summary>
/// <c>CraftToolKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"CraftTool_Enchant"</c>). 17 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeCraftToolInfoCatalog : IDisposable
{
    private readonly CrimsonCraftToolInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeCraftToolInfoCatalog(CrimsonCraftToolInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeCraftToolInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.CraftToolInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_craft_tool_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonCraftToolInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.CraftToolInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_craft_tool_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeCraftToolInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint craftToolKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.CraftToolInfoLookupStringKey(_handle, craftToolKey, buf, bufLen, out required),
                $"crimson_craft_tool_info_lookup_string_key({craftToolKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonCraftToolInfoHandle : SafeHandle
{
    public CrimsonCraftToolInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonCraftToolInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonCraftToolInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.CraftToolInfoFree(handle);
        return true;
    }
}

// ── CraftToolGroupInfo ──────────────────────────────────────────────────────

/// <summary>
/// <c>CraftToolGroupKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"CraftTool_Equip_Enchant"</c>). 10 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeCraftToolGroupInfoCatalog : IDisposable
{
    private readonly CrimsonCraftToolGroupInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeCraftToolGroupInfoCatalog(CrimsonCraftToolGroupInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeCraftToolGroupInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.CraftToolGroupInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_craft_tool_group_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonCraftToolGroupInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.CraftToolGroupInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_craft_tool_group_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeCraftToolGroupInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint craftToolGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.CraftToolGroupInfoLookupStringKey(_handle, craftToolGroupKey, buf, bufLen, out required),
                $"crimson_craft_tool_group_info_lookup_string_key({craftToolGroupKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonCraftToolGroupInfoHandle : SafeHandle
{
    public CrimsonCraftToolGroupInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonCraftToolGroupInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonCraftToolGroupInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.CraftToolGroupInfoFree(handle);
        return true;
    }
}

// ── TriggerRegionInfo ───────────────────────────────────────────────────────

/// <summary>
/// <c>TriggerRegionKey (u32)</c> → row internal name
/// (e.g. <c>"Swamp"</c>, <c>"IceTerrain"</c>). 12 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeTriggerRegionInfoCatalog : IDisposable
{
    private readonly CrimsonTriggerRegionInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeTriggerRegionInfoCatalog(CrimsonTriggerRegionInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeTriggerRegionInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.TriggerRegionInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_trigger_region_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonTriggerRegionInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.TriggerRegionInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_trigger_region_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeTriggerRegionInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint triggerRegionKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.TriggerRegionInfoLookupStringKey(_handle, triggerRegionKey, buf, bufLen, out required),
                $"crimson_trigger_region_info_lookup_string_key({triggerRegionKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonTriggerRegionInfoHandle : SafeHandle
{
    public CrimsonTriggerRegionInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonTriggerRegionInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonTriggerRegionInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.TriggerRegionInfoFree(handle);
        return true;
    }
}

// ── GamePlayVariableInfo ────────────────────────────────────────────────────

/// <summary>
/// <c>GamePlayVariableKey (u32)</c> → row internal name
/// (e.g. <c>"CD_Live"</c>, <c>"BaseCamp_Ranch_Lv1"</c>). 47 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeGamePlayVariableInfoCatalog : IDisposable
{
    private readonly CrimsonGamePlayVariableInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGamePlayVariableInfoCatalog(CrimsonGamePlayVariableInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGamePlayVariableInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.GamePlayVariableInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_gameplay_variable_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGamePlayVariableInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GamePlayVariableInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_gameplay_variable_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGamePlayVariableInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint gamePlayVariableKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GamePlayVariableInfoLookupStringKey(_handle, gamePlayVariableKey, buf, bufLen, out required),
                $"crimson_gameplay_variable_info_lookup_string_key({gamePlayVariableKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGamePlayVariableInfoHandle : SafeHandle
{
    public CrimsonGamePlayVariableInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGamePlayVariableInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGamePlayVariableInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GamePlayVariableInfoFree(handle);
        return true;
    }
}

// ── GlobalGameEventInfo ─────────────────────────────────────────────────────

/// <summary>
/// <c>GlobalGameEventInfoKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"Drought_Varnian"</c>). 103 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeGlobalGameEventInfoCatalog : IDisposable
{
    private readonly CrimsonGlobalGameEventInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGlobalGameEventInfoCatalog(CrimsonGlobalGameEventInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGlobalGameEventInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.GlobalGameEventInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_global_game_event_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGlobalGameEventInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GlobalGameEventInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_global_game_event_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGlobalGameEventInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint globalGameEventInfoKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GlobalGameEventInfoLookupStringKey(_handle, globalGameEventInfoKey, buf, bufLen, out required),
                $"crimson_global_game_event_info_lookup_string_key({globalGameEventInfoKey})");
        }
    }

    /// <summary>
    /// The <c>GlobalGameEventGroupKey</c> for this event (resolvable via
    /// <see cref="NativeGlobalGameEventGroupInfoCatalog.LookupStringKey"/>).
    /// Returns <see langword="null"/> when the event key isn't in the
    /// table. Universal across all 103 rows in 1.07/1.08 — every event
    /// belongs to exactly one of the 7 group keys.
    /// </summary>
    public uint? LookupGroupKey(uint globalGameEventInfoKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.GlobalGameEventInfoLookupGroupKey(
            _handle, globalGameEventInfoKey, out var groupKey);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_global_game_event_info_lookup_group_key({globalGameEventInfoKey}) " +
                $"failed: {NameBuffer.ErrorName(rc)}");
        }
        return groupKey;
    }

    /// <summary>
    /// The 64-bit PALOC key (<c>hi32 = event_key, lo32 = namespace</c>)
    /// for this event's localized display name. Returns <see langword="null"/>
    /// when the event key isn't in the table; returns <c>0</c> when the
    /// row exists but lacks an embedded <c>PalocStringRef</c> (the
    /// <c>RoyalSupply</c> + <c>FactionBlockEvent_*</c> groups — ~24 of
    /// 103 rows in 1.07/1.08). Callers should treat <c>0</c> as "no
    /// localized name" and fall back to
    /// <see cref="LookupStringKey(uint)"/>.
    /// </summary>
    public ulong? LookupPalocKey(uint globalGameEventInfoKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.GlobalGameEventInfoLookupPalocKey(
            _handle, globalGameEventInfoKey, out var palocKey);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_global_game_event_info_lookup_paloc_key({globalGameEventInfoKey}) " +
                $"failed: {NameBuffer.ErrorName(rc)}");
        }
        return palocKey;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGlobalGameEventInfoHandle : SafeHandle
{
    public CrimsonGlobalGameEventInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGlobalGameEventInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGlobalGameEventInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GlobalGameEventInfoFree(handle);
        return true;
    }
}

// ── GlobalGameEventGroupInfo ────────────────────────────────────────────────

/// <summary>
/// <c>GlobalGameEventGroupKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"WeatherEventGroup"</c>). 7 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeGlobalGameEventGroupInfoCatalog : IDisposable
{
    private readonly CrimsonGlobalGameEventGroupInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGlobalGameEventGroupInfoCatalog(CrimsonGlobalGameEventGroupInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGlobalGameEventGroupInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.GlobalGameEventGroupInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_global_game_event_group_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGlobalGameEventGroupInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GlobalGameEventGroupInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_global_game_event_group_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGlobalGameEventGroupInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint globalGameEventGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GlobalGameEventGroupInfoLookupStringKey(_handle, globalGameEventGroupKey, buf, bufLen, out required),
                $"crimson_global_game_event_group_info_lookup_string_key({globalGameEventGroupKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGlobalGameEventGroupInfoHandle : SafeHandle
{
    public CrimsonGlobalGameEventGroupInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGlobalGameEventGroupInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGlobalGameEventGroupInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GlobalGameEventGroupInfoFree(handle);
        return true;
    }
}

// ── GameAdviceInfo ──────────────────────────────────────────────────────────

/// <summary>
/// <c>GameAdviceInfoKey (u32)</c> → row internal name
/// (e.g. <c>"Advice_Control_Move"</c>). 461 rows in 1.07. Name-only;
/// PALOC chain deferred.
/// </summary>
public sealed class NativeGameAdviceInfoCatalog : IDisposable
{
    private readonly CrimsonGameAdviceInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGameAdviceInfoCatalog(CrimsonGameAdviceInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGameAdviceInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.GameAdviceInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_game_advice_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGameAdviceInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GameAdviceInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_game_advice_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGameAdviceInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint gameAdviceInfoKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GameAdviceInfoLookupStringKey(_handle, gameAdviceInfoKey, buf, bufLen, out required),
                $"crimson_game_advice_info_lookup_string_key({gameAdviceInfoKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGameAdviceInfoHandle : SafeHandle
{
    public CrimsonGameAdviceInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGameAdviceInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGameAdviceInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GameAdviceInfoFree(handle);
        return true;
    }
}

// ── GameAdviceGroupInfo ─────────────────────────────────────────────────────

/// <summary>
/// <c>GameAdviceGroupKey (u32)</c> → row internal name
/// (e.g. <c>"GameAdviceGroup_ControlBasics"</c>). 8 rows in 1.07. Name-only.
/// </summary>
public sealed class NativeGameAdviceGroupInfoCatalog : IDisposable
{
    private readonly CrimsonGameAdviceGroupInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeGameAdviceGroupInfoCatalog(CrimsonGameAdviceGroupInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeGameAdviceGroupInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.GameAdviceGroupInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_game_advice_group_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonGameAdviceGroupInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.GameAdviceGroupInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_game_advice_group_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeGameAdviceGroupInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint gameAdviceGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.GameAdviceGroupInfoLookupStringKey(_handle, gameAdviceGroupKey, buf, bufLen, out required),
                $"crimson_game_advice_group_info_lookup_string_key({gameAdviceGroupKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonGameAdviceGroupInfoHandle : SafeHandle
{
    public CrimsonGameAdviceGroupInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonGameAdviceGroupInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonGameAdviceGroupInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.GameAdviceGroupInfoFree(handle);
        return true;
    }
}

// ── ReserveSlotInfo ─────────────────────────────────────────────────────────

/// <summary>
/// <c>ReserveSlotKey (u32)</c> → row internal name
/// (e.g. <c>"ArrowItem"</c>, <c>"BulletItem"</c>, <c>"BombItem"</c>). 27 rows in 1.07.
/// Name-only; PALOC chain deferred.
/// </summary>
public sealed class NativeReserveSlotInfoCatalog : IDisposable
{
    private readonly CrimsonReserveSlotInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeReserveSlotInfoCatalog(CrimsonReserveSlotInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeReserveSlotInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.ReserveSlotInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_reserve_slot_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonReserveSlotInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.ReserveSlotInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_reserve_slot_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeReserveSlotInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint reserveSlotKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.ReserveSlotInfoLookupStringKey(_handle, reserveSlotKey, buf, bufLen, out required),
                $"crimson_reserve_slot_info_lookup_string_key({reserveSlotKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonReserveSlotInfoHandle : SafeHandle
{
    public CrimsonReserveSlotInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonReserveSlotInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonReserveSlotInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.ReserveSlotInfoFree(handle);
        return true;
    }
}

// ── RegionInfo ──────────────────────────────────────────────────────────────

/// <summary>
/// <c>RegionKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"Region_Pywel"</c>, <c>"Region_Kweiden"</c>). 1,004 rows in 1.07.
/// Name-only.
/// </summary>
public sealed class NativeRegionInfoCatalog : IDisposable
{
    private readonly CrimsonRegionInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeRegionInfoCatalog(CrimsonRegionInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeRegionInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.RegionInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_region_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonRegionInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.RegionInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_region_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeRegionInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint regionKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.RegionInfoLookupStringKey(_handle, regionKey, buf, bufLen, out required),
                $"crimson_region_info_lookup_string_key({regionKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonRegionInfoHandle : SafeHandle
{
    public CrimsonRegionInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonRegionInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonRegionInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.RegionInfoFree(handle);
        return true;
    }
}

// ── ItemGroupInfo ───────────────────────────────────────────────────────────

/// <summary>
/// <c>ItemGroupKey (u16-widened-u32)</c> → row internal name
/// (e.g. <c>"ItemGroup_Category_Equipment"</c>). 1,500 rows in 1.07.
/// Name-only.
/// </summary>
public sealed class NativeItemGroupInfoCatalog : IDisposable
{
    private readonly CrimsonItemGroupInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeItemGroupInfoCatalog(CrimsonItemGroupInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeItemGroupInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.ItemGroupInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_item_group_info_load_from_bytes(pabgb={pabgb.Length},pabgh={pabgh.Length}) " +
                        $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonItemGroupInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.ItemGroupInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_item_group_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeItemGroupInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupStringKey(uint itemGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.ItemGroupInfoLookupStringKey(_handle, itemGroupKey, buf, bufLen, out required),
                $"crimson_item_group_info_lookup_string_key({itemGroupKey})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonItemGroupInfoHandle : SafeHandle
{
    public CrimsonItemGroupInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonItemGroupInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonItemGroupInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.ItemGroupInfoFree(handle);
        return true;
    }
}
