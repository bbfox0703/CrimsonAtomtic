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
            // Composition root for the UI. NativeSaveLoader P/Invokes
            // into vendor/crimson-rs/target/release/crimson_rs.dll, which
            // .\scripts\build_rust.ps1 produces and the Ui csproj copies
            // next to CrimsonAtomtic.exe.
            ISaveLoader loader = new NativeSaveLoader();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
