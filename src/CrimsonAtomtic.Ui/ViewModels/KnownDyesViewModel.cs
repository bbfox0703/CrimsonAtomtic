using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for Tools → Known Dyes: which dye colours the save knows, and learning
/// more. A colour is a knowledge entry (see <see cref="KnownDyeService"/>);
/// learning one also learns its theme, and optionally its dye item's
/// collection entry. The write goes through
/// <see cref="MainWindowViewModel.LearnKnowledgeAsync"/>, the same inject the
/// Knowledge editor uses. Learn only: nothing here forgets a colour.
/// </summary>
public sealed partial class KnownDyesViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private List<KnownDyeRow> _allRows = [];

    public ObservableCollection<KnownDyeRow> Rows { get; } = [];

    /// <summary>"All themes" plus each theme with its known / total count.</summary>
    public ObservableCollection<NodeFilterOption> ThemeFilters { get; } = [];

    [ObservableProperty]
    private NodeFilterOption? _selectedThemeFilter;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _showUnknownOnly;

    /// <summary>Also learn each colour's dye item collection entry (codex progress).</summary>
    [ObservableProperty]
    private bool _includeDyeItems;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _filterSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LearnSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(LearnAllShownCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnselectAllCommand))]
    private bool _isBusy;

    private KnownDyesViewModel(MainWindowViewModel main) => _main = main;

    public static async Task<KnownDyesViewModel> CreateAsync(
        MainWindowViewModel main, LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(localization);
        var vm = new KnownDyesViewModel(main);
        var learned = main.GetLearnedKnowledgeKeys() ?? [];
        var entries = await Task.Run(() => KnownDyeService.Scan(localization)).ConfigureAwait(true);
        vm._allRows = entries.Select(e => new KnownDyeRow(e)).ToList();
        vm.Sync(learned);
        vm.BuildThemeFilters();
        vm.ApplyFilter();
        vm.StatusMessage = entries.Count == 0
            ? UiText.Get("KnowledgeNotLoaded", "knowledgeinfo.pabgb not loaded (no game install configured).")
            : UiText.Format("KnownDyesStatus",
                "{0} of {1} dye colours known. Tick colours, then Learn selected.",
                vm._allRows.Count(r => r.IsKnown), vm._allRows.Count);
        return vm;
    }

    private void Sync(HashSet<uint> learned)
    {
        foreach (var r in _allRows)
        {
            r.IsKnown = learned.Contains(r.Entry.Key);
            r.IsThemeKnown = r.Entry.ThemeKey is not { } t || learned.Contains(t);
            r.IsDyeItemKnown = r.Entry.DyeItemKey is { } i ? learned.Contains(i) : null;
            if (r.IsKnown)
            {
                r.IsChecked = false;
            }
        }
    }

    private void BuildThemeFilters()
    {
        var keep = SelectedThemeFilter?.Text;
        ThemeFilters.Clear();
        ThemeFilters.Add(new NodeFilterOption(UiText.Get("KnownDyesAllThemes", "All themes"), null));
        foreach (var g in _allRows.GroupBy(r => r.Entry.ThemeId))
        {
            var first = g.First();
            ThemeFilters.Add(new NodeFilterOption(
                $"{first.ThemeLabel} ({g.Count(r => r.IsKnown)}/{g.Count()})", null, g.Key));
        }
        SelectedThemeFilter = ThemeFilters.FirstOrDefault(o => o.Text == keep) ?? ThemeFilters[0];
    }

    partial void OnSelectedThemeFilterChanged(NodeFilterOption? value) => ApplyFilter();
    partial void OnSearchTextChanged(string? value) => ApplyFilter();
    partial void OnShowUnknownOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);
        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (SelectedThemeFilter?.Text is { } theme && r.Entry.ThemeId != theme) continue;
            if (ShowUnknownOnly && r.IsKnown) continue;
            if (hasSearch
                && !r.Entry.ColorName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.ThemeLabel.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.Entry.InternalName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.KeyText.Contains(search!, StringComparison.Ordinal))
            {
                continue;
            }
            Rows.Add(r);
        }
        FilterSummary = UiText.Format("DialogFilterSummary", "Showing {0:N0} of {1:N0}",
            Rows.Count, _allRows.Count);
    }

    private bool CanAct => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void SelectAll()
    {
        foreach (var r in Rows.Where(r => !r.IsKnown)) r.IsChecked = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void UnselectAll()
    {
        foreach (var r in _allRows) r.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task LearnSelected() =>
        LearnAsync(_allRows.Where(r => r.IsChecked).ToList());

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task LearnAllShown() => LearnAsync(Rows.ToList());

    private async Task LearnAsync(List<KnownDyeRow> rows)
    {
        var learned = _main.GetLearnedKnowledgeKeys() ?? [];
        var keys = KnownDyeService.KeysToLearn(rows.Select(r => r.Entry), learned, IncludeDyeItems);
        if (keys.Count == 0)
        {
            StatusMessage = UiText.Get("KnownDyesNothing", "Nothing to learn: the chosen colours are already known.");
            return;
        }
        IsBusy = true;
        StatusMessage = UiText.Format("KnowledgeInjecting", "Injecting {0} knowledge key(s)…", keys.Count);
        try
        {
            var (_, applied, message) = await _main.LearnKnowledgeAsync(keys).ConfigureAwait(true);
            if (applied > 0 && _main.GetLearnedKnowledgeKeys() is { } now)
            {
                Sync(now);
                BuildThemeFilters();
                ApplyFilter();
            }
            StatusMessage = UiText.Format("KnownDyesLearned",
                "{0} Now {1} of {2} dye colours known; save the file to keep them.",
                message, _allRows.Count(r => r.IsKnown), _allRows.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One dye colour row.</summary>
public sealed partial class KnownDyeRow : ObservableObject
{
    public KnownDyeRow(KnownDyeEntry entry)
    {
        Entry = entry;
        ThemeLabel = entry.ThemeId == "Grey" && entry.ColorGroupKey is null
            ? UiText.Get("KnownDyesGreyTheme", "Grey (all themes)")
            : $"{entry.ThemeName} ({entry.ThemeId})";
        Swatches = (entry.Swatches ?? [])
            .Select(c => (IBrush)new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)))
            .ToList();
    }

    public KnownDyeEntry Entry { get; }
    public string ThemeLabel { get; }
    public string ColorName => Entry.ColorName;
    public int Shade => Entry.Shade;

    /// <summary>"Grey" for the theme's grey ramp (shade 0), else the shade number.</summary>
    public string ShadeText => Entry.Shade == 0 && Entry.ColorGroupKey is not null
        ? UiText.Get("KnownDyesShadeGrey", "Grey")
        : Entry.Shade.ToString(CultureInfo.InvariantCulture);

    /// <summary>The palette colours this knowledge unlocks (empty without the mapping).</summary>
    public IReadOnlyList<IBrush> Swatches { get; }
    public string KeyText => Entry.Key.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeStatusText))]
    private bool _isThemeKnown;

    /// <summary>Null when no dye item matches this colour.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DyeItemText))]
    private bool? _isDyeItemKnown;

    public string StatusText => IsKnown
        ? UiText.Get("KnownDyesKnown", "Known")
        : UiText.Get("KnownDyesUnknown", "Unknown");

    public string ThemeStatusText => IsThemeKnown
        ? UiText.Get("KnownDyesKnown", "Known")
        : UiText.Get("KnownDyesUnknown", "Unknown");

    public string DyeItemText => IsDyeItemKnown switch
    {
        true => UiText.Get("KnownDyesCollected", "Collected"),
        false => UiText.Get("KnownDyesNotCollected", "Not collected"),
        null => "—",
    };
}
