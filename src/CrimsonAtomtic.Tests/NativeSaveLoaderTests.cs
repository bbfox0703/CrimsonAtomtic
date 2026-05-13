using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end smoke tests for <see cref="NativeSaveLoader"/>. Drives the
/// real crimson-rs C ABI against a live save under
/// <c>%LOCALAPPDATA%\Pearl Abyss\CD\save\…\slot0\save.save</c>. Tests
/// skip cleanly when no save is present so CI / fresh machines don't
/// fail — matching the Rust side's <c>test_save_*</c> tests.
/// </summary>
public sealed class NativeSaveLoaderTests
{
    private static string? FindLiveSave()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local))
        {
            return null;
        }
        var root = Path.Combine(local, "Pearl Abyss", "CD", "save");
        if (!Directory.Exists(root))
        {
            return null;
        }
        foreach (var user in Directory.EnumerateDirectories(root))
        {
            foreach (var slot in new[] { "slot0", "slot1", "slot2" })
            {
                var p = Path.Combine(user, slot, "save.save");
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }
        return null;
    }

    [Fact]
    public void Load_LiveSave_ReturnsConsistentSummary()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }
        var loader = new NativeSaveLoader();

        var summary = loader.Load(path);

        // Header invariants — same checks the Rust test_save_parse runs.
        Assert.Equal(2, summary.Version);
        Assert.True(summary.HmacOk);
        Assert.True(summary.UncompressedSize > 0);
        Assert.True(summary.PayloadSize > 0);

        // Body invariants.
        Assert.True(summary.SchemaTypeCount > 0);
        Assert.True(summary.TocEntryCount > 0);
        Assert.Equal(summary.TocEntryCount, summary.Blocks.Count);

        // Per-block: class names non-empty; every present field decoded.
        Assert.All(summary.Blocks, b =>
        {
            Assert.False(string.IsNullOrEmpty(b.ClassName));
            Assert.Equal(b.FieldsPresent, b.FieldsDecoded);
        });

        // Slot name from path parent.
        Assert.False(string.IsNullOrEmpty(summary.SlotName));
    }

    [Fact]
    public void Load_NonexistentPath_ThrowsCrimsonSaveException()
    {
        // Skip when the dll isn't present (e.g. fresh checkout before
        // build_rust.ps1 has been run). Calling LibraryImport against a
        // missing dll throws DllNotFoundException, which is its own
        // failure mode unrelated to what this test asserts.
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var loader = new NativeSaveLoader();

        var ex = Assert.Throws<CrimsonSaveException>(() =>
            loader.Load(@"C:\definitely\does\not\exist\save.save"));

        // -3 == IO. Confirms the error code is propagated, not just any
        // generic Exception.
        Assert.Equal(-3, ex.ErrorCode);
    }

    [Fact]
    public void Load_RejectsEmptyPath()
    {
        var loader = new NativeSaveLoader();
        Assert.Throws<ArgumentException>(() => loader.Load(""));
    }

    [Fact]
    public void Load_RespectsCancellation()
    {
        var loader = new NativeSaveLoader();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            loader.Load(@"C:\fake\slot0\save.save", cts.Token));
    }

    [Fact]
    public void LoadBlockDetails_LiveSave_ReturnsFieldsMatchingSummary()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }
        var loader = new NativeSaveLoader();

        // Pull the summary first so we know the index range.
        var summary = loader.Load(path);
        Assert.NotEmpty(summary.Blocks);

        // Block 0 always exists in any non-empty save. Spot-check the
        // mid-stream block too if available, since field shapes differ
        // dramatically across classes.
        foreach (var idx in new[] { 0, Math.Min(50, summary.Blocks.Count - 1) })
        {
            var details = loader.LoadBlockDetails(path, idx);
            var summaryRow = summary.Blocks[idx];

            Assert.Equal(summaryRow.ClassIndex, details.ClassIndex);
            Assert.Equal(summaryRow.DataOffset, details.DataOffset);
            Assert.Equal(summaryRow.DataSize, details.DataSize);

            // mask_bytes_hex must be lowercase hex.
            Assert.Matches("^[0-9a-f]*$", details.MaskBytesHex);

            // Field count from the JSON must match the schema's field
            // count for this class. We don't have schema visibility from
            // here, but the summary tracks present + decoded which must
            // hold inside the field list too.
            var present = details.Fields.Count(f => f.Present);
            var decoded = details.Fields.Count(f => f.Present && f.Kind != "absent" && f.Kind != "unknown");
            Assert.Equal(summaryRow.FieldsPresent, present);
            Assert.Equal(summaryRow.FieldsDecoded, decoded);

            // Every field row carries a non-null name and kind.
            Assert.All(details.Fields, f =>
            {
                Assert.False(string.IsNullOrEmpty(f.Name));
                Assert.False(string.IsNullOrEmpty(f.Kind));
            });
        }
    }

    [Fact]
    public void LoadBlockDetails_OutOfRange_ThrowsCrimsonSaveException()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }
        var loader = new NativeSaveLoader();

        var ex = Assert.Throws<CrimsonSaveException>(() =>
            loader.LoadBlockDetails(path, int.MaxValue));

        // -10 == OUT_OF_RANGE.
        Assert.Equal(-10, ex.ErrorCode);
    }

    [Fact]
    public void LoadBlockDetails_AfterLoad_ReusesCachedHandle()
    {
        // We can't directly observe whether the cached handle was used,
        // but we can prove the path stays functional across many calls
        // and gives identical output to the slow path (a fresh loader).
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var cached = new NativeSaveLoader();
        cached.Load(path);                              // primes cache
        var cachedDetails = cached.LoadBlockDetails(path, 0);

        using var fresh = new NativeSaveLoader();
        var freshDetails = fresh.LoadBlockDetails(path, 0);   // slow path

        Assert.Equal(freshDetails.ClassIndex,     cachedDetails.ClassIndex);
        Assert.Equal(freshDetails.DataOffset,     cachedDetails.DataOffset);
        Assert.Equal(freshDetails.DataSize,       cachedDetails.DataSize);
        Assert.Equal(freshDetails.MaskBytesHex,   cachedDetails.MaskBytesHex);
        Assert.Equal(freshDetails.Fields.Count,   cachedDetails.Fields.Count);
    }

    [Fact]
    public void Dispose_AfterLoad_FreesCacheAndFallsBackToSlowPath()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        var loader = new NativeSaveLoader();
        loader.Load(path);
        loader.Dispose();

        // After Dispose the cache is empty, but LoadBlockDetails should
        // still succeed via the slow path (transient open).
        var details = loader.LoadBlockDetails(path, 0);
        Assert.NotEmpty(details.Fields);

        // Repeated Dispose is a no-op.
        loader.Dispose();
    }

    [Fact]
    public void SetScalarField_LiveSave_RoundTripsThroughWriteToFile()
    {
        // End-to-end: load → mutate block 0 field 0 → write to a temp
        // file → reload the temp file → confirm the mutation persisted
        // across encrypt + LZ4 + HMAC. Mirrors the Rust-side smoke test
        // (c_abi_mutate_and_write_roundtrip).
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        loader.Load(path);

        // Block 0 field 0 is _characterKey (fixed_suffix u32). Sanity-
        // check the layout before mutating, so a schema drift surfaces
        // here rather than silently corrupting bytes.
        var before = loader.LoadBlockDetails(path, 0);
        Assert.Equal("fixed_suffix", before.Fields[0].Kind);
        Assert.Equal(4, before.Fields[0].End - before.Fields[0].Start);

        // 0x01EFCDAB = 32_492_971; distinct from any plausible original.
        ReadOnlySpan<byte> sentinel = [0xAB, 0xCD, 0xEF, 0x01];
        loader.SetScalarField(0, 0, sentinel);

        var after = loader.LoadBlockDetails(path, 0);
        Assert.Equal("32492971 <u32>", after.Fields[0].Value);

        // Write to a temp file, reload, confirm.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"crimsonatomtic_test_{Guid.NewGuid():N}.save");
        try
        {
            loader.WriteToFile(tempPath);
            using var fresh = new NativeSaveLoader();
            var freshSummary = fresh.Load(tempPath);
            Assert.True(freshSummary.HmacOk, "reloaded save must verify HMAC");

            var reloaded = fresh.LoadBlockDetails(tempPath, 0);
            Assert.Equal("32492971 <u32>", reloaded.Fields[0].Value);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SetScalarField_BeforeLoad_ThrowsInvalidOperation()
    {
        // No file IO; the loader is empty.
        using var loader = new NativeSaveLoader();
        ReadOnlySpan<byte> bytes = [0, 0, 0, 0];
        var bytesArr = bytes.ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            loader.SetScalarField(0, 0, bytesArr));
    }

    [Fact]
    public void WriteToFile_BeforeLoad_ThrowsInvalidOperation()
    {
        using var loader = new NativeSaveLoader();
        Assert.Throws<InvalidOperationException>(() =>
            loader.WriteToFile(Path.Combine(Path.GetTempPath(), "x.save")));
    }

    [Fact]
    public void SetScalarField_NonScalarOrWrongLength_ThrowsTypedException()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }
        using var loader = new NativeSaveLoader();
        loader.Load(path);

        // NOT_SCALAR: block 0 field 3 (_experience) is absent / non-scalar.
        var notScalar = Assert.Throws<CrimsonSaveException>(() =>
            loader.SetScalarField(0, 3, ReadOnlySpan<byte>.Empty));
        Assert.Equal(-12, notScalar.ErrorCode);

        // LENGTH_MISMATCH: field 0 is 4 bytes; pass 5.
        ReadOnlySpan<byte> tooLong = [0, 0, 0, 0, 0];
        var tooLongArr = tooLong.ToArray();
        var lenMismatch = Assert.Throws<CrimsonSaveException>(() =>
            loader.SetScalarField(0, 0, tooLongArr));
        Assert.Equal(-13, lenMismatch.ErrorCode);

        // OUT_OF_RANGE on field axis.
        ReadOnlySpan<byte> any = [0, 0, 0, 0];
        var anyArr = any.ToArray();
        var oor = Assert.Throws<CrimsonSaveException>(() =>
            loader.SetScalarField(0, int.MaxValue, anyArr));
        Assert.Equal(-10, oor.ErrorCode);
    }

    [Fact]
    public void SetScalarUInt32_TypedExtension_MatchesRawByteSet()
    {
        // The typed setter must produce the same byte order as the raw
        // byte path. Use SetScalarUInt32, reload, and confirm the formatted
        // value matches the one the byte-level test exercises.
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        loader.Load(path);

        loader.SetScalarUInt32(0, 0, 32_492_971u);   // 0x01EFCDAB

        var after = loader.LoadBlockDetails(path, 0);
        Assert.Equal("32492971 <u32>", after.Fields[0].Value);
    }

    [Fact]
    public void SetScalarField_NestedPath_RoundTripsThroughWriteToFile()
    {
        // End-to-end nested editing: find a live block whose decode
        // contains a one-step-reachable u32 scalar (via either a
        // Locator child or the first element of an ObjectList), mutate
        // it through the path API, write + reload, confirm the new
        // value sticks at the same path.
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        loader.Load(path);

        var target = FindNestedU32Target(loader, path);
        Assert.True(target is not null,
            "expected a one-step-reachable u32 scalar in a live save; schema or fixture drifted");
        var t = target!.Value;

        // Sentinel guaranteed to differ from the original value.
        var sentinel = t.OriginalValue + 0x0BADF00Du;
        PathStep[] steps = [t.Step];
        loader.SetScalarUInt32(t.BlockIndex, steps, t.LeafFieldIndex, sentinel);

        // Verify the in-memory mutation surfaces at the same nested path.
        var afterBlock = loader.LoadBlockDetails(path, t.BlockIndex);
        var afterValue = ReadNestedU32At(afterBlock, t.Step, t.LeafFieldIndex);
        Assert.Equal(sentinel, afterValue);

        // Roundtrip through the encrypted on-disk format.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"crimsonatomtic_nested_{Guid.NewGuid():N}.save");
        try
        {
            loader.WriteToFile(tempPath);
            using var fresh = new NativeSaveLoader();
            var reloadedSummary = fresh.Load(tempPath);
            Assert.True(reloadedSummary.HmacOk, "reloaded save must verify HMAC");
            var reloadedBlock = fresh.LoadBlockDetails(tempPath, t.BlockIndex);
            var reloadedValue = ReadNestedU32At(reloadedBlock, t.Step, t.LeafFieldIndex);
            Assert.Equal(sentinel, reloadedValue);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SetScalarField_PathStep_AtScalarMidpath_ThrowsNotNavigable()
    {
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }
        using var loader = new NativeSaveLoader();
        loader.Load(path);

        // Block 0 field 0 is the fixed_suffix u32 _characterKey — a scalar,
        // not navigable. Targeting it as a mid-path step must surface as
        // NOT_NAVIGABLE (-15), distinct from NOT_SCALAR which fires only
        // on the leaf.
        PathStep[] badSteps = [new PathStep(0, 0)];
        var bytes = new byte[] { 0, 0, 0, 0 };
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            loader.SetScalarField(0, badSteps, 0, bytes));
        Assert.Equal(-15, ex.ErrorCode);
    }

    [Fact]
    public void SetScalarFieldsBatch_LiveSave_RoundTripsThroughWriteToFile()
    {
        // Apply a mix of top-level + nested ops in one batch call,
        // verify each value reflected in the decoded JSON, write to
        // disk, reload, and confirm every sentinel survived HMAC /
        // ChaCha20 / LZ4 re-emission. Mirrors the Rust-side
        // c_abi_set_scalar_fields_batch_smoke test from the
        // crimson-rs PR.
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        loader.Load(path);

        // Op 1: top-level — block 0 field 0 _characterKey (u32).
        var before0 = loader.LoadBlockDetails(path, 0);
        Assert.Equal("fixed_suffix", before0.Fields[0].Kind);
        const uint topSentinel = 32_492_971u; // 0x01EFCDAB
        var topBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(topBytes, topSentinel);

        // Op 2: nested — first one-step-reachable u32 anywhere in the save.
        var nested = FindNestedU32Target(loader, path);
        Assert.True(nested is not null,
            "expected a one-step-reachable u32 scalar in a live save");
        var n = nested!.Value;
        var nestedSentinel = n.OriginalValue + 0x0BADF00Du;
        var nestedBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(nestedBytes, nestedSentinel);

        var ops = new List<ScalarBatchOp>
        {
            new(BlockIndex: 0,
                Path: [],
                FieldIndex: 0,
                Bytes: topBytes),
            new(BlockIndex: n.BlockIndex,
                Path: [n.Step],
                FieldIndex: n.LeafFieldIndex,
                Bytes: nestedBytes),
        };

        loader.SetScalarFieldsBatch(ops);

        // Both ops must be visible in the decoded JSON immediately.
        var afterTop = loader.LoadBlockDetails(path, 0);
        Assert.Equal("32492971 <u32>", afterTop.Fields[0].Value);
        var afterNested = loader.LoadBlockDetails(path, n.BlockIndex);
        Assert.Equal(nestedSentinel, ReadNestedU32At(afterNested, n.Step, n.LeafFieldIndex));

        // Round-trip via WriteToFile + reload — both sentinels must
        // survive HMAC / ChaCha20 / LZ4 re-emission.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"crimsonatomtic_batch_{Guid.NewGuid():N}.save");
        try
        {
            loader.WriteToFile(tempPath);
            using var fresh = new NativeSaveLoader();
            var summary = fresh.Load(tempPath);
            Assert.True(summary.HmacOk, "reloaded save must verify HMAC after batch");

            var freshTop = fresh.LoadBlockDetails(tempPath, 0);
            Assert.Equal("32492971 <u32>", freshTop.Fields[0].Value);

            var freshNested = fresh.LoadBlockDetails(tempPath, n.BlockIndex);
            Assert.Equal(nestedSentinel, ReadNestedU32At(freshNested, n.Step, n.LeafFieldIndex));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SetScalarFieldsBatch_ValidationFailure_LeavesSaveUntouched()
    {
        // All-or-nothing contract: if any op in the batch fails
        // validation, no mutation must be applied — even ops that
        // came BEFORE the failing one in input order. The thrown
        // exception's FailedOpIndex must pinpoint the offending op.
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        loader.Load(path);

        // Capture the original block 0 field 0 value so we can prove
        // it's unchanged after the failed batch.
        var before = loader.LoadBlockDetails(path, 0);
        var originalValue = before.Fields[0].Value;
        Assert.Equal("fixed_suffix", before.Fields[0].Kind);

        // Op 0 is valid (a u32 sentinel into block 0 field 0).
        // Op 1 targets the same field but with bytes_len = 3 — a
        // guaranteed LENGTH_MISMATCH (the field is u32, 4 bytes).
        var validBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(validBytes, 0xDEADBEEFu);
        var badBytes = new byte[] { 0, 0, 0 };
        var ops = new List<ScalarBatchOp>
        {
            new(0, [], 0, validBytes),
            new(0, [], 0, badBytes),
        };

        var ex = Assert.Throws<CrimsonSaveException>(() => loader.SetScalarFieldsBatch(ops));
        Assert.Equal(-13, ex.ErrorCode); // LENGTH_MISMATCH
        Assert.Equal(1, ex.FailedOpIndex);

        // The body must be untouched — op 0 must NOT have been
        // applied just because validation reached it first.
        var after = loader.LoadBlockDetails(path, 0);
        Assert.Equal(originalValue, after.Fields[0].Value);

        // NOT_NAVIGABLE on op 0 → also leaves the body untouched, and
        // FailedOpIndex points at op 0.
        PathStep[] badSteps = [new PathStep(0, 0)];
        var dummyBytes = new byte[] { 0, 0, 0, 0 };
        var ops2 = new List<ScalarBatchOp>
        {
            new(0, badSteps, 0, dummyBytes),
        };
        var ex2 = Assert.Throws<CrimsonSaveException>(() => loader.SetScalarFieldsBatch(ops2));
        Assert.Equal(-15, ex2.ErrorCode); // NOT_NAVIGABLE
        Assert.Equal(0, ex2.FailedOpIndex);

        var stillAfter = loader.LoadBlockDetails(path, 0);
        Assert.Equal(originalValue, stillAfter.Fields[0].Value);
    }

    [Fact]
    public void SetScalarFieldsBatch_EmptyOps_IsNoOpEvenWithoutSaveLoaded()
    {
        // Empty batch must short-circuit before the "no save loaded"
        // check — there's nothing to do, so there's nothing to fail.
        // Matches the Rust-side semantics (op_count == 0 returns OK
        // without touching the handle).
        using var loader = new NativeSaveLoader();
        loader.SetScalarFieldsBatch(Array.Empty<ScalarBatchOp>());
    }

    [Fact]
    public void SetScalarFieldsBatch_BeforeLoad_ThrowsInvalidOperation()
    {
        // A non-empty batch on an empty loader must surface the same
        // InvalidOperationException as the single-op setters.
        using var loader = new NativeSaveLoader();
        var ops = new List<ScalarBatchOp>
        {
            new(0, [], 0, new byte[] { 0, 0, 0, 0 }),
        };
        Assert.Throws<InvalidOperationException>(() => loader.SetScalarFieldsBatch(ops));
    }

    /// <summary>
    /// Find any (top-level block, one-step path, leaf field, original u32
    /// value) reachable through either a Locator's inline child or the
    /// first element of an ObjectList. Robust to schema drift: pick the
    /// first such target encountered.
    /// </summary>
    private static NestedU32Target? FindNestedU32Target(NativeSaveLoader loader, string path)
    {
        var summary = loader.Load(path);
        for (var b = 0; b < summary.Blocks.Count; b++)
        {
            var block = loader.LoadBlockDetails(path, b);
            for (var f = 0; f < block.Fields.Count; f++)
            {
                var parent = block.Fields[f];
                if (parent.Child is { } child)
                {
                    var leaf = FindU32Scalar(child);
                    if (leaf is not null)
                    {
                        return new NestedU32Target(
                            BlockIndex: b,
                            Step: new PathStep((uint)f, 0),
                            LeafFieldIndex: leaf.Value.FieldIndex,
                            OriginalValue: leaf.Value.Value);
                    }
                }
                if (parent.Elements is { Count: > 0 } els)
                {
                    var leaf = FindU32Scalar(els[0]);
                    if (leaf is not null)
                    {
                        return new NestedU32Target(
                            BlockIndex: b,
                            Step: new PathStep((uint)f, 0),
                            LeafFieldIndex: leaf.Value.FieldIndex,
                            OriginalValue: leaf.Value.Value);
                    }
                }
            }
        }
        return null;
    }

    private static (int FieldIndex, uint Value)? FindU32Scalar(BlockDetails block)
    {
        foreach (var row in block.Fields)
        {
            if (row.Kind is not ("fixed_prefix" or "fixed_suffix"))
            {
                continue;
            }
            if (!ScalarFieldEditing.TryParse(row.Value, out var raw, out var tag))
            {
                continue;
            }
            if (tag == "u32" && uint.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                return (row.FieldIndex, v);
            }
        }
        return null;
    }

    private static uint ReadNestedU32At(BlockDetails parent, PathStep step, int leafFieldIndex)
    {
        var parentField = parent.Fields[(int)step.FieldIndex];
        var nested = parentField.Child
                     ?? (parentField.Elements is { Count: > 0 } els
                         ? els[(int)step.ElementIndex]
                         : throw new InvalidOperationException("path step doesn't resolve"));
        var leaf = nested.Fields[leafFieldIndex];
        if (!ScalarFieldEditing.TryParse(leaf.Value, out var raw, out var tag))
        {
            throw new InvalidOperationException($"leaf value not parseable: {leaf.Value}");
        }
        if (tag != "u32")
        {
            throw new InvalidOperationException($"expected u32 leaf, got {tag}");
        }
        return uint.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
    }

    private readonly record struct NestedU32Target(
        int BlockIndex,
        PathStep Step,
        int LeafFieldIndex,
        uint OriginalValue);

    [Fact]
    public void LoadBlockDetails_NestedDataReachable()
    {
        // Find any block whose decode contains an object_list or
        // object_locator with inline children, then assert the JSON path
        // surfaces it via Child / Elements. Inventory- and equipment-shaped
        // blocks are the most likely candidates in a 1.06 save.
        var path = FindLiveSave();
        if (path is null)
        {
            return;
        }

        using var loader = new NativeSaveLoader();
        var summary = loader.Load(path);

        BlockDetails? withChild = null;
        BlockDetails? withElements = null;
        for (var i = 0; i < summary.Blocks.Count; i++)
        {
            var d = loader.LoadBlockDetails(path, i);
            foreach (var f in d.Fields)
            {
                if (withChild is null && f.Child is not null)
                {
                    withChild = d;
                }
                if (withElements is null && f.Elements is { Count: > 0 })
                {
                    withElements = d;
                }
            }
            if (withChild is not null && withElements is not null)
            {
                break;
            }
        }

        // At least one of the two must show up in a real save — Inventory
        // saves are full of ObjectLists, and Equipment uses Locators.
        Assert.True(withChild is not null || withElements is not null,
            "expected at least one block with nested data in a live save");

        if (withChild is not null)
        {
            var locator = withChild.Fields.First(f => f.Child is not null);
            Assert.Equal("object_locator", locator.Kind);
            Assert.NotNull(locator.Child);
            Assert.False(string.IsNullOrEmpty(locator.Child!.ClassName));
            Assert.NotEmpty(locator.Child.Fields);
        }
        if (withElements is not null)
        {
            var list = withElements.Fields.First(f => f.Elements is { Count: > 0 });
            Assert.Equal("object_list", list.Kind);
            Assert.NotNull(list.Elements);
            Assert.NotEmpty(list.Elements!);
            // Every element has a class name; every element has its own
            // (possibly empty) field list.
            Assert.All(list.Elements!, e =>
            {
                Assert.False(string.IsNullOrEmpty(e.ClassName));
            });
        }
    }
}
