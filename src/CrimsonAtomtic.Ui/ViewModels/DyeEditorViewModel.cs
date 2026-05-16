using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the master "Edit Item Dyes" dialog. Walks every
/// <c>InventorySaveData</c> AND <c>EquipmentSaveData</c> top-level
/// block, finds items whose <c>_itemDyeDataList</c> field is present
/// + non-empty, and lists them with one row per dyed item. The
/// per-row Edit button raises <see cref="EditRequested"/>; the
/// hosting MainWindow code-behind opens the per-item slot editor
/// (<see cref="DyeSlotEditorViewModel"/>) in response.
///
/// <para>
/// <b>v1 scope</b>: edit-existing-dye only. The per-slot editor
/// mutates RGBA / grime / palette key / color-group key on existing
/// dye-list elements. "Add dye to undyed item" is deferred until the
/// upstream <c>set_object_list_present</c> ABI lands (per
/// <c>vendor/crimson-rs/docs/dye-editor-scope.md</c>).
/// </para>
///
/// <para>
/// <b>2026-05-16 part 14</b>: switched from
/// <see cref="ISaveLoader.ListInventoryItems"/> (inventory-only) to
/// direct block walking so equipped gear under
/// <c>EquipmentSaveData._list[i]._item</c> shows up too. Equipped
/// items use the same <c>ItemSaveData</c> schema as inventory items
/// — the only difference is the descent path. <see cref="DyeEditorItemRow"/>
/// stores the two descent steps as "first-step / second-step" pairs
/// that the path-addressed scalar setter consumes uniformly across
/// both sources.
/// </para>
///
/// <para>
/// One snapshot read at open + on Refresh. Edits made via the per-
/// item child dialog flip <see cref="IsDirty"/> + bump the host VM's
/// dirty flag on dialog close.
/// </para>
/// </summary>
public sealed partial class DyeEditorViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced in <see cref="Items"/>.</summary>
    public const int MaxResults = 500;

    /// <summary>
    /// Schema field name of the dye list on each <c>ItemSaveData</c>
    /// element. Per the upstream survey (<c>dye-editor-scope.md</c>),
    /// this is field index 14 in 1.07 — but we look up by name so a
    /// patch that reorders fields doesn't break us.
    /// </summary>
    public const string DyeListFieldName = "_itemDyeDataList";

    /// <summary>Top-level block class names this editor walks.</summary>
    private const string InventorySaveDataClass = "InventorySaveData";
    private const string EquipmentSaveDataClass = "EquipmentSaveData";

    /// <summary>Field names along the descent path.</summary>
    private const string InventoryListFieldName = "_inventorylist";
    private const string ItemListFieldName = "_itemList";
    private const string EquipListFieldName = "_list";
    private const string EquipItemLocatorFieldName = "_item";
    private const string ItemKeyFieldName = "_itemKey";
    private const string InventoryKeyFieldName = "_inventoryKey";

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly string _savePath;
    private readonly IReadOnlyList<BlockSummary> _blocks;
    private List<DyeEditorItemRow> _allRows = new();

    public DyeEditorViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);
        _loader = loader;
        _localization = localization;
        _savePath = savePath;
        _blocks = blocks;
        SecondaryLanguage = localization.SecondaryLanguage;
        Refresh();
    }

    public string? SecondaryLanguage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    public ObservableCollection<DyeEditorItemRow> Items { get; } = [];

    public int TotalDyedItems => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (!_localization.HasDyeGamedata)
            {
                return "Dye gamedata not loaded — no game install configured. "
                    + "Material / color-group dropdowns will be unavailable.";
            }
            if (TotalDyedItems == 0)
            {
                return "No dyed items found in this save.";
            }
            if (string.IsNullOrEmpty(SearchText))
            {
                return $"{Items.Count:N0} dyed item(s).";
            }
            return $"{Items.Count:N0} matches of {TotalDyedItems:N0}.";
        }
    }

    /// <summary>
    /// True once at least one Apply has been issued via the per-item
    /// child dialog. Surfaces back to the MainWindow on dialog close
    /// so the title-bar dirty * shows + File → Save persists.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Raised when the user clicks a row's Edit button. The hosting
    /// MainWindow subscribes and opens the per-item slot editor.
    /// </summary>
    public event Action<DyeEditorItemRow>? EditRequested;

    public void RequestEdit(DyeEditorItemRow row) => EditRequested?.Invoke(row);

    /// <summary>
    /// Called by the host after a child editor applied at least one
    /// edit, so the master VM can mark itself dirty. (The child VM
    /// owns the actual writes.)
    /// </summary>
    public void NotifyChildApplied() => IsDirty = true;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    public void Refresh()
    {
        _allRows = ScanForDyedItems(_loader, _localization, _savePath, _blocks);
        ApplyFilter();
    }

    /// <summary>
    /// Scan every relevant top-level block for items with non-empty
    /// dye lists. Two block-type branches share one CollectFromItem
    /// helper — same uniform "first-step + second-step" descent
    /// semantics the Sockets editor uses.
    /// </summary>
    private static List<DyeEditorItemRow> ScanForDyedItems(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        var results = new List<DyeEditorItemRow>();
        foreach (var b in blocks)
        {
            BlockDetails details;
            try
            {
                details = loader.LoadBlockDetails(savePath, b.Index);
            }
            catch (CrimsonSaveException)
            {
                continue;
            }
            if (string.Equals(b.ClassName, InventorySaveDataClass, StringComparison.Ordinal))
            {
                CollectFromInventory(results, localization, b.Index, details);
            }
            else if (string.Equals(b.ClassName, EquipmentSaveDataClass, StringComparison.Ordinal))
            {
                CollectFromEquipment(results, localization, b.Index, details);
            }
            if (results.Count >= MaxResults)
            {
                break;
            }
        }
        // Sort by source label then by item name — predictable ordering.
        results.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.BagLabel, b.BagLabel);
            return c != 0 ? c : string.CompareOrdinal(a.ItemNameEnglish, b.ItemNameEnglish);
        });
        return results;
    }

    private static void CollectFromInventory(
        List<DyeEditorItemRow> sink,
        LocalizationProvider localization,
        int blockIndex,
        BlockDetails top)
    {
        for (var f = 0; f < top.Fields.Count; f++)
        {
            var invList = top.Fields[f];
            if (!string.Equals(invList.Name, InventoryListFieldName, StringComparison.Ordinal)
                || invList.Elements is not { Count: > 0 } bags)
            {
                continue;
            }
            for (var bagIdx = 0; bagIdx < bags.Count; bagIdx++)
            {
                var bag = bags[bagIdx];
                // Resolve a friendly bag label from the bag's _inventoryKey
                // scalar so the row shows e.g. "Pocket" / "Equipment" /
                // "Bag N". Mirrors the Sockets editor's FormatBagLabel.
                var bagLabel = ResolveBagLabel(localization, bag, bagIdx);
                for (var g = 0; g < bag.Fields.Count; g++)
                {
                    var itemListField = bag.Fields[g];
                    if (!string.Equals(itemListField.Name, ItemListFieldName, StringComparison.Ordinal)
                        || itemListField.Elements is not { Count: > 0 } items)
                    {
                        continue;
                    }
                    for (var itemIdx = 0; itemIdx < items.Count; itemIdx++)
                    {
                        if (sink.Count >= MaxResults) return;
                        TryAddIfDyed(
                            sink, localization, blockIndex,
                            firstStepFieldIdx: (uint)f,
                            firstStepElementIdx: (uint)bagIdx,
                            secondStepFieldIdx: (uint)g,
                            secondStepElementIdx: (uint)itemIdx,
                            bagLabel: bagLabel,
                            item: items[itemIdx]);
                    }
                }
            }
        }
    }

    private static void CollectFromEquipment(
        List<DyeEditorItemRow> sink,
        LocalizationProvider localization,
        int blockIndex,
        BlockDetails top)
    {
        for (var f = 0; f < top.Fields.Count; f++)
        {
            var listField = top.Fields[f];
            if (!string.Equals(listField.Name, EquipListFieldName, StringComparison.Ordinal)
                || listField.Elements is not { Count: > 0 } slots)
            {
                continue;
            }
            for (var slotIdx = 0; slotIdx < slots.Count; slotIdx++)
            {
                var slot = slots[slotIdx];
                for (var g = 0; g < slot.Fields.Count; g++)
                {
                    var itemLocator = slot.Fields[g];
                    if (!string.Equals(itemLocator.Name, EquipItemLocatorFieldName, StringComparison.Ordinal)
                        || !itemLocator.Present
                        || itemLocator.Child is not { } itemChild)
                    {
                        continue;
                    }
                    if (sink.Count >= MaxResults) return;
                    TryAddIfDyed(
                        sink, localization, blockIndex,
                        firstStepFieldIdx: (uint)f,
                        firstStepElementIdx: (uint)slotIdx,
                        secondStepFieldIdx: (uint)g,
                        secondStepElementIdx: 0u,    // locator descent ignores element_idx
                        bagLabel: "Equipped",
                        item: itemChild);
                }
            }
        }
    }

    private static void TryAddIfDyed(
        List<DyeEditorItemRow> sink,
        LocalizationProvider localization,
        int blockIndex,
        uint firstStepFieldIdx,
        uint firstStepElementIdx,
        uint secondStepFieldIdx,
        uint secondStepElementIdx,
        string bagLabel,
        BlockDetails item)
    {
        uint itemKey = 0;
        DecodedFieldRow? dyeListField = null;
        foreach (var f in item.Fields)
        {
            if (string.Equals(f.Name, ItemKeyFieldName, StringComparison.Ordinal)
                && f.Present
                && TryParseScalarUInt(f.Value, out var k)
                && k <= uint.MaxValue)
            {
                itemKey = (uint)k;
            }
            else if (string.Equals(f.Name, DyeListFieldName, StringComparison.Ordinal))
            {
                dyeListField = f;
            }
        }
        if (dyeListField is null
            || !dyeListField.Present
            || dyeListField.Elements is not { Count: > 0 } dyeSlots)
        {
            return;
        }
        var nameEn = localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                     ?? localization.ItemInfoStringKey(itemKey)
                     ?? itemKey.ToString(CultureInfo.InvariantCulture);
        var secondaryLang = localization.SecondaryLanguage;
        string? nameSecondary = secondaryLang is null
            ? null
            : localization.LookupItemName(itemKey, secondaryLang);
        sink.Add(new DyeEditorItemRow(
            blockIndex: blockIndex,
            firstStepFieldIndex: firstStepFieldIdx,
            firstStepElementIndex: firstStepElementIdx,
            secondStepFieldIndex: secondStepFieldIdx,
            secondStepElementIndex: secondStepElementIdx,
            dyeListFieldIndex: (uint)dyeListField.FieldIndex,
            dyeSlotCount: dyeSlots.Count,
            bagLabel: bagLabel,
            itemKey: itemKey,
            itemNameEnglish: nameEn,
            itemNameSecondary: nameSecondary));
    }

    /// <summary>
    /// Find the bag's <c>_inventoryKey</c> scalar (when present) and
    /// route it through PALOC's InventoryKey table for a friendly
    /// label ("Pocket", "Equipment", etc.). Falls back to "Bag N"
    /// when the key isn't in the table.
    /// </summary>
    private static string ResolveBagLabel(
        LocalizationProvider localization, BlockDetails bag, int bagIdx)
    {
        foreach (var f in bag.Fields)
        {
            if (string.Equals(f.Name, InventoryKeyFieldName, StringComparison.Ordinal)
                && f.Present
                && TryParseScalarUInt(f.Value, out var ik)
                && ik <= uint.MaxValue)
            {
                var label = localization.ResolveByFieldTypeName("InventoryKey", (uint)ik);
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
                return $"InventoryKey {(uint)ik}";
            }
        }
        return $"Bag {bagIdx}";
    }

    private static bool TryParseScalarUInt(string formatted, out ulong value)
    {
        value = 0;
        if (!ScalarFieldEditing.TryParse(formatted, out var rawText, out var tag))
        {
            return false;
        }
        if (tag is not ("u8" or "u16" or "u32" or "u64"))
        {
            return false;
        }
        return ulong.TryParse(rawText, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out value);
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var needle = SearchText;
        var unfiltered = string.IsNullOrWhiteSpace(needle);
        foreach (var row in _allRows)
        {
            if (unfiltered
                || row.BagLabel.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || row.ItemNameEnglish.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || (row.ItemNameSecondary is not null
                    && row.ItemNameSecondary.Contains(needle!, StringComparison.OrdinalIgnoreCase))
                || row.ItemKeyText.Contains(needle!, StringComparison.Ordinal))
            {
                Items.Add(row);
            }
        }
        OnPropertyChanged(nameof(ResultsCountText));
    }
}

/// <summary>
/// One dyed-item row in the master Dye editor dialog. Carries the
/// descent path (block + first/second-step indices + dye-list field)
/// that the per-item child editor needs to address individual scalars
/// inside this item's dye list. The first/second-step pair encodes
/// either an inventory descent
/// <c>(_inventorylist, bagIdx) / (_itemList, itemIdx)</c> or an
/// equipment descent
/// <c>(_list, slotIdx) / (_item, 0)</c> uniformly — the path-addressed
/// ABI consumes both the same way.
/// </summary>
public sealed partial class DyeEditorItemRow : ObservableObject
{
    public DyeEditorItemRow(
        int blockIndex,
        uint firstStepFieldIndex,
        uint firstStepElementIndex,
        uint secondStepFieldIndex,
        uint secondStepElementIndex,
        uint dyeListFieldIndex,
        int dyeSlotCount,
        string bagLabel,
        uint itemKey,
        string itemNameEnglish,
        string? itemNameSecondary)
    {
        BlockIndex = blockIndex;
        FirstStepFieldIndex = firstStepFieldIndex;
        FirstStepElementIndex = firstStepElementIndex;
        SecondStepFieldIndex = secondStepFieldIndex;
        SecondStepElementIndex = secondStepElementIndex;
        DyeListFieldIndex = dyeListFieldIndex;
        DyeSlotCount = dyeSlotCount;
        BagLabel = bagLabel;
        ItemKey = itemKey;
        ItemNameEnglish = itemNameEnglish;
        ItemNameSecondary = itemNameSecondary;
        ItemKeyText = itemKey.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Top-level block index (InventorySaveData or EquipmentSaveData).</summary>
    public int BlockIndex { get; }

    /// <summary>
    /// First descent step's <b>field index</b>.
    /// Inventory: <c>_inventorylist</c>. Equipped: <c>_list</c>.
    /// </summary>
    public uint FirstStepFieldIndex { get; }

    /// <summary>
    /// First descent step's <b>element index</b>.
    /// Inventory: bag index. Equipped: equip-slot index (0..17 in 1.07).
    /// </summary>
    public uint FirstStepElementIndex { get; }

    /// <summary>
    /// Second descent step's <b>field index</b>.
    /// Inventory: <c>_itemList</c>. Equipped: <c>_item</c> locator.
    /// </summary>
    public uint SecondStepFieldIndex { get; }

    /// <summary>
    /// Second descent step's <b>element index</b>.
    /// Inventory: item index. Equipped: <c>0</c> (locator descent
    /// ignores it, but the slot still has to be filled).
    /// </summary>
    public uint SecondStepElementIndex { get; }

    /// <summary>Dye list field index on the resolved ItemSaveData.</summary>
    public uint DyeListFieldIndex { get; }

    public int DyeSlotCount { get; }
    public string BagLabel { get; }
    public uint ItemKey { get; }
    public string ItemNameEnglish { get; }
    public string? ItemNameSecondary { get; }
    public string ItemKeyText { get; }

    /// <summary>
    /// Build the descent path that addresses this item's
    /// <see cref="ItemSaveData"/> inside <see cref="BlockIndex"/>. The
    /// per-slot scalar writes then append one more
    /// <c>(DyeListFieldIndex, slotIdx)</c> step.
    /// </summary>
    public PathStep[] BuildPathToItem() =>
    [
        new PathStep(FirstStepFieldIndex, FirstStepElementIndex),
        new PathStep(SecondStepFieldIndex, SecondStepElementIndex),
    ];
}
