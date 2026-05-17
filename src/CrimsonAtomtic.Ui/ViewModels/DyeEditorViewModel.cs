using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the master "Edit Item Dyes" dialog. Lists every item with
/// a non-empty <c>_itemDyeDataList</c> across all five container
/// kinds (active equip / reserve / inventory / mercenary equip /
/// mercenary inventory) via
/// <see cref="ISaveLoader.ListAllItems"/>. The per-row Edit button
/// raises <see cref="EditRequested"/>; the hosting MainWindow
/// code-behind opens the per-item slot editor
/// (<see cref="DyeSlotEditorViewModel"/>) in response.
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
        // The scan walks every inventory + equipment block and can run
        // ~1-2 seconds on a large save. Deliberately don't kick it off
        // here — the host calls RefreshAsync after Show() so the dialog
        // paints first with "Loading dyed items…" in the footer (driven
        // by IsLoading via ResultsCountText) instead of freezing for a
        // second.
        IsLoading = true;
    }

    public string? SecondaryLanguage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    /// <summary>
    /// True while <see cref="RefreshAsync"/> is running. Drives the
    /// "Loading dyed items…" status message in
    /// <see cref="ResultsCountText"/> so the dialog has a visible
    /// indicator during the ~1-2s background scan instead of looking
    /// frozen with an empty grid.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private bool _isLoading;

    public ObservableCollection<DyeEditorItemRow> Items { get; } = [];

    public int TotalDyedItems => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (IsLoading)
            {
                return "Loading dyed items…";
            }
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
    /// Raised when the user clicks a row's "+ Add" button on an
    /// un-dyed but dye-eligible item. The hosting MainWindow handles
    /// the confirm dialog + <see cref="ISaveLoader.SetObjectListPresent"/>
    /// call + refresh.
    /// </summary>
    public event Action<DyeEditorItemRow>? AddDyeRequested;

    public void RequestAddDye(DyeEditorItemRow row) => AddDyeRequested?.Invoke(row);

    /// <summary>
    /// Called by the host after a child editor applied at least one
    /// edit, so the master VM can mark itself dirty. (The child VM
    /// owns the actual writes.)
    /// </summary>
    public void NotifyChildApplied() => IsDirty = true;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    /// <summary>
    /// Async refresh: runs the per-block walk on the thread pool and
    /// republishes the results back on the UI thread. Drives
    /// <see cref="IsLoading"/> for the "Loading dyed items…" footer.
    /// Safe to call multiple times — every call replaces
    /// <see cref="_allRows"/> atomically.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await Task.Run(() =>
                ScanForDyedItems(_loader, _localization, _savePath));
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
    ///     set, all container kinds) — row carries Edit action.</item>
    ///   <item><b>Un-dyed equipped items</b>
    ///     (<see cref="ContainerKind.ActiveEquip"/> /
    ///     <see cref="ContainerKind.ActiveUseReserve"/> /
    ///     <see cref="ContainerKind.MercenaryEquip"/>) whose
    ///     <c>_itemDyeDataList</c> field exists in schema but is
    ///     absent — row carries "+ Add" action that materializes a
    ///     default empty dye via
    ///     <see cref="ISaveLoader.SetObjectListPresent"/>.</item>
    /// </list>
    /// Inventory bags' un-dyed items are intentionally skipped —
    /// players dye worn gear, not stockpiled materials, and including
    /// the full inventory would flood the list with hundreds of
    /// non-dyeable consumables.
    /// </summary>
    /// <remarks>
    /// IS_PLAYER_OWNED widening (mounts whose <c>_ownedCharacterKey</c>
    /// is absent — slot103 Tiuta_kliff + Stefano) goes through
    /// <see cref="LocalizationProvider.IsPlayerEditableItem"/>; NPC
    /// followers get filtered out by the strict flag.
    /// </remarks>
    private static List<DyeEditorItemRow> ScanForDyedItems(
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
        // Sort: dyed rows first (most actionable), then un-dyed; within
        // each group by source label then item name.
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

    /// <summary>
    /// Descend a 2-step path from <paramref name="top"/> to the inner
    /// <c>ItemSaveData</c> the record points at. Step 0 is always
    /// <c>ObjectList</c> (bag list / mercenary list / equip list);
    /// step 1 is <c>ObjectList</c> (item list) for inventory /
    /// mercenary kinds and <c>ObjectLocator</c> (locator → child) for
    /// active equip / reserve kinds. Returns null if either step
    /// can't navigate (shouldn't happen if the record came from
    /// <see cref="ISaveLoader.ListAllItems"/>, but defensive against
    /// snapshot staleness).
    /// </summary>
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
        // Active equip / reserve use a locator descent (element_idx ignored);
        // inventory / mercenary use an object-list descent.
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
            // Item's schema lacks the dye field entirely (some non-equipment
            // ItemSaveData variants) — can't add dye even via the toggle ABI.
            return;
        }
        var isDyed = dyeListField.Present
            && dyeListField.Elements is { Count: > 0 };
        var dyeSlots = isDyed ? dyeListField.Elements! : null;
        var nameEn = localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                     ?? localization.ItemInfoStringKey(itemKey)
                     ?? itemKey.ToString(CultureInfo.InvariantCulture);
        var secondaryLang = localization.SecondaryLanguage;
        string? nameSecondary = secondaryLang is null
            ? null
            : localization.LookupItemName(itemKey, secondaryLang);
        // Pre-format the combined "English / Secondary" display string so
        // the AXAML Item column can bind to a single property — matches
        // the Sockets editor's ItemName / ItemNameEnglish / ItemNameSecondary
        // pattern (the per-language fields stay for filter-matching;
        // ItemName drives the rendered cell).
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
        BagLabel = bagLabel;
        ItemKey = itemKey;
        ItemNameEnglish = itemNameEnglish;
        ItemNameSecondary = itemNameSecondary;
        ItemName = itemNameDisplay;
        ItemKeyText = itemKey.ToString(CultureInfo.InvariantCulture);
        CanAddDye = canAddDye;
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
    /// <summary>
    /// Pre-formatted "English / Secondary" display string the AXAML's
    /// Item column binds to. Falls back to English-only when no
    /// secondary language is configured or the secondary lookup misses.
    /// Filter substring matching still routes through the per-language
    /// <see cref="ItemNameEnglish"/> + <see cref="ItemNameSecondary"/>
    /// (see <see cref="DyeEditorViewModel.ApplyFilter"/>).
    /// </summary>
    public string ItemName { get; }
    public string ItemKeyText { get; }

    /// <summary>
    /// True when this row represents an un-dyed but dye-eligible item.
    /// Drives the AXAML to render an "Add" button instead of the
    /// "Edit" button. The Add action calls
    /// <see cref="ISaveLoader.SetObjectListPresent"/> to materialize
    /// a default empty dye element; the master VM then re-scans so
    /// the row flips to <c>CanAddDye=false</c> + slot count 1.
    /// </summary>
    public bool CanAddDye { get; }

    /// <summary>True when this row carries an editable dye list (Edit button).</summary>
    public bool CanEditDye => !CanAddDye && DyeSlotCount > 0;

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
