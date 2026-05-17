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

    /// <summary>
    /// List every NPC-portrait DDS path in a PAZ group's PAMT. Output
    /// is the raw NUL-separated UTF-8 buffer emitted by the Rust C
    /// ABI's <c>crimson_paz_list_npc_portraits</c> — each record is
    /// <c>&lt;dir&gt;/&lt;filename&gt;</c>, suitable for feeding
    /// straight into
    /// <c>NativeCharacterInfoCatalog.ResolvePortrait</c> without
    /// re-serialising.
    /// </summary>
    /// <param name="pamtPath">Absolute path to the group's
    /// <c>0.pamt</c>.</param>
    /// <returns>
    /// A <c>(Buffer, Count)</c> tuple. <c>Buffer</c> is the raw
    /// NUL-separated UTF-8 path list (empty array when the PAMT
    /// contains zero portraits); <c>Count</c> is the number of
    /// portrait entries.
    /// </returns>
    /// <exception cref="CrimsonSaveException">
    /// Same shape as <see cref="ExtractFile"/> failures.
    /// </exception>
    (byte[] Buffer, int Count) ListNpcPortraits(string pamtPath);

    /// <summary>
    /// Enumerate every file in <paramref name="directory"/> inside the
    /// PAZ group whose manifest is at <paramref name="pamtPath"/>. Each
    /// record carries the leaf filename + compressed / uncompressed
    /// sizes + the partial-compression + name-truncated flags — enough
    /// to drive <see cref="ExtractFile"/> per entry without a second
    /// PAMT walk.
    /// </summary>
    /// <param name="pamtPath">Absolute path to the group's
    /// <c>0.pamt</c>.</param>
    /// <param name="directory">In-archive directory path,
    /// e.g. <c>leveldata/rootlevel/terrain/color</c> (no trailing
    /// slash; no leading slash).</param>
    /// <returns>
    /// Flat list of every file in that directory. Empty when the
    /// directory exists but has no files.
    /// </returns>
    /// <exception cref="CrimsonSaveException">
    /// <c>NOT_FOUND (-16)</c> when <paramref name="directory"/> isn't
    /// in the PAMT; <c>IO (-3)</c> for filesystem failures;
    /// <c>BODY_PARSE (-9)</c> for malformed PAMT;
    /// <c>INVALID_PATH (-2)</c> for bad UTF-8 in either path.
    /// </exception>
    /// <remarks>
    /// Each call re-parses the PAMT (no caching). For 0012's ~750 KB
    /// PAMT that's a few ms — cheap but not free. Callers walking
    /// many directories from the same PAMT may want to add their own
    /// outer cache.
    /// </remarks>
    IReadOnlyList<PazFileEntry> ListDir(string pamtPath, string directory);
}
