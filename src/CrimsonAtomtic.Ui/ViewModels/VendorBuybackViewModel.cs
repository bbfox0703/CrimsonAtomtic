using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the Tools → Vendor Buyback dialog. Walks the singleton
/// <c>StoreSaveData</c> block, drills into every
/// <c>StoreDataSaveData._storeSoldItemDataList</c> that's present +
/// non-empty, and surfaces one row per sold (and thus buyback-able)
/// item. Each row carries the descent path the path-addressed ABI
/// needs to remove the entry via <c>ListRemoveElement</c>.
///
/// <para>
/// <b>v1 scope</b>: <i>View + Remove from buyback</i>. The "move
/// back to inventory" half (clone the ItemSaveData into a target bag
/// + remove from buyback) is a planned follow-up — it needs target-
/// bag picking + per-bag empty-slot detection. Stack/endurance edits
/// are out-of-scope here too; the generic block editor handles those.
/// </para>
///
/// <para>
/// <b>StoreKey resolution</b>: routed through the new
/// <c>storeinfo</c> bridge (<see cref="LocalizationProvider.StoreInfo"/>).
/// 292 rows in 1.07; names are internal templates like
/// <c>"Store_Her_General"</c> — Pearl Abyss doesn't ship localized
/// store titles, so the secondary-language column intentionally echoes
/// English (same convention as QuestGauge / Skill).
/// </para>
/// </summary>
public sealed partial class VendorBuybackViewModel : ObservableObject
{
    /// <summary>Top-level class carrying the per-store buyback lists.</summary>
    private const string StoreSaveDataClass = "StoreSaveData";

    /// <summary>Field name of the object-list of stores on the singleton.</summary>
    private const string StoreListFieldName = "_storeDataList";

    /// <summary>Field name of the per-store buyback object-list.</summary>
    private const string BuybackListFieldName = "_storeSoldItemDataList";

    /// <summary>Per-store scalar identifying which store this is.</summary>
    private const string StoreKeyFieldName = "_storeKey";

    /// <summary>Per-item scalars on each <c>ItemSaveData</c> element.</summary>
    private const string ItemKeyFieldName    = "_itemKey";
    private const string StackCountFieldName = "_stackCount";
    private const string EnduranceFieldName  = "_endurance";
    private const string TimePushedFieldName = "_timeWhenPushItem";

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly ChangeJournal _journal;
    private readonly string _savePath;
    private readonly List<VendorBuybackRow> _allItems = new();

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Filtered view bound to the DataGrid.</summary>
    public ObservableCollection<VendorBuybackRow> Items { get; } = new();

    /// <summary>
    /// Live filter — matches store name (parent-identity pass shows
    /// all items in matched stores), or item name / item key / store
    /// key (narrow per-row matches). Same two-pass shape as the
    /// Sockets editor v2 filter.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterCountText))]
    private string? _searchText;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    public string FilterCountText
    {
        get
        {
            var total = _allItems.Count;
            if (total == 0) return string.Empty;
            if (string.IsNullOrWhiteSpace(SearchText)) return $"{total} sold item(s).";
            return $"{Items.Count} of {total} sold item(s) match.";
        }
    }

    /// <summary>
    /// Set once the first successful <c>Remove</c> lands; the host
    /// MainWindow reads it on dialog close to flip its own dirty flag
    /// so the title-bar * shows + File → Save persists.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Raised when a row's Jump-to button fires. The hosting
    /// MainWindow subscribes, closes the buyback dialog, and asks the
    /// main VM to navigate to this specific
    /// <c>StoreSaveData._storeDataList[storeIdx]._storeSoldItemDataList[itemIdx]</c>
    /// ItemSaveData so the generic block editor can drive
    /// stack / endurance / sockets / dye edits.
    /// </summary>
    public event Action<VendorBuybackRow>? JumpToItemRequested;

    /// <summary>Invoked by <see cref="VendorBuybackRow.Jump"/>.</summary>
    internal void RequestJump(VendorBuybackRow row) =>
        JumpToItemRequested?.Invoke(row);

    private VendorBuybackViewModel(
        ISaveLoader loader, LocalizationProvider localization,
        ChangeJournal journal, string savePath)
    {
        _loader = loader;
        _localization = localization;
        _journal = journal;
        _savePath = savePath;
    }

    /// <summary>
    /// Build a fresh VM against the loaded save. Returns <c>null</c>
    /// when no store carries a non-empty buyback list — caller
    /// surfaces an alert rather than opening an empty window.
    /// </summary>
    public static VendorBuybackViewModel? TryCreate(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        var vm = new VendorBuybackViewModel(loader, localization, journal, savePath);
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, StoreSaveDataClass, StringComparison.Ordinal))
            {
                continue;
            }
            BlockDetails top;
            try
            {
                top = loader.LoadBlockDetails(savePath, b.Index);
            }
            catch (CrimsonSaveException)
            {
                continue;
            }
            vm.CollectFromStoreBlock(top, b.Index);
        }
        if (vm._allItems.Count == 0)
        {
            return null;
        }
        // Sort by store name then by item name for predictable order.
        vm._allItems.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.StoreName, b.StoreName);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.ItemNameEnglish, b.ItemNameEnglish);
            return c != 0 ? c : a.BuybackElementIdx.CompareTo(b.BuybackElementIdx);
        });
        vm.ApplyFilter();
        var distinctStores = new HashSet<uint>();
        foreach (var r in vm._allItems) distinctStores.Add(r.StoreKey);
        vm.StatusMessage =
            $"{vm._allItems.Count} sold item(s) across {distinctStores.Count} store(s).";
        return vm;
    }

    private void CollectFromStoreBlock(BlockDetails top, int blockIdx)
    {
        // Find _storeDataList (object_list of StoreDataSaveData).
        DecodedFieldRow? storeListField = null;
        foreach (var f in top.Fields)
        {
            if (string.Equals(f.Name, StoreListFieldName, StringComparison.Ordinal))
            {
                storeListField = f;
                break;
            }
        }
        if (storeListField?.Elements is not { Count: > 0 } stores)
        {
            return;
        }
        var storeListFieldIdx = (uint)storeListField.FieldIndex;
        for (var storeIdx = 0; storeIdx < stores.Count; storeIdx++)
        {
            var store = stores[storeIdx];
            // Scan the StoreDataSaveData element's scalar + list fields.
            uint storeKey = 0;
            DecodedFieldRow? buybackListField = null;
            foreach (var f in store.Fields)
            {
                if (string.Equals(f.Name, StoreKeyFieldName, StringComparison.Ordinal)
                    && f.Present
                    && TryParseScalarUInt(f.Value, out var sk)
                    && sk <= uint.MaxValue)
                {
                    storeKey = (uint)sk;
                }
                else if (string.Equals(f.Name, BuybackListFieldName, StringComparison.Ordinal))
                {
                    buybackListField = f;
                }
            }
            if (buybackListField is null
                || !buybackListField.Present
                || buybackListField.Elements is not { Count: > 0 } sold)
            {
                continue;
            }
            var buybackFieldIdx = (uint)buybackListField.FieldIndex;
            var storeName = ResolveStoreName(storeKey);
            for (var itemIdx = 0; itemIdx < sold.Count; itemIdx++)
            {
                var item = sold[itemIdx];
                uint itemKey = 0;
                ulong stackCount = 0;
                ulong endurance = 0;
                ulong soldAt = 0;
                foreach (var f in item.Fields)
                {
                    if (!f.Present) continue;
                    if (string.Equals(f.Name, ItemKeyFieldName, StringComparison.Ordinal)
                        && TryParseScalarUInt(f.Value, out var ik) && ik <= uint.MaxValue)
                    {
                        itemKey = (uint)ik;
                    }
                    else if (string.Equals(f.Name, StackCountFieldName, StringComparison.Ordinal)
                             && TryParseScalarUInt(f.Value, out var sc))
                    {
                        stackCount = sc;
                    }
                    else if (string.Equals(f.Name, EnduranceFieldName, StringComparison.Ordinal)
                             && TryParseScalarUInt(f.Value, out var ed))
                    {
                        endurance = ed;
                    }
                    else if (string.Equals(f.Name, TimePushedFieldName, StringComparison.Ordinal)
                             && TryParseScalarUInt(f.Value, out var ta))
                    {
                        soldAt = ta;
                    }
                }
                var (nameEn, nameSec) = ResolveItemNames(itemKey);
                var row = new VendorBuybackRow(
                    parent: this,
                    blockIndex: blockIdx,
                    storeListFieldIdx: storeListFieldIdx,
                    storeElementIdx: (uint)storeIdx,
                    buybackListFieldIdx: buybackFieldIdx,
                    buybackElementIdx: (uint)itemIdx,
                    storeKey: storeKey,
                    storeName: storeName,
                    itemKey: itemKey,
                    itemNameEnglish: nameEn,
                    itemNameSecondary: nameSec,
                    stackCount: stackCount,
                    endurance: endurance,
                    soldAtTicks: soldAt);
                _allItems.Add(row);
            }
        }
    }

    private string ResolveStoreName(uint storeKey)
    {
        var name = _localization.StoreInfo?.LookupStringKey(storeKey);
        return string.IsNullOrEmpty(name)
            ? $"Store {storeKey}"
            : name;
    }

    private (string English, string? Secondary) ResolveItemNames(uint itemKey)
    {
        var en = _localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                 ?? _localization.ItemInfoStringKey(itemKey)
                 ?? itemKey.ToString(CultureInfo.InvariantCulture);
        var secondaryLang = _localization.SecondaryLanguage;
        string? sec = string.IsNullOrEmpty(secondaryLang)
            ? null
            : _localization.LookupItemName(itemKey, secondaryLang);
        return (en, sec);
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var needle = SearchText;
        if (string.IsNullOrWhiteSpace(needle))
        {
            foreach (var r in _allItems) Items.Add(r);
            OnPropertyChanged(nameof(FilterCountText));
            return;
        }

        // Two-pass: store-identity match expands to every row in that
        // store; per-row gem/item/key match shows only the matching row.
        var matchedStores = new HashSet<(int Block, uint StoreElement)>();
        foreach (var r in _allItems)
        {
            if (r.StoreName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.StoreKey.ToString(CultureInfo.InvariantCulture)
                    .Contains(needle, StringComparison.Ordinal))
            {
                matchedStores.Add((r.BlockIndex, r.StoreElementIdx));
            }
        }
        foreach (var r in _allItems)
        {
            if (matchedStores.Contains((r.BlockIndex, r.StoreElementIdx))
                || r.MatchesItemFilter(needle))
            {
                Items.Add(r);
            }
        }
        OnPropertyChanged(nameof(FilterCountText));
    }

    /// <summary>
    /// Apply a Remove on <paramref name="row"/>. Issues one
    /// <c>ListRemoveElement</c> against the per-store buyback list;
    /// after success, decrements the in-memory
    /// <see cref="VendorBuybackRow.BuybackElementIdx"/> of every
    /// remaining row in the same store whose index sat ABOVE the
    /// removed one (list-shift contract — without this, a follow-up
    /// remove on a sibling would target the wrong element).
    /// </summary>
    internal void ApplyRemove(VendorBuybackRow row)
    {
        var path = new[] { new PathStep(row.StoreListFieldIdx, row.StoreElementIdx) };
        try
        {
            _loader.ListRemoveElement(
                row.BlockIndex, path,
                (int)row.BuybackListFieldIdx,
                (int)row.BuybackElementIdx);
        }
        catch (CrimsonSaveException ex)
        {
            row.LastError = $"{ex.Message} (code {ex.ErrorCode})";
            StatusMessage = $"Remove failed: {ex.Message}";
            return;
        }
        // Shift down every sibling at index > removed.
        var sameStoreSiblings = new List<VendorBuybackRow>();
        var removedIdx = row.BuybackElementIdx;
        foreach (var r in _allItems)
        {
            if (r != row
                && r.BlockIndex == row.BlockIndex
                && r.StoreElementIdx == row.StoreElementIdx
                && r.BuybackElementIdx > removedIdx)
            {
                sameStoreSiblings.Add(r);
            }
        }
        foreach (var sib in sameStoreSiblings)
        {
            sib.BuybackElementIdx -= 1;
        }
        // Drop the removed row from both snapshot + visible list.
        _allItems.Remove(row);
        Items.Remove(row);

        IsDirty = true;
        _journal.Log("Vendor Buyback",
            $"Removed {row.ItemNameEnglish} ×{row.StackCount} from {row.StoreName} buyback");
        StatusMessage =
            $"Removed {row.ItemNameEnglish} ×{row.StackCount} from {row.StoreName} buyback.";
        OnPropertyChanged(nameof(FilterCountText));
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
}

/// <summary>
/// One sold-item row in the Vendor Buyback dialog. Carries the
/// descent indices needed to address its
/// <c>_storeSoldItemDataList</c> element for removal.
/// </summary>
public sealed partial class VendorBuybackRow : ObservableObject
{
    private readonly VendorBuybackViewModel _parent;

    public VendorBuybackRow(
        VendorBuybackViewModel parent,
        int blockIndex,
        uint storeListFieldIdx,
        uint storeElementIdx,
        uint buybackListFieldIdx,
        uint buybackElementIdx,
        uint storeKey,
        string storeName,
        uint itemKey,
        string itemNameEnglish,
        string? itemNameSecondary,
        ulong stackCount,
        ulong endurance,
        ulong soldAtTicks)
    {
        _parent = parent;
        BlockIndex = blockIndex;
        StoreListFieldIdx = storeListFieldIdx;
        StoreElementIdx = storeElementIdx;
        BuybackListFieldIdx = buybackListFieldIdx;
        _buybackElementIdx = buybackElementIdx;
        StoreKey = storeKey;
        StoreName = storeName;
        ItemKey = itemKey;
        ItemNameEnglish = itemNameEnglish;
        ItemNameSecondary = itemNameSecondary;
        ItemName = string.IsNullOrEmpty(itemNameSecondary)
            ? itemNameEnglish
            : $"{itemNameEnglish} / {itemNameSecondary}";
        ItemKeyText = itemKey.ToString(CultureInfo.InvariantCulture);
        StackCount = stackCount;
        Endurance = endurance;
        SoldAtTicks = soldAtTicks;
    }

    /// <summary>Top-level StoreSaveData block index.</summary>
    public int BlockIndex { get; }

    /// <summary>Field index of <c>_storeDataList</c> on StoreSaveData.</summary>
    public uint StoreListFieldIdx { get; }

    /// <summary>Element index of this row's store inside <c>_storeDataList</c>.</summary>
    public uint StoreElementIdx { get; }

    /// <summary>Field index of <c>_storeSoldItemDataList</c> on StoreDataSaveData.</summary>
    public uint BuybackListFieldIdx { get; }

    /// <summary>
    /// Element index of this row inside the per-store
    /// <c>_storeSoldItemDataList</c>. <b>Mutable</b>: a sibling Remove
    /// in the same store shifts this row down by 1 when its old
    /// position sat above the removed one.
    /// </summary>
    public uint BuybackElementIdx
    {
        get => _buybackElementIdx;
        internal set => SetProperty(ref _buybackElementIdx, value);
    }
    private uint _buybackElementIdx;

    public uint StoreKey { get; }
    public string StoreName { get; }
    public uint ItemKey { get; }
    public string ItemNameEnglish { get; }
    public string? ItemNameSecondary { get; }
    public string ItemName { get; }
    public string ItemKeyText { get; }
    public ulong StackCount { get; }
    public ulong Endurance { get; }
    public ulong SoldAtTicks { get; }

    [ObservableProperty]
    private string? _lastError;

    /// <summary>
    /// True when <paramref name="needle"/> matches any of the row's
    /// substantive item-level identifying fields (English / secondary
    /// item name, item key). Store-level identity (store name / store
    /// key) is handled separately by the VM's two-pass filter.
    /// </summary>
    public bool MatchesItemFilter(string needle)
    {
        if (ItemNameEnglish.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (ItemNameSecondary is not null
            && ItemNameSecondary.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (ItemKeyText.Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Remove() => _parent.ApplyRemove(this);

    /// <summary>
    /// Jump to this sold item's ItemSaveData in the main window's
    /// block tree. Closes the buyback dialog (handled by host) and
    /// builds the nav stack down to
    /// <c>StoreSaveData → _storeDataList[storeIdx] → _storeSoldItemDataList[itemIdx]</c>
    /// so the user can use the generic block editor to mutate
    /// stack / endurance / sockets / dye exactly like an inventory
    /// item. No dialog-local editor needed.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Jump() => _parent.RequestJump(this);
}
