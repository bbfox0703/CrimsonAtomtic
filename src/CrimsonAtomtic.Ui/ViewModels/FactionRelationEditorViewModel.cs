using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for Tools → Edit Faction Relations. One row per faction with its
/// relation to the player's faction; the user picks a new relation per row
/// and Apply writes the changed rows both ways through
/// <see cref="MainWindowViewModel.ApplyFactionRelationsAsync"/>.
/// </summary>
public sealed partial class FactionRelationEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private List<FactionRelationRow> _allRows = [];

    /// <summary>Wired by the window code-behind to a yes/no confirm dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    public ObservableCollection<FactionRelationRow> Rows { get; } = [];

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _onlyWithRows;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _filterSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    /// <summary>True when the save holds a faction list.</summary>
    public bool HasFactions => _allRows.Count > 0;

    public static string NoFactionsSummary =>
        UiText.Get("RelNoFactions", "No factions found — this save has no FactionSaveData._factionElementSaveDataList.");

    private FactionRelationEditorViewModel(MainWindowViewModel main) => _main = main;

    public static async Task<FactionRelationEditorViewModel> CreateAsync(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var vm = new FactionRelationEditorViewModel(main);
        await vm.RescanAsync().ConfigureAwait(true);
        vm.StatusMessage = vm._allRows.Count == 0
            ? NoFactionsSummary
            : UiText.Format("RelHeaderStatus",
                "{0} faction(s); {1} have an explicit relation with you. Pick a new relation, then Apply.",
                vm._allRows.Count, vm._allRows.Count(r => r.Entry.PlayerToFaction != FactionRelation.GroupDefault));
        return vm;
    }

    private async Task RescanAsync()
    {
        var entries = await Task.Run(_main.ScanFactionRelations).ConfigureAwait(true);
        var loc = _main.Localization;
        // Most members first: the faction a place's rank-and-file NPCs
        // follow is what turns it hostile, not the named family faction.
        _allRows = entries
            .Select(e =>
            {
                var (english, secondary) = loc.ResolveFactionDisplayName(e.FactionKey);
                var display = english is null ? string.Empty
                    : secondary is null || secondary == english ? english
                    : $"{english} / {secondary}";
                var typical = string.Join("; ", loc.FactionMemberNames(e.FactionKey, 2).Select(m =>
                    m.Secondary is null || m.Secondary == m.Name
                        ? $"{m.Name} ×{m.Count}"
                        : $"{m.Name} / {m.Secondary} ×{m.Count}"));
                return new FactionRelationRow(
                    e,
                    Name(loc, "FactionKey", e.FactionKey),
                    typical,
                    display,
                    loc.FactionMemberCount(e.FactionKey),
                    e.LeaderCharacterKey == 0 ? string.Empty : Name(loc, "CharacterKey", e.LeaderCharacterKey),
                    Name(loc, "FactionRelationGroupKey", e.RelationGroupKey));
            })
            .OrderByDescending(r => r.MemberCount)
            .ThenBy(r => r.FactionName, StringComparer.Ordinal)
            .ToList();
        ApplyFilter();
    }

    private static string Name(LocalizationProvider loc, string typeName, uint key)
    {
        var n = loc.ResolveByFieldTypeName(typeName, key);
        return string.IsNullOrEmpty(n) ? key.ToString(CultureInfo.InvariantCulture) : n;
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnOnlyWithRowsChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim();
        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (OnlyWithRows && r.Entry.PlayerToFaction == FactionRelation.GroupDefault
                && r.Entry.FactionToPlayer == FactionRelation.GroupDefault)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(search)
                && !r.FactionName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.TypicalMembers.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.KeyText.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.LeaderName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Rows.Add(r);
        }
        FilterSummary = UiText.Format("DialogFilterSummary", "Showing {0:N0} of {1:N0}", Rows.Count, _allRows.Count);
    }

    private bool CanAct => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Apply()
    {
        var changed = _allRows
            .Where(r => r.NewRelation is { } n && (n.Value != r.Entry.PlayerToFaction || r.Entry.IsMismatched))
            .ToList();
        if (changed.Count == 0)
        {
            StatusMessage = UiText.Get("RelNoChanges", "Nothing changed.");
            return;
        }
        if (ConfirmRequested is { } ask
            && !await ask(UiText.Get("RelConfirmTitle", "Apply faction relations?"),
                UiText.Format("RelConfirmBody",
                    "Set the relation with {0} faction(s), both directions?\n\nReversible by reloading the save without writing.",
                    changed.Count)).ConfigureAwait(true))
        {
            StatusMessage = UiText.Get("DialogCancelled", "Cancelled.");
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.Get("RelApplying", "Applying…");
        try
        {
            var changes = changed.Select(r => new FactionRelationChange(r.Entry.FactionKey, r.NewRelation!.Value)).ToList();
            var result = await _main.ApplyFactionRelationsAsync(changes).ConfigureAwait(true);
            StatusMessage = result.Error is null
                ? UiText.Format("RelDone", "Done: set {0} faction(s). Saved to the file on File → Save.", result.Applied)
                : UiText.Format("RelFailed", "Stopped after {0} faction(s): {1}. Reload without writing to revert.",
                    result.Applied, result.Error);
            await RescanAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One relation choice for a row's combo.</summary>
public sealed record FactionRelationOption(FactionRelation Value, string Label)
{
    public override string ToString() => Label;

    public static string LabelOf(FactionRelation r) => r switch
    {
        FactionRelation.GroupDefault => UiText.Get("RelGroupDefault", "Group default (no row)"),
        FactionRelation.Neutral => UiText.Get("RelNeutral", "Neutral"),
        FactionRelation.War => UiText.Get("RelWar", "War"),
        FactionRelation.Agreement => UiText.Get("RelAgreement", "Agreement"),
        FactionRelation.BloodAlly => UiText.Get("RelBloodAlly", "Blood ally"),
        FactionRelation.ByGroup => UiText.Get("RelByGroup", "By group"),
        _ => ((int)r).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The choices every row offers; ByGroup only where the save already uses it.</summary>
    public static IReadOnlyList<FactionRelationOption> For(FactionRelation current)
    {
        List<FactionRelation> values =
        [
            FactionRelation.GroupDefault, FactionRelation.Neutral, FactionRelation.War,
            FactionRelation.Agreement, FactionRelation.BloodAlly,
        ];
        if (!values.Contains(current))
        {
            values.Add(current);
        }
        return values.Select(v => new FactionRelationOption(v, LabelOf(v))).ToList();
    }
}

/// <summary>One faction, its relation with the player, and the picked new one.</summary>
public sealed partial class FactionRelationRow : ObservableObject
{
    internal FactionRelationRow(
        FactionRelationEntry entry, string factionName, string typicalMembers, string displayName,
        int memberCount, string leaderName, string groupName)
    {
        Entry = entry;
        FactionName = factionName;
        TypicalMembers = typicalMembers;
        DisplayName = displayName;
        MemberCount = memberCount;
        LeaderName = leaderName;
        GroupName = groupName;
        Options = FactionRelationOption.For(entry.PlayerToFaction);
        _newRelation = Options.First(o => o.Value == entry.PlayerToFaction);
    }

    internal FactionRelationEntry Entry { get; }

    public string FactionName { get; }

    /// <summary>
    /// The two most common member names with counts ("Hernandian Guard /
    /// 埃爾南德警衛隊 ×49; …") — how a player recognises the faction.
    /// </summary>
    public string TypicalMembers { get; }

    /// <summary>Localized faction-group name, in practice a region ("Hernand"); shared by many factions.</summary>
    public string DisplayName { get; }

    /// <summary>Characters in characterinfo that belong to this faction.</summary>
    public int MemberCount { get; }

    public string KeyText => Entry.FactionKey.ToString(CultureInfo.InvariantCulture);
    public string LeaderName { get; }
    public string GroupName { get; }
    public IReadOnlyList<FactionRelationOption> Options { get; }

    /// <summary>Current relation; "A / B" when the two directions disagree.</summary>
    public string CurrentText => Entry.IsMismatched
        ? $"{FactionRelationOption.LabelOf(Entry.PlayerToFaction)} / {FactionRelationOption.LabelOf(Entry.FactionToPlayer)}"
        : FactionRelationOption.LabelOf(Entry.PlayerToFaction);

    [ObservableProperty]
    private FactionRelationOption? _newRelation;
}
