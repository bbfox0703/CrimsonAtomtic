using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the Tools → Unlock Mounts… dialog. Presents the static
/// <see cref="MountCatalog"/> as a per-row list and routes each row's
/// "Unlock" action back into <see cref="MainWindowViewModel.UnlockMountAsync"/>
/// (which owns the loaded-save loader and does the real mutation):
/// <list type="bullet">
///   <item>Sigil mounts → grant the Sigil of Solidarity item into Quest
///     Artifacts; the player uses it in-game to finish the unlock.</item>
///   <item>Dragon → transplant its real merc element from the embedded donor
///     + inject its identity/summon knowledge.</item>
/// </list>
/// The host (<see cref="MainWindowViewModel"/>) already flips its own dirty
/// flag + window title inside <c>UnlockMountAsync</c>, so this dialog is a
/// thin presenter.
/// </summary>
public sealed partial class MountUnlockViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<MountUnlockRow> Rows { get; } = [];

    public MountUnlockViewModel(MainWindowViewModel main, LocalizationProvider localization)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(localization);
        _main = main;

        foreach (var entry in MountCatalog.All)
        {
            // Prefer the localized sigil-item name ("Sigil of Solidarity
            // (…)") for sigil rows — a reliable game-data label. Fall back
            // to the catalog's English name (always set for the dragon).
            var label = entry.DisplayName;
            if (entry.Kind == MountUnlockKind.SigilGrant && entry.SigilItemKey != 0)
            {
                var resolved = localization.LookupItemName(
                    entry.SigilItemKey, LocalizationProvider.DefaultLanguage);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    label = resolved;
                }
            }
            Rows.Add(new MountUnlockRow(this, entry, label));
        }

        StatusMessage = $"{Rows.Count} unlockable mount(s). Sigil mounts grant the "
            + "sigil into Quest Artifacts — use it in-game to finish; the dragon "
            + "is unlocked directly.";
    }

    /// <summary>
    /// Apply one row's unlock. Serialized per row via
    /// <see cref="MountUnlockRow.IsBusy"/>; the result message lands on the
    /// row + the dialog footer.
    /// </summary>
    internal async Task ApplyAsync(MountUnlockRow row)
    {
        if (row.IsBusy)
        {
            return;
        }
        row.IsBusy = true;
        row.StatusText = "Working…";
        try
        {
            var (ok, msg) = await _main.UnlockMountAsync(row.Entry);
            row.Succeeded = ok;
            row.StatusText = msg;
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            row.Succeeded = false;
            row.StatusText = $"Error: {ex.Message}";
            StatusMessage = row.StatusText;
        }
        finally
        {
            row.IsBusy = false;
        }
    }
}

/// <summary>One catalog mount + its per-row unlock state.</summary>
public sealed partial class MountUnlockRow : ObservableObject
{
    private readonly MountUnlockViewModel _parent;

    public MountUnlockRow(MountUnlockViewModel parent, MountEntry entry, string displayName)
    {
        _parent = parent;
        Entry = entry;
        DisplayName = displayName;
        KindLabel = entry.IsPet ? "Pet" : "Special mount";
        MethodLabel = entry.Kind == MountUnlockKind.SigilGrant
            ? "Grant Sigil → use in-game"
            : "Transplant element + knowledge";
    }

    public MountEntry Entry { get; }
    public string DisplayName { get; }
    public string KindLabel { get; }
    public string MethodLabel { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _succeeded;

    private bool CanUnlock => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private Task Unlock() => _parent.ApplyAsync(this);
}
