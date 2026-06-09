using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the Tools → Complete Sealed Abyss Artifact Challenges dialog.
/// Replaces the former one-shot bulk command with a preview: every
/// eligible challenge is listed as a checkbox row (all ticked by
/// default) with a keyword filter and Select all / Unselect all / Invert,
/// so the user picks exactly which to mark complete. The scan + apply
/// both delegate to <see cref="MainWindowViewModel"/>
/// (<see cref="MainWindowViewModel.ScanSealedArtifactCandidates"/> +
/// <see cref="MainWindowViewModel.ApplySealedArtifactChallengesAsync"/>),
/// so the Pattern B v1 mechanics stay in one place. Mirrors the shape of
/// <see cref="KnowledgeEditorViewModel"/>.
/// </summary>
public sealed partial class SealedArtifactChallengeViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private List<SealedArtifactRow> _allRows = [];
    private MainWindowViewModel.BulkSaPreview _preview;

    /// <summary>Wired by the window code-behind to a yes/no confirm dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    public ObservableCollection<SealedArtifactRow> Rows { get; } = [];

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _filterSummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    private bool _isBusy;

    /// <summary>
    /// Scan scope. false (default, strict): only true
    /// <c>Challenge_SealedArtifact_*</c> challenges. true (broad): also lists
    /// other missions a sealed artifact points at (abyss gates, node /
    /// territory, knowledge / discovery, generic missions) — unsupported by
    /// Pattern B v1. Toggling it on warns, then re-scans.
    /// </summary>
    [ObservableProperty]
    private bool _includeNonSealedArtifact;

    // Guards the re-entrant revert when the user declines the broad-scan warning.
    private bool _suppressScopeToggle;

    /// <summary>
    /// True when iteminfo carries at least one <c>Sealed_Abyss_Artifact_*</c>
    /// row, so there is relevant data to act on — the broad scan can surface
    /// missions even when the strict scan found nothing. The code-behind opens
    /// the dialog whenever this is true (so the user can opt into the broad scan
    /// from an otherwise-empty strict list) and only suppresses it — surfacing
    /// <see cref="NoCandidatesSummary"/> in the footer — when this is false,
    /// i.e. no SA artifacts exist at all (game install not configured).
    /// </summary>
    public bool HasRelevantData => _preview.KnownArtifactCount > 0;

    private SealedArtifactChallengeViewModel(MainWindowViewModel main) => _main = main;

    /// <summary>
    /// Build the VM: scan the loaded save for eligible SA challenges on a
    /// background thread, resolve each row's display name, default every
    /// row to ticked.
    /// </summary>
    public static async Task<SealedArtifactChallengeViewModel> CreateAsync(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        var vm = new SealedArtifactChallengeViewModel(main);
        await vm.PopulateAsync();
        return vm;
    }

    /// <summary>
    /// (Re)scan the loaded save for eligible SA challenges using the current
    /// <see cref="IncludeNonSealedArtifact"/> scope on a background thread, then
    /// rebuild the row list. Called at construction and on every scope toggle.
    /// </summary>
    private async Task PopulateAsync()
    {
        IsBusy = true;
        try
        {
            await RescanAsync();
            StatusMessage = _allRows.Count == 0
                ? (IncludeNonSealedArtifact
                    ? NoCandidatesSummary
                    : NoCandidatesSummary + " "
                      + UiText.Get("SaBroadScanHint",
                          "Tick \"Broad scan\" above to also list other mission types a sealed artifact points at."))
                : UiText.Format("SaStatusEligible",
                    "{0:N0} eligible challenge(s). Tick the ones to complete, then Complete selected. "
                    + "(Catalog row + twin are left untouched — the engine fills them at reward pickup; "
                    + "achievements still require in-game completion.)",
                    _allRows.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIncludeNonSealedArtifactChanged(bool value)
    {
        if (_suppressScopeToggle)
        {
            return;
        }
        _ = OnScopeToggledAsync(value);
    }

    private async Task OnScopeToggledAsync(bool value)
    {
        if (value)
        {
            // Opting into the broad scan — warn. These extra mission types are
            // not the linear SA challenges Pattern B v1 is verified on, and any
            // length-changing edit can fail to load on some saves anyway.
            var ask = ConfirmRequested;
            var ok = ask is null || await ask(
                UiText.Get("SaBroadWarnTitle", "Broad scan — not recommended"),
                UiText.Get("SaBroadWarnBody",
                    "Strict scan (default) lists only true Sealed Abyss Artifact challenges "
                    + "(Challenge_SealedArtifact_*).\n\nBroad scan also lists other missions a "
                    + "sealed artifact happens to point at — abyss gates, node/territory, "
                    + "knowledge/discovery, generic missions. Completing those with this tool is "
                    + "unsupported and more likely to produce a save the game cannot load.\n\n"
                    + "Enable broad scan and re-scan?"));
            if (!ok)
            {
                _suppressScopeToggle = true;
                IncludeNonSealedArtifact = false;
                _suppressScopeToggle = false;
                return;
            }
        }
        await PopulateAsync();
    }

    /// <summary>
    /// Human-readable "why nothing to do" text, reusing the old bulk
    /// command's breakdown. Surfaced by the code-behind when the scan
    /// found no eligible challenges (so the empty dialog never opens).
    /// </summary>
    public string NoCandidatesSummary =>
        _preview.KnownArtifactCount == 0
            ? UiText.Get("SaNoCandidatesNoArtifacts",
                "Nothing to do — no Sealed_Abyss_Artifact_* entries in iteminfo.pabgb. "
                + "Is the game install configured?")
            : UiText.Format("SaNoCandidatesNoneEligible",
                "Nothing to apply — iteminfo lists {0} SA artifact(s) but none has an "
                + "eligible challenge in this save: either no FAR tracker exists yet (artifact never picked up), "
                + "the catalog row is already at state=5, or the X_2 sub-mission key isn't in iteminfo. "
                + "({1} no-mission, {2} no-FAR, {3} already done, {4} other.)",
                _preview.KnownArtifactCount, _preview.SkippedNoMission, _preview.SkippedNoFar,
                _preview.SkippedAlreadyDone, _preview.SkippedOther);

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);

        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (hasSearch
                && !r.DisplayName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.InternalName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.FollowUpName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.KeyText.Contains(search!, StringComparison.OrdinalIgnoreCase)
                && !r.CatalogKey.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(search!))
            {
                continue;
            }
            Rows.Add(r);
        }
        FilterSummary = UiText.Format("DialogFilterSummary", "Showing {0:N0} of {1:N0}",
            Rows.Count, _allRows.Count);
    }

    private bool CanAct => !IsBusy;

    /// <summary>Tick every row in the current filtered view.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void SelectAll()
    {
        foreach (var r in Rows)
        {
            r.IsChecked = true;
        }
    }

    /// <summary>Clear every tick (across all rows, not just the visible ones).</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void UnselectAll()
    {
        foreach (var r in _allRows)
        {
            r.IsChecked = false;
        }
    }

    /// <summary>Invert the tick of every row in the current filtered view.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void InvertSelection()
    {
        foreach (var r in Rows)
        {
            r.IsChecked = !r.IsChecked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CompleteSelected()
    {
        var picked = _allRows.Where(r => r.IsChecked).Select(r => r.Context).ToList();
        if (picked.Count == 0)
        {
            StatusMessage = UiText.Get("SaNoneTicked", "No challenges are ticked.");
            return;
        }

        if (ConfirmRequested is { } ask)
        {
            var ok = await ask(
                UiText.Get("SaConfirmTitle", "Complete selected Sealed Abyss Artifact challenges?"),
                UiText.Format("SaConfirmBody",
                    "Mark {0} challenge(s) complete using Pattern B v1?\n\n"
                    + "Per-challenge writes: FAR tracker _state ← 5 + _completedTime stamped + visible tag added; "
                    + "X_2 sub-mission entry cloned from FAR (when missing). Catalog row + adjacent twin: UNTOUCHED "
                    + "— the engine fills those at reward pickup.\n\n"
                    + "Achievements still require in-game completion. Backed up at "
                    + "%LOCALAPPDATA%\\CrimsonAtomtic\\Backups\\ before write; File → Restore from Backup… rolls back.\n\n"
                    + "Proceed?",
                    picked.Count)).ConfigureAwait(true);
            if (!ok)
            {
                StatusMessage = UiText.Get("DialogCancelled", "Cancelled.");
                return;
            }
        }

        IsBusy = true;
        StatusMessage = UiText.Format("SaApplying", "Applying Pattern B v1 to {0} challenge(s)…", picked.Count);
        try
        {
            var (applied, err, errKey) = await _main
                .ApplySealedArtifactChallengesAsync(picked).ConfigureAwait(true);
            StatusMessage = err is null
                ? UiText.Format("SaDone", "Done: completed {0} of {1} challenge(s) via Pattern B v1.",
                    applied, picked.Count)
                : UiText.Format("SaFailed",
                    "Failed at 0x{0} after {1}/{2}: {3}. Save state is partial — reload without writing to revert.",
                    errKey.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                    applied, picked.Count, err.Message);

            // Re-scan so completed (now state=5) challenges drop off the
            // list and a re-run can't double-apply. Accurate even on
            // partial failure.
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
        var include = IncludeNonSealedArtifact;
        var preview = await Task.Run(() => _main.ScanSealedArtifactCandidates(include)).ConfigureAwait(true);
        _preview = preview;
        var candidates = preview.Candidates ?? [];
        var rows = new List<SealedArtifactRow>(candidates.Count);
        foreach (var c in candidates)
        {
            var display = loc.ResolveByFieldTypeName("MissionKey", c.CatalogKey);
            if (string.IsNullOrEmpty(display))
            {
                display = c.InternalName;
            }
            rows.Add(new SealedArtifactRow(c, display));
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        _allRows = rows;
        ApplyFilter();
        OnPropertyChanged(nameof(HasRelevantData));
    }
}

/// <summary>One eligible Sealed Abyss challenge + its per-row tick state.</summary>
public sealed partial class SealedArtifactRow : ObservableObject
{
    internal SealedArtifactRow(MainWindowViewModel.CurrentChallengeContext context, string displayName)
    {
        Context = context;
        DisplayName = displayName;
        _isChecked = true;
    }

    internal MainWindowViewModel.CurrentChallengeContext Context { get; }
    public string DisplayName { get; }

    public uint CatalogKey => Context.CatalogKey;
    public string KeyText => $"0x{Context.CatalogKey:X8}";
    public string InternalName => Context.InternalName;
    public string FollowUpName => Context.FollowUpInternalName;

    [ObservableProperty]
    private bool _isChecked;
}
