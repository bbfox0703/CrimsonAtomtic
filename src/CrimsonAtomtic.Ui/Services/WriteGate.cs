namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// One writer at a time for the shared save loader. A multi-call edit
/// running on a background thread must not interleave with another edit:
/// a batch is per handle, so a second writer's calls would land inside the
/// first one's batch, and two writers can compute the same "next free"
/// slot or item number. Every background edit holds the gate for its whole
/// flow; a synchronous edit on the UI thread checks <see cref="IsBusy"/>
/// and refuses while one runs. Re-entering from the same async flow (an
/// edit that calls another) does not wait on itself.
/// </summary>
public static class WriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>True while a background edit holds the gate.</summary>
    public static bool IsBusy => Gate.CurrentCount == 0;

    /// <summary>
    /// Wait for the gate and hold it until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <c>async</c> method: an <see cref="AsyncLocal{T}"/>
    /// set inside one does not flow back to the caller, and the depth must be
    /// visible to the edits the caller goes on to call.
    /// </remarks>
    public static Task<IDisposable> EnterAsync()
    {
        if (Depth.Value > 0)
        {
            Depth.Value++;
            return Task.FromResult<IDisposable>(new Releaser(release: false));
        }
        Depth.Value = 1;
        return WaitAsync();

        static async Task<IDisposable> WaitAsync()
        {
            await Gate.WaitAsync().ConfigureAwait(true);
            return new Releaser(release: true);
        }
    }

    private sealed class Releaser(bool release) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            Depth.Value--;
            if (release)
            {
                Gate.Release();
            }
        }
    }
}
