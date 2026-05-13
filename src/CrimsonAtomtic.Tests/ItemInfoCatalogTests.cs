using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the C# binding over <c>crimson_iteminfo_*</c>. Synthetic
/// iteminfo blobs aren't practical (each item is ~600 B with 100+
/// fields), so the happy path runs against the live game install when
/// present and skips cleanly otherwise. Error paths exercise the
/// wrappers without needing valid item bytes.
/// </summary>
public sealed class ItemInfoCatalogTests
{
    private static string? FindIteminfoPamt()
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

    [Fact]
    public void LoadFromBytes_LiveInstall_RoundTripsGetEntryAndLookup()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindIteminfoPamt();
        if (pamt is null)
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt, "gamedata/binary__/client/bin", "iteminfo.pabgb");

        using var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);
        // 1.05 had ~6,236; 1.06 has ~6,400. Just assert plausibly populated.
        Assert.True(cat.EntryCount > 5_000,
            $"expected >5k items, got {cat.EntryCount}");

        var first = cat.GetEntry(0);
        Assert.NotNull(first);
        Assert.False(string.IsNullOrEmpty(first!.Value.StringKey),
            "first item's string_key should be non-empty");

        // LookupStringKey by the u32 we just got back must yield the
        // same string key.
        var lookup = cat.LookupStringKey(first.Value.ItemKey);
        Assert.Equal(first.Value.StringKey, lookup);

        // Past-the-end returns null cleanly.
        Assert.Null(cat.GetEntry(99_999_999));

        // NOT_FOUND on an obviously-invalid item key.
        Assert.Null(cat.LookupStringKey(uint.MaxValue));
    }

    [Fact]
    public void LoadFromBytes_GarbageBytes_ThrowsCrimsonSaveException()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var garbage = new byte[32];
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            NativeItemInfoCatalog.LoadFromBytes(garbage));
        Assert.Equal(-9, ex.ErrorCode); // BODY_PARSE
    }

    [Fact]
    public void LookupStringKey_AfterDispose_Throws()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindIteminfoPamt();
        if (pamt is null)
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt, "gamedata/binary__/client/bin", "iteminfo.pabgb");
        var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);
        cat.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cat.LookupStringKey(0));
        // Double-dispose is a no-op.
        cat.Dispose();
    }
}
