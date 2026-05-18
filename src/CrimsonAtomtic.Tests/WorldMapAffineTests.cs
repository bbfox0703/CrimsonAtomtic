using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Regression coverage for <see cref="WorldMapAffine"/>. The dialog's
/// marker placement only works if these constants stay glued to the
/// canonical <c>crimson-desert-full-world-map.jpg</c> (5178×5240) crop;
/// any drift would silently slide every pinned marker off its real
/// in-game location. The nine landmark fixtures below are the actual
/// CE-read world positions of well-known Abyss Nexus / Cresset sites
/// the user collected — same data set the constants were fitted to.
/// </summary>
public sealed class WorldMapAffineTests
{
    /// <summary>
    /// (Landmark name, world X, world Z, expected reference-pixel X,
    /// expected reference-pixel Y). Y in CE is height — ignored for
    /// top-down plotting.
    /// </summary>
    public static IEnumerable<object[]> Landmarks => new[]
    {
        new object[] { "Howling Hill char",         -10503.16602, -4375.878418,  1400,  3759 },
        new object[] { "Howling Hill nexus",        -10531.5918,  -4329.918457,  1381,  3744 },
        new object[] { "Witch Woods nexus",         -11517.12207, -4628.442383,   960,  3868 },
        new object[] { "Five Finger Cresset",       -11746.07227,  -153.2326965,  864,  1932 },
        new object[] { "Coast Windmill nexus",       -4261.412598, -4629.567383, 4099,  3869 },
        new object[] { "Trivana Sound Cresset",      -6196.891602,  1010.535034, 3262,  1427 },
        new object[] { "Frozen Souls Cresset",      -11610.14746, -6272.822266,   939,  4582 },
        new object[] { "Vellua nexus",              -10623.20215, -6109.266602,  1344,  4511 },
        new object[] { "Three Brother's nexus",      -6926.178711, -5265.687988, 2947,  4146 },
    };

    /// <summary>
    /// Every landmark projects to within 20 pixels of its measured
    /// reference position. The Frozen Souls fixture is the outlier
    /// (~18 px on X) since it's labelled "approx. south-west" — not
    /// a precisely-clicked Nexus icon. Tightening the threshold would
    /// chase noise in the source data, not real drift.
    /// </summary>
    [Theory]
    [MemberData(nameof(Landmarks))]
    public void Canonical_ProjectsLandmark_WithinTwentyPixels(
        string name, double worldX, double worldZ, double expectedPx, double expectedPy)
    {
        _ = name;
        var (px, py) = WorldMapAffine.Canonical.WorldToPixel(worldX, worldZ);
        Assert.InRange(px - expectedPx, -20, 20);
        Assert.InRange(py - expectedPy, -20, 20);
    }

    /// <summary>
    /// <see cref="WorldMapAffine.PixelToWorld"/> is the proper inverse
    /// of <see cref="WorldMapAffine.WorldToPixel"/>: round-tripping any
    /// pixel through (px → world → px) should land back within 1e-6.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1400, 3759)]
    [InlineData(5177, 5239)]
    public void WorldToPixel_RoundTrips(double px, double py)
    {
        var affine = WorldMapAffine.Canonical;
        var (wx, wz) = affine.PixelToWorld(px, py);
        var (px2, py2) = affine.WorldToPixel(wx, wz);
        Assert.InRange(px2 - px, -1e-6, 1e-6);
        Assert.InRange(py2 - py, -1e-6, 1e-6);
    }

    /// <summary>
    /// Display-canvas projection scales reference-space pixels linearly
    /// by <c>displaySide / referenceWidth</c> (X axis) and
    /// <c>displaySide / referenceHeight</c> (Y axis). Sanity-check the
    /// X axis: a landmark known to land at reference X=1400 should
    /// land at display X = 1400 × T/5178 when the canvas is T-wide.
    /// </summary>
    [Fact]
    public void WorldToDisplayPixel_ScalesReferenceLinearly()
    {
        var affine = WorldMapAffine.Canonical;
        // Howling Hill char — closest to integer reference pixels of
        // any landmark in the fixture set.
        const double worldX = -10503.16602;
        const double worldZ = -4375.878418;
        const double T = 1024;
        var (px, py) = affine.WorldToDisplayPixel(worldX, worldZ, T, T);
        // Reference-space coords (1400, 3759) scaled by T/5178 (X) and T/5240 (Y).
        var expectedPx = 1400.0 * T / affine.ReferenceWidth;
        var expectedPy = 3759.0 * T / affine.ReferenceHeight;
        // Allow ±2 dip — the affine's own residual at this landmark is
        // ~0.5 px in reference space, ~0.1 dip at T=1024.
        Assert.InRange(px - expectedPx, -2, 2);
        Assert.InRange(py - expectedPy, -2, 2);
    }
}
