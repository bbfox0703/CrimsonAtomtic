namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One-shot extractor over <c>crimson_paz_extract_file</c>. Pulls a
/// single file out of a Crimson Desert PAZ archive group (PAMT + .paz
/// chunks) and returns the decrypted, decompressed bytes.
/// </summary>
public interface IPazExtractor
{
    /// <summary>
    /// Extract one file from <paramref name="pamtPath"/> (the
    /// <c>0.pamt</c> manifest inside a pack-group folder). The matching
    /// <c>*.paz</c> chunk(s) must live in the same directory.
    /// </summary>
    /// <param name="pamtPath">Absolute path to <c>0.pamt</c>.</param>
    /// <param name="directory">In-archive directory path,
    /// e.g. <c>gamedata/stringtable/binary__</c>.</param>
    /// <param name="fileName">Leaf filename in that directory.</param>
    /// <returns>The extracted bytes.</returns>
    /// <exception cref="CrimsonSaveException">
    /// Thrown with a precise error code: <c>NOT_FOUND (-16)</c> for an
    /// unknown <paramref name="directory"/> or <paramref name="fileName"/>;
    /// <c>IO (-3)</c> for filesystem failures; <c>BODY_PARSE (-9)</c>
    /// for malformed PAMT or extraction failures.
    /// </exception>
    byte[] ExtractFile(string pamtPath, string directory, string fileName);
}
