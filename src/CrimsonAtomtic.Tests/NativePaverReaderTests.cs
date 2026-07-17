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
/// live target (1.14) is compatible, the previous minor (1.13) and any
/// other minor are not.
/// </para>
/// </summary>
public sealed class NativePaverReaderTests
{
    /// <summary>Bit-for-bit copy of the live 1.14.00 install's paver
    /// (<c>01 00 0e 00 00 00 f8 42 7d 59</c> → build 0x597d42f8 LE).</summary>
    private static readonly byte[] Paver_1_14_Live =
        [0x01, 0x00, 0x0e, 0x00, 0x00, 0x00, 0xf8, 0x42, 0x7d, 0x59];

    /// <summary>The previous patch's paver (1.13.00) — kept to pin that
    /// it is now flagged INCOMPATIBLE. 1.14 is a <b>content-only</b> patch
    /// over 1.13 (item field values changed but the iteminfo layout is
    /// byte-identical — the 1.13 parser reads 1.14 unchanged), so 1.13 is in
    /// fact layout-compatible; the allow-list is nonetheless kept target-only
    /// by convention (<c>CompatibleMinors</c> = <c>{14}</c>), so a 1.13
    /// install is still warned to update. (The last <i>structural</i> iteminfo
    /// drift was 1.12 → 1.13.)</summary>
    private static readonly byte[] Paver_1_13_Prev =
        [0x01, 0x00, 0x0d, 0x00, 0x00, 0x00, 0x0d, 0x2c, 0x6a, 0x53];

    [Fact]
    public void TryReadFromBytes_HappyPath_Returns_1_14_Live()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(Paver_1_14_Live);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(14, v.Value.Minor);
        Assert.Equal(0, v.Value.Patch);
        Assert.Equal(0x597d42f8u, v.Value.Build);
        Assert.True(v.Value.IsCompatibleWithParser,
            "1.14.00 should be compatible with the current ParserTargetMinor=14");
        Assert.Equal("1.14.00", v.Value.ShortVersionString);
        Assert.Equal("1.14.00 build 0x597d42f8", v.Value.DisplayString);
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
        // constant. Pin the currently-vendored target (14) and that the
        // target is always a member of the compatible set.
        Assert.Equal(14, GameDataVersion.ParserTargetMinor);
        Assert.Contains<ushort>(14, GameDataVersion.CompatibleMinors);
        Assert.DoesNotContain<ushort>(13, GameDataVersion.CompatibleMinors);
    }

    [Fact]
    public void TryReadFromBytes_PreviousMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 1.14 is content-only over 1.13 (item values changed, iteminfo layout
        // byte-identical), so the 1.13 schema is in fact readable — but
        // CompatibleMinors is kept target-only ({14}) by convention, so a 1.13
        // install is flagged incompatible and warned to update before iteminfo
        // / save-body loading. (The last STRUCTURAL drift was 1.12 → 1.13.)
        var v = NativePaverReader.TryReadFromBytes(Paver_1_13_Prev);
        Assert.NotNull(v);
        Assert.Equal(13, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.13.00 must NOT be compatible — CompatibleMinors is {14}");
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
        // {14} — 1.07 used a different iteminfo layout.
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(7, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.07.xx must NOT be compatible — not in CompatibleMinors {14}");
    }

    [Fact]
    public void TryReadFromBytes_FutureMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 1.15.xx layout: minor = 15 is past the validated set.
        // The gate is an explicit allow-list, not "≥ target", so a future
        // patch this build hasn't been validated against is flagged until
        // CompatibleMinors is extended (Rust-side, via the vendored parser).
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x0f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(15, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.15.xx must NOT be compatible — not yet in CompatibleMinors {14}");
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
        // be 1.14 forever, and this test should survive a future
        // game-data minor bump without being rewritten alongside.
        Assert.Equal(1, v!.Value.Major);
    }
}
