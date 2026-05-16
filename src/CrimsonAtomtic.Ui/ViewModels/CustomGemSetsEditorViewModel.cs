using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the standalone "Edit Custom Gem Sets" dialog launched
/// from the Sockets editor toolbar (and Tools menu). Three editable
/// rows persisted to <see cref="AppSettings.CustomGemSets"/>.
/// </summary>
/// <remarks>
/// Each row is one custom set with a label + 5 ItemKey text boxes.
/// Empty / non-numeric ItemKey entries are dropped at Save (so a
/// row with 2 keys + 3 blanks saves as a 2-key set). A row with all
/// keys blank saves as an undefined slot (the Sockets editor's
/// dropdown skips those).
/// </remarks>
public sealed partial class CustomGemSetsEditorViewModel : ObservableObject
{
    public const int SlotCount = 3;
    public const int MaxGemsPerSet = 5;

    private readonly IPlatformPaths _paths;

    public ObservableCollection<CustomGemSetEditorRow> Rows { get; } = new();

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// True when the user clicked Save and the persistence succeeded.
    /// The Sockets editor reads this on close to decide whether to
    /// emit a status hint.
    /// </summary>
    public bool Saved { get; private set; }

    public CustomGemSetsEditorViewModel(IPlatformPaths paths, IReadOnlyList<CustomGemSet>? existing)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        // Seed each of the 3 fixed slots, padding existing with empty
        // entries when the user has fewer than SlotCount stored.
        for (var i = 0; i < SlotCount; i++)
        {
            var src = (existing is { } e && i < e.Count) ? e[i] : null;
            Rows.Add(CustomGemSetEditorRow.From(src, i));
        }
        StatusMessage = "Up to 3 custom sets, each with up to 5 gems. Empty key cells are skipped at Save.";
    }

    [RelayCommand]
    private void Save()
    {
        // Build the persisted shape: drop empty / non-numeric cells.
        var built = new List<CustomGemSet>(SlotCount);
        foreach (var row in Rows)
        {
            var keys = new List<uint>(MaxGemsPerSet);
            foreach (var cell in row.Cells)
            {
                var text = cell.KeyText?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
                {
                    StatusMessage =
                        $"Set {row.Index + 1}: \"{text}\" isn't a valid ItemKey (decimal u32). Save aborted.";
                    return;
                }
                if (k == 0) continue;
                keys.Add(k);
            }
            built.Add(new CustomGemSet
            {
                Label = string.IsNullOrWhiteSpace(row.Label) ? $"Custom Set {row.Index + 1}" : row.Label,
                GemKeys = keys.ToArray(),
            });
        }
        // Persist via the existing AppSettingsStore.
        var existing = AppSettingsStore.Load(_paths.LocalAppDataDirectory);
        var updated = existing with { CustomGemSets = built.ToArray() };
        if (!AppSettingsStore.TrySave(_paths.LocalAppDataDirectory, updated))
        {
            StatusMessage = "Save failed (couldn't write settings.json). Custom sets not persisted.";
            return;
        }
        Saved = true;
        StatusMessage =
            $"Saved {built.Count} custom set definition(s). "
            + "Close and reopen the Sockets editor to use them in the Apply Set toolbar.";
    }

    /// <summary>
    /// Reload from disk — discards in-progress edits + resets to the
    /// most-recently-persisted state.
    /// </summary>
    public IReadOnlyList<CustomGemSet> GetCurrentlyPersistedSets()
    {
        var settings = AppSettingsStore.Load(_paths.LocalAppDataDirectory);
        return settings.CustomGemSets ?? Array.Empty<CustomGemSet>();
    }
}

/// <summary>
/// One editable custom-set row. Label + 5 cells; each cell is an
/// editable ItemKey decimal string. Save serialises to
/// <see cref="CustomGemSet"/>.
/// </summary>
public sealed partial class CustomGemSetEditorRow : ObservableObject
{
    public int Index { get; }
    public ObservableCollection<CustomGemSetEditorCell> Cells { get; } = new();

    [ObservableProperty]
    private string _label;

    private CustomGemSetEditorRow(int index, string label)
    {
        Index = index;
        _label = label;
    }

    public static CustomGemSetEditorRow From(CustomGemSet? source, int index)
    {
        var label = source?.Label ?? $"Custom Set {index + 1}";
        var row = new CustomGemSetEditorRow(index, label);
        for (var i = 0; i < CustomGemSetsEditorViewModel.MaxGemsPerSet; i++)
        {
            var key = (source?.GemKeys is { } keys && i < keys.Length) ? keys[i] : 0u;
            row.Cells.Add(new CustomGemSetEditorCell
            {
                Index = i,
                KeyText = key == 0
                    ? string.Empty
                    : key.ToString(CultureInfo.InvariantCulture),
            });
        }
        return row;
    }
}

/// <summary>One cell in the per-set 5-column ItemKey grid.</summary>
public sealed partial class CustomGemSetEditorCell : ObservableObject
{
    public int Index { get; init; }
    [ObservableProperty]
    private string? _keyText;
}
