using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using Xunit;

namespace CrimsonAtomtic.Tests;

public sealed class PlaceholderSaveLoaderTests
{
    [Fact]
    public void Load_ReturnsCannedSummary()
    {
        var loader = new PlaceholderSaveLoader();

        var summary = loader.Load(@"C:\fake\slot0\save.save");

        Assert.Equal(2, summary.Version);
        Assert.True(summary.HmacOk);
        Assert.Equal(101, summary.SchemaTypeCount);
        Assert.Equal(1_112, summary.TocEntryCount);
        Assert.NotEmpty(summary.Blocks);
        Assert.All(summary.Blocks, b => Assert.False(string.IsNullOrEmpty(b.ClassName)));
    }

    [Fact]
    public void Load_PullsSlotNameFromParentDirectory()
    {
        var loader = new PlaceholderSaveLoader();

        var summary = loader.Load(@"C:\fake\slot7\save.save");

        Assert.Equal("slot7", summary.SlotName);
    }

    [Fact]
    public void Load_RejectsEmptyPath()
    {
        var loader = new PlaceholderSaveLoader();

        Assert.Throws<ArgumentException>(() => loader.Load(""));
    }

    [Fact]
    public void Load_RespectsCancellation()
    {
        var loader = new PlaceholderSaveLoader();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            loader.Load(@"C:\fake\slot0\save.save", cts.Token));
    }
}
