using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the standalone "Browse items" dialog. Joins the iteminfo
/// bridge (numeric <c>ItemKey</c> ↔ internal <c>string_key</c>) with
/// each loaded PALOC catalog's localized name, so a single search
/// surfaces every known item with its key, internal id, and one or
/// two display names.
///
/// Purpose: when the user wants to edit a save's <c>_itemKey</c> field
/// to a specific item, they need to know the numeric key. Without this
/// dialog they'd have to know item IDs by heart or scroll Browse
/// Localization looking for a string-key match. This dialog narrows
/// the universe to the ~6,400 known items and exposes both naming
/// systems side-by-side, with copy buttons mirroring Browse
/// Localization.
/// </summary>
public sealed partial class ItemPickerViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced in <see cref="Results"/>.</summary>
    public const int MaxResults = 500;

    private readonly List<ItemPickerRow> _allRows;

    public ItemPickerViewModel(LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        SecondaryLanguage = localization.SecondaryLanguage;

        _allRows = new List<ItemPickerRow>(localization.ItemCount);
        for (var i = 0; i < localization.ItemCount; i++)
        {
            var entry = localization.GetItem(i);
            if (entry is not { } e)
            {
                continue;
            }
            // English name falls back to the iteminfo string_key when
            // PALOC has no 0x70 entry — matches the in-field
            // ResolvedName behaviour so users see consistent labels
            // between the picker and the main view.
            var nameEn = localization.LookupItemName(e.ItemKey, LocalizationProvider.DefaultLanguage)
                         ?? e.StringKey;
            var nameSecondary = SecondaryLanguage is null
                ? null
                : localization.LookupItemName(e.ItemKey, SecondaryLanguage);
            _allRows.Add(new ItemPickerRow(
                e.ItemKey,
                e.ItemKey.ToString(CultureInfo.InvariantCulture),
                e.StringKey,
                nameEn,
                nameSecondary));
        }
        // Sort once by ItemKey ascending so the unfiltered view has
        // predictable ordering (lowest keys first — Camp Funds, Pearl,
        // Gold, etc.). The DataGrid still lets the user re-sort by
        // clicking column headers.
        _allRows.Sort((a, b) => a.ItemKey.CompareTo(b.ItemKey));

        Refresh();
    }

    /// <summary>
    /// User's currently-active secondary language, captured at dialog
    /// open. Null when only English is loaded. Changing the secondary
    /// after this dialog is open won't refresh the cached rows — close
    /// and reopen.
    /// </summary>
    public string? SecondaryLanguage { get; }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    public ObservableCollection<ItemPickerRow> Results { get; } = [];

    public int TotalItems => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (TotalItems == 0)
            {
                return "Item info not loaded.";
            }
            if (string.IsNullOrEmpty(SearchText))
            {
                return $"Showing first {Results.Count:N0} of {TotalItems:N0} items — type to filter.";
            }
            return Results.Count >= MaxResults
                ? $"{Results.Count:N0}+ matches (capped) of {TotalItems:N0}."
                : $"{Results.Count:N0} matches of {TotalItems:N0}.";
        }
    }

    partial void OnSearchTextChanged(string? value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        var needle = SearchText;
        var unfiltered = string.IsNullOrWhiteSpace(needle);
        foreach (var row in _allRows)
        {
            if (unfiltered
                // Numeric search ("11" jumps straight to Camp Funds /
                // Gold etc.) — match against the decimal ItemKey,
                // anchored by Contains so the user can type partial
                // prefixes too.
                || row.ItemKeyText.Contains(needle!, StringComparison.Ordinal)
                || row.StringKey.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || row.NameEnglish.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || (row.NameSecondary is not null
                    && row.NameSecondary.Contains(needle!, StringComparison.OrdinalIgnoreCase)))
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
/// One row in the Item Picker DataGrid.
/// <see cref="ItemKey"/> is the numeric u32 the save schema stores;
/// <see cref="StringKey"/> is iteminfo's internal identifier (e.g.
/// <c>"Item_Currency_Gold"</c>); <see cref="NameEnglish"/> falls back
/// to <see cref="StringKey"/> when no PALOC entry exists, so the
/// English column always has *something* showing.
/// </summary>
public sealed record ItemPickerRow(
    uint ItemKey,
    string ItemKeyText,
    string StringKey,
    string NameEnglish,
    string? NameSecondary);
