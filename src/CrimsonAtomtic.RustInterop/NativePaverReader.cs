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
/// Currently <see cref="ParserTargetMinor"/> = 16.</param>
/// <param name="Patch">Sub-version (e.g. <c>1.16</c> → 0, <c>1.16.01</c> → 1).
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
    /// 8→9→10→11→12→13→14→15→16→17→18 manual-bump chain ended at the 1.13
    /// alignment (which wired this value to the ABI); the value now follows
    /// whatever parser the vendored lib ships (currently 1.18, vendored
    /// crimson-rs commit <c>87fd09f</c>).
    /// <para>
    /// 1.18 ships exactly ONE iteminfo layout drift: every
    /// <c>MergedPrefabVisualData</c> element gained a <c>u32</c> between
    /// <c>tribe_gender_list</c> and the 3-byte flag tail. It reads the same
    /// constant on all 12,274 elements of all 6,573 items — <c>0xeac5e173</c>,
    /// the "empty string" Jenkins sentinel already documented under the 1.10
    /// <c>money_icon_path</c> removal — so it is very likely a string/prefab
    /// name hash shipping unset; it is typed as a bare <c>u32</c> until a
    /// populated value turns up. Iteminfo went 6,572 → 6,573 items (one new
    /// key, 1005446 <c>Demian_Greyfur_Fabric_Cloak_II</c>) and
    /// 6,139,734 → 6,190,316 B. Everything else is content-only: skill grew to
    /// 2,027 entries with ZERO drift (probe 2027/2027 ok, format still
    /// <c>WithField58</c>), and the save body did not drift (still v2 /
    /// flags 0x0080; the live 1.18 save decodes with undecoded_bytes=0).
    /// </para>
    /// <para>
    /// The 1.16 patch was the other recent structural one — the largest since
    /// 1.13 and the first patch ever to break the skill parser: four iteminfo
    /// layout changes (head-side <c>inventory_info</c> removed;
    /// <c>DockingChildData::unk_post_summon_tag</c> removed; a 10 + 28*N byte
    /// <c>UnkPreRespawnData</c> block inserted before
    /// <c>respawn_time_seconds</c> with <c>unk_pre_max_endurance</c> swapped
    /// ahead of it; <c>inventory_info</c> relocated to the item END as
    /// <c>inventory_info_list: [u16; 9]</c>) plus
    /// <c>PostBuff::unk_pre_damage_type: u8</c> in skill. (1.17 in between was
    /// content-only.) The C ABI surface came through all of those unchanged and
    /// is unchanged again here.
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
    /// (<c>{18}</c>) by convention — it tracks just the target even when a
    /// content-only patch (like 1.17 over 1.16) leaves an older minor's
    /// layout readable — so a user still on 1.17 or earlier is warned to
    /// update. Because 1.18 carries a real iteminfo layout drift, that warning
    /// is SUBSTANTIVE this time — 1.17 data genuinely mis-decodes, same as
    /// 1.15 data did at 1.16 — rather than the conventional one it was at the
    /// content-only 1.14/1.15/1.17 patches.
    /// <see cref="ParserTargetMinor"/> is always present here.
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
    /// Human-readable version (e.g. <c>"1.18.00 build 0x28577c0f"</c>).
    /// Suitable for an About / Settings dialog or a status-bar field.
    /// </summary>
    public string DisplayString =>
        $"{Major}.{Minor:D2}.{Patch:D2} build 0x{Build:x8}";

    /// <summary>
    /// Short version string without the build id (e.g. <c>"1.18.00"</c>).
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
