using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            || vm.LoadedPath is not { } loadedPath)
        {
            return;
        }
        DyeEditorViewModel masterVm;
        try
        {
            masterVm = new DyeEditorViewModel(vm.GetSaveLoader(), vm.Localization, loadedPath);
        }
        catch (CrimsonAtomtic.RustInterop.CrimsonSaveException ex)
        {
            _ = ConfirmDialog.ShowAlertAsync(this,
                "Could not scan dyed items",
                $"{ex.Message} (code {ex.ErrorCode})");
            return;
        }
        if (masterVm.TotalDyedItems == 0)
        {
            var title = (Application.Current?.FindResource("DyeEditorNotAvailableTitle") as string)
                        ?? "No dyed items";
            var body = (Application.Current?.FindResource("DyeEditorNotAvailableBody") as string)
                       ?? "Scan returned 0 rows.";
            _ = ConfirmDialog.ShowAlertAsync(this, title, body);
            return;
        }

        // Per-row Edit → open the child slot editor.
        masterVm.EditRequested += row =>
        {
            DyeSlotEditorViewModel childVm;
            try
            {
                childVm = new DyeSlotEditorViewModel(
                    vm.GetSaveLoader(), vm.Localization, loadedPath, row);
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
                    // Re-scan so the master row count + per-row state
                    // refresh against the new save body.
                    masterVm.Refresh();
                }
            };
            child.Show(this);
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
                vm.GetSaveLoader(), vm.Localization, loadedPath, blocks);
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
        var summary = vm.Summary;
        if (summary?.Blocks is not { Count: > 0 } blocks
            || vm.LoadedPath is not { } path)
        {
            return;
        }
        var socketsVm = ViewModels.SocketEditorViewModel.TryCreate(
            vm.GetSaveLoader(),
            vm.Localization,
            path,
            blocks);
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
