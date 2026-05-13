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
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _normalPosition = Position;
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
            // Keep the snapshot up to date while we're in Normal state
            // so user resizes feed into the restore target.
            if (WindowState == WindowState.Normal)
            {
                _normalWidth = Width;
                _normalHeight = Height;
            }
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
    /// Rebuild Tools → Secondary Language with the languages the
    /// LocalizationProvider discovered, plus a "checked" indicator on
    /// the currently-active one. Keeps the static "English only" entry
    /// at index 0 (Tag = "").
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }
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

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
