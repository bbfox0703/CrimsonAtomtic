using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        : this(localization, allowedStringKeyPrefixes: null)
    {
    }

    /// <summary>
    /// Filtered constructor — passes a list of string-key prefixes that
    /// gate which iteminfo entries land in <see cref="_allRows"/>.
    /// When non-null and non-empty, only items whose
    /// <c>StringKey.StartsWith(prefix, OrdinalIgnoreCase)</c> for at
    /// least one prefix are eligible. Used by Sockets editor to scope
    /// the picker to gems (<c>Item_Stat_AbyssGear_</c> /
    /// <c>Item_Skill_AbyssGear_</c>).
    /// </summary>
    public ItemPickerViewModel(
        LocalizationProvider localization,
        IReadOnlyList<string>? allowedStringKeyPrefixes)
    {
        ArgumentNullException.ThrowIfNull(localization);
        SecondaryLanguage = localization.SecondaryLanguage;
        var prefixes = allowedStringKeyPrefixes is { Count: > 0 } p ? p : null;

        _allRows = new List<ItemPickerRow>(localization.ItemCount);
        for (var i = 0; i < localization.ItemCount; i++)
        {
            var entry = localization.GetItem(i);
            if (entry is not { } e)
            {
                continue;
            }
            if (prefixes is not null && !MatchesAnyPrefix(e.StringKey, prefixes))
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

    private static bool MatchesAnyPrefix(string stringKey, IReadOnlyList<string> prefixes)
    {
        foreach (var p in prefixes)
        {
            if (stringKey.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// User's currently-active secondary language, captured at dialog
    /// open. Null when only English is loaded. Changing the secondary
    /// after this dialog is open won't refresh the cached rows — close
    /// and reopen.
    /// </summary>
    public string? SecondaryLanguage { get; }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLanguage);

    /// <summary>
    /// Caller-overridable label on the per-row "action" button. Default
    /// is the original "+ Bag" Add-to-bag wording so existing call sites
    /// stay correct without change. The Sockets editor overrides this
    /// to <c>"Pick"</c> when the picker is acting as a gem selector.
    /// </summary>
    public string ActionButtonLabel { get; init; } = "+ Bag";

    /// <summary>
    /// Caller-overridable tooltip on the action button. Mirrors
    /// <see cref="ActionButtonLabel"/>; defaults to the
    /// <c>ItemPickerAddToBagTip</c> static-resource shape so existing
    /// behaviour is preserved.
    /// </summary>
    public string? ActionButtonTooltip { get; init; }

    public string SecondaryNameHeader =>
        HasSecondary ? $"Name ({SecondaryLanguage})" : "Name (secondary)";

    /// <summary>
    /// When true the picker shows a single prominent top action bar
    /// (driven by <see cref="SelectedRow"/> + <see cref="SourceName"/>)
    /// and hides the per-row "+ Bag" action button. This is the unified
    /// Add-Item flow used by Tools → Browse Items and the per-row
    /// "Add Item…" button. The Sockets gem-picker leaves this at the
    /// default <c>false</c> so it keeps its per-row "Pick" button.
    /// </summary>
    public bool ShowTopActionBar { get; init; }

    /// <summary>Inverse of <see cref="ShowTopActionBar"/> — drives the
    /// per-row action button's visibility from XAML.</summary>
    public bool ShowPerRowAction => !ShowTopActionBar;

    /// <summary>
    /// The DataGrid's selected row in top-action-bar mode — the item the
    /// "Add" button will add. Two-way bound from the view.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddActionText))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
    private ItemPickerRow? _selectedRow;

    /// <summary>
    /// Live clone-source display name pushed in by the main window (e.g.
    /// <c>"Bullet / 子彈"</c>), or null for the first-row fallback. The
    /// localized action phrase is composed from this name, so word order
    /// stays per-language. Updates as the user reselects inventory rows
    /// while the picker is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddActionText))]
    private string? _sourceName;

    /// <summary>
    /// Whether the current main-window context can accept an added item
    /// (the nav top is an <c>_itemList</c>). Pushed in by the main window;
    /// gates <see cref="AddSelectedCommand"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddActionText))]
    [NotifyPropertyChangedFor(nameof(TopActionHint))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
    private bool _canAddToTarget;

    /// <summary>
    /// Resolve a localized UI string by resource key (null when absent).
    /// Mirrors the helper in MainWindowViewModel / FindItemsViewModel.
    /// </summary>
    private static string? LookupUiResourceString(string key)
        => Avalonia.Application.Current?.TryGetResource(key, null, out var v) == true && v is string s
            ? s
            : null;

    /// <summary>
    /// Composed label for the top action button: names the picked item
    /// and (when there's a valid clone source) where it comes from. Built
    /// from localized format strings so en/zh/ja each control word order;
    /// the item label itself carries the secondary-language name.
    /// </summary>
    public string AddActionText
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return LookupUiResourceString("ItemPickerSelectPrompt")
                    ?? "Select an item below, then Add";
            }
            return CanAddToTarget && !string.IsNullOrEmpty(SourceName)
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    LookupUiResourceString("ItemPickerAddWithSource") ?? "+ Add \"{0}\" from \"{1}\"",
                    row.DisplayLabel, SourceName)
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    LookupUiResourceString("ItemPickerAddNoSource") ?? "+ Add \"{0}\"",
                    row.DisplayLabel);
        }
    }

    /// <summary>Hint shown under the bar when adding is currently blocked.</summary>
    public string? TopActionHint =>
        CanAddToTarget
            ? null
            : LookupUiResourceString("ItemPickerOpenBagHint")
              ?? "Open a bag (drill into an item list) and pick a row to clone from.";

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

    /// <summary>
    /// Raised when the user clicks the "+ Bag" button on a row,
    /// asking the main window to add that item to the currently-
    /// displayed inventory list (clone + patch _itemKey, PR B.4.2).
    ///
    /// MainWindow's <c>OnBrowseItemsClick</c> subscribes a handler
    /// that delegates to
    /// <c>MainWindowViewModel.AddItemToCurrentListAsync</c>; the
    /// picker itself stays decoupled from the main VM so it can be
    /// shown standalone (read-only) by other call sites.
    /// </summary>
    public event Action<uint>? AddItemRequested;

    /// <summary>
    /// Invoked by the code-behind click handler when the user clicks
    /// the "+ Bag" button. Fires <see cref="AddItemRequested"/>. Public
    /// so the AXAML.cs in this assembly can call it without reaching
    /// into private state.
    /// </summary>
    public void RequestAddItem(uint itemKey) => AddItemRequested?.Invoke(itemKey);

    private bool CanAddSelected => SelectedRow is not null && CanAddToTarget;

    /// <summary>
    /// Top-action-bar "Add" command: fires <see cref="AddItemRequested"/>
    /// for the currently-selected row. Enabled only when a row is picked
    /// and the main window reports a valid target.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddSelected))]
    private void AddSelected()
    {
        if (SelectedRow is { } row)
        {
            RequestAddItem(row.ItemKey);
        }
    }

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
    string? NameSecondary)
{
    /// <summary>
    /// Combined human label for the top action bar:
    /// <c>"English / Secondary"</c> when a secondary name is loaded, else
    /// just the English name. Mirrors the "Bullet / 子彈" shape the
    /// inventory grid's ResolvedName uses.
    /// </summary>
    public string DisplayLabel =>
        string.IsNullOrEmpty(NameSecondary) ? NameEnglish : $"{NameEnglish} / {NameSecondary}";
}
