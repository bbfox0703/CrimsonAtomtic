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
/// Currently <see cref="ParserTargetMinor"/> = 12.</param>
/// <param name="Patch">Sub-version (e.g. <c>1.12</c> → 0, <c>1.12.01</c> → 1).
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
    /// patch's parser. Latest: 1.11 → 1.12 in vendor commit
    /// <c>0694dfb</c>. 1.12 DRIFTED the iteminfo schema again (+150
    /// items, 6,333 → 6,483) with four byte-perfect layout changes: a
    /// payload-free <c>SubItem</c> <c>type_id==16</c> variant, an
    /// unconditional <c>unk_pre_max_endurance: u32</c>, a sibling-gated
    /// <c>unk_pre_gimmick_visual: u32</c> (equipment/gem only), and
    /// inter-element <c>u32</c> separators in <c>EnchantData</c>. So 1.11
    /// iteminfo no longer round-trips and the parser targets the 1.12
    /// layout exclusively. The <c>partprefabdyeslotinfo</c> table also
    /// drifted (−143 rows, 1,111 → 968, plus a new 5-byte per-slot
    /// field) — handled Rust-side. Like 1.11, 1.12 brought NO save-body
    /// drift: the save format is unchanged (v2 / flags 0x0080), every
    /// live slot (incl. the new-format slot106/slot107) parses hmac_ok
    /// with undecoded_bytes=0, and a body-stable write round-trips. Keep
    /// this in sync with the Rust side; bump both at the same time on the
    /// next patch.
    /// </remarks>
    public const ushort ParserTargetMinor = 12;

    /// <summary>
    /// Every game-data minor whose iteminfo / save-body schema this
    /// build can load without mis-decoding — not just the single
    /// latest target. 1.12 changed the iteminfo layout vs 1.11 (see
    /// <see cref="ParserTargetMinor"/> remarks — +150 items and four
    /// schema drifts), so the older minors are NOT byte-compatible with
    /// this parser and item-name resolution would fail against them.
    /// The allow-list is therefore just <c>{12}</c>; a user still on
    /// 1.11 or earlier is warned to update.
    /// <see cref="ParserTargetMinor"/> is always present here.
    /// </summary>
    public static readonly ushort[] CompatibleMinors = [12];

    /// <summary>
    /// True when this install's schema is one this parser build can
    /// load (i.e. <see cref="Minor"/> is in <see cref="CompatibleMinors"/>).
    /// False values should surface a UI warning before iteminfo /
    /// save-body loading; the user can still opt to continue but the
    /// load may crash or mis-decode.
    /// </summary>
    public bool IsCompatibleWithParser => Array.IndexOf(CompatibleMinors, Minor) >= 0;

    /// <summary>
    /// Human-readable version (e.g. <c>"1.12.00 build 0xac738402"</c>).
    /// Suitable for an About / Settings dialog or a status-bar field.
    /// </summary>
    public string DisplayString =>
        $"{Major}.{Minor:D2}.{Patch:D2} build 0x{Build:x8}";

    /// <summary>
    /// Short version string without the build id (e.g. <c>"1.12.00"</c>).
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
