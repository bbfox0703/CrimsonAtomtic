using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Parsed contents of <c>meta/0.paver</c>, the version stamp every Crimson
/// Desert install carries (10 bytes through 2.03, longer once an install
/// ships DLC). Mirrors the <c>Paver</c> struct in
/// <c>vendor/crimson-rs/src/binary/paver.rs</c>; see
/// <see cref="NativePaverReader.TryReadFromInstall"/> for the load surface and
/// <see cref="IsCompatibleWithParser"/> for the "should I attempt to load
/// iteminfo / save data?" check. Per-patch layout changes are recorded in
/// docs/game-versions.md.
/// </summary>
/// <param name="Major">Major version. Part of the schema key: see
/// <see cref="Minor"/>.</param>
/// <param name="Minor">Minor: the schema-compatibility key, but only together
/// with <see cref="Major"/>, because it resets across a major bump (1.18 →
/// 2.00 is minor <c>18</c> → <c>0</c>). The iteminfo / save-body parsers
/// target one <c>(major, minor)</c>; a mismatched pair crashes or silently
/// mis-decodes.</param>
/// <param name="Patch">Sub-version (<c>2.06</c> → 0, <c>2.06.02</c> → 2).
/// Compatible within the same major + minor, from
/// <see cref="ParserMinPatch"/> up.</param>
/// <param name="Build">Opaque build identifier that moves with every hotfix;
/// informational only.</param>
public readonly record struct GameDataVersion(ushort Major, ushort Minor, ushort Patch, uint Build)
{
    /// <summary>
    /// The game-data <b>major</b> this crimson-rs build targets. Read it
    /// together with <see cref="ParserTargetMinor"/>: since the minor resets
    /// across a major bump, a minor on its own cannot tell 2.00 from a
    /// hypothetical 1.00, and <see cref="IsCompatibleWithParser"/> gates on
    /// both.
    /// </summary>
    /// <remarks>
    /// Read from the C ABI (<c>crimson_parser_target_gamedata_major</c>), so
    /// the Rust parser decides it, never a C# constant.
    /// </remarks>
    public static ushort ParserTargetMajor => ParserTargetInfo.Value.Major;

    /// <summary>
    /// The game-data minor this crimson-rs build targets: the schema the
    /// Rust iteminfo / save-body parsers assume, and the version the
    /// mismatch dialog shows as "parser targets …". Always a member of
    /// <see cref="CompatibleMinors"/>. Never read it without
    /// <see cref="ParserTargetMajor"/>.
    /// </summary>
    /// <remarks>
    /// Read from the C ABI (<c>crimson_parser_target_gamedata_minor</c>). The
    /// editor's own <c>VerMinor</c> in the Ui csproj is a separate, manual
    /// build-identity number kept in step with it.
    /// </remarks>
    public static ushort ParserTargetMinor => ParserTargetInfo.Value.Target;

    /// <summary>
    /// Every game-data minor, within <see cref="ParserTargetMajor"/>, whose
    /// iteminfo / save-body schema this build can load without mis-decoding
    /// (<c>crimson_parser_compatible_gamedata_minors</c>, first call sizes,
    /// second fills). Upstream keeps it to the target alone even when an
    /// older minor would still read, so a user on an older patch is told to
    /// update. A minor from another major says nothing, which is why
    /// <see cref="IsCompatibleWithParser"/> checks the major first.
    /// <see cref="ParserTargetMinor"/> is always present here.
    /// </summary>
    public static IReadOnlyList<ushort> CompatibleMinors => Array.AsReadOnly(ParserTargetInfo.Value.Compatible);

    /// <summary>
    /// The lowest <see cref="Patch"/> of the target minor this build parses
    /// (<c>0</c> = any). A patch can change a layout without moving the minor
    /// (2.04.08 did), and then an earlier patch of the same minor does not
    /// parse. Read from <c>crimson_parser_min_gamedata_patch</c>.
    /// </summary>
    public static ushort ParserMinPatch => ParserTargetInfo.Value.MinPatch;

    // Read once per process from the native lib and cached. Rust exposes the
    // target major / target minor / compatible set as compile-time constants,
    // so a single read is authoritative. Guarded so the startup version-check
    // path stays non-throwing: these are only ever accessed after a successful
    // native paver read (which proves the DLL is present), but if the native
    // lib is missing or is a stale build without the parser-target exports we
    // degrade to "no data" rather than throwing at type-init. That degraded
    // state is deliberately fail-CLOSED — Compatible is empty, so
    // IsCompatibleWithParser is false for every install and the user gets the
    // mismatch warning instead of a silent mis-decode.
    private static readonly Lazy<(ushort Major, ushort Target, ushort[] Compatible, ushort MinPatch)> ParserTargetInfo =
        new(LoadParserTargetInfo);

    private static (ushort Major, ushort Target, ushort[] Compatible, ushort MinPatch) LoadParserTargetInfo()
    {
        try
        {
            // A DLL without crimson_parser_min_gamedata_patch throws
            // EntryPointNotFound here and takes the whole tuple down with it.
            // That is intended: such a lib predates the patch-level gate and
            // cannot parse current iteminfo, so nothing it reports should read
            // as compatible.
            ushort minPatch = NativeMethods.ParserMinGamedataPatch();
            ushort major = NativeMethods.ParserTargetGamedataMajor();
            ushort target = NativeMethods.ParserTargetGamedataMinor();
            ushort[] compatible = ReadCompatibleMinors();
            // The target is always a member of the compatible set; fall back
            // to a singleton if the set query came back empty for any reason.
            return (major, target, compatible.Length > 0 ? compatible : [target], minPatch);
        }
        catch (DllNotFoundException)
        {
            return (0, 0, [], 0);
        }
        catch (EntryPointNotFoundException)
        {
            return (0, 0, [], 0);
        }
        catch (BadImageFormatException)
        {
            return (0, 0, [], 0);
        }
    }

    private static unsafe ushort[] ReadCompatibleMinors()
    {
        // Sizing call: null buffer / cap 0 → BUFFER_TOO_SMALL + the count.
        var rc = NativeMethods.ParserCompatibleGamedataMinors(null, 0, out nuint count);
        if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
        {
            return [];
        }
        if (count == 0)
        {
            return [];
        }
        var buf = new ushort[count];
        fixed (ushort* p = buf)
        {
            rc = NativeMethods.ParserCompatibleGamedataMinors(p, count, out count);
        }
        return rc == NativeMethods.OK ? buf : [];
    }

    /// <summary>
    /// True when this install's schema is one this parser build can load —
    /// i.e. <see cref="Major"/> equals <see cref="ParserTargetMajor"/>,
    /// <see cref="Minor"/> is in <see cref="CompatibleMinors"/>, <b>and</b>,
    /// on the target minor, <see cref="Patch"/> is at least
    /// <see cref="ParserMinPatch"/>.
    /// False values should surface a UI warning before iteminfo /
    /// save-body loading; the user can still opt to continue but the
    /// load may crash or mis-decode.
    /// </summary>
    /// <remarks>
    /// The major half of the check was added at the 2.00 alignment and is not
    /// cosmetic: 2.00 reset the minor to <c>0</c>, so the previous minor-only
    /// test would have reported a hypothetical <c>1.00.xx</c> install as
    /// compatible with the 2.00 parser. The patch half came with 2.04.08, the
    /// first layout change inside a minor. All three are ABI-sourced, so a
    /// future bump needs no edit here.
    /// </remarks>
    public bool IsCompatibleWithParser =>
        Major == ParserTargetMajor
        && Array.IndexOf(ParserTargetInfo.Value.Compatible, Minor) >= 0
        && (Minor != ParserTargetMinor || Patch >= ParserMinPatch);

    /// <summary>
    /// Human-readable version (e.g. <c>"2.00.00 build 0xb241d214"</c>).
    /// Suitable for an About / Settings dialog or a status-bar field.
    /// </summary>
    public string DisplayString =>
        $"{Major}.{Minor:D2}.{Patch:D2} build 0x{Build:x8}";

    /// <summary>
    /// Short version string without the build id (e.g. <c>"2.00.00"</c>).
    /// Suitable for inline log lines / warning dialogs where the build
    /// number is noise.
    /// </summary>
    public string ShortVersionString =>
        $"{Major}.{Minor:D2}.{Patch:D2}";
}

/// <summary>
/// C# wrapper over <c>crimson_paver_read_from_*</c> — reads the
/// <c>meta/0.paver</c> version stamp from a Crimson Desert install
/// root. Used by the App startup path to detect game-data version
/// mismatches BEFORE iteminfo / save-body parsing, so the user can
/// be warned rather than hitting a parse crash deep in the game-data
/// bootstrap.
/// </summary>
public static class NativePaverReader
{
    /// <summary>
    /// Read <c>meta/0.paver</c> from a Crimson Desert install. The
    /// <paramref name="installRoot"/> argument should be the
    /// install-root directory (e.g.
    /// <c>D:\SteamLibrary\steamapps\common\Crimson Desert</c>); the
    /// Rust side auto-appends <c>meta/0.paver</c> when it sees a
    /// directory.
    /// </summary>
    /// <returns>
    /// The parsed <see cref="GameDataVersion"/>, or <see langword="null"/>
    /// when <paramref name="installRoot"/> is null/empty, the file is
    /// missing, or the read fails for any reason. Never throws — the
    /// startup path needs to degrade gracefully if the install layout
    /// is unexpected (e.g. user pointed us at a non-Crimson directory).
    /// </returns>
    public static GameDataVersion? TryReadFromInstall(string? installRoot)
    {
        if (string.IsNullOrEmpty(installRoot))
        {
            return null;
        }
        ushort major = 0;
        ushort minor = 0;
        ushort patch = 0;
        uint build = 0;
        try
        {
            var rc = NativeMethods.PaverReadFromFile(
                installRoot, out major, out minor, out patch, out build);
            if (rc != NativeMethods.OK)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
        return new GameDataVersion(major, minor, patch, build);
    }

    /// <summary>
    /// Parse a paver buffer already loaded in memory (10+ bytes). Used
    /// in tests where the on-disk file isn't convenient.
    /// </summary>
    public static GameDataVersion? TryReadFromBytes(ReadOnlySpan<byte> bytes)
    {
        ushort major = 0;
        ushort minor = 0;
        ushort patch = 0;
        uint build = 0;
        try
        {
            unsafe
            {
                fixed (byte* p = bytes)
                {
                    var rc = NativeMethods.PaverReadFromBytes(
                        p, (nuint)bytes.Length,
                        out major, out minor, out patch, out build);
                    if (rc != NativeMethods.OK)
                    {
                        return null;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
        return new GameDataVersion(major, minor, patch, build);
    }
}
