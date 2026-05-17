using System.Runtime.InteropServices;
using SkiaSharp;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Builds a parchment-style world-map basemap by compositing the
/// individual layers the game UI assembles at runtime. The output is
/// a single PNG cached by <see cref="WorldMapBasemapService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Source layers (decoded by <see cref="IconImageEncoder.DecodeDdsToRgba"/>):
/// </para>
/// <list type="bullet">
///   <item><c>cd_worldmap_blur_height.dds</c> — 8192² grayscale relief / land-water mask. R channel = elevation; values close to 0 = water, larger values = land.</item>
///   <item><c>cd_worldmap_paper_pattern.dds</c> — small tileable parchment texture used as the land fill.</item>
///   <item><c>cd_worldmap_road_sdf_32768x32768.dds</c> — 8192² signed-distance-field for roads. Pixels close to <c>0x80</c> (the SDF zero crossing the game uses) sit on road lines.</item>
/// </list>
///
/// <para>
/// Output is <see cref="OutputSize"/>×<see cref="OutputSize"/> RGBA8888,
/// produced via direct buffer iteration (faster + simpler than chaining
/// Skia blend modes for this many per-pixel conditionals). The 4096²
/// target balances visual fidelity against the ~256 MB transient pixel
/// buffers the 8192² source layers need during decode.
/// </para>
///
/// <para>
/// <b>Phase-1 fidelity</b>: this is a first cut. The output captures the
/// land-water silhouette + parchment texture + road network — enough to
/// recognise the geography of <c>crimson-desert-full-world-map.jpg</c>.
/// The web JPG also has region labels (HERNAND / ILLUZ / CRIMSON DESERT
/// / DEMENISS / DELESYA) and POI markers which we don't compose here;
/// those are roadmap follow-ons.
/// </para>
/// </remarks>
public static class WorldMapCompositor
{
    /// <summary>
    /// Output basemap resolution (square). 4K balances visual fidelity
    /// vs. the ~256 MB transient buffer the 8192² source DDSes need.
    /// </summary>
    public const int OutputSize = 4096;

    // Tunable parameters for the parchment look. Picked by eye from the
    // web JPG; tweaking these is the right knob if the result looks off.

    /// <summary>
    /// Blur-height threshold below which a pixel is treated as water.
    /// The DDS uses the R channel as the height; 0 = sea floor, 255 =
    /// highest peak. Threshold of 40 puts the coastline where the JPG
    /// puts it.
    /// </summary>
    private const byte WaterHeightThreshold = 40;

    /// <summary>Color for water pixels (muted dark teal, matches the JPG).</summary>
    private static readonly (byte R, byte G, byte B) WaterColor = (0x4A, 0x6B, 0x7E);

    /// <summary>
    /// Road-SDF minimum value treated as "on a road line". Empirical
    /// histogram of the live <c>cd_worldmap_road_sdf_32768x32768.dds</c>
    /// shows the road network packed into bytes 120..135 (1.1% of
    /// pixels) with everything else either far from roads (mode at 0)
    /// or in a soft gradient up to the line. Lowering this threshold
    /// thickens the lines + brings in road-margin gradient pixels.
    /// </summary>
    private const byte RoadSdfMinimum = 120;

    /// <summary>Road line color (warm dark gray, matches the JPG's terrain network).</summary>
    private static readonly (byte R, byte G, byte B) RoadColor = (0x5A, 0x55, 0x4A);

    /// <summary>
    /// Composite the parchment-style basemap from the layer DDS byte
    /// streams. Returns the PNG bytes ready to write to disk.
    /// </summary>
    /// <param name="blurHeightDds">
    /// Raw bytes of <c>cd_worldmap_blur_height.dds</c>. Expected to
    /// decode to 8192×8192 grayscale-in-RGBA.
    /// </param>
    /// <param name="paperPatternDds">
    /// Raw bytes of <c>cd_worldmap_paper_pattern.dds</c>. Small tileable
    /// texture (probably 512² or 1024²).
    /// </param>
    /// <param name="roadSdfDds">
    /// Raw bytes of <c>cd_worldmap_road_sdf_32768x32768.dds</c>.
    /// Expected to decode to 8192×8192 grayscale SDF. Pass <c>null</c>
    /// to skip the road overlay.
    /// </param>
    public static byte[] CompositeParchment(
        byte[] blurHeightDds,
        byte[] paperPatternDds,
        byte[]? roadSdfDds)
    {
        ArgumentNullException.ThrowIfNull(blurHeightDds);
        ArgumentNullException.ThrowIfNull(paperPatternDds);

        var (heightRgba, hw, hh) = IconImageEncoder.DecodeDdsToRgba(blurHeightDds);
        var (paperRgba, pw, ph) = IconImageEncoder.DecodeDdsToRgba(paperPatternDds);
        byte[]? roadRgba = null;
        int rw = 0, rh = 0;
        if (roadSdfDds is not null)
        {
            (roadRgba, rw, rh) = IconImageEncoder.DecodeDdsToRgba(roadSdfDds);
        }

        var output = new byte[OutputSize * OutputSize * 4];

        // Pre-compute the integer source-pixel index for each output
        // pixel along each axis. Cheap (8 KB of int) and removes the
        // multiply/divide from the hot inner loop.
        var heightSampleX = new int[OutputSize];
        var heightSampleY = new int[OutputSize];
        for (var i = 0; i < OutputSize; i++)
        {
            heightSampleX[i] = (int)((long)i * hw / OutputSize);
            heightSampleY[i] = (int)((long)i * hh / OutputSize);
        }
        var roadSampleX = new int[OutputSize];
        var roadSampleY = new int[OutputSize];
        if (roadRgba is not null)
        {
            for (var i = 0; i < OutputSize; i++)
            {
                roadSampleX[i] = (int)((long)i * rw / OutputSize);
                roadSampleY[i] = (int)((long)i * rh / OutputSize);
            }
        }

        for (var y = 0; y < OutputSize; y++)
        {
            var hy = heightSampleY[y];
            var ry = roadRgba is null ? 0 : roadSampleY[y];
            for (var x = 0; x < OutputSize; x++)
            {
                var hx = heightSampleX[x];
                var heightR = heightRgba[((hy * hw) + hx) * 4];

                byte outR, outG, outB;
                if (heightR < WaterHeightThreshold)
                {
                    // Water: solid muted blue. Subtle depth modulation
                    // makes deep water darker than shallow; the gradient
                    // mirrors the web JPG's coastal colouring.
                    var t = (double)heightR / WaterHeightThreshold;
                    var deepen = 0.65 + (t * 0.35);
                    outR = (byte)(WaterColor.R * deepen);
                    outG = (byte)(WaterColor.G * deepen);
                    outB = (byte)(WaterColor.B * deepen);
                }
                else
                {
                    // Land: parchment texture, brightness modulated by
                    // height (higher terrain = slightly darker for
                    // relief). Paper tiles via wraparound — small
                    // texture so wraparound seams are nearly invisible
                    // at output scale.
                    var px = x % pw;
                    var py = y % ph;
                    var paperIdx = ((py * pw) + px) * 4;
                    var paperR = paperRgba[paperIdx];
                    var paperG = paperRgba[paperIdx + 1];
                    var paperB = paperRgba[paperIdx + 2];
                    // Modulate brightness: clamp height to [40, 255],
                    // map to a shading factor in [1.0, 0.82] (higher
                    // peaks darker, low land bright).
                    var shadingT = (heightR - WaterHeightThreshold) / 215.0;
                    var shade = 1.0 - (shadingT * 0.18);
                    outR = (byte)Math.Clamp(paperR * shade, 0, 255);
                    outG = (byte)Math.Clamp(paperG * shade, 0, 255);
                    outB = (byte)Math.Clamp(paperB * shade, 0, 255);
                }

                // Overlay roads: SDF value ≥ RoadSdfMinimum = on a road
                // line, with a smooth fade so road lines anti-alias
                // against the background instead of pixel-stepping. The
                // SDF tops out around byte 135 in the live data — clamp
                // the fade ramp to the live max to avoid a discontinuity.
                if (roadRgba is not null)
                {
                    var rx = roadSampleX[x];
                    var roadSdf = roadRgba[((ry * rw) + rx) * 4];
                    if (roadSdf >= RoadSdfMinimum)
                    {
                        var t = Math.Min(1.0,
                            (roadSdf - RoadSdfMinimum) / 8.0);
                        outR = (byte)((outR * (1 - t)) + (RoadColor.R * t));
                        outG = (byte)((outG * (1 - t)) + (RoadColor.G * t));
                        outB = (byte)((outB * (1 - t)) + (RoadColor.B * t));
                    }
                }

                var outIdx = ((y * OutputSize) + x) * 4;
                output[outIdx] = outR;
                output[outIdx + 1] = outG;
                output[outIdx + 2] = outB;
                output[outIdx + 3] = 0xFF;
            }
        }

        // Encode to PNG via Skia. SKBitmap copies our buffer so the
        // managed array can be GC'd once Encode returns.
        var info = new SKImageInfo(OutputSize, OutputSize,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(output, 0, bitmap.GetPixels(), output.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
