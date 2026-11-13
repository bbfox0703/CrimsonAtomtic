using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Round-trip tests for the <c>crimson_paver_read_*</c> C ABI through
/// <see cref="NativePaverReader"/>. Synthetic bytes test exercises the
/// happy path + bad-input handling; a live-install pin reads the real
/// <c>meta/0.paver</c> when present and skips otherwise.
///
/// <para>
/// <see cref="GameDataVersion.ParserTargetMajor"/>,
/// <see cref="GameDataVersion.ParserTargetMinor"/> and
/// <see cref="GameDataVersion.CompatibleMinors"/> are all read from the
/// crimson-rs C ABI (Rust is the single source of truth), so the
/// compatibility assertions below transitively verify the wiring: the
/// live target (2.05) is compatible, the previous patch (2.04.08) is not,
/// and — the case game 2.00 newly opened up — neither is a version that
/// happens to share the target's <i>minor</i> under a different major.
/// </para>
/// </summary>
public sealed class NativePaverReaderTests
{
    /// <summary>Bit-for-bit copy of the live 2.05.00 install's paver. Since
    /// 2.04 a DLC list sits ahead of <c>build</c>: <c>u32</c> count, then
    /// <c>u32</c> length + name + <c>u32</c> revision per DLC, so <c>build</c>
    /// (0x6B551EA7 LE) is at the end of the file.</summary>
    private static readonly byte[] Paver_2_05_Live =
    [
        0x02, 0x00, 0x05, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, (byte)'d', (byte)'l', (byte)'c', (byte)'_', (byte)'0', (byte)'1',
        0x08, 0x00, 0x00, 0x00,
        0xa7, 0x1e, 0x55, 0x6b,
    ];

    /// <summary>The previous patch's paver (2.04.08) — kept to pin that it
    /// is flagged INCOMPATIBLE. 2.05 named the rows of both dye tables,
    /// nameless since 2.04, so 2.04 data no longer reads against this
    /// parser.</summary>
    private static readonly byte[] Paver_2_04_08_Prev =
    [
        0x02, 0x00, 0x04, 0x00, 0x08, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, (byte)'d', (byte)'l', (byte)'c', (byte)'_', (byte)'0', (byte)'1',
        0x07, 0x00, 0x00, 0x00,
        0x97, 0x3a, 0xd5, 0xe4,
    ];

    /// <summary>Synthetic <c>1.05.xx</c> — constructed so its <b>minor
    /// matches the 2.05 target's minor (5)</b> while its major does not.
    /// This is the exact hole the 2.00 major bump opened: before
    /// <see cref="GameDataVersion.IsCompatibleWithParser"/> gated on the
    /// major, these bytes would have been reported as compatible. It
    /// tracks the target minor, so it moves with every bump.</summary>
    private static readonly byte[] Paver_1_05_SameMinorOtherMajor =
        [0x01, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void TryReadFromBytes_HappyPath_Returns_2_05_Live()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(Paver_2_05_Live);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(5, v.Value.Minor);
        Assert.Equal(0, v.Value.Patch);
        Assert.Equal(0x6b551ea7u, v.Value.Build);
        Assert.True(v.Value.IsCompatibleWithParser,
            "2.05.00 should be compatible with the current parser target 2.5");
        Assert.Equal("2.05.00", v.Value.ShortVersionString);
        Assert.Equal("2.05.00 build 0x6b551ea7", v.Value.DisplayString);
    }

    /// <summary>
    /// A 2.04+ stamp on an install without DLC: the list is present with a
    /// count of 0. crimson-rs read <c>build</c> as 0 here until PR #103;
    /// the version gate never depended on it, so this pins both.
    /// </summary>
    [Fact]
    public void TryReadFromBytes_DlcLessStamp_ReadsBuildAndIsCompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(
        [
            0x02, 0x00, 0x05, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xa7, 0x1e, 0x55, 0x6b,
        ]);
        Assert.NotNull(v);
        Assert.Equal("2.05.00 build 0x6b551ea7", v!.Value.DisplayString);
        Assert.True(v.Value.IsCompatibleWithParser);
    }

    /// <summary>
    /// The patch half of the gate, against whatever floor the vendored
    /// crimson-rs reports: on the target minor, the floor itself passes and
    /// one patch below it fails. The floor came in at 2.04.08 (8, so
    /// 2.04.03 failed) and resets to 0 when the minor moves (2.05).
    /// </summary>
    [Fact]
    public void TryReadFromBytes_PatchFloor_GatesTheTargetMinor()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        static GameDataVersion? Read(ushort patch) => NativePaverReader.TryReadFromBytes(
        [
            (byte)GameDataVersion.ParserTargetMajor, 0x00,
            (byte)GameDataVersion.ParserTargetMinor, 0x00,
            (byte)patch, (byte)(patch >> 8),
            0x00, 0x00, 0x00, 0x00,
        ]);
        var floor = GameDataVersion.ParserMinPatch;
        Assert.True(Read(floor)!.Value.IsCompatibleWithParser,
            $"patch {floor} is the floor and must be compatible");
        if (floor > 0)
        {
            Assert.False(Read((ushort)(floor - 1))!.Value.IsCompatibleWithParser,
                $"patch {floor - 1} is below the floor and must NOT be compatible");
        }
    }

    [Fact]
    public void ParserTarget_And_CompatibleSet_ComeFromAbi()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // These values are sourced from the crimson-rs C ABI
        // (crimson_parser_target_gamedata_major / _minor /
        // crimson_parser_compatible_gamedata_minors /
        // crimson_parser_min_gamedata_patch), NOT hand-coded C# constants.
        // Pin the currently-vendored target (2.05 → major 2, minor 5, any
        // patch) and that the target minor is always a member of the
        // compatible set.
        Assert.Equal(2, GameDataVersion.ParserTargetMajor);
        Assert.Equal(5, GameDataVersion.ParserTargetMinor);
        Assert.Contains<ushort>(5, GameDataVersion.CompatibleMinors);
        Assert.DoesNotContain<ushort>(4, GameDataVersion.CompatibleMinors);
        Assert.Equal(0, GameDataVersion.ParserMinPatch);
    }

    [Fact]
    public void TryReadFromBytes_PreviousPatch_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 2.05 named the rows of both dye tables, so 2.04.08 data no longer
        // reads against this parser — a substantive incompatibility (as with
        // 1.16, 1.18, 2.00, 2.03 and 2.04), not the target-only convention
        // 2.01 and 2.02 were.
        var v = NativePaverReader.TryReadFromBytes(Paver_2_04_08_Prev);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(4, v.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "2.04.08 must NOT be compatible — the parser targets 2.5");
    }

    [Fact]
    public void TryReadFromBytes_SameMinorUnderOtherMajor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // REGRESSION GUARD for the game-2.00 major bump. The minor is the
        // schema-compatibility key only *within* a major, and 2.00 reset it
        // from 18 to 0. So a hypothetical 1.00 install carries minor 0 — the
        // very value CompatibleMinors holds — and the old minor-only check
        // (Array.IndexOf(CompatibleMinors, Minor) >= 0) would have waved it
        // straight through into a mis-decode. Assert the premise explicitly
        // so this test still means something if CompatibleMinors changes.
        var v = NativePaverReader.TryReadFromBytes(Paver_1_05_SameMinorOtherMajor);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(GameDataVersion.ParserTargetMinor, v.Value.Minor);
        Assert.Contains(v.Value.Minor, GameDataVersion.CompatibleMinors);
        Assert.NotEqual(GameDataVersion.ParserTargetMajor, v.Value.Major);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.05.xx must NOT be compatible — matching minor under a "
            + "different major says nothing about schema compatibility");
    }

    [Fact]
    public void TryReadFromBytes_ShortBuffer_ReturnsNull()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Fewer than 10 bytes → BODY_PARSE on the Rust side → null
        // on the C# wrapper (it doesn't propagate the specific error
        // code, just "this didn't work").
        var v = NativePaverReader.TryReadFromBytes([0x01, 0x00, 0x08]);
        Assert.Null(v);
    }

    [Fact]
    public void TryReadFromBytes_LegacyVersion_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 1.07.xx layout: major 1 is not the target major, and
        // minor 7 is not in CompatibleMinors {5} either — 1.07 used a
        // different iteminfo layout. Fails both halves of the gate.
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(7, v.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.07.xx must NOT be compatible — wrong major AND wrong minor");
    }

    [Fact]
    public void TryReadFromBytes_FutureMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 2.06.xx layout: the right major, but a minor past the
        // validated set. The gate is an explicit allow-list, not "≥ target",
        // so the next patch inside this major is still flagged until
        // CompatibleMinors is extended (Rust-side, via the vendored parser).
        ReadOnlySpan<byte> bytes =
            [0x02, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(6, v.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "2.06.xx must NOT be compatible — not yet in CompatibleMinors {5}");
    }

    [Fact]
    public void TryReadFromInstall_NullOrEmpty_ReturnsNullWithoutCallingNative()
    {
        // The wrapper short-circuits on null/empty before touching the
        // FFI surface — so this test runs even when crimson_rs.dll
        // isn't on the test runner's load path.
        Assert.Null(NativePaverReader.TryReadFromInstall(null));
        Assert.Null(NativePaverReader.TryReadFromInstall(string.Empty));
    }

    [Fact]
    public void TryReadFromInstall_LiveInstall_ParsesPlausibly()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        string? installRoot = null;
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "meta", "0.paver")))
            {
                installRoot = c;
                break;
            }
        }
        if (installRoot is null)
        {
            return;
        }
        var v = NativePaverReader.TryReadFromInstall(installRoot);
        Assert.NotNull(v);
        // Deliberately pins nothing that moves. This used to assert
        // Major == 1 "always 1 in shipped versions" — and game 2.00 broke
        // exactly that assumption, which is the reason the whole
        // major-aware gate exists. So assert only what a real paver can
        // never violate: majors start at 1, so a successful read that
        // yields 0 means we parsed something that isn't a version stamp.
        Assert.True(v!.Value.Major >= 1,
            $"a real install's paver major is >= 1, got {v.Value.Major}");
        // The "is the dev box on the patch this build supports" canary
        // lives Rust-side (c_abi::paver's live_install_paver_matches_parser_target),
        // where it drives off the same constants. Duplicating it here would
        // just add a second test that goes red on every game patch.
    }
}
