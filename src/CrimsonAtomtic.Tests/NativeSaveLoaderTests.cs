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
