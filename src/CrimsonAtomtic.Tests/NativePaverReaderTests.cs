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
/// <see cref="GameDataVersion.ParserTargetMinor"/> and
/// <see cref="GameDataVersion.CompatibleMinors"/> are now read from the
/// crimson-rs C ABI (Rust is the single source of truth), so the
/// compatibility assertions below transitively verify the wiring: the
/// live target (1.16) is compatible, the previous minor (1.15) and any
/// other minor are not.
/// </para>
/// </summary>
public sealed class NativePaverReaderTests
{
    /// <summary>Bit-for-bit copy of the live 1.16.00 install's paver
    /// (<c>01 00 10 00 00 00 e1 6d 1d 8d</c> → build 0x8d1d6de1 LE).</summary>
    private static readonly byte[] Paver_1_16_Live =
        [0x01, 0x00, 0x10, 0x00, 0x00, 0x00, 0xe1, 0x6d, 0x1d, 0x8d];

    /// <summary>The previous patch's paver (1.15.00) — kept to pin that
    /// it is flagged INCOMPATIBLE. Unlike the 1.14 → 1.15 and 1.13 → 1.14
    /// content-only patches (where the older layout stayed readable and the
    /// target-only allow-list was the <i>only</i> reason for the warning),
    /// 1.16 carries a <b>genuine structural drift</b>: four iteminfo layout
    /// changes plus the first-ever skill drift, so the 1.16 parser really
    /// cannot read 1.15 data and the incompatibility is substantive, not just
    /// conventional.</summary>
    private static readonly byte[] Paver_1_15_Prev =
        [0x01, 0x00, 0x0f, 0x00, 0x00, 0x00, 0xe1, 0x88, 0x84, 0x6a];

    [Fact]
    public void TryReadFromBytes_HappyPath_Returns_1_16_Live()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(Paver_1_16_Live);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(16, v.Value.Minor);
        Assert.Equal(0, v.Value.Patch);
        Assert.Equal(0x8d1d6de1u, v.Value.Build);
        Assert.True(v.Value.IsCompatibleWithParser,
            "1.16.00 should be compatible with the current ParserTargetMinor=16");
        Assert.Equal("1.16.00", v.Value.ShortVersionString);
        Assert.Equal("1.16.00 build 0x8d1d6de1", v.Value.DisplayString);
    }

    [Fact]
    public void ParserTarget_And_CompatibleSet_ComeFromAbi()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // These values are sourced from the crimson-rs C ABI
        // (crimson_parser_target_gamedata_minor /
        // crimson_parser_compatible_gamedata_minors), NOT a hand-coded C#
        // constant. Pin the currently-vendored target (16) and that the
        // target is always a member of the compatible set.
        Assert.Equal(16, GameDataVersion.ParserTargetMinor);
        Assert.Contains<ushort>(16, GameDataVersion.CompatibleMinors);
        Assert.DoesNotContain<ushort>(15, GameDataVersion.CompatibleMinors);
    }

    [Fact]
    public void TryReadFromBytes_PreviousMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 1.16 is a STRUCTURAL drift over 1.15 — four iteminfo layout changes
        // (head-side inventory_info removed, DockingChildData::
        // unk_post_summon_tag removed, a 10+28*N UnkPreRespawnData block
        // inserted before respawn_time_seconds, and inventory_info relocated to
        // the item end as [u16; 9]) plus the first-ever skill drift
        // (PostBuff::unk_pre_damage_type). So the 1.15 schema is genuinely
        // unreadable by this parser, and CompatibleMinors = {16} reflects a
        // real incompatibility rather than the target-only convention that
        // covered the 1.14/1.15 content-only patches.
        var v = NativePaverReader.TryReadFromBytes(Paver_1_15_Prev);
        Assert.NotNull(v);
        Assert.Equal(15, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.15.00 must NOT be compatible — CompatibleMinors is {16}");
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
    public void TryReadFromBytes_LegacyMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 1.07.xx layout: minor = 7 is not in CompatibleMinors
        // {16} — 1.07 used a different iteminfo layout.
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(7, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.07.xx must NOT be compatible — not in CompatibleMinors {16}");
    }

    [Fact]
    public void TryReadFromBytes_FutureMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 1.17.xx layout: minor = 17 is past the validated set.
        // The gate is an explicit allow-list, not "≥ target", so a future
        // patch this build hasn't been validated against is flagged until
        // CompatibleMinors is extended (Rust-side, via the vendored parser).
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(17, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.17.xx must NOT be compatible — not yet in CompatibleMinors {16}");
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
    public void TryReadFromInstall_LiveInstall_PinsCurrent()
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
        // Pin the major (always 1 in shipped versions). Don't pin the
        // minor — the live install on a developer's machine may not
        // be 1.16 forever, and this test should survive a future
        // game-data minor bump without being rewritten alongside.
        Assert.Equal(1, v!.Value.Major);
    }
}
