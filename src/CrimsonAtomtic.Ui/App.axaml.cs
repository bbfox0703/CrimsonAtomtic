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

            // Icon cache wiring. Configured path (settings.json) wins;
            // falls back to <exe-dir>/IconCache/. Pearl Abyss owns the
            // artwork — we don't bundle them, the user points at their
            // own extracted folder.
            var exeDir = System.IO.Path.GetDirectoryName(
                System.AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            localization.ConfigureIconProvider(settings.IconCacheDirectory, exeDir);
            // Wire the static converter singleton so XAML icon-column
            // bindings can resolve immediately. Updated whenever the
            // user re-points the icon path through Tools menu.
            CrimsonAtomtic.Ui.Services.ItemKeyToIconConverter.Provider = localization.Icons;

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader, paths, localization),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
