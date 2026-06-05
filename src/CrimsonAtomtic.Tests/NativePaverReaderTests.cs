using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Round-trip tests for the <c>crimson_paver_read_*</c> C ABI through
/// <see cref="NativePaverReader"/>. Synthetic bytes test exercises the
/// happy path + bad-input handling; a live-install pin reads the real
/// <c>meta/0.paver</c> when present and skips otherwise.
/// </summary>
public sealed class NativePaverReaderTests
{
    /// <summary>Bit-for-bit copy of the live 1.10.00 install's paver
    /// (<c>01 00 0a 00 00 00 ac b2 84 cf</c> → build 0xcf84b2ac LE).</summary>
    private static readonly byte[] Paver_1_10_Live =
        [0x01, 0x00, 0x0a, 0x00, 0x00, 0x00, 0xac, 0xb2, 0x84, 0xcf];

    /// <summary>The previous patch's paver (1.09.00) — kept to pin that
    /// it is now flagged INCOMPATIBLE: 1.10 drifted the iteminfo schema
    /// (crimson-rs <c>dd2ed2e</c>), so 1.09 is no longer in
    /// <c>CompatibleMinors</c>.</summary>
    private static readonly byte[] Paver_1_09_Prev =
        [0x01, 0x00, 0x09, 0x00, 0x00, 0x00, 0x24, 0x48, 0xf3, 0xbb];

    [Fact]
    public void TryReadFromBytes_HappyPath_Returns_1_10_Live()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var v = NativePaverReader.TryReadFromBytes(Paver_1_10_Live);
        Assert.NotNull(v);
        Assert.Equal(1, v!.Value.Major);
        Assert.Equal(10, v.Value.Minor);
        Assert.Equal(0, v.Value.Patch);
        Assert.Equal(0xcf84b2acu, v.Value.Build);
        Assert.True(v.Value.IsCompatibleWithParser,
            "1.10.00 should be compatible with the current ParserTargetMinor=10");
        Assert.Equal("1.10.00", v.Value.ShortVersionString);
        Assert.Equal("1.10.00 build 0xcf84b2ac", v.Value.DisplayString);
    }

    [Fact]
    public void TryReadFromBytes_PreviousMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // 1.10 changed the iteminfo layout vs 1.09 (dropped money_icon_path,
        // added UnitData.unk_post_icon_path — crimson-rs dd2ed2e), so unlike
        // the byte-identical 1.08/1.09 pair, 1.09 is NO LONGER compatible
        // with this parser build (CompatibleMinors = {10}). A user still on
        // 1.09 is warned to update before iteminfo / save-body loading.
        var v = NativePaverReader.TryReadFromBytes(Paver_1_09_Prev);
        Assert.NotNull(v);
        Assert.Equal(9, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.09.00 must NOT be compatible — 1.10 drifted the iteminfo schema");
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
        // {10} — 1.07 used a different iteminfo layout.
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(7, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.07.xx must NOT be compatible — not in CompatibleMinors {10}");
    }

    [Fact]
    public void TryReadFromBytes_FutureMinor_FlagsIncompatible()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        // Synthetic 1.11.xx layout: minor = 11 is past the validated set.
        // The gate is an explicit allow-list, not "≥ target", so a future
        // patch this build hasn't been validated against is flagged until
        // CompatibleMinors is extended.
        ReadOnlySpan<byte> bytes =
            [0x01, 0x00, 0x0b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var v = NativePaverReader.TryReadFromBytes(bytes);
        Assert.NotNull(v);
        Assert.Equal(11, v!.Value.Minor);
        Assert.False(v.Value.IsCompatibleWithParser,
            "1.11.xx must NOT be compatible — not yet in CompatibleMinors {10}");
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
        // be 1.10 forever, and this test should survive a future
        // game-data minor bump without being rewritten alongside.
        Assert.Equal(1, v!.Value.Major);
    }
}
