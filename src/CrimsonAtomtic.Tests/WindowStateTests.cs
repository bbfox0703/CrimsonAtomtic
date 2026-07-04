using System.Collections.Generic;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure (no-IO) tests for the window-placement persistence + geometry helpers ported
/// from UE5CEDumper: <see cref="WindowStateStore"/> Format/Parse round-trip and
/// <see cref="WindowPlacement"/> visibility / centering rules.
/// </summary>
public sealed class WindowStateTests
{
    // ────────────────────────────────────────────────────────────────
    // WindowStateStore — Format / Parse round-trip (pure, no IO)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatParse_RoundTrips_Normal()
    {
        var rec = new WindowStateRecord(120, 240, 1400, 900, Maximized: false);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void FormatParse_RoundTrips_Maximized()
    {
        var rec = new WindowStateRecord(-1920, 0, 1600.5, 1000.25, Maximized: true);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void FormatParse_NegativeCoords_RoundTrip()
    {
        // A window on a left-hand second monitor saves negative X — must survive.
        var rec = new WindowStateRecord(-3000, -120, 1280, 720, Maximized: false);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(System.Array.Empty<string>()));
    }

    [Fact]
    public void Parse_MissingFields_ReturnsNull()
    {
        // No width / height → unusable.
        Assert.Null(WindowStateStore.Parse(new[] { "x=10", "y=20" }));
    }

    [Fact]
    public void Parse_NonPositiveSize_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(new[] { "x=10", "y=20", "w=0", "h=0", "max=0" }));
    }

    [Fact]
    public void Parse_Corrupt_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(new[] { "garbage", "not=a=number", "w=abc" }));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void Parse_MaximizedFlag_Variants(string val, bool expected)
    {
        var rec = WindowStateStore.Parse(new[] { "x=0", "y=0", "w=800", "h=600", $"max={val}" });
        Assert.NotNull(rec);
        Assert.Equal(expected, rec!.Value.Maximized);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var rec = WindowStateStore.Parse(new[] { "# comment", "", "  ", "x=5", "y=6", "w=800", "h=600" });
        Assert.NotNull(rec);
        Assert.Equal(5, rec!.Value.X);
        Assert.Equal(6, rec.Value.Y);
    }

    // ────────────────────────────────────────────────────────────────
    // WindowPlacement.IsVisibleEnough — off-screen reset rule
    // ────────────────────────────────────────────────────────────────

    private static readonly List<(int, int, int, int)> SinglePrimary = new() { (0, 0, 1920, 1080) };

    [Fact]
    public void Visible_FullyOnScreen_True()
    {
        Assert.True(WindowPlacement.IsVisibleEnough(100, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_CompletelyOffToTheRight_False()
    {
        // Saved on a second monitor at x=3000 that no longer exists.
        Assert.False(WindowPlacement.IsVisibleEnough(3000, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_OnlySliverShowing_False()
    {
        // Just 10 px peeking in from the left edge — not grabbable.
        Assert.False(WindowPlacement.IsVisibleEnough(-1390, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_GrabbableChunkShowing_True()
    {
        // 120 px on-screen (== MinVisibleWidth) with full-height overlap → reachable.
        Assert.True(WindowPlacement.IsVisibleEnough(-1280, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_SecondMonitorRemovedVsPresent()
    {
        // Window saved on the second monitor (1920..3840).
        var onSecond = (2000, 100, 1400, 900);

        // Second monitor gone → not visible → caller resets.
        Assert.False(WindowPlacement.IsVisibleEnough(
            onSecond.Item1, onSecond.Item2, onSecond.Item3, onSecond.Item4, SinglePrimary));

        // Second monitor present → visible → keep placement.
        var dual = new List<(int, int, int, int)> { (0, 0, 1920, 1080), (1920, 0, 1920, 1080) };
        Assert.True(WindowPlacement.IsVisibleEnough(
            onSecond.Item1, onSecond.Item2, onSecond.Item3, onSecond.Item4, dual));
    }

    [Fact]
    public void Visible_NoScreens_False()
    {
        Assert.False(WindowPlacement.IsVisibleEnough(0, 0, 1400, 900, new List<(int, int, int, int)>()));
    }

    // ────────────────────────────────────────────────────────────────
    // WindowPlacement.CenterIn
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CenterIn_CentersWithinScreen()
    {
        var (x, y) = WindowPlacement.CenterIn((0, 0, 1920, 1080), 1400, 900);
        Assert.Equal(260, x); // (1920-1400)/2
        Assert.Equal(90, y);  // (1080-900)/2
    }

    [Fact]
    public void CenterIn_WindowLargerThanScreen_ClampsToOrigin()
    {
        var (x, y) = WindowPlacement.CenterIn((0, 0, 800, 600), 1400, 900);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void CenterIn_RespectsScreenOffset()
    {
        var (x, y) = WindowPlacement.CenterIn((1920, 0, 1920, 1080), 1400, 900);
        Assert.Equal(1920 + 260, x);
        Assert.Equal(90, y);
    }
}
