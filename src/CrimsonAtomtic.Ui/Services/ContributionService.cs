using System.Globalization;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// One contribution currency as the save holds it: a stack in an inventory
/// container (<see cref="IsInSave"/>), or an item the save has none of.
/// <see cref="ItemPath"/> descends from the <c>InventorySaveData</c> block to
/// the <c>ItemSaveData</c> element; null for an absent item.
/// </summary>
public sealed record ContributionEntry(
    uint ItemKey,
    ulong Amount,
    bool IsInSave,
    uint? InventoryKey,
    int BlockIndex,
    PathStep[]? ItemPath,
    int StackFieldIndex);

/// <summary>A requested amount for one <see cref="ContributionEntry"/>.</summary>
public sealed record ContributionChange(ContributionEntry Entry, ulong NewAmount);

/// <summary>Outcome of <see cref="ContributionService.Apply"/>.</summary>
public sealed record ContributionApplyResult(int Updated, int Added, string? Error);

/// <summary>
/// Reads and writes the faction contribution currencies
/// (<c>Contribution_Hernandian</c>, <c>Contribution_Graymane</c>, …).
/// </summary>
/// <remarks>
/// <para>
/// They are ordinary <c>ItemSaveData</c> stacks. iteminfo gives them no tag,
/// but all of them, and nothing else, carry <c>category_info</c>
/// <see cref="ContributionCategory"/> (6 base items on 2.06; the DLC table
/// repeats the same 6), so the catalog is read from iteminfo instead of a
/// hard-coded key list. Their <c>item_type</c> (32) is shared with ordinary
/// money and camp resources, so it cannot tell them apart.
/// </para>
/// <para>
/// An item the save has none of is added by cloning an existing contribution
/// stack (same iteminfo profile, so the same field mask) into the container
/// that already holds them — <c>_inventorylist</c> key 1, "Camp &amp;
/// Contributions", on every save seen so far. With no contribution in the
/// save at all, a money-type (<c>item_type</c> 32) stack in container 1 is the
/// template; with neither, the add is refused. A consumable template would be
/// wrong: the game rejects a currency row with a consumable's mask on load.
/// </para>
/// </remarks>
public sealed class ContributionService(ISaveLoader loader, LocalizationProvider localization)
{
    /// <summary>iteminfo <c>category_info</c> of every contribution currency.</summary>
    public const ushort ContributionCategory = 60001;

    /// <summary>iteminfo <c>item_type</c> of money-like items (copper, camp resources, contributions).</summary>
    private const byte MoneyItemType = 32;

    /// <summary><c>_inventoryKey</c> of the "Camp &amp; Contributions" container.</summary>
    private const uint ContributionContainerKey = 1;

    private const string InventoryClass = "InventorySaveData";

    /// <summary>Contribution item keys from iteminfo (base, then DLC-only).</summary>
    public IReadOnlyList<uint> CatalogKeys() =>
        localization.EnumerateItemsByCategory(ContributionCategory);

    /// <summary>
    /// Every contribution stack in the save, plus one absent entry (amount 0)
    /// for each catalog item the save holds none of.
    /// </summary>
    public IReadOnlyList<ContributionEntry> Scan(string savePath, IReadOnlyList<BlockSummary> blocks)
    {
        var catalog = CatalogKeys();
        var wanted = catalog.ToHashSet();
        var found = new List<ContributionEntry>();
        var invBlock = blocks.FirstOrDefault(b => b.ClassName == InventoryClass);
        if (invBlock is not null)
        {
            var inv = loader.LoadBlockDetails(savePath, invBlock.Index);
            ForEachItem(inv, (containerKey, path, item, fields) =>
            {
                if (fields.ItemKey is { } key && wanted.Contains(key))
                {
                    found.Add(new ContributionEntry(key, fields.Stack ?? 0, true, containerKey,
                        invBlock.Index, path, fields.StackIndex));
                }
            });
        }
        var present = found.Select(e => e.ItemKey).ToHashSet();
        foreach (var key in catalog)
        {
            if (!present.Contains(key))
            {
                found.Add(new ContributionEntry(key, 0, false, null, invBlock?.Index ?? -1, null, -1));
            }
        }
        return found;
    }

    /// <summary>
    /// Validate a change set without touching the save. Returns null when it
    /// is valid. A stack in the save needs 1..max_stack; an absent item takes
    /// 0 (no change) or 1..max_stack (added).
    /// </summary>
    public string? Validate(IReadOnlyList<ContributionChange> changes)
    {
        foreach (var c in changes)
        {
            var max = localization.GetItemMaxStackCount(c.Entry.ItemKey) ?? ulong.MaxValue;
            if (c.NewAmount > max)
            {
                return UiText.Format("ContribErrTooLarge", "{0}: {1:N0} is above the stack limit {2:N0}.",
                    Name(c.Entry.ItemKey), c.NewAmount, max);
            }
            if (c.Entry.IsInSave && c.NewAmount == 0)
            {
                return UiText.Format("ContribErrZero",
                    "{0}: a stack the save holds can't be set to 0. Use at least 1, or delete the row in the inventory view.",
                    Name(c.Entry.ItemKey));
            }
        }
        return null;
    }

    /// <summary>
    /// Apply <paramref name="changes"/>: set the stack count of stacks the save
    /// holds, then add each absent item with a positive amount. Changes whose
    /// amount equals the current one are skipped. Stops at the first error.
    /// </summary>
    public ContributionApplyResult Apply(
        string savePath, IReadOnlyList<BlockSummary> blocks, IReadOnlyList<ContributionChange> changes)
    {
        if (Validate(changes) is { } invalid)
        {
            return new ContributionApplyResult(0, 0, invalid);
        }
        var updates = changes.Where(c => c.Entry.IsInSave && c.NewAmount != c.Entry.Amount).ToList();
        var adds = changes.Where(c => !c.Entry.IsInSave && c.NewAmount > 0).ToList();
        var updated = 0;
        var added = 0;
        try
        {
            if (updates.Count > 0)
            {
                loader.SetScalarFieldsBatch(updates
                    .Select(c => new ScalarBatchOp(c.Entry.BlockIndex, c.Entry.ItemPath!, c.Entry.StackFieldIndex,
                        BitConverter.GetBytes(c.NewAmount)))
                    .ToList());
                updated = updates.Count;
            }
            var invBlock = blocks.FirstOrDefault(b => b.ClassName == InventoryClass);
            foreach (var c in adds)
            {
                if (invBlock is null)
                {
                    return new ContributionApplyResult(updated, added,
                        UiText.Get("ContribErrNoInventory", "This save has no InventorySaveData block."));
                }
                if (AddOne(savePath, invBlock.Index, c.Entry.ItemKey, c.NewAmount) is { } err)
                {
                    return new ContributionApplyResult(updated, added, err);
                }
                added++;
            }
        }
        catch (CrimsonSaveException ex)
        {
            return new ContributionApplyResult(updated, added, ex.Message);
        }
        return new ContributionApplyResult(updated, added, null);
    }

    /// <summary>Clone a template stack into its container and patch it to <paramref name="itemKey"/>.</summary>
    private string? AddOne(string savePath, int blockIndex, uint itemKey, ulong amount)
    {
        // Re-read every time: an insert shifts the indices of the list it lands in.
        var inv = loader.LoadBlockDetails(savePath, blockIndex);
        if (FindTemplate(inv) is not { } t)
        {
            return UiText.Format("ContribErrNoTemplate",
                "Can't add {0}: the save holds no contribution, and no money-type item in the Camp & Contributions "
                + "container, to copy. Earn any contribution in-game once, then retry.",
                Name(itemKey));
        }
        var (_, maxItemNo) = ScanMaxes(inv, null);
        var (maxSlot, _) = ScanMaxes(inv, t.ContainerElement);
        var listPath = new[] { new PathStep((uint)t.ContainerListField, (uint)t.ContainerIndex) };
        var clonePath = new[]
        {
            new PathStep((uint)t.ContainerListField, (uint)t.ContainerIndex),
            new PathStep((uint)t.ItemListField, 0),
        };
        var ops = new List<ScalarBatchOp>
        {
            new(blockIndex, clonePath, t.Fields.ItemKeyIndex, BitConverter.GetBytes(itemKey)),
            new(blockIndex, clonePath, t.Fields.StackIndex, BitConverter.GetBytes(amount)),
            new(blockIndex, clonePath, t.Fields.SlotIndex, BitConverter.GetBytes((ushort)Math.Min(maxSlot + 1, ushort.MaxValue))),
            new(blockIndex, clonePath, t.Fields.ItemNoIndex, BitConverter.GetBytes(maxItemNo + 1)),
        };
        if (t.Fields.TransferredIndex >= 0)
        {
            ops.Add(new ScalarBatchOp(blockIndex, clonePath, t.Fields.TransferredIndex,
                BitConverter.GetBytes(TransferredItemKey(itemKey))));
        }
        loader.ListCloneElement(blockIndex, listPath, t.ItemListField, t.ItemIndex, destinationIndex: 0);
        loader.SetScalarFieldsBatch(ops);
        return null;
    }

    /// <summary>
    /// The engine's cross-check handle:
    /// <c>((itemKey &amp; 0xFFFF) &lt;&lt; 16) | 0x0101</c> (see
    /// <c>MainWindowViewModel.AddItemToCurrentListAsync</c> for the derivation).
    /// </summary>
    internal static uint TransferredItemKey(uint itemKey) => ((itemKey & 0xFFFFu) << 16) | 0x0101u;

    private sealed record Template(
        int ContainerListField, int ContainerIndex, BlockDetails ContainerElement,
        int ItemListField, int ItemIndex, ItemFields Fields);

    private Template? FindTemplate(BlockDetails inv)
    {
        var wanted = CatalogKeys().ToHashSet();
        Template? money = null;
        Template? contribution = null;
        ForEachItem(inv, (containerKey, path, item, fields) =>
        {
            if (contribution is not null || fields.ItemKey is not { } key || !fields.CanClone)
            {
                return;
            }
            var t = new Template((int)path[0].FieldIndex, (int)path[0].ElementIndex,
                ContainerAt(inv, path[0]), (int)path[1].FieldIndex, (int)path[1].ElementIndex, fields);
            if (wanted.Contains(key))
            {
                contribution = t;
            }
            else if (money is null && containerKey == ContributionContainerKey
                     && localization.LookupItemInfoSummary(key) is { ItemType: MoneyItemType })
            {
                money = t;
            }
        });
        return contribution ?? money;
    }

    private static BlockDetails ContainerAt(BlockDetails inv, PathStep step) =>
        inv.Fields.First(f => f.FieldIndex == (int)step.FieldIndex).Elements![(int)step.ElementIndex];

    /// <summary>Max <c>_slotNo</c> within <paramref name="container"/> (or none) and max <c>_itemNo</c> across the whole block.</summary>
    private static (ulong MaxSlot, ulong MaxItemNo) ScanMaxes(BlockDetails inv, BlockDetails? container)
    {
        ulong maxSlot = 0, maxItemNo = 0;
        ForEachItem(inv, (_, path, item, fields) =>
        {
            maxItemNo = Math.Max(maxItemNo, fields.ItemNo ?? 0);
            if (container is not null && ReferenceEquals(ContainerAt(inv, path[0]), container))
            {
                maxSlot = Math.Max(maxSlot, fields.Slot ?? 0);
            }
        });
        return (maxSlot, maxItemNo);
    }

    private readonly record struct ItemFields(
        uint? ItemKey, ulong? Stack, ulong? Slot, ulong? ItemNo,
        int ItemKeyIndex, int StackIndex, int SlotIndex, int ItemNoIndex, int TransferredIndex)
    {
        public bool CanClone => ItemKeyIndex >= 0 && StackIndex >= 0 && SlotIndex >= 0 && ItemNoIndex >= 0;
    }

    /// <summary>
    /// Visit every <c>ItemSaveData</c> in <c>_inventorylist[*]._itemList[*]</c>
    /// with its container's <c>_inventoryKey</c> and the two-step path to it.
    /// </summary>
    private static void ForEachItem(
        BlockDetails inv, Action<uint?, PathStep[], BlockDetails, ItemFields> visit)
    {
        var list = inv.Fields.FirstOrDefault(f => f.Name == "_inventorylist");
        if (list?.Elements is not { } containers)
        {
            return;
        }
        for (var ci = 0; ci < containers.Count; ci++)
        {
            var container = containers[ci];
            uint? containerKey = null;
            DecodedFieldRow? itemList = null;
            foreach (var f in container.Fields)
            {
                if (f.Name == "_inventoryKey" && SaveFieldValues.TryU64(f) is { } k) containerKey = (uint)k;
                else if (f.Name == "_itemList") itemList = f;
            }
            if (itemList?.Elements is not { } items)
            {
                continue;
            }
            for (var ii = 0; ii < items.Count; ii++)
            {
                var item = items[ii];
                if (item.ClassName != "ItemSaveData")
                {
                    continue;
                }
                PathStep[] path = [new((uint)list.FieldIndex, (uint)ci), new((uint)itemList.FieldIndex, (uint)ii)];
                visit(containerKey, path, item, ReadFields(item));
            }
        }
    }

    private static ItemFields ReadFields(BlockDetails item)
    {
        uint? key = null;
        ulong? stack = null, slot = null, itemNo = null;
        int iKey = -1, iStack = -1, iSlot = -1, iNo = -1, iTransferred = -1;
        foreach (var f in item.Fields)
        {
            switch (f.Name)
            {
                case "_itemKey": iKey = f.FieldIndex; key = SaveFieldValues.TryU64(f) is { } k ? (uint)k : null; break;
                case "_stackCount": iStack = f.FieldIndex; stack = SaveFieldValues.TryU64(f); break;
                case "_slotNo": iSlot = f.FieldIndex; slot = SaveFieldValues.TryU64(f); break;
                case "_itemNo": iNo = f.FieldIndex; itemNo = SaveFieldValues.TryU64(f); break;
                case "_transferredItemKey": if (f.Present) iTransferred = f.FieldIndex; break;
            }
        }
        return new ItemFields(key, stack, slot, itemNo, iKey, iStack, iSlot, iNo, iTransferred);
    }

    private string Name(uint itemKey) =>
        localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
        ?? localization.ItemInfoStringKey(itemKey)
        ?? itemKey.ToString(CultureInfo.InvariantCulture);
}
