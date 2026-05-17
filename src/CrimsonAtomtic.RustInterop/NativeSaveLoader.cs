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

    // BlockDetails cache keyed by blockIndex. Each entry pairs the
    // decoded BlockDetails with the handle's mutation_version at the
    // moment the snapshot was taken. Reads validate by re-reading the
    // current mutation_version: equal → cache hit, mismatched →
    // refetch + replace.
    //
    // Version-based invalidation replaces the older "every mutation
    // entry point clears the cache" pattern that needed manual
    // bookkeeping on each new C ABI mutation surface (per
    // `vendor/crimson-rs/docs/save-mutation-version.md`: "Hardcoding
    // 'invalidate the cache on every save mutation we know about' is
    // fragile — easy to miss an FFI path. Version check is the
    // ground-truth.").
    //
    // For a save like QuestSaveData with 4341 mission elements, one
    // fetch costs ~1-2 s (Rust JSON serialize + C# deserialize);
    // subsequent re-clicks are O(1) — one u64 read for the version
    // check — until the next mutation bumps the version. WriteToFile
    // is intentionally NOT a cache buster — it doesn't touch the body
    // (and doesn't bump the version on the Rust side either).
    //
    // Load(path) and Dispose still clear the cache outright because
    // the mutation_version of a fresh handle starts back at 0 — a
    // stale entry whose stored version happens to equal 0 would
    // otherwise falsely cache-hit against the new handle.
    private readonly Dictionary<int, (ulong Version, BlockDetails Details)> _detailsCache = new();

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
        // The details cache is keyed by blockIndex against the previous handle,
        // so it must be cleared too — even if the new path matches the old,
        // the underlying bytes could have changed on disk between loads.
        CrimsonSaveHandle? previous;
        lock (_cacheLock)
        {
            previous = _cachedHandle;
            _cachedHandle = newHandle;
            _cachedPath = savePath;
            _detailsCache.Clear();
        }
        previous?.Dispose();

        return summary;
    }

    public BlockDetails LoadBlockDetails(string savePath, int blockIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        cancellationToken.ThrowIfCancellationRequested();

        // Fast path: same save still loaded — reuse the cached handle and
        // the per-block details cache. SafeHandle.AddRef'd implicitly when
        // crossing the LibraryImport boundary, so a Dispose race can't yank
        // the native handle out from under the FFI call.
        //
        // Cache validation: each entry pairs (mutation_version,
        // BlockDetails). On hit, query the current version via FFI
        // (one u64 read) and compare. Match → return cached. Mismatch
        // → re-fetch + replace. This catches every mutation path
        // automatically without per-entry-point manual invalidation.
        lock (_cacheLock)
        {
            if (PathsMatch(_cachedPath, savePath)
                && _cachedHandle is { IsInvalid: false } cached)
            {
                var currentVersion = ReadMutationVersionOrThrow(cached);
                if (_detailsCache.TryGetValue(blockIndex, out var hit)
                    && hit.Version == currentVersion)
                {
                    return hit.Details;
                }
                var details = ReadBlockDetails(cached, (uint)blockIndex);
                _detailsCache[blockIndex] = (currentVersion, details);
                return details;
            }
        }

        // Slow path: the cache hasn't been primed for this save (e.g.
        // LoadBlockDetails called before Load, or with a different
        // path). Open transiently and tear down at scope end. Transient
        // reads aren't cached — they don't share state with the live
        // handle and could go stale undetectably.
        using var handle = OpenHandle(savePath);
        return ReadBlockDetails(handle, (uint)blockIndex);
    }

    public ulong GetMutationVersion()
    {
        var cached = RequireLoaded(nameof(GetMutationVersion));
        var rc = NativeMethods.GetMutationVersion(cached, out var v);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_get_mutation_version failed: {ErrorName(rc)}");
        }
        return v;
    }

    public void BeginDeferredRedecode()
    {
        var cached = RequireLoaded(nameof(BeginDeferredRedecode));
        var rc = NativeMethods.BeginDeferredRedecode(cached);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_begin_deferred_redecode failed: {ErrorName(rc)}");
        }
    }

    public void EndDeferredRedecode()
    {
        var cached = RequireLoaded(nameof(EndDeferredRedecode));
        var rc = NativeMethods.EndDeferredRedecode(cached);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_end_deferred_redecode failed: {ErrorName(rc)}");
        }
    }

    public void AbortDeferredRedecode()
    {
        var cached = RequireLoaded(nameof(AbortDeferredRedecode));
        var rc = NativeMethods.AbortDeferredRedecode(cached);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_abort_deferred_redecode failed: {ErrorName(rc)}");
        }
    }

    public bool IsDeferredRedecodeOpen()
    {
        var cached = RequireLoaded(nameof(IsDeferredRedecodeOpen));
        var rc = NativeMethods.IsDeferredRedecodeOpen(cached, out var openFlag);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_is_deferred_redecode_open failed: {ErrorName(rc)}");
        }
        return openFlag != 0;
    }

    /// <summary>
    /// Run <paramref name="body"/> inside a deferred-redecode batch on
    /// the currently-loaded save. The batch suspends per-call
    /// <c>decode_blocks</c> for every mutation entry point, then runs
    /// <b>one</b> encode + parse + decode pass on commit — collapsing
    /// dozens of seconds of bulk-mutation re-decode into ~0.1 s on a
    /// 1.07-baseline save.
    ///
    /// <para>
    /// Exception in <paramref name="body"/> calls
    /// <see cref="AbortDeferredRedecode"/> and rethrows; the handle's
    /// in-memory state is restored to its pre-begin snapshot. Normal
    /// completion calls <see cref="EndDeferredRedecode"/>, which
    /// commits the accumulated tree and bumps
    /// <c>mutation_version</c> exactly once. A <c>MUTATION_INVALID</c>
    /// at commit time is wrapped as <see cref="CrimsonSaveException"/>;
    /// the handle is auto-rolled-back by the Rust side.
    /// </para>
    ///
    /// <para>
    /// For partial-success workflows (e.g. the bulk SA challenge
    /// sweep, which stops on first per-op failure but wants to KEEP
    /// the already-applied work), callers should swallow per-op
    /// exceptions inside <paramref name="body"/> and let it return
    /// normally so the commit lands. Letting the exception escape
    /// here would abort the whole batch.
    /// </para>
    /// </summary>
    public void RunDeferred(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        BeginDeferredRedecode();
        try
        {
            body();
        }
        catch
        {
            try { AbortDeferredRedecode(); }
            catch (CrimsonSaveException) { /* surface the original exception */ }
            throw;
        }
        EndDeferredRedecode();
    }

    public IReadOnlyList<InventoryItemRecord> ListInventoryItems(out ulong version)
    {
        var cached = RequireLoaded(nameof(ListInventoryItems));
        // Two-call shape: first probes count + version, second fills
        // the typed buffer. The Rust side returns OK (not
        // BUFFER_TOO_SMALL) when the save has zero items — handle that
        // case without a second call.
        unsafe
        {
            nuint count = 0;
            ulong v = 0;
            int rc = NativeMethods.ListInventoryItems(
                cached, null, 0, out count, out v);
            if (rc == NativeMethods.OK && count == 0)
            {
                version = v;
                return Array.Empty<InventoryItemRecord>();
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_inventory_items size query failed: {ErrorName(rc)}");
            }
            var buf = new InventoryItemRecord[(int)count];
            fixed (InventoryItemRecord* p = buf)
            {
                rc = NativeMethods.ListInventoryItems(
                    cached, p, count, out _, out v);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_inventory_items fill failed: {ErrorName(rc)}");
            }
            version = v;
            return buf;
        }
    }

    public IReadOnlyList<ItemRecord> ListAllItems(out ulong version)
    {
        var cached = RequireLoaded(nameof(ListAllItems));
        // Same two-call buffer dance as ListInventoryItems.
        unsafe
        {
            nuint count = 0;
            ulong v = 0;
            int rc = NativeMethods.ListAllItems(
                cached, null, 0, out count, out v);
            if (rc == NativeMethods.OK && count == 0)
            {
                version = v;
                return Array.Empty<ItemRecord>();
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_all_items size query failed: {ErrorName(rc)}");
            }
            var buf = new ItemRecord[(int)count];
            fixed (ItemRecord* p = buf)
            {
                rc = NativeMethods.ListAllItems(
                    cached, p, count, out _, out v);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_all_items fill failed: {ErrorName(rc)}");
            }
            version = v;
            return buf;
        }
    }

    public IReadOnlyList<PositionedEntityRecord> ListFieldPositions(out ulong version)
    {
        var cached = RequireLoaded(nameof(ListFieldPositions));
        // Same two-call buffer-dance shape as ListAllItems.
        unsafe
        {
            nuint count = 0;
            ulong v = 0;
            int rc = NativeMethods.ListFieldPositions(
                cached, null, 0, out count, out v);
            if (rc == NativeMethods.OK && count == 0)
            {
                version = v;
                return Array.Empty<PositionedEntityRecord>();
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_field_positions size query failed: {ErrorName(rc)}");
            }
            var buf = new PositionedEntityRecord[(int)count];
            fixed (PositionedEntityRecord* p = buf)
            {
                rc = NativeMethods.ListFieldPositions(
                    cached, p, count, out _, out v);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_field_positions fill failed: {ErrorName(rc)}");
            }
            version = v;
            return buf;
        }
    }

    /// <summary>
    /// Flat-list every <c>CharacterKey</c>-typed scalar (and every
    /// element of <c>DynamicArray&lt;CharacterKey&gt;</c>) across every
    /// top-level block + every ObjectList / Locator descendant in the
    /// currently-loaded save. Mirror of <see cref="ListInventoryItems"/>:
    /// two-call buffer dance, version-stamped for staleness detection.
    /// </summary>
    public IReadOnlyList<CharacterRefRecord> ListCharacterRefs(out ulong version)
    {
        var cached = RequireLoaded(nameof(ListCharacterRefs));
        unsafe
        {
            nuint count = 0;
            ulong v = 0;
            int rc = NativeMethods.ListCharacterRefs(
                cached, null, 0, out count, out v);
            if (rc == NativeMethods.OK && count == 0)
            {
                version = v;
                return Array.Empty<CharacterRefRecord>();
            }
            if (rc != NativeMethods.BUFFER_TOO_SMALL)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_character_refs size query failed: {ErrorName(rc)}");
            }
            var buf = new CharacterRefRecord[(int)count];
            fixed (CharacterRefRecord* p = buf)
            {
                rc = NativeMethods.ListCharacterRefs(
                    cached, p, count, out _, out v);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_save_list_character_refs fill failed: {ErrorName(rc)}");
            }
            version = v;
            return buf;
        }
    }

    public void SetScalarField(int blockIndex, int fieldIndex, ReadOnlySpan<byte> bytes) =>
        SetScalarField(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, bytes);

    public void SetScalarField(int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        // No manual cache invalidation needed — the Rust side bumps
        // mutation_version on the success path, and LoadBlockDetails
        // validates cache entries against it on every read.
        var cached = RequireLoaded(nameof(SetScalarField));

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

    public void SetScalarFieldsBatch(IReadOnlyList<ScalarBatchOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var count = ops.Count;
        if (count == 0)
        {
            // The Rust ABI also treats op_count == 0 as a no-op.
            return;
        }

        var cached = RequireLoaded(nameof(SetScalarFieldsBatch));

        // Per-op input checks (block/field non-negative). These mirror
        // the ArgumentOutOfRangeException pattern the single-op setter
        // uses for its top-level args, so a bad input is caught with
        // the same exception shape regardless of which entry point the
        // caller picked.
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.BlockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].BlockIndex must be non-negative, was {op.BlockIndex}.");
            }
            if (op.FieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].FieldIndex must be non-negative, was {op.FieldIndex}.");
            }
        }

        // Pack all paths into one PathStep[] arena and all bytes into
        // one byte[] arena, recording each op's offset. This gives us
        // 3 GC pins regardless of `count` instead of 2 × count via
        // per-op GCHandle.Alloc — much cheaper at the ~168-op scale
        // the production "Fill stacks" path runs.
        var pathOffsets = new int[count];
        var bytesOffsets = new int[count];
        var totalPathSteps = 0;
        var totalBytes = 0;
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            pathOffsets[i] = totalPathSteps;
            bytesOffsets[i] = totalBytes;
            totalPathSteps += op.Path?.Length ?? 0;
            totalBytes += op.Bytes?.Length ?? 0;
        }

        var pathArena = totalPathSteps == 0 ? Array.Empty<PathStep>() : new PathStep[totalPathSteps];
        var bytesArena = totalBytes == 0 ? Array.Empty<byte>() : new byte[totalBytes];
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.Path is { Length: > 0 } path)
            {
                Array.Copy(path, 0, pathArena, pathOffsets[i], path.Length);
            }
            if (op.Bytes is { Length: > 0 } bytes)
            {
                Array.Copy(bytes, 0, bytesArena, bytesOffsets[i], bytes.Length);
            }
        }

        var cOps = new NativeMethods.CrimsonScalarBatchOp[count];
        unsafe
        {
            fixed (PathStep* pPath = pathArena)
            fixed (byte* pBytes = bytesArena)
            {
                for (var i = 0; i < count; i++)
                {
                    var op = ops[i];
                    var pathLen = op.Path?.Length ?? 0;
                    var bytesLen = op.Bytes?.Length ?? 0;
                    cOps[i] = new NativeMethods.CrimsonScalarBatchOp
                    {
                        BlockIdx = (uint)op.BlockIndex,
                        FieldIdx = (uint)op.FieldIndex,
                        // Empty-slice ops carry a null pointer with
                        // matching zero length; Rust gates the deref
                        // on the length, but we keep things tidy.
                        Path     = pathLen == 0 ? null : pPath + pathOffsets[i],
                        PathLen  = (nuint)pathLen,
                        Bytes    = bytesLen == 0 ? null : pBytes + bytesOffsets[i],
                        BytesLen = (nuint)bytesLen,
                    };
                }

                fixed (NativeMethods.CrimsonScalarBatchOp* pOps = cOps)
                {
                    var rc = NativeMethods.SetScalarFieldsBatch(
                        cached, pOps, (nuint)count, out var failedIdx);
                    if (rc != NativeMethods.OK)
                    {
                        // usize::MAX (~unsigned -1) is the success
                        // sentinel — never written on error, but
                        // guard against a defensive Rust write anyway.
                        int? failedOpIdx = failedIdx == nuint.MaxValue
                            ? null
                            : checked((int)failedIdx);
                        throw new CrimsonSaveException(
                            rc,
                            failedOpIdx is { } fi
                                ? $"crimson_save_set_scalar_fields_batch failed at op {fi}/{count}: {ErrorName(rc)}"
                                : $"crimson_save_set_scalar_fields_batch failed: {ErrorName(rc)}",
                            failedOpIdx);
                    }
                }
            }
        }
    }

    public void SetScalarFieldsPresentBatch(IReadOnlyList<ScalarPresentBatchOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var count = ops.Count;
        if (count == 0)
        {
            return;
        }

        var cached = RequireLoaded(nameof(SetScalarFieldsPresentBatch));

        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.BlockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].BlockIndex must be non-negative, was {op.BlockIndex}.");
            }
            if (op.FieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].FieldIndex must be non-negative, was {op.FieldIndex}.");
            }
        }

        // Same arena-packing trick the scalar batch uses — 3 GC pins
        // total regardless of `count`. PathStep[] for every op's
        // descent, byte[] for every op's payload.
        var pathOffsets = new int[count];
        var bytesOffsets = new int[count];
        var totalPathSteps = 0;
        var totalBytes = 0;
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            pathOffsets[i] = totalPathSteps;
            bytesOffsets[i] = totalBytes;
            totalPathSteps += op.Path?.Length ?? 0;
            totalBytes += op.Bytes?.Length ?? 0;
        }
        var pathArena = totalPathSteps == 0 ? Array.Empty<PathStep>() : new PathStep[totalPathSteps];
        var bytesArena = totalBytes == 0 ? Array.Empty<byte>() : new byte[totalBytes];
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.Path is { Length: > 0 } path)
            {
                Array.Copy(path, 0, pathArena, pathOffsets[i], path.Length);
            }
            if (op.Bytes is { Length: > 0 } bytes)
            {
                Array.Copy(bytes, 0, bytesArena, bytesOffsets[i], bytes.Length);
            }
        }

        var cOps = new NativeMethods.CrimsonScalarPresentBatchOp[count];
        unsafe
        {
            fixed (PathStep* pPath = pathArena)
            fixed (byte* pBytes = bytesArena)
            {
                for (var i = 0; i < count; i++)
                {
                    var op = ops[i];
                    var pathLen = op.Path?.Length ?? 0;
                    var bytesLen = op.Bytes?.Length ?? 0;
                    cOps[i] = new NativeMethods.CrimsonScalarPresentBatchOp
                    {
                        BlockIdx     = (uint)op.BlockIndex,
                        FieldIdx     = (uint)op.FieldIndex,
                        Path         = pathLen == 0 ? null : pPath + pathOffsets[i],
                        PathLen      = (nuint)pathLen,
                        MakePresent  = op.MakePresent ? 1 : 0,
                        Bytes        = bytesLen == 0 ? null : pBytes + bytesOffsets[i],
                        BytesLen     = (nuint)bytesLen,
                    };
                }
                fixed (NativeMethods.CrimsonScalarPresentBatchOp* pOps = cOps)
                {
                    var rc = NativeMethods.SetScalarFieldsPresentBatch(
                        cached, pOps, (nuint)count, out var failedIdx);
                    if (rc != NativeMethods.OK)
                    {
                        int? failedOpIdx = failedIdx == nuint.MaxValue
                            ? null
                            : checked((int)failedIdx);
                        throw new CrimsonSaveException(
                            rc,
                            failedOpIdx is { } fi
                                ? $"crimson_save_set_scalar_fields_present_batch failed at op {fi}/{count}: {ErrorName(rc)}"
                                : $"crimson_save_set_scalar_fields_present_batch failed: {ErrorName(rc)}",
                            failedOpIdx);
                    }
                }
            }
        }
    }

    public void ListRemoveElementsBatch(IReadOnlyList<ListRemoveBatchOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var count = ops.Count;
        if (count == 0)
        {
            return;
        }

        var cached = RequireLoaded(nameof(ListRemoveElementsBatch));

        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.BlockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].BlockIndex must be non-negative, was {op.BlockIndex}.");
            }
            if (op.FieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].FieldIndex must be non-negative, was {op.FieldIndex}.");
            }
            if (op.ElementIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ops),
                    $"ops[{i}].ElementIndex must be non-negative, was {op.ElementIndex}.");
            }
        }

        var pathOffsets = new int[count];
        var totalPathSteps = 0;
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            pathOffsets[i] = totalPathSteps;
            totalPathSteps += op.Path?.Length ?? 0;
        }
        var pathArena = totalPathSteps == 0 ? Array.Empty<PathStep>() : new PathStep[totalPathSteps];
        for (var i = 0; i < count; i++)
        {
            var op = ops[i];
            if (op.Path is { Length: > 0 } path)
            {
                Array.Copy(path, 0, pathArena, pathOffsets[i], path.Length);
            }
        }

        var cOps = new NativeMethods.CrimsonListRemoveBatchOp[count];
        unsafe
        {
            fixed (PathStep* pPath = pathArena)
            {
                for (var i = 0; i < count; i++)
                {
                    var op = ops[i];
                    var pathLen = op.Path?.Length ?? 0;
                    cOps[i] = new NativeMethods.CrimsonListRemoveBatchOp
                    {
                        BlockIdx   = (uint)op.BlockIndex,
                        FieldIdx   = (uint)op.FieldIndex,
                        Path       = pathLen == 0 ? null : pPath + pathOffsets[i],
                        PathLen    = (nuint)pathLen,
                        ElementIdx = (uint)op.ElementIndex,
                    };
                }
                fixed (NativeMethods.CrimsonListRemoveBatchOp* pOps = cOps)
                {
                    var rc = NativeMethods.ListRemoveElementsBatch(
                        cached, pOps, (nuint)count, out var failedIdx);
                    if (rc != NativeMethods.OK)
                    {
                        int? failedOpIdx = failedIdx == nuint.MaxValue
                            ? null
                            : checked((int)failedIdx);
                        throw new CrimsonSaveException(
                            rc,
                            failedOpIdx is { } fi
                                ? $"crimson_save_list_remove_elements_batch failed at op {fi}/{count}: {ErrorName(rc)}"
                                : $"crimson_save_list_remove_elements_batch failed: {ErrorName(rc)}",
                            failedOpIdx);
                    }
                }
            }
        }
    }

    public void DynamicArraySetU32Elements(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        ReadOnlySpan<uint> newElements)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        var cached = RequireLoaded(nameof(DynamicArraySetU32Elements));
        unsafe
        {
            fixed (PathStep* pPath = path)
            fixed (uint* pElems = newElements)
            {
                var rc = NativeMethods.DynamicArraySetU32Elements(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    pElems,
                    (nuint)newElements.Length);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_dynamic_array_set_u32_elements(block={blockIndex}, "
                        + $"path_len={path.Length}, field={fieldIndex}, count={newElements.Length}) failed: "
                        + $"{ErrorName(rc)}");
                }
            }
        }
    }

    public void SetInlineBytesField(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        ReadOnlySpan<byte> newBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        var cached = RequireLoaded(nameof(SetInlineBytesField));
        unsafe
        {
            fixed (PathStep* pPath = path)
            fixed (byte* pBytes = newBytes)
            {
                var rc = NativeMethods.SetInlineBytesField(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    pBytes,
                    (nuint)newBytes.Length);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_set_inline_bytes_field(block={blockIndex}, "
                        + $"path_len={path.Length}, field={fieldIndex}, bytes_len={newBytes.Length}) failed: "
                        + $"{ErrorName(rc)}");
                }
            }
        }
    }

    public uint[] DynamicArrayGetU32Elements(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        var cached = RequireLoaded(nameof(DynamicArrayGetU32Elements));
        unsafe
        {
            // Two-call: size query first.
            nuint required = 0;
            fixed (PathStep* pPath = path)
            {
                var sizeRc = NativeMethods.DynamicArrayGetU32Elements(
                    cached, (uint)blockIndex, pPath, (nuint)path.Length,
                    (uint)fieldIndex, null, 0, out required);
                if (sizeRc != NativeMethods.BUFFER_TOO_SMALL && sizeRc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(sizeRc,
                        $"crimson_save_dynamic_array_get_u32_elements(block={blockIndex}, "
                        + $"path_len={path.Length}, field={fieldIndex}) size query failed: "
                        + $"{ErrorName(sizeRc)}");
                }
            }
            if (required == 0)
            {
                return Array.Empty<uint>();
            }

            var buf = new uint[(int)required];
            fixed (PathStep* pPath = path)
            fixed (uint* pBuf = buf)
            {
                var rc = NativeMethods.DynamicArrayGetU32Elements(
                    cached, (uint)blockIndex, pPath, (nuint)path.Length,
                    (uint)fieldIndex, pBuf, (nuint)buf.Length, out _);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_dynamic_array_get_u32_elements(block={blockIndex}, "
                        + $"path_len={path.Length}, field={fieldIndex}) fill failed: "
                        + $"{ErrorName(rc)}");
                }
            }
            return buf;
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

    // ── Length-changing edits (PR B) ───────────────────────────────────────

    public void ListRemoveElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int elementIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);

        var cached = RequireLoaded(nameof(ListRemoveElement));
        unsafe
        {
            fixed (PathStep* pPath = path)
            {
                var rc = NativeMethods.ListRemoveElement(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    (uint)elementIndex);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_list_remove_element(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, element={elementIndex}) failed: {ErrorName(rc)}");
                }
            }
        }
    }

    public void ListCloneElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int sourceIndex,
        int destinationIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);

        var cached = RequireLoaded(nameof(ListCloneElement));
        unsafe
        {
            fixed (PathStep* pPath = path)
            {
                var rc = NativeMethods.ListCloneElement(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    (uint)sourceIndex,
                    (uint)destinationIndex);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_list_clone_element(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, src={sourceIndex}, dst={destinationIndex}) failed: {ErrorName(rc)}");
                }
            }
        }
    }

    public void SetScalarFieldPresent(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        bool makePresent,
        ReadOnlySpan<byte> initialBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        var cached = RequireLoaded(nameof(SetScalarFieldPresent));
        unsafe
        {
            fixed (PathStep* pPath = path)
            fixed (byte* pInit = initialBytes)
            {
                var rc = NativeMethods.SetScalarFieldPresent(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    makePresent ? 1 : 0,
                    pInit,
                    (nuint)initialBytes.Length);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_set_scalar_field_present(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, present={makePresent}, bytes={initialBytes.Length}) failed: " +
                        $"{ErrorName(rc)}");
                }
            }
        }
    }

    /// <summary>
    /// Toggle the presence of an <c>ObjectList</c> field. Closes the
    /// "add dye to undyed item" path: <c>makePresent=true</c> flips the
    /// mask bit + auto-materializes a <c>count=1</c> list with one
    /// default-empty element (element class copied from any sibling
    /// block of the same parent class with the field present).
    /// Caller drives the element's scalars via
    /// <see cref="SetScalarFieldPresent"/> /
    /// <see cref="SetScalarField(int, ReadOnlySpan{PathStep}, int, ReadOnlySpan{byte})"/>
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Error codes:
    /// <list type="bullet">
    ///   <item><c>NOT_OBJECT_LIST (-23)</c>: target field's schema
    ///     <c>meta_kind</c> isn't 6 or 7 (scalar / inline-bytes /
    ///     dynamic-array fields use different presence-toggle ABIs).</item>
    ///   <item><c>NOT_FOUND (-16)</c>: <c>makePresent=true</c> with
    ///     no sibling block in the save providing a template element.
    ///     UX: prompt the user to perform the equivalent action
    ///     in-game first (e.g. "dye one item") so the schema sample
    ///     becomes available.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>makePresent=false</c> is byte-identical to never-present:
    /// the field's mask bit clears, existing elements are discarded.
    /// Round-trip-safe — present→absent→present yields the same
    /// default empty element each time.
    /// </para>
    /// <para>
    /// Full contract: <c>vendor/crimson-rs/docs/dye-editor-scope.md</c> §v2.
    /// </para>
    /// </remarks>
    public void SetObjectListPresent(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        bool makePresent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);

        var cached = RequireLoaded(nameof(SetObjectListPresent));
        unsafe
        {
            fixed (PathStep* pPath = path)
            {
                var rc = NativeMethods.SetObjectListPresent(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    makePresent ? 1 : 0);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_set_object_list_present(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, present={makePresent}) failed: {ErrorName(rc)}");
                }
            }
        }
    }

    public byte[] MakeEmptyElementBytes(int classIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(classIndex);

        var cached = RequireLoaded(nameof(MakeEmptyElementBytes));
        nuint required = 0;
        unsafe
        {
            var sizeRc = NativeMethods.MakeEmptyElementBytes(
                cached, (uint)classIndex, null, 0, out required);
            if (sizeRc != NativeMethods.BUFFER_TOO_SMALL && sizeRc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(sizeRc,
                    $"crimson_save_make_empty_element_bytes(class={classIndex}) size query failed: " +
                    $"{ErrorName(sizeRc)}");
            }
        }
        if (required == 0)
        {
            return Array.Empty<byte>();
        }

        var buf = new byte[(int)required];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var rc = NativeMethods.MakeEmptyElementBytes(
                    cached, (uint)classIndex, p, (nuint)buf.Length, out _);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_make_empty_element_bytes(class={classIndex}) fill failed: " +
                        $"{ErrorName(rc)}");
                }
            }
        }
        return buf;
    }

    public void ListInsertElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int insertAt,
        ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(fieldIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(insertAt);

        var cached = RequireLoaded(nameof(ListInsertElement));
        unsafe
        {
            fixed (PathStep* pPath = path)
            fixed (byte* pBytes = bytes)
            {
                var rc = NativeMethods.ListInsertElement(
                    cached,
                    (uint)blockIndex,
                    pPath,
                    (nuint)path.Length,
                    (uint)fieldIndex,
                    (uint)insertAt,
                    pBytes,
                    (nuint)bytes.Length);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_save_list_insert_element(block={blockIndex}, path_len={path.Length}, " +
                        $"field={fieldIndex}, insert_at={insertAt}, bytes={bytes.Length}) failed: " +
                        $"{ErrorName(rc)}");
                }
            }
        }
    }

    /// <summary>
    /// Validate a save is loaded and return its cached handle.
    /// </summary>
    /// <remarks>
    /// Mutation entry points used to pass a <c>invalidateDetailsCache</c>
    /// flag to drop the per-block details cache atomically with the
    /// handle read. That flag is gone — the cache now validates entries
    /// against the handle's <c>mutation_version</c> on every read, so
    /// stale data can't survive a mutation regardless of which entry
    /// point committed it.
    /// </remarks>
    private CrimsonSaveHandle RequireLoaded(string caller)
    {
        CrimsonSaveHandle? cached;
        lock (_cacheLock)
        {
            cached = _cachedHandle;
        }
        if (cached is null || cached.IsInvalid)
        {
            throw new InvalidOperationException(
                $"No save is currently loaded. Call Load(savePath) before {caller}.");
        }
        return cached;
    }

    /// <summary>
    /// Read the live mutation_version on the cached handle, throwing
    /// if the FFI call rejects. Called by <see cref="LoadBlockDetails"/>
    /// to validate cache entries; lifted out so the cache-check path
    /// stays under the lock without re-entering the public method
    /// (and its own RequireLoaded).
    /// </summary>
    private static ulong ReadMutationVersionOrThrow(CrimsonSaveHandle handle)
    {
        var rc = NativeMethods.GetMutationVersion(handle, out var v);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_save_get_mutation_version failed: {ErrorName(rc)}");
        }
        return v;
    }

    public void Dispose()
    {
        CrimsonSaveHandle? toDispose;
        lock (_cacheLock)
        {
            toDispose = _cachedHandle;
            _cachedHandle = null;
            _cachedPath = null;
            _detailsCache.Clear();
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
        NativeMethods.NOT_FOUND             => "NOT_FOUND",
        NativeMethods.LIST_VARIANT_UNSUPPORTED => "LIST_VARIANT_UNSUPPORTED",
        NativeMethods.NOT_SCALAR_FIELD_KIND => "NOT_SCALAR_FIELD_KIND",
        NativeMethods.MUTATION_INVALID      => "MUTATION_INVALID",
        NativeMethods.NOT_INLINE_BYTES      => "NOT_INLINE_BYTES",
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

    public CrimsonSaveException(int errorCode, string message, int? failedOpIndex) : base(message)
    {
        ErrorCode = errorCode;
        FailedOpIndex = failedOpIndex;
    }

    public int ErrorCode { get; }

    /// <summary>
    /// When set, identifies which op in a
    /// <see cref="ISaveLoader.SetScalarFieldsBatch(System.Collections.Generic.IReadOnlyList{ScalarBatchOp})"/>
    /// call failed validation (zero-based index into the input list).
    /// <c>null</c> on errors from single-op entry points or when the
    /// batch ABI couldn't pinpoint a specific op (e.g. NULL handle).
    /// </summary>
    public int? FailedOpIndex { get; }
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
    // Length-changing edit error codes (PR B).
    public const int LIST_VARIANT_UNSUPPORTED = -17;
    public const int NOT_SCALAR_FIELD_KIND    = -18;
    public const int MUTATION_INVALID         = -19;
    public const int NOT_INLINE_BYTES         = -20;
    // Deferred-redecode batch error codes (per
    // vendor/crimson-rs/docs/save-deferred-redecode.md).
    public const int BATCH_IN_PROGRESS        = -21;
    public const int BATCH_NOT_OPEN           = -22;
    // Object-list presence-toggle error (per
    // vendor/crimson-rs/docs/dye-editor-scope.md §v2).
    public const int NOT_OBJECT_LIST          = -23;
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

    /// <summary>
    /// Mirror of <c>CrimsonScalarBatchOp</c> in
    /// <c>vendor/crimson-rs/src/c_abi/mod.rs</c>. Layout-compatible
    /// repr(C) — passed across the FFI by pointer + length pair to
    /// <see cref="SetScalarFieldsBatch"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CrimsonScalarBatchOp
    {
        public uint BlockIdx;
        public uint FieldIdx;
        public PathStep* Path;
        public nuint PathLen;
        public byte* Bytes;
        public nuint BytesLen;
    }

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_scalar_fields_batch")]
    public static unsafe partial int SetScalarFieldsBatch(
        CrimsonSaveHandle handle,
        CrimsonScalarBatchOp* ops,
        nuint opCount,
        out nuint failedOpIndex);

    /// <summary>
    /// Mirror of <c>CrimsonScalarPresentBatchOp</c> in
    /// <c>vendor/crimson-rs/src/c_abi/mod.rs</c>. Repr(C) — passed by
    /// pointer + length pair to <see cref="SetScalarFieldsPresentBatch"/>.
    /// <c>MakePresent</c> is i32 (not bool) on the Rust side; we use
    /// 0/1 for absent/present.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CrimsonScalarPresentBatchOp
    {
        public uint BlockIdx;
        public uint FieldIdx;
        public PathStep* Path;
        public nuint PathLen;
        public int MakePresent;
        public byte* Bytes;
        public nuint BytesLen;
    }

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_scalar_fields_present_batch")]
    public static unsafe partial int SetScalarFieldsPresentBatch(
        CrimsonSaveHandle handle,
        CrimsonScalarPresentBatchOp* ops,
        nuint opCount,
        out nuint failedOpIndex);

    /// <summary>
    /// Mirror of <c>CrimsonListRemoveBatchOp</c> in
    /// <c>vendor/crimson-rs/src/c_abi/mod.rs</c>. Repr(C).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CrimsonListRemoveBatchOp
    {
        public uint BlockIdx;
        public uint FieldIdx;
        public PathStep* Path;
        public nuint PathLen;
        public uint ElementIdx;
    }

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_remove_elements_batch")]
    public static unsafe partial int ListRemoveElementsBatch(
        CrimsonSaveHandle handle,
        CrimsonListRemoveBatchOp* ops,
        nuint opCount,
        out nuint failedOpIndex);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_dynamic_array_set_u32_elements")]
    public static unsafe partial int DynamicArraySetU32Elements(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        uint* newElements,
        nuint newCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_dynamic_array_get_u32_elements")]
    public static unsafe partial int DynamicArrayGetU32Elements(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        uint* outBuf,
        nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_inline_bytes_field")]
    public static unsafe partial int SetInlineBytesField(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        byte* newBytes,
        nuint newBytesLen);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_write_to_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int WriteToFile(CrimsonSaveHandle handle, string path);

    // ── Length-changing edits (PR B) ────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_remove_element")]
    public static unsafe partial int ListRemoveElement(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        uint elementIdx);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_clone_element")]
    public static unsafe partial int ListCloneElement(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        uint srcElementIdx,
        uint dstElementIdx);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_scalar_field_present")]
    public static unsafe partial int SetScalarFieldPresent(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        int presentFlag,
        byte* initBytes,
        nuint initLen);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_set_object_list_present")]
    public static unsafe partial int SetObjectListPresent(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        int presentFlag);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_make_empty_element_bytes")]
    public static unsafe partial int MakeEmptyElementBytes(
        CrimsonSaveHandle handle,
        uint classIndex,
        byte* buf,
        nuint bufLen,
        out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_insert_element")]
    public static unsafe partial int ListInsertElement(
        CrimsonSaveHandle handle,
        uint blockIdx,
        PathStep* path,
        nuint pathLen,
        uint fieldIdx,
        uint insertAt,
        byte* bytes,
        nuint bytesLen);

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

    // ── Mutation-version counter ────────────────────────────────────────────
    //
    // Monotonic u64 bumped by exactly 1 on every successful mutation
    // through the C ABI surface. Pure reads (LoadBlockDetails, snapshot
    // listings, etc.) DO NOT bump it. Pair with snapshot-style reads
    // for cheap O(1) staleness detection.

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_get_mutation_version")]
    public static partial int GetMutationVersion(CrimsonSaveHandle handle, out ulong outVersion);

    // ── Deferred-redecode batch (suspend per-call decode_blocks) ────────────
    //
    // Transactional wrapper that lets bulk-mutation flows collapse N
    // per-op encode + parse + decode_blocks cycles into ONE on the
    // matching end_*. The motivating consumer is the "Complete All
    // Held Sealed Abyss Artifact Challenges" sweep — 3 length-changing
    // calls × 141 challenges ≈ 423 re-decodes (~10s) collapses to 1
    // (~0.1s). See vendor/crimson-rs/docs/save-deferred-redecode.md.
    //
    // Contract:
    //  - begin_*: no nesting. Returns BATCH_IN_PROGRESS if a batch is
    //    already open on this handle.
    //  - end_*: commits the accumulated tree, bumps mutation_version
    //    exactly once. Returns MUTATION_INVALID on encode/re-parse
    //    failure (handle is auto-rolled-back to pre-begin state).
    //  - abort_*: discards every in-batch mutation, restores
    //    pre-begin mutation_version.
    //  - write_to_file is rejected with BATCH_IN_PROGRESS while a
    //    batch is open — caller must end_* / abort_* first.
    //  - Reads (get_block_json / list_inventory_items / etc.) work
    //    normally during a batch (see the in-progress tree).

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_begin_deferred_redecode")]
    public static partial int BeginDeferredRedecode(CrimsonSaveHandle handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_end_deferred_redecode")]
    public static partial int EndDeferredRedecode(CrimsonSaveHandle handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_abort_deferred_redecode")]
    public static partial int AbortDeferredRedecode(CrimsonSaveHandle handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_is_deferred_redecode_open")]
    public static partial int IsDeferredRedecodeOpen(CrimsonSaveHandle handle, out int outOpen);

    // ── Inventory flat enumeration ──────────────────────────────────────────
    //
    // Single-FFI walk of every `_inventoryList[N]._itemList[M]` slot
    // across every InventorySaveData block. 48-byte repr(C) records,
    // blittable to C# InventoryItemRecord. The third out-param is the
    // mutation-version snapshot — callers pair it with the records so
    // a later GetMutationVersion call detects staleness.

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_inventory_items")]
    public static unsafe partial int ListInventoryItems(
        CrimsonSaveHandle handle,
        InventoryItemRecord* outRecords,
        nuint capacityRecords,
        out nuint outCountRecords,
        out ulong outVersion);

    // ── Cross-container item flat enumeration ───────────────────────────────
    //
    // Single-FFI walk of every player-relevant item slot across five
    // container kinds (ActiveEquip / ActiveUseReserve / Inventory /
    // MercenaryEquip / MercenaryInventory). 64-byte repr(C) records,
    // blittable to C# ItemRecord. The third out-param is the
    // mutation-version snapshot — callers pair it with the records so
    // a later GetMutationVersion call detects staleness.
    //
    // Replaces / supersedes ListInventoryItems for editors that need
    // equipped gear or mercenary-side items. The two ABIs coexist —
    // ListInventoryItems is retained for callers that only care about
    // the inventory bags (e.g. the dye/sockets editors' v1 walkers
    // before they were rewritten).

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_all_items")]
    public static unsafe partial int ListAllItems(
        CrimsonSaveHandle handle,
        ItemRecord* outRecords,
        nuint capacityRecords,
        out nuint outCountRecords,
        out ulong outVersion);

    // ── Positioned-entity enumeration (world-map plotting) ──────────────────
    //
    // Yields every save-side positioned entity (active char + present-
    // _spawnPosition mercenaries + present-_transform field gimmicks).
    // 56-byte repr(C) records, blittable to PositionedEntityRecord.
    // pos_x/y/z are already in the global frame — apply the documented
    // basemap affine for pixel coords (see vendor/crimson-rs/docs/
    // worldmap-plotting.md). Same two-call buffer-dance + version-stamp
    // shape as ListAllItems.

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_field_positions")]
    public static unsafe partial int ListFieldPositions(
        CrimsonSaveHandle handle,
        PositionedEntityRecord* outRecords,
        nuint capacityRecords,
        out nuint outCountRecords,
        out ulong outVersion);

    // ── CharacterKey reference flat enumeration ─────────────────────────────
    //
    // Walks every top-level block + descends into ObjectList / Locator
    // children, emitting one 16-byte repr(C) record per schema-tagged
    // CharacterKey occurrence (scalar or DynamicArray element). The
    // third out-param is the mutation-version snapshot — callers pair
    // it with the records so a later GetMutationVersion call detects
    // staleness.

    [LibraryImport(LibraryName, EntryPoint = "crimson_save_list_character_refs")]
    public static unsafe partial int ListCharacterRefs(
        CrimsonSaveHandle handle,
        CharacterRefRecord* outRecords,
        nuint capacityRecords,
        out nuint outCountRecords,
        out ulong outVersion);

    // ── PAZ extraction ──────────────────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_paz_extract_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int PazExtractFile(
        string pamtPath,
        string directory,
        string fileName,
        byte* outBuf, nuint outBufLen,
        out nuint outRequired);

    // List every NPC portrait DDS file in a PAZ group's PAMT. Output is
    // a NUL-separated UTF-8 list of `<dir>/<filename>` paths. Filters
    // out non-NPC "portrait-like" images (animal / riding / pet / wagon
    // / knowledge thumbnails) — see the Rust prefix table in paz.rs.
    [LibraryImport(LibraryName, EntryPoint = "crimson_paz_list_npc_portraits",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int PazListNpcPortraits(
        string pamtPath,
        byte* outBuf, nuint outBufLen,
        out nuint outRequired,
        out uint outCount);

    // List every file in a PAMT directory as a flat 272-byte repr(C)
    // record stream. Powers the world-map basemap discovery pipeline —
    // pair with PazExtractFile to pull each discovered file. Same
    // two-call buffer-dance shape as ListAllItems / ListFieldPositions.
    [LibraryImport(LibraryName, EntryPoint = "crimson_paz_list_dir",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int PazListDir(
        string pamtPath,
        string directory,
        PazFileEntry* outEntries,
        nuint capacityEntries,
        out nuint outCountEntries);

    // ── Main quest chapter rollup (curated static table) ────────────────────
    //
    // No handle, no file load — pure static data backed by the curated
    // (chapter, arc, mission) rows in
    // vendor/crimson-rs/docs/ref-gamedata/main-quest-list.md. ~170 rows
    // across Prologue + 12 chapters + Epilogue.

    [LibraryImport(LibraryName, EntryPoint = "crimson_main_quest_table_entry_count")]
    public static partial int MainQuestTableEntryCount(out uint outCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_main_quest_table_get_entry")]
    public static unsafe partial int MainQuestTableGetEntry(
        uint idx,
        byte* chapterBuf, nuint chapterBufLen, out nuint chapterRequired,
        byte* arcBuf, nuint arcBufLen, out nuint arcRequired,
        byte* missionBuf, nuint missionBufLen, out nuint missionRequired);

    [LibraryImport(LibraryName, EntryPoint = "crimson_main_quest_chapter_for_arc",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int MainQuestChapterForArc(
        string arcTitle, byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_main_quest_chapter_for_mission",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int MainQuestChapterForMission(
        string missionTitle, byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_main_quest_arc_for_mission",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int MainQuestArcForMission(
        string missionTitle, byte* buf, nuint bufLen, out nuint required);

    // ── Side quest faction rollup (curated static table) ────────────────────
    //
    // Sibling of main_quest_chapter — flat (quest_title, faction_name)
    // rollup from vendor/crimson-rs/docs/ref-gamedata/side-quest-list.md.
    // 84 quests across 22 factions.

    [LibraryImport(LibraryName, EntryPoint = "crimson_side_quest_table_entry_count")]
    public static partial int SideQuestTableEntryCount(out uint outCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_side_quest_table_get_entry")]
    public static unsafe partial int SideQuestTableGetEntry(
        uint idx,
        byte* questBuf, nuint questBufLen, out nuint questRequired,
        byte* factionBuf, nuint factionBufLen, out nuint factionRequired);

    [LibraryImport(LibraryName, EntryPoint = "crimson_side_quest_faction_for_quest",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int SideQuestFactionForQuest(
        string questTitle, byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_side_quest_quest_count_for_faction",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SideQuestQuestCountForFaction(
        string factionName, out uint outCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_side_quest_quest_at_for_faction",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int SideQuestQuestAtForFaction(
        string factionName, uint idx,
        byte* buf, nuint bufLen, out nuint required);

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

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_look_detail_mission_info")]
    public static partial int ItemInfoLookupLookDetailMissionInfo(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        out uint outMissionKey);

    // Reverse of look_detail_mission_info: given a MissionKey, find the
    // ItemKey of the artifact whose pickup starts that challenge. 1:1
    // mapping verified by upstream (141 missions / 141 items in 1.07,
    // all named Challenge_SealedArtifact_*). Used by the catalog UI's
    // "Sealed Artifact required" badge — not strictly needed for the
    // bulk-complete-held flow (which goes artifact → mission via
    // ItemInfoLookupLookDetailMissionInfo) but bound here for symmetry.
    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_artifact_for_mission")]
    public static partial int ItemInfoLookupArtifactForMission(
        CrimsonItemInfoHandle handle,
        uint missionKey,
        out uint outItemKey);

    // ── ItemInfo socket caps + canonical gem set ────────────────────────────
    //
    // Gamedata-side answers for the Sockets editor v2: how many sockets
    // is this item allowed to have, and what's the canonical gem set
    // (used both as the gem-picker fallback source and the
    // gem-id-validation lookup). Save's `_validSocketCount` /
    // `_maxSocketCount` may legitimately diverge from these (CE-bumped
    // overflow); the editor surfaces the gamedata view but doesn't
    // enforce it — per user request, fills are allowed up to the slot
    // list's actual capacity regardless of gamedata.

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_lookup_socket_caps")]
    public static partial int ItemInfoLookupSocketCaps(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        out byte outUseSocket,
        out byte outValidCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_socket_allows_gem")]
    public static partial int ItemInfoSocketAllowsGem(
        CrimsonItemInfoHandle handle,
        uint itemKey,
        uint gemKey,
        out byte outAllowed);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_canonical_gem_count")]
    public static partial int ItemInfoCanonicalGemCount(
        CrimsonItemInfoHandle handle,
        out uint outCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_iteminfo_canonical_gem_at")]
    public static partial int ItemInfoCanonicalGemAt(
        CrimsonItemInfoHandle handle,
        uint idx,
        out uint outGemKey);

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

    // ── MissionInfo bridge (missioninfo.pabgb) ──────────────────────────────
    //
    // Mission / Quest / Stage / Knowledge all share the same shape: load
    // (file/bytes) + free + entry_count + lookup_string_key (internal name
    // fallback) + lookup_display_name (PALOC-resolved title via the
    // hashlittle2 hash hop, takes a CrimsonPalocHandle + lo32 namespace
    // selector) + get_entry. lo32 namespace values vary by table —
    // documented in vendor/crimson-rs/docs/save-editor-keys-plan.md.

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int MissionInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_load_from_bytes")]
    public static unsafe partial int MissionInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_free")]
    public static partial void MissionInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_entry_count")]
    public static partial int MissionInfoEntryCount(CrimsonMissionInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_lookup_string_key")]
    public static unsafe partial int MissionInfoLookupStringKey(
        CrimsonMissionInfoHandle handle, uint missionKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_lookup_display_name")]
    public static unsafe partial int MissionInfoLookupDisplayName(
        CrimsonMissionInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint missionKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_missioninfo_get_entry")]
    public static unsafe partial int MissionInfoGetEntry(
        CrimsonMissionInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── QuestInfo bridge (questinfo.pabgb) ──────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int QuestInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_load_from_bytes")]
    public static unsafe partial int QuestInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_free")]
    public static partial void QuestInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_entry_count")]
    public static partial int QuestInfoEntryCount(CrimsonQuestInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_lookup_string_key")]
    public static unsafe partial int QuestInfoLookupStringKey(
        CrimsonQuestInfoHandle handle, uint questKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_lookup_display_name")]
    public static unsafe partial int QuestInfoLookupDisplayName(
        CrimsonQuestInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint questKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questinfo_get_entry")]
    public static unsafe partial int QuestInfoGetEntry(
        CrimsonQuestInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── StageInfo bridge (stageinfo.pabgb) ──────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StageInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_load_from_bytes")]
    public static unsafe partial int StageInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_free")]
    public static partial void StageInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_entry_count")]
    public static partial int StageInfoEntryCount(CrimsonStageInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_lookup_string_key")]
    public static unsafe partial int StageInfoLookupStringKey(
        CrimsonStageInfoHandle handle, uint stageKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_lookup_display_name")]
    public static unsafe partial int StageInfoLookupDisplayName(
        CrimsonStageInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint stageKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_stageinfo_get_entry")]
    public static unsafe partial int StageInfoGetEntry(
        CrimsonStageInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── KnowledgeInfo bridge (knowledgeinfo.pabgb) ──────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int KnowledgeInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_load_from_bytes")]
    public static unsafe partial int KnowledgeInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_free")]
    public static partial void KnowledgeInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_entry_count")]
    public static partial int KnowledgeInfoEntryCount(CrimsonKnowledgeInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_lookup_string_key")]
    public static unsafe partial int KnowledgeInfoLookupStringKey(
        CrimsonKnowledgeInfoHandle handle, uint knowledgeKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_lookup_display_name")]
    public static unsafe partial int KnowledgeInfoLookupDisplayName(
        CrimsonKnowledgeInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint knowledgeKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_knowledgeinfo_get_entry")]
    public static unsafe partial int KnowledgeInfoGetEntry(
        CrimsonKnowledgeInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── QuestGaugeInfo bridge (questgaugeinfo.pabgb) ────────────────────────
    //
    // Pattern A only — gauges aren't in PALOC, so there's no
    // lookup_display_name. The bridge returns the internal name as the
    // user-facing label.

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int QuestGaugeInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_load_from_bytes")]
    public static unsafe partial int QuestGaugeInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_free")]
    public static partial void QuestGaugeInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_entry_count")]
    public static partial int QuestGaugeInfoEntryCount(CrimsonQuestGaugeInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_lookup_string_key")]
    public static unsafe partial int QuestGaugeInfoLookupStringKey(
        CrimsonQuestGaugeInfoHandle handle, uint gaugeKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_questgaugeinfo_get_entry")]
    public static unsafe partial int QuestGaugeInfoGetEntry(
        CrimsonQuestGaugeInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── SkillInfo bridge (skill.pabgb + skill.pabgh — two files) ────────────
    //
    // Pattern A only. The internal-name fallback IS the user-facing label
    // (skills don't sit at a PALOC byte that resolves through the hash
    // hop, at least not yet). Loader takes both pabgh + pabgb halves; the
    // editor extracts them via two PAZ calls.

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SkillInfoLoadFromFile(string pabghPath, string pabgbPath, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_load_from_bytes")]
    public static unsafe partial int SkillInfoLoadFromBytes(
        byte* pabghData, nuint pabghLen,
        byte* pabgbData, nuint pabgbLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_free")]
    public static partial void SkillInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_entry_count")]
    public static partial int SkillInfoEntryCount(CrimsonSkillInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_lookup_string_key")]
    public static unsafe partial int SkillInfoLookupStringKey(
        CrimsonSkillInfoHandle handle, uint skillKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_skillinfo_get_entry")]
    public static unsafe partial int SkillInfoGetEntry(
        CrimsonSkillInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── StoreInfo bridge (storeinfo.pabgb + storeinfo.pabgh — two files) ────
    //
    // Resolves save-side StoreKey (u16-widened-u32) → row internal name
    // ("Store_Her_General", "Store_BlackMarket", …). 292 rows in 1.07.
    // Name-only — no PALOC chain (no LookupDisplayName entry point).
    // Same load/free/entry_count/lookup_string_key/get_entry shape as
    // QuestGaugeInfo / SubLevelInfo, but two-file like Skill because the
    // .pabgh index is required to find each row in the .pabgb body.

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StoreInfoLoadFromFile(
        string pabgbPath, string pabghPath, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_load_from_bytes")]
    public static unsafe partial int StoreInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_free")]
    public static partial void StoreInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_entry_count")]
    public static partial int StoreInfoEntryCount(CrimsonStoreInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_lookup_string_key")]
    public static unsafe partial int StoreInfoLookupStringKey(
        CrimsonStoreInfoHandle handle, uint storeKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_store_info_get_entry")]
    public static unsafe partial int StoreInfoGetEntry(
        CrimsonStoreInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── 13 name-only bridges generated by impl_name_only_bridge! ────────────
    //
    // Each block exposes the same 5-call surface as StoreInfo above
    // (load_from_bytes / free / entry_count / lookup_string_key /
    // get_entry — load_from_file omitted, see hard-constraint note). All
    // u16/u32 keys are widened to u32 on the Rust side; pass the raw save
    // value through. None ship a PALOC chain — internal name is the
    // user-facing label.

    // ── HouseInfo bridge (houseinfo.pabgb + houseinfo.pabgh) ────────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_house_info_load_from_bytes")]
    public static unsafe partial int HouseInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_house_info_free")]
    public static partial void HouseInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_house_info_entry_count")]
    public static partial int HouseInfoEntryCount(CrimsonHouseInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_house_info_lookup_string_key")]
    public static unsafe partial int HouseInfoLookupStringKey(
        CrimsonHouseInfoHandle handle, uint houseKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_house_info_get_entry")]
    public static unsafe partial int HouseInfoGetEntry(
        CrimsonHouseInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── RoyalSupplyInfo bridge (royalsupply.pabgb + royalsupply.pabgh) ──────

    [LibraryImport(LibraryName, EntryPoint = "crimson_royal_supply_info_load_from_bytes")]
    public static unsafe partial int RoyalSupplyInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_royal_supply_info_free")]
    public static partial void RoyalSupplyInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_royal_supply_info_entry_count")]
    public static partial int RoyalSupplyInfoEntryCount(CrimsonRoyalSupplyInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_royal_supply_info_lookup_string_key")]
    public static unsafe partial int RoyalSupplyInfoLookupStringKey(
        CrimsonRoyalSupplyInfoHandle handle, uint royalSupplyKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_royal_supply_info_get_entry")]
    public static unsafe partial int RoyalSupplyInfoGetEntry(
        CrimsonRoyalSupplyInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── CraftToolInfo bridge (crafttoolinfo.pabgb + crafttoolinfo.pabgh) ────

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_info_load_from_bytes")]
    public static unsafe partial int CraftToolInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_info_free")]
    public static partial void CraftToolInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_info_entry_count")]
    public static partial int CraftToolInfoEntryCount(CrimsonCraftToolInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_info_lookup_string_key")]
    public static unsafe partial int CraftToolInfoLookupStringKey(
        CrimsonCraftToolInfoHandle handle, uint craftToolKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_info_get_entry")]
    public static unsafe partial int CraftToolInfoGetEntry(
        CrimsonCraftToolInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── CraftToolGroupInfo bridge (crafttoolgroupinfo.pabgb + .pabgh) ───────

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_group_info_load_from_bytes")]
    public static unsafe partial int CraftToolGroupInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_group_info_free")]
    public static partial void CraftToolGroupInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_group_info_entry_count")]
    public static partial int CraftToolGroupInfoEntryCount(CrimsonCraftToolGroupInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_group_info_lookup_string_key")]
    public static unsafe partial int CraftToolGroupInfoLookupStringKey(
        CrimsonCraftToolGroupInfoHandle handle, uint craftToolGroupKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_craft_tool_group_info_get_entry")]
    public static unsafe partial int CraftToolGroupInfoGetEntry(
        CrimsonCraftToolGroupInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── TriggerRegionInfo bridge (triggerregioninfo.pabgb + .pabgh) ─────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_trigger_region_info_load_from_bytes")]
    public static unsafe partial int TriggerRegionInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_trigger_region_info_free")]
    public static partial void TriggerRegionInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_trigger_region_info_entry_count")]
    public static partial int TriggerRegionInfoEntryCount(CrimsonTriggerRegionInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_trigger_region_info_lookup_string_key")]
    public static unsafe partial int TriggerRegionInfoLookupStringKey(
        CrimsonTriggerRegionInfoHandle handle, uint triggerRegionKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_trigger_region_info_get_entry")]
    public static unsafe partial int TriggerRegionInfoGetEntry(
        CrimsonTriggerRegionInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── GamePlayVariableInfo bridge (gameplayvariableinfo.pabgb + .pabgh) ───

    [LibraryImport(LibraryName, EntryPoint = "crimson_gameplay_variable_info_load_from_bytes")]
    public static unsafe partial int GamePlayVariableInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gameplay_variable_info_free")]
    public static partial void GamePlayVariableInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gameplay_variable_info_entry_count")]
    public static partial int GamePlayVariableInfoEntryCount(CrimsonGamePlayVariableInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gameplay_variable_info_lookup_string_key")]
    public static unsafe partial int GamePlayVariableInfoLookupStringKey(
        CrimsonGamePlayVariableInfoHandle handle, uint gamePlayVariableKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gameplay_variable_info_get_entry")]
    public static unsafe partial int GamePlayVariableInfoGetEntry(
        CrimsonGamePlayVariableInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── GlobalGameEventInfo bridge (globalgameevent.pabgb + .pabgh) ─────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_info_load_from_bytes")]
    public static unsafe partial int GlobalGameEventInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_info_free")]
    public static partial void GlobalGameEventInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_info_entry_count")]
    public static partial int GlobalGameEventInfoEntryCount(CrimsonGlobalGameEventInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_info_lookup_string_key")]
    public static unsafe partial int GlobalGameEventInfoLookupStringKey(
        CrimsonGlobalGameEventInfoHandle handle, uint globalGameEventInfoKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_info_get_entry")]
    public static unsafe partial int GlobalGameEventInfoGetEntry(
        CrimsonGlobalGameEventInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── GlobalGameEventGroupInfo bridge (globalgameeventgroup.pabgb + .pabgh) ──

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_group_info_load_from_bytes")]
    public static unsafe partial int GlobalGameEventGroupInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_group_info_free")]
    public static partial void GlobalGameEventGroupInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_group_info_entry_count")]
    public static partial int GlobalGameEventGroupInfoEntryCount(CrimsonGlobalGameEventGroupInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_group_info_lookup_string_key")]
    public static unsafe partial int GlobalGameEventGroupInfoLookupStringKey(
        CrimsonGlobalGameEventGroupInfoHandle handle, uint globalGameEventGroupKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_global_game_event_group_info_get_entry")]
    public static unsafe partial int GlobalGameEventGroupInfoGetEntry(
        CrimsonGlobalGameEventGroupInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── GameAdviceInfo bridge (gameadviceinfo.pabgb + .pabgh) ───────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_info_load_from_bytes")]
    public static unsafe partial int GameAdviceInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_info_free")]
    public static partial void GameAdviceInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_info_entry_count")]
    public static partial int GameAdviceInfoEntryCount(CrimsonGameAdviceInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_info_lookup_string_key")]
    public static unsafe partial int GameAdviceInfoLookupStringKey(
        CrimsonGameAdviceInfoHandle handle, uint gameAdviceInfoKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_info_get_entry")]
    public static unsafe partial int GameAdviceInfoGetEntry(
        CrimsonGameAdviceInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── GameAdviceGroupInfo bridge (gameadvicegroupinfo.pabgb + .pabgh) ─────

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_group_info_load_from_bytes")]
    public static unsafe partial int GameAdviceGroupInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_group_info_free")]
    public static partial void GameAdviceGroupInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_group_info_entry_count")]
    public static partial int GameAdviceGroupInfoEntryCount(CrimsonGameAdviceGroupInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_group_info_lookup_string_key")]
    public static unsafe partial int GameAdviceGroupInfoLookupStringKey(
        CrimsonGameAdviceGroupInfoHandle handle, uint gameAdviceGroupKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_game_advice_group_info_get_entry")]
    public static unsafe partial int GameAdviceGroupInfoGetEntry(
        CrimsonGameAdviceGroupInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── ReserveSlotInfo bridge (reserveslot.pabgb + reserveslot.pabgh) ──────

    [LibraryImport(LibraryName, EntryPoint = "crimson_reserve_slot_info_load_from_bytes")]
    public static unsafe partial int ReserveSlotInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_reserve_slot_info_free")]
    public static partial void ReserveSlotInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_reserve_slot_info_entry_count")]
    public static partial int ReserveSlotInfoEntryCount(CrimsonReserveSlotInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_reserve_slot_info_lookup_string_key")]
    public static unsafe partial int ReserveSlotInfoLookupStringKey(
        CrimsonReserveSlotInfoHandle handle, uint reserveSlotKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_reserve_slot_info_get_entry")]
    public static unsafe partial int ReserveSlotInfoGetEntry(
        CrimsonReserveSlotInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── RegionInfo bridge (regioninfo.pabgb + regioninfo.pabgh) ─────────────

    [LibraryImport(LibraryName, EntryPoint = "crimson_region_info_load_from_bytes")]
    public static unsafe partial int RegionInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_region_info_free")]
    public static partial void RegionInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_region_info_entry_count")]
    public static partial int RegionInfoEntryCount(CrimsonRegionInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_region_info_lookup_string_key")]
    public static unsafe partial int RegionInfoLookupStringKey(
        CrimsonRegionInfoHandle handle, uint regionKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_region_info_get_entry")]
    public static unsafe partial int RegionInfoGetEntry(
        CrimsonRegionInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── ItemGroupInfo bridge (itemgroupinfo.pabgb + itemgroupinfo.pabgh) ────

    [LibraryImport(LibraryName, EntryPoint = "crimson_item_group_info_load_from_bytes")]
    public static unsafe partial int ItemGroupInfoLoadFromBytes(
        byte* pabgbData, nuint pabgbLen,
        byte* pabghData, nuint pabghLen,
        out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_item_group_info_free")]
    public static partial void ItemGroupInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_item_group_info_entry_count")]
    public static partial int ItemGroupInfoEntryCount(CrimsonItemGroupInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_item_group_info_lookup_string_key")]
    public static unsafe partial int ItemGroupInfoLookupStringKey(
        CrimsonItemGroupInfoHandle handle, uint itemGroupKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_item_group_info_get_entry")]
    public static unsafe partial int ItemGroupInfoGetEntry(
        CrimsonItemGroupInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── Checksum helper (Jenkins hashlittle2_c) ─────────────────────────────
    //
    // Exposed for callers that need to compose the hash hop themselves
    // (e.g. if a future Key type's display chain isn't covered by the
    // lookup_display_name family above). The five lookup_display_name
    // entry points already use this internally, so the editor typically
    // doesn't need to call it directly.

    [LibraryImport(LibraryName, EntryPoint = "crimson_calculate_checksum")]
    public static unsafe partial int CalculateChecksum(byte* data, nuint dataLen, out uint outHash);

    // ── GimmickInfo bridge (gimmickinfo.pabgb) ──────────────────────────────
    //
    // Mirrors mission/quest/stage/knowledge: load + free + entry_count +
    // lookup_string_key + lookup_display_name (hash hop at lo32=0x200) +
    // get_entry. Co-resident with the existing PALOC-byte-0x00 path for
    // GimmickInfoKey / LevelGimmickSceneObjectInfoKey — the editor prefers
    // the bridge (canonical hash hop) and falls back to PALOC 0x00 when
    // the bridge doesn't cover the value.

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GimmickInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_load_from_bytes")]
    public static unsafe partial int GimmickInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_free")]
    public static partial void GimmickInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_entry_count")]
    public static partial int GimmickInfoEntryCount(CrimsonGimmickInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_lookup_string_key")]
    public static unsafe partial int GimmickInfoLookupStringKey(
        CrimsonGimmickInfoHandle handle, uint gimmickKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_lookup_display_name")]
    public static unsafe partial int GimmickInfoLookupDisplayName(
        CrimsonGimmickInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint gimmickKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_gimmickinfo_get_entry")]
    public static unsafe partial int GimmickInfoGetEntry(
        CrimsonGimmickInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── CharacterInfo bridge (characterinfo.pabgb) ──────────────────────────
    //
    // Resolves save-side CharacterKey (u32) via three layers: internal
    // name from the characterinfo entry, PALOC display name at
    // `((charkey & 0x00FF_FFFF) << 32) | lo32` (default lo32 = 0x30 —
    // 24-bit row key with cat-byte hi-byte STRIPPED, no Jenkins hash
    // hop unlike Mission / Quest / Stage / Knowledge), and a high-level
    // resolve_portrait matcher that chains the display name against
    // the NPC portrait DDS list emitted by
    // crimson_paz_list_npc_portraits. The bridge supersedes the
    // generic PALOC byte-0x30 path for CharacterKey because it does
    // the cat-byte strip we don't, and falls back to the internal
    // name when PALOC misses.

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int CharacterInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_load_from_bytes")]
    public static unsafe partial int CharacterInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_free")]
    public static partial void CharacterInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_entry_count")]
    public static partial int CharacterInfoEntryCount(CrimsonCharacterInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_lookup_string_key")]
    public static unsafe partial int CharacterInfoLookupStringKey(
        CrimsonCharacterInfoHandle handle, uint characterKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_lookup_display_name")]
    public static unsafe partial int CharacterInfoLookupDisplayName(
        CrimsonCharacterInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint characterKey, uint lo32Namespace,
        byte* buf, nuint bufLen, out nuint required);

    // Two-call enumerate over the loaded characterinfo entries by
    // insertion index. The lo24 row key + internal name pair drives
    // the Browse Characters dialog.
    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_get_entry")]
    public static unsafe partial int CharacterInfoGetEntry(
        CrimsonCharacterInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // High-level CharacterKey → portrait DDS path matcher. Chains the
    // display name + internal name against the portrait list returned
    // by PazListNpcPortraits, scores each candidate (0–100), and
    // returns the best match. out_score is informational; callers can
    // apply their own threshold (~30 = noise floor, ~50 = suggestive,
    // higher = exact normalised match).
    [LibraryImport(LibraryName, EntryPoint = "crimson_characterinfo_resolve_portrait")]
    public static unsafe partial int CharacterInfoResolvePortrait(
        CrimsonCharacterInfoHandle handle,
        CrimsonPalocHandle palocHandle,
        uint characterKey,
        byte* portraitListBuf, nuint portraitListLen,
        byte* outBuf, nuint outBufLen,
        out nuint outRequired,
        out int outScore);

    // ── SubLevelInfo bridge (sublevelinfo.pabgb) ────────────────────────────
    //
    // Pattern A only — no lookup_display_name exposed yet. SubLevelKey
    // wasn't in any catalog before this bridge; internal-name resolution
    // alone closes the 7-distinct-key handoff worklist for slot0.

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_load_from_file",
                   StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SubLevelInfoLoadFromFile(string path, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_load_from_bytes")]
    public static unsafe partial int SubLevelInfoLoadFromBytes(byte* data, nuint dataLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_free")]
    public static partial void SubLevelInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_entry_count")]
    public static partial int SubLevelInfoEntryCount(CrimsonSubLevelInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_lookup_string_key")]
    public static unsafe partial int SubLevelInfoLookupStringKey(
        CrimsonSubLevelInfoHandle handle, uint subLevelKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_sublevelinfo_get_entry")]
    public static unsafe partial int SubLevelInfoGetEntry(
        CrimsonSubLevelInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // ── DyeColorGroupInfo bridge (dyecolorgroupinfo.pabgb + .pabgh) ─────────
    //
    // Resolves DyeColorGroupInfoKey (u32) → named color group
    // ("Her_Color_Group_I", "Dem_Color_Group_I/II/III", …). 10 rows
    // in 1.07. Drives the Dye editor's color-group dropdown.

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_load_from_bytes")]
    public static unsafe partial int DyeColorGroupInfoLoadFromBytes(
        byte* pabgb, nuint pabgbLen, byte* pabgh, nuint pabghLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_free")]
    public static partial void DyeColorGroupInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_entry_count")]
    public static partial int DyeColorGroupInfoEntryCount(
        CrimsonDyeColorGroupInfoHandle handle, out uint count);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_lookup_name")]
    public static unsafe partial int DyeColorGroupInfoLookupName(
        CrimsonDyeColorGroupInfoHandle handle, uint colorGroupKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_get_entry")]
    public static unsafe partial int DyeColorGroupInfoGetEntry(
        CrimsonDyeColorGroupInfoHandle handle, uint idx, out uint outKey,
        byte* buf, nuint bufLen, out nuint required);

    // Palette accessors — the dye picker is a 109-position grid per
    // theme (9 grayscale + 10×10 chromatic), NOT freeform RGB. The
    // PyQt5 reference editor's free-form RGB sliders were wrong; the
    // engine constrains visible colors to the palette positions. Per
    // vendor/crimson-rs/docs/dye-editor-scope.md §"Recommended C#
    // editor UX".

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_palette_size")]
    public static partial int DyeColorGroupInfoPaletteSize(
        CrimsonDyeColorGroupInfoHandle handle, uint colorGroupKey, out uint outCount);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_palette_at")]
    public static partial int DyeColorGroupInfoPaletteAt(
        CrimsonDyeColorGroupInfoHandle handle, uint colorGroupKey, uint positionIdx,
        out byte outR, out byte outG, out byte outB, out byte outA);

    [LibraryImport(LibraryName, EntryPoint = "crimson_dye_color_group_info_position_for_rgb")]
    public static partial int DyeColorGroupInfoPositionForRgb(
        CrimsonDyeColorGroupInfoHandle handle, uint colorGroupKey,
        byte r, byte g, byte b, out uint outPosition);

    // ── PartPrefabDyeTexturePalleteInfo bridge ──────────────────────────────
    //
    // Resolves PartPrefabDyeTexturePalleteKey (u16, widened to u32 in
    // the bridge) → material tier with 2–3 sub-records each carrying
    // material name ("cloth"/"leather"/"metal"/…) + icon DDS path +
    // texture DDS path + optional variant. 11 rows (keys 0..10) in
    // 1.07. Drives the Dye editor's material dropdown.

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_load_from_bytes")]
    public static unsafe partial int PartPrefabDyeTexturePalleteLoadFromBytes(
        byte* pabgb, nuint pabgbLen, byte* pabgh, nuint pabghLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_part_prefab_dye_texture_pallete_free")]
    public static partial void PartPrefabDyeTexturePalleteFree(IntPtr handle);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_entry_count")]
    public static partial int PartPrefabDyeTexturePalleteEntryCount(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, out uint count);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_count")]
    public static partial int PartPrefabDyeTexturePalleteLookupSubCount(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, out uint count);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_material_name")]
    public static unsafe partial int PartPrefabDyeTexturePalleteLookupSubMaterialName(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, uint subIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_icon_path")]
    public static unsafe partial int PartPrefabDyeTexturePalleteLookupSubIconPath(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, uint subIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_texture_path")]
    public static unsafe partial int PartPrefabDyeTexturePalleteLookupSubTexturePath(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, uint subIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_variant_name")]
    public static unsafe partial int PartPrefabDyeTexturePalleteLookupSubVariantName(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, uint subIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_lookup_sub_variant_value")]
    public static partial int PartPrefabDyeTexturePalleteLookupSubVariantValue(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint paletteKey, uint subIdx,
        out float value);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_texture_pallete_get_entry_key")]
    public static partial int PartPrefabDyeTexturePalleteGetEntryKey(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, uint idx, out uint outKey);

    // ── PartPrefabDyeSlotInfo bridge ────────────────────────────────────────
    //
    // PartPrefabKey (u32) → per-prefab slot count + per-slot default
    // material / mask / tail-name detail. 1,105 rows in 1.07.
    // Replaces the PyQt5 editor's dye_slot_counts.json — once the
    // _itemKey → _partPrefabKey cross-reference lands. Bound here for
    // future use; v1 Dye editor doesn't consume this bridge yet.

    [LibraryImport(LibraryName, EntryPoint = "crimson_part_prefab_dye_slot_info_load_from_bytes")]
    public static unsafe partial int PartPrefabDyeSlotInfoLoadFromBytes(
        byte* pabgb, nuint pabgbLen, byte* pabgh, nuint pabghLen, out IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_part_prefab_dye_slot_info_free")]
    public static partial void PartPrefabDyeSlotInfoFree(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "crimson_part_prefab_dye_slot_info_entry_count")]
    public static partial int PartPrefabDyeSlotInfoEntryCount(
        CrimsonPartPrefabDyeSlotInfoHandle handle, out uint count);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_slot_count")]
    public static partial int PartPrefabDyeSlotInfoLookupSlotCount(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey, out uint count);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_prefab_name")]
    public static unsafe partial int PartPrefabDyeSlotInfoLookupPrefabName(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_slot_default_material")]
    public static unsafe partial int PartPrefabDyeSlotInfoLookupSlotDefaultMaterial(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey, uint slotIdx, uint matIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_slot_tail_name")]
    public static unsafe partial int PartPrefabDyeSlotInfoLookupSlotTailName(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey, uint slotIdx,
        byte* buf, nuint bufLen, out nuint required);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_slot_mat_indices")]
    public static unsafe partial int PartPrefabDyeSlotInfoLookupSlotMatIndices(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey, uint slotIdx,
        byte* outIndices);

    [LibraryImport(LibraryName,
        EntryPoint = "crimson_part_prefab_dye_slot_info_lookup_slot_mask")]
    public static unsafe partial int PartPrefabDyeSlotInfoLookupSlotMask(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint prefabKey, uint slotIdx,
        byte* outMask);

    [LibraryImport(LibraryName, EntryPoint = "crimson_part_prefab_dye_slot_info_get_entry_key")]
    public static partial int PartPrefabDyeSlotInfoGetEntryKey(
        CrimsonPartPrefabDyeSlotInfoHandle handle, uint idx, out uint outKey);
}
