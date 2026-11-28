using System.Diagnostics;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Where an exception goes when no caller handled it: an <c>async void</c>
/// handler, a fire-and-forget task, a binding. The app sets
/// <see cref="Sink"/> to show the error to the user, so a failure is
/// reported instead of ending the process with the user's edits in it.
/// </summary>
public static class AppErrors
{
    /// <summary>Shows one error to the user; set once at startup.</summary>
    public static Action<Exception>? Sink { get; set; }

    /// <summary>Report <paramref name="ex"/> through <see cref="Sink"/>, or trace it when none is set.</summary>
    public static void Report(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Trace.TraceError($"Unhandled: {ex}");
        AppLog.Write("errors", ex.ToString());
        Sink?.Invoke(ex);
    }
}
