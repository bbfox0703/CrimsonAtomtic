using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the Tools → Edit Knowledge dialog. Enumerates every
/// <c>knowledgeinfo</c> entry, buckets it into a curated category (by the
/// <c>Knowledge_&lt;Prefix&gt;_…</c> internal-name token), and exposes a
/// filterable, multi-check table so the user learns knowledge per-item or
/// per-category — never a blunt "learn everything" (which would trip codex /
/// achievement state). Mirrors the reference editor's Knowledge tab. The
/// actual inject runs through <see cref="MainWindowViewModel.LearnKnowledgeAsync"/>
/// (which reuses the abyss/mount knowledge-inject primitives).
/// </summary>
public sealed partial class KnowledgeEditorViewModel : ObservableObject
{
    /// <summary>
    /// The 16 curated categories (== the reference editor's set). Any
    /// knowledge whose internal-name prefix isn't one of these falls into
    /// <see cref="OtherCategory"/>.
    /// </summary>
    private static readonly string[] CuratedCategories =
    [
        "Skill", "Recipe", "Node", "Faction", "Collection", "Visione",
        "AbyssRuins", "Paper", "Dungeon", "Riding", "Living", "Permit",
        "Legendary", "Minigame", "Unique", "WantedPaper",
    ];

    private const string AllCategory = "All";
    private const string OtherCategory = "Other";

    private readonly MainWindowViewModel _main;
    private List<KnowledgeRow> _allRows = [];

    /// <summary>Wired by the window code-behind to a yes/no confirm dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<KnowledgeRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LearnCategoryCommand))]
    private string _selectedCategory = AllCategory;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _showUnlearnedOnly;

    [ObservableProperty]
    private bool _showLearnedOnly;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _filterSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LearnSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(LearnCategoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    private bool _isBusy;

    private KnowledgeEditorViewModel(MainWindowViewModel main)
    {
        _main = main;
        Categories.Add(AllCategory);
        foreach (var c in CuratedCategories)
        {
            Categories.Add(c);
        }
        Categories.Add(OtherCategory);
    }

    /// <summary>
    /// Build the VM: enumerate knowledgeinfo + resolve display names on a
    /// background thread, mark each row learned/unlearned against the loaded
    /// save. The dialog shows a "Scanning…" status until this returns.
    /// </summary>
    public static async Task<KnowledgeEditorViewModel> CreateAsync(
        MainWindowViewModel main, LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(localization);

        var vm = new KnowledgeEditorViewModel(main);
        var learned = main.GetLearnedKnowledgeKeys() ?? [];

        var rows = await Task.Run(() =>
        {
            var count = localization.KnowledgeCount;
            var list = new List<KnowledgeRow>(count);
            for (var i = 0; i < count; i++)
            {
                if (localization.GetKnowledge(i) is not { } entry)
                {
                    continue;
                }
                var display = localization.ResolveByFieldTypeName("KnowledgeKey", entry.KnowledgeKey);
                if (string.IsNullOrEmpty(display))
                {
                    display = entry.InternalName;
                }
                list.Add(new KnowledgeRow(
                    entry.KnowledgeKey,
                    display,
                    entry.InternalName,
                    CategoryFor(entry.InternalName),
                    learned.Contains(entry.KnowledgeKey)));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            return list;
        }).ConfigureAwait(true);

        vm._allRows = rows;
        vm.ApplyFilter();
        vm.StatusMessage = rows.Count == 0
            ? "knowledgeinfo.pabgb not loaded (no game install configured)."
            : $"{rows.Count:N0} knowledge entries · {rows.Count(r => r.IsLearned):N0} already learned. "
              + "Pick a category or search, tick rows, then Learn selected.";
        return vm;
    }

    /// <summary>
    /// Bucket by the <c>Knowledge_&lt;Prefix&gt;_…</c> token: the prefix when
    /// it's one of the 16 curated categories, else <see cref="OtherCategory"/>.
    /// </summary>
    public static string CategoryFor(string internalName)
    {
        const string marker = "Knowledge_";
        if (internalName.StartsWith(marker, StringComparison.Ordinal))
        {
            var rest = internalName.AsSpan(marker.Length);
            var end = 0;
            while (end < rest.Length && char.IsLetterOrDigit(rest[end]))
            {
                end++;
            }
            var token = rest[..end].ToString();
            if (Array.IndexOf(CuratedCategories, token) >= 0)
            {
                return token;
            }
        }
        return OtherCategory;
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string? value) => ApplyFilter();
    partial void OnShowUnlearnedOnlyChanged(bool value) => ApplyFilter();
    partial void OnShowLearnedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var cat = SelectedCategory;
        var search = SearchText?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);

        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (!string.Equals(cat, AllCategory, StringComparison.Ordinal)
                && !string.Equals(r.Category, cat, StringComparison.Ordinal))
            {
                continue;
            }
            // The two toggles are independent; only one set narrows.
            if (ShowUnlearnedOnly && !ShowLearnedOnly && r.IsLearned)
            {
                continue;
            }
            if (ShowLearnedOnly && !ShowUnlearnedOnly && !r.IsLearned)
            {
                continue;
            }
            if (hasSearch
                && !r.DisplayName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.InternalName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.Category.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.Key.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(search!))
            {
                continue;
            }
            Rows.Add(r);
        }
        FilterSummary = $"Showing {Rows.Count:N0} of {_allRows.Count:N0}";
    }

    private bool CanLearn => !IsBusy;

    /// <summary>True when a real category (not All) is selected.</summary>
    public bool CanLearnCategory =>
        !IsBusy && !string.Equals(SelectedCategory, AllCategory, StringComparison.Ordinal);

    /// <summary>Tick every row in the current filtered view.</summary>
    [RelayCommand(CanExecute = nameof(CanLearn))]
    private void SelectAll()
    {
        foreach (var r in Rows)
        {
            r.IsChecked = true;
        }
    }

    /// <summary>Clear every tick (across all rows, not just the visible ones).</summary>
    [RelayCommand(CanExecute = nameof(CanLearn))]
    private void UnselectAll()
    {
        foreach (var r in _allRows)
        {
            r.IsChecked = false;
        }
    }

    /// <summary>Invert the tick of every row in the current filtered view.</summary>
    [RelayCommand(CanExecute = nameof(CanLearn))]
    private void InvertSelection()
    {
        foreach (var r in Rows)
        {
            r.IsChecked = !r.IsChecked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task LearnSelected()
    {
        var keys = _allRows.Where(r => r.IsChecked && !r.IsLearned)
                           .Select(r => r.Key).ToList();
        if (keys.Count == 0)
        {
            StatusMessage = "No unlearned rows are ticked.";
            return;
        }
        await LearnAsync(keys, $"{keys.Count} ticked").ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanLearnCategory))]
    private async Task LearnCategory()
    {
        // Learn every unlearned row in the current filtered view.
        var keys = Rows.Where(r => !r.IsLearned).Select(r => r.Key).ToList();
        if (keys.Count == 0)
        {
            StatusMessage = "Nothing unlearned in the current view.";
            return;
        }

        // Warn for map-reveal / codex-adjacent categories or large sets.
        var risky = SelectedCategory is "Node" or "Collection" || keys.Count > 200;
        if (risky && ConfirmRequested is { } ask)
        {
            var note = SelectedCategory switch
            {
                "Node" => "Node entries are map locations — learning them reveals those places on your map.\n\n",
                "Collection" => "Collection entries feed codex completion and may affect achievements.\n\n",
                _ => string.Empty,
            };
            var ok = await ask("Learn all in this category?",
                $"Learn {keys.Count:N0} knowledge entries in '{SelectedCategory}'?\n\n"
                + note + "Reversible by reloading the save without writing.").ConfigureAwait(true);
            if (!ok)
            {
                StatusMessage = "Cancelled.";
                return;
            }
        }
        await LearnAsync(keys, $"all {keys.Count} in '{SelectedCategory}'").ConfigureAwait(true);
    }

    private async Task LearnAsync(List<uint> keys, string label)
    {
        IsBusy = true;
        StatusMessage = $"Learning {label}…";
        try
        {
            var (_, applied, message) = await _main.LearnKnowledgeAsync(keys).ConfigureAwait(true);
            StatusMessage = message;

            // Re-sync learned state from the save (accurate even on partial
            // failure, where only some keys landed).
            if (applied > 0 && _main.GetLearnedKnowledgeKeys() is { } nowLearned)
            {
                foreach (var r in _allRows)
                {
                    var isLearned = nowLearned.Contains(r.Key);
                    r.IsLearned = isLearned;
                    if (isLearned)
                    {
                        r.IsChecked = false;
                    }
                }
                ApplyFilter();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One knowledge entry + its per-row check/learned state.</summary>
public sealed partial class KnowledgeRow : ObservableObject
{
    public KnowledgeRow(uint key, string displayName, string internalName,
        string category, bool isLearned)
    {
        Key = key;
        DisplayName = displayName;
        InternalName = internalName;
        Category = category;
        _isLearned = isLearned;
    }

    public uint Key { get; }
    public string DisplayName { get; }
    public string InternalName { get; }
    public string Category { get; }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isLearned;

    public string KeyText => Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string StatusText => IsLearned ? "Learned" : "Not learned";
}
