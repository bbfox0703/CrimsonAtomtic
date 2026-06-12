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
/// Currently <see cref="ParserTargetMinor"/> = 11.</param>
/// <param name="Patch">Sub-version (e.g. <c>1.11</c> → 0, <c>1.11.01</c> → 1).
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
    /// Bumped together with the vendor refresh that lands a new
    /// patch's parser. Latest: 1.10 → 1.11 in vendor commit
    /// <c>cc37011</c> (iteminfo change <c>8fdeb45</c>). 1.11 DRIFTED the
    /// iteminfo schema again — it inserts a new boolean <c>u8</c>
    /// (<c>unk_post_apply_drop_stat_type</c>) between
    /// <c>apply_drop_stat_type</c> and <c>drop_default_data</c>, so every
    /// item grows by exactly one byte (anchored export: ok=6,333,
    /// leftover=0). The parser therefore targets the 1.11 layout
    /// exclusively and 1.10 iteminfo no longer round-trips against it.
    /// Unlike the 1.09→1.10 jump, 1.11 brought NO save-body drift — the
    /// save format is unchanged (v2 / flags 0x0080), all live slots parse
    /// hmac_ok with undecoded_bytes=0, and slot100 (old-format) + slot102
    /// (its 1.11 save-as) both round-trip clean. The prior 1.10 fix (the
    /// ContentsMiscSaveData leading-pad scan widened to 4, commit
    /// <c>f1513b8</c>) still applies. Keep this in sync with the Rust
    /// side; bump both at the same time on the next patch.
    /// </remarks>
    public const ushort ParserTargetMinor = 11;

    /// <summary>
    /// Every game-data minor whose iteminfo / save-body schema this
    /// build can load without mis-decoding — not just the single
    /// latest target. 1.11 changed the iteminfo layout vs 1.10 (see
    /// <see cref="ParserTargetMinor"/> remarks — a new per-item
    /// <c>u8</c>), so the older minors are NOT byte-compatible with
    /// this parser and item-name resolution would fail against them.
    /// The allow-list is therefore just <c>{11}</c>; a user still on
    /// 1.10 or earlier is warned to update.
    /// <see cref="ParserTargetMinor"/> is always present here.
    /// </summary>
    public static readonly ushort[] CompatibleMinors = [11];

    /// <summary>
    /// True when this install's schema is one this parser build can
    /// load (i.e. <see cref="Minor"/> is in <see cref="CompatibleMinors"/>).
    /// False values should surface a UI warning before iteminfo /
    /// save-body loading; the user can still opt to continue but the
    /// load may crash or mis-decode.
    /// </summary>
    public bool IsCompatibleWithParser => Array.IndexOf(CompatibleMinors, Minor) >= 0;

    /// <summary>
    /// Human-readable version (e.g. <c>"1.11.00 build 0x202c7a24"</c>).
    /// Suitable for an About / Settings dialog or a status-bar field.
    /// </summary>
    public string DisplayString =>
        $"{Major}.{Minor:D2}.{Patch:D2} build 0x{Build:x8}";

    /// <summary>
    /// Short version string without the build id (e.g. <c>"1.11.00"</c>).
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
