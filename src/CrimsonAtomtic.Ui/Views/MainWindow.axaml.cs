using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
