using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Modal dialog the user sees when closing the app with unsaved
/// changes, or via Tools → Review changes…. Lists every entry in
/// the <see cref="ChangeJournal"/> and offers three buttons:
/// Save / Discard / Cancel.
///
/// <para>
/// Two call modes:
/// <list type="bullet">
///   <item><b>Closing path</b>: <see cref="ShowAsync"/> returns the
///     user's chosen action so the caller can either invoke Save +
///     close, or close-without-save, or cancel the close.</item>
///   <item><b>Review path</b>: Tools menu launch — the user just
///     wants to look. Result == Cancel == "close the dialog, don't
///     touch the save".</item>
/// </list>
/// The two paths share the same UI; the caller decides what to do
/// with the result.
/// </para>
/// </summary>
public sealed partial class ChangeSummaryDialog : Window
{
    private ChangeSummaryDialogResult _result = ChangeSummaryDialogResult.Cancel;

    public ChangeSummaryDialog()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    /// <summary>
    /// Display the dialog. Behaviour:
    /// <list type="bullet">
    ///   <item>When <paramref name="closingContext"/> is true, the
    ///     header asks "Save before exiting?" and the Discard button
    ///     reads "Discard &amp; exit".</item>
    ///   <item>Otherwise (Tools → Review changes…), the header reads
    ///     "Review pending changes" and Discard reads "Discard all
    ///     edits (reload save)" — but the caller still receives the
    ///     user's choice and decides whether to honour Discard.</item>
    /// </list>
    /// </summary>
    public static async Task<ChangeSummaryDialogResult> ShowAsync(
        Window owner, ChangeJournal journal, bool closingContext)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(journal);
        var dlg = new ChangeSummaryDialog();
        // Bind the entries (the live observable collection — if the
        // user has the dialog open and an edit lands meanwhile, the
        // grid will refresh; rare but harmless).
        dlg.EntriesGrid.ItemsSource = journal.Entries;
        // Title + button labels swap per context. We pull localised
        // strings out of the static resources directly so the dialog
        // doesn't need its own ViewModel for two text labels.
        var app = Application.Current;
        dlg.HeaderText.Text = closingContext
            ? (app?.FindResource("ChangeSummaryHeaderClose") as string
               ?? $"You have {journal.Count} unsaved change(s). Save before exiting?")
            : (app?.FindResource("ChangeSummaryHeaderReview") as string
               ?? $"Pending changes ({journal.Count})");
        dlg.SubHeaderText.Text = closingContext
            ? (app?.FindResource("ChangeSummarySubHeaderClose") as string ?? string.Empty)
            : (app?.FindResource("ChangeSummarySubHeaderReview") as string ?? string.Empty);
        dlg.DiscardButton.Content = closingContext
            ? (app?.FindResource("ChangeSummaryDiscardExit") as string ?? "Discard & exit")
            : (app?.FindResource("ChangeSummaryDiscard") as string ?? "Discard");
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _result = ChangeSummaryDialogResult.Save;
        Close();
    }

    private void OnDiscardClick(object? sender, RoutedEventArgs e)
    {
        _result = ChangeSummaryDialogResult.Discard;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = ChangeSummaryDialogResult.Cancel;
        Close();
    }
}

/// <summary>
/// User's choice from <see cref="ChangeSummaryDialog"/>.
/// </summary>
public enum ChangeSummaryDialogResult
{
    /// <summary>Dialog closed without taking any action (cancel button / Esc / X).</summary>
    Cancel,
    /// <summary>User accepted "save and continue" (e.g. on close, save then close).</summary>
    Save,
    /// <summary>User accepted "discard" — close-without-save, or reload to drop edits.</summary>
    Discard,
}
