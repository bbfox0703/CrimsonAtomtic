using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// <see cref="IStringInfoCatalog"/> backed by the crimson-rs C ABI
/// (<c>vendor/crimson-rs</c>, <c>--features c_abi</c>). Mirrors the
/// <see cref="NativeItemInfoCatalog"/> shape: a SafeHandle wraps the
/// native pointer; <see cref="LoadFromBytes"/> is the preferred
/// entry point because the caller pulls bytes through
/// <see cref="IPazExtractor"/> first.
/// </summary>
public sealed class NativeStringInfoCatalog : IStringInfoCatalog
{
    private readonly CrimsonStringInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeStringInfoCatalog(CrimsonStringInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    /// <summary>Load stringinfo bytes from a pre-extracted file on disk.</summary>
    /// <remarks>
    /// The raw <c>stringinfo.pabgb</c> in a Steam install lives at
    /// <c>0008/0.paz</c> under
    /// <c>gamedata/binary__/client/bin/stringinfo.pabgb</c> and is
    /// PAZ-wrapped; use <see cref="LoadFromBytes"/> after extracting.
    /// </remarks>
    public static NativeStringInfoCatalog LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var rc = NativeMethods.StringInfoLoadFromFile(path, out var raw);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_string_info_load_from_file({path}) failed: {ErrorName(rc)}");
        }
        return Build(CrimsonStringInfoHandle.FromOwnedPointer(raw));
    }

    /// <summary>Load stringinfo from already-extracted bytes (preferred).</summary>
    public static NativeStringInfoCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.StringInfoLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_string_info_load_from_bytes(bytes={bytes.Length}) failed: {ErrorName(rc)}");
                }
                return Build(CrimsonStringInfoHandle.FromOwnedPointer(raw));
            }
        }
    }

    private static NativeStringInfoCatalog Build(CrimsonStringInfoHandle handle)
    {
        var rc = NativeMethods.StringInfoEntryCount(handle, out var count);
        if (rc != NativeMethods.OK)
        {
            handle.Dispose();
            throw new CrimsonSaveException(rc,
                $"crimson_string_info_entry_count failed: {ErrorName(rc)}");
        }
        return new NativeStringInfoCatalog(handle, (int)count);
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entryCount;
        }
    }

    public string? LookupByHash(uint hash)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            nuint required = 0;
            var rc = NativeMethods.StringInfoLookupByHash(_handle, hash,
                null, 0, out required);
            if (rc == NativeMethods.NOT_FOUND)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_string_info_lookup_by_hash(0x{hash:X8}) size query failed: {ErrorName(rc)}");
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
                    rc = NativeMethods.StringInfoLookupByHash(_handle, hash,
                        b, (nuint)rented.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_string_info_lookup_by_hash(0x{hash:X8}) fill failed: {ErrorName(rc)}");
                }
                return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public (uint Hash, string Value)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        unsafe
        {
            uint outHash = 0;
            nuint required = 0;
            var rc = NativeMethods.StringInfoGetEntry(_handle, (uint)index,
                out outHash, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_string_info_get_entry({index}) size query failed: {ErrorName(rc)}");
            }
            if (required <= 1)
            {
                return (outHash, string.Empty);
            }
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.StringInfoGetEntry(_handle, (uint)index,
                        out outHash, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_string_info_get_entry({index}) fill failed: {ErrorName(rc)}");
                }
                return (outHash, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
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
/// SafeHandle wrapper for the native <c>CrimsonStringInfoHandle*</c>.
/// Releases via <c>crimson_string_info_free</c>.
/// </summary>
internal sealed class CrimsonStringInfoHandle : SafeHandle
{
    // CA1419: parameterless ctor matching the type's visibility.
    public CrimsonStringInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public static CrimsonStringInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonStringInfoHandle();
        h.SetHandle(ptr);
        return h;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.StringInfoFree(handle);
        return true;
    }
}
