using System.Runtime.InteropServices;
using Avalonia;

namespace CrimsonAtomtic.Ui;

internal static class Program
{
    // App-wide identity used by the single-instance guard. The ".v1" suffix
    // lets a future release that must run side by side with this one pick a
    // new name.
    // Local\: one instance per user session. Global\ would also need
    // rights a standard account may not have, and would lock out a second
    // user on the same machine.
    private const string SingleInstanceMutexName = "Local\\CrimsonAtomtic.v1";

    [STAThread]
    public static int Main(string[] args)
    {
        using var guard = OperatingSystem.IsWindows()
            ? new WindowsMutexGuard(SingleInstanceMutexName)
            : (Core.ISingleInstanceGuard)new NullSingleInstanceGuard();

        if (!guard.TryAcquire())
        {
            if (OperatingSystem.IsWindows())
            {
                new Services.FileAppLog(new Platform.WindowsPlatformPaths().LogDirectory)
                    .Write("app", "Another instance is already running; this start exits.");
            }
            return 1;
        }

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Picked up by Avalonia's preview tooling. Keep this signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // ── Single-instance helpers ────────────────────────────────────────

    private sealed class WindowsMutexGuard(string name) : Core.ISingleInstanceGuard
    {
        private System.Threading.Mutex? _mutex;
        private bool _owned;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Interoperability",
            "CA1416:Validate platform compatibility",
            Justification = "Constructor only runs on Windows; gated in Main.")]
        public bool TryAcquire()
        {
            try
            {
                _mutex = new System.Threading.Mutex(initiallyOwned: true, name, out _owned);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Threading.WaitHandleCannotBeOpenedException)
            {
                // The name exists but cannot be opened: another instance owns it.
                _owned = false;
            }
            return _owned;
        }

        public void Dispose()
        {
            if (_mutex is null)
            {
                return;
            }
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); } catch { /* best effort */ }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private sealed class NullSingleInstanceGuard : Core.ISingleInstanceGuard
    {
        // Non-Windows platforms ship a real implementation later
        // (file lock under XDG_RUNTIME_DIR). For now they always succeed.
        public bool TryAcquire() => true;
        public void Dispose() { }
    }

    // Keep RuntimeInformation alive so a future cross-platform check
    // doesn't get trimmed away.
    private static readonly bool _ = RuntimeInformation.OSDescription.Length >= 0;
}
