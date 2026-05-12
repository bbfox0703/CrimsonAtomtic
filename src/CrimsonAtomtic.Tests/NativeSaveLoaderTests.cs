using CrimsonAtomtic.RustInterop;
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
}
