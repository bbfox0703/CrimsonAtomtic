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

    /// <summary>
    /// Tools → Set Icon Folder. Opens a folder picker (starting at the
    /// currently-configured icon path when one is set, so re-pointing
    /// nearby folders doesn't require re-navigating from scratch);
    /// persists the choice to settings.json on confirm; re-seeds the
    /// IconProvider so the main window's element grid refreshes
    /// without a restart. (Open Item Picker windows don't auto-refresh
    /// — close + reopen to see the change.) Cancels are a no-op.
    /// </summary>
    private async void OnSetIconFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Start the picker at the existing icon folder (if any) so a
        // re-point lands in the right neighbourhood. Falls back to
        // whatever Avalonia chooses when no path is set / the saved
        // path no longer exists.
        IStorageFolder? startLocation = null;
        var currentRoot = vm.Localization.Icons.Root;
        if (!string.IsNullOrEmpty(currentRoot) && Directory.Exists(currentRoot))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(currentRoot);
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick item-icon folder (contains <ItemKey>.webp files)",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        });
        if (folders.Count == 0)
        {
            return;
        }
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        vm.SetIconCacheDirectory(path);
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
        var child = new ItemPickerWindow
        {
            DataContext = new ItemPickerViewModel(vm.Localization),
        };
        child.Show(this);
    }

    /// <summary>
    /// Tools → Extract Icons from Game Data. Drives
    /// <see cref="IconExtractionProgressDialog"/> against the loaded
    /// LocalizationProvider, then — if anything was written — re-seeds
    /// the IconProvider at the same path so the new icons show up in
    /// the elements grid + Item Picker on the next render pass.
    ///
    /// Degrades silently when stringinfo isn't loaded (no game install
    /// found at bootstrap), or when no icon-cache target can be
    /// resolved. The status footer is the user-facing signal for
    /// those cases.
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

        // Target directory probe order matches IconProvider's: configured
        // path (Tools → Set Icon Folder) wins, else fall back to
        // <exe-dir>/IconCache. Either way we ensure the directory
        // exists before the extractor opens it.
        var configured = loc.Icons.Root;
        var cacheDir = !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "IconCache");

        var result = await IconExtractionProgressDialog.RunAsync(
            owner: this,
            localization: loc,
            paz: loc.Paz,
            gameRoot: loc.GameRoot!,
            cacheDirectory: cacheDir,
            overwriteExisting: false);

        // Re-seed the icon provider so newly-written .webp files
        // appear immediately in already-rendered DataGrids. Same code
        // path as the user picking the folder manually via Tools →
        // Set Icon Folder, so all the same cache-invalidation work
        // (drop the per-key Bitmap dict, repaint visible rows) fires.
        if (result is not null && result.Written > 0)
        {
            vm.SetIconCacheDirectory(cacheDir);
        }
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
