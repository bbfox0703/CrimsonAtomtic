using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// The stale-snapshot guard tool windows use before they write: any
/// mutation after the snapshot makes it stale, inside a deferred batch and
/// across an abort too, and <see cref="SnapshotGuard.Resync"/> takes the
/// owner's own write back out.
/// </summary>
public sealed class SnapshotGuardTests
{
    [Fact]
    public void Guard_GoesStaleOnAnyMutationAndResyncClearsIt()
    {
        var path = LiveSaves.Newest();
        if (path is null) return;
        using var loader = new NativeSaveLoader();
        loader.Load(path);
        var guard = new SnapshotGuard(loader);
        Assert.False(guard.IsStale);

        var value = loader.LoadBlockDetails(path, 0).Fields[0].Value;
        var bytes = BitConverter.GetBytes(uint.Parse(value[..value.IndexOf(' ')],
            System.Globalization.CultureInfo.InvariantCulture));
        loader.SetScalarField(0, 0, bytes);
        Assert.True(guard.IsStale);

        guard.Resync();
        Assert.False(guard.IsStale);

        // An aborted batch leaves the tree as it was, but the guard cannot
        // tell that from a change, so it still reads as stale.
        loader.BeginDeferredRedecode();
        loader.SetScalarField(0, 0, bytes);
        loader.AbortDeferredRedecode();
        Assert.True(guard.IsStale);
    }

    [Fact]
    public void Guard_WithNoSaveLoaded_IsNotStale()
    {
        using var loader = new NativeSaveLoader();
        Assert.False(new SnapshotGuard(loader).IsStale);
    }
}
