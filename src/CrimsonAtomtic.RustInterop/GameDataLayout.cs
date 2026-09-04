namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Where the static-info gamedata tables and the localization blobs live
/// inside the live install's PAZ groups.
///
/// <para>
/// Crimson Desert 2.01 renamed the <c>0008</c> gamedata directory and every
/// one of its file extensions:
/// </para>
///
/// <code>
/// 1.05 - 2.00   gamedata/binary__/client/bin/&lt;table&gt;.pabgb / .pabgh
/// 2.01+         gamedata/binarystaticinfo__/bin/&lt;table&gt;.staticinfobody
///                                              /&lt;table&gt;.staticinfoheader
/// </code>
///
/// <para>
/// The file <i>contents</i> did not change — bodies and PABGH indices parse
/// byte-identically either way — so every crimson-rs parser is untouched and
/// only the lookup path moved. Mirrors
/// <c>vendor/crimson-rs/src/binary/gamedata_layout.rs</c>, which is
/// <c>#[cfg(test)]</c> over there and so not reachable through the C ABI.
/// </para>
///
/// <para>
/// Resolution is newest-layout-first with a fallback, so a pre-2.01 install
/// (the cross-version RE workflow keeps one around) still works.
/// </para>
/// </summary>
public sealed class GameDataLayout
{
    /// <summary>PAZ group holding the static-info gamedata tables.</summary>
    public const string GameDataGroup = "0008";

    /// <summary>
    /// Archive directory holding the localization files — or, since 2.01,
    /// the per-language subdirectories holding them. The root itself did
    /// not move.
    /// </summary>
    public const string PalocRoot = "gamedata/stringtable/binary__";

    private readonly string _bodyExtension;
    private readonly string _headerExtension;

    private GameDataLayout(string binDirectory, string bodyExtension, string headerExtension)
    {
        BinDirectory = binDirectory;
        _bodyExtension = bodyExtension;
        _headerExtension = headerExtension;
    }

    /// <summary>2.01+ layout. Probed first.</summary>
    public static GameDataLayout Modern { get; } =
        new("gamedata/binarystaticinfo__/bin", "staticinfobody", "staticinfoheader");

    /// <summary>1.05 – 2.00 layout.</summary>
    public static GameDataLayout Legacy { get; } =
        new("gamedata/binary__/client/bin", "pabgb", "pabgh");

    /// <summary>Newest layout first, so a current install resolves on the
    /// first probe and older ones fall through.</summary>
    private static readonly GameDataLayout[] ProbeOrder = [Modern, Legacy];

    /// <summary>
    /// Archive directory holding the static-info tables inside
    /// <see cref="GameDataGroup"/>.
    /// </summary>
    public string BinDirectory { get; }

    /// <summary>
    /// Table body filename: <c>"skill"</c> → <c>skill.pabgb</c> or
    /// <c>skill.staticinfobody</c>.
    /// </summary>
    public string Body(string tableStem) => $"{tableStem}.{_bodyExtension}";

    /// <summary>
    /// Table index filename: <c>"skill"</c> → <c>skill.pabgh</c> or
    /// <c>skill.staticinfoheader</c>.
    /// </summary>
    public string Header(string tableStem) => $"{tableStem}.{_headerExtension}";

    /// <summary>
    /// Resolve which layout an install ships by probing its
    /// <see cref="GameDataGroup"/> manifest for each candidate directory,
    /// newest first.
    /// </summary>
    /// <param name="paz">Extractor used to enumerate the manifest.</param>
    /// <param name="pamtPath">Absolute path to <c>0008/0.pamt</c>.</param>
    /// <returns>
    /// The layout the install ships. Reports <see cref="Modern"/> when the
    /// manifest is missing or unreadable — callers bail out on the absent
    /// install before the answer matters.
    /// </returns>
    public static GameDataLayout Resolve(IPazExtractor paz, string pamtPath)
    {
        ArgumentNullException.ThrowIfNull(paz);
        if (string.IsNullOrEmpty(pamtPath))
        {
            return Modern;
        }
        foreach (var candidate in ProbeOrder)
        {
            if (DirectoryExists(paz, pamtPath, candidate.BinDirectory))
            {
                return candidate;
            }
        }
        return Modern;
    }

    /// <summary>
    /// Every localization file for one language, as
    /// <c>(archive directory, filenames)</c>.
    ///
    /// <para>
    /// 2.01 split each language's single blob into one file per namespace
    /// inside a per-language subdirectory:
    /// </para>
    ///
    /// <code>
    /// 1.05 - 2.00   gamedata/stringtable/binary__/localizationstring_&lt;lang&gt;.paloc
    /// 2.01+         gamedata/stringtable/binary__/&lt;lang&gt;/&lt;namespace&gt;.paloc
    /// </code>
    ///
    /// <para>
    /// The namespace is already encoded in every entry's key, and the
    /// container is a flat entry list — so the split is presentational and
    /// the per-file catalogs can simply be queried in turn.
    /// </para>
    /// </summary>
    /// <returns>
    /// One filename pre-2.01, many from 2.01 on (sorted, so load order is
    /// stable). <c>null</c> when the group's manifest or the language is
    /// absent — the caller's cue to keep probing other groups.
    /// </returns>
    public static (string Directory, IReadOnlyList<string> Files)? ResolvePalocFiles(
        IPazExtractor paz, string pamtPath, string languageCode)
    {
        ArgumentNullException.ThrowIfNull(paz);
        if (string.IsNullOrEmpty(pamtPath) || string.IsNullOrEmpty(languageCode))
        {
            return null;
        }

        // 2.01+: the language IS the directory.
        var languageDirectory = $"{PalocRoot}/{languageCode}";
        var entries = TryListDir(paz, pamtPath, languageDirectory);
        if (entries is { Count: > 0 })
        {
            var names = entries
                .Select(e => e.Name)
                .Where(n => n.EndsWith(".paloc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            if (names.Length > 0)
            {
                return (languageDirectory, names);
            }
        }

        // Pre-2.01: one blob per language, directly under the root.
        var legacyName = $"localizationstring_{languageCode}.paloc";
        var rootEntries = TryListDir(paz, pamtPath, PalocRoot);
        if (rootEntries is not null
            && rootEntries.Any(e => string.Equals(e.Name, legacyName, StringComparison.Ordinal)))
        {
            return (PalocRoot, (IReadOnlyList<string>)[legacyName]);
        }
        return null;
    }

    private static bool DirectoryExists(IPazExtractor paz, string pamtPath, string directory) =>
        TryListDir(paz, pamtPath, directory) is not null;

    /// <summary>
    /// <see cref="IPazExtractor.ListDir"/> with the "this install simply
    /// doesn't have it" outcomes folded into <c>null</c>. A missing
    /// directory is the normal answer while probing, not an error.
    /// </summary>
    private static IReadOnlyList<PazFileEntry>? TryListDir(
        IPazExtractor paz, string pamtPath, string directory)
    {
        try
        {
            return paz.ListDir(pamtPath, directory);
        }
        catch (CrimsonSaveException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
