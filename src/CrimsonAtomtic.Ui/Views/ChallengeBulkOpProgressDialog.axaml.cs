using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Modal dialog that drives <see cref="ArtifactBulkOpService.RunAsync"/>
/// from the UI. Reports phase + per-iteration progress, supports a
/// single Cancel gesture that aborts between FFI calls (the Rust side
/// itself isn't cancellable mid-call), and turns the Cancel button
/// into Close once the work completes.
/// </summary>
/// <remarks>
/// <para>
/// As of crimson-rs PR B.5 the artifact-remove phase uses a batch C
/// ABI that shares a single body re-emit + re-decode at the end —
/// total Rust-side cost is roughly that of one per-call mutation
/// regardless of how many artifacts are dropped. Typical wall-time
/// is sub-second.
/// </para>
/// <para>
/// File / class name kept as <c>ChallengeBulkOpProgressDialog</c> for
/// XAML compatibility — the historical bulk operation included a
/// challenge-completion phase that was removed after it was verified
/// to corrupt save state. See <see cref="ArtifactBulkOpService"/> for
/// the full rationale.
/// </para>
/// </remarks>
// CA1001: Avalonia Windows aren't disposed by the consumer — the
// framework owns their lifecycle. The CTS is disposed manually in
// the Closed handler. Suppress at the type level (matches the same
// pattern in IconExtractionProgressDialog).
#pragma warning disable CA1001
public sealed partial class ChallengeBulkOpProgressDialog : Window
#pragma warning restore CA1001
{
    private readonly CancellationTokenSource _cts = new();
    private ISaveLoader? _loader;
    private LocalizationProvider? _localization;
    private string? _savePath;
    private IReadOnlyList<BlockSummary>? _blocks;
    private ArtifactBulkOpResult? _result;
    private Exception? _failure;
    private bool _completed;

    public ChallengeBulkOpProgressDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += (_, _) => _cts.Dispose();
    }

    /// <summary>
    /// Open the dialog modally over <paramref name="owner"/> and resolve
    /// to the <see cref="ArtifactBulkOpResult"/> on success or
    /// <c>null</c> when cancelled / errored. Mid-flight cancellation
    /// keeps already-applied changes — the loader's in-memory state
    /// reflects every successful op before the cancel was honoured.
    /// </summary>
    public static async Task<ArtifactBulkOpResult?> RunAsync(
        Window owner,
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dlg = new ChallengeBulkOpProgressDialog
        {
            _loader = loader,
            _localization = localization,
            _savePath = savePath,
            _blocks = blocks,
        };
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    /// <summary>
    /// Counts of partial work done before a cancel / error. Always set
    /// (zeros when the worker never started). Lets the caller report
    /// "partial: applied N of M" in the parent VM's status footer.
    /// </summary>
    public ArtifactBulkOpResult? PartialResult => _result;

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_loader is null || _localization is null
            || _savePath is null || _blocks is null)
        {
            StatusText.Text = "Dialog opened without arguments.";
            ShowFinishedUi();
            return;
        }

        var progress = new Progress<ArtifactBulkOpProgress>(UpdateProgress);
        try
        {
            _result = await ArtifactBulkOpService.RunAsync(
                _loader, _localization, _savePath, _blocks,
                progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancel via button or window close. _result stays null.
        }
#pragma warning disable CA1031 // surface every other exception via StatusText
        catch (Exception ex)
        {
            _failure = ex;
        }
#pragma warning restore CA1031

        ShowFinishedUi();
    }

    private void UpdateProgress(ArtifactBulkOpProgress p)
    {
        PhaseText.Text = p.Phase switch
        {
            ArtifactBulkOpPhase.Scanning       => "Phase 1/2 — Scanning inventories",
            ArtifactBulkOpPhase.ArtifactRemove => "Phase 2/2 — Removing Sealed Abyss Artifacts",
            ArtifactBulkOpPhase.Done           => "Done",
            _                                  => "Working…",
        };
        if (p.Total > 0)
        {
            Progress.Maximum = p.Total;
            Progress.Value = p.Processed;
        }
        StatusText.Text = p.Message;
    }

    private void ShowFinishedUi()
    {
        _completed = true;
        if (_failure is not null)
        {
            HeaderText.Text = "Bulk operation failed";
            StatusText.Text = $"{_failure.GetType().Name}: {_failure.Message}\n\n"
                              + "Partial changes are kept in memory; reload the save without "
                              + "writing to revert.";
        }
        else if (_result is null)
        {
            HeaderText.Text = "Cancelled";
            StatusText.Text = "Already-applied changes are kept in memory.\n"
                              + "Reload the save without writing to revert, or click Save to commit "
                              + "what got through.";
        }
        else
        {
            var r = _result.Value;
            HeaderText.Text = "Done";
            Progress.Value = Progress.Maximum;
            StatusText.Text =
                $"Removed {r.ArtifactsRemoved:N0} Sealed Abyss Artifact(s) from inventories "
                + $"({r.ArtifactItemKeysKnown:N0} known artifact item key(s) in iteminfo).";
        }
        ActionButton.Content = "Close";
        ActionButton.IsEnabled = true;
    }

    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            ActionButton.IsEnabled = false;
            StatusText.Text = "Cancelling… (the current FFI call must finish first — "
                              + "up to ~1 s).";
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_completed && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            // Hold the close until the worker honours cancel — otherwise
            // we'd race the Rust side mid-FFI.
            e.Cancel = true;
            ActionButton.IsEnabled = false;
            StatusText.Text = "Cancelling…";
        }
    }
}
