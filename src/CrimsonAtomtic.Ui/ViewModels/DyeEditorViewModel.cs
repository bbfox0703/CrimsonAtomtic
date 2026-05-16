using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the master "Edit Item Dyes" dialog. Walks every
/// <c>InventorySaveData</c> block, finds items whose
/// <c>_itemDyeDataList</c> field is present + non-empty, and lists
/// them with one row per dyed item. The per-row Edit button raises
/// <see cref="EditRequested"/>; the hosting MainWindow code-behind
/// opens the per-item slot editor (<see cref="DyeSlotEditorViewModel"/>)
/// in response.
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

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly string _savePath;
    private List<DyeEditorItemRow> _allRows = new();

    public DyeEditorViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        _loader = loader;
        _localization = localization;
        _savePath = savePath;
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
        _allRows = ScanForDyedItems(_loader, _localization, _savePath);
        ApplyFilter();
    }

    private static List<DyeEditorItemRow> ScanForDyedItems(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath)
    {
        var results = new List<DyeEditorItemRow>();
        IReadOnlyList<InventoryItemRecord> records;
        try
        {
            records = loader.ListInventoryItems(out _);
        }
        catch (CrimsonSaveException)
        {
            return results;
        }
        // Group by block — one LoadBlockDetails per InventorySaveData
        // block, walked once. Subsequent reads of the same block hit
        // the version-validated cache (O(1) post-fetch).
        var byBlock = new Dictionary<uint, BlockDetails>();
        foreach (var rec in records)
        {
            if (!byBlock.TryGetValue(rec.BlockIndex, out var details))
            {
                try
                {
                    details = loader.LoadBlockDetails(savePath, (int)rec.BlockIndex);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                byBlock[rec.BlockIndex] = details;
            }

            // Drill InventorySaveData → _inventoryList[N] → _itemList[M].
            // list_inventory_items already gave us the indices; we just
            // walk the cached tree.
            var invListField = FindField(details.Fields, "_inventorylist");
            if (invListField?.Elements is not { Count: > 0 } containers
                || rec.InventoryElementIndex >= (uint)containers.Count)
            {
                continue;
            }
            var container = containers[(int)rec.InventoryElementIndex];
            var itemListField = FindField(container.Fields, "_itemList");
            if (itemListField?.Elements is not { Count: > 0 } items
                || rec.ItemElementIndex >= (uint)items.Count)
            {
                continue;
            }
            var item = items[(int)rec.ItemElementIndex];
            var dyeListField = FindField(item.Fields, DyeListFieldName);
            if (dyeListField is null
                || !dyeListField.Present
                || dyeListField.Elements is not { Count: > 0 } dyeSlots)
            {
                continue;
            }

            var bagLabel = localization.ResolveByFieldTypeName("InventoryKey", rec.InventoryKey);
            if (string.IsNullOrEmpty(bagLabel))
            {
                bagLabel = $"InventoryKey {rec.InventoryKey}";
            }
            var itemName =
                localization.LookupItemName(rec.ItemKey, LocalizationProvider.DefaultLanguage)
                ?? localization.ItemInfoStringKey(rec.ItemKey)
                ?? rec.ItemKey.ToString(CultureInfo.InvariantCulture);

            results.Add(new DyeEditorItemRow(
                rec,
                (uint)invListField.FieldIndex,
                (uint)itemListField.FieldIndex,
                (uint)dyeListField.FieldIndex,
                dyeSlots.Count,
                bagLabel,
                itemName));
            if (results.Count >= MaxResults)
            {
                break;
            }
        }
        // Sort by bag then by item — predictable ordering.
        results.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.BagLabel, b.BagLabel);
            return c != 0 ? c : string.CompareOrdinal(a.ItemNameEnglish, b.ItemNameEnglish);
        });
        return results;
    }

    private static DecodedFieldRow? FindField(IReadOnlyList<DecodedFieldRow> fields, string name)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.Name, name, StringComparison.Ordinal))
            {
                return f;
            }
        }
        return null;
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
/// descent path (block + 3 field indices + 2 element indices) that
/// the per-item child editor needs to address individual scalars
/// inside this item's dye list.
/// </summary>
public sealed partial class DyeEditorItemRow : ObservableObject
{
    public DyeEditorItemRow(
        InventoryItemRecord record,
        uint inventoryListFieldIndex,
        uint itemListFieldIndex,
        uint dyeListFieldIndex,
        int dyeSlotCount,
        string bagLabel,
        string itemNameEnglish)
    {
        Record = record;
        InventoryListFieldIndex = inventoryListFieldIndex;
        ItemListFieldIndex = itemListFieldIndex;
        DyeListFieldIndex = dyeListFieldIndex;
        DyeSlotCount = dyeSlotCount;
        BagLabel = bagLabel;
        ItemNameEnglish = itemNameEnglish;
        ItemKeyText = record.ItemKey.ToString(CultureInfo.InvariantCulture);
    }

    public InventoryItemRecord Record { get; }
    public uint InventoryListFieldIndex { get; }
    public uint ItemListFieldIndex { get; }
    public uint DyeListFieldIndex { get; }
    public int DyeSlotCount { get; }

    public string BagLabel { get; }
    public string ItemNameEnglish { get; }
    public string ItemKeyText { get; }
    public uint ItemKey => Record.ItemKey;

    /// <summary>
    /// Build the descent path that addresses this item's dye list
    /// inside <see cref="Record"/>.BlockIndex. The per-slot scalar
    /// writes then append one more <c>(DyeListFieldIndex, slotIdx)</c>
    /// step.
    /// </summary>
    public PathStep[] BuildPathToItem() =>
    [
        new PathStep(InventoryListFieldIndex, Record.InventoryElementIndex),
        new PathStep(ItemListFieldIndex, Record.ItemElementIndex),
    ];
}
