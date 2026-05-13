using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Modal dialog that drives <see cref="IconExtractionService"/> from
/// the UI. Reports per-batch progress, supports a single Cancel
/// gesture, and turns its Cancel button into Close once the work
/// completes (success, cancellation, or error). The completed
/// <see cref="IconExtractionResult"/> — or <c>null</c> on cancel /
/// error — is returned through <see cref="RunAsync"/>.
/// </summary>
// CA1001: Avalonia Windows aren't disposed by the consumer — the
// framework owns their lifecycle. The CTS is disposed manually in
// the Closed handler instead. Suppress the analyzer's "class owns a
// disposable, must be IDisposable" complaint at the type level.
#pragma warning disable CA1001
public sealed partial class IconExtractionProgressDialog : Window
#pragma warning restore CA1001
{
    private readonly CancellationTokenSource _cts = new();
    private LocalizationProvider? _localization;
    private IPazExtractor? _paz;
    private string? _gameRoot;
    private string? _cacheDirectory;
    private bool _overwriteExisting;
    private IconExtractionResult? _result;
    private Exception? _failure;
    private bool _completed;

    public IconExtractionProgressDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        // CA1001: dispose the linked cancellation source once the
        // window's gone. After Closed fires the dialog won't be reused
        // — RunAsync constructs a fresh one per invocation.
        Closed += (_, _) => _cts.Dispose();
    }

    /// <summary>
    /// Open the dialog modally over <paramref name="owner"/>, kick off
    /// the extraction once it's shown, and resolve to the
    /// <see cref="IconExtractionResult"/> on success or <c>null</c>
    /// when cancelled / errored.
    /// </summary>
    public static async Task<IconExtractionResult?> RunAsync(
        Window owner,
        LocalizationProvider localization,
        IPazExtractor paz,
        string gameRoot,
        string cacheDirectory,
        bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dlg = new IconExtractionProgressDialog
        {
            _localization = localization,
            _paz = paz,
            _gameRoot = gameRoot,
            _cacheDirectory = cacheDirectory,
            _overwriteExisting = overwriteExisting,
        };
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_localization is null || _paz is null
            || _gameRoot is null || _cacheDirectory is null)
        {
            // Misconfigured — shouldn't happen through RunAsync.
            StatusText.Text = "Dialog opened without extraction arguments.";
            ShowFinishedUi();
            return;
        }

        // Progress<T> captures the current SynchronizationContext at
        // construction time. We're on the UI thread here, so callbacks
        // marshal back to UI automatically — no Dispatcher.Post needed.
        var progress = new Progress<IconExtractionProgress>(UpdateProgress);

        try
        {
            _result = await Task.Run(() => IconExtractionService.RunAsync(
                _localization!, _paz!, _gameRoot!, _cacheDirectory!,
                _overwriteExisting, progress, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Cancelled via the button or window close. _result stays null.
        }
#pragma warning disable CA1031 // surfaced to the user via StatusText
        catch (Exception ex)
        {
            _failure = ex;
        }
#pragma warning restore CA1031

        ShowFinishedUi();
    }

    private void UpdateProgress(IconExtractionProgress p)
    {
        if (p.Total <= 0)
        {
            return;
        }
        Progress.Maximum = p.Total;
        Progress.Value = p.Processed;
        StatusText.Text =
            $"Processed {p.Processed:N0} of {p.Total:N0} • written: {p.Written:N0} • failed: {p.Failed:N0}";
    }

    private void ShowFinishedUi()
    {
        _completed = true;
        if (_failure is not null)
        {
            HeaderText.Text = "Extraction failed";
            StatusText.Text = $"{_failure.GetType().Name}: {_failure.Message}";
        }
        else if (_result is null)
        {
            HeaderText.Text = "Extraction cancelled";
            StatusText.Text =
                "The cache was not fully populated. Run the action again to resume; "
                + "previously-written icons are kept.";
        }
        else
        {
            var r = _result;
            HeaderText.Text = "Extraction complete";
            Progress.Maximum = r.Total;
            Progress.Value = r.Total;
            StatusText.Text =
                $"Wrote {r.Written:N0} of {r.Total:N0} icons.\n"
                + $"Skipped — already cached: {r.SkippedAlreadyCached:N0}, "
                + $"no icon entry: {r.SkippedNoIcon:N0}, "
                + $"name unresolved: {r.SkippedNoString:N0}, "
                + $"DDS missing: {r.SkippedNotInArchive:N0}.\n"
                + $"Failed: {r.Failed:N0}.";
        }
        ActionButton.Content = "Close";
    }

    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }
        // First click: request cooperative cancel. The
        // OperationCanceledException catch in OnOpened will fire
        // when the worker honours it, then ShowFinishedUi runs.
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            ActionButton.IsEnabled = false;
            StatusText.Text = "Cancelling…";
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Closing via the X button before the work is done also cancels.
        if (!_completed && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            // Don't actually close yet — let the worker exit cleanly
            // and ShowFinishedUi flip the state, then the user clicks
            // Close again.
            e.Cancel = true;
            ActionButton.IsEnabled = false;
            StatusText.Text = "Cancelling…";
        }
    }
}
