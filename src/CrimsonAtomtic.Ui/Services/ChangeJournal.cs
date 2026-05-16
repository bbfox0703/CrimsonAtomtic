using System.Collections.ObjectModel;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Append-only log of human-readable "what the user changed" entries
/// since the last <see cref="Clear"/>. Cleared on save success + on
/// fresh load — i.e. the journal mirrors
/// <c>MainWindowViewModel.IsDirty</c>'s lifecycle as a richer log.
///
/// <para>
/// Granularity is **per named operation**, not per scalar field.
/// Each Tools-menu action / dialog / bulk-op logs a single
/// human-readable line ("Renamed mercenary X → 'Y'"); raw field
/// edits from the main edit panel log one line per applied edit.
/// True per-scalar before/after capture (Level B in the scope
/// discussion) is a future extension — the current shape is the
/// "I forgot what I changed before saving" UX, not undo.
/// </para>
///
/// <para>
/// Thread-safety: writes happen on UI thread (every mutation entry
/// point dispatches on the VM dispatcher); reads happen from the
/// ChangeSummaryDialog which is also UI-thread. A single lock guards
/// against the edge where a background mutation worker (e.g.
/// SetScalarFieldsBatch on Task.Run) calls Log before its UI-thread
/// continuation publishes.
/// </para>
/// </summary>
public sealed class ChangeJournal
{
    private readonly object _gate = new();
    private readonly ObservableCollection<ChangeEntry> _entries = new();

    /// <summary>Live, observable view of the journal. UI binds to this.</summary>
    public ReadOnlyObservableCollection<ChangeEntry> Entries { get; }

    public ChangeJournal()
    {
        Entries = new ReadOnlyObservableCollection<ChangeEntry>(_entries);
    }

    /// <summary>
    /// Fired after each <see cref="Log"/> or <see cref="Clear"/>.
    /// Subscribers refresh derived state (e.g. status footers,
    /// "review changes" command's CanExecute).
    /// </summary>
    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate) { return _entries.Count; }
        }
    }

    /// <summary>
    /// True iff at least one entry is unsaved. Mirrors the host VM's
    /// <c>IsDirty</c> flag; both are cleared on the same events.
    /// </summary>
    public bool HasUnsavedChanges => Count > 0;

    /// <summary>
    /// Append one entry. <paramref name="category"/> groups related
    /// operations in the summary dialog (e.g. "Sockets", "Dye",
    /// "Field edit"); <paramref name="summary"/> is the per-entry
    /// human-readable description; <paramref name="details"/> is an
    /// optional longer description shown on hover / in a detail
    /// pane.
    /// </summary>
    public void Log(string category, string summary, string? details = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentException.ThrowIfNullOrEmpty(summary);
        var entry = new ChangeEntry(DateTime.Now, category, summary, details);
        lock (_gate) { _entries.Add(entry); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drop every entry. Called by the host on Save success +
    /// fresh Load so the journal reflects only edits since the
    /// last persistence event.
    /// </summary>
    public void Clear()
    {
        bool anything;
        lock (_gate)
        {
            anything = _entries.Count > 0;
            _entries.Clear();
        }
        if (anything)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// One row in the change journal. Timestamp is local; the UI
/// renders it as HH:mm so 100+ entries from one session stay
/// compact.
/// </summary>
public sealed record ChangeEntry(
    DateTime Timestamp,
    string Category,
    string Summary,
    string? Details)
{
    /// <summary>Display-formatted timestamp (HH:mm).</summary>
    public string TimestampText => Timestamp.ToString("HH:mm",
        System.Globalization.CultureInfo.InvariantCulture);
}
