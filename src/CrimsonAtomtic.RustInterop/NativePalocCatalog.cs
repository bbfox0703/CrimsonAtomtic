using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// <see cref="IPalocCatalog"/> backed by the crimson-rs C ABI
/// (<c>vendor/crimson-rs</c>, <c>--features c_abi</c>). Uses
/// <see cref="LibraryImportAttribute"/> source generators so the
/// marshalling is AOT-safe (no runtime reflection).
/// </summary>
/// <remarks>
/// Mirrors the shape of <see cref="NativeSaveLoader"/>: a SafeHandle
/// wraps the native pointer so the AddRef/Release pattern protects
/// in-flight FFI calls from a concurrent Dispose, and analyzer rules
/// stay happy.
/// </remarks>
public sealed class NativePalocCatalog : IPalocCatalog
{
    private readonly CrimsonPalocHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativePalocCatalog(CrimsonPalocHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    /// <summary>Load a PALOC table from a pre-extracted file on disk.</summary>
    /// <remarks>
    /// The raw <c>gamedata/*.paloc</c> files in a Steam install are
    /// still PAZ-wrapped (encrypted + compressed) and will fail to
    /// parse here. Use <see cref="LoadFromBytes"/> after running them
    /// through <see cref="IPazExtractor"/>.
    /// </remarks>
    public static NativePalocCatalog LoadFromFile(string palocPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(palocPath);
        var rc = NativeMethods.PalocLoadFromFile(palocPath, out var raw);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_paloc_load_from_file({palocPath}) failed: {NativeSaveLoader_ErrorName(rc)}");
        }
        var handle = CrimsonPalocHandle.FromOwnedPointer(raw);
        return Build(handle);
    }

    /// <summary>Load a PALOC table from already-extracted bytes (preferred).</summary>
    public static NativePalocCatalog LoadFromBytes(ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var rc = NativeMethods.PalocLoadFromBytes(p, (nuint)bytes.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_paloc_load_from_bytes(bytes={bytes.Length}) failed: {NativeSaveLoader_ErrorName(rc)}");
                }
                var handle = CrimsonPalocHandle.FromOwnedPointer(raw);
                return Build(handle);
            }
        }
    }

    private static NativePalocCatalog Build(CrimsonPalocHandle handle)
    {
        var rc = NativeMethods.PalocEntryCount(handle, out var count);
        if (rc != NativeMethods.OK)
        {
            handle.Dispose();
            throw new CrimsonSaveException(rc,
                $"crimson_paloc_entry_count failed: {NativeSaveLoader_ErrorName(rc)}");
        }
        return new NativePalocCatalog(handle, (int)count);
    }

    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entryCount;
        }
    }

    public string? Lookup(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            return null;
        }

        // Two-call pattern. Keys are ASCII / short UTF-8; values can run
        // into a few hundred bytes for verbose descriptions but most are
        // under 64 bytes. Always pool — the size we need depends on the
        // value, so we can't usefully stack-alloc speculatively.
        var keyBytes = Encoding.UTF8.GetBytes(key);
        unsafe
        {
            nuint required = 0;
            int rc;
            fixed (byte* k = keyBytes)
            {
                rc = NativeMethods.PalocLookup(_handle, k, (nuint)keyBytes.Length,
                    null, 0, out required);
            }
            if (rc == NativeMethods.NOT_FOUND)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paloc_lookup({key}) size query failed: {NativeSaveLoader_ErrorName(rc)}");
            }
            if (required <= 1)
            {
                // Empty value: required == 1 covers just the trailing NUL.
                // Still need the second call to satisfy the C ABI contract,
                // but the result is the empty string.
                Span<byte> stack = stackalloc byte[1];
                fixed (byte* k = keyBytes)
                fixed (byte* b = stack)
                {
                    rc = NativeMethods.PalocLookup(_handle, k, (nuint)keyBytes.Length,
                        b, (nuint)stack.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_paloc_lookup({key}) empty-value fill failed: {NativeSaveLoader_ErrorName(rc)}");
                }
                return string.Empty;
            }

            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* k = keyBytes)
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.PalocLookup(_handle, k, (nuint)keyBytes.Length,
                        b, (nuint)rented.Length, out _);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_paloc_lookup({key}) fill failed: {NativeSaveLoader_ErrorName(rc)}");
                }
                // Exclude the trailing NUL.
                return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public (string Key, string Value)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        unsafe
        {
            nuint keyReq = 0;
            nuint valReq = 0;
            int rc = NativeMethods.PalocGetEntry(_handle, (uint)index,
                null, 0, out keyReq, null, 0, out valReq);
            if (rc == NativeMethods.OUT_OF_RANGE)
            {
                return null;
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paloc_get_entry({index}) size query failed: {NativeSaveLoader_ErrorName(rc)}");
            }
            // Both keyReq / valReq are >=1 because of the trailing NUL.
            var keyBuf = ArrayPool<byte>.Shared.Rent((int)keyReq);
            var valBuf = ArrayPool<byte>.Shared.Rent((int)valReq);
            try
            {
                fixed (byte* kp = keyBuf)
                fixed (byte* vp = valBuf)
                {
                    rc = NativeMethods.PalocGetEntry(_handle, (uint)index,
                        kp, (nuint)keyBuf.Length, out keyReq,
                        vp, (nuint)valBuf.Length, out valReq);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_paloc_get_entry({index}) fill failed: {NativeSaveLoader_ErrorName(rc)}");
                }
                var key = Encoding.UTF8.GetString(keyBuf, 0, (int)keyReq - 1);
                var val = Encoding.UTF8.GetString(valBuf, 0, (int)valReq - 1);
                return (key, val);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(keyBuf);
                ArrayPool<byte>.Shared.Return(valBuf);
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

    /// <summary>Re-exposes <see cref="NativeSaveLoader"/>'s private error-name table.</summary>
    private static string NativeSaveLoader_ErrorName(int code) => code switch
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
/// SafeHandle wrapper around the raw <c>CrimsonPalocHandle*</c> from the
/// Rust ABI. Releases via <c>crimson_paloc_free</c>.
/// </summary>
internal sealed class CrimsonPalocHandle : SafeHandle
{
    // Parameterless ctor at the type's visibility (CA1419) — the
    // marshaller never constructs one of these (LoadFromFile/Bytes
    // returns an IntPtr we wrap explicitly), but the analyzer asks
    // for it.
    public CrimsonPalocHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public static CrimsonPalocHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonPalocHandle();
        h.SetHandle(ptr);
        return h;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.PalocFree(handle);
        return true;
    }
}
