using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// The single-writer gate: a second writer waits until the first lets go,
/// a writer that calls another writer does not wait on itself, and
/// <see cref="WriteGate.IsBusy"/> tells synchronous editors to refuse.
/// </summary>
public sealed class WriteGateTests
{
    [Fact]
    public async Task SecondWriterWaitsAndNestedEntryDoesNot()
    {
        var outer = await WriteGate.EnterAsync();
        Assert.True(WriteGate.IsBusy);

        // Re-entry from the same flow returns at once.
        var nested = WriteGate.EnterAsync();
        Assert.True(nested.IsCompleted);
        (await nested).Dispose();
        Assert.True(WriteGate.IsBusy);

        // An unrelated flow (one that does not inherit this flow's context,
        // like a second click) waits for the release.
        Task<bool> other;
        using (ExecutionContext.SuppressFlow())
        {
            other = Task.Run(async () =>
            {
                using var _ = await WriteGate.EnterAsync();
                return WriteGate.IsBusy;
            });
        }
        await Task.Delay(100);
        Assert.False(other.IsCompleted);

        outer.Dispose();
        Assert.True(await other.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(WriteGate.IsBusy);
    }
}
