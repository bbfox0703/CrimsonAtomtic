using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for Tools → Edit Contributions. One row per contribution stack in the
/// save, plus one per catalog contribution the save has none of. The user
/// types new amounts (or uses "Set all to"), then Apply writes them through
/// <see cref="MainWindowViewModel.ApplyContributionsAsync"/>: stacks the save
/// holds are set in place, absent ones with an amount above 0 are added.
/// </summary>
public sealed partial class ContributionEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    /// <summary>Wired by the window code-behind to a yes/no confirm dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmRequested { get; set; }

    public ObservableCollection<ContributionRow> Rows { get; } = [];

    [ObservableProperty]
    private string? _setAllText;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetAllCommand))]
    private bool _isBusy;

    /// <summary>True when iteminfo lists at least one contribution item.</summary>
    public bool HasCatalog => Rows.Count > 0;

    /// <summary>Message for a missing catalog (no game install / iteminfo).</summary>
    public static string NoCatalogSummary =>
        UiText.Get("ContribNoCatalog",
            "No contribution items found — iteminfo isn't loaded (configure the game install for name resolution).");

    private ContributionEditorViewModel(MainWindowViewModel main) => _main = main;

    public static async Task<ContributionEditorViewModel> CreateAsync(MainWindowViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var vm = new ContributionEditorViewModel(main);
        await vm.RescanAsync().ConfigureAwait(true);
        vm.StatusMessage = vm.Rows.Count == 0
            ? NoCatalogSummary
            : UiText.Format("ContribHeaderStatus",
                "{0} contribution(s); {1} not in this save. Type new amounts, then Apply.",
                vm.Rows.Select(r => r.ItemKey).Distinct().Count(), vm.Rows.Count(r => !r.Entry.IsInSave));
        return vm;
    }

    private async Task RescanAsync()
    {
        var entries = await Task.Run(_main.ScanContributions).ConfigureAwait(true);
        var loc = _main.Localization;
        Rows.Clear();
        foreach (var e in entries
                     .OrderBy(e => e.IsInSave ? 0 : 1)
                     .ThenBy(e => e.ItemKey))
        {
            var (english, secondary) = loc.ResolveItemName(e.ItemKey);
            var name = english ?? loc.ItemInfoStringKey(e.ItemKey)
                       ?? e.ItemKey.ToString(CultureInfo.InvariantCulture);
            var location = e.IsInSave
                ? ContainerLabel(loc, e.InventoryKey)
                : UiText.Get("ContribNotInSave", "Not in save");
            Rows.Add(new ContributionRow(e, name, secondary, location, loc.GetItemMaxStackCount(e.ItemKey)));
        }
        OnPropertyChanged(nameof(HasCatalog));
    }

    private static string ContainerLabel(LocalizationProvider loc, uint? key)
    {
        if (key is not { } k)
        {
            return string.Empty;
        }
        var label = loc.ResolveByFieldTypeName("InventoryKey", k);
        return string.IsNullOrEmpty(label)
            ? k.ToString(CultureInfo.InvariantCulture)
            : $"{label} ({k})";
    }

    private bool CanAct => !IsBusy;

    /// <summary>Copy the "Set all to" value into every row's new amount.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void SetAll()
    {
        if (!TryParseAmount(SetAllText, out var v))
        {
            StatusMessage = UiText.Get("ContribBadNumber", "Enter a whole number of 0 or more.");
            return;
        }
        foreach (var r in Rows)
        {
            r.NewAmountText = (r.Entry.IsInSave ? Math.Max(v, 1) : v).ToString(CultureInfo.InvariantCulture);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Apply()
    {
        var changes = new List<ContributionChange>();
        foreach (var r in Rows)
        {
            if (!TryParseAmount(r.NewAmountText, out var v))
            {
                StatusMessage = UiText.Format("ContribBadRow", "{0}: \"{1}\" is not a whole number of 0 or more.",
                    r.Name, r.NewAmountText ?? string.Empty);
                return;
            }
            if (v != r.Entry.Amount)
            {
                changes.Add(new ContributionChange(r.Entry, v));
            }
        }
        if (changes.Count == 0)
        {
            StatusMessage = UiText.Get("ContribNoChanges", "Nothing changed.");
            return;
        }
        if (_main.Contributions.Validate(changes) is { } invalid)
        {
            StatusMessage = invalid;
            return;
        }
        var adds = changes.Count(c => !c.Entry.IsInSave);
        if (ConfirmRequested is { } ask
            && !await ask(UiText.Get("ContribConfirmTitle", "Apply contribution changes?"),
                UiText.Format("ContribConfirmBody",
                    "Set {0} contribution stack(s) and add {1} new one(s)?\n\nReversible by reloading the save without writing.",
                    changes.Count - adds, adds)).ConfigureAwait(true))
        {
            StatusMessage = UiText.Get("DialogCancelled", "Cancelled.");
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.Get("ContribApplying", "Applying…");
        try
        {
            var result = await _main.ApplyContributionsAsync(changes).ConfigureAwait(true);
            StatusMessage = result.Error is null
                ? UiText.Format("ContribDone", "Done: set {0} stack(s), added {1}. Saved to the file on File → Save.",
                    result.Updated, result.Added)
                : UiText.Format("ContribFailed",
                    "Stopped after setting {0} and adding {1}: {2}. Reload without writing to revert.",
                    result.Updated, result.Added, result.Error);
            await RescanAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryParseAmount(string? text, out ulong value) =>
        ulong.TryParse((text ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out value);
}

/// <summary>One contribution stack (or absent catalog item) with its editable new amount.</summary>
public sealed partial class ContributionRow : ObservableObject
{
    internal ContributionRow(ContributionEntry entry, string name, string? secondaryName, string location, ulong? maxStack)
    {
        Entry = entry;
        Name = name;
        SecondaryName = secondaryName;
        Location = location;
        MaxStack = maxStack;
        _newAmountText = entry.Amount.ToString(CultureInfo.InvariantCulture);
    }

    internal ContributionEntry Entry { get; }

    public uint ItemKey => Entry.ItemKey;
    public string Name { get; }
    public string? SecondaryName { get; }
    public string Location { get; }
    public ulong? MaxStack { get; }
    public string CurrentText => Entry.IsInSave
        ? Entry.Amount.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    [ObservableProperty]
    private string? _newAmountText;
}
