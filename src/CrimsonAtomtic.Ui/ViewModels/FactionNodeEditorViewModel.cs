using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the Tools → Edit Faction Nodes dialog. Lists every faction
/// stronghold (<c>FactionSaveData._factionNodeElementSaveDataList</c>) as a
/// checkbox row with a keyword filter and Select all / Unselect all /
/// Invert, then applies a chosen <c>_factionState</c> to the ticked rows
/// (the "discover / set state" action). Scan + apply delegate to
/// <see cref="MainWindowViewModel.ScanFactionNodes"/> /
/// <see cref="MainWindowViewModel.SetFactionNodeStatesAsync"/>. Mirrors the
/// shape of <see cref="SealedArtifactChallengeViewModel"/>.
/// </summary>
public sealed partial class FactionNodeEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private List<FactionNodeRow> _allRows = [];

    /// <summary>Wired by the window code-behind to a yes/no confirm dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    public ObservableCollection<FactionNodeRow> Rows { get; } = [];

    /// <summary>The five target states for the "set selected to…" combo.</summary>
    public ObservableCollection<FactionNodeStateOption> TargetStates { get; } = [];

    [ObservableProperty]
    private FactionNodeStateOption? _selectedTargetState;

    [ObservableProperty]
    private string? _searchText;

    /// <summary>"All states" plus one entry per <c>_factionState</c> value.</summary>
    public ObservableCollection<NodeFilterOption> StateFilters { get; } = [];

    [ObservableProperty]
    private NodeFilterOption? _selectedStateFilter;

    /// <summary>"All regions" plus every region guessed from the nodes' internal names, with counts.</summary>
    public ObservableCollection<NodeFilterOption> RegionFilters { get; } = [];

    [ObservableProperty]
    private NodeFilterOption? _selectedRegionFilter;

    /// <summary>"All" plus every conqueror faction present, by member name, with node counts.</summary>
    public ObservableCollection<NodeFilterOption> ConquerorFilters { get; } = [];

    [ObservableProperty]
    private NodeFilterOption? _selectedConquerorFilter;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _filterSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscoverAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    private bool _isBusy;

    /// <summary>True when the scan found at least one faction node.</summary>
    public bool HasNodes => _allRows.Count > 0;

    /// <summary>"No FactionSaveData / faction nodes" message for the empty case.</summary>
    public static string NoNodesSummary =>
        UiText.Get("FactionNoNodes",
            "No faction nodes found — this save has no FactionSaveData._factionNodeElementSaveDataList "
            + "(or the game install isn't configured for name resolution).");

    private FactionNodeEditorViewModel(MainWindowViewModel main)
    {
        _main = main;
        foreach (var (value, label) in MainWindowViewModel.FactionNodeStates.All)
        {
            TargetStates.Add(new FactionNodeStateOption(value, label));
        }
        // Default target = Active (the "discovered" state).
        _selectedTargetState = TargetStates.FirstOrDefault(o => o.Value == 2) ?? TargetStates.FirstOrDefault();
        StateFilters.Add(new NodeFilterOption(UiText.Get("FactionNodeFilterAllStates", "All states"), null));
        foreach (var (value, label) in MainWindowViewModel.FactionNodeStates.All)
        {
            StateFilters.Add(new NodeFilterOption(label, value));
        }
        _selectedStateFilter = StateFilters[0];
    }

    /// <summary>
    /// Build the VM: scan faction nodes on a background thread, resolve
    /// owner (FactionNodeKey) + conqueror (FactionKey) names, default every
    /// row unchecked, sort by owner name.
    /// </summary>
    public static async Task<FactionNodeEditorViewModel> CreateAsync(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        var vm = new FactionNodeEditorViewModel(main);
        await vm.RescanAsync().ConfigureAwait(true);
        var rows = vm._allRows;
        vm.StatusMessage = rows.Count == 0
            ? NoNodesSummary
            : UiText.Format("FactionNodeHeaderStatus",
                "{0:N0} faction node(s). Filter / tick rows, pick a target state, then Set selected. "
                + "\"Discover all\" sets every Undiscovered/Discovered node to Active.",
                rows.Count);
        return vm;
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnSelectedStateFilterChanged(NodeFilterOption? value) => ApplyFilter();

    partial void OnSelectedConquerorFilterChanged(NodeFilterOption? value) => ApplyFilter();

    partial void OnSelectedRegionFilterChanged(NodeFilterOption? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);

        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (SelectedStateFilter?.Value is { } state && r.Target.CurrentState != state)
            {
                continue;
            }
            if (SelectedConquerorFilter?.Value is { } conqueror && r.Target.ConquerorKey != conqueror)
            {
                continue;
            }
            if (SelectedRegionFilter?.Text is { } region && r.Region != region)
            {
                continue;
            }
            if (hasSearch
                && !r.OwnerName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.PlaceName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.OwnerKeyText.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.StateLabel.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && (r.ConquerorName is null || !r.ConquerorName.Contains(search!, StringComparison.OrdinalIgnoreCase)))
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
        foreach (var r in Rows)
        {
            r.IsChecked = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void UnselectAll()
    {
        foreach (var r in _allRows)
        {
            r.IsChecked = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void InvertSelection()
    {
        foreach (var r in Rows)
        {
            r.IsChecked = !r.IsChecked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SetSelected()
    {
        if (SelectedTargetState is not { } target)
        {
            StatusMessage = UiText.Get("FactionPickState", "Pick a target state first.");
            return;
        }
        var picked = _allRows.Where(r => r.IsChecked).ToList();
        if (picked.Count == 0)
        {
            StatusMessage = UiText.Get("FactionNoneTicked", "No nodes are ticked.");
            return;
        }
        await ApplyAsync(picked, target.Value, UiText.Format("FactionSetConfirm",
            "Set {0} faction node(s) to \"{1}\"?\n\nReversible by reloading the save without writing.",
            picked.Count, target.Label)).ConfigureAwait(true);
    }

    /// <summary>
    /// Convenience: tick every Undiscovered/Discovered node (state &lt; 2)
    /// and set them all to Active in one shot — the "探索全部" shortcut.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DiscoverAll()
    {
        var picked = _allRows.Where(r => r.Target.CurrentState < 2).ToList();
        if (picked.Count == 0)
        {
            StatusMessage = UiText.Get("FactionNothingToDiscover",
                "Nothing to discover — every node is already Active or beyond.");
            return;
        }
        await ApplyAsync(picked, 2, UiText.Format("FactionDiscoverConfirm",
            "Discover all {0} undiscovered faction node(s) (set to Active)?\n\n"
            + "Reversible by reloading the save without writing.",
            picked.Count)).ConfigureAwait(true);
    }

    private async Task ApplyAsync(List<FactionNodeRow> picked, byte newState, string confirmBody)
    {
        if (ConfirmRequested is { } ask)
        {
            var ok = await ask(
                UiText.Get("FactionConfirmTitle", "Set faction node state?"),
                confirmBody).ConfigureAwait(true);
            if (!ok)
            {
                StatusMessage = UiText.Get("DialogCancelled", "Cancelled.");
                return;
            }
        }

        IsBusy = true;
        StatusMessage = UiText.Format("FactionSetting", "Setting {0} node(s)…", picked.Count);
        try
        {
            var changes = picked.Select(r => (r.Target, newState)).ToList();
            var (applied, err) = await _main.SetFactionNodeStatesAsync(changes).ConfigureAwait(true);
            StatusMessage = err is null
                ? (applied < picked.Count
                    ? UiText.Format("FactionDonePartial", "Done: set {0} node(s) ({1} already at target).",
                        applied, picked.Count - applied)
                    : UiText.Format("FactionDoneAll", "Done: set {0} node(s).", applied))
                : UiText.Format("FactionFailed", "Failed: {0}. Reload without writing to revert.", err.Message);

            if (applied > 0)
            {
                await RescanAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RescanAsync()
    {
        var loc = _main.Localization;
        var targets = await Task.Run(_main.ScanFactionNodes).ConfigureAwait(true);
        var labels = new Dictionary<uint, string>();
        string Label(uint faction) => labels.TryGetValue(faction, out var l) ? l : labels[faction] = loc.ResolveFactionLabel(faction);
        var rows = new List<FactionNodeRow>(targets.Count);
        foreach (var t in targets)
        {
            var owner = loc.ResolveByFieldTypeName("FactionNodeKey", t.OwnerKey);
            var (english, secondary) = loc.ResolveFactionNodeName(t.OwnerKey);
            var place = english is null ? string.Empty
                : secondary is null || secondary == english ? english
                : $"{english} / {secondary}";
            rows.Add(new FactionNodeRow(t, owner, place, t.ConquerorKey == 0 ? null : Label(t.ConquerorKey)));
        }
        // Region, then named places first, then by place name.
        rows.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.Region, b.Region);
            if (c != 0) return c;
            c = string.IsNullOrEmpty(a.PlaceName).CompareTo(string.IsNullOrEmpty(b.PlaceName));
            return c != 0 ? c : string.CompareOrdinal(a.PlaceName + a.OwnerName, b.PlaceName + b.OwnerName);
        });
        _allRows = rows;

        var keepRegion = SelectedRegionFilter?.Text;
        RegionFilters.Clear();
        RegionFilters.Add(new NodeFilterOption(UiText.Get("FactionNodeFilterAllRegions", "All regions"), null));
        foreach (var g in rows.GroupBy(r => r.Region).OrderByDescending(g => g.Count()))
        {
            RegionFilters.Add(new NodeFilterOption($"{g.Key} ({g.Count():N0})", null, g.Key));
        }
        SelectedRegionFilter = RegionFilters.FirstOrDefault(o => o.Text == keepRegion) ?? RegionFilters[0];

        // Conqueror choices: every faction that holds a node, most nodes first.
        var keep = SelectedConquerorFilter?.Value;
        ConquerorFilters.Clear();
        ConquerorFilters.Add(new NodeFilterOption(UiText.Get("FactionNodeFilterAllConquerors", "All conquerors"), null));
        foreach (var g in rows.Where(r => r.Target.ConquerorKey != 0)
                     .GroupBy(r => r.Target.ConquerorKey)
                     .OrderByDescending(g => g.Count()))
        {
            ConquerorFilters.Add(new NodeFilterOption($"{Label(g.Key)} ({g.Count():N0})", g.Key));
        }
        SelectedConquerorFilter = ConquerorFilters.FirstOrDefault(o => o.Value == keep) ?? ConquerorFilters[0];
        ApplyFilter();
    }
}

/// <summary>
/// One choice in a filter combo: a state value or a FactionKey in
/// <see cref="Value"/>, a region in <see cref="Text"/>, or neither for "all".
/// </summary>
public sealed record NodeFilterOption(string Label, uint? Value, string? Text = null)
{
    public override string ToString() => Label;
}

/// <summary>One target-state choice for the "set selected to…" combo.</summary>
public sealed record FactionNodeStateOption(byte Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One faction stronghold node + its per-row tick state.</summary>
public sealed partial class FactionNodeRow : ObservableObject
{
    internal FactionNodeRow(
        MainWindowViewModel.FactionNodeTarget target, string? ownerName, string placeName, string? conquerorName)
    {
        Target = target;
        OwnerName = string.IsNullOrEmpty(ownerName)
            ? target.OwnerKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ownerName;
        PlaceName = placeName;
        Region = RegionOf(OwnerName);
        ConquerorName = conquerorName;
    }

    /// <summary>
    /// Region guessed from the internal name's prefix (<c>Node_Her_…</c> →
    /// Hernand). The game data holds no region for a node — crimson-rs found
    /// none — so this is a naming habit, labelled as a guess in the UI.
    /// </summary>
    private static string RegionOf(string internalName)
    {
        var parts = internalName.Split(" / ")[0].Split('_');
        var code = parts.Length > 2 && parts[0] == "Node" ? parts[1] : string.Empty;
        return code switch
        {
            "Her" or "Hernand" => "Hernand",
            "Dem" => "Demeniss",
            "Del" => "Delesyia",
            "Crim" or "Crimson" => "Crimson Desert",
            "Kwe" => "Kweiden",
            "Cal" => "Calphade",
            "Pailune" => "Pailune",
            "Island" => "Islands",
            "Ocean" or "Pywel" => "Ocean",
            "Abyss" or "Abyssone" => "Abyss",
            _ => UiText.Get("FactionNodeRegionOther", "Other"),
        };
    }

    /// <summary>The place name the game shows ("Hernand Castle / 埃爾南德城"); empty for unnamed nodes.</summary>
    public string PlaceName { get; }

    /// <summary>See <see cref="RegionOf"/> — a guess.</summary>
    public string Region { get; }

    internal MainWindowViewModel.FactionNodeTarget Target { get; }

    public string OwnerName { get; }
    public string OwnerKeyText => Target.OwnerKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string StateLabel => MainWindowViewModel.FactionNodeStates.Label(Target.CurrentState);
    public string? ConquerorName { get; }
    public string CapitalText => Target.IsCapital ? "★" : string.Empty;

    [ObservableProperty]
    private bool _isChecked;
}
