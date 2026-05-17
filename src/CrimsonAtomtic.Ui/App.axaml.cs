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

            // Icon cache wiring. Fixed location under
            // %LOCALAPPDATA%\CrimsonAtomtic\IconCache\ — matches where
            // the save-backup tree lives, so all of the editor's
            // per-user data sits under one root. Pearl Abyss owns the
            // artwork, so we don't bundle them; Tools → Extract Icons
            // populates this directory from the user's game install.
            localization.ConfigureIconProvider(
                CrimsonAtomtic.Ui.Services.IconProvider.ResolveRoot(paths.LocalAppDataDirectory));
            // Wire the static converter singleton so XAML icon-column
            // bindings can resolve immediately. Re-pointed whenever
            // Tools → Extract Icons completes (the provider is rebuilt
            // so its FileCount snapshot reflects the freshly-written
            // .webp files).
            CrimsonAtomtic.Ui.Services.ItemKeyToIconConverter.Provider = localization.Icons;

            // NPC portrait cache wiring. Same %LOCALAPPDATA% root as
            // the icon cache — sibling PortraitCache folder. Unlike
            // icons there's no "extract all" pre-pass; portraits are
            // resolved on-demand per CharacterKey when a dialog asks
            // for one (mercenary list is small enough that batching
            // isn't worth it).
            localization.ConfigurePortraitProvider(
                CrimsonAtomtic.Ui.Services.PortraitProvider.ResolveRoot(paths.LocalAppDataDirectory));

            // Backup service: pure file-system orchestrator, no native deps.
            // Lives for the app lifetime alongside the loader so the same
            // %LOCALAPPDATA%\CrimsonAtomtic\Backups\ tree is the single
            // source of truth for the Restore dialog.
            var backupService = new CrimsonAtomtic.Ui.Services.SaveBackupService(paths);
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader, paths, localization, backupService),
            };

            // First-launch legal disclaimer. Show AFTER the main window
            // has rendered once so the user sees the editor's frame
            // behind the modal — clearer that "Quit" exits the whole
            // app, not just dismisses a splash. The Opened event is
            // the Avalonia-idiomatic post-paint hook; we capture the
            // already-loaded `settings` (above) so the dialog reuses
            // the same snapshot the secondary-language wiring used.
            var startupSettings = settings;
            mainWindow.Opened += async (_, _) =>
            {
                var accepted = await DisclaimerDialog.ShowIfNotAcceptedAsync(
                    mainWindow, startupSettings, paths.LocalAppDataDirectory);
                if (!accepted)
                {
                    desktop.Shutdown();
                }
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
