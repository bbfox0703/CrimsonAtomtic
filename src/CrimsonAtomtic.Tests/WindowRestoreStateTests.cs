using System.Collections.Generic;
using Avalonia;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Unit tests for the pure maximize/restore snapshot state machine that backs
/// <see cref="ManagedWindowRestore"/> (the resizable child dialogs) and the main
/// window's placement logic. Verifies the core bug fix: after a maximize→restore,
/// the window's pre-maximize NORMAL rect is what gets re-applied — even though the
/// Windows property-change order stashes the maximized dimensions first. Ported from
/// UE5CEDumper's window-restore design.
/// </summary>
public sealed class WindowRestoreStateTests
{
    [Fact]
    public void Unseeded_HasNoRestoreRect()
    {
        var s = new WindowRestoreState();
        Assert.False(s.Seeded);
        Assert.False(s.TryGetRestoreRect(out _, out _, out _));
    }

    [Fact]
    public void Seed_CapturesNormalRect()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);

        Assert.True(s.Seeded);
        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);

        Assert.True(s.TryGetRestoreRect(out var pos, out var w, out var h));
        Assert.Equal(new PixelPoint(100, 200), pos);
        Assert.Equal(860, w);
        Assert.Equal(520, h);
    }

    [Fact]
    public void Commit_WhileNormal_PromotesPendingGeometry()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(0, 0), 800, 500);

        // User drags + resizes the still-Normal window.
        s.NotePosition(new PixelPoint(300, 150));
        s.NoteSize(900, 600);
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(300, 150), s.NormalPosition);
        Assert.Equal(900, s.NormalWidth);
        Assert.Equal(600, s.NormalHeight);
    }

    [Fact]
    public void Commit_AfterFlipToMaximized_IsAbandoned()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(120, 80), 860, 520);

        // The Windows quirk: Width/Height arrive as the MAXIMIZED dims while the
        // window still reads Normal, so they get stashed...
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        // ...but by commit time WindowState has flipped to Maximized -> abandon.
        s.Commit(isNormalNow: false);

        // The pre-maximize NORMAL rect must survive untouched.
        Assert.Equal(new PixelPoint(120, 80), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);
    }

    [Fact]
    public void MaximizeThenRestore_ReappliesOriginalRect()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(120, 80), 860, 520);   // opened, Normal

        // --- maximize: poisoned stash gets abandoned at commit ---
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: false);

        // --- restore: the rect we re-apply is the original normal one ---
        Assert.True(s.TryGetRestoreRect(out var pos, out var w, out var h));
        Assert.Equal(new PixelPoint(120, 80), pos);
        Assert.Equal(860, w);
        Assert.Equal(520, h);
    }

    [Fact]
    public void NoteSize_IgnoresNonPositiveValues()
    {
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(0, 0), 800, 500);

        s.NoteSize(0, -1);          // transient layout noise
        s.Commit(isNormalNow: true);

        Assert.Equal(800, s.NormalWidth);
        Assert.Equal(500, s.NormalHeight);
    }

    // ── Position acceptability guard (the position twin of NoteSize's >0 guard) ──────

    private static readonly IReadOnlyList<(int, int, int, int)> SinglePrimary =
        new[] { (0, 0, 1920, 1080) };

    [Fact]
    public void NotePosition_OffScreen_IsRejected()
    {
        // Direct BUG 1 regression: a transition transient at a far-off top-left must not
        // poison the stash. Without the guard, Commit would promote it into the snapshot.
        var s = new WindowRestoreState();
        s.SetScreens(SinglePrimary);
        s.Seed(new PixelPoint(100, 200), 1400, 900);

        s.NotePosition(new PixelPoint(-5000, -5000));   // off every monitor
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition); // unchanged
    }

    [Fact]
    public void NotePosition_OnScreenCorner_IsAccepted()
    {
        // The guard must NOT over-reject: (0,0) is a legitimate on-screen top-left, so a
        // user who parks the window in the corner is honoured. (The "jumps to 0,0" bug is
        // fixed by the deferred re-apply ordering, not by rejecting (0,0).)
        var s = new WindowRestoreState();
        s.SetScreens(SinglePrimary);
        s.Seed(new PixelPoint(100, 200), 1400, 900);

        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(0, 0), s.NormalPosition);
    }

    [Fact]
    public void Commit_OffScreenPendingPosition_KeepsPositionPromotesSize()
    {
        // The position and size guards are independent: a monitor unplugged between stash
        // and commit leaves an off-screen pending position, which Commit must reject while
        // still promoting the (valid) pending size.
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);     // no screens yet -> all accepted

        s.NotePosition(new PixelPoint(2000, 100));       // on the now-removed 2nd monitor
        s.NoteSize(900, 600);
        s.SetScreens(SinglePrimary);                     // 2nd monitor gone
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition); // off-screen pos rejected
        Assert.Equal(900, s.NormalWidth);                        // size still promoted
        Assert.Equal(600, s.NormalHeight);
    }

    [Fact]
    public void OnRestoreReapplied_ResetsPendingToCommittedNormal()
    {
        // The core BUG 1 regression at the state-machine level. A maximize leaves the
        // maximized dims + origin poisoning the pending stash. The caller defers the
        // restore re-apply and then calls OnRestoreReapplied so a LATER commit (the
        // Background-deferred commit firing while genuinely Normal) can't promote the
        // poisoned stash into the snapshot.
        var s = new WindowRestoreState();
        s.Seed(new PixelPoint(100, 200), 860, 520);

        // maximize: pending poisoned, commit abandoned because not Normal at commit time.
        s.NoteSize(2560, 1380);
        s.NotePosition(new PixelPoint(0, 0));
        s.Commit(isNormalNow: false);

        // restore: caller re-applied the rect to the live window, then re-seeds the stash.
        s.OnRestoreReapplied();

        // A stray commit that NOW reads Normal must keep the original normal rect, not the
        // maximized poison.
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(100, 200), s.NormalPosition);
        Assert.Equal(860, s.NormalWidth);
        Assert.Equal(520, s.NormalHeight);
    }

    [Fact]
    public void SetScreens_Null_TreatedAsEmpty_Accepts()
    {
        var s = new WindowRestoreState();
        s.SetScreens(null!);                 // defensive: treated as "no screens" -> accept
        s.Seed(new PixelPoint(0, 0), 800, 500);

        s.NotePosition(new PixelPoint(-9000, -9000));
        s.Commit(isNormalNow: true);

        Assert.Equal(new PixelPoint(-9000, -9000), s.NormalPosition);
    }
}
