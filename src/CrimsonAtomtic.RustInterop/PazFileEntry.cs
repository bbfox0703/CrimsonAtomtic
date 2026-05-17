using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One flat record from <see cref="IPazExtractor.ListDir"/>.
/// <c>repr(C)</c> blittable struct with the exact 272-byte layout the
/// Rust C ABI's <c>CrimsonPazFileEntry</c> emits — see the upstream
/// doc in <c>vendor/crimson-rs/src/c_abi/paz.rs</c>.
///
/// <para>
/// Used to discover the basemap tile inventory for the world-map UX:
/// pair with <see cref="IPazExtractor.ExtractFile"/> for a full
/// "discover → extract → decode → cache" pipeline. The 256-byte
/// fixed name buffer holds a NUL-terminated UTF-8 filename; access it
/// as a managed string via the <see cref="Name"/> property.
/// </para>
///
/// <para>
/// Note: each <see cref="IPazExtractor.ListDir"/> call re-parses the
/// PAMT (no caching). Callers walking many directories from the same
/// PAMT may want to add their own outer cache.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 272)]
public readonly struct PazFileEntry
{
    // 0..256: NUL-terminated UTF-8 filename buffer. Wrapped in an
    // InlineArray so the struct stays a single 272-byte blittable
    // value rather than depending on `unsafe fixed byte[]`.
    private readonly PazFileNameBuffer _nameBuffer;

    /// <summary>
    /// Compressed size of this file inside the PAZ archive (post LZ4 /
    /// pre header). Always positive for present files.
    /// </summary>
    public readonly uint CompressedSize;

    /// <summary>
    /// Uncompressed (decoded) size in bytes. Use this to pre-allocate
    /// a buffer for <see cref="IPazExtractor.ExtractFile"/>.
    /// </summary>
    public readonly uint UncompressedSize;

    // Raw bit flags — exposed via the IsPartial / NameTruncated bool
    // accessors below.
    private readonly uint _isPartial;
    private readonly uint _nameTruncated;

    /// <summary>
    /// True when the file uses the partial-compression layout
    /// (header(128) + LZ4-with-prefix-dict or identity). The
    /// extractor handles both layouts transparently; the flag is
    /// informational only.
    /// </summary>
    public bool IsPartial => _isPartial != 0;

    /// <summary>
    /// True when the source filename exceeded the 256-byte buffer and
    /// was truncated. None of the in-repo basemap tile filenames
    /// observed in 1.07 hit this limit, but the flag is exposed so
    /// callers can detect schema drift.
    /// </summary>
    public bool NameTruncated => _nameTruncated != 0;

    /// <summary>
    /// Leaf filename (no directory prefix) decoded as UTF-8, with the
    /// trailing NUL + zero padding stripped. Suitable for feeding
    /// straight into
    /// <see cref="IPazExtractor.ExtractFile(string, string, string)"/>.
    /// </summary>
    public string Name
    {
        get
        {
            ReadOnlySpan<byte> span = _nameBuffer;
            var nulIdx = span.IndexOf((byte)0);
            var slice = nulIdx < 0 ? span : span[..nulIdx];
            return slice.IsEmpty ? string.Empty : Encoding.UTF8.GetString(slice);
        }
    }
}

/// <summary>
/// 256-byte fixed-size buffer holding the NUL-terminated UTF-8
/// filename portion of <see cref="PazFileEntry"/>. Hidden from public
/// surface — <see cref="PazFileEntry.Name"/> exposes the decoded
/// string.
/// </summary>
[InlineArray(256)]
internal struct PazFileNameBuffer
{
    private byte _e0;
}
