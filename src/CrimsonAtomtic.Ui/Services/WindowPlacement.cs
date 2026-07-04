using System;
using System.Collections.Generic;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Pure geometry helpers for validating a restored window position against the
/// monitors currently attached. Kept free of Avalonia types so the rules are
/// unit-testable; the View converts <c>Screens.All</c> → plain rects and calls
/// in. All coordinates are PHYSICAL pixels (the unit of Avalonia's
/// <c>Window.Position</c> and <c>Screen.WorkingArea</c>).
///
/// <para>
/// Ported from UE5CEDumper's window-restore design (same Avalonia 12 / .NET 10
/// stack). See <see cref="WindowRestoreState"/> and <see cref="WindowStateStore"/>.
/// </para>
/// </summary>
public static class WindowPlacement
{
    /// <summary>Minimum on-screen width (px) of the window for it to count as reachable.</summary>
    public const int MinVisibleWidth = 120;

    /// <summary>
    /// Minimum on-screen height (px). ~A title bar's worth, so the user can
    /// always grab and drag the window even if most of it is off-screen.
    /// </summary>
    public const int MinVisibleHeight = 40;

    /// <summary>
    /// True when the window rect (x, y, w, h) overlaps at least one screen's
    /// working area by <paramref name="minW"/> × <paramref name="minH"/> px — i.e.
    /// a grabbable chunk is visible. False means the saved monitor is gone or the
    /// resolution shrank past the window, so the caller should reset to a default
    /// centered placement.
    /// </summary>
    /// <param name="screens">Each tuple is a working area: (X, Y, Width, Height) in physical px.</param>
    public static bool IsVisibleEnough(
        int x, int y, int w, int h,
        IReadOnlyList<(int X, int Y, int W, int H)> screens,
        int minW = MinVisibleWidth, int minH = MinVisibleHeight)
    {
        if (w <= 0 || h <= 0) return false;

        foreach (var s in screens)
        {
            int ix = Math.Max(x, s.X);
            int iy = Math.Max(y, s.Y);
            int ax = Math.Min(x + w, s.X + s.W);
            int ay = Math.Min(y + h, s.Y + s.H);

            int overlapW = ax - ix;
            int overlapH = ay - iy;
            if (overlapW >= minW && overlapH >= minH)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Top-left (physical px) that centers a <paramref name="winW"/> × <paramref name="winH"/>
    /// window within a screen working area. Used when resetting an off-screen
    /// window to a sane default.
    /// </summary>
    public static (int X, int Y) CenterIn(
        (int X, int Y, int W, int H) screen, int winW, int winH)
    {
        int x = screen.X + (screen.W - winW) / 2;
        int y = screen.Y + (screen.H - winH) / 2;
        // Never let the title bar go above/left of the working area.
        return (Math.Max(screen.X, x), Math.Max(screen.Y, y));
    }
}
