using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Unit tests for <see cref="MainWindowViewModel.TryComputeTargetStack"/> —
/// the fill-to-max target calculator. Focus: the bulk-path cap
/// (<c>capLarge: true</c>) introduced so "Fill ALL stacks" / per-container
/// "Fill stacks" clamp huge-cap items at 9,999,999, while the deliberate
/// single-item "Fill stack" (<c>capLarge: false</c>) stays uncapped.
/// </summary>
public sealed class StackFillCapTests
{
    private const ulong Cap = 9_999_999UL;

    // ── Uncapped path (single "Fill stack" / "Set to Max") ──────────────

    [Fact]
    public void Uncapped_LargeMax_FillsToTrueMax()
    {
        // Currency-type item: max far above the cap, current below it.
        Assert.True(MainWindowViewModel.TryComputeTargetStack(5_000_000, 50_000_000, capLarge: false, out var t));
        Assert.Equal(50_000_000UL, t);
    }

    [Fact]
    public void Uncapped_AtMax_Skips()
    {
        Assert.False(MainWindowViewModel.TryComputeTargetStack(50_000_000, 50_000_000, capLarge: false, out _));
    }

    // ── Capped path (bulk "Fill stacks" / "Fill ALL stacks") ────────────

    [Fact]
    public void Capped_LargeMaxBelowCap_ClampsToCap()
    {
        // max 50M would overshoot — clamp the target to the cap.
        Assert.True(MainWindowViewModel.TryComputeTargetStack(5_000_000, 50_000_000, capLarge: true, out var t));
        Assert.Equal(Cap, t);
    }

    [Fact]
    public void Capped_CurrentAboveCap_LeavesAlone()
    {
        // Already past the cap → never reduce; skip entirely.
        Assert.False(MainWindowViewModel.TryComputeTargetStack(20_000_000, 50_000_000, capLarge: true, out _));
    }

    [Fact]
    public void Capped_CurrentAtCap_Skips()
    {
        // Clamped target == current → no-op skip.
        Assert.False(MainWindowViewModel.TryComputeTargetStack(Cap, 50_000_000, capLarge: true, out _));
    }

    [Fact]
    public void Capped_MaxBelowCap_FillsToMax_NotCap()
    {
        // max comfortably under the cap → behave like the uncapped path.
        Assert.True(MainWindowViewModel.TryComputeTargetStack(100, 999, capLarge: true, out var t));
        Assert.Equal(999UL, t);
    }

    // ── Small-stack (≤100) round-up — unaffected by the cap ─────────────

    [Theory]
    [InlineData(120UL, 50UL, 150UL)]   // 120 mod 50 = 20 → round up to 150
    [InlineData(30UL, 50UL, 50UL)]     // partial single stack → top up to max
    public void SmallStack_RoundsUp_RegardlessOfCap(ulong current, ulong max, ulong expected)
    {
        Assert.True(MainWindowViewModel.TryComputeTargetStack(current, max, capLarge: true, out var capped));
        Assert.Equal(expected, capped);
        Assert.True(MainWindowViewModel.TryComputeTargetStack(current, max, capLarge: false, out var uncapped));
        Assert.Equal(expected, uncapped);
    }

    [Fact]
    public void SmallStack_AtCleanMultiple_Skips()
    {
        // 100 is an exact multiple of 50 → already a clean pile, skip.
        Assert.False(MainWindowViewModel.TryComputeTargetStack(100, 50, capLarge: true, out _));
    }

    [Fact]
    public void ZeroMax_Skips()
    {
        Assert.False(MainWindowViewModel.TryComputeTargetStack(5, 0, capLarge: true, out _));
        Assert.False(MainWindowViewModel.TryComputeTargetStack(5, 0, capLarge: false, out _));
    }
}
