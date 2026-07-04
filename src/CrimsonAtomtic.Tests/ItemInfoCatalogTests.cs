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

        // Max-stack lookup must succeed for any real key. We don't
        // assert a specific value — the contract is "the schema's
        // value is returned"; pinning a number would lock the test
        // to a particular item's stack cap which is irrelevant to
        // the binding.
        var maxStack = cat.LookupMaxStackCount(first.Value.ItemKey);
        Assert.NotNull(maxStack);
        // NOT_FOUND on a definitely-invalid key.
        Assert.Null(cat.LookupMaxStackCount(uint.MaxValue));
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
    public void SocketCaps_AndCanonicalGemSet_LiveInstall()
    {
        if (!File.Exists("crimson_rs.dll")) return;
        var pamt = FindIteminfoPamt();
        if (pamt is null) return;
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt, "gamedata/binary__/client/bin", "iteminfo.pabgb");
        using var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);

        // Unknown item → null (the LookupSocketCaps NOT_FOUND path).
        Assert.Null(cat.LookupSocketCaps(uint.MaxValue));

        // Canonical gem set is non-empty and enumeration works.
        var gemCount = cat.CanonicalGemCount;
        Assert.True(gemCount > 50,
                    $"expected >50 canonical gems, got {gemCount}");
        var firstGemKey = cat.GetCanonicalGemKey(0);
        Assert.NotNull(firstGemKey);
        Assert.True(firstGemKey!.Value > 0);
        // Past-end → null
        Assert.Null(cat.GetCanonicalGemKey(gemCount + 1000));

        // Sample gem from the upstream survey (1002979 = greater gem
        // verified durability-bearing) should be in the canonical
        // set. Walk to find it.
        const uint sampleGem = 1002979;
        var found = false;
        for (var i = 0; i < gemCount; i++)
        {
            if (cat.GetCanonicalGemKey(i) == sampleGem) { found = true; break; }
        }
        Assert.True(found,
                    $"sample gem {sampleGem} should be in canonical gem set");

        // SocketAllowsGem on an unknown item → null
        Assert.Null(cat.SocketAllowsGem(uint.MaxValue, sampleGem));
    }

    [Fact]
    public void ItemInfoSummary_Layout_MatchesRustAbi()
    {
        // The C ABI surface pins this at 80 bytes via
        // `const _: () = { assert!(size_of::<CrimsonItemInfoSummary>() == 80) }`
        // in vendor/crimson-rs/src/c_abi/iteminfo.rs. Mirror the
        // assertion on the C# side so any drift in either layout
        // breaks the build, not the consumer at runtime.
        var size = System.Runtime.InteropServices.Marshal.SizeOf<ItemInfoSummary>();
        Assert.Equal(80, size);
    }

    [Fact]
    public void LookupSummary_LiveInstall_PinsKnownItem()
    {
        if (!File.Exists("crimson_rs.dll")) return;
        var pamt = FindIteminfoPamt();
        if (pamt is null) return;
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt, "gamedata/binary__/client/bin", "iteminfo.pabgb");
        using var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);

        // Unknown key → null (NOT_FOUND path).
        Assert.Null(cat.LookupSummary(uint.MaxValue));
        Assert.Null(cat.LookupFlags(uint.MaxValue));

        // Pyeonjeon_Arrow (key 2200) — vendor pins this as an arrow that is
        // explicitly NOT IS_EQUIP_QUICK_SLOT_VISIBLE in the upstream
        // live-install test. Its item_type was remapped 0 → 23 in game 1.13
        // (a game-side enum shuffle, not a parser drift — crimson-rs tag
        // v1.0.13.x bumped the same pin). We mirror just enough of that pin
        // to catch a parser regression here; the broader cross-version drift
        // the upstream covers is out of scope for this binding test.
        var arrow = cat.LookupSummary(2200);
        Assert.NotNull(arrow);
        Assert.Equal(2200u, arrow!.Value.Key);
        Assert.Equal(23, arrow.Value.ItemType);
        Assert.False(arrow.Value.Flags.HasFlag(ItemInfoFlags.IsEquipQuickSlotVisible),
            "Pyeonjeon_Arrow should not be flagged as IS_EQUIP_QUICK_SLOT_VISIBLE");

        // Marni_Devotee_PlateArmor_Helm (key 14510) — vendor pin says
        // item_type=24 and IS_EQUIP_QUICK_SLOT_VISIBLE set. Mirrors
        // the upstream c_abi_iteminfo_static_lookups_live pin.
        var helm = cat.LookupSummary(14510);
        Assert.NotNull(helm);
        Assert.Equal(14510u, helm!.Value.Key);
        Assert.Equal(24, helm.Value.ItemType);
        Assert.True(helm.Value.Flags.HasFlag(ItemInfoFlags.IsEquipQuickSlotVisible),
            "Marni_Devotee_PlateArmor_Helm should be flagged as IS_EQUIP_QUICK_SLOT_VISIBLE");

        // The flags-only lookup must agree with the summary lookup's
        // Flags field — both read from the same cache so they should be
        // identical bit-for-bit.
        Assert.Equal(arrow.Value.Flags, cat.LookupFlags(2200));
        Assert.Equal(helm.Value.Flags, cat.LookupFlags(14510));

        // Reserved padding byte must round-trip through the marshaller
        // as 0 (Rust side writes `_reserved: 0`).
        Assert.Equal(0, arrow.Value.ReservedByte);
        Assert.Equal(0, helm.Value.ReservedByte);
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
