using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Parsed contents of <c>meta/0.paver</c> — the 10-byte version stamp
/// every Crimson Desert install carries. Mirrors the
/// <c>Paver</c> struct in <c>vendor/crimson-rs/src/binary/paver.rs</c>;
/// see <see cref="NativePaverReader.TryReadFromInstall"/> for the
/// load surface and <see cref="IsCompatibleWithParser"/> for the
/// canonical "should I attempt to load iteminfo / save data?" check.
/// </summary>
/// <param name="Major">Major version — <c>1</c> on every shipped patch to date.</param>
/// <param name="Minor">Minor — the **schema-compatibility key**.
/// Iteminfo / save-body parsers target a specific minor; running them
/// against a mismatched minor either crashes or silently corrupts.
/// Currently <see cref="ParserTargetMinor"/> = 13.</param>
/// <param name="Patch">Sub-version (e.g. <c>1.13</c> → 0, <c>1.13.01</c> → 1).
/// Compatible within the same minor.</param>
/// <param name="Build">Opaque build identifier. Bumps every PA hotfix
/// — informational only.</param>
public readonly record struct GameDataVersion(ushort Major, ushort Minor, ushort Patch, uint Build)
{
    /// <summary>
    /// The latest game-data minor this crimson-rs build targets — the
    /// canonical schema reference and the version shown as "parser
    /// targets …" in the mismatch dialog. The Rust-side iteminfo /
    /// save-body parsers assume this schema. Always a member of
    /// <see cref="CompatibleMinors"/>.
    /// </summary>
    /// <remarks>
    /// Read from the crimson-rs C ABI
    /// (<c>crimson_parser_target_gamedata_minor</c>) so Rust is the single
    /// source of truth — this is no longer a hand-bumped C# constant. The
    /// 8→9→10→11→12→13 manual-bump chain ended at the 1.13 alignment
    /// (vendored crimson-rs <c>7462f0e</c> / tag <c>v1.0.13.x</c>); the
    /// value now follows whatever parser the vendored lib ships.
    /// <para>
    /// 1.13 DRIFTED the iteminfo schema again (+25 items, 6,483 → 6,508):
    /// the payload-free <c>SubItem</c> variant's <c>type_id</c> bumped
    /// 16 → 17, and <c>prefab_data_list</c> + <c>gimmick_visual_prefab_data_list</c>
    /// were merged into a single <c>MergedPrefabVisualData</c> block
    /// relocated to the end of each item. So 1.12 iteminfo no longer
    /// round-trips and the parser targets the 1.13 layout exclusively. The
    /// <c>partprefabdyeslotinfo</c> table also drifted (968 → 1,538 rows,
    /// +570, plus a new additive <c>DyeExtraLayer</c> 2nd layer) — handled
    /// Rust-side. Like 1.11/1.12, 1.13 brought NO save-body drift: the save
    /// format is unchanged (v2 / flags 0x0080), every live slot (incl. the
    /// live-1.13 <c>slot107</c>) parses hmac_ok with undecoded_bytes=0, and
    /// a body-stable write round-trips.
    /// </para>
    /// The editor's own <c>VerMinor</c> in the .csproj still tracks this as
    /// a manual lock-step build-identity bump — intentionally separate from
    /// this ABI-sourced value.
    /// </remarks>
    public static ushort ParserTargetMinor => ParserTargetInfo.Value.Target;

    /// <summary>
    /// Every game-data minor whose iteminfo / save-body schema this
    /// build can load without mis-decoding — not just the single latest
    /// target. Read from the crimson-rs C ABI
    /// (<c>crimson_parser_compatible_gamedata_minors</c>, first-call
    /// sizing then refill). Each new patch drifts the iteminfo layout, so
    /// older minors are NOT byte-compatible and the allow-list is currently
    /// a single element (<c>{13}</c>); a user still on 1.12 or earlier is
    /// warned to update. <see cref="ParserTargetMinor"/> is always present
    /// here.
    /// </summary>
    public static ushort[] CompatibleMinors => ParserTargetInfo.Value.Compatible;

    // Read once per process from the native lib and cached. Rust exposes the
    // target / compatible set as compile-time constants, so a single read is
    // authoritative. Guarded so the startup version-check path stays
    // non-throwing: these are only ever accessed after a successful native
    // paver read (which proves the DLL is present), but if the native lib is
    // missing or is a stale build without the parser-target exports we degrade
    // to "no data" rather than throwing at type-init.
    private static readonly Lazy<(ushort Target, ushort[] Compatible)> ParserTargetInfo =
        new(LoadParserTargetInfo);

    private static (ushort Target, ushort[] Compatible) LoadParserTargetInfo()
    {
        try
        {
            ushort target = NativeMethods.ParserTargetGamedataMinor();
            ushort[] compatible = ReadCompatibleMinors();
            // The target is always a member of the compatible set; fall back
            // to a singleton if the set query came back empty for any reason.
            return (target, compatible.Length > 0 ? compatible : [target]);
        }
        catch (DllNotFoundException)
        {
            return (0, []);
        }
        catch (EntryPointNotFoundException)
        {
            return (0, []);
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
    /// True when this install's schema is one this parser build can
    /// load (i.e. <see cref="Minor"/> is in <see cref="CompatibleMinors"/>).
    /// False values should surface a UI warning before iteminfo /
    /// save-body loading; the user can still opt to continue but the
    /// load may crash or mis-decode.
    /// </summary>
    public bool IsCompatibleWithParser => Array.IndexOf(CompatibleMinors, Minor) >= 0;

    /// <summary>
    /// Human-readable version (e.g. <c>"1.13.00 build 0x536a2c0d"</c>).
    /// Suitable for an About / Settings dialog or a status-bar field.
    /// </summary>
    public string DisplayString =>
        $"{Major}.{Minor:D2}.{Patch:D2} build 0x{Build:x8}";

    /// <summary>
    /// Short version string without the build id (e.g. <c>"1.13.00"</c>).
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
/// be warned rather than hitting a parse crash deep in
/// <see cref="LocalizationProvider"/>'s bootstrap.
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
        var rc = NativeMethods.PaverReadFromFile(
            installRoot, out major, out minor, out patch, out build);
        if (rc != NativeMethods.OK)
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
        return new GameDataVersion(major, minor, patch, build);
    }
}
