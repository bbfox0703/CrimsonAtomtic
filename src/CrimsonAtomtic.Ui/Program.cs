using System.Runtime.InteropServices;
using Avalonia;

namespace CrimsonAtomtic.Ui;

internal static class Program
{
    // App-wide identity used by the single-instance guard. A versioned
    // GUID prefix keeps us safe if a future major release wants to allow
    // side-by-side runs.
    private const string SingleInstanceMutexName = "Global\\CrimsonAtomtic.v1";

    [STAThread]
    public static int Main(string[] args)
    {
        using var guard = OperatingSystem.IsWindows()
            ? new WindowsMutexGuard(SingleInstanceMutexName)
            : (Core.ISingleInstanceGuard)new NullSingleInstanceGuard();

        if (!guard.TryAcquire())
        {
            Console.Error.WriteLine("CrimsonAtomtic is already running.");
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
            _mutex = new System.Threading.Mutex(initiallyOwned: true, name, out _owned);
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
