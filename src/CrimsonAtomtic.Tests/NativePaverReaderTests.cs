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
/// live target (2.01) is compatible, the previous patch (2.00) is not,
/// and — the case game 2.00 newly opened up — neither is a version that
/// happens to share the target's <i>minor</i> under a different major.
/// </para>
/// </summary>
public sealed class NativePaverReaderTests
{
    /// <summary>Bit-for-bit copy of the live 2.01.00 install's paver
    /// (<c>02 00 01 00 00 00 cb 5f 1e a3</c> → build 0xa31e5fcb LE).</summary>
    private static readonly byte[] Paver_2_01_Live =
        [0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0xcb, 0x5f, 0x1e, 0xa3];

    /// <summary>The previous patch's paver (2.00.00) — kept to pin that
    /// it is flagged INCOMPATIBLE. Unlike 1.15 → 1.16, 1.17 → 1.18 and
    /// 1.18 → 2.00, this one is the <i>target-only allow-list convention</i>
    /// rather than a substantive drift: 2.01 moved every gamedata table to
    /// a new archive path with new extensions but changed not one byte
    /// inside them, so 2.00 data would still parse byte-perfectly. The
    /// list stays single-element by convention, so a 2.00 install is
    /// warned anyway.</summary>
    private static readonly byte[] Paver_2_00_Prev =
        [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x14, 0xd2, 0x41, 0xb2];

    /// <summary>Synthetic <c>1.01.xx</c> — constructed so its <b>minor
    /// matches the 2.01 target's minor (1)</b> while its major does not.
    /// This is the exact hole the 2.00 major bump opened: before
    /// <see cref="GameDataVersion.IsCompatibleWithParser"/> gated on the
    /// major, these bytes would have been reported as compatible. It
    /// tracks the target minor, so it moves with every bump.</summary>
    private static readonly byte[] Paver_1_01_SameMinorOtherMajor =
        [0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void TryReadFromBytes_HappyPath_Returns_2_01_Live()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(Paver_2_01_Live);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(1, v.Value.Minor);
        Assert.Equal(0, v.Value.Patch);
        Assert.Equal(0xa31e5fcbu, v.Value.Build);
        Assert.True(v.Value.IsCompatibleWithParser,
            "2.01.00 should be compatible with the current parser target 2.1");
        Assert.Equal("2.01.00", v.Value.ShortVersionString);
        Assert.Equal("2.01.00 build 0xa31e5fcb", v.Value.DisplayString);
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
        // crimson_parser_compatible_gamedata_minors), NOT hand-coded C#
        // constants. Pin the currently-vendored target (2.01 → major 2,
        // minor 1) and that the target minor is always a member of the
        // compatible set.
        Assert.Equal(2, GameDataVersion.ParserTargetMajor);
        Assert.Equal(1, GameDataVersion.ParserTargetMinor);
        Assert.Contains<ushort>(1, GameDataVersion.CompatibleMinors);
        Assert.DoesNotContain<ushort>(0, GameDataVersion.CompatibleMinors);
    }

    [Fact]
    public void TryReadFromBytes_PreviousPatch_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 2.01 is content-only over 2.00 as far as the parsers are
        // concerned: it moved every gamedata table to a new archive path
        // with new file extensions, but changed not one byte inside them.
        // So 2.00 data would still parse — this is the target-only
        // allow-list convention (as with 1.14/1.15/1.17), not a
        // substantive drift. Pin the warning anyway.
        var v = NativePaverReader.TryReadFromBytes(Paver_2_00_Prev);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(0, v.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "2.00.00 must NOT be compatible — the parser targets 2.1");
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
        var v = NativePaverReader.TryReadFromBytes(Paver_1_01_SameMinorOtherMajor);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(GameDataVersion.ParserTargetMinor, v.Value.Minor);
        Assert.Contains(v.Value.Minor, GameDataVersion.CompatibleMinors);
        Assert.NotEqual(GameDataVersion.ParserTargetMajor, v.Value.Major);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.01.xx must NOT be compatible — matching minor under a "
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
        // minor 7 is not in CompatibleMinors {1} either — 1.07 used a
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
        // Synthetic 2.02.xx layout: the right major, but a minor past the
        // validated set. The gate is an explicit allow-list, not "≥ target",
        // so the next patch inside this major is still flagged until
        // CompatibleMinors is extended (Rust-side, via the vendored parser).
        ReadOnlySpan<byte> bytes =
            [0x02, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Value.Major);
        Assert.Equal(2, v.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "2.02.xx must NOT be compatible — not yet in CompatibleMinors {1}");
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
