using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
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
                try
                {
                    if (vm.SaveCommand.CanExecute(null))
                    {
                        vm.SaveCommand.Execute(null);
                    }
                }
                catch (System.Exception ex)
                {
                    await ConfirmDialog.ShowAlertAsync(this,
                        "Save failed",
                        $"Could not write save: {ex.Message}. The app stayed open so you can retry or use Discard.");
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
                if (vm.SaveCommand.CanExecute(null))
                {
                    vm.SaveCommand.Execute(null);
                }
                break;
            case ChangeSummaryDialogResult.Discard:
                if (vm.LoadedPath is { } path
                    && vm.LoadSaveCommand.CanExecute(path))
                {
                    // Reload the save bytes from disk → journal +
                    // IsDirty both clear via the normal Load flow.
                    vm.LoadSaveCommand.Execute(path);
                }
                break;
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (WindowState == WindowState.Normal)
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
        _normalPosition = _pendingPosition;
    }

    private void HandleWindowStateTransition(WindowState oldState, WindowState newState)
    {
        // Leaving Normal: snapshot was already kept fresh by the
        // property-change branch above. Nothing to do here.
        if (oldState == WindowState.Normal && newState != WindowState.Normal)
        {
            return;
        }
        // Returning to Normal: re-apply the snapshot. Without this,
        // restoring from Maximized on a multi-monitor setup can land
        // the window straddling both screens.
        if (newState == WindowState.Normal && oldState != WindowState.Normal)
        {
            if (_normalPosition is { } pos)
            {
                Position = pos;
            }
            if (_normalWidth > 0)
            {
                Width = _normalWidth;
            }
            if (_normalHeight > 0)
            {
                Height = _normalHeight;
            }
        }
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
            vm.LoadSaveCommand.Execute(path);
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
            vm.SaveAsCommand.Execute(path);
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
            _ = vm.RestoreFromBackupAsync(entry);
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
                "Could not scan dyed items",
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
                    "Could not open slot editor",
                    $"{ex.Message} (code {ex.ErrorCode})");
                return;
            }
            var child = new DyeSlotEditorWindow { DataContext = childVm };
            child.Closed += (_, _) =>
            {
                if (childVm.IsDirty)
                {
                    masterVm.NotifyChildApplied();
                    _ = masterVm.RefreshAsync();
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
        _ = masterVm.RefreshAsync();
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
            _ = vm.NavigateToVendorBuybackItemAsync(
                row.BlockIndex,
                row.StoreElementIdx,
                row.BuybackElementIdx);
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
                "Could not load abyss gates",
                $"Failed to scan save: {ex.Message} (code {ex.ErrorCode})");
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
            _ = NavigateToFindItemsRowAsync(vm, row);
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
            _ = vm.NavigateToTopLevelBlockAsync(blockIdx);
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
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
        if (vm.Localization.ItemCount == 0)
        {
            return;
        }
        var pickerVm = new ItemPickerViewModel(vm.Localization);
        // PR B.4.2 — wire the picker's "+ Bag" click back into the main
        // window so a click clones-and-patches the item into the
        // currently-displayed inventory list. The handler is fire-and-
        // forget on the UI thread; AddItemToCurrentListAsync drives the
        // FFI on a Task.Run worker. The picker stays decoupled — other
        // call sites can show it without subscribing and the "+ Bag"
        // button still works (just no-ops the click).
        pickerVm.AddItemRequested += itemKey =>
        {
            _ = vm.AddItemToCurrentListAsync(itemKey);
        };
        var child = new ItemPickerWindow
        {
            DataContext = pickerVm,
        };
        child.Show(this);
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
        var socketsVm = ViewModels.SocketEditorViewModel.TryCreate(
            vm.GetSaveLoader(),
            vm.Localization,
            vm.Journal,
            path,
            vm.LoadCustomGemSets());
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
