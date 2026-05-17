namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// World → basemap-pixel affine transform.
///
/// <para>
/// The transform is diagonal with a Z-axis flip (no rotation, no shear).
/// World X maps to image +X (east), world Z maps to image −Y (Z+ = north
/// = smaller pixel Y). See <c>vendor/crimson-rs/docs/worldmap-plotting.md</c>
/// for the derivation.
/// </para>
///
/// <para>
/// Each basemap candidate has its own affine — the constants from the
/// web-fetched <c>crimson-desert-full-world-map.jpg</c> (5178×5240) don't
/// apply to a game-extracted 2048×2048 <c>global_colormap.dds</c>. The
/// recommended in-app basemap is <see cref="GlobalColormap"/>, derived
/// from the same chunk-grid assumptions the web-map fit used.
/// </para>
/// </summary>
public readonly record struct WorldMapAffine(
    double ScaleX,
    double OffsetX,
    double ScaleZ,
    double OffsetY,
    int PixelWidth,
    int PixelHeight)
{
    /// <summary>
    /// World coords → pixel coords on the basemap image.
    /// </summary>
    public (double Px, double Py) WorldToPixel(double worldX, double worldZ)
        => (ScaleX * worldX + OffsetX, ScaleZ * worldZ + OffsetY);

    /// <summary>
    /// Pixel coords on the basemap → world coords. <see cref="ScaleX"/>
    /// and <see cref="ScaleZ"/> must be non-zero; both are by construction
    /// for any real basemap.
    /// </summary>
    public (double WorldX, double WorldZ) PixelToWorld(double px, double py)
        => ((px - OffsetX) / ScaleX, (py - OffsetY) / ScaleZ);

    /// <summary>
    /// Affine for the web-fetched <c>crimson-desert-full-world-map.jpg</c>
    /// (5178×5240). Derived in <c>vendor/crimson-rs</c> via least-squares
    /// fit against 9 TP-marker calibration points (RMSE 6.4 px). Kept here
    /// as the regression baseline — the editor doesn't ship this image
    /// since it's user-fetched.
    /// </summary>
    public static readonly WorldMapAffine WebMap5178x5240 = new(
        ScaleX: 0.432044,
        OffsetX: 5937.50,
        ScaleZ: -0.433071,
        OffsetY: 1864.08,
        PixelWidth: 5178,
        PixelHeight: 5240);

    /// <summary>
    /// Affine for the <see cref="WorldMapCompositor"/>-built parchment
    /// basemap (4096×4096 RGBA PNG, composited from
    /// <c>cd_worldmap_blur_height.dds</c> +
    /// <c>cd_worldmap_paper_pattern.dds</c> +
    /// <c>cd_worldmap_road_sdf_32768x32768.dds</c>). Matches the visual
    /// style of the web-fetched <c>crimson-desert-full-world-map.jpg</c>
    /// (parchment land, muted-teal water, road network overlay).
    ///
    /// <para>
    /// Affine derivation: the playable continent on the composite
    /// occupies roughly the inner 0..2200 × 0..2700 pixels of the 4096²
    /// canvas. Cross-referencing the web-map fit (5178×5240 covering
    /// ~12,000 world units, world origin at pixel (5937.50, 1864.08)):
    /// per-pixel scale on our composite ≈ 0.183 px/world-unit, world
    /// origin lands at pixel (2515, 1864). These constants are
    /// arithmetic, not a least-squares fit — expect a few-percent
    /// residual.
    /// </para>
    ///
    /// <para>
    /// <b>Calibration follow-on</b>: pin-point accuracy needs landmark
    /// anchoring (user-facing "drag two landmarks" calibration, or an
    /// offline fit analogous to
    /// <c>vendor/crimson-rs/scripts/worldmap_tp_fit.py</c>).
    /// </para>
    /// </summary>
    public static readonly WorldMapAffine ParchmentComposite = new(
        ScaleX: 0.183,
        OffsetX: 2515.0,
        ScaleZ: -0.183,
        OffsetY: 1864.0,
        PixelWidth: 4096,
        PixelHeight: 4096);
}
