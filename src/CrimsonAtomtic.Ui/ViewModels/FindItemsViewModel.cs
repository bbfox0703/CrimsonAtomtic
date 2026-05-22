using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the standalone "Find Items" dialog. Cross-bag flat view of
/// every item slot in the loaded save, powered by
/// <see cref="ISaveLoader.ListInventoryItems"/> — one FFI call replaces
/// the manual 18-container × N-item nesting walk users would otherwise
/// have to do through the main blocks tree.
///
/// <para>
/// Snapshot semantics: the rows are captured at dialog open. The
/// snapshot's mutation-version is stored so the user can hit Refresh
/// after editing elsewhere to re-list. The dialog does NOT auto-refresh
/// on every mutation — that would re-render the DataGrid mid-scroll
/// every time the user fixes a typo in the edit panel.
/// </para>
///
/// <para>
/// Read-only — no Add-to-bag analog. To add items use the Browse Items
/// dialog; this view is for "I have lots of stuff, where is X?".
/// </para>
/// </summary>
public sealed partial class FindItemsViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced in <see cref="Results"/>.</summary>
    public const int MaxResults = 1000;

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private List<FindItemsRow> _allRows = new();

    /// <summary>
    /// Snapshot version captured at the last successful list call.
    /// Surfaced via <see cref="SnapshotInfoText"/> so the user can
    /// tell whether their snapshot is fresh against the current
    /// save state — see <see cref="IsStale"/>.
    /// </summary>
    private ulong _snapshotVersion;

    public FindItemsViewModel(ISaveLoader loader, LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        _loader = loader;
        _localization = localization;
        SecondaryLanguage = localization.SecondaryLanguage;
        Refresh();
    }

    /// <summary>
    /// Raised when the user clicks a row's "Go" button to navigate
    /// the main window to that exact item slot. The hosting
    /// MainWindow subscribes when it opens this dialog and routes
    /// through <c>MainWindowViewModel.NavigateToInventoryItemAsync</c>.
    /// The dialog itself stays open after the jump (the user often
    /// wants to inspect several items in sequence).
    /// </summary>
    public event Action<FindItemsRow>? GotoRequested;

    /// <summary>
    /// Invoked by the code-behind click handler when the user clicks
    /// the "Go" button. Public so the AXAML.cs in this assembly can
    /// call it without reaching into private state.
    /// </summary>
    public void RequestGoto(FindItemsRow row) => GotoRequested?.Invoke(row);

    public string? SecondaryLanguage { get; }
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    /// <summary>
    /// The row the user has highlighted in the DataGrid. Drives the
    /// detail pane on the right — when null, the pane shows a hint;
    /// when non-null, it surfaces the row's iteminfo
    /// <see cref="ItemInfoSummary"/> (static flags + scalar metadata
    /// pulled from <c>iteminfo.pabgb</c>, not from the save).
    /// </summary>
    /// <remarks>
    /// Bound to <c>DataGrid.SelectedItem</c> via TwoWay. The pane's
    /// content stays consistent across <see cref="ApplyFilter"/>
    /// re-fills because the row records are immutable — the same
    /// reference survives a filter pass unless the row genuinely drops
    /// out of the filtered set.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRowWithSummary))]
    private FindItemsRow? _selectedRow;

    /// <summary>
    /// True when the detail pane should render its iteminfo content
    /// (the selected row exists AND iteminfo carried a summary for
    /// its item key — dev-only / unmapped items return null from
    /// <see cref="LocalizationProvider.LookupItemInfoSummary"/>).
    /// </summary>
    public bool HasSelectedRowWithSummary =>
        SelectedRow is not null && SelectedRow.HasSummary;

    public ObservableCollection<FindItemsRow> Results { get; } = [];

    public int TotalSlots => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (TotalSlots == 0)
            {
                return "Loaded save has no inventory items.";
            }
            if (string.IsNullOrEmpty(SearchText))
            {
                return $"Showing first {Results.Count:N0} of {TotalSlots:N0} slots — type to filter.";
            }
            return Results.Count >= MaxResults
                ? $"{Results.Count:N0}+ matches (capped) of {TotalSlots:N0}."
                : $"{Results.Count:N0} matches of {TotalSlots:N0}.";
        }
    }

    /// <summary>
    /// Footer hint about the snapshot freshness. The actual live
    /// version check fires on demand via <see cref="IsStale"/>; this
    /// string just describes the snapshot the dialog is currently
    /// showing.
    /// </summary>
    public string SnapshotInfoText =>
        $"Snapshot v{_snapshotVersion}. Click Refresh after editing elsewhere to re-list.";

    /// <summary>
    /// True iff the loaded save has bumped its mutation version since
    /// this dialog last listed. Calls one cheap FFI (u64 read). Used
    /// by the Refresh button's CanExecute and the footer indicator.
    /// </summary>
    public bool IsStale
    {
        get
        {
            try
            {
                return _loader.GetMutationVersion() != _snapshotVersion;
            }
            catch (InvalidOperationException)
            {
                // Save was unloaded under us — nothing to refresh.
                return false;
            }
        }
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    [RelayCommand]
    private void Refresh()
    {
        IReadOnlyList<InventoryItemRecord> records;
        try
        {
            records = _loader.ListInventoryItems(out var version);
            _snapshotVersion = version;
        }
        catch (InvalidOperationException)
        {
            // No save loaded — leave the dialog in its empty state.
            _allRows = new List<FindItemsRow>();
            ApplyFilter();
            return;
        }
        _allRows = new List<FindItemsRow>(records.Count);
        foreach (var rec in records)
        {
            _allRows.Add(FindItemsRow.From(rec, _localization, SecondaryLanguage));
        }
        // Stable default order: container first, then slot — matches
        // how the in-game bag UI lays out items. The DataGrid still
        // lets the user re-sort by clicking column headers.
        _allRows.Sort((a, b) =>
        {
            var c = a.InventoryKey.CompareTo(b.InventoryKey);
            return c != 0 ? c : a.SlotNo.CompareTo(b.SlotNo);
        });
        ApplyFilter();
        OnPropertyChanged(nameof(SnapshotInfoText));
        OnPropertyChanged(nameof(IsStale));
    }

    private void ApplyFilter()
    {
        Results.Clear();
        var needle = SearchText;
        var unfiltered = string.IsNullOrWhiteSpace(needle);
        foreach (var row in _allRows)
        {
            if (unfiltered
                // Numeric search hits ItemKey + ItemNo + StackCount —
                // covers "show me everything with 42 of it" style queries.
                || row.ItemKeyText.Contains(needle!, StringComparison.Ordinal)
                || row.ItemNoText.Contains(needle!, StringComparison.Ordinal)
                || row.InventoryLabel.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || row.ItemNameEnglish.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || (row.ItemNameSecondary is not null
                    && row.ItemNameSecondary.Contains(needle!, StringComparison.OrdinalIgnoreCase)))
            {
                Results.Add(row);
                if (Results.Count >= MaxResults)
                {
                    break;
                }
            }
        }
        OnPropertyChanged(nameof(ResultsCountText));
    }
}

/// <summary>
/// One row in the Find Items DataGrid. Combines the raw
/// <see cref="InventoryItemRecord"/> fields with PALOC-resolved labels
/// (container name, English + optional secondary item name) so the
/// grid can show useful columns without each row re-resolving names on
/// every render pass. <see cref="Record"/> is retained so the "Go"
/// button can navigate to the exact descent path.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Summary"/> + <see cref="SetFlagChips"/> + iteminfo
/// scalars are populated at factory time from
/// <see cref="LocalizationProvider.LookupItemInfoSummary"/>. They are
/// <c>null</c> / empty when the item key isn't in
/// <c>iteminfo.pabgb</c> (dev-only or content-stripped items); the UI
/// hides the detail pane sections accordingly. The motivating example
/// is "why isn't <c>is_housing_only</c> visible when I look up an
/// inventory item?" — that flag lives in iteminfo, not save data, and
/// surfaces here as one of the chips in <see cref="SetFlagChips"/>.
/// </para>
/// </remarks>
public sealed record FindItemsRow(
    InventoryItemRecord Record,
    uint ItemKey,
    string ItemKeyText,
    uint InventoryKey,
    string InventoryLabel,
    string ItemNameEnglish,
    string? ItemNameSecondary,
    uint SlotNo,
    ulong StackCount,
    ulong ItemNo,
    string ItemNoText,
    bool IsLocked,
    bool IsNewMark,
    bool HasSummary,
    ItemInfoSummary Summary,
    IReadOnlyList<ItemFlagChipModel> SetFlagChips)
{
    /// <summary>
    /// Convenience getters off <see cref="Summary"/>. Returning
    /// <c>null</c> for the missing-summary case keeps the AXAML
    /// bindings simple — bind to the string, fall back to "—" via a
    /// null-coalesce in the template.
    /// </summary>
    public string? ItemTypeText =>
        HasSummary ? Summary.ItemType.ToString(CultureInfo.InvariantCulture) : null;
    public string? ItemTierText =>
        HasSummary ? Summary.ItemTier.ToString(CultureInfo.InvariantCulture) : null;
    public string? EquipableLevelText =>
        HasSummary ? Summary.EquipableLevel.ToString(CultureInfo.InvariantCulture) : null;
    public string? MaxEnduranceText =>
        HasSummary ? Summary.MaxEndurance.ToString(CultureInfo.InvariantCulture) : null;
    public string? MaxStackCountText =>
        HasSummary ? Summary.MaxStackCount.ToString(CultureInfo.InvariantCulture) : null;
    public string? CooltimeText =>
        HasSummary ? Summary.Cooltime.ToString(CultureInfo.InvariantCulture) : null;
    public string? RespawnTimeText =>
        HasSummary ? Summary.RespawnTimeSeconds.ToString(CultureInfo.InvariantCulture) : null;
    public string? CategoryInfoText =>
        HasSummary ? Summary.CategoryInfo.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// True when iteminfo says this row has a summary but the summary
    /// has no static flags set. Drives the "(no flags set)" hint in
    /// the detail pane — bound directly so we don't need a custom
    /// IValueConverter.
    /// </summary>
    public bool HasSummaryAndNoFlagsSet => HasSummary && SetFlagChips.Count == 0;

    /// <summary>
    /// Factory that joins one <see cref="InventoryItemRecord"/> with
    /// the resolved labels. Uses the same name surfaces the rest of
    /// the editor exposes (<see cref="LocalizationProvider.LookupItemName"/>
    /// + <see cref="LocalizationProvider.ResolveByFieldTypeName"/> for
    /// the InventoryKey label) so the picker stays visually consistent
    /// with the main field grid's resolved-name column.
    /// </summary>
    public static FindItemsRow From(
        InventoryItemRecord rec,
        LocalizationProvider localization,
        string? secondaryLanguage)
    {
        // Item name falls back to the iteminfo string_key when PALOC
        // misses (mirrors ResolvedName behaviour elsewhere). If
        // iteminfo itself doesn't know the key, fall back to the raw
        // decimal so the cell isn't blank.
        var nameEn = localization.LookupItemName(rec.ItemKey, LocalizationProvider.DefaultLanguage)
                     ?? localization.ItemInfoStringKey(rec.ItemKey)
                     ?? rec.ItemKey.ToString(CultureInfo.InvariantCulture);
        var nameSecondary = secondaryLanguage is null
            ? null
            : localization.LookupItemName(rec.ItemKey, secondaryLanguage);
        var inventoryLabel = localization.ResolveByFieldTypeName("InventoryKey", rec.InventoryKey);
        if (string.IsNullOrEmpty(inventoryLabel))
        {
            inventoryLabel = $"InventoryKey {rec.InventoryKey}";
        }
        // Static metadata from iteminfo. May return null for dev /
        // content-stripped items; the row carries that explicit signal
        // via HasSummary so the detail pane can render an empty hint.
        var summaryNullable = localization.LookupItemInfoSummary(rec.ItemKey);
        var summary = summaryNullable ?? default;
        var hasSummary = summaryNullable.HasValue;
        var flagChips = hasSummary
            ? ItemFlagChipModel.BuildForSetFlags(summary.Flags)
            : Array.Empty<ItemFlagChipModel>();
        return new FindItemsRow(
            Record: rec,
            ItemKey: rec.ItemKey,
            ItemKeyText: rec.ItemKey.ToString(CultureInfo.InvariantCulture),
            InventoryKey: rec.InventoryKey,
            InventoryLabel: inventoryLabel,
            ItemNameEnglish: nameEn,
            ItemNameSecondary: nameSecondary,
            SlotNo: rec.SlotNo,
            StackCount: rec.StackCount,
            ItemNo: rec.ItemNo,
            ItemNoText: rec.ItemNo.ToString(CultureInfo.InvariantCulture),
            IsLocked: rec.IsLocked,
            IsNewMark: rec.IsNewMark,
            HasSummary: hasSummary,
            Summary: summary,
            SetFlagChips: flagChips);
    }
}

/// <summary>
/// One chip surfaced in the detail pane's flag-list. Pre-resolved at
/// row-construction time so the AXAML template stays trivial — bind to
/// <see cref="Label"/> for the chip text, <see cref="Tooltip"/> for the
/// hover tooltip explaining what the flag means.
/// </summary>
public sealed record ItemFlagChipModel(string Label, string Tooltip)
{
    /// <summary>
    /// Build the chip list for whichever bits of <paramref name="flags"/>
    /// are set. The output preserves a stable display order matching
    /// the bit order in the underlying <c>u32</c> bitmask. Cleared bits
    /// don't contribute a chip — the detail pane only surfaces flags
    /// that are <i>on</i> for the selected item.
    /// </summary>
    public static IReadOnlyList<ItemFlagChipModel> BuildForSetFlags(ItemInfoFlags flags)
    {
        // Per-bit (label key, tooltip key, English fallback label,
        // English fallback tooltip). The keys live in the shipped
        // language dictionaries under ItemFlagLabel* / ItemFlagTip*.
        // Fallbacks fire when the dictionary entry is missing (e.g.
        // a future build adds a flag without all language files
        // updated yet) so the detail pane never renders a blank chip.
        var defs = FlagDefinitions;
        var chips = new List<ItemFlagChipModel>();
        foreach (var d in defs)
        {
            if ((flags & d.Bit) == 0) continue;
            var label = LookupUiResourceString(d.LabelKey) ?? d.LabelFallback;
            var tip = LookupUiResourceString(d.TipKey) ?? d.TipFallback;
            chips.Add(new ItemFlagChipModel(label, tip));
        }
        return chips;
    }

    private static string? LookupUiResourceString(string key)
    {
        if (Avalonia.Application.Current?.TryGetResource(key, null, out var v) == true
            && v is string s)
        {
            return s;
        }
        return null;
    }

    private readonly record struct FlagDef(
        ItemInfoFlags Bit,
        string LabelKey,
        string TipKey,
        string LabelFallback,
        string TipFallback);

    // Bit order matches CRIMSON_ITEMINFO_FLAG_* in
    // vendor/crimson-rs/src/c_abi/iteminfo.rs. Display order is the
    // same so the chip layout is predictable across saves.
    private static readonly FlagDef[] FlagDefinitions =
    [
        new(ItemInfoFlags.IsBlocked, "ItemFlagLabelIsBlocked", "ItemFlagTipIsBlocked",
            "Blocked", "Dev-blocked / unused entry"),
        new(ItemInfoFlags.IsDyeable, "ItemFlagLabelIsDyeable", "ItemFlagTipIsDyeable",
            "Dyeable", "Has at least one dye slot"),
        new(ItemInfoFlags.IsDestroyWhenBroken, "ItemFlagLabelIsDestroyWhenBroken", "ItemFlagTipIsDestroyWhenBroken",
            "Destroy when broken", "Removed from inventory when endurance hits 0"),
        new(ItemInfoFlags.IsHousingOnly, "ItemFlagLabelIsHousingOnly", "ItemFlagTipIsHousingOnly",
            "Housing only", "Only meaningful inside player housing"),
        new(ItemInfoFlags.IsEquipQuickSlotVisible, "ItemFlagLabelIsEquipQuickSlotVisible", "ItemFlagTipIsEquipQuickSlotVisible",
            "Quick-slot visible", "Eligible for the equip quick-slot bar (1.08+)"),
        new(ItemInfoFlags.IsImportantItem, "ItemFlagLabelIsImportantItem", "ItemFlagTipIsImportantItem",
            "Important", "Story / quest-important item"),
        new(ItemInfoFlags.IsShieldItem, "ItemFlagLabelIsShieldItem", "ItemFlagTipIsShieldItem",
            "Shield", "Equippable in the off-hand shield slot"),
        new(ItemInfoFlags.IsTowerShieldItem, "ItemFlagLabelIsTowerShieldItem", "ItemFlagTipIsTowerShieldItem",
            "Tower shield", "Tower-shield variant"),
        new(ItemInfoFlags.IsWild, "ItemFlagLabelIsWild", "ItemFlagTipIsWild",
            "Wild", "Gathered from the world rather than crafted"),
        new(ItemInfoFlags.HideFromInventoryOnPopItem, "ItemFlagLabelHideFromInventoryOnPopItem", "ItemFlagTipHideFromInventoryOnPopItem",
            "Hide on pop", "Hidden from inventory after the PopItem use action"),
        new(ItemInfoFlags.Discardable, "ItemFlagLabelDiscardable", "ItemFlagTipDiscardable",
            "Discardable", "Player is allowed to discard"),
        new(ItemInfoFlags.IsRegisterTradeMarket, "ItemFlagLabelIsRegisterTradeMarket", "ItemFlagTipIsRegisterTradeMarket",
            "Tradeable", "Eligible for the trade-market posting flow"),
        new(ItemInfoFlags.IsEditorUsable, "ItemFlagLabelIsEditorUsable", "ItemFlagTipIsEditorUsable",
            "Editor", "Reserved for in-engine editor tooling"),
        new(ItemInfoFlags.IsEditableGrime, "ItemFlagLabelIsEditableGrime", "ItemFlagTipIsEditableGrime",
            "Editable grime", "Has an editable dirt / wear overlay"),
        new(ItemInfoFlags.UseImmediately, "ItemFlagLabelUseImmediately", "ItemFlagTipUseImmediately",
            "Use immediately", "Triggers its use effect on pickup"),
        new(ItemInfoFlags.ApplyMaxStackCap, "ItemFlagLabelApplyMaxStackCap", "ItemFlagTipApplyMaxStackCap",
            "Apply stack cap", "Caps runtime stack count to max_stack_count"),
        new(ItemInfoFlags.IsBlockedStoreSell, "ItemFlagLabelIsBlockedStoreSell", "ItemFlagTipIsBlockedStoreSell",
            "No store sell", "Blocked from store sell-back"),
        new(ItemInfoFlags.IsPreorderItem, "ItemFlagLabelIsPreorderItem", "ItemFlagTipIsPreorderItem",
            "Preorder", "Granted via pre-order entitlement"),
        new(ItemInfoFlags.IsHasItemUseDataInventoryBuff, "ItemFlagLabelIsHasItemUseDataInventoryBuff", "ItemFlagTipIsHasItemUseDataInventoryBuff",
            "Inventory buff", "Carries an inventory-time buff payload"),
        new(ItemInfoFlags.IsPreservedOnExtract, "ItemFlagLabelIsPreservedOnExtract", "ItemFlagTipIsPreservedOnExtract",
            "Carry-out", "Preserved during extraction-style runs"),
        new(ItemInfoFlags.EnableAlertSystemToUi, "ItemFlagLabelEnableAlertSystemToUi", "ItemFlagTipEnableAlertSystemToUi",
            "UI alert", "Item pickup raises a UI alert"),
        new(ItemInfoFlags.IsSaveGameDataAtUseItem, "ItemFlagLabelIsSaveGameDataAtUseItem", "ItemFlagTipIsSaveGameDataAtUseItem",
            "Save on use", "Use action triggers a save write"),
        new(ItemInfoFlags.IsLogoutAtUseItem, "ItemFlagLabelIsLogoutAtUseItem", "ItemFlagTipIsLogoutAtUseItem",
            "Logout on use", "Use action ends the session"),
        new(ItemInfoFlags.EnableEquipInCloneActor, "ItemFlagLabelEnableEquipInCloneActor", "ItemFlagTipEnableEquipInCloneActor",
            "Clone-actor", "Equippable on a clone-actor"),
        new(ItemInfoFlags.CanDisassemble, "ItemFlagLabelCanDisassemble", "ItemFlagTipCanDisassemble",
            "Disassemble", "Eligible for the disassemble / salvage workflow"),
        new(ItemInfoFlags.IsAllGimmickSealable, "ItemFlagLabelIsAllGimmickSealable", "ItemFlagTipIsAllGimmickSealable",
            "Gimmick-sealable", "Sealable against all gimmick effects"),
        new(ItemInfoFlags.DeleteByGimmickUnlock, "ItemFlagLabelDeleteByGimmickUnlock", "ItemFlagTipDeleteByGimmickUnlock",
            "Auto-delete", "Auto-deleted when its bound gimmick unlocks"),
        new(ItemInfoFlags.UseDropSetTarget, "ItemFlagLabelUseDropSetTarget", "ItemFlagTipUseDropSetTarget",
            "Drop-set", "Used as a drop-set targeting key"),
    ];
}
