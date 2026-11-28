using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the DDS → WebP encoder behind icon extraction. Every input is
/// built here: a game-extracted texture is Pearl Abyss content and stays
/// out of the repo (docs/data-policy.md). The textured BC3 input checks the
/// encoder end to end (header and size, since Skia's resize makes pixels
/// inexact); the solid blocks check the block decoders pixel for pixel.
/// </summary>
public sealed class IconImageEncoderTests
{
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
    public void EncodeDdsToWebp_TexturedBc3_ProducesValidWebp()
    {
        var dds = BuildTexturedBc3(32, 32);
        var webp = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 64, quality: 80);
        AssertIsWebp(webp);
        // A near-empty file would mean the encode produced a blank image;
        // a huge one that the resize did not happen.
        Assert.InRange(webp.Length, 100, 20_000);
    }

    [Fact]
    public void EncodeDdsToWebp_TexturedBc3_ResizeToDifferentSize()
    {
        var dds = BuildTexturedBc3(32, 32);
        var small = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 32, quality: 80);
        var large = IconImageEncoder.EncodeDdsToWebp(dds, targetSize: 128, quality: 80);
        AssertIsWebp(small);
        AssertIsWebp(large);
        Assert.True(large.Length > small.Length,
            $"expected 128×128 webp ({large.Length}B) > 32×32 webp ({small.Length}B)");
    }

    [Fact]
    public void DecodeDdsToRgba_SolidBc3Block_DecodesColourAndAlphaExactly()
    {
        // alpha0 = alpha1 = 0x80 with every alpha index 0, colour0 = colour1
        // = pure red (RGB565 0xF800) with every colour index 0.
        var dds = BuildBc3Header(4, 4, 1);
        var block = dds.AsSpan(128, 16);
        block[0] = 0x80;
        block[1] = 0x80;
        BitConverter.GetBytes((ushort)0xF800).CopyTo(block[8..]);
        BitConverter.GetBytes((ushort)0xF800).CopyTo(block[10..]);

        var (rgba, width, height) = IconImageEncoder.DecodeDdsToRgba(dds);

        Assert.Equal(4, width);
        Assert.Equal(4, height);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0x80 }, rgba.AsSpan(i * 4, 4).ToArray());
        }
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

    private static byte[] BuildBc3Header(int width, int height, int blocks)
    {
        var dds = new byte[128 + blocks * 16]; // BC3 = 16 bytes/block
        "DDS "u8.CopyTo(dds);
        WriteUInt32(dds, 4, 124);
        WriteUInt32(dds, 12, (uint)height);
        WriteUInt32(dds, 16, (uint)width);
        WriteUInt32(dds, 80, 0x4); // DDPF_FOURCC
        "DXT5"u8.CopyTo(dds.AsSpan(84));
        return dds;
    }

    /// <summary>
    /// A BC3 texture with different endpoints and indices in every block,
    /// from a fixed-seed generator, so the image has detail to resize and
    /// the same bytes on every run.
    /// </summary>
    private static byte[] BuildTexturedBc3(int width, int height)
    {
        var blocks = ((width + 3) / 4) * ((height + 3) / 4);
        var dds = BuildBc3Header(width, height, blocks);
        new Random(20261128).NextBytes(dds.AsSpan(128));
        return dds;
    }

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

    [Theory]
    [InlineData(0xFFFFFFFFu, 4u)]
    [InlineData(4u, 0x80000000u)]
    [InlineData(16385u, 16u)]
    public void DecodeDdsToRgba_RefusesAnOversizedHeaderBeforeAllocating(uint width, uint height)
    {
        // A 128-byte header with DXT5 and no pixel data: only the header's
        // dimensions can make this fail, and they must fail as bad data.
        var dds = new byte[128];
        "DDS "u8.CopyTo(dds);
        BitConverter.GetBytes(124u).CopyTo(dds, 4);
        BitConverter.GetBytes(height).CopyTo(dds, 12);
        BitConverter.GetBytes(width).CopyTo(dds, 16);
        BitConverter.GetBytes(0x4u).CopyTo(dds, 80);
        "DXT5"u8.CopyTo(dds.AsSpan(84));
        Assert.Throws<InvalidDataException>(() => IconImageEncoder.DecodeDdsToRgba(dds));
    }
}
