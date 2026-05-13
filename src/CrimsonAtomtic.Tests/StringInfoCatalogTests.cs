using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the C# binding over <c>crimson_string_info_*</c>. Synthetic
/// stringinfo blobs are trivial to build (the format is
/// <c>[u32 hash][u32 zero][u8 flag][u32 slen][N utf-8]</c>) so the
/// error paths get their own deterministic coverage. The happy path
/// also runs against the live game install when present and skips
/// cleanly otherwise.
/// </summary>
public sealed class StringInfoCatalogTests
{
    private static string? FindStringInfoPamt()
    {
        // Mirror the probe order in WindowsPlatformPaths.GameInstallRoot.
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            var p = Path.Combine(root, "0008", "0.pamt");
            if (File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>
    /// Build a minimal valid stringinfo.pabgb buffer with the supplied
    /// entries. Each entry is laid out as:
    ///   [u32 hash][u32 zero][u8 flag][u32 slen][N bytes utf-8]
    /// </summary>
    private static byte[] BuildSyntheticBlob(params (uint Hash, string Value)[] entries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var (hash, value) in entries)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            bw.Write(hash);
            bw.Write(0u);             // reserved_zero
            bw.Write((byte)0);        // reserved_flag
            bw.Write((uint)utf8.Length);
            bw.Write(utf8);
        }
        return ms.ToArray();
    }

    [Fact]
    public void LoadFromBytes_Synthetic_LookupByHashRoundTrips()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var blob = BuildSyntheticBlob(
            (0x2ad9f89e, "RealWorld"),
            (0x04f6d06d, "ChildWild"),
            (0xdeadbeef, "cd_icon_arrow_basic.dds"));

        using var cat = NativeStringInfoCatalog.LoadFromBytes(blob);
        Assert.Equal(3, cat.EntryCount);

        Assert.Equal("RealWorld", cat.LookupByHash(0x2ad9f89e));
        Assert.Equal("ChildWild", cat.LookupByHash(0x04f6d06d));
        Assert.Equal("cd_icon_arrow_basic.dds", cat.LookupByHash(0xdeadbeef));

        // NOT_FOUND returns null cleanly.
        Assert.Null(cat.LookupByHash(0x12345678));

        // Enumeration round-trips: every (hash, value) we put in must
        // come out, and lookup-by-hash must agree.
        var first = cat.GetEntry(0);
        Assert.NotNull(first);
        Assert.Equal(0x2ad9f89eu, first!.Value.Hash);
        Assert.Equal("RealWorld", first.Value.Value);

        var third = cat.GetEntry(2);
        Assert.NotNull(third);
        Assert.Equal(0xdeadbeefu, third!.Value.Hash);

        // Past-the-end returns null cleanly.
        Assert.Null(cat.GetEntry(99));
    }

    [Fact]
    public void LoadFromBytes_LiveInstall_ResolvesItemIconPath()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindStringInfoPamt();
        if (pamt is null)
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt, "gamedata/binary__/client/bin", "stringinfo.pabgb");

        using var cat = NativeStringInfoCatalog.LoadFromBytes(bytes);
        // 1.06 ships ~30,206 entries; sanity-check the order of magnitude.
        Assert.True(cat.EntryCount > 20_000,
            $"expected >20k entries, got {cat.EntryCount}");

        // The first entry on disk in 1.06 is the well-known "RealWorld"
        // mapping. Pinning it catches a schema drift loudly.
        var first = cat.GetEntry(0);
        Assert.NotNull(first);
        Assert.Equal(0x2ad9f89eu, first!.Value.Hash);
        Assert.Equal("RealWorld", first.Value.Value);

        // Round-trip: any hash returned by GetEntry must lookup to the
        // same value via LookupByHash. Spot-check a handful.
        for (int i = 0; i < cat.EntryCount; i += 5_000)
        {
            var entry = cat.GetEntry(i);
            Assert.NotNull(entry);
            Assert.Equal(entry!.Value.Value, cat.LookupByHash(entry.Value.Hash));
        }

        // NOT_FOUND on a definitely-invalid hash. (u32.MaxValue
        // collisions against ~30k entries are vanishingly unlikely.)
        Assert.Null(cat.LookupByHash(uint.MaxValue));
    }

    [Fact]
    public void LoadFromBytes_GarbageBytes_ThrowsCrimsonSaveException()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 8 bytes is short enough that the very first entry's slen read
        // overruns the buffer → BODY_PARSE.
        var garbage = new byte[8];
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            NativeStringInfoCatalog.LoadFromBytes(garbage));
        Assert.Equal(-9, ex.ErrorCode); // BODY_PARSE
    }

    [Fact]
    public void LoadFromBytes_Empty_IsZeroEntryHandle()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // An empty pabgb is a degenerate-but-valid case.
        using var cat = NativeStringInfoCatalog.LoadFromBytes(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, cat.EntryCount);
        Assert.Null(cat.LookupByHash(0));
        Assert.Null(cat.GetEntry(0));
    }

    [Fact]
    public void LookupByHash_AfterDispose_Throws()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var blob = BuildSyntheticBlob((1u, "x"));
        var cat = NativeStringInfoCatalog.LoadFromBytes(blob);
        cat.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cat.LookupByHash(0));
        Assert.Throws<ObjectDisposedException>(() => cat.GetEntry(0));
        // Double-dispose is a no-op.
        cat.Dispose();
    }
}
