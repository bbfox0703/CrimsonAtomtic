using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Attaches the "restore to the pre-maximize rect" behaviour to any resizable
/// <see cref="Window"/> — the drift-free maximize→restore fix, without cross-restart
/// persistence. On Avalonia 12 (Windows), restoring a maximized window can land it
/// stretched across monitors or at the wrong spot instead of where it was before the
/// maximize; this snapshots the last NORMAL geometry and re-applies it on the way back.
///
/// <para>
/// The snapshot/decision logic lives in the pure, unit-tested
/// <see cref="WindowRestoreState"/>; this class only wires it to a live window's events.
/// It is an ATTACH HELPER rather than a base <c>Window</c> class (as UE5CEDumper's
/// <c>ManagedDialogWindow</c> is) because CrimsonAtomtic's dialogs are XAML-rooted — a
/// one-line <c>ManagedWindowRestore.Attach(this)</c> in the code-behind ctor avoids
/// re-rooting 20 <c>.axaml</c> files and any control-theme lookup surprises.
/// </para>
///
/// <para>
/// Seeding happens on <see cref="Window.Opened"/> (CenterOwner sets the position only
/// after the window is shown, so the ctor can't read it). The deferred commit handles the
/// Windows quirk where Width/Height change BEFORE WindowState during a maximize: stash on
/// the size/position change, then commit one Background-priority dispatcher tick later and
/// re-check WindowState — abandoning the stash if it has flipped to maximized. The re-apply
/// on the way back to Normal is ALSO deferred to a Background tick rather than done
/// synchronously inside the WindowState change: a synchronous set mid-un-maximize emitted a
/// maximized-origin (~0,0) position transient that latched into the snapshot and surfaced as
/// the window jumping to 0,0 on the SECOND restore.
/// </para>
///
/// <para>
/// <see cref="Views.MainWindow"/> keeps its own equivalent (entangled with cross-restart
/// persistence + monitor validation) and is intentionally not routed through this helper.
/// </para>
/// </summary>
public sealed class ManagedWindowRestore
{
    private readonly Window _window;
    private readonly WindowRestoreState _restore = new();
    private WindowState _previousWindowState = WindowState.Normal;
    private bool _commitScheduled;
    // Set once the window has closed so a deferred re-apply that fires afterwards can't
    // touch a torn-down window (Position/Size on a closed handle).
    private bool _closed;

    private ManagedWindowRestore(Window window) => _window = window;

    /// <summary>
    /// Wire the restore behaviour onto <paramref name="window"/>. Call once from the
    /// window's code-behind constructor, after <c>InitializeComponent()</c>. The returned
    /// instance keeps itself alive via the window's event subscriptions for the window's
    /// lifetime; the caller can ignore it.
    /// </summary>
    public static ManagedWindowRestore Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var m = new ManagedWindowRestore(window);
        window.PositionChanged += m.OnPositionChanged;
        window.Opened += m.OnOpenedSeed;
        window.Closed += m.OnClosed;
        // Width / Height / WindowState are AvaloniaProperties — observe via the
        // AvaloniaObject.PropertyChanged event (a behavior can't override the protected
        // OnPropertyChanged the way a Window subclass would).
        window.PropertyChanged += m.OnWindowPropertyChanged;
        return m;
    }

    private List<(int X, int Y, int W, int H)> CurrentScreens()
    {
        var list = new List<(int X, int Y, int W, int H)>();
        var all = _window.Screens?.All;
        if (all == null) return list;
        foreach (var s in all)
        {
            var wa = s.WorkingArea;
            list.Add((wa.X, wa.Y, wa.Width, wa.Height));
        }
        return list;
    }

    private void OnOpenedSeed(object? sender, EventArgs e)
    {
        try
        {
            _restore.SetScreens(CurrentScreens());
            _previousWindowState = _window.WindowState;
            if (_window.WindowState == WindowState.Normal)
                _restore.Seed(_window.Position, _window.Width, _window.Height);
        }
        finally
        {
            _window.Opened -= OnOpenedSeed;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _window.PositionChanged -= OnPositionChanged;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closed -= OnClosed;
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!_restore.Seeded) return;
        if (_window.WindowState == WindowState.Normal)
        {
            _restore.NotePosition(_window.Position);
            ScheduleSnapshotCommit();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Window.WindowStateProperty)
        {
            var newState = change.GetNewValue<WindowState>();
            HandleWindowStateTransition(_previousWindowState, newState);
            _previousWindowState = newState;
        }
        else if (change.Property == Layoutable.WidthProperty || change.Property == Layoutable.HeightProperty)
        {
            // Reading WindowState here is unreliable on the to-maximized transition
            // (Width/Height flip first), so stash now and commit deferred.
            if (_restore.Seeded && _window.WindowState == WindowState.Normal)
            {
                _restore.NoteSize(_window.Width, _window.Height);
                ScheduleSnapshotCommit();
            }
        }
    }

    // Coalesced deferred commit at Background priority so any WindowState change in the
    // same Win32 message has propagated before we promote the stash.
    private void ScheduleSnapshotCommit()
    {
        if (_commitScheduled) return;
        _commitScheduled = true;
        Dispatcher.UIThread.Post(CommitSnapshot, DispatcherPriority.Background);
    }

    private void CommitSnapshot()
    {
        _commitScheduled = false;
        _restore.Commit(_window.WindowState == WindowState.Normal);
    }

    private void HandleWindowStateTransition(WindowState oldState, WindowState newState)
    {
        // Refresh the monitors backing the restore-state position guard — the window may
        // have been dragged to another screen before this transition.
        _restore.SetScreens(CurrentScreens());

        // Leaving Normal: the snapshot is already kept fresh by the change handlers above.
        if (oldState == WindowState.Normal && newState != WindowState.Normal)
            return;
        // Returning to Normal: re-apply the snapshot so the window lands where it was.
        // DEFER the re-apply to a Background dispatcher tick (after the OS's own un-maximize
        // placement has settled) rather than fighting it synchronously mid-transition.
        if (newState == WindowState.Normal && oldState != WindowState.Normal)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || _window.WindowState != WindowState.Normal) return;
                if (!_restore.TryGetRestoreRect(out var pos, out var w, out var h)) return;
                // Size before position so final DPI scaling resolves against the target monitor.
                _window.Width = w;
                _window.Height = h;
                _window.Position = pos;
                _restore.OnRestoreReapplied();
            }, DispatcherPriority.Background);
        }
    }
}
