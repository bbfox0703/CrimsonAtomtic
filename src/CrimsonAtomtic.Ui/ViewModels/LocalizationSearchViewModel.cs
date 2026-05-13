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
///
/// When the user has picked a secondary language, both catalogs are
/// joined by key on construction so each row carries its
/// English-and-secondary pair without any per-keystroke FFI cost.
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
        SecondaryLanguage = localization.SecondaryLanguage;

        // Join the English and (optional) secondary catalogs by raw
        // string key in a single pass. PALOC entries between two
        // language files share keys (it's a localization table — the
        // whole point) but the on-disk *order* isn't guaranteed to
        // match, so we have to lookup by key rather than zipping by
        // index. ~180k lookups in the secondary catalog cost ~150 ms
        // on a debug build; acceptable for a one-time dialog open.
        var secondaryByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(SecondaryLanguage))
        {
            var count = _localization.EntryCountFor(SecondaryLanguage);
            secondaryByKey.EnsureCapacity(count);
            for (var i = 0; i < count; i++)
            {
                var e = _localization.GetEntry(i, SecondaryLanguage);
                if (e is { } se)
                {
                    secondaryByKey[se.Key] = se.Value;
                }
            }
        }

        // Eager-cache English entries on construction. The Rust side
        // already holds them in memory; copying ~180k tuples into
        // managed strings costs ~20 MB but makes substring filtering
        // trivial.
        _allRows = new List<LocalizationRow>(_localization.EntryCount);
        for (var i = 0; i < _localization.EntryCount; i++)
        {
            var entry = _localization.GetEntry(i);
            if (entry is { } e)
            {
                var sec = secondaryByKey.GetValueOrDefault(e.Key);
                _allRows.Add(new LocalizationRow(e.Key, e.Value, sec));
            }
        }

        Refresh();
    }

    /// <summary>
    /// User's currently-active secondary language code, captured at
    /// dialog-open time. Switching the secondary language while this
    /// dialog is open won't refresh the cached rows — close and
    /// reopen to see the new pairing. Null when no secondary is set.
    /// </summary>
    public string? SecondaryLanguage { get; }

    /// <summary>True when the Value₂ / Copy-V₂ columns should be shown.</summary>
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    /// <summary>Header text for the secondary-value column, e.g. "Value (zho-tw)".</summary>
    public string SecondaryValueHeader =>
        HasSecondary ? $"Value ({SecondaryLanguage})" : "Value (secondary)";

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
                || row.Value.Contains(needle!, StringComparison.OrdinalIgnoreCase)
                || (row.Value2 is not null
                    && row.Value2.Contains(needle!, StringComparison.OrdinalIgnoreCase)))
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
/// One row in the browse-localization DataGrid. <see cref="Value2"/>
/// is null when no secondary language is loaded, or when the secondary
/// catalog has no entry for this key (rare but possible — translation
/// gaps exist).
/// </summary>
public sealed record LocalizationRow(string Key, string Value, string? Value2);
