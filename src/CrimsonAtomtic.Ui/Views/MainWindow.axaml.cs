using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
