using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Tools → Edit Faction Relations: thin wrappers that hand the loaded save to
/// <see cref="FactionRelationService"/> and do the view-model bookkeeping.
/// </summary>
public sealed partial class MainWindowViewModel
{
    internal FactionRelationService FactionRelations => new(loader);

    /// <summary>Every faction with its relation to the player's. Pure read.</summary>
    internal IReadOnlyList<FactionRelationEntry> ScanFactionRelations() =>
        _loadedPath is null || Summary is not { Blocks: { } blocks }
            ? []
            : FactionRelations.Scan(_loadedPath, blocks);

    /// <summary>Apply relation changes on a background thread, then refresh and journal.</summary>
    internal async Task<FactionRelationApplyResult> ApplyFactionRelationsAsync(IReadOnlyList<FactionRelationChange> changes)
    {
        if (_loadedPath is null || Summary is not { Blocks: { } blocks } || changes.Count == 0)
        {
            return new FactionRelationApplyResult(0, null);
        }
        var path = _loadedPath;
        var service = FactionRelations;
        var result = await Task.Run(() => service.Apply(path, blocks, changes)).ConfigureAwait(true);

        RefreshSelectedBlockSilently();
        if (result.Applied > 0)
        {
            IsDirty = true;
            Journal.Log(UiText.Get("JournalCatFactionRelations", "Faction relations"),
                UiText.Format("JournalFactionRelations", "Set the relation with {0} faction(s)", result.Applied));
            OnPropertyChanged(nameof(WindowTitle));
        }
        return result;
    }
}
