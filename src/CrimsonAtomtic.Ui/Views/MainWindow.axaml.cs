using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Position + size of the window the last time it was in
    /// <see cref="WindowState.Normal"/>. When the OS reports a
    /// transition Normal → Maximized → Normal, Avalonia 12 on Windows
    /// can land the restored window stretched across multiple monitors
    /// (especially common on dual-monitor setups). Snapshotting before
    /// the maximize and re-applying on restore puts the window back on
    /// the monitor it started from at its original size.
    /// </summary>
    private PixelPoint? _normalPosition;
    private double _normalWidth;
    private double _normalHeight;
    private WindowState _previousWindowState = WindowState.Normal;

    // Deferred-commit snapshot state. On Windows + Avalonia 12 the
    // property-change order during a maximize transition is
    // (Width/Height first, WindowState second), so reading
    // WindowState inside the Width/Height handler still sees Normal
    // when the values are already the maximized dimensions. Capturing
    // synchronously into _normalWidth/_normalHeight at that point
    // poisons the snapshot — the next restore then lands at
    // near-maximized size. Defer the commit one dispatcher tick at
    // Background priority so any concurrent WindowState change in the
    // same Win32 message has been propagated; the commit re-checks
    // WindowState and abandons when it has flipped to non-Normal.
    private double _pendingWidth;
    private double _pendingHeight;
    private PixelPoint _pendingPosition;
    private bool _snapshotCommitScheduled;

    // Last "non-minimized" state. _previousWindowState is overwritten on every
    // transition, so after Maximized → Minimized it is already Minimized and
    // can't tell us the window was minimized FROM a maximized state. Track that
    // separately here so OnClosingSaveState can persist Maximized=true when the
    // window is closed while minimized-from-maximized.
    private WindowState _lastNonMinimizedState = WindowState.Normal;

    // Set once the window has closed so a deferred (Background-posted) restore
    // re-apply that fires afterwards can't set Position/Width/Height on a
    // torn-down window.
    private bool _closed;

    // ── Cross-restart placement persistence (AttachWindowState) ──────────────
    // Restores last-session position / size / maximized state, validated
    // against the monitors present THIS session (a window saved on a now-absent
    // second monitor is reset to a centered default). Reuses the normal-vs-
    // maximized snapshot above so un-maximizing a restored window lands right.
    private WindowStateStore? _stateStore;
    private bool _restorePending;     // a saved record was applied; validate on Opened
    private double _defaultWidth;     // XAML default, used when resetting off-screen
    private double _defaultHeight;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Position isn't an AvaloniaProperty in 12 — listen via the
        // explicit event instead. Width / Height are AvaloniaProperty
        // and flow through OnPropertyChanged.
        PositionChanged += OnPositionChanged;
        // Seed the snapshot with the XAML-declared default so the very
        // first restore (without a prior maximize) still has something
        // sane to fall back to.
        _normalWidth = Width;
        _normalHeight = Height;
        _pendingWidth = Width;
        _pendingHeight = Height;
        _pendingPosition = Position;
        // Remember the XAML default size as the reset fallback for when a
        // restored placement turns out to be off every current monitor.
        _defaultWidth = Width;
        _defaultHeight = Height;
        // Block any still-queued Background restore re-apply from touching the
        // torn-down window once we've closed.
        Closed += (_, _) => _closed = true;
        // Close-on-dirty confirm — Closing fires before the window
        // tears down so we can cancel + show the modal + re-close.
        Closing += OnWindowClosing;
    }

    /// <summary>
    /// Set true once the user has confirmed via the change-summary
    /// dialog that they want to close (either Save+exit or Discard).
    /// Subsequent Closing events bypass the confirm so the second
    /// Close() call goes through.
    /// </summary>
    private bool _allowCloseWithoutPrompt;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowCloseWithoutPrompt) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.Journal.HasUnsavedChanges) return;
        // Cancel the default close — we'll re-trigger after the
        // user makes a choice. Avalonia 12's Closing event allows
        // this synchronously; the awaitable dialog continues after
        // the early return.
        e.Cancel = true;
        var result = await ChangeSummaryDialog.ShowAsync(this, vm.Journal, closingContext: true);
        switch (result)
        {
            case ChangeSummaryDialogResult.Save:
                // Attempt save; on success the journal clears + we
                // close. On failure the dialog stays implicitly
                // dismissed but the close is aborted so the user
                // can try again.
                //
                // SaveCommand is an AsyncRelayCommand. Await the actual
                // task (ExecuteAsync) rather than the fire-and-forget
                // Execute so that:
                //   (a) a write failure surfaces in the catch below
                //       instead of being rethrown on the dispatcher and
                //       crashing the AOT process during teardown, and
                //   (b) the window stays open while SaveAsync's
                //       structural-edit confirm dialog runs — calling
                //       Close() synchronously (as before) tore that modal
                //       down, resolving its await to "cancel" and
                //       silently abandoning the save.
                try
                {
                    if (vm.SaveCommand.CanExecute(null))
                    {
                        await vm.SaveCommand.ExecuteAsync(null);
                    }
                }
                catch (System.Exception ex)
                {
                    await ConfirmDialog.ShowAlertAsync(this,
                        UiText.Get("SaveFailedTitle", "Save failed"),
                        UiText.Format("SaveFailedBody",
                            "Could not write save: {0}. The app stayed open so you can retry or use Discard.",
                            ex.Message));
                    return;
                }
                // Only close if the save actually went through. Declining
                // the structural-edit warning returns without writing and
                // without throwing; closing then would discard the edits
                // silently. The journal is cleared on a successful write.
                if (vm.Journal.HasUnsavedChanges)
                {
                    return;
                }
                _allowCloseWithoutPrompt = true;
                Close();
                break;
            case ChangeSummaryDialogResult.Discard:
                _allowCloseWithoutPrompt = true;
                Close();
                break;
            case ChangeSummaryDialogResult.Cancel:
            default:
                // Stay open — leave _allowCloseWithoutPrompt false
                // so a subsequent close re-prompts.
                break;
        }
    }

    private async void OnReviewChangesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.Journal.HasUnsavedChanges)
        {
            var title = (Application.Current?.FindResource("ChangeSummaryNoChangesTitle") as string)
                        ?? "No pending changes";
            var body = (Application.Current?.FindResource("ChangeSummaryNoChangesBody") as string)
                       ?? "Nothing has been edited.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }
        var result = await ChangeSummaryDialog.ShowAsync(this, vm.Journal, closingContext: false);
        // Review mode: Save fires the Save command; Discard is
        // routed to "reload the save without writing" since the
        // user explicitly opted into discarding edits without
        // closing.
        switch (result)
        {
            case ChangeSummaryDialogResult.Save:
                // Await the async save so a write failure is caught here
                // rather than rethrown on the dispatcher and crashing the
                // process.
                try
                {
                    if (vm.SaveCommand.CanExecute(null))
                    {
                        await vm.SaveCommand.ExecuteAsync(null);
                    }
                }
                catch (System.Exception ex)
                {
                    await ConfirmDialog.ShowAlertAsync(this,
                        UiText.Get("SaveFailedTitle", "Save failed"),
                        UiText.Format("SaveFailedBody",
                            "Could not write save: {0}. The app stayed open so you can retry or use Discard.",
                            ex.Message));
                }
                break;
            case ChangeSummaryDialogResult.Discard:
                if (vm.LoadedPath is { } path
                    && vm.LoadSaveCommand.CanExecute(path))
                {
                    // Reload the save bytes from disk → journal +
                    // IsDirty both clear via the normal Load flow. A
                    // reload failure (file deleted / locked since load)
                    // must surface as an alert, not crash the app.
                    try
                    {
                        vm.LoadSaveCommand.Execute(path);
                    }
                    catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
                    {
                        await ConfirmDialog.ShowAlertAsync(this,
                            UiText.Get("OpenSaveFailedTitle", "Could not open save"),
                            UiText.Format("OpenSaveFailedBody",
                                "Failed to load the save file: {0}. The file may be corrupt, locked by the running game, or from an unsupported version.",
                                ex.Message));
                    }
                }
                break;
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Reject an off-screen / maximized-origin top-left so a transition transient can't
        // poison the snapshot (the position twin of the size >0 guard in CommitSnapshot).
        if (WindowState == WindowState.Normal && IsSnapshotPositionAcceptable(Position))
        {
            _pendingPosition = Position;
            ScheduleSnapshotCommit();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var newState = change.GetNewValue<WindowState>();
            HandleWindowStateTransition(_previousWindowState, newState);
            _previousWindowState = newState;
            if (newState != WindowState.Minimized)
                _lastNonMinimizedState = newState;
        }
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            // Stash, but commit later (see the field comments). Reading
            // WindowState here is unreliable on the to-maximized
            // transition.
            if (WindowState == WindowState.Normal)
            {
                _pendingWidth = Width;
                _pendingHeight = Height;
                ScheduleSnapshotCommit();
            }
        }
    }

    /// <summary>
    /// Queue a deferred snapshot commit on the dispatcher at
    /// Background priority. Coalesced: scheduling while a commit is
    /// already pending is a no-op.
    /// </summary>
    private void ScheduleSnapshotCommit()
    {
        if (_snapshotCommitScheduled)
        {
            return;
        }
        _snapshotCommitScheduled = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            CommitSnapshot,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Apply the pending snapshot — but only if the window is still in
    /// <see cref="WindowState.Normal"/> at the time of the commit. If
    /// a WindowState change snuck in during the same dispatcher tick
    /// (the bug we're guarding against), the state will have flipped
    /// by now and the pending values are the maximized dimensions
    /// we don't want.
    /// </summary>
    private void CommitSnapshot()
    {
        _snapshotCommitScheduled = false;
        if (WindowState != WindowState.Normal)
        {
            return;
        }
        if (_pendingWidth > 0)
        {
            _normalWidth = _pendingWidth;
        }
        if (_pendingHeight > 0)
        {
            _normalHeight = _pendingHeight;
        }
        // Position promotion is gated like the stash (was unconditional): an off-screen /
        // maximized-origin top-left must never latch into _normalPosition, or the next
        // restore re-applies it (the "second restore jumps to 0,0" bug).
        if (IsSnapshotPositionAcceptable(_pendingPosition))
        {
            _normalPosition = _pendingPosition;
        }
    }

    private void HandleWindowStateTransition(WindowState oldState, WindowState newState)
    {
        // Leaving Normal: snapshot was already kept fresh by the
        // property-change branch above. Nothing to do here.
        if (oldState == WindowState.Normal && newState != WindowState.Normal)
        {
            return;
        }
        // Returning to Normal: re-apply the snapshot. Without this, restoring from
        // Maximized on a multi-monitor setup can land the window straddling both screens.
        //
        // DEFER the re-apply to a Background dispatcher tick rather than setting
        // Position/Width/Height synchronously inside the WindowState change. The
        // synchronous set fought the OS mid-un-maximize: it emitted a maximized-origin
        // (~0,0) position transient that latched into the snapshot and re-surfaced as the
        // window jumping to 0,0 on the SECOND restore, and lost the placement race against a
        // window BORN maximized (cross-restart). Background priority runs FIFO after the
        // current Win32 message burst, so the re-apply lands AFTER any OS placement and
        // OVERRIDES it; re-seeding _pending* stops the events it triggers from being read as
        // a fresh user move.
        if (newState == WindowState.Normal && oldState != WindowState.Normal)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                ReapplyNormalSnapshot,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Force the live window back onto the saved normal snapshot. Posted at Background
    /// priority from the Maximized/Minimized → Normal transition. Gated so it never runs
    /// during startup placement restore (<see cref="_restorePending"/> — that path is the
    /// sole authority until validated) nor on a closed window.
    /// </summary>
    private void ReapplyNormalSnapshot()
    {
        if (_closed || _restorePending) return;
        if (WindowState != WindowState.Normal) return;
        // Size before position so final DPI scaling resolves against the target monitor.
        if (_normalWidth > 0)
        {
            Width = _normalWidth;
        }
        if (_normalHeight > 0)
        {
            Height = _normalHeight;
        }
        if (_normalPosition is { } pos)
        {
            Position = pos;
        }
        // Re-seed the stash so the Position/Size events this re-apply triggers aren't
        // mis-read as a user move (which would thrash, or re-poison, the snapshot).
        _pendingPosition = _normalPosition ?? _pendingPosition;
        _pendingWidth = _normalWidth;
        _pendingHeight = _normalHeight;
    }

    /// <summary>
    /// Wire up cross-restart window placement. Called once by App right after
    /// construction and BEFORE the window is shown, so saved geometry is applied
    /// without a visible jump. Validation against the monitors present THIS
    /// session happens on Opened (Screens is only reliable once the platform
    /// window exists).
    /// </summary>
    public void AttachWindowState(WindowStateStore store)
    {
        _stateStore = store;
        Closing += OnClosingSaveState;

        if (store.Load() is not { } r)
        {
            return; // first run / corrupt → keep XAML defaults + OS placement
        }

        // Clamp restored size to at least the window minimum so a tiny saved
        // size can't make the window unusable.
        double w = Math.Max(r.Width, MinWidth);
        double h = Math.Max(r.Height, MinHeight);
        var pos = new PixelPoint(r.X, r.Y);

        // Take placement over from the OS and seed BOTH the live geometry and
        // the normal-vs-maximized snapshot, so a later un-maximize restores to
        // exactly this rect.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = pos;
        Width = w;
        Height = h;
        _normalPosition = pos;
        _normalWidth = w;
        _normalHeight = h;
        _pendingPosition = pos;
        _pendingWidth = w;
        _pendingHeight = h;

        _restorePending = true;
        if (r.Maximized)
        {
            // Show maximized immediately; the snapshot above is the restore-down
            // target. Avalonia doesn't reliably keep the normal rect across a
            // maximize, and a born-maximized window has no valid OS restore
            // rectangle — so the first un-maximize is corrected by the deferred
            // ReapplyNormalSnapshot, which consumes this seeded snapshot.
            WindowState = WindowState.Maximized;
        }

        Opened += OnOpenedValidatePlacement;
    }

    /// <summary>
    /// Once the platform window exists (Screens reliable), confirm the restored
    /// NORMAL rect is reachable on some current monitor. If not — the monitor was
    /// removed or the resolution shrank — reset to a default size centered on the
    /// primary screen. Runs once.
    /// </summary>
    private void OnOpenedValidatePlacement(object? sender, EventArgs e)
    {
        Opened -= OnOpenedValidatePlacement;
        if (!_restorePending) return;
        _restorePending = false;

        var screens = CurrentScreenWorkingAreas();
        if (screens.Count == 0) return; // no screen info — leave as restored

        // RenderScaling reflects the monitor the window is CURRENTLY on; it is
        // fine for the visibility tolerance check (the restored rect is on that
        // monitor), but must NOT be used to convert the default size for primary-
        // screen centering — see ResetToDefaultPlacement / PrimaryScaling().
        double scale = RenderScaling > 0 ? RenderScaling : 1.0;
        int rx = _normalPosition?.X ?? 0;
        int ry = _normalPosition?.Y ?? 0;
        int rw = (int)Math.Round(_normalWidth * scale);
        int rh = (int)Math.Round(_normalHeight * scale);

        if (WindowPlacement.IsVisibleEnough(rx, ry, rw, rh, screens))
            return; // restored placement is reachable — keep it

        ResetToDefaultPlacement();
    }

    /// <summary>Drop to a default-size window centered on the primary monitor.</summary>
    private void ResetToDefaultPlacement()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        Width = _defaultWidth;
        Height = _defaultHeight;

        // Convert the default DIP size to physical px using the PRIMARY screen's
        // own scaling — NOT the window's current RenderScaling. On a mixed-DPI
        // multi-monitor setup the two differ, and using the current monitor's
        // scale offsets the window from center (can push the title bar off the
        // working area).
        var primary = PrimaryWorkingArea();
        double primaryScale = PrimaryScaling();
        int pw = (int)Math.Round(_defaultWidth * primaryScale);
        int ph = (int)Math.Round(_defaultHeight * primaryScale);
        var (cx, cy) = WindowPlacement.CenterIn(primary, pw, ph);
        var pos = new PixelPoint(cx, cy);
        Position = pos;

        _normalPosition = pos;
        _normalWidth = _defaultWidth;
        _normalHeight = _defaultHeight;
        _pendingPosition = pos;
        _pendingWidth = _defaultWidth;
        _pendingHeight = _defaultHeight;
    }

    /// <summary>
    /// Persist the current placement on close (geometry still valid). Runs
    /// independently of the dirty-save confirm in <see cref="OnWindowClosing"/> —
    /// Closing multicasts to every handler regardless of <c>e.Cancel</c>, so the
    /// placement is saved even when the first close attempt is cancelled to prompt.
    /// </summary>
    private void OnClosingSaveState(object? sender, WindowClosingEventArgs e)
    {
        if (_stateStore is null) return;

        bool maximized = WindowState == WindowState.Maximized
            || (WindowState == WindowState.Minimized && _lastNonMinimizedState == WindowState.Maximized);

        int x, y;
        double w, h;
        if (WindowState == WindowState.Normal)
        {
            // Live geometry is authoritative when Normal.
            x = Position.X;
            y = Position.Y;
            w = Width;
            h = Height;
        }
        else
        {
            // Maximized / minimized report sentinel geometry — use the tracked
            // normal snapshot so we persist the real restore-down rect.
            var p = _normalPosition ?? Position;
            x = p.X;
            y = p.Y;
            w = _normalWidth;
            h = _normalHeight;
        }

        _stateStore.Save(new WindowStateRecord(x, y, w, h, maximized));
    }

    /// <summary>
    /// True when (<paramref name="pos"/>, the current normal size) shows a grabbable chunk
    /// on some current monitor — the position twin of <see cref="CommitSnapshot"/>'s size
    /// &gt;0 guard. Stops a transition-artifact / off-screen top-left from latching into the
    /// snapshot. No screen info yet (pre-show) => accept, so the very first open-time stash
    /// isn't dropped.
    /// </summary>
    private bool IsSnapshotPositionAcceptable(PixelPoint pos)
    {
        var screens = CurrentScreenWorkingAreas();
        if (screens.Count == 0) return true;
        return WindowPlacement.IsVisibleEnough(
            pos.X, pos.Y,
            (int)Math.Round(_pendingWidth), (int)Math.Round(_pendingHeight),
            screens);
    }

    /// <summary>Current monitors' working areas as plain physical-pixel rects.</summary>
    private List<(int X, int Y, int W, int H)> CurrentScreenWorkingAreas()
    {
        var list = new List<(int X, int Y, int W, int H)>();
        var all = Screens?.All;
        if (all == null) return list;
        foreach (var s in all)
        {
            var wa = s.WorkingArea;
            list.Add((wa.X, wa.Y, wa.Width, wa.Height));
        }
        return list;
    }

    /// <summary>Primary monitor working area (fallback: first screen, then 1080p).</summary>
    private (int X, int Y, int W, int H) PrimaryWorkingArea()
    {
        var primary = Screens?.Primary;
        if (primary == null)
        {
            var all = Screens?.All;
            if (all != null && all.Count > 0) primary = all[0];
        }
        if (primary != null)
        {
            var wa = primary.WorkingArea;
            return (wa.X, wa.Y, wa.Width, wa.Height);
        }
        return (0, 0, 1920, 1080);
    }

    /// <summary>
    /// Primary monitor's scaling (fallback: first screen, then 1.0). Used for the
    /// DIP→physical-px conversion when centering on the primary screen, so a
    /// mixed-DPI second monitor's RenderScaling can't skew the centered result.
    /// </summary>
    private double PrimaryScaling()
    {
        var primary = Screens?.Primary;
        if (primary == null)
        {
            var all = Screens?.All;
            if (all != null && all.Count > 0) primary = all[0];
        }
        double s = primary?.Scaling ?? 1.0;
        return s > 0 ? s : 1.0;
    }

    /// <summary>
    /// VM reference held so we can detach scroll-event handlers when
    /// the DataContext flips. Null until OnDataContextChanged sees the
    /// first MainWindowViewModel; tracked here rather than rechecking
    /// DataContext each time so the detach path uses the exact instance
    /// we subscribed to.
    /// </summary>
    private MainWindowViewModel? _wiredVm;

    /// <summary>
    /// Rebuild Tools → Secondary Language with the languages the
    /// LocalizationProvider discovered, plus a "checked" indicator on
    /// the currently-active one. Keeps the static "English only" entry
    /// at index 0 (Tag = "").
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // Detach from the previous VM (if any) before binding the new
        // one — otherwise a swap leaks handlers and stale targets keep
        // scrolling out from under the user.
        if (_wiredVm is { } prev)
        {
            prev.FieldScrollRequested -= ScrollFieldIntoView;
            prev.ElementScrollRequested -= ScrollElementIntoView;
            prev.ConfirmRequested = null;
            prev.AlertRequested = null;
            _wiredVm = null;
        }
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        vm.FieldScrollRequested += ScrollFieldIntoView;
        vm.ElementScrollRequested += ScrollElementIntoView;
        // Bridge VM-side confirm requests to the actual modal dialog.
        // Lambda captures the Window directly so the VM never sees a
        // Window type — keeps the MVVM boundary intact.
        vm.ConfirmRequested = (title, msg) => ConfirmDialog.ShowAsync(this, title, msg);
        vm.AlertRequested = (title, msg) => ConfirmDialog.ShowAlertAsync(this, title, msg);
        _wiredVm = vm;
        var menu = SecondaryLanguageMenu;
        // Clear any dynamic entries (everything past the static "English
        // only" placeholder at index 0).
        while (menu.Items.Count > 1)
        {
            menu.Items.RemoveAt(menu.Items.Count - 1);
        }
        var current = vm.SecondaryLanguage ?? string.Empty;
        // Update the static "English only" toggle state.
        if (menu.Items[0] is MenuItem englishOnly)
        {
            englishOnly.Icon = string.IsNullOrEmpty(current)
                ? new TextBlock { Text = "✓" }
                : null;
        }
        foreach (var code in vm.AvailableLanguages)
        {
            if (string.Equals(code, "eng", System.StringComparison.OrdinalIgnoreCase))
            {
                continue; // "English only" is the no-secondary path
            }
            var item = new MenuItem
            {
                Header = code,
                Tag = code,
                Icon = string.Equals(code, current, System.StringComparison.OrdinalIgnoreCase)
                    ? new TextBlock { Text = "✓" }
                    : null,
            };
            item.Click += OnSetSecondaryLanguageClick;
            menu.Items.Add(item);
        }

        // Refresh the Font Size submenu's check marks. Each static
        // menu item carries a Tag with its size value; the entry
        // whose Tag matches FontSize gets the ✓ icon.
        RefreshFontSizeCheckmarks(vm.FontSize);

        // Refresh the UI Language submenu's check marks. Auto is
        // marked when the user has no persisted override; otherwise
        // the entry whose Tag matches the active code gets ✓.
        RefreshUiLanguageCheckmarks(vm.IsUiLanguageAuto, vm.CurrentUiLanguage);
    }

    /// <summary>
    /// Walk the Font Size submenu and set ✓ next to the entry whose
    /// numeric tag matches <paramref name="active"/>. Tolerant of
    /// non-preset values: nothing is checked when no preset is within
    /// 0.01 of the active size. Called once from
    /// <see cref="OnDataContextChanged"/> at startup and again from
    /// <see cref="OnSetFontSizeClick"/> after each menu pick so the
    /// check mark tracks the live state.
    /// </summary>
    private void RefreshFontSizeCheckmarks(double active)
    {
        foreach (var item in FontSizeMenu.Items)
        {
            if (item is not MenuItem mi || mi.Tag is not string tagText)
            {
                continue;
            }
            if (!double.TryParse(tagText, System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }
            mi.Icon = System.Math.Abs(size - active) < 0.01
                ? new TextBlock { Text = "✓" }
                : null;
        }
    }

    private void OnSetFontSizeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || sender is not MenuItem mi
            || mi.Tag is not string tagText
            || !double.TryParse(tagText, System.Globalization.CultureInfo.InvariantCulture, out var size))
        {
            return;
        }
        vm.SetFontSize(size);
        RefreshFontSizeCheckmarks(vm.FontSize);
    }

    /// <summary>
    /// Scroll <paramref name="row"/> into the viewport of the fields
    /// DataGrid. Triggered by <see cref="MainWindowViewModel.FieldScrollRequested"/>
    /// right after a breadcrumb pop restores the previously-drilled row.
    /// Posted via <see cref="Avalonia.Threading.Dispatcher"/> so the
    /// scroll happens after the binding cycle has populated the grid
    /// — calling ScrollIntoView before the row is materialized is a
    /// silent no-op on Avalonia 12.
    /// </summary>
    private void ScrollFieldIntoView(FieldRowViewModel row)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            FieldsDataGrid.ScrollIntoView(row, null);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollElementIntoView(ElementRowViewModel row)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ElementsDataGrid.ScrollIntoView(row, null);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private async void OnOpenSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Default to the Crimson Desert save folder when it exists.
        // TryGetFolderFromPathAsync returns null for non-existent paths,
        // in which case the picker falls back to its own default.
        IStorageFolder? startLocation = null;
        var startPath = vm.DefaultOpenSaveStartingPath;
        if (Directory.Exists(startPath))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(startPath);
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Crimson Desert save",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter =
            [
                new FilePickerFileType("Save files") { Patterns = ["*.save"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            // LoadSave throws CrimsonSaveException on any parse / HMAC /
            // IO failure (corrupt file, file picked mid-write by the
            // running game, unsupported schema version). LoadSaveCommand
            // is a synchronous RelayCommand, so the throw propagates here
            // — guard it or it crashes the dispatcher.
            try
            {
                vm.LoadSaveCommand.Execute(path);
            }
            catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
            {
                await ConfirmDialog.ShowAlertAsync(this,
                    UiText.Get("OpenSaveFailedTitle", "Could not open save"),
                    UiText.Format("OpenSaveFailedBody",
                        "Failed to load the save file: {0}. The file may be corrupt, locked by the running game, or from an unsupported version.",
                        ex.Message));
            }
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.HasSave)
        {
            return;
        }

        // Suggest the directory of the currently-loaded save plus a
        // ".edited.save" filename so the user doesn't accidentally
        // overwrite the original by clicking through the picker.
        IStorageFolder? startLocation = null;
        var loaded = vm.LoadedPath;
        if (!string.IsNullOrEmpty(loaded))
        {
            var dir = Path.GetDirectoryName(loaded);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(dir);
            }
        }
        var suggestedName = !string.IsNullOrEmpty(loaded)
            ? $"{Path.GetFileNameWithoutExtension(loaded)}.edited.save"
            : "save.edited.save";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Crimson Desert save as…",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedName,
            DefaultExtension = "save",
            FileTypeChoices =
            [
                new FilePickerFileType("Save files") { Patterns = ["*.save"] },
                FilePickerFileTypes.All,
            ],
        });
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            // Save As writes the file then re-loads it to re-anchor the
            // handle, so it can throw on either the write or the verify
            // re-load. Await the async command + catch so a failure is
            // reported instead of crashing the process.
            try
            {
                await vm.SaveAsCommand.ExecuteAsync(path);
            }
            catch (System.Exception ex)
            {
                await ConfirmDialog.ShowAlertAsync(this,
                    UiText.Get("SaveFailedTitle", "Save failed"),
                    UiText.Format("SaveFailedBody",
                        "Could not write save: {0}. The app stayed open so you can retry or use Discard.",
                        ex.Message));
            }
        }
    }

    /// <summary>
    /// Quality-of-life for the edit textbox: Enter commits, Escape reverts.
    /// IsDefault on the Apply button already handles Enter on its own, but
    /// catching it here means the user doesn't have to tab to the button.
    /// </summary>
    private void OnEditValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedField is not { } row)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            vm.CommitFieldEditCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.RevertFieldEditCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void OnSetSecondaryLanguageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (sender is not MenuItem mi)
        {
            return;
        }
        // Tag carries the language code ("" = English only).
        var code = mi.Tag as string;
        vm.SetSecondaryLanguage(string.IsNullOrEmpty(code) ? null : code);
        // Re-paint the check marks.
        OnDataContextChanged(this, System.EventArgs.Empty);
    }

    private void OnSetUiLanguageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (sender is not MenuItem mi)
        {
            return;
        }
        // Tag carries the language code; empty string = Auto (clear
        // override, fall back to OS detection).
        var code = mi.Tag as string;
        vm.SetUiLanguage(string.IsNullOrEmpty(code) ? null : code);
        RefreshUiLanguageCheckmarks(vm.IsUiLanguageAuto, vm.CurrentUiLanguage);
    }

    /// <summary>
    /// Walk the UI Language submenu and set ✓ next to the active
    /// entry. When <paramref name="isAuto"/> is true the "Auto" entry
    /// (empty-string Tag) gets the mark; otherwise the entry whose
    /// Tag matches <paramref name="activeCode"/> (case-insensitive)
    /// is marked. Called from <see cref="OnDataContextChanged"/> at
    /// startup and from <see cref="OnSetUiLanguageClick"/> after each
    /// menu pick.
    /// </summary>
    private void RefreshUiLanguageCheckmarks(bool isAuto, string activeCode)
    {
        foreach (var item in UiLanguageMenu.Items)
        {
            if (item is not MenuItem mi || mi.Tag is not string tagText)
            {
                continue;
            }
            bool isThisOne;
            if (string.IsNullOrEmpty(tagText))
            {
                isThisOne = isAuto;
            }
            else
            {
                isThisOne = !isAuto && string.Equals(tagText, activeCode, System.StringComparison.OrdinalIgnoreCase);
            }
            mi.Icon = isThisOne ? new TextBlock { Text = "✓" } : null;
        }
    }

    private void OnBrowseLocalizationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        // Don't open the dialog when there's no PALOC loaded — the empty
        // grid would just confuse the user. The status footer already
        // explains why; we keep the menu item enabled so the affordance
        // stays discoverable.
        if (!vm.Localization.IsLoaded)
        {
            return;
        }
        var child = new LocalizationSearchWindow
        {
            DataContext = new LocalizationSearchViewModel(vm.Localization),
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Browse Items. Same degrade-silently rule as Browse
    /// Localization: needs an iteminfo bridge loaded to be useful,
    /// so skip opening when no items are available — the status
    /// footer is the user-facing signal that bootstrap didn't find
    /// the install.
    /// </summary>
    /// <summary>
    /// File → Restore from Backup… opens the picker dialog. The dialog
    /// surfaces every backup currently under
    /// %LOCALAPPDATA%\CrimsonAtomtic\Backups\; click-Restore on a row
    /// fires the picker VM's RestoreRequested event which routes to
    /// <c>MainWindowViewModel.RestoreFromBackupAsync</c>. Same
    /// decoupling pattern as Browse Items / "+ Bag".
    /// </summary>
    private void OnRestoreFromBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        var restoreVm = new RestoreFromBackupViewModel(vm.BackupService);
        restoreVm.RestoreRequested += entry =>
        {
            vm.RestoreFromBackupAsync(entry).SafeFireAndForget();
        };
        var dialog = new RestoreFromBackupWindow
        {
            DataContext = restoreVm,
        };
        dialog.Show(this);
    }

    /// <summary>
    /// Tools → Edit Item Dyes… handler. Opens the master dye-editor
    /// dialog which lists every item with a non-empty
    /// <c>_itemDyeDataList</c>. Per-row Edit button opens a child
    /// slot-editor dialog. Both dialogs flip the main VM's dirty flag
    /// on close if any Apply succeeded.
    /// </summary>
    private void OnEditItemDyesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is not { } loadedPath
            || vm.Summary is null)
        {
            return;
        }
        DyeEditorViewModel masterVm;
        try
        {
            masterVm = new DyeEditorViewModel(
                vm.GetSaveLoader(), vm.Localization, loadedPath);
        }
        catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
        {
            _ = ConfirmDialog.ShowAlertAsync(this,
                UiText.Get("DyeScanFailedTitle", "Could not scan dyed items"),
                $"{ex.Message} (code {ex.ErrorCode})");
            return;
        }
        // No upfront "no dyed items" check: the dialog opens
        // immediately with "Loading dyed items…" in the footer; once
        // RefreshAsync below finishes, the same footer flips to
        // "No dyed items found in this save." when the scan returns
        // empty — single code path for both states.

        // Per-row Edit → open the child slot editor.
        masterVm.EditRequested += row =>
        {
            DyeSlotEditorViewModel childVm;
            try
            {
                childVm = new DyeSlotEditorViewModel(
                    vm.GetSaveLoader(), vm.Localization, vm.Journal, loadedPath, row);
            }
            catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
            {
                _ = ConfirmDialog.ShowAlertAsync(this,
                    UiText.Get("DyeSlotOpenFailedTitle", "Could not open slot editor"),
                    $"{ex.Message} (code {ex.ErrorCode})");
                return;
            }
            var child = new DyeSlotEditorWindow { DataContext = childVm };
            child.Closed += (_, _) =>
            {
                if (childVm.IsDirty)
                {
                    masterVm.NotifyChildApplied();
                    masterVm.RefreshAsync().SafeFireAndForget();
                }
            };
            child.Show(this);
        };

        // Per-row "+ Add" → run slot picker → materialize dye element
        // → patch its _dyeSlotNo to the user-picked value. The slot
        // picker requires LookupDyeSlotCount to have returned non-null
        // at scan time (which is the gate for surfacing the + Add
        // button), so we can re-derive the count here without another
        // null check.
        masterVm.AddDyeRequested += async row =>
        {
            if (row.GamedataSlotCount is not int slotCount || slotCount <= 0)
            {
                return;
            }
            var pickerVm = new DyeSlotPickerViewModel(
                row.ItemKey, row.ItemName, slotCount, vm.Localization);
            int? pickedSlot = await DyeSlotPickerWindow.ShowAsync(this, pickerVm);
            if (pickedSlot is not int slotIdx)
            {
                return;
            }
            try
            {
                var loader = vm.GetSaveLoader();
                // Step 1: materialize the _itemDyeDataList with one
                // default-empty element (count=1, all fields absent).
                loader.SetObjectListPresent(
                    row.BlockIndex,
                    row.BuildPathToItem(),
                    (int)row.DyeListFieldIndex,
                    makePresent: true);
                // Step 2: patch _dyeSlotNo on the new element to the
                // picked slot. _dyeSlotNo is an i8 (signed), but slot
                // values 0..N-1 always fit in a u8 representation; the
                // wire encoding is identical for non-negative values.
                var newElementPath = new[]
                {
                    new PathStep(row.FirstStepFieldIndex, row.FirstStepElementIndex),
                    new PathStep(row.SecondStepFieldIndex, row.SecondStepElementIndex),
                    new PathStep(row.DyeListFieldIndex, 0u),
                };
                var dyeSlotNoFieldIdx = ResolveDyeSlotNoFieldIndex(
                    loader, loadedPath, row, newElementPath);
                if (dyeSlotNoFieldIdx is int fieldIdx)
                {
                    loader.SetScalarFieldPresent(
                        row.BlockIndex, newElementPath, fieldIdx,
                        makePresent: true,
                        initialBytes: [(byte)slotIdx]);
                }
            }
            catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex) when (ex.ErrorCode == -16)
            {
                // NOT_FOUND from SetObjectListPresent: no sibling block
                // provides a template element. Tell the user how to
                // unblock it (dye one item in-game first).
                var noTplTitle = (string?)this.FindResource("DyeEditorAddNoTemplateTitle")
                                 ?? "No dye template in this save";
                var noTplBody = (string?)this.FindResource("DyeEditorAddNoTemplateBody")
                                ?? "Dye one item in-game first to establish a template.";
                await ConfirmDialog.ShowAlertAsync(this, noTplTitle, noTplBody);
                return;
            }
            catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
            {
                var failTitle = (string?)this.FindResource("DyeEditorAddFailedTitle")
                                ?? "Could not add dye";
                await ConfirmDialog.ShowAlertAsync(this, failTitle,
                    $"{ex.Message} (code {ex.ErrorCode})");
                return;
            }
            masterVm.NotifyChildApplied();
            await masterVm.RefreshAsync();
        };

        var master = new DyeEditorWindow { DataContext = masterVm };
        master.Closed += (_, _) =>
        {
            if (masterVm.IsDirty)
            {
                vm.MarkDirtyFromExternalEdit();
            }
        };
        master.Show(this);
        // Kick off the initial dyed-item scan AFTER the window paints.
        // The VM constructor only stashes inputs + sets IsLoading=true,
        // so the dialog renders immediately with "Loading dyed items…"
        // in the footer; the actual block walk runs on the thread pool.
        masterVm.RefreshAsync().SafeFireAndForget();
    }

    /// <summary>
    /// After <see cref="ISaveLoader.SetObjectListPresent"/> materializes
    /// the dye list with one default-empty element, look up the field
    /// index of <c>_dyeSlotNo</c> within that element so the caller can
    /// <see cref="ISaveLoader.SetScalarFieldPresent"/> the user's
    /// picked slot value. Returns <c>null</c> when the element can't
    /// be navigated (defensive — shouldn't happen if the SetObjectListPresent
    /// call just succeeded).
    /// </summary>
    private static int? ResolveDyeSlotNoFieldIndex(
        ISaveLoader loader,
        string savePath,
        DyeEditorItemRow row,
        ReadOnlySpan<PathStep> pathToNewElement)
    {
        var top = loader.LoadBlockDetails(savePath, row.BlockIndex);
        BlockDetails? cursor = top;
        foreach (var step in pathToNewElement)
        {
            if (cursor is null) return null;
            var field = cursor.Fields.FirstOrDefault(f => f.FieldIndex == step.FieldIndex);
            if (field is null) return null;
            if (field.Child is { } locatorChild
                && field.Elements is not { Count: > 0 })
            {
                cursor = locatorChild;
                continue;
            }
            if (field.Elements is { } elements && step.ElementIndex < elements.Count)
            {
                cursor = elements[(int)step.ElementIndex];
                continue;
            }
            return null;
        }
        if (cursor is null) return null;
        var slotNo = cursor.Fields.FirstOrDefault(
            f => string.Equals(f.Name, "_dyeSlotNo", StringComparison.Ordinal));
        return slotNo?.FieldIndex;
    }

    /// <summary>
    /// Tools → Vendor Buyback handler. Walks every
    /// <c>StoreSaveData._storeDataList</c> store, drills into its
    /// <c>_storeSoldItemDataList</c>, and surfaces a per-row Remove
    /// action. v1 scope: view + remove; "move back to inventory" is a
    /// planned follow-up. Read-only when no save is loaded (menu item
    /// gated on HasSave).
    /// </summary>
    private async void OnVendorBuybackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is not { } loadedPath
            || vm.Summary is not { Blocks: { } blocks })
        {
            return;
        }
        var buybackVm = ViewModels.VendorBuybackViewModel.TryCreate(
            vm.GetSaveLoader(),
            vm.Localization,
            vm.Journal,
            loadedPath,
            blocks);
        if (buybackVm is null)
        {
            var title = (string?)this.FindResource("VendorBuybackNotAvailableTitle")
                        ?? "No buyback entries";
            var body = (string?)this.FindResource("VendorBuybackNotAvailableBody")
                       ?? "No sold items in any store's buyback queue.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }
        var child = new VendorBuybackWindow { DataContext = buybackVm };
        // Per-row Jump → close the buyback dialog + navigate the main
        // window's block tree to this sold item's ItemSaveData so the
        // generic per-field editor can drive stack / endurance / sockets
        // / dye edits exactly like an inventory item.
        buybackVm.JumpToItemRequested += row =>
        {
            child.Close();
            vm.NavigateToVendorBuybackItemAsync(
                row.BlockIndex,
                row.StoreElementIdx,
                row.BuybackElementIdx).SafeFireAndForget();
        };
        child.Closed += (_, _) =>
        {
            if (buybackVm.IsDirty)
            {
                vm.MarkDirtyFromExternalEdit();
            }
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → World Map handler. Opens the dialog with whatever
    /// basemap path the user last picked (from
    /// <see cref="Services.AppSettings.WorldMapPath"/>); if that file
    /// no longer exists / never set, the dialog opens empty and the
    /// user picks via the toolbar's "Pick Map…" button. Markers come
    /// from <c>crimson_save_list_field_positions</c> on the current
    /// save — gated on <see cref="MainWindowViewModel.LoadedPath"/>
    /// since there's nothing to ping without a save.
    /// </summary>
    private void OnWorldMapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is null)
        {
            return;
        }

        var paths = vm.GetPlatformPaths();
        var settings = Services.AppSettingsStore.Load(paths.LocalAppDataDirectory);
        var savedPath = settings.WorldMapPath;
        var bitmap = Services.WorldMapBasemapService.TryLoad(savedPath);
        // savedPath may have pointed to a file the user deleted /
        // moved — surface that by clearing the persisted path so the
        // dialog opens in the "no map selected" state instead of
        // pretending it's still configured.
        var pathForVm = bitmap is not null ? savedPath : null;

        var mapVm = new ViewModels.WorldMapViewModel(
            vm.GetSaveLoader(),
            paths,
            vm.Localization,
            bitmap,
            pathForVm);

        var window = new WorldMapWindow { DataContext = mapVm };
        window.Show(this);
    }

    /// <summary>
    /// Tools → Edit Abyss Gates… handler. Walks the loaded save
    /// asynchronously to build the per-gate list, then opens the
    /// dialog. Closes the dialog and flips the main VM's dirty flag
    /// when the user has applied at least one toggle. Read-only when
    /// no save is loaded (menu item gated on HasSave).
    /// </summary>
    private async void OnEditAbyssGatesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is not { } loadedPath
            || vm.Summary is not { Blocks: { } blocks })
        {
            return;
        }
        AbyssGatesViewModel dialogVm;
        try
        {
            dialogVm = await AbyssGatesViewModel.CreateAsync(
                vm.GetSaveLoader(), vm.Localization, vm.Journal, loadedPath, blocks);
        }
        catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
        {
            await ConfirmDialog.ShowAlertAsync(this,
                UiText.Get("AbyssGatesLoadFailedTitle", "Could not load abyss gates"),
                UiText.Format("AbyssGatesLoadFailedBody", "Failed to scan save: {0} (code {1})",
                    ex.Message, ex.ErrorCode));
            return;
        }

        if (dialogVm.Rows.Count == 0)
        {
            var title = (Application.Current?.FindResource("AbyssGatesNotAvailableTitle") as string)
                        ?? "No abyss gates";
            var body = (Application.Current?.FindResource("AbyssGatesNotAvailableBody") as string)
                       ?? dialogVm.StatusMessage ?? "Scan returned 0 rows.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }

        var child = new AbyssGatesWindow { DataContext = dialogVm };
        child.Closed += (_, _) =>
        {
            if (dialogVm.IsDirty)
            {
                vm.MarkDirtyFromExternalEdit();
            }
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Unlock Mounts… handler. Opens the mount-unlock dialog over
    /// the loaded save: sigil mounts grant a Sigil of Solidarity into Quest
    /// Artifacts (use in-game to finish); the dragon is transplanted +
    /// knowledge-injected directly. The dialog routes each row back into
    /// <see cref="MainWindowViewModel.UnlockMountAsync"/>, which owns the
    /// loader and flips the main VM's dirty flag itself — so no close-time
    /// dirty handler is needed here. Gated on <c>HasSave</c>.
    /// </summary>
    private void OnUnlockMountsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is null
            || vm.Summary is not { Blocks: not null })
        {
            return;
        }
        var dialogVm = new MountUnlockViewModel(vm, vm.Localization);
        var child = new MountUnlockWindow { DataContext = dialogVm };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Edit Knowledge… handler. Enumerates knowledgeinfo on a
    /// background thread (CreateAsync), then opens the per-category /
    /// multi-select Knowledge dialog. Inject routes back into
    /// <see cref="MainWindowViewModel.LearnKnowledgeAsync"/> which flips the
    /// main VM's dirty flag itself. Gated on <c>HasSave</c>.
    /// </summary>
    private async void OnEditKnowledgeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is null
            || vm.Summary is not { Blocks: not null })
        {
            return;
        }
        var dialogVm = await KnowledgeEditorViewModel.CreateAsync(vm, vm.Localization);
        dialogVm.ConfirmRequested = (title, msg) => ConfirmDialog.ShowAsync(this, title, msg);
        var child = new KnowledgeEditorWindow { DataContext = dialogVm };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Complete Sealed Abyss Artifact Challenges. Scans for
    /// eligible challenges, then opens a checkbox preview so the user
    /// picks which to mark complete (replaces the former one-shot bulk
    /// command). The dialog opens whenever iteminfo carries any
    /// <c>Sealed_Abyss_Artifact_*</c> row — even if the strict scan found
    /// no candidates — so the user can still opt into the broad scan from
    /// there. Only when there's no relevant data at all (no SA artifacts in
    /// iteminfo) does it skip the dialog and surface the breakdown in the
    /// status footer.
    /// </summary>
    private async void OnCompleteSealedArtifactChallengesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is null
            || vm.Summary is not { Blocks: not null })
        {
            return;
        }
        var dialogVm = await SealedArtifactChallengeViewModel.CreateAsync(vm);
        if (!dialogVm.HasRelevantData)
        {
            // No Sealed_Abyss_Artifact_* rows in iteminfo at all — the broad
            // scan couldn't surface anything either, so there's nothing to open
            // the dialog for. Surface the breakdown in the status footer.
            vm.BulkOpStatus = dialogVm.NoCandidatesSummary;
            return;
        }
        dialogVm.ConfirmRequested = (title, msg) => ConfirmDialog.ShowAsync(this, title, msg);
        var child = new SealedArtifactChallengeWindow { DataContext = dialogVm };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Edit Faction Nodes. Scans faction strongholds, then opens a
    /// checkbox dialog to discover / set their <c>_factionState</c>. When
    /// the save has no faction nodes, surfaces that in the status footer
    /// instead of opening an empty dialog.
    /// </summary>
    private async void OnEditFactionNodesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.LoadedPath is null
            || vm.Summary is not { Blocks: not null })
        {
            return;
        }
        var dialogVm = await FactionNodeEditorViewModel.CreateAsync(vm);
        if (!dialogVm.HasNodes)
        {
            vm.BulkOpStatus = FactionNodeEditorViewModel.NoNodesSummary;
            return;
        }
        dialogVm.ConfirmRequested = (title, msg) => ConfirmDialog.ShowAsync(this, title, msg);
        var child = new FactionNodeEditorWindow { DataContext = dialogVm };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Find Items… handler. Opens the cross-bag item-search
    /// dialog powered by <see cref="ISaveLoader.ListInventoryItems"/>.
    /// Read-only; the menu item is gated on <c>HasSave</c> so this
    /// only fires with a loaded save.
    /// </summary>
    private void OnFindItemsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        FindItemsViewModel pickerVm;
        try
        {
            pickerVm = new FindItemsViewModel(vm.GetSaveLoader(), vm.Localization);
        }
        catch (InvalidOperationException)
        {
            // HasSave gate is the primary defense, but in case of a
            // race (save unloaded between menu open and click) — no-op.
            return;
        }
        // Wire the "Go" button to navigate the main window. We bring
        // ourselves to the foreground after the jump so the user sees
        // the new selection without having to alt-tab past the
        // still-open Find Items dialog.
        pickerVm.GotoRequested += row =>
        {
            NavigateToFindItemsRowAsync(vm, row).SafeFireAndForget();
        };
        var child = new FindItemsWindow
        {
            DataContext = pickerVm,
        };
        child.Show(this);
    }

    private async Task NavigateToFindItemsRowAsync(MainWindowViewModel vm, FindItemsRow row)
    {
        await vm.NavigateToInventoryItemAsync(row.Record);
        // Surface the main window above the picker so the user sees
        // the freshly-rebuilt nav stack. The picker stays open behind
        // — by design, so the user can pick another row.
        Activate();
    }

    /// <summary>
    /// Tools → Browse Characters / NPCs… handler. Opens the character
    /// picker dialog (mirror of Browse Items, but driven by
    /// <c>characterinfo.pabgb</c> + the portrait pipeline). Read-only;
    /// no Add-to-bag analog — characters aren't items.
    /// </summary>
    private void OnBrowseCharactersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (vm.Localization.CharacterCount == 0)
        {
            return;
        }
        var pickerVm = new CharacterPickerViewModel(vm.Localization);
        var child = new CharacterPickerWindow
        {
            DataContext = pickerVm,
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Browse Character References… handler. Opens a flat list
    /// of every schema-tagged <c>CharacterKey</c> occurrence in the
    /// loaded save (one row per scalar field; one row per element of a
    /// <c>DynamicArray&lt;CharacterKey&gt;</c>). Per-row Jump button
    /// closes the dialog and navigates the main window's block tree
    /// down to the owning top-level block. Gated on <c>HasSave</c>.
    /// </summary>
    private void OnBrowseCharacterRefsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.Summary is not { Blocks: { } blocks })
        {
            return;
        }
        ViewModels.CharacterRefsBrowserViewModel browserVm;
        try
        {
            browserVm = new ViewModels.CharacterRefsBrowserViewModel(
                vm.GetSaveLoader(), vm.Localization, blocks);
        }
        catch (InvalidOperationException)
        {
            // HasSave gate is the primary defense; race on save unload
            // between menu open and click — silent bail.
            return;
        }
        var child = new CharacterRefsBrowserWindow { DataContext = browserVm };
        // Per-row Jump → close the browser dialog + navigate the main
        // window's block tree to the owning top-level block. From
        // there the user drills manually down to the specific field;
        // the flat-list ABI doesn't carry field-level descent paths
        // (a CharacterKey can sit at arbitrary nesting depths and
        // refs-of-refs would explode the row count).
        browserVm.JumpToBlockRequested += blockIdx =>
        {
            child.Close();
            vm.NavigateToTopLevelBlockAsync(blockIdx).SafeFireAndForget();
        };
        child.Show(this);
    }

    /// <summary>
    /// Edit-panel "Pick character…" button: opens the shared
    /// <see cref="CharacterPickerWindow"/> in pick mode. The chosen
    /// CharacterKey is written into the selected field's
    /// <c>RawText</c> so the user can review + click Apply as usual.
    /// Button visibility is bound to
    /// <see cref="MainWindowViewModel.IsSelectedFieldCharacterKey"/>,
    /// so this handler only fires when there's a CharacterKey-typed
    /// scalar selected — the null check is defensive.
    /// </summary>
    private async void OnPickCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm
            || vm.SelectedField is not { } selectedField)
        {
            return;
        }
        if (vm.Localization.CharacterCount == 0)
        {
            return;
        }
        var pickerVm = new CharacterPickerViewModel(vm.Localization, isPickMode: true);
        var dlg = new CharacterPickerWindow { DataContext = pickerVm };
        var picked = await dlg.ShowDialog<uint?>(this);
        if (picked is { } key)
        {
            // Fill the textbox; the user clicks Apply as the explicit
            // commit step. Use invariant culture so the integer-format
            // round-trips identically across locales.
            selectedField.RawText = key.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnBrowseItemsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            OpenAddItemPicker(vm);
        }
    }

    /// <summary>
    /// Per-row "Add Item…" button on the elements DataGrid. Sets the
    /// clicked row as the clone template (so the picker's top bar reads
    /// "from &lt;that row&gt;") and opens the unified Add-Item picker.
    /// </summary>
    private void OnAddItemFromRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (sender is Control { DataContext: ElementRowViewModel row })
        {
            // Becomes the clone donor + drives the live "from X" label.
            vm.SelectedElement = row;
        }
        OpenAddItemPicker(vm);
    }

    /// <summary>
    /// Open the unified Add-Item picker (top-action-bar mode). The picker
    /// names the item to add and where it lands; the "from X" half is
    /// pushed live as the user reselects inventory rows or navigates while
    /// the picker stays open. Routes the Add click through
    /// <see cref="MainWindowViewModel.AddItemToCurrentListAsync"/>.
    /// </summary>
    private void OpenAddItemPicker(MainWindowViewModel vm)
    {
        if (vm.Localization.ItemCount == 0)
        {
            return;
        }
        var pickerVm = new ItemPickerViewModel(vm.Localization)
        {
            ShowTopActionBar = true,
            CanAddToTarget = vm.CanAddItemToCurrentList,
            SourceName = vm.AddItemSourceName,
        };
        pickerVm.AddItemRequested += itemKey =>
        {
            vm.AddItemToCurrentListAsync(itemKey).SafeFireAndForget();
        };
        // "Go to item in save": jump the main window to the item's slot
        // in the loaded save (or report "not in this save"). Mirrors the
        // Find Items "Go" routing.
        pickerVm.GotoItemRequested += itemKey =>
        {
            NavigateToBrowseItemAsync(vm, itemKey).SafeFireAndForget();
        };

        // Live-sync the picker's target bar to the main VM as the user
        // reselects rows / navigates. Unsubscribed on close.
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CanAddItemToCurrentList))
            {
                pickerVm.CanAddToTarget = vm.CanAddItemToCurrentList;
            }
            else if (args.PropertyName == nameof(MainWindowViewModel.AddItemSourceName))
            {
                pickerVm.SourceName = vm.AddItemSourceName;
            }
        };
        vm.PropertyChanged += handler;

        var child = new ItemPickerWindow { DataContext = pickerVm };
        child.Closed += (_, _) => vm.PropertyChanged -= handler;
        child.Show(this);
    }

    /// <summary>
    /// Routes the Browse Items "Go to item in save" button: navigates the
    /// main window to the item's slot in the loaded save (or reports "not
    /// in this save" via the status bar), then surfaces the main window
    /// above the still-open picker — same pattern as
    /// <see cref="NavigateToFindItemsRowAsync"/>.
    /// </summary>
    private async Task NavigateToBrowseItemAsync(MainWindowViewModel vm, uint itemKey)
    {
        await vm.NavigateToItemByKeyAsync(itemKey);
        Activate();
    }

    /// <summary>
    /// Tools → Extract Icons from Game Data. Drives
    /// <see cref="IconExtractionProgressDialog"/> against the loaded
    /// LocalizationProvider, writing into the fixed
    /// <c>%LOCALAPPDATA%\CrimsonAtomtic\IconCache\</c> directory.
    /// Re-seeds the IconProvider on success so the new icons appear
    /// in already-rendered DataGrids without a restart.
    ///
    /// Degrades silently when stringinfo isn't loaded (no game install
    /// found at bootstrap). The status footer is the user-facing
    /// signal for that case.
    /// </summary>
    private async void OnExtractIconsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        var loc = vm.Localization;
        if (!loc.HasStringInfo || string.IsNullOrEmpty(loc.GameRoot))
        {
            // Bootstrap didn't find the install — nothing to extract from.
            return;
        }

        // Always target the canonical IconCache root under LocalAppData.
        // IconProvider creates the directory on construction, so the
        // path is guaranteed to exist at this point.
        var cacheDir = loc.Icons.Root;

        var result = await IconExtractionProgressDialog.RunAsync(
            owner: this,
            localization: loc,
            paz: loc.Paz,
            gameRoot: loc.GameRoot!,
            cacheDirectory: cacheDir,
            overwriteExisting: false);

        // Re-seed the icon provider so newly-written .webp files
        // appear immediately in already-rendered DataGrids — drops
        // the per-key Bitmap dict, refreshes FileCount, repaints.
        if (result is not null && result.Written > 0)
        {
            vm.RefreshIconCache();
        }
    }

    /// <summary>
    /// Tools → Set Game Install Folder. Opens an Avalonia folder picker
    /// for the user to point at their Crimson Desert install when the
    /// auto-probe didn't find it (e.g. Steam library in an unusual
    /// location, Epic install elsewhere, or assets copied out of the
    /// Game Pass WindowsApps tree). Validates the witness file before
    /// persisting; on success, the localization provider re-bootstraps
    /// against the new root so resolved-name columns light up
    /// immediately without restarting.
    /// </summary>
    private async void OnSetGameInstallFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        // Anchor the picker at the currently-resolved game install when
        // there is one, so the user can adjust an existing override
        // without re-navigating from drive C:\.
        IStorageFolder? startLocation = null;
        var current = vm.Localization.GameRoot;
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(current);
        }
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Crimson Desert install folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        });
        if (picked.Count == 0)
        {
            return;
        }
        var folder = picked[0];
        var path = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var ok = vm.SetGameInstallRoot(path);
        if (!ok)
        {
            var title = (string?)this.FindResource("SetGameInstallFolderInvalidTitle")
                        ?? "Invalid install folder";
            var body = (string?)this.FindResource("SetGameInstallFolderInvalidBody")
                       ?? "The selected folder doesn't look like a Crimson Desert install.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
        }
    }

    /// <summary>
    /// Tools → Rename Mercenary. Opens
    /// <see cref="RenameMercenaryWindow"/> bound to a fresh
    /// <see cref="ViewModels.RenameMercenaryViewModel"/> built against
    /// the loaded save. Skips with an alert when the save has no
    /// <c>MercenaryClanSaveData</c> block.
    /// </summary>
    private async void OnRenameMercenaryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        var summary = vm.Summary;
        if (summary?.Blocks is not { Count: > 0 } blocks
            || vm.LoadedPath is not { } path)
        {
            return;
        }
        var renameVm = ViewModels.RenameMercenaryViewModel.TryCreate(
            vm.GetSaveLoader(),
            vm.Localization,
            vm.Journal,
            path,
            blocks);
        if (renameVm is null)
        {
            var title = (string?)this.FindResource("RenameMercenaryNotAvailableTitle")
                        ?? "No mercenaries to rename";
            var body = (string?)this.FindResource("RenameMercenaryNotAvailableBody")
                       ?? "MercenaryClanSaveData not found in this save.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }
        var child = new RenameMercenaryWindow { DataContext = renameVm };
        child.Closed += (_, _) =>
        {
            if (renameVm.IsDirty)
            {
                vm.MarkDirtyFromExternalEdit();
            }
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Edit Item Sockets. Opens
    /// <see cref="SocketEditorWindow"/> with a fresh
    /// <see cref="ViewModels.SocketEditorViewModel"/> built against the
    /// loaded save. Each row's "Change Gem…" button opens a
    /// gem-filtered <see cref="ItemPickerWindow"/> as a child of the
    /// socket editor; the picker's action click routes back into
    /// <see cref="ViewModels.SocketEditorViewModel.ApplyGemPick"/> to
    /// write the new gem ItemKey in-place via
    /// <c>SetScalarField</c>.
    /// </summary>
    private async void OnEditItemSocketsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (vm.Summary is null || vm.LoadedPath is not { } path)
        {
            return;
        }
        ViewModels.SocketEditorViewModel? socketsVm;
        try
        {
            // TryCreate walks every item container via ListAllItems (an
            // FFI enumerator); a save unloaded mid-call / FFI fault throws
            // CrimsonSaveException. This runs inside an async void handler,
            // so an uncaught throw would crash the process — report it
            // instead, matching the other Tools dialogs.
            socketsVm = ViewModels.SocketEditorViewModel.TryCreate(
                vm.GetSaveLoader(),
                vm.Localization,
                vm.Journal,
                path,
                vm.LoadCustomGemSets());
        }
        catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
        {
            await ConfirmDialog.ShowAlertAsync(this,
                UiText.Get("OpenSaveFailedTitle", "Could not open save"),
                UiText.Format("OpenSaveFailedBody",
                    "Failed to load the save file: {0}. The file may be corrupt, locked by the running game, or from an unsupported version.",
                    ex.Message));
            return;
        }
        if (socketsVm is null)
        {
            var title = (string?)this.FindResource("SocketEditorNotAvailableTitle")
                        ?? "No filled sockets";
            var body = (string?)this.FindResource("SocketEditorNotAvailableBody")
                       ?? "No sockets to edit.";
            await ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }
        var child = new SocketEditorWindow { DataContext = socketsVm };
        socketsVm.ChangeGemRequested += row =>
        {
            OpenGemPicker(child, vm, socketsVm, row);
        };
        child.Closed += (_, _) =>
        {
            if (socketsVm.IsDirty)
            {
                vm.MarkDirtyFromExternalEdit();
            }
        };
        child.Show(this);
    }

    /// <summary>
    /// Helper: open a gem-filtered <see cref="ItemPickerWindow"/> as a
    /// child of the Sockets editor. The picker's action click is
    /// routed to <see cref="ViewModels.SocketEditorViewModel.ApplyGemPick"/>
    /// instead of the usual Add-to-bag handler.
    /// </summary>
    private static void OpenGemPicker(
        SocketEditorWindow owner,
        MainWindowViewModel _,
        ViewModels.SocketEditorViewModel socketsVm,
        ViewModels.SocketRow row)
    {
        var pickerVm = new ViewModels.ItemPickerViewModel(
            socketsVm.Localization
                ?? throw new System.InvalidOperationException("localization not attached"),
            ViewModels.SocketEditorViewModel.GemStringKeyPrefixes)
        {
            ActionButtonLabel = "Pick",
            ActionButtonTooltip = "Apply this gem to the selected socket.",
        };
        var picker = new ItemPickerWindow { DataContext = pickerVm };
        pickerVm.AddItemRequested += pickedItemKey =>
        {
            socketsVm.ApplyGemPick(row, pickedItemKey);
            picker.Close();
        };
        picker.Show(owner);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
