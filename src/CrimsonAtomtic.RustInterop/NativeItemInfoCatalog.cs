using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// <see cref="IItemInfoCatalog"/> backed by the crimson-rs C ABI
/// (<c>vendor/crimson-rs</c>, <c>--features c_abi</c>). Mirrors the
/// <see cref="NativePalocCatalog"/> shape: a SafeHandle wraps the
/// native pointer; <see cref="LoadFromBytes"/> is the preferred
/// entry point because the caller pulls bytes through
/// <see cref="IPazExtractor"/> first.
/// </summary>
public sealed class NativeItemInfoCatalog : IItemInfoCatalog
{
    private readonly CrimsonItemInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeItemInfoCatalog(CrimsonItemInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    /// <summary>Load iteminfo bytes from a pre-extracted file on disk.</summary>
    /// <remarks>
    /// The raw <c>iteminfo.pabgb</c> in a Steam install lives at
    /// <c>0008/0.paz</c> under
    /// <c>gamedata/binary__/client/bin/iteminfo.pabgb</c> and is
    /// PAZ-wrapped; use <see cref="LoadFromBytes"/> after extracting.
    /// </remarks>
    public static NativeItemInfoCatalog LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var rc = NativeMethods.ItemInfoLoadFromFile(path, out var raw);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_iteminfo_load_from_file({path}) failed: {ErrorName(rc)}");
        }
        return Build(CrimsonItemInfoHandle.FromOwnedPointer(raw));
    }

    /// <summary>Load iteminfo from already-extracted bytes (preferred).</summary>
    public static NativeItemInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.ItemInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_iteminfo_load_from_bytes(bytes={bytes.Length}) failed: {ErrorName(rc)}");
                }
                return Build(CrimsonItemInfoHandle.FromOwnedPointer(raw));
            }
        }
    }

    private static NativeItemInfoCatalog Build(CrimsonItemInfoHandle handle)
    {
        var rc = NativeMethods.ItemInfoEntryCount(handle, out var count);
        if (rc != NativeMethods.OK)
        {
            handle.Dispose();
            throw new CrimsonSaveException(rc,
                $"crimson_iteminfo_entry_count failed: {ErrorName(rc)}");
        }
        return new NativeItemInfoCatalog(handle, (int)count);
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entryCount;
        }
    }

    public string? LookupStringKey(uint itemKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            nuint required = 0;
            var rc = NativeMethods.ItemInfoLookupStringKey(_handle, itemKey,
                null, 0, out required);
            if (rc == NativeMethods.NOT_FOUND)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_iteminfo_lookup_string_key({itemKey}) size query failed: {ErrorName(rc)}");
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
                    rc = NativeMethods.ItemInfoLookupStringKey(_handle, itemKey,
                        b, (nuint)rented.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_iteminfo_lookup_string_key({itemKey}) fill failed: {ErrorName(rc)}");
                }
                return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public ulong? LookupMaxStackCount(uint itemKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.ItemInfoLookupMaxStack(_handle, itemKey, out var max);
        if (rc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_iteminfo_lookup_max_stack({itemKey}) failed: {ErrorName(rc)}");
        }
        return max;
    }

    public uint? LookupIconPathHash(uint itemKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.ItemInfoLookupIconPathHash(_handle, itemKey, out var hash);
        if (rc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_iteminfo_lookup_icon_path_hash({itemKey}) failed: {ErrorName(rc)}");
        }
        return hash;
    }

    public uint? LookupLookDetailMissionInfo(uint itemKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.ItemInfoLookupLookDetailMissionInfo(_handle, itemKey, out var mk);
        if (rc == NativeMethods.NOT_FOUND)
        {
            return null;
        }
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_iteminfo_lookup_look_detail_mission_info({itemKey}) failed: {ErrorName(rc)}");
        }
        return mk;
    }

    public (uint ItemKey, string StringKey)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.ItemInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_iteminfo_get_entry({index}) size query failed: {ErrorName(rc)}");
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
                    rc = NativeMethods.ItemInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_iteminfo_get_entry({index}) fill failed: {ErrorName(rc)}");
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
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _handle.Dispose();
    }

    private static string ErrorName(int code) => code switch
    {
        NativeMethods.OK                    => "OK",
        NativeMethods.NULL_ARG              => "NULL_ARG",
        NativeMethods.INVALID_PATH          => "INVALID_PATH",
        NativeMethods.IO                    => "IO",
        NativeMethods.BODY_PARSE            => "BODY_PARSE",
        NativeMethods.OUT_OF_RANGE          => "OUT_OF_RANGE",
        NativeMethods.BUFFER_TOO_SMALL      => "BUFFER_TOO_SMALL",
        NativeMethods.NOT_FOUND             => "NOT_FOUND",
        NativeMethods.PANIC                 => "PANIC",
        _                                   => $"UNKNOWN({code})",
    };
}

/// <summary>
/// SafeHandle wrapper for the native <c>CrimsonItemInfoHandle*</c>.
/// Releases via <c>crimson_iteminfo_free</c>.
/// </summary>
internal sealed class CrimsonItemInfoHandle : SafeHandle
{
    // CA1419: parameterless ctor matching the type's visibility.
    public CrimsonItemInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public static CrimsonItemInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonItemInfoHandle();
        h.SetHandle(ptr);
        return h;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.ItemInfoFree(handle);
        return true;
    }
}
