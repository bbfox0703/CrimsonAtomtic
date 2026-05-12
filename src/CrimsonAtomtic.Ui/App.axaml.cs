using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Platform;
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
            var loader = new NativeSaveLoader();
            // Free the cached native handle on app exit. Without this the
            // OS still reclaims the process memory, but a clean Dispose
            // keeps the SafeHandle's finalizer book-keeping honest.
            desktop.Exit += (_, _) => loader.Dispose();

            var paths = new WindowsPlatformPaths();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader, paths),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
