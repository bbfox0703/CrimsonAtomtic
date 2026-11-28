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

    /// <summary>"All relations" plus each relation present, with counts.</summary>
    public ObservableCollection<FactionRelationFilter> RelationFilters { get; } = [];

    [ObservableProperty]
    private FactionRelationFilter? _selectedRelationFilter;

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
        var playerGroup = await Task.Run(_main.PlayerRelationGroup).ConfigureAwait(true);
        var loc = _main.Localization;
        // What a faction in a given group is to the player when no row says
        // otherwise (game data: War or BloodAlly).
        FactionRelation? GroupEffect(uint group) => playerGroup is { } pg ? loc.GroupRelation(pg, group) : null;
        // The relation groups, as the save's factions use them.
        var groups = entries.Select(e => e.RelationGroupKey).Distinct().Order()
            .Select(k => new FactionGroupOption(k, Name(loc, "FactionRelationGroupKey", k)))
            .ToList();
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
                    groups,
                    GroupEffect);
            })
            .OrderByDescending(r => r.MemberCount)
            .ThenBy(r => r.FactionName, StringComparer.Ordinal)
            .ToList();

        // One choice per current relation as displayed, so "Group default →
        // War" and "Group default → Blood ally" are separate.
        var keep = SelectedRelationFilter?.Key;
        RelationFilters.Clear();
        RelationFilters.Add(new FactionRelationFilter(UiText.Get("RelFilterAllRelations", "All relations"), null));
        foreach (var g in _allRows.GroupBy(r => r.CurrentText)
                     .OrderBy(g => (int)g.First().Entry.PlayerToFaction).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            RelationFilters.Add(new FactionRelationFilter($"{g.Key} ({g.Count():N0})", g.Key));
        }
        SelectedRelationFilter = RelationFilters.FirstOrDefault(f => f.Key == keep) ?? RelationFilters[0];
        ApplyFilter();
    }

    private static string Name(LocalizationProvider loc, string typeName, uint key)
    {
        var n = loc.ResolveByFieldTypeName(typeName, key);
        return string.IsNullOrEmpty(n) ? key.ToString(CultureInfo.InvariantCulture) : n;
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnOnlyWithRowsChanged(bool value) => ApplyFilter();

    partial void OnSelectedRelationFilterChanged(FactionRelationFilter? value) => ApplyFilter();

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
            if (SelectedRelationFilter?.Key is { } relation && r.CurrentText != relation)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(search)
                && !r.FactionName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.TypicalMembers.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.KeyText.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.LeaderName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !r.CurrentText.Contains(search, StringComparison.OrdinalIgnoreCase))
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
            // A row whose two sides disagree is rewritten only when the user
            // picked a relation for it; untouched, it is left as the save has it.
            .Where(r => r.NewRelation is { } n
                        && (n.Value != r.Entry.PlayerToFaction || (r.Entry.IsMismatched && r.RelationPicked)))
            .ToList();
        var regrouped = _allRows
            .Where(r => r.NewGroup is { } g && g.Key != r.Entry.RelationGroupKey)
            .ToList();
        if (changed.Count + regrouped.Count == 0)
        {
            StatusMessage = UiText.Get("RelNoChanges", "Nothing changed.");
            return;
        }
        if (ConfirmRequested is { } ask
            && !await ask(UiText.Get("RelConfirmTitle", "Apply faction relations?"),
                UiText.Format("RelConfirmBody",
                    "Set the relation with {0} faction(s), both directions, and the relation group of {1}?\n\nReversible by reloading the save without writing.",
                    changed.Count, regrouped.Count)).ConfigureAwait(true))
        {
            StatusMessage = UiText.Get("DialogCancelled", "Cancelled.");
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.Get("RelApplying", "Applying…");
        try
        {
            var changes = changed.Select(r => new FactionRelationChange(r.Entry.FactionKey, r.NewRelation!.Value)).ToList();
            var groupChanges = regrouped.Select(r => new FactionGroupChange(r.Entry.FactionKey, r.NewGroup!.Key)).ToList();
            var result = await _main.ApplyFactionRelationsAsync(changes, groupChanges).ConfigureAwait(true);
            StatusMessage = result.Error is null
                ? UiText.Format("RelDone", "Done: set {0} relation(s) and {1} relation group(s). Saved to the file on File → Save.",
                    result.Applied, result.GroupsApplied)
                : UiText.Format("RelFailed", "Stopped after {0} relation(s) and {1} group(s): {2}. Reload without writing to revert.",
                    result.Applied, result.GroupsApplied, result.Error);
            await RescanAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A "current relation" filter choice, keyed by the row's current-relation text; null = all.</summary>
public sealed record FactionRelationFilter(string Label, string? Key)
{
    public override string ToString() => Label;
}

/// <summary>A relation-group choice for a row's combo.</summary>
public sealed record FactionGroupOption(uint Key, string Name)
{
    public override string ToString() => Name;
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
        int memberCount, string leaderName, IReadOnlyList<FactionGroupOption> groupOptions,
        Func<uint, FactionRelation?> groupEffect)
    {
        _groupEffect = groupEffect;
        Entry = entry;
        FactionName = factionName;
        TypicalMembers = typicalMembers;
        DisplayName = displayName;
        MemberCount = memberCount;
        LeaderName = leaderName;
        GroupOptions = groupOptions;
        _newGroup = groupOptions.FirstOrDefault(g => g.Key == entry.RelationGroupKey);
        GroupName = _newGroup?.Name ?? entry.RelationGroupKey.ToString(CultureInfo.InvariantCulture);
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

    private readonly Func<uint, FactionRelation?> _groupEffect;

    /// <summary>
    /// Current relation; "A / B" when the two directions disagree; for no row,
    /// what the faction's group makes it ("Group default → War").
    /// </summary>
    public string CurrentText => Entry.IsMismatched
        ? $"{FactionRelationOption.LabelOf(Entry.PlayerToFaction)} / {FactionRelationOption.LabelOf(Entry.FactionToPlayer)}"
        : Entry.PlayerToFaction == FactionRelation.GroupDefault
            ? $"{FactionRelationOption.LabelOf(FactionRelation.GroupDefault)} → {EffectLabel(Entry.RelationGroupKey)}"
            : FactionRelationOption.LabelOf(Entry.PlayerToFaction);

    /// <summary>
    /// What the selected relation group makes this faction toward the player
    /// by default — follows the group combo, so a change previews here.
    /// </summary>
    public string GroupEffectText => EffectLabel(NewGroup?.Key ?? Entry.RelationGroupKey);

    private string EffectLabel(uint group) => _groupEffect(group) is { } r
        ? FactionRelationOption.LabelOf(r)
        : UiText.Get("RelGroupEffectUndefined", "Undefined");

    partial void OnNewGroupChanged(FactionGroupOption? value) => OnPropertyChanged(nameof(GroupEffectText));

    [ObservableProperty]
    private FactionRelationOption? _newRelation;

    /// <summary>True once the user has picked a relation on this row.</summary>
    public bool RelationPicked { get; private set; }

    partial void OnNewRelationChanged(FactionRelationOption? value) => RelationPicked = true;

    /// <summary>The relation groups a faction can be moved to.</summary>
    public IReadOnlyList<FactionGroupOption> GroupOptions { get; }

    /// <summary>
    /// The faction's relation group, editable: it decides the faction's stance
    /// toward every faction it has no relation row with.
    /// </summary>
    [ObservableProperty]
    private FactionGroupOption? _newGroup;
}
