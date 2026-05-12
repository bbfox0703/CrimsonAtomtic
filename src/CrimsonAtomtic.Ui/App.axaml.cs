using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.ViewModels;
using CrimsonAtomtic.Ui.Views;

namespace CrimsonAtomtic.Ui;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root for the UI. The placeholder loader will be
            // swapped for a real P/Invoke implementation once the C ABI
            // on vendor/crimson-rs lands.
            ISaveLoader loader = new PlaceholderSaveLoader();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
