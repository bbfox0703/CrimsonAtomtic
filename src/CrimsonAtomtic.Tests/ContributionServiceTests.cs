using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Edit Contributions against the live install and a copy of the newest
/// save: the catalog comes from iteminfo category 60001, existing stacks are
/// set in place, an absent contribution is added by cloning an existing one,
/// and the written file reads back with the new amounts.
/// </summary>
public sealed class ContributionServiceTests
{
    /// <summary>Contribution_Hernandian — the currency Hernand's shops take.</summary>
    private const uint Hernandian = 1001754;

    private static (LocalizationProvider Loc, string Save)? LiveOrSkip()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return null;
        }
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        var save = LiveSaves.Newest();
        if (pamt is null || save is null)
        {
            return null;
        }
        var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        return (loc, save);
    }

    [Fact]
    public void Catalog_IsIteminfoCategory60001()
    {
        if (LiveOrSkip() is not { } live) return;
        using var loc = live.Loc;
        var keys = new ContributionService(new NativeSaveLoader(), loc).CatalogKeys();
        // 2.06: Graymane, Hernandian, Demenissian, Pailunese, Delesyian,
        // Tashkalpan. A floor, so a new region's currency doesn't break it.
        Assert.True(keys.Count >= 6, $"expected >= 6 contribution items, got {keys.Count}");
        Assert.Contains(Hernandian, keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.StartsWith("Contribution_", loc.ItemInfoStringKey(k) ?? string.Empty));
    }

    [Fact]
    public void Apply_SetsExistingAndAddsAbsent_AndReadsBack()
    {
        if (LiveOrSkip() is not { } live) return;
        using var loc = live.Loc;
        var dir = Directory.CreateTempSubdirectory("crimson-contrib-");
        try
        {
            using var loader = new NativeSaveLoader();
            var blocks = loader.Load(live.Save).Blocks;
            var service = new ContributionService(loader, loc);
            var before = service.Scan(live.Save, blocks);
            Assert.Equal(service.CatalogKeys().ToHashSet(), before.Select(e => e.ItemKey).ToHashSet());

            var existing = before.FirstOrDefault(e => e.IsInSave);
            var absent = before.FirstOrDefault(e => !e.IsInSave);
            if (existing is null)
            {
                return; // nothing to clone from in this save
            }
            var changes = new List<ContributionChange> { new(existing, existing.Amount + 7) };
            if (absent is not null)
            {
                changes.Add(new ContributionChange(absent, 1234));
            }
            var result = service.Apply(live.Save, blocks, changes);
            Assert.Null(result.Error);
            Assert.Equal(1, result.Updated);
            Assert.Equal(absent is null ? 0 : 1, result.Added);

            var outPath = Path.Combine(dir.FullName, "save.save");
            loader.WriteToFile(outPath);

            using var reread = new NativeSaveLoader();
            var blocks2 = reread.Load(outPath).Blocks;
            var after = new ContributionService(reread, loc).Scan(outPath, blocks2);
            Assert.Equal(existing.Amount + 7,
                after.Single(e => e.IsInSave && e.ItemKey == existing.ItemKey && e.InventoryKey == existing.InventoryKey).Amount);
            if (absent is not null)
            {
                var added = after.Single(e => e.ItemKey == absent.ItemKey);
                Assert.True(added.IsInSave);
                Assert.Equal(1234UL, added.Amount);
                Assert.Equal(existing.InventoryKey, added.InventoryKey);
                AssertWellFormed(reread, outPath, added);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Validate_RejectsZeroOnAStackAndAboveTheLimit()
    {
        if (LiveOrSkip() is not { } live) return;
        using var loc = live.Loc;
        var service = new ContributionService(new NativeSaveLoader(), loc);
        var inSave = new ContributionEntry(Hernandian, 5, true, 1, 0, [], 0);
        var absent = inSave with { Amount = 0, IsInSave = false, ItemPath = null };
        Assert.NotNull(service.Validate([new(inSave, 0)]));
        Assert.NotNull(service.Validate([new(inSave, ulong.MaxValue)]));
        Assert.Null(service.Validate([new(inSave, 1)]));
        Assert.Null(service.Validate([new(absent, 0)]));
    }

    /// <summary>
    /// The added stack carries the engine's <c>_transferredItemKey</c> for its
    /// own key, an <c>_itemNo</c> no other item in the inventory uses, and the
    /// inventory block still decodes completely.
    /// </summary>
    private static void AssertWellFormed(
        NativeSaveLoader loader, string path, ContributionEntry added)
    {
        var inv = loader.LoadBlockDetails(path, added.BlockIndex);
        Assert.Empty(inv.UndecodedRanges);
        var list = inv.Fields.Single(f => f.Name == "_inventorylist");
        var item = list.Elements![(int)added.ItemPath![0].ElementIndex]
            .Fields.Single(f => f.Name == "_itemList").Elements![(int)added.ItemPath[1].ElementIndex];
        Assert.Equal(ContributionService.TransferredItemKey(added.ItemKey), U(item, "_transferredItemKey"));
        var itemNo = U(item, "_itemNo");
        var all = list.Elements!
            .SelectMany(c => c.Fields.Single(f => f.Name == "_itemList").Elements ?? [])
            .Select(e => U(e, "_itemNo"))
            .Where(n => n == itemNo)
            .Count();
        Assert.Equal(1, all);
    }

    private static ulong U(BlockDetails element, string field)
    {
        var f = element.Fields.Single(x => x.Name == field);
        if (!f.Present || !ScalarFieldEditing.TryParse(f.Value, out var raw, out _))
        {
            return ulong.MaxValue;
        }
        return ulong.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
    }
}
