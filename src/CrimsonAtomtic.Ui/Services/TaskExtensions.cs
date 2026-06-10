using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Helpers for launching fire-and-forget tasks from event handlers and
/// property-changed callbacks without a bare <c>async void</c> / discarded
/// <c>Task</c> that lets an exception escape to the SynchronizationContext.
///
/// <para>
/// On a Native AOT app an unobserved fault either crashes the process (when
/// it reaches the dispatcher) or is swallowed by the finalizer's
/// <see cref="TaskScheduler.UnobservedTaskException"/> path — in both cases
/// the user gets no feedback. <see cref="SafeFireAndForget"/> awaits the
/// task inside an exception boundary so the fault is always observed: routed
/// to an optional handler (e.g. to show an alert) or, by default, traced.
/// </para>
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Observe <paramref name="task"/> on the captured (UI) context and
    /// route any exception to <paramref name="onError"/>, or to
    /// <see cref="Trace"/> when none is supplied, instead of letting it
    /// surface unhandled. Use at the handful of fire-and-forget call sites
    /// where the awaited method's own try/catch is the primary guard and
    /// this is the backstop that keeps a new throw path from crashing the
    /// process.
    /// </summary>
    public static async void SafeFireAndForget(
        this Task task, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            await task.ConfigureAwait(true);
        }
#pragma warning disable CA1031 // backstop: a fire-and-forget task must never crash the process
        catch (Exception ex)
        {
            if (onError is not null)
            {
                onError(ex);
            }
            else
            {
                Trace.TraceError($"Fire-and-forget task faulted: {ex}");
            }
        }
#pragma warning restore CA1031
    }
}
