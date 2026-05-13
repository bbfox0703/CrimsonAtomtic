using System.Runtime.InteropServices;
using SkiaSharp;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Decodes a Crimson Desert game-data <c>.dds</c> texture and re-encodes
/// it as a fixed-size <c>.webp</c> for the editor's icon cache.
/// </summary>
/// <remarks>
/// <para>
/// Supported source formats: <b>BC1</b> (DXT1, opaque or 1-bit alpha)
/// and <b>BC3</b> (DXT5). These two cover every item-icon texture
/// observed in 1.06 — they're the formats Pearl Abyss's pipeline
/// outputs for the icon set. BC4/BC5/BC7 would need additional
/// decoders; the spec is straightforward but the icons don't use them
/// today.
/// </para>
/// <para>
/// Pipeline: DDS header → 4×4 BC block decode → RGBA8888 buffer →
/// SkiaSharp <c>SKBitmap</c> → resize via <c>SKImage.ScalePixels</c>
/// → WebP encode. The resize step always runs (downscaling from
/// 256×256 or upscaling from 32×32) so the cache is one consistent
/// resolution per call site.
/// </para>
/// <para>
/// Hand-rolled rather than NuGet-backed: this keeps the dependency
/// surface in line with project rule 8 ("vendor deps cloned into
/// vendor/, never submodules") and removes a potential AOT/trim
/// hazard. Each block-decoder helper is ~30 lines and unit-tested
/// against a real game-data fixture.
/// </para>
/// </remarks>
public static class IconImageEncoder
{
    /// <summary>
    /// Decode <paramref name="ddsBytes"/> as a DDS texture and re-encode
    /// it as a <paramref name="targetSize"/>×<paramref name="targetSize"/>
    /// WebP image at <paramref name="quality"/> (0-100). Returns the
    /// encoded WebP bytes ready for <c>File.WriteAllBytes</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The input isn't a recognisable DDS, or the FourCC isn't a
    /// supported BC format. The caller is expected to fall back to
    /// "no icon" rather than show a corrupted image.
    /// </exception>
    public static byte[] EncodeDdsToWebp(
        ReadOnlySpan<byte> ddsBytes,
        int targetSize = 64,
        int quality = 80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 0);

        var (width, height, format, pixelOffset) = ParseDdsHeader(ddsBytes);
        var rgba = DecodeBlocks(ddsBytes[pixelOffset..], width, height, format);
        return ResizeAndEncode(rgba, width, height, targetSize, quality);
    }

    // ── Header parsing ─────────────────────────────────────────────────────

    private enum BcFormat
    {
        Bc1, // DXT1 (opaque or 1-bit alpha)
        Bc3, // DXT5
    }

    private static (int Width, int Height, BcFormat Format, int PixelOffset) ParseDdsHeader(
        ReadOnlySpan<byte> dds)
    {
        if (dds.Length < 128 || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
        {
            throw new InvalidDataException("Not a DDS file (missing 'DDS ' magic).");
        }
        var headerSize = MemoryMarshal.Read<uint>(dds[4..]);
        if (headerSize != 124)
        {
            throw new InvalidDataException(
                $"Unexpected DDS header size: {headerSize} (expected 124).");
        }
        var height = (int)MemoryMarshal.Read<uint>(dds[12..]);
        var width = (int)MemoryMarshal.Read<uint>(dds[16..]);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                $"Invalid DDS dimensions: {width}×{height}.");
        }
        // FourCC lives at offset 84. Pixel format size at 76 = 32, flags
        // at 80 should have DDPF_FOURCC (0x4) set for BC textures.
        var pfFlags = MemoryMarshal.Read<uint>(dds[80..]);
        if ((pfFlags & 0x4) == 0)
        {
            throw new InvalidDataException(
                "DDS pixel format does not have DDPF_FOURCC set — non-BC textures aren't supported.");
        }
        var fourCc = dds.Slice(84, 4);
        var format = fourCc switch
        {
        [(byte)'D', (byte)'X', (byte)'T', (byte)'1'] => BcFormat.Bc1,
        [(byte)'D', (byte)'X', (byte)'T', (byte)'5'] => BcFormat.Bc3,
            _ => throw new InvalidDataException(
                $"Unsupported DDS FourCC '{System.Text.Encoding.ASCII.GetString(fourCc)}' " +
                "— only DXT1 (BC1) and DXT5 (BC3) are implemented today."),
        };
        return (width, height, format, 128);
    }

    // ── BC block decode ────────────────────────────────────────────────────

    private static byte[] DecodeBlocks(
        ReadOnlySpan<byte> pixels, int width, int height, BcFormat format)
    {
        var rgba = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var blockSize = format switch
        {
            BcFormat.Bc1 => 8,
            BcFormat.Bc3 => 16,
            _ => throw new InvalidDataException($"unreachable: format={format}"),
        };
        var expected = blocksX * blocksY * blockSize;
        if (pixels.Length < expected)
        {
            throw new InvalidDataException(
                $"DDS pixel data is {pixels.Length} bytes but the layout " +
                $"({width}×{height}, {format}) needs {expected}.");
        }

        Span<byte> blockRgba = stackalloc byte[64]; // 4×4 RGBA
        var blockOffset = 0;
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var src = pixels.Slice(blockOffset, blockSize);
                switch (format)
                {
                    case BcFormat.Bc1:
                        DecodeBc1Block(src, blockRgba);
                        break;
                    case BcFormat.Bc3:
                        DecodeBc3Block(src, blockRgba);
                        break;
                }
                WriteBlockToImage(blockRgba, rgba, bx * 4, by * 4, width, height);
                blockOffset += blockSize;
            }
        }
        return rgba;
    }

    private static void WriteBlockToImage(
        ReadOnlySpan<byte> blockRgba, byte[] image, int px, int py, int width, int height)
    {
        for (var ry = 0; ry < 4; ry++)
        {
            var y = py + ry;
            if (y >= height) break;
            for (var rx = 0; rx < 4; rx++)
            {
                var x = px + rx;
                if (x >= width) continue;
                var srcIdx = (ry * 4 + rx) * 4;
                var dstIdx = (y * width + x) * 4;
                image[dstIdx + 0] = blockRgba[srcIdx + 0];
                image[dstIdx + 1] = blockRgba[srcIdx + 1];
                image[dstIdx + 2] = blockRgba[srcIdx + 2];
                image[dstIdx + 3] = blockRgba[srcIdx + 3];
            }
        }
    }

    private static void DecodeBc1Block(ReadOnlySpan<byte> block, Span<byte> rgbaOut)
    {
        var c0 = (ushort)(block[0] | (block[1] << 8));
        var c1 = (ushort)(block[2] | (block[3] << 8));
        Span<uint> palette = stackalloc uint[4];
        Rgb565ToRgba(c0, out palette[0]);
        Rgb565ToRgba(c1, out palette[1]);
        if (c0 > c1)
        {
            palette[2] = Lerp565(palette[0], palette[1], 2, 1);
            palette[3] = Lerp565(palette[0], palette[1], 1, 2);
        }
        else
        {
            palette[2] = Lerp565(palette[0], palette[1], 1, 1);
            palette[3] = 0x00000000; // transparent black
        }
        var indexBits = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
        for (var i = 0; i < 16; i++)
        {
            var idx = (indexBits >> (i * 2)) & 0x3;
            var color = palette[(int)idx];
            rgbaOut[i * 4 + 0] = (byte)(color & 0xFF);
            rgbaOut[i * 4 + 1] = (byte)((color >> 8) & 0xFF);
            rgbaOut[i * 4 + 2] = (byte)((color >> 16) & 0xFF);
            rgbaOut[i * 4 + 3] = (byte)((color >> 24) & 0xFF);
        }
    }

    private static void DecodeBc3Block(ReadOnlySpan<byte> block, Span<byte> rgbaOut)
    {
        // Alpha block: 2 endpoints + 48 bits of 3-bit indices.
        var a0 = block[0];
        var a1 = block[1];
        Span<byte> alphaPalette = stackalloc byte[8];
        alphaPalette[0] = a0;
        alphaPalette[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i <= 6; i++)
            {
                alphaPalette[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
            }
        }
        else
        {
            for (var i = 1; i <= 4; i++)
            {
                alphaPalette[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            }
            alphaPalette[6] = 0;
            alphaPalette[7] = 255;
        }
        // 48 bits of 3-bit indices, packed little-endian across bytes 2..7.
        ulong alphaIndices = 0;
        for (var i = 0; i < 6; i++)
        {
            alphaIndices |= (ulong)block[2 + i] << (i * 8);
        }

        // Color block: BC1-style, always 4-color (no punch-through alpha).
        var c0 = (ushort)(block[8] | (block[9] << 8));
        var c1 = (ushort)(block[10] | (block[11] << 8));
        Span<uint> colorPalette = stackalloc uint[4];
        Rgb565ToRgba(c0, out colorPalette[0]);
        Rgb565ToRgba(c1, out colorPalette[1]);
        colorPalette[2] = Lerp565(colorPalette[0], colorPalette[1], 2, 1);
        colorPalette[3] = Lerp565(colorPalette[0], colorPalette[1], 1, 2);
        var indexBits = (uint)(block[12] | (block[13] << 8) | (block[14] << 16) | (block[15] << 24));

        for (var i = 0; i < 16; i++)
        {
            var ci = (indexBits >> (i * 2)) & 0x3;
            var ai = (int)((alphaIndices >> (i * 3)) & 0x7);
            var color = colorPalette[(int)ci];
            rgbaOut[i * 4 + 0] = (byte)(color & 0xFF);
            rgbaOut[i * 4 + 1] = (byte)((color >> 8) & 0xFF);
            rgbaOut[i * 4 + 2] = (byte)((color >> 16) & 0xFF);
            rgbaOut[i * 4 + 3] = alphaPalette[ai];
        }
    }

    private static void Rgb565ToRgba(ushort c, out uint rgba)
    {
        var r = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        var g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        var b = (byte)((c & 0x1F) * 255 / 31);
        rgba = (uint)r | ((uint)g << 8) | ((uint)b << 16) | (0xFFu << 24);
    }

    /// <summary>
    /// Weighted average of two RGBA8888 colors (alpha forced to 255).
    /// <c>(wA * a + wB * b) / (wA + wB)</c> per channel.
    /// </summary>
    private static uint Lerp565(uint a, uint b, int wA, int wB)
    {
        var w = wA + wB;
        var r = (byte)(((a & 0xFF) * wA + (b & 0xFF) * wB) / w);
        var g = (byte)((((a >> 8) & 0xFF) * wA + ((b >> 8) & 0xFF) * wB) / w);
        var bl = (byte)((((a >> 16) & 0xFF) * wA + ((b >> 16) & 0xFF) * wB) / w);
        return (uint)r | ((uint)g << 8) | ((uint)bl << 16) | (0xFFu << 24);
    }

    // ── Resize + WebP encode ───────────────────────────────────────────────

    private static byte[] ResizeAndEncode(
        byte[] rgba, int srcWidth, int srcHeight, int targetSize, int quality)
    {
        var srcInfo = new SKImageInfo(srcWidth, srcHeight,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var srcBitmap = new SKBitmap(srcInfo);
        Marshal.Copy(rgba, 0, srcBitmap.GetPixels(), rgba.Length);

        var dstInfo = new SKImageInfo(targetSize, targetSize,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dstBitmap = new SKBitmap(dstInfo);
        using (var srcImage = SKImage.FromBitmap(srcBitmap))
        using (var dstPixels = dstBitmap.PeekPixels())
        {
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            if (!srcImage.ScalePixels(dstPixels, sampling))
            {
                throw new InvalidOperationException(
                    $"SKImage.ScalePixels failed for {srcWidth}×{srcHeight} → {targetSize}×{targetSize}.");
            }
        }

        using var dstImage = SKImage.FromBitmap(dstBitmap);
        using var encoded = dstImage.Encode(SKEncodedImageFormat.Webp, quality);
        return encoded.ToArray();
    }
}
