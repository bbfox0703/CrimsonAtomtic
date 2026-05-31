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
    public const string NoNodesSummary =
        "No faction nodes found — this save has no FactionSaveData._factionNodeElementSaveDataList "
        + "(or the game install isn't configured for name resolution).";

    private FactionNodeEditorViewModel(MainWindowViewModel main)
    {
        _main = main;
        foreach (var (value, label) in MainWindowViewModel.FactionNodeStates.All)
        {
            TargetStates.Add(new FactionNodeStateOption(value, label));
        }
        // Default target = Active (the "discovered" state).
        _selectedTargetState = TargetStates.FirstOrDefault(o => o.Value == 2) ?? TargetStates.FirstOrDefault();
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
        var loc = main.Localization;
        var targets = await Task.Run(main.ScanFactionNodes).ConfigureAwait(true);

        var rows = new List<FactionNodeRow>(targets.Count);
        foreach (var t in targets)
        {
            var owner = loc.ResolveByFieldTypeName("FactionNodeKey", t.OwnerKey);
            var conq = t.ConquerorKey == 0 ? null : loc.ResolveByFieldTypeName("FactionKey", t.ConquerorKey);
            rows.Add(new FactionNodeRow(t, owner, conq));
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.OwnerName, b.OwnerName));
        vm._allRows = rows;
        vm.ApplyFilter();
        vm.StatusMessage = rows.Count == 0
            ? NoNodesSummary
            : $"{rows.Count:N0} faction node(s). Filter / tick rows, pick a target state, then Set selected. "
              + "\"Discover all\" sets every Undiscovered/Discovered node to Active.";
        return vm;
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);

        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (hasSearch
                && !r.OwnerName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.OwnerKeyText.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.StateLabel.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && (r.ConquerorName is null || !r.ConquerorName.Contains(search!, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            Rows.Add(r);
        }
        FilterSummary = $"Showing {Rows.Count:N0} of {_allRows.Count:N0}";
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
            StatusMessage = "Pick a target state first.";
            return;
        }
        var picked = _allRows.Where(r => r.IsChecked).ToList();
        if (picked.Count == 0)
        {
            StatusMessage = "No nodes are ticked.";
            return;
        }
        await ApplyAsync(picked, target.Value,
            $"Set {picked.Count} faction node(s) to \"{target.Label}\"?").ConfigureAwait(true);
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
            StatusMessage = "Nothing to discover — every node is already Active or beyond.";
            return;
        }
        await ApplyAsync(picked, 2,
            $"Discover all {picked.Count} undiscovered faction node(s) (set to Active)?").ConfigureAwait(true);
    }

    private async Task ApplyAsync(List<FactionNodeRow> picked, byte newState, string confirmBody)
    {
        if (ConfirmRequested is { } ask)
        {
            var ok = await ask(
                "Set faction node state?",
                confirmBody + "\n\nReversible by reloading the save without writing.").ConfigureAwait(true);
            if (!ok)
            {
                StatusMessage = "Cancelled.";
                return;
            }
        }

        IsBusy = true;
        StatusMessage = $"Setting {picked.Count} node(s)…";
        try
        {
            var changes = picked.Select(r => (r.Target, newState)).ToList();
            var (applied, err) = await _main.SetFactionNodeStatesAsync(changes).ConfigureAwait(true);
            StatusMessage = err is null
                ? $"Done: set {applied} node(s)" + (applied < picked.Count ? $" ({picked.Count - applied} already at target)." : ".")
                : $"Failed: {err.Message}. Reload without writing to revert.";

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
        var rows = new List<FactionNodeRow>(targets.Count);
        foreach (var t in targets)
        {
            var owner = loc.ResolveByFieldTypeName("FactionNodeKey", t.OwnerKey);
            var conq = t.ConquerorKey == 0 ? null : loc.ResolveByFieldTypeName("FactionKey", t.ConquerorKey);
            rows.Add(new FactionNodeRow(t, owner, conq));
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.OwnerName, b.OwnerName));
        _allRows = rows;
        ApplyFilter();
    }
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
        MainWindowViewModel.FactionNodeTarget target, string? ownerName, string? conquerorName)
    {
        Target = target;
        OwnerName = string.IsNullOrEmpty(ownerName)
            ? target.OwnerKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ownerName;
        ConquerorName = conquerorName;
    }

    internal MainWindowViewModel.FactionNodeTarget Target { get; }

    public string OwnerName { get; }
    public string OwnerKeyText => Target.OwnerKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string StateLabel => MainWindowViewModel.FactionNodeStates.Label(Target.CurrentState);
    public string? ConquerorName { get; }
    public string CapitalText => Target.IsCapital ? "★" : string.Empty;

    [ObservableProperty]
    private bool _isChecked;
}
