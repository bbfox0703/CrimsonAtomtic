using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Tools → Edit Contributions: thin wrappers that hand the loaded save to
/// <see cref="ContributionService"/> and do the view-model bookkeeping
/// (refresh, dirty flag, journal). The logic lives in the service.
/// </summary>
public sealed partial class MainWindowViewModel
{
    internal ContributionService Contributions => new(loader, localization);

    /// <summary>Contribution stacks in the loaded save, plus absent catalog items. Pure read.</summary>
    internal IReadOnlyList<ContributionEntry> ScanContributions() =>
        _loadedPath is null || Summary is not { Blocks: { } blocks }
            ? []
            : Contributions.Scan(_loadedPath, blocks);

    /// <summary>Apply amount changes on a background thread, then refresh and journal.</summary>
    internal async Task<ContributionApplyResult> ApplyContributionsAsync(IReadOnlyList<ContributionChange> changes)
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks } || changes.Count == 0)
        {
            return new ContributionApplyResult(0, 0, null);
        }
        var path = _loadedPath;
        var service = Contributions;
        var result = await Task.Run(() => service.Apply(path, blocks, changes)).ConfigureAwait(true);

        RefreshSelectedBlockSilently();
        if (result.Updated + result.Added > 0)
        {
            IsDirty = true;
            Journal.Log(UiText.Get("JournalCatContributions", "Contributions"),
                UiText.Format("JournalContributions", "Set {0} contribution stack(s), added {1}",
                    result.Updated, result.Added));
            OnPropertyChanged(nameof(WindowTitle));
        }
        return result;
    }
}
