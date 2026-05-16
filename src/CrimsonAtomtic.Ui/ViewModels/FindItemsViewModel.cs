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

    public string? SecondaryLanguage { get; }
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

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
/// every render pass.
/// </summary>
public sealed record FindItemsRow(
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
    bool IsNewMark)
{
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
        return new FindItemsRow(
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
            IsNewMark: rec.IsNewMark);
    }
}
