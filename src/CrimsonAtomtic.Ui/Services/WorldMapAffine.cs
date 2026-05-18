namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// World → reference-pixel affine transform for the Crimson Desert
/// world map.
///
/// <para>
/// The reference pixel space is the canonical
/// <c>crimson-desert-full-world-map.jpg</c> (5178×5240). The transform
/// is diagonal with a Z-axis flip (no rotation, no shear): world X
/// maps to image +X (east), world Z maps to image −Y (Z+ = north =
/// smaller pixel Y). Constants came out of a least-squares fit against
/// 9 in-game landmarks (Abyss Nexus / Cresset locations across the
/// playable continent); residuals are &lt;20 px on the 5178-wide
/// canvas (0.13% — sub-pixel on any normal display). See
/// <c>vendor/crimson-rs/docs/worldmap-plotting.md</c> for the derivation
/// + <c>WorldMapAffineTests</c> for the regression matrix.
/// </para>
///
/// <para>
/// The editor doesn't ship a basemap image — the user picks their own
/// file and the dialog force-stretches it to a square display canvas.
/// <see cref="WorldToDisplayPixel"/> handles the per-axis scale from
/// reference space to that display canvas.
/// </para>
/// </summary>
public readonly record struct WorldMapAffine(
    double ScaleX,
    double OffsetX,
    double ScaleZ,
    double OffsetY,
    int ReferenceWidth,
    int ReferenceHeight)
{
    /// <summary>
    /// World coords → pixel coords on the reference 5178×5240 image.
    /// </summary>
    public (double Px, double Py) WorldToPixel(double worldX, double worldZ)
        => ((ScaleX * worldX) + OffsetX, (ScaleZ * worldZ) + OffsetY);

    /// <summary>
    /// Inverse of <see cref="WorldToPixel"/> — used by the cursor-coord
    /// readout when the dialog renders the basemap at reference scale.
    /// </summary>
    public (double WorldX, double WorldZ) PixelToWorld(double px, double py)
        => ((px - OffsetX) / ScaleX, (py - OffsetY) / ScaleZ);

    /// <summary>
    /// World coords → pixel coords on a <paramref name="displayWidth"/>
    /// × <paramref name="displayHeight"/> display canvas where the
    /// user's basemap image has been force-stretched to fill the canvas.
    /// Each axis is scaled independently:
    /// <c>Px_disp = (ScaleX·Wx + OffsetX) · displayWidth / ReferenceWidth</c>.
    ///
    /// <para>
    /// The user's chosen image can be any size or aspect — the marker
    /// position is computed purely in reference space and then mapped
    /// onto whatever display rectangle the dialog is showing. As long
    /// as the user's image is the canonical Crimson Desert world map
    /// (5178×5240 crop, just resized), markers land on the right
    /// in-world location regardless of the file's pixel dimensions.
    /// </para>
    /// </summary>
    public (double Px, double Py) WorldToDisplayPixel(
        double worldX, double worldZ, double displayWidth, double displayHeight)
    {
        var (refPx, refPy) = WorldToPixel(worldX, worldZ);
        return (
            refPx * displayWidth / ReferenceWidth,
            refPy * displayHeight / ReferenceHeight);
    }

    /// <summary>
    /// Affine for the canonical
    /// <c>crimson-desert-full-world-map.jpg</c> (5178×5240). Used as the
    /// default for every user-picked basemap (the user is expected to
    /// supply the same image, possibly resized). Recalibration support
    /// for community / cropped maps is a roadmap follow-on.
    /// </summary>
    public static readonly WorldMapAffine Canonical = new(
        ScaleX: 0.432044,
        OffsetX: 5937.50,
        ScaleZ: -0.433071,
        OffsetY: 1864.08,
        ReferenceWidth: 5178,
        ReferenceHeight: 5240);
}
