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
            // The bootstrap itself runs once the window is open (see Opened
            // below), so the window appears at once and shows that game data
            // is loading.
            var settings = AppSettingsStore.Load(paths.LocalAppDataDirectory);

            // UI language wiring. Resolve effective code from settings
            // (explicit pick wins) + OS UI culture (auto-detect fallback)
            // and swap the merged ResourceDictionary BEFORE the main
            // window is constructed — that way every initial AXAML
            // resource lookup hits the correct language. AXAML uses
            // DynamicResource so subsequent runtime swaps via Tools →
            // UI Language pick up live without restarting the app.
            var uiLanguage = new UiLanguageService(this);
            // Use the Win32-backed detector — .NET's CultureInfo facade
            // reports InvariantCulture in this build (csproj has
            // <InvariantGlobalization>true</InvariantGlobalization> for
            // AOT binary-size reasons), so CurrentUICulture.Name is "" on
            // every OS and culture-based detect would always fall back to
            // English. GetUserDefaultUILanguage bypasses that.
            var effectiveLanguage = UiLanguageService.ResolveActiveFromOs(
                settings.UiLanguage);
            uiLanguage.Apply(effectiveLanguage);

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

            // Backup service: pure file-system orchestrator, no native deps.
            // Lives for the app lifetime alongside the loader so the same
            // %LOCALAPPDATA%\CrimsonAtomtic\Backups\ tree is the single
            // source of truth for the Restore dialog.
            var backupService = new CrimsonAtomtic.Ui.Services.SaveBackupService(paths);
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(loader, paths, localization, backupService, uiLanguage),
            };

            // Cross-restart placement: load last-session position / size /
            // maximized state and apply BEFORE the window is shown (validation
            // against the monitors present this session happens on Opened).
            // An exception no handler caught (an async void handler, a
            // fire-and-forget task) is shown instead of ending the process.
            AppErrors.Sink = ex => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ConfirmDialog.ShowAlertAsync(mainWindow,
                    UiText.Get("UnhandledErrorTitle", "Something went wrong"),
                    UiText.Format("UnhandledErrorBody",
                        "An operation failed: {0}\n\nThe editor kept running; your edits are still in memory. "
                        + "Save them, or reload the save, before trying again.",
                        ex.Message)).SafeFireAndForget(_ => { }));
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                AppErrors.Report(e.Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                e.SetObserved();
                AppErrors.Report(e.Exception);
            };

            mainWindow.AttachWindowState(
                new CrimsonAtomtic.Ui.Services.WindowStateStore(paths.LocalAppDataDirectory));

            // First-launch legal disclaimer + game-data-version
            // mismatch warning. Both fire AFTER the main window has
            // rendered once so the user sees the editor's frame behind
            // the modal — clearer that "Quit" exits the whole app, not
            // just dismisses a splash. The version-mismatch dialog runs
            // FIRST: if the user opts to quit on a mismatched install,
            // we shouldn't have nagged them with the disclaimer first.
            //
            // Order: opened → version-mismatch (skipped on compatible
            // installs) → disclaimer (skipped if already accepted).
            // Quit on either → desktop.Shutdown.
            var startupSettings = settings;
            mainWindow.Opened += async (_, _) =>
            {
                // Bootstrap with the window disabled: nothing can look a name
                // up while tables load and DLC rows merge into them, which
                // crimson-rs does not allow alongside lookups. The saved
                // secondary language is applied after it (an unknown code
                // is ignored), then the portrait cache keyed on the install.
                var mainVm = (MainWindowViewModel)mainWindow.DataContext!;
                await mainVm.LoadGameDataAsync(() =>
                {
                    localization.TryBootstrapFromGameRoot(paths.GameInstallRoot);
                    if (!string.IsNullOrEmpty(startupSettings.SecondaryLanguage))
                    {
                        localization.SecondaryLanguage = startupSettings.SecondaryLanguage;
                    }
                    return localization.ConfigurePortraitProvider(
                        CrimsonAtomtic.Ui.Services.PortraitProvider.ResolveRoot(paths.LocalAppDataDirectory));
                });
                var versionOk = await GameVersionMismatchDialog.ShowIfMismatchedAsync(
                    mainWindow, localization.GameDataVersion);
                if (!versionOk)
                {
                    desktop.Shutdown();
                    return;
                }
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
