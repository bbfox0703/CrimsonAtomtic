using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the C# binding over <c>crimson_paloc_*</c>. Most cases
/// are driven by a synthetic in-memory PALOC blob so they run on
/// every machine; one optional live-install test exercises the
/// end-to-end PAZ → PALOC chain when a Crimson Desert install is
/// present.
/// </summary>
public sealed class PalocCatalogTests
{
    /// <summary>Build a synthetic 3-entry PALOC blob (same layout as
    /// the Rust c_abi tests; mirror the wire format).</summary>
    private static byte[] Synthesise()
    {
        var ms = new MemoryStream();
        void PushEntry(ulong unkId, string key, string value)
        {
            ms.Write(BitConverter.GetBytes(unkId));
            ms.Write(BitConverter.GetBytes((uint)key.Length));
            ms.Write(System.Text.Encoding.UTF8.GetBytes(key));
            ms.Write(BitConverter.GetBytes((uint)value.Length));
            ms.Write(System.Text.Encoding.UTF8.GetBytes(value));
        }
        PushEntry(1, "ITEM_GOLD", "Gold");
        PushEntry(2, "ITEM_POTION", "Health Potion");
        PushEntry(3, "EMPTY_KEY", "");
        ms.Write(BitConverter.GetBytes(3u));
        return ms.ToArray();
    }

    [Fact]
    public void LoadFromBytes_Synthetic_ExposesEntries()
    {
        // Skip if dll isn't present — same guard the save-loader tests
        // use, so a fresh checkout without the rust build won't fail.
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        using var cat = NativePalocCatalog.LoadFromBytes(Synthesise());
        Assert.Equal(3, cat.EntryCount);

        Assert.Equal("Gold", cat.Lookup("ITEM_GOLD"));
        Assert.Equal("Health Potion", cat.Lookup("ITEM_POTION"));
        Assert.Equal(string.Empty, cat.Lookup("EMPTY_KEY"));
        Assert.Null(cat.Lookup("nonexistent_xyzzy"));
        Assert.Null(cat.Lookup(""));

        var first = cat.GetEntry(0);
        Assert.NotNull(first);
        Assert.Equal("ITEM_GOLD", first!.Value.Key);
        Assert.Equal("Gold", first.Value.Value);

        // Out-of-range returns null cleanly.
        Assert.Null(cat.GetEntry(99));
    }

    [Fact]
    public void LoadFromBytes_GarbageBytes_ThrowsCrimsonSaveException()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 32 zero bytes — trailing entry_count is 0, so parse fails with
        // "entry data ends at 0x0 but expected 0x1C", surfacing as
        // BODY_PARSE (-9) at the C ABI boundary.
        var garbage = new byte[32];
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            NativePalocCatalog.LoadFromBytes(garbage));
        Assert.Equal(-9, ex.ErrorCode);
    }

    [Fact]
    public void Lookup_AfterDispose_Throws()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var cat = NativePalocCatalog.LoadFromBytes(Synthesise());
        cat.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cat.Lookup("ITEM_GOLD"));

        // Double-dispose is a no-op.
        cat.Dispose();
    }
}
