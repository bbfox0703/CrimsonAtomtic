using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// <see cref="ISaveLoader"/> backed by the crimson-rs C ABI
/// (vendor/crimson-rs, --features c_abi). Uses
/// <see cref="LibraryImportAttribute"/> source generators so the
/// marshalling is AOT-safe (no reflection at runtime).
/// </summary>
/// <remarks>
/// <para>
/// Keeps a live <see cref="CrimsonSaveHandle"/> cached after
/// <see cref="Load"/> so subsequent <see cref="LoadBlockDetails"/> calls
/// (one per block click in the UI) reuse it instead of re-parsing the
/// .save file. <see cref="Load"/> with a different path frees the old
/// handle automatically; <see cref="Dispose"/> tears the cache down on
/// app exit.
/// </para>
/// <para>
/// Cache reads / swaps are guarded by an internal lock for defensive
/// concurrency, even though Avalonia's VM dispatcher is single-threaded.
/// The SafeHandle's AddRef/Release semantics keep concurrent calls
/// during a Dispose race correct on top of the lock.
/// </para>
/// </remarks>
public sealed class NativeSaveLoader : ISaveLoader, IDisposable
{
    private readonly object _cacheLock = new();
    private string? _cachedPath;
    private CrimsonSaveHandle? _cachedHandle;

    public SaveSummary Load(string savePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        cancellationToken.ThrowIfCancellationRequested();

        var newHandle = OpenHandle(savePath);

        SaveSummary summary;
        try
        {
            summary = BuildSummary(newHandle, savePath, cancellationToken);
        }
        catch
        {
            newHandle.Dispose();
            throw;
        }

        // Swap into cache: free the old handle, take ownership of the new one.
        CrimsonSaveHandle? previous;
        lock (_cacheLock)
        {
            previous = _cachedHandle;
            _cachedHandle = newHandle;
            _cachedPath = savePath;
        }
        previous?.Dispose();

        return summary;
    }

    public BlockDetails LoadBlockDetails(string savePath, int blockIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        cancellationToken.ThrowIfCancellationRequested();

        // Fast path: same save still loaded — reuse the cached handle.
        // SafeHandle.AddRef'd implicitly when crossing the LibraryImport
        // boundary, so a Dispose race can't yank the native handle out
        // from under the FFI call.
        lock (_cacheLock)
        {
            if (PathsMatch(_cachedPath, savePath)
                && _cachedHandle is { IsInvalid: false } cached)
            {
                return ReadBlockDetails(cached, (uint)blockIndex);
            }
        }

        // Slow path: the cache hasn't been primed for this save (e.g.
        // LoadBlockDetails called before Load, or with a different
        // path). Open transiently and tear down at scope end.
        using var handle = OpenHandle(savePath);
        return ReadBlockDetails(handle, (uint)blockIndex);
    }

    public void SetScalarField(int blockIndex, int fieldIndex, ReadOnlySpan<byte> bytes) =>
        SetScalarField(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, bytes);

    public void SetScalarField(int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        CrimsonSaveHandle? cached;
        lock (_cacheLock)
        {
            cached = _cachedHandle;
        }
        if (cached is null || cached.IsInvalid)
        {
            throw new InvalidOperationException(
                "No save is currently loaded. Call Load(savePath) before SetScalarField.");
        }

        unsafe
        {
            fixed (byte* pBytes = bytes)
            fixed (PathStep* pPath = path)
            {
                var rc = NativeMethods.SetScalarFieldPath(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    pBytes,
                    (nuint)bytes.Length);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_set_scalar_field_path(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, bytes={bytes.Length}) failed: {ErrorName(rc)}");
                }
            }
        }
    }

    public void WriteToFile(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);

        CrimsonSaveHandle? cached;
        lock (_cacheLock)
        {
            cached = _cachedHandle;
        }
        if (cached is null || cached.IsInvalid)
        {
            throw new InvalidOperationException(
                "No save is currently loaded. Call Load(savePath) before WriteToFile.");
        }

        var rc = NativeMethods.WriteToFile(cached, destinationPath);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_write_to_file({destinationPath}) failed: {ErrorName(rc)}");
        }
    }

    public void Dispose()
    {
        CrimsonSaveHandle? toDispose;
        lock (_cacheLock)
        {
            toDispose = _cachedHandle;
            _cachedHandle = null;
            _cachedPath = null;
        }
        toDispose?.Dispose();
    }

    private static CrimsonSaveHandle OpenHandle(string savePath)
    {
        var rc = NativeMethods.LoadFromFile(savePath, out var rawHandle);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc, $"crimson_save_load_from_file failed: {ErrorName(rc)}");
        }
        return CrimsonSaveHandle.FromOwnedPointer(rawHandle);
    }

    private static bool PathsMatch(string? a, string b) =>
        // Windows: file system is case-insensitive. Linux/macOS file
        // systems are case-sensitive, but Crimson Desert ships Windows
        // only — match that platform's behavior.
        a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static SaveSummary BuildSummary(CrimsonSaveHandle handle, string savePath, CancellationToken cancellationToken)
    {
        var version          = ReadU16(handle, NativeMethods.GetVersion,         "version");
        var flags            = ReadU16(handle, NativeMethods.GetFlags,           "flags");
        var hmacOk           = ReadI32(handle, NativeMethods.GetHmacOk,          "hmac_ok") != 0;
        var payloadSize      = ReadU32(handle, NativeMethods.GetPayloadSize,     "payload_size");
        var uncompressedSize = ReadU32(handle, NativeMethods.GetUncompressedSize,"uncompressed_size");
        var schemaTypeCount  = ReadU32(handle, NativeMethods.GetSchemaTypeCount, "schema_type_count");
        var tocEntryCount    = ReadU32(handle, NativeMethods.GetTocEntryCount,   "toc_entry_count");
        var blockCount       = ReadU32(handle, NativeMethods.GetBlockCount,      "block_count");

        var blocks = new List<BlockSummary>((int)blockCount);
        long totalBlockBytes = 0;
        for (uint i = 0; i < blockCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var infoRc = NativeMethods.GetBlockInfo(handle, i, out var info);
            if (infoRc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(infoRc,
                    $"crimson_save_get_block_info[{i}] failed: {ErrorName(infoRc)}");
            }

            var className = ReadBlockClassName(handle, i);
            totalBlockBytes += info.DataSize;
            blocks.Add(new BlockSummary(
                Index:          (int)i,
                ClassIndex:     (int)info.ClassIndex,
                ClassName:      className,
                DataOffset:     info.DataOffset,
                DataSize:       info.DataSize,
                FieldsPresent:  (int)info.FieldsPresent,
                FieldsDecoded:  (int)info.FieldsDecoded));
        }

        return new SaveSummary(
            Source:           savePath,
            SlotName:         Path.GetFileName(Path.GetDirectoryName(savePath)) ?? string.Empty,
            Version:          version,
            Flags:            flags,
            PayloadSize:      payloadSize,
            UncompressedSize: uncompressedSize,
            HmacOk:           hmacOk,
            SchemaTypeCount:  (int)schemaTypeCount,
            TocEntryCount:    (int)tocEntryCount,
            TotalBlockBytes:  totalBlockBytes,
            Blocks:           blocks);
    }

    private static BlockDetails ReadBlockDetails(CrimsonSaveHandle handle, uint blockIndex)
    {
        var json = ReadBlockJson(handle, blockIndex);
        return JsonSerializer.Deserialize(json, SaveModelJsonContext.Default.BlockDetails)
            ?? throw new CrimsonSaveException(
                NativeMethods.PANIC,
                "crimson_save_get_block_json returned a JSON document that deserialized to null.");
    }

    private static unsafe string ReadBlockJson(CrimsonSaveHandle h, uint index)
    {
        // Two-call pattern, same as class_name. Blocks emit a few hundred
        // bytes typical, with the biggest known one (StoreSaveData) at
        // ~30 KB — well past the stack threshold, so this always rents
        // from the pool.
        nuint required = 0;
        var sizeRc = NativeMethods.GetBlockJson(h, index, null, 0, out required);
        if (sizeRc != NativeMethods.BUFFER_TOO_SMALL && sizeRc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(sizeRc,
                $"crimson_save_get_block_json[{index}] (size query) failed: {ErrorName(sizeRc)}");
        }
        if (required <= 1)
        {
            return "{}";
        }

        var rented = ArrayPool<byte>.Shared.Rent((int)required);
        try
        {
            fixed (byte* p = rented)
            {
                var rc = NativeMethods.GetBlockJson(h, index, p, (nuint)rented.Length, out _);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_get_block_json[{index}] failed: {ErrorName(rc)}");
                }
            }
            // Exclude the trailing NUL.
            return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private delegate int GetU16(CrimsonSaveHandle h, out ushort value);
    private delegate int GetU32(CrimsonSaveHandle h, out uint value);
    private delegate int GetI32(CrimsonSaveHandle h, out int value);

    private static ushort ReadU16(CrimsonSaveHandle h, GetU16 fn, string label)
    {
        var rc = fn(h, out var v);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc, $"crimson_save_get_{label} failed: {ErrorName(rc)}");
        }
        return v;
    }

    private static uint ReadU32(CrimsonSaveHandle h, GetU32 fn, string label)
    {
        var rc = fn(h, out var v);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc, $"crimson_save_get_{label} failed: {ErrorName(rc)}");
        }
        return v;
    }

    private static int ReadI32(CrimsonSaveHandle h, GetI32 fn, string label)
    {
        var rc = fn(h, out var v);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc, $"crimson_save_get_{label} failed: {ErrorName(rc)}");
        }
        return v;
    }

    private static unsafe string ReadBlockClassName(CrimsonSaveHandle h, uint index)
    {
        // Two-call pattern: first call sizes the buffer, second fills it.
        // class names observed in 1.06 are all < 64 bytes, so we stack-
        // allocate up to that and fall back to ArrayPool for longer ones.
        nuint required = 0;
        var sizeRc = NativeMethods.GetBlockClassName(h, index, null, 0, out required);
        if (sizeRc != NativeMethods.BUFFER_TOO_SMALL && sizeRc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(sizeRc,
                $"crimson_save_get_block_class_name[{index}] (size query) failed: {ErrorName(sizeRc)}");
        }
        if (required <= 1)
        {
            return string.Empty;
        }

        const int StackThreshold = 64;
        if (required <= StackThreshold)
        {
            Span<byte> stack = stackalloc byte[StackThreshold];
            fixed (byte* p = stack)
            {
                var rc = NativeMethods.GetBlockClassName(h, index, p, (nuint)stack.Length, out _);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_get_block_class_name[{index}] failed: {ErrorName(rc)}");
                }
            }
            return Encoding.UTF8.GetString(stack[..((int)required - 1)]);
        }

        var rented = ArrayPool<byte>.Shared.Rent((int)required);
        try
        {
            fixed (byte* p = rented)
            {
                var rc = NativeMethods.GetBlockClassName(h, index, p, (nuint)rented.Length, out _);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_get_block_class_name[{index}] failed: {ErrorName(rc)}");
                }
            }
            return Encoding.UTF8.GetString(rented, 0, (int)required - 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string ErrorName(int code) => code switch
    {
        NativeMethods.OK                    => "OK",
        NativeMethods.NULL_ARG              => "NULL_ARG",
        NativeMethods.INVALID_PATH          => "INVALID_PATH",
        NativeMethods.IO                    => "IO",
        NativeMethods.TOO_SMALL             => "TOO_SMALL",
        NativeMethods.BAD_MAGIC             => "BAD_MAGIC",
        NativeMethods.UNSUPPORTED_VERSION   => "UNSUPPORTED_VERSION",
        NativeMethods.PAYLOAD_OUT_OF_RANGE  => "PAYLOAD_OUT_OF_RANGE",
        NativeMethods.DECOMPRESS            => "DECOMPRESS",
        NativeMethods.BODY_PARSE            => "BODY_PARSE",
        NativeMethods.OUT_OF_RANGE          => "OUT_OF_RANGE",
        NativeMethods.BUFFER_TOO_SMALL      => "BUFFER_TOO_SMALL",
        NativeMethods.NOT_SCALAR            => "NOT_SCALAR",
        NativeMethods.LENGTH_MISMATCH       => "LENGTH_MISMATCH",
        NativeMethods.WRITE_FAILED          => "WRITE_FAILED",
        NativeMethods.NOT_NAVIGABLE         => "NOT_NAVIGABLE",
        NativeMethods.PANIC                 => "PANIC",
        _                                   => $"UNKNOWN({code})",
    };
}

/// <summary>
/// Owns the raw <c>CrimsonSaveHandle*</c> returned by the Rust ABI and
/// releases it via <c>crimson_save_free</c>. Using SafeHandle prevents
/// double-free and gives us implicit AddRef/Release across all P/Invoke
/// calls that take a handle parameter.
/// </summary>
internal sealed class CrimsonSaveHandle : SafeHandle
{
    // CA1419 requires a parameterless ctor at least as visible as the
    // enclosing type for SafeHandle marshalling. We never let the
    // P/Invoke marshaller construct one of these (LoadFromFile returns
    // IntPtr which we wrap explicitly), but the analyzer is satisfied
    // just by the ctor existing.
    public CrimsonSaveHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public static CrimsonSaveHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonSaveHandle();
        h.SetHandle(ptr);
        return h;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.Free(handle);
        return true;
    }
}

/// <summary>Thrown when the C ABI returns a negative error code.</summary>
public sealed class CrimsonSaveException : Exception
{
    public CrimsonSaveException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}

/// <summary>
/// LibraryImport declarations matching <c>vendor/crimson-rs/src/c_abi/mod.rs</c>.
/// Kept private so callers go through <see cref="NativeSaveLoader"/>.
/// </summary>
internal static partial class NativeMethods
{
    private const string LibraryName = "crimson_rs";

    // Error codes — must match `c_abi::error` in the Rust source.
    public const int OK                    = 0;
    public const int NULL_ARG              = -1;
    public const int INVALID_PATH          = -2;
    public const int IO                    = -3;
    public const int TOO_SMALL             = -4;
    public const int BAD_MAGIC             = -5;
    public const int UNSUPPORTED_VERSION   = -6;
    public const int PAYLOAD_OUT_OF_RANGE  = -7;
    public const int DECOMPRESS            = -8;
    public const int BODY_PARSE            = -9;
    public const int OUT_OF_RANGE          = -10;
    public const int BUFFER_TOO_SMALL      = -11;
    public const int NOT_SCALAR            = -12;
    public const int LENGTH_MISMATCH       = -13;
    public const int WRITE_FAILED          = -14;
    public const int NOT_NAVIGABLE         = -15;
    public const int NOT_FOUND             = -16;
    public const int PANIC                 = -99;

    [StructLayout(LayoutKind.Sequential)]
    public struct CrimsonBlockInfo
    {
        public uint ClassIndex;
        public uint DataOffset;
        public uint DataSize;
        public uint FieldsPresent;
        public uint FieldsDecoded;
    }

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int LoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_free")]
    public static partial void Free(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_version")]
    public static partial int GetVersion(CrimsonSaveHandle handle, out ushort value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_flags")]
    public static partial int GetFlags(CrimsonSaveHandle handle, out ushort value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_hmac_ok")]
    public static partial int GetHmacOk(CrimsonSaveHandle handle, out int value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_payload_size")]
    public static partial int GetPayloadSize(CrimsonSaveHandle handle, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_uncompressed_size")]
    public static partial int GetUncompressedSize(CrimsonSaveHandle handle, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_schema_type_count")]
    public static partial int GetSchemaTypeCount(CrimsonSaveHandle handle, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_toc_entry_count")]
    public static partial int GetTocEntryCount(CrimsonSaveHandle handle, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_block_count")]
    public static partial int GetBlockCount(CrimsonSaveHandle handle, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_block_info")]
    public static partial int GetBlockInfo(CrimsonSaveHandle handle, uint index, out CrimsonBlockInfo info);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_block_class_name")]
    public static unsafe partial int GetBlockClassName(
        CrimsonSaveHandle handle, uint index, byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_block_json")]
    public static unsafe partial int GetBlockJson(
        CrimsonSaveHandle handle, uint index, byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_scalar_field_path")]
    public static unsafe partial int SetScalarFieldPath(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        byte* bytes,
        nuint bytesLen);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_write_to_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int WriteToFile(CrimsonSaveHandle handle, string path);

    // ── PALOC catalog ───────────────────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int PalocLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_load_from_bytes")]
    public static unsafe partial int PalocLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_free")]
    public static partial void PalocFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_entry_count")]
    public static partial int PalocEntryCount(CrimsonPalocHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_lookup")]
    public static unsafe partial int PalocLookup(
        CrimsonPalocHandle handle,
        byte* key, nuint keyLen,
        byte* buf, nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_paloc_get_entry")]
    public static unsafe partial int PalocGetEntry(
        CrimsonPalocHandle handle,
        uint idx,
        byte* keyBuf, nuint keyBufLen, out nuint keyRequired,
        byte* valueBuf, nuint valueBufLen, out nuint valueRequired);

    // ── PAZ extraction ──────────────────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_paz_extract_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int PazExtractFile(
        string pamtPath,
        string directory,
        string fileName,
        byte* outBuf, nuint outBufLen,
        out nuint outRequired);

    // ── ItemInfo bridge (iteminfo.pabgb) ────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ItemInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_load_from_bytes")]
    public static unsafe partial int ItemInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_free")]
    public static partial void ItemInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_entry_count")]
    public static partial int ItemInfoEntryCount(CrimsonItemInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_string_key")]
    public static unsafe partial int ItemInfoLookupStringKey(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        byte* buf, nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_get_entry")]
    public static unsafe partial int ItemInfoGetEntry(
        CrimsonItemInfoHandle handle,
        uint idx,
        out uint outKey,
        byte* buf, nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_max_stack")]
    public static partial int ItemInfoLookupMaxStack(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        out ulong outMaxStack);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_icon_path_hash")]
    public static partial int ItemInfoLookupIconPathHash(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        out uint outHash);

    // ── StringInfo bridge (stringinfo.pabgb) ────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StringInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_load_from_bytes")]
    public static unsafe partial int StringInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_free")]
    public static partial void StringInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_entry_count")]
    public static partial int StringInfoEntryCount(CrimsonStringInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_lookup_by_hash")]
    public static unsafe partial int StringInfoLookupByHash(
        CrimsonStringInfoHandle handle,
        uint hash,
        byte* buf, nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_string_info_get_entry")]
    public static unsafe partial int StringInfoGetEntry(
        CrimsonStringInfoHandle handle,
        uint idx,
        out uint outHash,
        byte* buf, nuint bufLen,
        out nuint required);
}
