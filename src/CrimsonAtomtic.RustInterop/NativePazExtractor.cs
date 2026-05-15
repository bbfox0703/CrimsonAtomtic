namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// <see cref="IPazExtractor"/> backed by the crimson-rs C ABI's
/// <c>crimson_paz_extract_file</c>. Stateless — every call re-reads
/// the PAMT from disk. Fine for the "extract PALOC once at app
/// startup" use case driving this surface; a batched / cached variant
/// belongs in a future PR if anyone needs to pull many files in a
/// loop.
/// </summary>
public sealed class NativePazExtractor : IPazExtractor
{
    public byte[] ExtractFile(string pamtPath, string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(pamtPath);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        // Two-call shape: first call queries the uncompressed size into
        // `required`, second call fills the freshly-sized buffer.
        unsafe
        {
            nuint required = 0;
            int rc = NativeMethods.PazExtractFile(
                pamtPath, directory, fileName,
                null, 0, out required);
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paz_extract_file({pamtPath}, {directory}, {fileName}) " +
                    $"size query failed: {ErrorName(rc)}");
            }
            // Even for a zero-byte file we still get required == 0; the
            // C ABI writes 0 bytes and returns OK. Handle the edge cleanly.
            if (required == 0)
            {
                return [];
            }
            var buf = new byte[required];
            fixed (byte* p = buf)
            {
                rc = NativeMethods.PazExtractFile(
                    pamtPath, directory, fileName,
                    p, (nuint)buf.Length, out _);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paz_extract_file({pamtPath}, {directory}, {fileName}) " +
                    $"fill failed: {ErrorName(rc)}");
            }
            return buf;
        }
    }

    public (byte[] Buffer, int Count) ListNpcPortraits(string pamtPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(pamtPath);
        unsafe
        {
            nuint required = 0;
            uint count = 0;
            int rc = NativeMethods.PazListNpcPortraits(
                pamtPath, null, 0, out required, out count);
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paz_list_npc_portraits({pamtPath}) " +
                    $"size query failed: {ErrorName(rc)}");
            }
            if (required == 0)
            {
                // No portraits in this PAMT (or empty PAMT). Both are
                // legitimate — return an empty buffer so the resolve
                // path can short-circuit to NOT_FOUND.
                return ([], (int)count);
            }
            var buf = new byte[required];
            fixed (byte* p = buf)
            {
                rc = NativeMethods.PazListNpcPortraits(
                    pamtPath, p, (nuint)buf.Length, out _, out count);
            }
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_paz_list_npc_portraits({pamtPath}) fill failed: {ErrorName(rc)}");
            }
            return (buf, (int)count);
        }
    }

    private static string ErrorName(int code) => code switch
    {
        NativeMethods.OK                    => "OK",
        NativeMethods.NULL_ARG              => "NULL_ARG",
        NativeMethods.INVALID_PATH          => "INVALID_PATH",
        NativeMethods.IO                    => "IO",
        NativeMethods.BODY_PARSE            => "BODY_PARSE",
        NativeMethods.OUT_OF_RANGE          => "OUT_OF_RANGE",
        NativeMethods.BUFFER_TOO_SMALL      => "BUFFER_TOO_SMALL",
        NativeMethods.NOT_FOUND             => "NOT_FOUND",
        NativeMethods.PANIC                 => "PANIC",
        _                                   => $"UNKNOWN({code})",
    };
}
