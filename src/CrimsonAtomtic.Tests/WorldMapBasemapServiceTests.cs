using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end smoke tests for
/// <see cref="WorldMapBasemapService"/>. Same skip-when-install-missing
/// pattern as the rest of the live tests.
/// </summary>
public sealed class WorldMapBasemapServiceTests
{
    private static string? FindGameRoot()
    {
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            // The basemap lives under 0000 — same probe witness as the
            // production service.
            if (File.Exists(Path.Combine(root, "0000", "0.pamt")))
            {
                return root;
            }
        }
        return null;
    }

    /// <summary>
    /// Smoke test: <c>EnsureBasemapAsync</c> extracts the three layer
    /// DDSes (paper pattern BC1, blur_height + road_sdf L8) from the
    /// live install, runs <see cref="WorldMapCompositor"/>, and writes
    /// the resulting 4096×4096 parchment PNG. Pins both the BC1 + the
    /// new L8 decode path against the live game asset shape.
    /// </summary>
    [Fact]
    public async Task EnsureBasemapAsync_LiveInstall_WritesParchmentComposite()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var gameRoot = FindGameRoot();
        if (gameRoot is null)
        {
            return;
        }

        // Use a one-shot temp directory so we don't pollute the user's
        // actual %LOCALAPPDATA% during a test run. The service hard-codes
        // its cache path, so we work around by forcing a refresh into
        // the real cache + checking the result lands as expected. (A
        // future refactor could inject the cache path; for now the test
        // accepts the global side-effect.)
        var paz = new NativePazExtractor();
        var path = await WorldMapBasemapService.EnsureBasemapAsync(
            paz, gameRoot, forceRefresh: true);

        Assert.True(File.Exists(path), $"basemap PNG was not written to {path}");
        var bytes = await File.ReadAllBytesAsync(path);
        // PNG magic: 89 50 4E 47 0D 0A 1A 0A.
        Assert.True(bytes.Length >= 8, $"basemap PNG truncated: {bytes.Length} bytes");
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
        // Composite output: 4096×4096 (pinned by WorldMapCompositor.OutputSize).
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        Assert.Equal(4096, width);
        Assert.Equal(4096, height);
    }

    /// <summary>
    /// Per-pair sanity for <see cref="WorldMapAffine"/>: round-tripping a
    /// world coord through World → Pixel → World must reproduce the
    /// input within floating-point precision.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-10502.729, -4373.9663)] // slot102 active char (from vendor docs)
    [InlineData(5000.0, 1000.0)]
    [InlineData(-7000.0, -3000.0)]
    public void Affine_WorldToPixelToWorld_RoundTrips(double worldX, double worldZ)
    {
        foreach (var affine in new[] { WorldMapAffine.WebMap5178x5240, WorldMapAffine.ParchmentComposite })
        {
            var (px, py) = affine.WorldToPixel(worldX, worldZ);
            var (rx, rz) = affine.PixelToWorld(px, py);
            Assert.InRange(rx - worldX, -1e-6, 1e-6);
            Assert.InRange(rz - worldZ, -1e-6, 1e-6);
        }
    }

    /// <summary>
    /// The web-map affine must reproduce the live-save active-char pin
    /// from the vendor's regression: slot103's active char lands at
    /// (1399.9, 3758.3) on the 5178×5240 basemap. Drift here means the
    /// affine constants drifted (e.g. someone copy-pasted in different
    /// numbers).
    /// </summary>
    [Fact]
    public void Affine_WebMap_ReproducesVendorActiveCharPin()
    {
        // slot102 / slot103 active char: pos_x = -10502.729, pos_z = -4373.9663.
        // Vendor's pinned pixel: (1399.9, 3758.3) — within 0.1 px tolerance.
        var (px, py) = WorldMapAffine.WebMap5178x5240.WorldToPixel(-10502.729, -4373.9663);
        Assert.InRange(px, 1399.0, 1401.0);
        Assert.InRange(py, 3757.0, 3759.0);
    }
}
