using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Platform;
using CrimsonAtomtic.Ui.Services;
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
            // Composition root for the UI. NativeSaveLoader / PazExtractor /
            // PalocCatalog all P/Invoke into
            // vendor/crimson-rs/target/release/crimson_rs.dll, which
            // .\scripts\build_rust.ps1 produces and the Ui csproj copies
            // next to CrimsonAtomtic.exe.
            var loader = new NativeSaveLoader();
            var paz = new NativePazExtractor();
            var localization = new LocalizationProvider(paz);
            // Free native handles on app exit. The OS reclaims the
            // process memory regardless, but a clean Dispose keeps the
            // SafeHandle finalizer bookkeeping honest.
            desktop.Exit += (_, _) =>
            {
                loader.Dispose();
                localization.Dispose();
            };

            var paths = new WindowsPlatformPaths();

            // Best-effort localization bootstrap. Failure (no game install
            // detected, PAZ extraction error, PALOC parse error) leaves
            // the provider in a "not loaded" state — the editor still
            // functions, just without localized name resolution.
            localization.TryBootstrapFromGameRoot(paths.GameInstallRoot);

            // Apply the user's previously-saved secondary-language pick
            // (if any). The provider silently rejects unknown codes so a
            // stale settings file from a pre-discovery build degrades.
            var settings = AppSettingsStore.Load(paths.LocalAppDataDirectory);
            if (!string.IsNullOrEmpty(settings.SecondaryLanguage))
            {
                localization.SecondaryLanguage = settings.SecondaryLanguage;
            }

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader, paths, localization),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
