using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the master "Edit Item Dyes" dialog. Lists both already-dyed
/// items (Edit action) and un-dyed equipped items whose schema supports
/// dye (+ Add action). Slot counts come from the
/// <c>partprefabdyeslotinfo</c> gamedata table via
/// <see cref="LocalizationProvider.LookupDyeSlotCount"/>, so the Add
/// flow can present a real slot picker instead of forcing every new
/// element onto slot 0 (the soundness bug that pulled the original
/// + Add UX back out in commit 5b107d4).
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

    private const string ItemKeyFieldName = "_itemKey";

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
        IsLoading = true;
    }

    public string? SecondaryLanguage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private bool _isLoading;

    public ObservableCollection<DyeEditorItemRow> Items { get; } = [];

    public int TotalRows => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (IsLoading) return "Loading dyeable items…";
            if (!_localization.HasDyeGamedata)
            {
                return "Dye gamedata not loaded — no game install configured. "
                    + "Material / color-group dropdowns will be unavailable.";
            }
            if (TotalRows == 0) return "No dyeable items found in this save.";
            if (string.IsNullOrEmpty(SearchText)) return $"{Items.Count:N0} item(s).";
            return $"{Items.Count:N0} matches of {TotalRows:N0}.";
        }
    }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Raised when the user clicks a row's Edit button. The hosting
    /// MainWindow subscribes and opens the per-item slot editor.
    /// </summary>
    public event Action<DyeEditorItemRow>? EditRequested;

    /// <summary>
    /// Raised when the user clicks a row's "+ Add" button (un-dyed
    /// equipped item, slot count resolvable). The hosting MainWindow
    /// drives the slot picker + the
    /// <see cref="ISaveLoader.SetObjectListPresent"/> mutation.
    /// </summary>
    public event Action<DyeEditorItemRow>? AddDyeRequested;

    public void RequestEdit(DyeEditorItemRow row) => EditRequested?.Invoke(row);
    public void RequestAddDye(DyeEditorItemRow row) => AddDyeRequested?.Invoke(row);

    public void NotifyChildApplied() => IsDirty = true;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await Task.Run(() =>
                Scan(_loader, _localization, _savePath));
            _allRows = rows;
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Scan the save for dye-relevant items via the single-FFI
    /// <see cref="ISaveLoader.ListAllItems"/> enumerator. Surfaces:
    /// <list type="bullet">
    ///   <item><b>Dyed items</b> (<see cref="ItemRecordFlags.HasDyeData"/>
    ///     set, any container kind) — Edit action.</item>
    ///   <item><b>Un-dyed equipped items</b>
    ///     (<see cref="ContainerKind.ActiveEquip"/> /
    ///     <see cref="ContainerKind.ActiveUseReserve"/> /
    ///     <see cref="ContainerKind.MercenaryEquip"/>) whose schema has
    ///     <c>_itemDyeDataList</c> AND whose <c>_itemKey</c> resolves to
    ///     a non-zero slot count via
    ///     <see cref="LocalizationProvider.LookupDyeSlotCount"/> —
    ///     + Add action.</item>
    /// </list>
    /// Inventory bags' un-dyed items are intentionally skipped — players
    /// dye worn gear, not stockpiled materials. Un-resolvable items
    /// (no partprefab → <see cref="DyeSlotCountSource.NotResolvedNoPartPrefab"/>)
    /// are skipped from the + Add list to avoid offering writes whose
    /// valid slot range is unknown.
    /// </summary>
    private static List<DyeEditorItemRow> Scan(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath)
    {
        var results = new List<DyeEditorItemRow>();
        var detailsCache = new Dictionary<uint, BlockDetails>();
        foreach (var rec in loader.ListAllItems(out _))
        {
            if (!localization.IsPlayerEditableItem(rec)) continue;
            var includeUndyed = !rec.HasDyeData && IsEquippedKind(rec.Container);
            if (!rec.HasDyeData && !includeUndyed) continue;
            if (!detailsCache.TryGetValue(rec.BlockIndex, out var top))
            {
                try
                {
                    top = loader.LoadBlockDetails(savePath, (int)rec.BlockIndex);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                detailsCache[rec.BlockIndex] = top;
            }
            var item = DescendToItem(top, rec);
            if (item is null) continue;
            TryAddRow(
                results, localization, (int)rec.BlockIndex,
                firstStepFieldIdx: rec.PathStep0Field,
                firstStepElementIdx: rec.PathStep0Element,
                secondStepFieldIdx: rec.PathStep1Field,
                secondStepElementIdx: rec.PathStep1Element,
                bagLabel: localization.FormatItemSourceLabel(rec),
                item: item);
            if (results.Count >= MaxResults) break;
        }
        // Sort: dyed rows first (most actionable), then "+ Add" rows;
        // within each group by source label then item name.
        results.Sort((a, b) =>
        {
            var byKind = a.CanAddDye.CompareTo(b.CanAddDye); // false (dyed) before true (add)
            if (byKind != 0) return byKind;
            var byBag = string.CompareOrdinal(a.BagLabel, b.BagLabel);
            return byBag != 0 ? byBag : string.CompareOrdinal(a.ItemNameEnglish, b.ItemNameEnglish);
        });
        return results;
    }

    private static bool IsEquippedKind(ContainerKind kind) => kind switch
    {
        ContainerKind.ActiveEquip => true,
        ContainerKind.ActiveUseReserve => true,
        ContainerKind.MercenaryEquip => true,
        _ => false,
    };

    private static BlockDetails? DescendToItem(BlockDetails top, ItemRecord rec)
    {
        if (rec.PathLen != 2) return null;
        var step0Field = top.Fields.FirstOrDefault(
            f => f.FieldIndex == rec.PathStep0Field);
        if (step0Field?.Elements is not { } step0Elements
            || rec.PathStep0Element >= step0Elements.Count)
        {
            return null;
        }
        var step1Host = step0Elements[(int)rec.PathStep0Element];
        var step1Field = step1Host.Fields.FirstOrDefault(
            f => f.FieldIndex == rec.PathStep1Field);
        if (step1Field is null) return null;
        if (step1Field.Child is { } locatorChild
            && step1Field.Elements is not { Count: > 0 })
        {
            return locatorChild;
        }
        if (step1Field.Elements is { } step1Elements
            && rec.PathStep1Element < step1Elements.Count)
        {
            return step1Elements[(int)rec.PathStep1Element];
        }
        return null;
    }

    private static void TryAddRow(
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
        if (dyeListField is null)
        {
            return;
        }
        var isDyed = dyeListField.Present
            && dyeListField.Elements is { Count: > 0 };
        var dyeSlots = isDyed ? dyeListField.Elements! : null;

        var gamedataSlotCount = localization.LookupDyeSlotCount(itemKey);

        // For + Add rows we *require* a resolvable slot count — without
        // it we don't know what slot numbers are safe to write. Dyed
        // rows always surface; their slot count is informational only.
        if (!isDyed && gamedataSlotCount is null or 0)
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
        var nameDisplay = string.IsNullOrEmpty(nameSecondary)
            ? nameEn
            : $"{nameEn} / {nameSecondary}";

        sink.Add(new DyeEditorItemRow(
            blockIndex: blockIndex,
            firstStepFieldIndex: firstStepFieldIdx,
            firstStepElementIndex: firstStepElementIdx,
            secondStepFieldIndex: secondStepFieldIdx,
            secondStepElementIndex: secondStepElementIdx,
            dyeListFieldIndex: (uint)dyeListField.FieldIndex,
            dyeSlotCount: dyeSlots?.Count ?? 0,
            gamedataSlotCount: gamedataSlotCount,
            bagLabel: bagLabel,
            itemKey: itemKey,
            itemNameEnglish: nameEn,
            itemNameSecondary: nameSecondary,
            itemNameDisplay: nameDisplay,
            canAddDye: !isDyed));
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
/// One row in the master Dye editor. Carries the descent path (block +
/// first/second-step indices + dye-list field) that the per-item child
/// editor needs to address individual scalars inside this item's dye
/// list, plus the slot-count info that drives both the per-row display
/// + the + Add slot picker.
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
        int? gamedataSlotCount,
        string bagLabel,
        uint itemKey,
        string itemNameEnglish,
        string? itemNameSecondary,
        string itemNameDisplay,
        bool canAddDye)
    {
        BlockIndex = blockIndex;
        FirstStepFieldIndex = firstStepFieldIndex;
        FirstStepElementIndex = firstStepElementIndex;
        SecondStepFieldIndex = secondStepFieldIndex;
        SecondStepElementIndex = secondStepElementIndex;
        DyeListFieldIndex = dyeListFieldIndex;
        DyeSlotCount = dyeSlotCount;
        GamedataSlotCount = gamedataSlotCount;
        BagLabel = bagLabel;
        ItemKey = itemKey;
        ItemNameEnglish = itemNameEnglish;
        ItemNameSecondary = itemNameSecondary;
        ItemName = itemNameDisplay;
        ItemKeyText = itemKey.ToString(CultureInfo.InvariantCulture);
        CanAddDye = canAddDye;
        CanEditDye = !canAddDye;
    }

    public int BlockIndex { get; }
    public uint FirstStepFieldIndex { get; }
    public uint FirstStepElementIndex { get; }
    public uint SecondStepFieldIndex { get; }
    public uint SecondStepElementIndex { get; }
    public uint DyeListFieldIndex { get; }
    public int DyeSlotCount { get; }
    /// <summary>
    /// Slot count from <c>partprefabdyeslotinfo</c>, or <c>null</c>
    /// when the item's prefab join didn't resolve. For dyed rows the
    /// existing per-item slot count is authoritative; this only drives
    /// the per-row display "X / Y" and the + Add slot-picker upper bound.
    /// </summary>
    public int? GamedataSlotCount { get; }
    public string BagLabel { get; }
    public uint ItemKey { get; }
    public string ItemNameEnglish { get; }
    public string? ItemNameSecondary { get; }
    public string ItemName { get; }
    public string ItemKeyText { get; }
    public bool CanAddDye { get; }
    public bool CanEditDye { get; }

    /// <summary>
    /// Display text for the Slots column. "X / Y" when both counts are
    /// known; "X" alone when the gamedata count is unresolvable;
    /// "— / Y" when the row is un-dyed (X = 0).
    /// </summary>
    public string SlotCountText
    {
        get
        {
            var current = DyeSlotCount;
            var total = GamedataSlotCount;
            if (total is null)
            {
                return current.ToString(CultureInfo.InvariantCulture);
            }
            if (current == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "— / {0}", total);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0} / {1}", current, total);
        }
    }

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
