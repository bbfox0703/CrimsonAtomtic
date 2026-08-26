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
/// <param name="Major">Major version — <c>1</c> from 1.03 through 1.18;
/// Crimson Desert <b>2.00</b> is the first bump to <c>2</c>. It is a real
/// component of the schema key, not decoration: see <see cref="Minor"/>.</param>
/// <param name="Minor">Minor — the **schema-compatibility key**, but only
/// *together with* <see cref="Major"/>, because it <b>resets</b> across a
/// major bump (1.18 → 2.00 is minor <c>18</c> → <c>0</c>). Iteminfo /
/// save-body parsers target one specific <c>(major, minor)</c>; running them
/// against a mismatched pair either crashes or silently corrupts. Currently
/// <see cref="ParserTargetMajor"/>.<see cref="ParserTargetMinor"/> = 2.0.</param>
/// <param name="Patch">Sub-version (e.g. <c>2.00</c> → 0, <c>2.00.01</c> → 1).
/// Compatible within the same major+minor.</param>
/// <param name="Build">Opaque build identifier. Bumps every PA hotfix
/// — informational only.</param>
public readonly record struct GameDataVersion(ushort Major, ushort Minor, ushort Patch, uint Build)
{
    /// <summary>
    /// The game-data <b>major</b> this crimson-rs build targets (currently
    /// <c>2</c>). Read together with <see cref="ParserTargetMinor"/>: the
    /// minor resets across a major bump (1.18 → 2.00 is <c>18</c> → <c>0</c>),
    /// so on its own it can no longer tell 2.00 apart from a hypothetical
    /// 1.00. This is what makes <see cref="IsCompatibleWithParser"/> a sound
    /// gate rather than a minor-only coincidence check.
    /// </summary>
    /// <remarks>
    /// Read from the crimson-rs C ABI
    /// (<c>crimson_parser_target_gamedata_major</c>, added in commit
    /// <c>0f2363b</c> for the 2.00 alignment) — like
    /// <see cref="ParserTargetMinor"/>, never a hand-coded C# constant.
    /// Before 2.00 the major was <c>1</c> on every shipped patch and the
    /// editor simply assumed it; that assumption is now wrong, and the two
    /// places that hard-coded a literal <c>1</c> (this compatibility check
    /// and the mismatch dialog's "parser targets …" readout) were fixed at
    /// the same time.
    /// </remarks>
    public static ushort ParserTargetMajor => ParserTargetInfo.Value.Major;

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
    /// 8→9→10→11→12→13→14→15→16→17→18 manual-bump chain ended at the 1.13
    /// alignment (which wired this value to the ABI); the value now follows
    /// whatever parser the vendored lib ships (currently 2.00, i.e. minor
    /// <c>0</c>, vendored crimson-rs commit <c>0f2363b</c>).
    /// <b>Never read this without <see cref="ParserTargetMajor"/></b> — 2.00
    /// reset the minor to 0, so the bare number no longer identifies a schema.
    /// <para>
    /// 2.00 ships TWO iteminfo layout drifts, on top of the first-ever major
    /// bump: (1) <c>SubItem</c> gained a payload-free <c>type_id == 18</c> —
    /// the same renumbering 1.12 (16) and 1.13 (17) did, and every site that
    /// read 17 in 1.18 reads 18 here, with no item still reading 14..=17; and
    /// (2) a new always-zero <c>u32</c> (<c>unk_pre_max_endurance_a</c>) sits
    /// directly ahead of the 1.12-era <c>unk_pre_max_endurance</c>, so the
    /// block before <c>respawn_time_seconds</c> now carries two <c>u32</c>s.
    /// Iteminfo went 6,573 → 6,810 items (+237, none removed) and
    /// 6,190,316 → 6,446,719 B. Everything else is content-only: skill grew to
    /// 2,046 entries with ZERO drift (probe 2046/2046 ok, format still
    /// <c>WithField58</c>) — even a major bump left it alone — and the save
    /// body did not drift (still v2 / flags 0x0080; the live 2.00 save decodes
    /// 1,103 blocks / 3,315 fields with undecoded_bytes=0).
    /// </para>
    /// <para>
    /// Drift (2)'s <i>position</i> is not decidable from the byte diff — it
    /// lands inside an all-zero run, so three different placements all
    /// round-trip byte-perfectly. What settles it is the value-sanity argument
    /// the 1.16 swap already rests on: only with the new <c>u32</c> first do
    /// both neighbours keep their known distributions
    /// (<c>unk_pre_max_endurance</c> = <c>0x01000000</c> on exactly the 59
    /// <c>Trade_*_PackedInVehicle</c> items; <c>respawn_time_seconds</c> =
    /// 0 / −1 / 604800). The wrong placements produce the same
    /// <c>-4294967296</c> nonsense signature 1.16 documented. Check value
    /// distributions, not just the round-trip.
    /// </para>
    /// <para>
    /// Recent history: 1.18 added a <c>u32</c> to every
    /// <c>MergedPrefabVisualData</c> element; 1.16 was the largest structural
    /// patch since 1.13 and the first ever to break the skill parser (four
    /// iteminfo layout changes plus
    /// <c>PostBuff::unk_pre_damage_type: u8</c>); 1.17 in between was
    /// content-only. The C ABI surface came through all of those unchanged,
    /// and 2.00 only <i>adds</i> to it
    /// (<c>crimson_parser_target_gamedata_major</c>) — nothing existing moved.
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
    /// sizing then refill). The allow-list is kept a single element
    /// (<c>{0}</c>, i.e. 2.00) by convention — it tracks just the target even
    /// when a content-only patch (like 1.17 over 1.16) leaves an older minor's
    /// layout readable — so a user still on 1.18 or earlier is warned to
    /// update. Because 2.00 carries two real iteminfo layout drifts, that
    /// warning is SUBSTANTIVE — 1.18 data genuinely mis-decodes, same as 1.17
    /// data did at 1.18 and 1.15 data did at 1.16 — rather than the
    /// conventional one it was at the content-only 1.14/1.15/1.17 patches.
    /// <para>
    /// <b>Entries are minors WITHIN <see cref="ParserTargetMajor"/>.</b> A
    /// minor from a different major says nothing about compatibility, which
    /// is why <see cref="IsCompatibleWithParser"/> gates on the major first:
    /// since 2.00 reset the minor to 0, a bare <c>Minor == 0</c> match would
    /// otherwise accept a hypothetical 1.00 install.
    /// </para>
    /// <see cref="ParserTargetMinor"/> is always present here.
    /// </summary>
    public static ushort[] CompatibleMinors => ParserTargetInfo.Value.Compatible;

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
    private static readonly Lazy<(ushort Major, ushort Target, ushort[] Compatible)> ParserTargetInfo =
        new(LoadParserTargetInfo);

    private static (ushort Major, ushort Target, ushort[] Compatible) LoadParserTargetInfo()
    {
        try
        {
            // crimson_parser_target_gamedata_major is the newest of the three
            // (added at the 2.00 alignment), so a stale vendored DLL throws
            // EntryPointNotFound here and takes the whole tuple down with it.
            // That is intended: a lib old enough to lack this export also
            // predates the 2.00 parser, so its minor is not one we should be
            // reporting as compatible either.
            ushort major = NativeMethods.ParserTargetGamedataMajor();
            ushort target = NativeMethods.ParserTargetGamedataMinor();
            ushort[] compatible = ReadCompatibleMinors();
            // The target is always a member of the compatible set; fall back
            // to a singleton if the set query came back empty for any reason.
            return (major, target, compatible.Length > 0 ? compatible : [target]);
        }
        catch (DllNotFoundException)
        {
            return (0, 0, []);
        }
        catch (EntryPointNotFoundException)
        {
            return (0, 0, []);
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
    /// i.e. <see cref="Major"/> equals <see cref="ParserTargetMajor"/>
    /// <b>and</b> <see cref="Minor"/> is in <see cref="CompatibleMinors"/>.
    /// False values should surface a UI warning before iteminfo /
    /// save-body loading; the user can still opt to continue but the
    /// load may crash or mis-decode.
    /// </summary>
    /// <remarks>
    /// The major half of the check was added at the 2.00 alignment and is not
    /// cosmetic: 2.00 reset the minor to <c>0</c>, so the previous minor-only
    /// test would have reported a hypothetical <c>1.00.xx</c> install as
    /// compatible with the 2.00 parser. Both halves are ABI-sourced, so a
    /// future major bump needs no edit here.
    /// </remarks>
    public bool IsCompatibleWithParser =>
        Major == ParserTargetMajor && Array.IndexOf(CompatibleMinors, Minor) >= 0;

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
