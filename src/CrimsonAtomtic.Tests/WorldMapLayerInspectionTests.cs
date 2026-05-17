using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using System.Buffers.Binary;
using System.Globalization;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Diagnostic helpers for the world-map basemap composite. All
/// <see cref="Trait"/>-tagged "Inspection" so default <c>dotnet test</c>
/// runs pick them up but they don't pollute CI summaries. Used to
/// validate that the source layers align in world space + iterate on
/// the composite output. Keep alive while the parchment basemap is in
/// flux; delete once <see cref="WorldMapAffine.ParchmentComposite"/>
/// is calibrated against landmarks.
/// </summary>
public sealed class WorldMapLayerInspectionTests
{
    private const string GameRoot = @"D:\SteamLibrary\steamapps\common\Crimson Desert";

    private static bool LiveInstallAvailable()
        => File.Exists("crimson_rs.dll")
        && File.Exists(Path.Combine(GameRoot, "0012", "0.pamt"));

    private static string OutDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrimsonAtomtic", "WorldMap", "inspect");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Decode + write each composite layer as its own standalone PNG
    /// so we can compare the world-space coverage of <c>blur_height</c>
    /// vs. <c>road_sdf</c> vs. the playable-continent JPG side by side.
    /// User-reported misalignment between blur_height and road_sdf
    /// suggests the two 8192² layers don't share the same world range
    /// despite having the same pixel dimensions — this dump is the
    /// starting point for finding the offset / scale that aligns them.
    /// </summary>
    [Fact]
    [Trait("Category", "Inspection")]
    public void Inspect_EachLayerAsStandalonePng()
    {
        if (!LiveInstallAvailable()) return;
        var paz = new NativePazExtractor();
        var pamt = Path.Combine(GameRoot, "0012", "0.pamt");
        var outDir = OutDir();

        (string dir, string name, string outName)[] layers =
        [
            ("ui/texture/image/worldmap", "cd_worldmap_blur_height.dds", "layer_blur_height.png"),
            ("ui/texture", "cd_worldmap_paper_pattern.dds", "layer_paper_pattern.png"),
            ("ui/texture/image/worldmap", "cd_worldmap_road_sdf_32768x32768.dds", "layer_road_sdf.png"),
        ];

        foreach (var (dir, name, outName) in layers)
        {
            var dds = paz.ExtractFile(pamt, dir, name);
            var (rgba, w, h) = IconImageEncoder.DecodeDdsToRgba(dds);
            var png = IconImageEncoder.EncodeRgbaAsPng(rgba, w, h);
            File.WriteAllBytes(Path.Combine(outDir, outName), png);
        }
    }

    /// <summary>
    /// Run the production compositor end-to-end + drop the output to
    /// <c>inspect/parchment.png</c>. Faster iteration than going via
    /// <see cref="WorldMapBasemapService.EnsureBasemapAsync"/> because
    /// it skips the cache lookup.
    /// </summary>
    [Fact]
    [Trait("Category", "Inspection")]
    public void Inspect_CompositeParchment()
    {
        if (!LiveInstallAvailable()) return;
        var paz = new NativePazExtractor();
        var pamt = Path.Combine(GameRoot, "0012", "0.pamt");

        var blur = paz.ExtractFile(pamt, "ui/texture/image/worldmap", "cd_worldmap_blur_height.dds");
        var paper = paz.ExtractFile(pamt, "ui/texture", "cd_worldmap_paper_pattern.dds");
        var roads = paz.ExtractFile(pamt, "ui/texture/image/worldmap", "cd_worldmap_road_sdf_32768x32768.dds");
        var png = WorldMapCompositor.CompositeParchment(blur, paper, roads);
        File.WriteAllBytes(Path.Combine(OutDir(), "parchment.png"), png);
    }

    /// <summary>
    /// Dump the 0015 terrain-color tile inventory + sizes — feeds the
    /// "Path B" follow-on (stitch 785 per-chunk 512² tiles into a 14k²
    /// composite with known per-pixel world coverage).
    /// </summary>
    [Fact]
    [Trait("Category", "Inspection")]
    public void Inspect_TerrainColorTilesIn0015()
    {
        if (!File.Exists("crimson_rs.dll")) return;
        var pamt0015 = Path.Combine(GameRoot, "0015", "0.pamt");
        if (!File.Exists(pamt0015)) return;
        var paz = new NativePazExtractor();
        var tiles = paz.ListDir(pamt0015, "leveldata/rootlevel/terrain/color");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Terrain color tiles: {tiles.Count}");
        // First 10 + last 5 are enough to confirm the X,Y range.
        for (var i = 0; i < tiles.Count && i < 10; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  [{i}] {tiles[i].Name}  comp={tiles[i].CompressedSize}  uncomp={tiles[i].UncompressedSize}");
        }
        sb.AppendLine("  …");
        for (var i = Math.Max(0, tiles.Count - 5); i < tiles.Count; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  [{i}] {tiles[i].Name}  comp={tiles[i].CompressedSize}  uncomp={tiles[i].UncompressedSize}");
        }
        throw new System.Xml.XmlException(sb.ToString());
    }
}
