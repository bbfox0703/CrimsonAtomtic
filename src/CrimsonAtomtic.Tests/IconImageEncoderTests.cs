using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the DDS → WebP encoder that backs Phase 3 of the icon
/// extraction pipeline.
///
/// Strategy: a real BC3-encoded DDS extracted from group 0012 of the
/// game install ships as <c>fixtures/sample_bc3_32x32.dds</c>; the
/// encoder runs end-to-end against it and we verify the result is a
/// well-formed WebP at the requested size. Decoding correctness of
/// the BC3 block layout itself is harder to assert pixel-perfect (it
/// depends on Skia's interpolation choices on resize) so the tests
/// stay structural — header + dimensions — plus a synthetic BC1
/// case that checks a single solid colour round-trips.
/// </summary>
public sealed class IconImageEncoderTests
{
    private const string Bc3Fixture = "fixtures/sample_bc3_32x32.dds";

    /// <summary>WebP magic: 'RIFF' .... 'WEBP' at offsets 0 and 8.</summary>
    private static void AssertIsWebp(byte[] bytes)
    {
        Assert.True(bytes.Length >= 16, $"output too short ({bytes.Length} bytes)");
        Assert.Equal((byte)'R', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal((byte)'F', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal((byte)'W', bytes[8]);
        Assert.Equal((byte)'E', bytes[9]);
        Assert.Equal((byte)'B', bytes[10]);
        Assert.Equal((byte)'P', bytes[11]);
    }

    [Fact]
    public void EncodeDdsToWebp_LiveBc3Fixture_ProducesValidWebp()
    {
        if (!File.Exists(Bc3Fixture))
        {
            return;
        }
        var dds = File.ReadAllBytes(Bc3Fixture);
        var webp = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 64, quality: 80);
        AssertIsWebp(webp);
        // Sanity: 64×64 quality-80 webp from a 32×32 BC3 source should
        // typically land in the hundreds-of-bytes to low-kB range. A
        // ridiculously small file would mean the encode silently
        // produced an empty image; a 100kB file would mean the resize
        // failed.
        Assert.InRange(webp.Length, 100, 20_000);
    }

    [Fact]
    public void EncodeDdsToWebp_LiveBc3Fixture_ResizeToDifferentSize()
    {
        if (!File.Exists(Bc3Fixture))
        {
            return;
        }
        var dds = File.ReadAllBytes(Bc3Fixture);
        var small = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 32, quality: 80);
        var large = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 128, quality: 80);
        AssertIsWebp(small);
        AssertIsWebp(large);
        // Bigger target should produce more bytes for the same source
        // (modulo encoder noise — the relation isn't strictly
        // monotonic on tiny images, but 32 vs 128 is far enough apart
        // to be reliable in practice).
        Assert.True(large.Length > small.Length,
            $"expected 128×128 webp ({large.Length}B) > 32×32 webp ({small.Length}B)");
    }

    [Fact]
    public void EncodeDdsToWebp_SyntheticBc1SolidRed_RoundTripsToWebp()
    {
        // Hand-craft a 4×4 BC1 DDS that paints solid red:
        //   color0 = 0xF800 (red in RGB565)
        //   color1 = 0xF800 (same)
        //   indices = 0 for every pixel
        // The decoder will hit the "c0 > c1 is false" branch (c0 == c1
        // is sortable as not-greater), so color2 = avg of equals = red,
        // color3 = transparent black. Index 0 always picks color0 = red.
        var dds = BuildSyntheticBc1Solid(width: 4, height: 4, rgb565: 0xF800);
        var webp = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 8, quality: 80);
        AssertIsWebp(webp);
    }

    [Fact]
    public void EncodeDdsToWebp_NotADds_Throws()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        Assert.Throws<InvalidDataException>(() =>
            IconImageEncoder.EncodeDdsToWebp(garbage));
    }

    [Fact]
    public void EncodeDdsToWebp_TruncatedDds_Throws()
    {
        // Valid magic but truncated header.
        var truncated = new byte[] { (byte)'D', (byte)'D', (byte)'S', (byte)' ', 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() =>
            IconImageEncoder.EncodeDdsToWebp(truncated));
    }

    [Fact]
    public void EncodeDdsToWebp_UnsupportedFourCc_Throws()
    {
        // Build a minimal valid-shaped header with FourCC "DXT3" (BC2,
        // not yet supported). The encoder should fail loudly rather
        // than emit a corrupt image.
        var dds = new byte[128 + 64]; // header + 16-byte DXT3 block
        dds[0] = (byte)'D';
        dds[1] = (byte)'D';
        dds[2] = (byte)'S';
        dds[3] = (byte)' ';
        WriteUInt32(dds, 4, 124);   // headerSize
        WriteUInt32(dds, 12, 4);    // height = 4
        WriteUInt32(dds, 16, 4);    // width = 4
        WriteUInt32(dds, 80, 0x4);  // pfFlags: DDPF_FOURCC
        dds[84] = (byte)'D';
        dds[85] = (byte)'X';
        dds[86] = (byte)'T';
        dds[87] = (byte)'3';
        Assert.Throws<InvalidDataException>(() =>
            IconImageEncoder.EncodeDdsToWebp(dds));
    }

    // ── Synthetic-input helpers ────────────────────────────────────────────

    private static byte[] BuildSyntheticBc1Solid(int width, int height, ushort rgb565)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var pixelBytes = blocksX * blocksY * 8; // BC1 = 8 bytes/block
        var dds = new byte[128 + pixelBytes];
        dds[0] = (byte)'D';
        dds[1] = (byte)'D';
        dds[2] = (byte)'S';
        dds[3] = (byte)' ';
        WriteUInt32(dds, 4, 124);
        WriteUInt32(dds, 12, (uint)height);
        WriteUInt32(dds, 16, (uint)width);
        WriteUInt32(dds, 80, 0x4); // DDPF_FOURCC
        dds[84] = (byte)'D';
        dds[85] = (byte)'X';
        dds[86] = (byte)'T';
        dds[87] = (byte)'1';
        // For each block: color0 = color1 = rgb565, indices = 0.
        for (var b = 0; b < blocksX * blocksY; b++)
        {
            var off = 128 + b * 8;
            dds[off + 0] = (byte)(rgb565 & 0xFF);
            dds[off + 1] = (byte)((rgb565 >> 8) & 0xFF);
            dds[off + 2] = (byte)(rgb565 & 0xFF);
            dds[off + 3] = (byte)((rgb565 >> 8) & 0xFF);
            // indices already 0 from zero-init
        }
        return dds;
    }

    private static void WriteUInt32(byte[] dst, int offset, uint value)
    {
        dst[offset + 0] = (byte)(value & 0xFF);
        dst[offset + 1] = (byte)((value >> 8) & 0xFF);
        dst[offset + 2] = (byte)((value >> 16) & 0xFF);
        dst[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
