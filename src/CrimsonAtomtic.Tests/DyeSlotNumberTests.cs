using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// The Dye editor's slot numbers: an item offers the dye slot numbers its
/// meshes' masks flag, unioned over every mesh its
/// prefabs list and every body type. They are the save's
/// <c>_dyeSlotNo</c> values — sparse, not 0..N-1 over sub-meshes.
/// </summary>
public sealed class DyeSlotNumberTests
{
    private static LocalizationProvider? LiveOrSkip()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return null;
        }
        var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        return loc;
    }

    /// <summary>
    /// Body-type armour, which the old name join could not reach. The user
    /// dyed 111147 (Demeniss Honor Armor) in-game on 2.06 and the save holds
    /// _dyeSlotNo 0, 1 and 7; 9102 (Hernandian Honor Guard Armor) holds 0.
    /// </summary>
    [Fact]
    public void BodyTypeArmour_OffersTheNumbersTheGameWrote()
    {
        using var loc = LiveOrSkip();
        if (loc is null) return;
        var demeniss = loc.LookupDyeSlotNumbers(111147);
        Assert.NotNull(demeniss);
        Assert.Subset(demeniss.ToHashSet(), new HashSet<int> { 0, 1, 7 });
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], demeniss);
        Assert.Equal([0, 2, 3, 6, 7, 9], loc.LookupDyeSlotNumbers(9102));
        Assert.Equal(8, loc.LookupDyeSlotCount(111147));
    }

    /// <summary>
    /// dlc_01's items join through the DLC's own prefabs once merged
    ///. Skipped on an install without the DLC.
    /// </summary>
    [Fact]
    public void DlcItems_ResolveAfterTheMerge()
    {
        using var loc = LiveOrSkip();
        if (loc is null) return;
        var root = loc.GameRoot!;
        if (!GameDataLayout.DlcNames(root).Contains("dlc_01")
            || !File.Exists(Path.Combine(root, GameDataLayout.DlcDirectory, "dlc_01", "0009", "0.pamt")))
        {
            return;
        }
        Assert.Equal([0, 1, 2, 3, 4], loc.LookupDyeSlotNumbers(1004390));
    }

    [Fact]
    public void DyeableItems_ResolveThroughTheirPrefabMeshes()
    {
        using var loc = LiveOrSkip();
        if (loc is null) return;
        var items = 0;
        var sparse = 0;
        for (var i = 0; i < loc.ItemCount; i++)
        {
            if (loc.GetItem(i) is not { } item || loc.LookupDyeSlotNumbers(item.ItemKey) is not { Count: > 0 } numbers)
            {
                continue;
            }
            items++;
            Assert.All(numbers, n => Assert.InRange(n, 0, 11));
            Assert.Equal(numbers.Order().ToList(), numbers);
            if (numbers[^1] != numbers.Count - 1) sparse++;
        }
        // 2.06: 909 of the 917 is_dyeable base items resolve through their
        // prefab meshes (70 did by name); the DLC adds more once merged.
        Assert.True(items > 800, $"expected >800 dyeable items, got {items}");
        Assert.True(sparse > 0, "expected some items with non-contiguous dye slot numbers");
    }
}
