using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the standalone "Browse localization" dialog. Walks the
/// loaded PALOC table once into an in-memory list, then filters
/// substring matches against <see cref="SearchText"/> on every
/// keystroke. Caps results at <see cref="MaxResults"/> so the
/// DataGrid doesn't render the full 100k+ entries.
/// </summary>
public sealed partial class LocalizationSearchViewModel : ObservableObject
{
    /// <summary>Hard cap on rows surfaced in <see cref="Results"/>.</summary>
    public const int MaxResults = 500;

    private readonly LocalizationProvider _localization;
    private readonly List<LocalizationRow> _allRows;

    public LocalizationSearchViewModel(LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;

        // Eager-cache all entries on construction. The Rust side already
        // holds them in memory; copying ~100k tuples into managed
        // strings costs ~10 MB but makes substring filtering trivial.
        _allRows = new List<LocalizationRow>(_localization.EntryCount);
        for (var i = 0; i < _localization.EntryCount; i++)
        {
            var entry = _localization.GetEntry(i);
            if (entry is { } e)
            {
                _allRows.Add(new LocalizationRow(e.Key, e.Value));
            }
        }

        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsCountText))]
    private string? _searchText;

    public ObservableCollection<LocalizationRow> Results { get; } = [];

    public int TotalEntries => _allRows.Count;

    public string ResultsCountText
    {
        get
        {
            if (TotalEntries == 0)
            {
                return "Localization not loaded.";
            }
            if (string.IsNullOrEmpty(SearchText))
            {
                return $"Showing first {Results.Count:N0} of {TotalEntries:N0} entries — type to filter.";
            }
            return Results.Count >= MaxResults
                ? $"{Results.Count:N0}+ matches (capped) of {TotalEntries:N0}."
                : $"{Results.Count:N0} matches of {TotalEntries:N0}.";
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
                || row.Key.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || row.Value.Contains(needle!, StringComparison.OrdinalIgnoreCase))
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

/// <summary>One row in the browse-localization DataGrid.</summary>
public sealed record LocalizationRow(string Key, string Value);
